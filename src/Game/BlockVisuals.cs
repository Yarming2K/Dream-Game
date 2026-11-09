using System;
using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>Meshes, materials and collision shapes for blocks. Everything is cached and shared.</summary>
public static class BlockVisuals
{
    private const float H = Conv.CS * 0.5f;

    private static readonly Dictionary<string, StandardMaterial3D> Materials = new();
    private static readonly Dictionary<string, Mesh> Meshes = new();
    private static NoiseTexture2D _grit, _bumps;
    private static BoxShape3D _box;
    private static ConvexPolygonShape3D _wedge;
    private static ArrayMesh _wedgeMesh;

    public static readonly Material GhostOk = Shaders.Hologram(new Color(0.35f, 1f, 0.5f));
    public static readonly Material GhostBad = Shaders.Hologram(new Color(1f, 0.32f, 0.25f));
    public static readonly StandardMaterial3D LooseOverlay = Overlay(new Color(1f, 0.15f, 0.1f, 0.45f));

    private static StandardMaterial3D Overlay(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = c,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A soft noise texture that gives metal surfaces some grime instead of flat color.</summary>
    private static NoiseTexture2D Grit()
    {
        if (_grit != null) return _grit;
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.72f, 0.72f, 0.72f));
        ramp.SetColor(1, new Color(1f, 1f, 1f));
        _grit = new NoiseTexture2D
        {
            Width = 256,
            Height = 256,
            Seamless = true,
            ColorRamp = ramp,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.06f, FractalOctaves = 3 },
        };
        return _grit;
    }

    /// <summary>Fine dents and hammer marks so plates catch the light unevenly.</summary>
    private static NoiseTexture2D Bumps()
    {
        if (_bumps != null) return _bumps;
        _bumps = new NoiseTexture2D
        {
            Width = 256,
            Height = 256,
            Seamless = true,
            AsNormalMap = true,
            BumpStrength = 3f,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.035f, FractalOctaves = 4 },
        };
        return _bumps;
    }

    public static StandardMaterial3D Material(string key, Color color, float metallic, float roughness, bool twoSided = true)
    {
        if (Materials.TryGetValue(key, out StandardMaterial3D m)) return m;
        m = new StandardMaterial3D
        {
            AlbedoColor = color,
            AlbedoTexture = Grit(),
            NormalEnabled = true,
            NormalTexture = Bumps(),
            NormalScale = 0.35f,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(1.6f, 1.6f, 1.6f),
            Metallic = metallic,
            Roughness = roughness,
            // Drawn from both sides so procedural meshes look right whichever way their triangles wind.
            CullMode = twoSided ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
        };
        Materials[key] = m;
        return m;
    }

    public static StandardMaterial3D MaterialOf(BlockDef def) =>
        Material("def:" + def.Id, new Color(def.ColorR, def.ColorG, def.ColorB), def.Metallic, def.Roughness,
            def.Shape == BlockShape.Wedge);

    private static StandardMaterial3D Dark => Material("dark", new Color(0.13f, 0.13f, 0.13f), 0.8f, 0.4f);
    private static StandardMaterial3D Rubber => Material("rubber", new Color(0.06f, 0.06f, 0.06f), 0f, 0.95f);
    private static StandardMaterial3D Steel => Material("steel", new Color(0.55f, 0.56f, 0.58f), 0.9f, 0.3f);
    private static StandardMaterial3D Brass => Material("brass", new Color(0.78f, 0.6f, 0.25f), 0.9f, 0.3f);
    private static StandardMaterial3D RedTip => Material("redtip", new Color(0.75f, 0.12f, 0.08f), 0.4f, 0.5f);

    private static StandardMaterial3D Glow(Color c, float energy)
    {
        string key = $"glow:{c.ToHtml()}:{energy}";
        if (Materials.TryGetValue(key, out StandardMaterial3D m)) return m;
        m = new StandardMaterial3D
        {
            AlbedoColor = c,
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = energy,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        Materials[key] = m;
        return m;
    }

    public static Shape3D ShapeOf(BlockDef def)
    {
        if (def.Shape == BlockShape.Wedge)
        {
            _wedge ??= new ConvexPolygonShape3D { Points = WedgePoints() };
            return _wedge;
        }

        _box ??= new BoxShape3D { Size = new Vector3(Conv.CS, Conv.CS, Conv.CS) };
        return _box;
    }

    private static Vector3[] WedgePoints() => new[]
    {
        new Vector3(-H, -H, -H), new Vector3(H, -H, -H), new Vector3(H, -H, H), new Vector3(-H, -H, H),
        new Vector3(-H, H, H), new Vector3(H, H, H),
    };

    /// <summary>Half a cube: the solid part is where local y &lt;= z, matching the core rules.</summary>
    private static ArrayMesh WedgeMesh()
    {
        if (_wedgeMesh != null) return _wedgeMesh;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 a = new(-H, -H, -H), b = new(H, -H, -H), c = new(H, -H, H), d = new(-H, -H, H);
        Vector3 e = new(-H, H, H), f = new(H, H, H);

        void Tri(Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            st.SetNormal(n);
            st.AddVertex(p0);
            st.SetNormal(n);
            st.AddVertex(p1);
            st.SetNormal(n);
            st.AddVertex(p2);
        }

        void Quad(Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            Tri(n, p0, p1, p2);
            Tri(n, p0, p2, p3);
        }

        Quad(Vector3.Down, a, b, c, d);
        Quad(Vector3.Back, d, c, f, e);
        Quad(new Vector3(0, 1, -1).Normalized(), a, e, f, b);
        Tri(Vector3.Left, a, d, e);
        Tri(Vector3.Right, b, f, c);
        _wedgeMesh = st.Commit();
        return _wedgeMesh;
    }

    /// <summary>
    /// A box with chamfered edges. The thin bevels catch highlights, which is what makes plates read as
    /// solid metal instead of flat colored cubes.
    /// </summary>
    private static Mesh Bevel(Vector3 size, float bevel = 0.022f)
    {
        string key = $"bevel:{size}:{bevel}";
        if (Meshes.TryGetValue(key, out Mesh cached)) return cached;
        Vector3 h = size * 0.5f;
        float b = Mathf.Min(bevel, Mathf.Min(h.X, Mathf.Min(h.Y, h.Z)) * 0.5f);
        Vector3 i = h - new Vector3(b, b, b);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        void Tri(Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            // Clockwise when seen from outside, which is what Godot treats as the front face.
            if ((p1 - p0).Cross(p2 - p0).Dot(n) > 0) (p1, p2) = (p2, p1);
            st.SetNormal(n);
            st.AddVertex(p0);
            st.SetNormal(n);
            st.AddVertex(p1);
            st.SetNormal(n);
            st.AddVertex(p2);
        }

        void Quad(Vector3 n, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d)
        {
            Tri(n, a, c1, c2);
            Tri(n, a, c2, d);
        }

        // Six faces, inset by the bevel.
        for (int axis = 0; axis < 3; axis++)
        for (int sign = -1; sign <= 1; sign += 2)
        {
            Vector3 n = Vector3.Zero;
            n[axis] = sign;
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            Vector3 Corner(float su, float sv)
            {
                Vector3 p = Vector3.Zero;
                p[axis] = sign * h[axis];
                p[u] = su * i[u];
                p[v] = sv * i[v];
                return p;
            }

            Quad(n, Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1));
        }

        // Twelve edge chamfers.
        for (int k = 0; k < 3; k++)
        {
            int a = (k + 1) % 3, c = (k + 2) % 3;
            for (int sa = -1; sa <= 1; sa += 2)
            for (int sc = -1; sc <= 1; sc += 2)
            {
                Vector3 n = Vector3.Zero;
                n[a] = sa;
                n[c] = sc;
                n = n.Normalized();
                Vector3 P(float along, bool onA)
                {
                    Vector3 p = Vector3.Zero;
                    p[k] = along * i[k];
                    p[a] = sa * (onA ? h[a] : i[a]);
                    p[c] = sc * (onA ? i[c] : h[c]);
                    return p;
                }

                Quad(n, P(-1, true), P(1, true), P(1, false), P(-1, false));
            }
        }

        // Eight corner triangles.
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            var n = new Vector3(sx, sy, sz).Normalized();
            Tri(n, new Vector3(sx * h.X, sy * i.Y, sz * i.Z), new Vector3(sx * i.X, sy * h.Y, sz * i.Z),
                new Vector3(sx * i.X, sy * i.Y, sz * h.Z));
        }

        Mesh mesh = st.Commit();
        Meshes[key] = mesh;
        return mesh;
    }

    private static Mesh Box(Vector3 size)
    {
        string key = $"box:{size}";
        if (!Meshes.TryGetValue(key, out Mesh m)) Meshes[key] = m = new BoxMesh { Size = size };
        return m;
    }

    private static Mesh Cylinder(float radius, float height, float topRadius = -1)
    {
        string key = $"cyl:{radius}:{height}:{topRadius}";
        if (!Meshes.TryGetValue(key, out Mesh m))
            Meshes[key] = m = new CylinderMesh
            {
                BottomRadius = radius, TopRadius = topRadius < 0 ? radius : topRadius, Height = height, RadialSegments = 20,
            };
        return m;
    }

    public static float BarrelRadius(float caliberMm) => Math.Clamp(caliberMm / 1000f * 0.85f, 0.03f, 0.12f);

    private static readonly Basis AlongX = new(Vector3.Back, Mathf.Pi / 2);
    private static readonly Basis AlongZ = new(Vector3.Right, Mathf.Pi / 2);

    private static void Add(Node3D parent, Mesh mesh, Material mat, Transform3D xf)
    {
        parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Transform = xf });
    }

    private static void Add(Node3D parent, Mesh mesh, Material mat, Vector3 pos) =>
        Add(parent, mesh, mat, new Transform3D(Basis.Identity, pos));

    /// <summary>
    /// Builds the look of one block in its own local frame (centered, unrotated).
    /// <paramref name="caliberMm"/> sizes barrels; <paramref name="hints"/> adds builder-only markers like the gun's facing arrow.
    /// </summary>
    public static Node3D Create(BlockDef def, float caliberMm = 76f, bool hints = false)
    {
        var root = new Node3D { Name = def.Id };
        StandardMaterial3D mat = MaterialOf(def);
        float full = Conv.CS;
        switch (def.Kind)
        {
            case BlockKind.Armor when def.Shape == BlockShape.Wedge:
                Add(root, WedgeMesh(), mat, Vector3.Zero);
                break;
            case BlockKind.Armor:
            case BlockKind.Frame:
                Add(root, Bevel(new Vector3(full, full, full)), mat, Vector3.Zero);
                break;
            case BlockKind.Engine:
                Add(root, Bevel(new Vector3(full, full, full)), mat, Vector3.Zero);
                Add(root, Cylinder(0.045f, 0.16f), Dark, new Vector3(0.12f, 0.3f, 0.12f));
                Add(root, Cylinder(0.045f, 0.16f), Dark, new Vector3(-0.12f, 0.3f, 0.12f));
                Add(root, Box(new Vector3(0.36f, 0.04f, 0.2f)), Steel, new Vector3(0, 0.26f, -0.1f));
                break;
            case BlockKind.FuelTank:
                Add(root, Cylinder(0.23f, full * 0.98f), mat, new Transform3D(AlongZ, Vector3.Zero));
                Add(root, Box(new Vector3(full, 0.06f, full)), Dark, new Vector3(0, -0.22f, 0));
                break;
            case BlockKind.Wheel:
            case BlockKind.SteerWheel:
                Add(root, Cylinder(def.WheelRadiusM, 0.28f), mat, new Transform3D(AlongX, Vector3.Zero));
                Add(root, Cylinder(def.WheelRadiusM * 0.45f, 0.3f),
                    def.Kind == BlockKind.SteerWheel ? Brass : Steel, new Transform3D(AlongX, Vector3.Zero));
                break;
            case BlockKind.Track:
                Add(root, Cylinder(def.WheelRadiusM * 0.85f, 0.32f), Dark, new Transform3D(AlongX, Vector3.Zero));
                Add(root, Box(new Vector3(0.42f, 0.07f, full)), mat, new Vector3(0, -def.WheelRadiusM, 0));
                Add(root, Box(new Vector3(0.42f, 0.07f, full)), mat, new Vector3(0, def.WheelRadiusM, 0));
                break;
            case BlockKind.DriverSeat:
                Add(root, Bevel(new Vector3(full, full, full)), mat, Vector3.Zero);
                Add(root, Box(new Vector3(0.3f, 0.07f, 0.04f)), Glow(new Color(0.4f, 0.85f, 1f), 1.5f), new Vector3(0, 0.15f, -0.25f));
                break;
            case BlockKind.GunnerSeat:
                Add(root, Bevel(new Vector3(full, full, full)), mat, Vector3.Zero);
                Add(root, Box(new Vector3(0.12f, 0.1f, 0.06f)), Dark, new Vector3(0.08f, 0.16f, -0.25f));
                Add(root, Box(new Vector3(0.05f, 0.04f, 0.02f)), Glow(new Color(1f, 0.35f, 0.25f), 2f), new Vector3(0.08f, 0.16f, -0.285f));
                break;
            case BlockKind.CommanderSeat:
                Add(root, Bevel(new Vector3(full, full, full)), mat, Vector3.Zero);
                Add(root, Cylinder(0.07f, 0.16f), Dark, new Vector3(0, 0.32f, 0));
                Add(root, Box(new Vector3(0.12f, 0.04f, 0.03f)), Glow(new Color(0.4f, 0.85f, 1f), 1.5f), new Vector3(0, 0.36f, -0.07f));
                break;
            case BlockKind.TurretRing:
                Add(root, Cylinder(0.25f, full), mat, Vector3.Zero);
                Add(root, Cylinder(0.2f, 0.04f), Dark, new Vector3(0, 0.25f, 0));
                break;
            case BlockKind.Breech:
                Add(root, Bevel(new Vector3(full, full, full)), mat, Vector3.Zero);
                Add(root, Cylinder(BarrelRadius(def.Breech.CaliberMm) * 1.4f, 0.12f), Steel,
                    new Transform3D(AlongZ, new Vector3(0, 0, -0.27f)));
                break;
            case BlockKind.Barrel:
                Add(root, Cylinder(BarrelRadius(caliberMm), full * 1.01f), mat, new Transform3D(AlongZ, Vector3.Zero));
                break;
            case BlockKind.MuzzleBrake:
                Add(root, Cylinder(BarrelRadius(caliberMm) * 1.7f, 0.3f), mat, new Transform3D(AlongZ, Vector3.Zero));
                Add(root, Box(new Vector3(BarrelRadius(caliberMm) * 4.2f, 0.06f, 0.1f)), Dark, Vector3.Zero);
                Add(root, Cylinder(BarrelRadius(caliberMm), full), mat, new Transform3D(AlongZ, Vector3.Zero));
                break;
            case BlockKind.Loader:
                Add(root, Bevel(new Vector3(full, full, full)), mat, Vector3.Zero);
                Add(root, Box(new Vector3(full * 1.01f, 0.06f, 0.3f)), Steel, Vector3.Zero);
                break;
            case BlockKind.Propellant:
                Add(root, Bevel(new Vector3(full, full * 0.6f, full)), Dark, new Vector3(0, -0.1f, 0));
                for (int i = 0; i < 4; i++)
                    Add(root, Cylinder(0.08f, 0.36f), mat, new Vector3(i % 2 == 0 ? -0.11f : 0.11f, 0.05f, i < 2 ? -0.11f : 0.11f));
                break;
            case BlockKind.AmmoRack:
                Add(root, Bevel(new Vector3(full, full * 0.7f, full)), mat, new Vector3(0, -0.075f, 0));
                for (int i = 0; i < 4; i++)
                {
                    var p = new Vector3(i % 2 == 0 ? -0.11f : 0.11f, 0.2f, i < 2 ? -0.11f : 0.11f);
                    Add(root, Cylinder(0.07f, 0.1f, 0.02f), def.Ammo == AmmoType.HE ? RedTip : Brass, p);
                }

                break;
            case BlockKind.AmmoFeed:
                Add(root, Bevel(new Vector3(0.3f, 0.3f, 0.3f), 0.03f), mat, Vector3.Zero);
                Add(root, Box(new Vector3(full, 0.12f, 0.12f)), Dark, Vector3.Zero);
                Add(root, Box(new Vector3(0.12f, full, 0.12f)), Dark, Vector3.Zero);
                Add(root, Box(new Vector3(0.12f, 0.12f, full)), Dark, Vector3.Zero);
                break;
            default:
                Add(root, Bevel(new Vector3(full, full, full)), mat, Vector3.Zero);
                break;
        }

        if (hints && def.Kind == BlockKind.Breech)
        {
            // Arrow showing which way the gun fires (where the barrels go).
            Add(root, Cylinder(0.09f, 0.2f, 0f), Glow(new Color(1f, 0.85f, 0.2f), 2f),
                new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), new Vector3(0, 0.32f, -0.12f)));
        }

        foreach (Node n in root.GetChildren())
            if (n is MeshInstance3D mi && mi.MaterialOverride is StandardMaterial3D sm && sm.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded)
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return root;
    }

    /// <summary>Sets one material on every mesh under a node (for ghosts).</summary>
    public static void SetOverride(Node node, Material mat)
    {
        foreach (Node c in node.GetChildren())
        {
            if (c is MeshInstance3D mi)
            {
                mi.MaterialOverride = mat;
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }

            SetOverride(c, mat);
        }
    }

    /// <summary>Draws a tint on top of a block (or clears it with null).</summary>
    public static void SetOverlay(Node node, Material mat)
    {
        foreach (Node c in node.GetChildren())
        {
            if (c is MeshInstance3D mi) mi.MaterialOverlay = mat;
            SetOverlay(c, mat);
        }
    }

    public static StandardMaterial3D Emissive(Color c, float energy) => Glow(c, energy);
}
