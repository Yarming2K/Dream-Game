using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Two things that keep a hundred vehicles affordable to draw:
/// <list type="bullet">
/// <item><b>Shared looks.</b> Vehicles built from the same design start out with the very same meshes, so
/// they share them instead of each building and storing its own. A vehicle only gets meshes of its own for
/// the parts of it that get damaged.</item>
/// <item><b>A far model.</b> Each section (hull, turret, gun) also gets a single low-detail mesh: its plating
/// as flat panels and its other parts in simple shapes, in their own colors, drawn in one go. Past a
/// distance that depends on the vehicle's size the detailed batches are switched off and the far model is
/// shown instead, so a tank on the horizon costs a few draw calls instead of dozens. The far model sits a
/// few centimeters inside the real surface, so where the two overlap during the switch the detailed one
/// is the one you see. It's rebuilt (now and then, and only while it's being looked at) after damage.</item>
/// </list>
/// </summary>
public sealed partial class GridView
{
    private sealed class Piece
    {
        public Resource Res;
        public Material Mat;
        public bool ShadowOff;

        public GeometryInstance3D Instance() => Res is MultiMesh mm
            ? new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = Mat, CastShadow = ShadowOff ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On }
            : new MeshInstance3D { Mesh = (Mesh)Res, MaterialOverride = Mat };
    }

    private sealed class SharedLook
    {
        public readonly Dictionary<(int, Int3), List<Piece>> Chunks = new();
        public readonly Dictionary<int, ArrayMesh> Far = new();
    }

    private static readonly ConditionalWeakTable<object, SharedLook> SharedLooks = new();
    private static readonly Dictionary<Mesh, (Vector3[] v, Vector3[] n, int[] i)> MeshData = new();
    private static StandardMaterial3D _farMaterial;

    private SharedLook _shared;
    private bool _building;
    private bool _lod;
    private float _nearEnd, _farBegin;
    private MeshInstance3D[] _far;
    private bool[] _farDirty;
    private bool _farTimer;
    private ulong _farBuiltMs;

    /// <summary>Beyond about this distance from the camera the far model takes over.</summary>
    public float LodDistance { get; private set; }

    /// <summary>
    /// Stretches every switch distance (looking through a zoomed sight makes far things big on screen, so
    /// they need their detail further out). Apply a change with <see cref="RefreshLod"/>.
    /// </summary>
    public static float LodScale = 1f;

    private readonly List<GeometryInstance3D> _nearNodes = new();
    private float _appliedScale = 1f;

    private void SetupLod(object shareKey, bool lod)
    {
        _lod = lod;
        _appliedScale = LodScale;
        if (shareKey != null) _shared = SharedLooks.GetValue(shareKey, _ => new SharedLook());
        if (!lod) return;
        float r = BoundingRadius();
        LodDistance = Mathf.Clamp(r * 40f, 150f, 1600f);
        _nearEnd = LodDistance + r;
        _farBegin = Mathf.Max(LodDistance - r, 1f);
        _far = new MeshInstance3D[Model.Sections.Count];
        _farDirty = new bool[Model.Sections.Count];
    }

    /// <summary>Switches the detailed parts under a node off past the far model's distance.</summary>
    private void ApplyNear(Node n)
    {
        if (!_lod) return;
        if (n is GeometryInstance3D g)
        {
            g.VisibilityRangeEnd = _nearEnd * LodScale;
            g.VisibilityRangeEndMargin = 4f;
            _nearNodes.Add(g);
        }

        foreach (Node c in n.GetChildren()) ApplyNear(c);
    }

    private void BuildAllFar()
    {
        if (!_lod) return;
        foreach (Section s in Model.Sections)
        {
            ArrayMesh mesh = null;
            if (_shared != null && _shared.Far.TryGetValue(s.Id, out ArrayMesh cached)) mesh = cached;
            else
            {
                mesh = BuildFarMesh(s.Id);
                if (_shared != null) _shared.Far[s.Id] = mesh;
            }

            SetFar(s.Id, mesh);
        }

        _farBuiltMs = Time.GetTicksMsec();
    }

    private void SetFar(int section, ArrayMesh mesh)
    {
        if (_far[section] != null && GodotObject.IsInstanceValid(_far[section])) _far[section].QueueFree();
        _far[section] = null;
        if (mesh == null) return;
        var mi = new MeshInstance3D
        {
            Name = "Far", Mesh = mesh, MaterialOverride = FarMaterial, VisibilityRangeBegin = _farBegin * LodScale, VisibilityRangeBeginMargin = 4f,
        };
        _sections[section].AddChild(mi);
        _far[section] = mi;
    }

    // Two-sided, like the blocks' own materials: some part shapes (slopes) are wound either way round.
    private static StandardMaterial3D FarMaterial => _farMaterial ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.72f, Metallic = 0.25f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>Re-applies the switch distances after <see cref="LodScale"/> changed.</summary>
    public void RefreshLod()
    {
        if (!_lod || Mathf.IsEqualApprox(_appliedScale, LodScale)) return;
        _appliedScale = LodScale;
        _nearNodes.RemoveAll(g => !GodotObject.IsInstanceValid(g));
        foreach (GeometryInstance3D g in _nearNodes) g.VisibilityRangeEnd = _nearEnd * LodScale;
        foreach (MeshInstance3D f in _far)
            if (f != null && GodotObject.IsInstanceValid(f)) f.VisibilityRangeBegin = _farBegin * LodScale;
    }

    /// <summary>A section's detailed look changed (damage, repairs): its far model needs redoing at some point.</summary>
    private void FarChanged(int section)
    {
        if (!_lod || _building) return;
        _farDirty[section] = true;
        TryRebuildFar();
    }

    private void TryRebuildFar()
    {
        if (!GodotObject.IsInstanceValid(Body) || !Body.IsInsideTree()) return;
        bool seen = Body.GlobalPosition.DistanceTo(WorldQuery.Viewer) > _farBegin - 50f;
        if (seen && Time.GetTicksMsec() - _farBuiltMs > 3000)
        {
            for (int i = 0; i < _farDirty.Length; i++)
            {
                if (!_farDirty[i]) continue;
                _farDirty[i] = false;
                SetFar(i, BuildFarMesh(i));
            }

            _farBuiltMs = Time.GetTicksMsec();
            return;
        }

        if (_farTimer) return;
        _farTimer = true;
        Body.GetTree().CreateTimer(3.0).Timeout += () =>
        {
            _farTimer = false;
            if (GodotObject.IsInstanceValid(Body) && Array.Exists(_farDirty, d => d)) TryRebuildFar();
        };
    }

    private static readonly Int3[] Dirs =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private static int Axis(Int3 d) => d.X != 0 ? 0 : d.Y != 0 ? 1 : 2;
    private static int Get(Int3 c, int axis) => axis == 0 ? c.X : axis == 1 ? c.Y : c.Z;

    private static Vector3 Make(int axis, float a, int uAxis, float u, int vAxis, float v)
    {
        var p = new float[3];
        p[axis] = a;
        p[uAxis] = u;
        p[vAxis] = v;
        return new Vector3(p[0], p[1], p[2]);
    }

    /// <summary>The far model of one section: plating as merged flat panels, everything else in simple shapes.</summary>
    private ArrayMesh BuildFarMesh(int sectionId)
    {
        long started = Perf.Now;
        Section s = Model.Sections[sectionId];
        var grid = new SectionGrid(this, sectionId);
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var cols = new List<Color>();
        var idx = new List<int>();
        const float inset = 0.03f;

        // Plating: every exposed face, merged into the biggest rectangles of one color. A painted pattern can't
        // be one flat color over a big panel, so then panels stay small and each corner takes the pattern's color.
        var palette = new List<Color>();
        var paletteDefs = new List<BlockDef>();
        var paletteIndex = new Dictionary<BlockDef, int>();
        int maxRun = _livery != null && _livery.Pattern != LiveryPattern.Plain || _waterline.HasValue ? 2 : int.MaxValue;
        foreach (Int3 d in Dirs)
        {
            int axis = Axis(d), uAxis = (axis + 1) % 3, vAxis = (axis + 2) % 3;
            int sign = Get(d, axis);
            var layers = new Dictionary<int, Dictionary<(int, int), int>>();
            foreach ((Int3 c, BlockInstance b) in s.Cells)
            {
                if (!b.Alive || !BlockModels.IsSurface(b.Def) || grid.CoversFace(c + d, new Int3(-d.X, -d.Y, -d.Z))) continue;
                BlockDef sd = BlockModels.SurfaceDefOf(b.Def);
                if (!paletteIndex.TryGetValue(sd, out int color))
                {
                    color = palette.Count;
                    // A touch darker than the block's color: the real plating is shaded into its corners.
                    palette.Add(new Color(sd.ColorR * FarShade, sd.ColorG * FarShade, sd.ColorB * FarShade));
                    paletteDefs.Add(sd);
                    paletteIndex[sd] = color;
                }

                int layer = Get(c, axis);
                if (!layers.TryGetValue(layer, out Dictionary<(int, int), int> mask)) layers[layer] = mask = new Dictionary<(int, int), int>();
                mask[(Get(c, uAxis), Get(c, vAxis))] = color;
            }

            Vector3 n = d.G();
            foreach ((int layer, Dictionary<(int, int), int> mask) in layers)
            {
                var used = new HashSet<(int, int)>();
                var keys = new List<(int, int)>(mask.Keys);
                keys.Sort();
                foreach ((int u0, int v0) in keys)
                {
                    if (used.Contains((u0, v0))) continue;
                    int color = mask[(u0, v0)];
                    int w = 1;
                    while (w < maxRun && mask.TryGetValue((u0 + w, v0), out int c2) && c2 == color && !used.Contains((u0 + w, v0))) w++;
                    int h = 1;
                    while (h < maxRun)
                    {
                        bool row = true;
                        for (int k = 0; k < w && row; k++)
                            row = mask.TryGetValue((u0 + k, v0 + h), out int c3) && c3 == color && !used.Contains((u0 + k, v0 + h));
                        if (!row) break;
                        h++;
                    }

                    for (int a = 0; a < w; a++)
                    for (int b = 0; b < h; b++)
                        used.Add((u0 + a, v0 + b));

                    float plane = (layer + 0.5f * sign) * CS - sign * inset;
                    // Sunk a little along the normal only: panels next to each other still meet exactly, and at
                    // corners the panels cross, so there are no gaps.
                    float ua = (u0 - 0.5f) * CS, ub = (u0 + w - 0.5f) * CS;
                    float va = (v0 - 0.5f) * CS, vb = (v0 + h - 0.5f) * CS;
                    int first = verts.Count;
                    Quad(verts, norms, cols, idx, n, palette[color],
                        Make(axis, plane, uAxis, ua, vAxis, va), Make(axis, plane, uAxis, ub, vAxis, va),
                        Make(axis, plane, uAxis, ub, vAxis, vb), Make(axis, plane, uAxis, ua, vAxis, vb));
                    if (_livery != null || paletteDefs[color].Glass)
                        for (int q = first; q < verts.Count; q++)
                            cols[q] = Paintwork.FarColor(paletteDefs[color], _livery, _waterline, verts[q].N(), n.N(), true, palette[color]) * FarShade;
                }
            }
        }

        // Shaped plating (slopes, corners...) and everything else that can be seen from outside, in their own
        // shapes, leaving out the tiny bits.
        foreach (BlockInstance b in Model.Instances)
        {
            if (b.Section != sectionId || !b.Alive || (BlockModels.IsSurface(b.Def) && b.Def.Shape == BlockShape.Cube) || Enclosed(b, grid)) continue;
            Transform3D at = Conv.BlockTransform(b.Cell, b.Orientation);
            bool shapedPlate = BlockModels.IsSurface(b.Def);
            foreach ((Mesh mesh, Material mat, Transform3D local, Paint finish) in BlockVisuals.PartsOf(b.Def, CaliberOf(b)))
            {
                Transform3D xf = at * local;
                Aabb box = xf * mesh.GetAabb();
                if (Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)) < 0.09f) continue;
                Color own = shapedPlate ? new Color(b.Def.ColorR * FarShade, b.Def.ColorG * FarShade, b.Def.ColorB * FarShade) : ColorOf(mat);
                bool painted = (_livery != null && Liveries.Paints(b.Def, finish)) || b.Def.Glass;
                BlockDef def = b.Def;
                Append(verts, norms, cols, idx, Simplified(mesh), xf, own,
                    painted ? (p, nrm) => Paintwork.FarColor(def, _livery, _waterline, p.N(), nrm.N(), shapedPlate, own) : null);
            }
        }

        Perf.Add(Perf.Visuals, started);
        if (idx.Count == 0) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var result = new ArrayMesh();
        result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return result;
    }

    private const float CS = Conv.CS;
    private const float FarShade = 0.9f;

    private static readonly Dictionary<Mesh, Mesh> Simpler = new();

    /// <summary>Round parts with fewer sides for the far model (nobody can count a wheel's sides from 200 m).</summary>
    private static Mesh Simplified(Mesh m)
    {
        if (Simpler.TryGetValue(m, out Mesh s)) return s;
        s = m switch
        {
            CylinderMesh c when c.RadialSegments > 8 => new CylinderMesh
            {
                TopRadius = c.TopRadius, BottomRadius = c.BottomRadius, Height = c.Height, RadialSegments = 8, Rings = 0,
                CapTop = c.CapTop, CapBottom = c.CapBottom,
            },
            TorusMesh t when t.Rings > 12 => new TorusMesh { InnerRadius = t.InnerRadius, OuterRadius = t.OuterRadius, Rings = 12, RingSegments = 4 },
            SphereMesh sp when sp.RadialSegments > 8 => new SphereMesh { Radius = sp.Radius, Height = sp.Height, RadialSegments = 8, Rings = 4 },
            _ => m,
        };
        return Simpler[m] = s;
    }

    /// <summary>True when plating covers every side of a block, so it can't be seen from outside.</summary>
    private static bool Enclosed(BlockInstance b, SectionGrid grid)
    {
        var own = new HashSet<Int3>(b.Cells);
        foreach (Int3 c in b.Cells)
        foreach (Int3 d in Dirs)
        {
            Int3 nb = c + d;
            if (own.Contains(nb)) continue;
            if (!grid.CoversFace(nb, new Int3(-d.X, -d.Y, -d.Z))) return false;
        }

        return true;
    }

    private static Color ColorOf(Material m) => m switch
    {
        BaseMaterial3D bm when bm.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded => bm.AlbedoColor,
        BaseMaterial3D bm when bm.EmissionEnabled => bm.Emission,
        BaseMaterial3D bm => bm.AlbedoColor,
        _ => new Color(0.5f, 0.5f, 0.5f),
    };

    /// <summary>One quad facing <paramref name="n"/>, wound the way Godot draws front faces.</summary>
    private static void Quad(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> idx, Vector3 normal, Color color,
        Vector3 a, Vector3 b, Vector3 cc, Vector3 d)
    {
        int i = v.Count;
        v.Add(a);
        v.Add(b);
        v.Add(cc);
        v.Add(d);
        for (int k = 0; k < 4; k++)
        {
            n.Add(normal);
            c.Add(color);
        }

        bool ccw = (b - a).Cross(cc - a).Dot(normal) > 0;
        if (ccw)
        {
            idx.Add(i);
            idx.Add(i + 2);
            idx.Add(i + 1);
            idx.Add(i);
            idx.Add(i + 3);
            idx.Add(i + 2);
        }
        else
        {
            idx.Add(i);
            idx.Add(i + 1);
            idx.Add(i + 2);
            idx.Add(i);
            idx.Add(i + 2);
            idx.Add(i + 3);
        }
    }

    /// <summary>Copies a mesh's triangles in, placed by <paramref name="xf"/>, in one color.</summary>
    private static void Append(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> idx, Mesh mesh, in Transform3D xf, Color color,
        Func<Vector3, Vector3, Color> colorAt = null)
    {
        if (!MeshData.TryGetValue(mesh, out (Vector3[] v, Vector3[] n, int[] i) data))
        {
            var vs = new List<Vector3>();
            var ns = new List<Vector3>();
            var ids = new List<int>();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                Godot.Collections.Array arr = mesh.SurfaceGetArrays(s);
                Vector3[] sv = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Variant normals = arr[(int)Mesh.ArrayType.Normal], indices = arr[(int)Mesh.ArrayType.Index];
                Vector3[] sn = normals.VariantType == Variant.Type.Nil ? Array.Empty<Vector3>() : normals.AsVector3Array();
                int[] si = indices.VariantType == Variant.Type.Nil ? Array.Empty<int>() : indices.AsInt32Array();
                int baseIndex = vs.Count;
                vs.AddRange(sv);
                for (int k = 0; k < sv.Length; k++) ns.Add(k < sn.Length ? sn[k] : Vector3.Up);
                if (si.Length == 0)
                    for (int k = 0; k < sv.Length; k++) ids.Add(baseIndex + k);
                else
                    foreach (int k in si) ids.Add(baseIndex + k);
            }

            data = (vs.ToArray(), ns.ToArray(), ids.ToArray());
            MeshData[mesh] = data;
        }

        int first = v.Count;
        Basis nb = xf.Basis.Inverse().Transposed();
        for (int k = 0; k < data.v.Length; k++)
        {
            Vector3 p = xf * data.v[k];
            Vector3 q = (nb * data.n[k]).Normalized();
            v.Add(p);
            n.Add(q);
            c.Add(colorAt != null ? colorAt(p, q) : color);
        }

        // A mirrored placement flips the winding; flip it back.
        bool mirrored = xf.Basis.Determinant() < 0;
        for (int t = 0; t + 2 < data.i.Length; t += 3)
        {
            idx.Add(first + data.i[t]);
            idx.Add(first + data.i[mirrored ? t + 2 : t + 1]);
            idx.Add(first + data.i[mirrored ? t + 1 : t + 2]);
        }
    }
}
