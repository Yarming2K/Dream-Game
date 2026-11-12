using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The world's trees, bushes and rocks (placed by <see cref="Vegetation.ScatterWorld"/> from what covers the ground,
/// drawn with the island's models and shader). They're grown in 128 m patches round you as you go, out to 1.1 km (bushes
/// stop being drawn at 260 m and small rocks at 420 m, as on the island), and dropped behind you. Trunks and rocks are
/// solid within 200 m of you. Vehicles knock trees down, blasts throw them, and a tree whose ground is blown away falls,
/// just as on the island.
/// </summary>
public partial class WorldScatter : Node3D
{
    public static WorldScatter Instance { get; private set; }

    private const float Patch = 128f, Reach = 1150f, SolidWithin = 200f;
    private readonly Dictionary<PlantKind, Mesh> _meshes = new();
    private ShaderMaterial _plantsMat, _rocksMat;
    private WorldMap _map;

    private sealed class PatchData
    {
        public int X, Z;
        public List<Plant> Plants;
        public float[] Y;
        public readonly Dictionary<PlantKind, (MultiMesh mm, List<int> idx)> Batches = new();
        public readonly List<Node> Nodes = new();
        public (MultiMesh mm, int slot)[] SlotOf;
        public StaticBody3D Body;
        public CollisionShape3D[] Shapes;
        public bool Ready, Dead;
    }

    private readonly Dictionary<(int, int), PatchData> _patches = new();
    private readonly ConcurrentQueue<PatchData> _grown = new();
    private int _growing;
    private float _tick, _knockTick;

    public static WorldScatter Create(WorldMap map) => new() { Name = "WorldScatter", _map = map };

    public override void _Ready()
    {
        Instance = this;
        if (!ResourceLoader.Exists(Scatter.FilePath))
        {
            GD.Print("[world scatter] no scenery library imported: no trees");
            return;
        }

        Node lib = ResourceLoader.Load<PackedScene>(Scatter.FilePath)?.Instantiate();
        if (lib == null) return;
        Scatter.Collect(lib, _meshes);
        lib.Free();
        _plantsMat = Scatter.Material(true);
        _rocksMat = Scatter.Material(false);
        Terrain.Changed += OnGroundChanged;
    }

    public override void _ExitTree()
    {
        Terrain.Changed -= OnGroundChanged;
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (_meshes.Count == 0) return;
        _tick += (float)delta;
        if (_tick >= 0.25f)
        {
            _tick = 0;
            Stream(WorldQuery.Viewer);
        }

        for (int n = 0; n < 6 && _grown.TryDequeue(out PatchData p); n++)
            if (!p.Dead) Show(p);
    }

    // ------------------------------------------------------------------ growing patches as you go

    private void Stream(Vector3 eye)
    {
        int r = (int)MathF.Ceiling(Reach / Patch);
        int cx = (int)MathF.Floor(eye.X / Patch), cz = (int)MathF.Floor(eye.Z / Patch);
        var wanted = new List<(int, int, float)>();
        for (int z = cz - r; z <= cz + r; z++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            float d = new Vector2((x + 0.5f) * Patch - eye.X, (z + 0.5f) * Patch - eye.Z).Length();
            if (d < Reach + Patch * 0.7f && !_patches.ContainsKey((x, z))) wanted.Add((x, z, d));
        }

        foreach ((int x, int z, float _) in wanted.OrderBy(w => w.Item3))
        {
            if (_growing >= 4) break;
            var p = new PatchData { X = x, Z = z };
            _patches[(x, z)] = p;
            System.Threading.Interlocked.Increment(ref _growing);
            Task.Run(() =>
            {
                try
                {
                    Grow(p);
                    _grown.Enqueue(p);
                }
                catch (Exception e)
                {
                    GD.PrintErr("[world scatter] " + e.Message);
                }
                finally
                {
                    System.Threading.Interlocked.Decrement(ref _growing);
                }
            });
        }

        // Drop patches well behind, and make the near ones solid.
        foreach (PatchData p in _patches.Values.ToList())
        {
            float d = new Vector2((p.X + 0.5f) * Patch - eye.X, (p.Z + 0.5f) * Patch - eye.Z).Length();
            if (d > Reach + Patch * 1.5f)
            {
                p.Dead = true;
                foreach (Node n in p.Nodes) n.QueueFree();
                p.Body?.QueueFree();
                _patches.Remove((p.X, p.Z));
                continue;
            }

            if (p.Ready) Solid(p, d < SolidWithin + Patch * 0.7f);
        }
    }

    private void Grow(PatchData p)
    {
        p.Plants = Vegetation.ScatterWorld(p.X * Patch, p.Z * Patch, Patch, _map.Seed + 1, (x, z) =>
        {
            float h = _map.Sample(x, z, out GroundInfo info);
            float hx = _map.HeightAt(x + 1.5f, z), hz = _map.HeightAt(x, z + 1.5f);
            var n = new System.Numerics.Vector3(h - hx, 1.5f, h - hz);
            float slope = 1f - 1.5f / n.Length();
            return (h, slope, WorldMap.Cover(info, h, slope));
        });
        p.Y = new float[p.Plants.Count];
        for (int i = 0; i < p.Plants.Count; i++) p.Y[i] = _map.HeightAt(p.Plants[i].X, p.Plants[i].Z);
    }

    private void Show(PatchData p)
    {
        // Ground changed near you since (craters): stand on it as it is now.
        for (int i = 0; i < p.Plants.Count; i++)
        {
            Plant pl = p.Plants[i];
            if (WorldQuery.Ground != null && WorldGround.Instance != null && WorldGround.Instance.Ready(new Vector3(pl.X, 0, pl.Z)))
                p.Y[i] = WorldQuery.Ground.HeightAt(pl.X, pl.Z);
        }

        p.SlotOf = new (MultiMesh, int)[p.Plants.Count];
        p.Shapes = new CollisionShape3D[p.Plants.Count];
        foreach (IGrouping<PlantKind, int> g in Enumerable.Range(0, p.Plants.Count).GroupBy(i => p.Plants[i].Kind))
        {
            if (!_meshes.TryGetValue(g.Key, out Mesh mesh)) continue;
            List<int> list = g.ToList();
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = list.Count,
            };
            for (int s = 0; s < list.Count; s++)
            {
                int i = list[s];
                mm.SetInstanceTransform(s, Scatter.Placement(p.Plants[i], p.Y[i]));
                uint h = (uint)((p.X * 92821 + p.Z * 68917 + i) * 2654435761u);
                mm.SetInstanceCustomData(s, new Color((h & 255) / 255f, ((h >> 8) & 255) / 255f, 0, 0));
                p.SlotOf[i] = (mm, s);
            }

            bool rock = g.Key is PlantKind.Rock or PlantKind.Boulder;
            bool small = g.Key is PlantKind.Bush or PlantKind.Rock;
            var node = new MultiMeshInstance3D
            {
                Multimesh = mm, MaterialOverride = rock ? _rocksMat : _plantsMat,
                CastShadow = small ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
                VisibilityRangeEnd = g.Key switch { PlantKind.Bush => 260f, PlantKind.Rock => 420f, _ => 1100f },
            };
            AddChild(node);
            p.Nodes.Add(node);
            p.Batches[g.Key] = (mm, list);
        }

        p.Ready = true;
    }

    /// <summary>Trunks and rocks solid (near you), or not (further off, where nothing of yours bumps into them).</summary>
    private void Solid(PatchData p, bool on)
    {
        if (on == (p.Body != null)) return;
        if (!on)
        {
            p.Body.QueueFree();
            p.Body = null;
            Array.Clear(p.Shapes);
            return;
        }

        p.Body = new StaticBody3D { CollisionLayer = GridRegistry.Layers.World, CollisionMask = 0 };
        AddChild(p.Body);
        for (int i = 0; i < p.Plants.Count; i++)
        {
            Plant pl = p.Plants[i];
            if (pl.Kind == PlantKind.Bush || pl.Down) continue;
            Shape3D shape = pl.IsTree ? new CylinderShape3D { Radius = pl.RadiusM, Height = 5f } : new SphereShape3D { Radius = pl.RadiusM * 0.75f };
            var cs = new CollisionShape3D { Shape = shape, Position = new Vector3(pl.X, p.Y[i] + (pl.IsTree ? 2.5f : pl.RadiusM * 0.2f), pl.Z) };
            p.Body.AddChild(cs);
            p.Shapes[i] = cs;
        }
    }

    // ------------------------------------------------------------------ what knocks them down

    private void Fell(PatchData p, int i, Vector3 toward)
    {
        Plant pl = p.Plants[i];
        if (pl.Down || pl.IsRock) return;
        pl.Down = true;
        if (toward.LengthSquared() > 1e-4f) pl.FellToward = MathF.Atan2(toward.X, toward.Z);
        p.Plants[i] = pl;
        Refresh(p, i);
    }

    private static void Refresh(PatchData p, int i)
    {
        (MultiMesh mm, int slot) = p.SlotOf[i];
        mm?.SetInstanceTransform(slot, Scatter.Placement(p.Plants[i], p.Y[i]));
        if (p.Plants[i].Down && p.Shapes?[i] != null && IsInstanceValid(p.Shapes[i]))
        {
            p.Shapes[i].QueueFree();
            p.Shapes[i] = null;
        }
    }

    private IEnumerable<PatchData> Near(Vector3 at, float reach)
    {
        for (int z = (int)MathF.Floor((at.Z - reach) / Patch); z <= (int)MathF.Floor((at.Z + reach) / Patch); z++)
        for (int x = (int)MathF.Floor((at.X - reach) / Patch); x <= (int)MathF.Floor((at.X + reach) / Patch); x++)
            if (_patches.TryGetValue((x, z), out PatchData p) && p.Ready)
                yield return p;
    }

    public override void _PhysicsProcess(double delta)
    {
        _knockTick += (float)delta;
        if (_knockTick < 0.1f) return;
        _knockTick = 0;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || !IsInstanceValid(v) || v.IsNaval) continue;
            Vector3 vel = v.LinearVelocity;
            float speed = vel.Length();
            if (speed < 0.8f) continue;
            if (v.IsAir && v.GlobalPosition.Y - (WorldQuery.Ground?.HeightAt(v.GlobalPosition.X, v.GlobalPosition.Z) ?? 0f) > 12f) continue;
            Rect2 plan = v.Plan;
            float reach = Mathf.Max(plan.Size.X, plan.Size.Y) * 0.5f + 2f;
            Transform3D inv = v.GlobalTransform.AffineInverse();
            Vector3 c = v.GlobalPosition;
            foreach (PatchData p in Near(c, reach))
                for (int i = 0; i < p.Plants.Count; i++)
                {
                    Plant pl = p.Plants[i];
                    if (pl.Down || pl.IsRock || MathF.Abs(pl.X - c.X) > reach + 3f || MathF.Abs(pl.Z - c.Z) > reach + 3f) continue;
                    Vector3 local = inv * new Vector3(pl.X, c.Y, pl.Z);
                    float r = pl.RadiusM + 0.3f;
                    if (local.X < plan.Position.X - r || local.X > plan.End.X + r || local.Z < plan.Position.Y - r || local.Z > plan.End.Y + r) continue;
                    if (Vegetation.Knocks(pl, v.Mass, speed)) Fell(p, i, vel);
                }
        }
    }

    /// <summary>A blast: trees in reach fall away from it, bushes go flat.</summary>
    public void Blast(Vector3 center, float radius, float damage)
    {
        if (WorldQuery.Ground == null || center.Y - WorldQuery.Ground.HeightAt(center.X, center.Z) > radius) return;
        foreach (PatchData p in Near(center, radius * 1.7f + 2f))
        {
            var near = new List<int>();
            for (int i = 0; i < p.Plants.Count; i++)
                if (MathF.Abs(p.Plants[i].X - center.X) < radius * 1.7f + 2f && MathF.Abs(p.Plants[i].Z - center.Z) < radius * 1.7f + 2f)
                    near.Add(i);
            if (near.Count == 0) continue;
            var subset = near.Select(i => p.Plants[i]).ToList();
            foreach (int k in Vegetation.Blast(subset, new System.Numerics.Vector2(center.X, center.Z), radius, damage))
            {
                p.Plants[near[k]] = subset[k];
                Refresh(p, near[k]);
            }
        }
    }

    /// <summary>Ground blown away or dug from under a tree topples it; anything else follows the ground.</summary>
    private void OnGroundChanged(Rect2 rect)
    {
        Vector2 c = rect.GetCenter();
        foreach (PatchData p in Near(new Vector3(c.X, 0, c.Y), rect.Size.Length() * 0.5f + 2f))
            for (int i = 0; i < p.Plants.Count; i++)
            {
                Plant pl = p.Plants[i];
                if (!rect.Grow(1f).HasPoint(new Vector2(pl.X, pl.Z))) continue;
                float now = WorldQuery.Ground.HeightAt(pl.X, pl.Z);
                float was = p.Y[i];
                p.Y[i] = now;
                if (!pl.Down && !pl.IsRock && was - now > 0.6f) Fell(p, i, new Vector3(MathF.Cos(i), 0, MathF.Sin(i)));
                else Refresh(p, i);
            }
    }
}
