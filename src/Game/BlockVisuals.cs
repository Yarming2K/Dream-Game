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

    public static float BarrelRadius(float caliberMm) => BlockModels.BarrelRadius(caliberMm);

    /// <summary>
    /// A flat-sided slab along Y whose far end is narrowed by <paramref name="taperX"/> across and
    /// <paramref name="taperZ"/> through: blades, fins and flaps.
    /// </summary>
    private static Mesh Slab(Vector3 size, float taperX, float taperZ)
    {
        string key = $"slab:{size}:{taperX}:{taperZ}";
        if (Meshes.TryGetValue(key, out Mesh cached)) return cached;
        float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f, tx = hx * taperX, tz = hz * taperZ;
        Vector3[] c =
        {
            new(-hx, -hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), new(-hx, -hy, hz),
            new(-tx, hy, -tz), new(tx, hy, -tz), new(tx, hy, tz), new(-tx, hy, tz),
        };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 mid = Vector3.Zero;

        void Face(int a, int b, int d, int e)
        {
            Vector3 n = (c[b] - c[a]).Cross(c[d] - c[a]).Normalized();
            Vector3 centroid = (c[a] + c[b] + c[d] + c[e]) * 0.25f;
            if (n.Dot(centroid - mid) < 0) n = -n;
            foreach ((Vector3 p0, Vector3 p1, Vector3 p2) in new[] { (c[a], c[b], c[d]), (c[a], c[d], c[e]) })
            {
                // Clockwise when seen from outside, which is what Godot treats as the front face.
                (Vector3 q1, Vector3 q2) = (p1 - p0).Cross(p2 - p0).Dot(n) > 0 ? (p2, p1) : (p1, p2);
                st.SetNormal(n);
                st.AddVertex(p0);
                st.SetNormal(n);
                st.AddVertex(q1);
                st.SetNormal(n);
                st.AddVertex(q2);
            }
        }

        Face(0, 1, 2, 3);
        Face(4, 5, 6, 7);
        Face(0, 1, 5, 4);
        Face(1, 2, 6, 5);
        Face(2, 3, 7, 6);
        Face(3, 0, 4, 7);
        Mesh mesh = st.Commit();
        Meshes[key] = mesh;
        return mesh;
    }

    /// <summary>The engine mesh for one shape of a block model (shared by every part with the same shape).</summary>
    private static Mesh MeshOf(in ModelPart p)
    {
        string key = p.MeshKey;
        if (Meshes.TryGetValue(key, out Mesh m)) return m;
        m = p.Prim switch
        {
            Prim.Bevel => Bevel(p.Size.G(), p.Detail),
            Prim.Box => new BoxMesh { Size = p.Size.G() },
            Prim.Cylinder => new CylinderMesh
            {
                BottomRadius = p.Size.X, Height = p.Size.Y, TopRadius = p.Size.Z, RadialSegments = Math.Max(3, (int)p.Detail), Rings = 1,
            },
            Prim.Torus => new TorusMesh { InnerRadius = p.Size.X - p.Size.Y, OuterRadius = p.Size.X + p.Size.Y, Rings = 32, RingSegments = 10 },
            Prim.Sphere => new SphereMesh { Radius = p.Size.X, Height = p.Size.X * 2f, RadialSegments = 20, Rings = 10 },
            Prim.Slab => Slab(p.Size.G(), p.Detail, p.Detail2),
            _ => WedgeMesh(),
        };
        Meshes[key] = m;
        return m;
    }

    private static Material PaintOf(BlockDef def, in ModelPart p) => p.Paint switch
    {
        Paint.Self => MaterialOf(def),
        Paint.SelfDark => Material("darker:" + def.Id, new Color(def.ColorR * 0.6f, def.ColorG * 0.6f, def.ColorB * 0.6f), def.Metallic, def.Roughness + 0.05f),
        Paint.Dark => Dark,
        Paint.Steel => Steel,
        Paint.Brass => Brass,
        Paint.Red => RedTip,
        Paint.Rubber => Rubber,
        Paint.Glass => Material("glass", new Color(0.1f, 0.16f, 0.2f), 0.3f, 0.06f),
        _ => Glow(new Color(p.GlowColor.X, p.GlowColor.Y, p.GlowColor.Z), p.GlowEnergy),
    };

    private static Transform3D ToTransform(System.Numerics.Matrix4x4 m) =>
        new(new Basis(new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M21, m.M22, m.M23), new Vector3(m.M31, m.M32, m.M33)),
            new Vector3(m.M41, m.M42, m.M43));

    /// <summary>
    /// Builds the look of one block in its own local frame (centered, unrotated) from its model.
    /// <paramref name="caliberMm"/> sizes barrels; <paramref name="hints"/> adds builder-only markers like the gun's facing arrow.
    /// Moving parts sit on child nodes named "Spinner", "Flap" or "Payload".
    /// </summary>
    public static Node3D Create(BlockDef def, float caliberMm = 76f, bool hints = false)
    {
        var root = new Node3D { Name = def.Id };
        BlockModel model = BlockModels.For(def, caliberMm, hints);
        var nodes = new Node3D[model.Nodes.Count];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new Node3D { Name = model.Nodes[i].Name, Position = model.Nodes[i].Position.G() };
            root.AddChild(nodes[i]);
        }

        foreach (ModelPart p in model.Parts)
        {
            Material mat = PaintOf(def, p);
            var mi = new MeshInstance3D { Mesh = MeshOf(p), MaterialOverride = mat, Transform = ToTransform(p.Transform) };
            if (p.Paint == Paint.Glow) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            (p.Node < 0 ? root : nodes[p.Node]).AddChild(mi);
        }

        return root;
    }

    // ---------------------------------------------------------------- hull surfaces

    /// <summary>The material for a merged hull surface: the block's own, shaded by the surface's vertex colors.</summary>
    public static StandardMaterial3D SurfaceMaterial(BlockDef def)
    {
        string key = "surface:" + def.Id;
        if (Materials.TryGetValue(key, out StandardMaterial3D m)) return m;
        m = (StandardMaterial3D)MaterialOf(def).Duplicate();
        m.VertexColorUseAsAlbedo = true;
        float k = HullMesher.ShadeCompensation;
        m.AlbedoColor = new Color(Mathf.Min(1f, def.ColorR * k), Mathf.Min(1f, def.ColorG * k), Mathf.Min(1f, def.ColorB * k));
        m.CullMode = BaseMaterial3D.CullModeEnum.Back;
        Materials[key] = m;
        return m;
    }

    /// <summary>Turns a merged hull surface into a mesh (the core winds triangles the other way round from Godot).</summary>
    public static ArrayMesh HullMesh(HullMesher.Surface s)
    {
        int n = s.Positions.Count;
        var verts = new Vector3[n];
        var normals = new Vector3[n];
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            verts[i] = s.Positions[i].G();
            normals[i] = s.Normals[i].G();
            float k = s.Shade[i];
            colors[i] = new Color(k, k, k);
        }

        var idx = new int[s.Indices.Count];
        for (int t = 0; t < idx.Length; t += 3)
        {
            idx[t] = s.Indices[t];
            idx[t + 1] = s.Indices[t + 2];
            idx[t + 2] = s.Indices[t + 1];
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>
    /// Whether a block fills its whole face on side <paramref name="dir"/>, so a hull surface touching it
    /// there can be left out: plain blocks on every side, slopes on their base and back.
    /// </summary>
    public static bool CoversFace(BlockDef def, int orientation, Int3 dir)
    {
        if (BlockModels.IsSurface(def)) return true;
        if (def.Shape != BlockShape.Wedge || def.IsMultiCell) return false;
        return Orientations.Apply(orientation, new Int3(0, -1, 0)) == dir || Orientations.Apply(orientation, new Int3(0, 0, 1)) == dir;
    }

    /// <summary>Blocks that move on their own (spinning, swinging, rolling, dropping their load): drawn one by one.</summary>
    public static bool IsAnimated(BlockDef def) =>
        def.IsWheelLike || def.Kind is BlockKind.AirPropeller or BlockKind.Rotor or BlockKind.LiftFan or BlockKind.ControlSurface or BlockKind.BombRack;

    private static readonly Dictionary<string, List<(Mesh, Material, Transform3D)>> PartsCache = new();

    /// <summary>A block's look as a flat list of meshes, materials and placements (for drawing many at once).</summary>
    public static List<(Mesh, Material, Transform3D)> Parts(BlockDef def, float caliberMm, bool hints = false)
    {
        string key = $"{def.Id}:{Mathf.RoundToInt(caliberMm)}:{hints}";
        if (PartsCache.TryGetValue(key, out List<(Mesh, Material, Transform3D)> parts)) return parts;
        parts = new List<(Mesh, Material, Transform3D)>();
        BlockModel model = BlockModels.For(def, caliberMm, hints);
        foreach (ModelPart p in model.Parts) parts.Add((MeshOf(p), PaintOf(def, p), ToTransform(model.PlacementOf(p))));
        PartsCache[key] = parts;
        return parts;
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
