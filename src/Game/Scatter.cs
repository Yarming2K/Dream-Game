using System;
using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The island's trees, bushes and rocks (placed by <see cref="Vegetation"/>, modelled in Blender:
/// models/env.glb, built by tools/blender/env_library.py). Drawn in batches, one per kind per 64 m patch of ground
/// so far patches drop out; trunks and rocks are solid. A heavy vehicle driving into a tree knocks it flat, a
/// light one bumps off it; bushes go flat under anything; blasts throw trees down away from them; a tree whose
/// ground is blown away falls over. Leaves sway in the wind.
/// </summary>
public partial class Scatter : Node3D
{
    public static Scatter Instance { get; private set; }

    public const string FilePath = "res://models/env.glb";
    private const float Patch = 64f;

    private List<Plant> _plants;
    private readonly Dictionary<PlantKind, Mesh> _meshes = new();
    private readonly Dictionary<(int px, int pz, PlantKind kind), (MultiMesh mm, List<int> plants)> _batches = new();
    private (MultiMesh mm, int slot)[] _slotOf;
    private CollisionShape3D[] _shapes;
    private readonly Dictionary<(int, int), List<int>> _grid = new();
    private float _tick;

    private const string VegetationCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform bool leaves = true;
uniform sampler2D clumps : filter_linear_mipmap, repeat_enable;
uniform sampler2D grain : filter_linear_mipmap, repeat_enable;

varying vec3 wpos;
varying vec2 tint;

void vertex() {
	tint = INSTANCE_CUSTOM.xy;
	// Sway: each plant at its own pace, the tips most.
	vec3 base = MODEL_MATRIX[3].xyz;
	float phase = base.x * 0.31 + base.z * 0.17 + INSTANCE_CUSTOM.y * 6.28;
	float sway = COLOR.a * (sin(TIME * 1.3 + phase) * 0.6 + sin(TIME * 2.9 + phase * 1.7) * 0.25);
	VERTEX.x += sway * 0.18;
	VERTEX.z += sway * 0.1;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 col = COLOR.rgb * (0.85 + 0.3 * tint.x);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	if (UV.x >= 0.0) {
		// A leaf card: clusters of leaves cut out of it, thinning toward its edges.
		vec2 c = UV - 0.5;
		float edge = 1.0 - length(c) * 1.9;
		float n = texture(clumps, UV * 1.3 + tint.yy).r;
		// The cellular noise is low near each cell's middle: those are the leaf clusters.
		ALPHA = smoothstep(0.32, 0.36, (1.0 - n) * 0.6 + edge * 0.55);
		col *= 0.75 + 0.5 * texture(grain, UV * 3.0).r;
		BACKLIGHT = col * 0.5;
		ROUGHNESS = 0.8;
	} else if (!leaves) {
		// Rock: grain and lichen on top of its colour.
		float g = texture(grain, wpos.xz * 0.6 + wpos.yy * 0.3).r;
		col *= 0.75 + 0.45 * g;
		ROUGHNESS = 0.9;
	} else {
		// Bark: ridges running up the trunk.
		float g = texture(grain, vec2(atan(wpos.x - MODEL_MATRIX[3].x, wpos.z - MODEL_MATRIX[3].z) * 2.0, wpos.y * 0.15)).r;
		col *= 0.7 + 0.5 * g;
		ROUGHNESS = 0.95;
	}
	ALBEDO = col;
}
";

    public override void _Ready()
    {
        Instance = this;
        if (!ResourceLoader.Exists(FilePath))
        {
            GD.Print("[scatter] no scenery library imported: no trees");
            return;
        }

        Node lib = ResourceLoader.Load<PackedScene>(FilePath)?.Instantiate();
        if (lib == null) return;
        Collect(lib);
        lib.Free();
        if (_meshes.Count == 0 || WorldQuery.Ground == null) return;

        _plants = Vegetation.Scatter(WorldQuery.Ground, 1, Terrain.LandHeight, KeepClear);
        _slotOf = new (MultiMesh, int)[_plants.Count];
        _shapes = new CollisionShape3D[_plants.Count];
        Build();
        Terrain.Changed += OnGroundChanged;
        GD.Print($"[scatter] {_plants.Count} trees, bushes and rocks");
    }

    public override void _ExitTree()
    {
        Terrain.Changed -= OnGroundChanged;
        if (Instance == this) Instance = null;
    }

    private void Collect(Node n)
    {
        if (n is MeshInstance3D { Mesh: not null } mi && Enum.TryParse(mi.Name.ToString(), true, out PlantKind k)) _meshes[k] = mi.Mesh;
        foreach (Node c in n.GetChildren()) Collect(c);
    }

    /// <summary>Where nothing grows: the firing lane and everything on it, the airstrip, the helipad, the building pads.</summary>
    private static bool KeepClear(float x, float z)
    {
        if (MathF.Abs(x) < Terrain.LaneHalfWidth + 8f) return true;
        if (MathF.Abs(x - Terrain.StripX) < Terrain.StripHalfWidth + 15f && MathF.Abs(z) < Terrain.StripHalfLength + 25f) return true;
        if (new Vector2(x, z).DistanceTo(Terrain.Helipad) < Terrain.HelipadRadius + 15f) return true;
        foreach (Vector2 p in new[] { Terrain.CitadelPad, Terrain.MortarPad, Terrain.FlakTowerPad })
            if (new Vector2(x, z).DistanceTo(p) < Terrain.PadRadius + 14f) return true;
        return false;
    }

    // ------------------------------------------------------------------ drawing

    private ShaderMaterial Material(bool leaves)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = VegetationCode } };
        m.SetShaderParameter("leaves", leaves);
        m.SetShaderParameter("clumps", new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.045f, Seed = 5,
                CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance, CellularDistanceFunction = FastNoiseLite.CellularDistanceFunctionEnum.Euclidean },
        });
        m.SetShaderParameter("grain", BlockVisuals.GritTexture);
        return m;
    }

    private void Build()
    {
        ShaderMaterial plants = Material(true), rocks = Material(false);
        var groups = new Dictionary<(int, int, PlantKind), List<int>>();
        for (int i = 0; i < _plants.Count; i++)
        {
            Plant p = _plants[i];
            if (!_meshes.ContainsKey(p.Kind)) continue;
            var key = ((int)MathF.Floor(p.X / Patch), (int)MathF.Floor(p.Z / Patch), p.Kind);
            if (!groups.TryGetValue(key, out List<int> list)) groups[key] = list = new List<int>();
            list.Add(i);
            var cell = ((int)MathF.Floor(p.X / 16f), (int)MathF.Floor(p.Z / 16f));
            if (!_grid.TryGetValue(cell, out List<int> g)) _grid[cell] = g = new List<int>();
            g.Add(i);
        }

        var bodies = new Dictionary<(int, int), StaticBody3D>();
        foreach (((int px, int pz, PlantKind kind) key, List<int> list) in groups)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = _meshes[key.kind], InstanceCount = list.Count,
            };
            for (int s = 0; s < list.Count; s++)
            {
                int i = list[s];
                mm.SetInstanceTransform(s, Placement(_plants[i]));
                uint h = (uint)(i * 2654435761u);
                mm.SetInstanceCustomData(s, new Color((h & 255) / 255f, ((h >> 8) & 255) / 255f, 0, 0));
                _slotOf[i] = (mm, s);
            }

            bool rock = key.kind is PlantKind.Rock or PlantKind.Boulder;
            bool small = key.kind is PlantKind.Bush or PlantKind.Rock;
            AddChild(new MultiMeshInstance3D
            {
                Multimesh = mm, MaterialOverride = rock ? rocks : plants,
                CastShadow = small ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
                VisibilityRangeEnd = key.kind switch { PlantKind.Bush => 260f, PlantKind.Rock => 420f, _ => 1100f },
            });
            _batches[key] = (mm, list);

            // Trunks and rocks are solid; bushes aren't.
            if (key.kind == PlantKind.Bush) continue;
            if (!bodies.TryGetValue((key.px, key.pz), out StaticBody3D body))
            {
                body = new StaticBody3D { Name = $"Scatter{key.px}_{key.pz}", CollisionLayer = GridRegistry.Layers.World, CollisionMask = 0 };
                AddChild(body);
                bodies[(key.px, key.pz)] = body;
            }

            foreach (int i in list)
            {
                Plant p = _plants[i];
                float y = WorldQuery.Ground.HeightAt(p.X, p.Z);
                Shape3D shape = p.IsTree
                    ? new CylinderShape3D { Radius = p.RadiusM, Height = 5f }
                    : new SphereShape3D { Radius = p.RadiusM * 0.75f };
                var cs = new CollisionShape3D { Shape = shape, Position = new Vector3(p.X, y + (p.IsTree ? 2.5f : p.RadiusM * 0.2f), p.Z) };
                body.AddChild(cs);
                _shapes[i] = cs;
            }
        }
    }

    /// <summary>Standing, or lying where it fell.</summary>
    private static Transform3D Placement(in Plant p)
    {
        float y = WorldQuery.Ground.HeightAt(p.X, p.Z);
        var basis = new Basis(Vector3.Up, p.Yaw).Scaled(Vector3.One * p.Scale);
        if (p.Down)
        {
            if (p.Kind == PlantKind.Bush) basis = basis.Scaled(new Vector3(1.15f, 0.25f, 1.15f));
            else
            {
                var toward = new Vector3(MathF.Sin(p.FellToward), 0, MathF.Cos(p.FellToward));
                Vector3 axis = Vector3.Up.Cross(toward).Normalized();
                basis = new Basis(axis, 1.45f) * basis;
                y += 0.25f * p.Scale;
            }
        }

        return new Transform3D(basis, new Vector3(p.X, y - 0.05f, p.Z));
    }

    private void Fell(int i, Vector3 toward)
    {
        Plant p = _plants[i];
        if (p.Down || p.IsRock) return;
        p.Down = true;
        if (toward.LengthSquared() > 1e-4f) p.FellToward = MathF.Atan2(toward.X, toward.Z);
        _plants[i] = p;
        Refresh(i);
    }

    private void Refresh(int i)
    {
        (MultiMesh mm, int slot) = _slotOf[i];
        mm?.SetInstanceTransform(slot, Placement(_plants[i]));
        if (_plants[i].Down && _shapes[i] != null && IsInstanceValid(_shapes[i]))
        {
            _shapes[i].QueueFree();
            _shapes[i] = null;
        }
    }

    // ------------------------------------------------------------------ what knocks them down

    public override void _PhysicsProcess(double delta)
    {
        if (_plants == null) return;
        _tick += (float)delta;
        if (_tick < 0.1f) return;
        _tick = 0;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || !IsInstanceValid(v) || v.IsNaval) continue;
            Vector3 vel = v.LinearVelocity;
            float speed = vel.Length();
            if (speed < 0.8f) continue;
            if (v.IsAir && v.GlobalPosition.Y - WorldQuery.Ground.HeightAt(v.GlobalPosition.X, v.GlobalPosition.Z) > 12f) continue;
            Rect2 plan = v.Plan;
            float reach = Mathf.Max(plan.Size.X, plan.Size.Y) * 0.5f + 2f;
            Transform3D inv = v.GlobalTransform.AffineInverse();
            foreach (int i in Near(v.GlobalPosition, reach))
            {
                Plant p = _plants[i];
                if (p.Down || p.IsRock) continue;
                Vector3 local = inv * new Vector3(p.X, v.GlobalPosition.Y, p.Z);
                float r = p.RadiusM + 0.3f;
                if (local.X < plan.Position.X - r || local.X > plan.End.X + r || local.Z < plan.Position.Y - r || local.Z > plan.End.Y + r) continue;
                if (Vegetation.Knocks(p, v.Mass, speed)) Fell(i, vel);
            }
        }
    }

    private IEnumerable<int> Near(Vector3 at, float reach)
    {
        int x0 = (int)MathF.Floor((at.X - reach) / 16f), x1 = (int)MathF.Floor((at.X + reach) / 16f);
        int z0 = (int)MathF.Floor((at.Z - reach) / 16f), z1 = (int)MathF.Floor((at.Z + reach) / 16f);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (_grid.TryGetValue((x, z), out List<int> list))
                foreach (int i in list) yield return i;
    }

    /// <summary>A blast: trees in reach fall away from it, bushes go flat.</summary>
    public void Blast(Vector3 center, float radius, float damage)
    {
        if (_plants == null || center.Y - WorldQuery.Ground.HeightAt(center.X, center.Z) > radius) return;
        var nearby = new List<int>(Near(center, radius * 1.7f + 2f));
        if (nearby.Count == 0) return;
        var subset = new List<Plant>(nearby.Count);
        foreach (int i in nearby) subset.Add(_plants[i]);
        foreach (int k in Vegetation.Blast(subset, new System.Numerics.Vector2(center.X, center.Z), radius, damage))
        {
            _plants[nearby[k]] = subset[k];
            Refresh(nearby[k]);
        }
    }

    /// <summary>Ground blown away or dug from under a tree topples it; anything else follows the ground.</summary>
    private void OnGroundChanged(Rect2 rect)
    {
        if (_plants == null) return;
        Vector2 c = rect.GetCenter();
        foreach (int i in Near(new Vector3(c.X, 0, c.Y), rect.Size.Length() * 0.5f + 2f))
        {
            Plant p = _plants[i];
            if (!rect.Grow(1f).HasPoint(new Vector2(p.X, p.Z))) continue;
            (MultiMesh mm, int slot) = _slotOf[i];
            if (mm == null) continue;
            float was = mm.GetInstanceTransform(slot).Origin.Y + 0.05f;
            float now = WorldQuery.Ground.HeightAt(p.X, p.Z);
            if (!p.Down && !p.IsRock && was - now > 0.6f) Fell(i, new Vector3(MathF.Cos(i), 0, MathF.Sin(i)));
            else Refresh(i);
        }
    }
}
