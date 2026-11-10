using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Draws a lot of blocks cheaply, for the builder: blocks are grouped into chunks of 16³ cells, and each
/// chunk draws one MultiMesh per mesh and material. Plain armor, frames and gas cells are merged into
/// continuous hull surfaces, as in the game. Changing a block only redraws its chunk and the chunks of
/// its neighbors, at most once a frame, so editing stays quick even on a 300 m design.
/// </summary>
public partial class BatchedBlocks : Node3D
{
    private const int ChunkSize = 16;

    private struct Entry
    {
        public BlockDef Def;
        public int Orientation;
        public float Caliber;
        public bool Tinted;
    }

    private readonly Dictionary<Int3, Entry> _entries = new();
    private readonly Dictionary<Int3, Int3> _owner = new();
    private readonly Dictionary<Int3, HashSet<Int3>> _chunks = new();
    private readonly Dictionary<Int3, Node3D> _chunkNodes = new();
    private readonly HashSet<Int3> _dirty = new();
    private readonly List<BlockDef> _surfaceDefs = new();
    private readonly Dictionary<BlockDef, int> _surfaceIndex = new();

    /// <summary>Draw builder markers (facing arrows) on the blocks.</summary>
    public bool Hints = true;

    private Livery _livery;
    private float? _waterline;
    private bool _armorView;

    /// <summary>
    /// Armor view: every block colored by how thick its armor is (grey for parts that aren't armor), so
    /// thin spots stand out.
    /// </summary>
    public bool ArmorView
    {
        get => _armorView;
        set
        {
            if (value == _armorView) return;
            _armorView = value;
            foreach (Int3 key in _chunks.Keys) _dirty.Add(key);
        }
    }

    /// <summary>The armor view's color for a thickness (the legend uses the same).</summary>
    public static Color ArmorColor(float mm)
    {
        (float at, Color c)[] stops =
        {
            (0f, new Color(0.3f, 0.3f, 0.32f)), (25f, new Color(0.25f, 0.5f, 0.95f)), (50f, new Color(0.3f, 0.8f, 0.45f)),
            (80f, new Color(0.95f, 0.82f, 0.25f)), (110f, new Color(1f, 0.5f, 0.15f)), (200f, new Color(0.9f, 0.15f, 0.15f)),
        };
        if (mm <= 0) return stops[0].c;
        for (int i = 1; i < stops.Length; i++)
            if (mm <= stops[i].at) return stops[i - 1].c.Lerp(stops[i].c, (mm - stops[i - 1].at) / (stops[i].at - stops[i - 1].at));
        return stops[^1].c;
    }

    private static readonly Dictionary<string, StandardMaterial3D> ArmorMats = new();

    private static StandardMaterial3D ArmorMaterial(BlockDef def, bool plating)
    {
        string key = $"{def.Id}:{plating}";
        if (ArmorMats.TryGetValue(key, out StandardMaterial3D m)) return m;
        Color c = def.IsArmorLike ? ArmorColor(def.ArmorMm) : new Color(0.24f, 0.24f, 0.25f);
        m = new StandardMaterial3D
        {
            AlbedoColor = c, Roughness = 0.7f, Metallic = 0.1f, VertexColorUseAsAlbedo = plating,
            CullMode = plating ? BaseMaterial3D.CullModeEnum.Back : BaseMaterial3D.CullModeEnum.Disabled,
        };
        return ArmorMats[key] = m;
    }

    /// <summary>Shows the design in a paint job (null or factory for the blocks' own colors); for ships, with the ship rules at <paramref name="waterline"/>.</summary>
    public void SetPaint(Livery livery, float? waterline)
    {
        Livery l = livery != null && !livery.IsFactory ? livery : null;
        float? w = l != null ? waterline : null;
        if (l == _livery && System.Nullable.Equals(w, _waterline)) return;
        _livery = l;
        _waterline = w;
        foreach (Int3 key in _chunks.Keys) _dirty.Add(key);
    }

    public Material TintOverlay;

    public int Count => _entries.Count;

    private static Int3 ChunkOf(Int3 c)
    {
        static int F(int a) => a >= 0 ? a / ChunkSize : -((-a + ChunkSize - 1) / ChunkSize);
        return new Int3(F(c.X), F(c.Y), F(c.Z));
    }

    /// <summary>Blocks drawn as part of a merged hull surface (tinted ones stay separate so the tint shows).</summary>
    private static bool Merges(in Entry e) => !e.Tinted && BlockModels.IsSurface(e.Def);

    private sealed class Grid : IHullGrid
    {
        private readonly BatchedBlocks _b;
        public Grid(BatchedBlocks b) => _b = b;

        private bool At(Int3 c, out Int3 anchor, out Entry e)
        {
            e = default;
            return _b._owner.TryGetValue(c, out anchor) && _b._entries.TryGetValue(anchor, out e);
        }

        public int SurfaceAt(Int3 c) => At(c, out _, out Entry e) && Merges(e) ? _b.SurfaceIndex(e.Def) : -1;
        public int PanelAt(Int3 c) => At(c, out Int3 a, out _) ? GridView.Panel(a) : 0;
        public bool CoversFace(Int3 c, Int3 dir) => At(c, out _, out Entry e) && BlockVisuals.CoversFace(e.Def, e.Orientation, dir);
        public bool Occupied(Int3 c) => _b._owner.ContainsKey(c);
        public float BevelOf(int material) => BlockModels.SurfaceBevel(_b._surfaceDefs[material]);

        public BlockShape ShapeAt(Int3 c, out int orientation)
        {
            bool ok = At(c, out _, out Entry e);
            orientation = ok ? e.Orientation : 0;
            return ok ? e.Def.Shape : BlockShape.Cube;
        }
    }

    private int SurfaceIndex(BlockDef def)
    {
        def = BlockModels.SurfaceDefOf(def);
        if (_surfaceIndex.TryGetValue(def, out int i)) return i;
        _surfaceDefs.Add(def);
        return _surfaceIndex[def] = _surfaceDefs.Count - 1;
    }

    /// <summary>What's in a cell, or null (for working out joints).</summary>
    private BlockDef DefAt(Int3 c) => _owner.TryGetValue(c, out Int3 a) && _entries.TryGetValue(a, out Entry e) ? e.Def : null;

    public void Set(Int3 anchor, BlockDef def, int orientation, float caliber = 76f, bool tinted = false)
    {
        if (_entries.ContainsKey(anchor)) Remove(anchor);
        var e = new Entry { Def = def, Orientation = orientation, Caliber = caliber, Tinted = tinted };
        _entries[anchor] = e;
        foreach (Int3 c in Footprint.Cells(def, anchor, orientation)) _owner[c] = anchor;
        Int3 key = ChunkOf(anchor);
        if (!_chunks.TryGetValue(key, out HashSet<Int3> set)) _chunks[key] = set = new HashSet<Int3>();
        set.Add(anchor);
        _dirty.Add(key);
        TouchNeighbors(def, anchor, orientation);
    }

    public void Remove(Int3 anchor)
    {
        if (!_entries.Remove(anchor, out Entry e)) return;
        foreach (Int3 c in Footprint.Cells(e.Def, anchor, e.Orientation))
            if (_owner.TryGetValue(c, out Int3 a) && a == anchor) _owner.Remove(c);
        Int3 key = ChunkOf(anchor);
        if (_chunks.TryGetValue(key, out HashSet<Int3> set)) set.Remove(anchor);
        _dirty.Add(key);
        TouchNeighbors(e.Def, anchor, e.Orientation);
    }

    /// <summary>Marks the chunks of every block next to this one for redrawing (their surfaces depend on it).</summary>
    private void TouchNeighbors(BlockDef def, Int3 anchor, int orientation)
    {
        foreach (Int3 c in Footprint.Cells(def, anchor, orientation))
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
            if (_owner.TryGetValue(c + new Int3(x, y, z), out Int3 a) && a != anchor)
                _dirty.Add(ChunkOf(a));
    }

    public void SetCaliber(Int3 anchor, float caliber)
    {
        if (!_entries.TryGetValue(anchor, out Entry e) || Mathf.Abs(e.Caliber - caliber) < 0.1f) return;
        e.Caliber = caliber;
        _entries[anchor] = e;
        _dirty.Add(ChunkOf(anchor));
    }

    public void SetTint(Int3 anchor, bool tinted)
    {
        if (!_entries.TryGetValue(anchor, out Entry e) || e.Tinted == tinted) return;
        e.Tinted = tinted;
        _entries[anchor] = e;
        _dirty.Add(ChunkOf(anchor));
        if (BlockModels.IsSurface(e.Def)) TouchNeighbors(e.Def, anchor, e.Orientation);
    }

    public void Clear()
    {
        foreach (Int3 key in _chunks.Keys) _dirty.Add(key);
        _entries.Clear();
        _owner.Clear();
        foreach (HashSet<Int3> set in _chunks.Values) set.Clear();
    }

    public override void _Process(double delta) => Flush();

    public void Flush()
    {
        if (_dirty.Count == 0) return;
        foreach (Int3 key in _dirty) Rebuild(key);
        _dirty.Clear();
    }

    private void Rebuild(Int3 key)
    {
        if (!_chunkNodes.TryGetValue(key, out Node3D node))
        {
            node = new Node3D { Name = $"Chunk{key.X}_{key.Y}_{key.Z}" };
            AddChild(node);
            _chunkNodes[key] = node;
        }

        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }

        if (!_chunks.TryGetValue(key, out HashSet<Int3> anchors) || anchors.Count == 0) return;
        var groups = new Dictionary<(Mesh, Material, bool), List<(Transform3D xf, int o)>>();
        var surface = new List<Int3>();
        foreach (Int3 a in anchors)
        {
            Entry e = _entries[a];
            if (Merges(e))
            {
                surface.AddRange(Footprint.Cells(e.Def, a, e.Orientation));
                continue;
            }

            Transform3D at = Conv.BlockTransform(a, e.Orientation);
            void AddPart(Mesh mesh, Material own, Transform3D local, Paint finish)
            {
                Material mat = _armorView ? ArmorMaterial(e.Def, false)
                    : _livery != null && Liveries.Paints(e.Def, finish) ? Paintwork.Part(e.Def, _livery, finish == Paint.SelfDark, _waterline) : own;
                var gk = (mesh, mat, e.Tinted);
                if (!groups.TryGetValue(gk, out List<(Transform3D, int)> list)) groups[gk] = list = new List<(Transform3D, int)>();
                list.Add((at * local, e.Orientation));
            }

            foreach ((Mesh mesh, Material own, Transform3D local, Paint finish) in BlockVisuals.PartsOf(e.Def, e.Caliber, Hints))
                AddPart(mesh, own, local, finish);
            foreach (ModelPart jp in Joints.For(e.Def, a, e.Orientation, e.Caliber, DefAt))
            {
                (Mesh jm, Material jmat, Transform3D jx, Paint jf) = BlockVisuals.PartOf(e.Def, jp);
                AddPart(jm, jmat, jx, jf);
            }
        }

        foreach (((Mesh mesh, Material mat, bool tinted), List<(Transform3D xf, int o)> xfs) in groups)
        {
            bool painted = mat is ShaderMaterial;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = painted, Mesh = mesh, InstanceCount = xfs.Count };
            for (int i = 0; i < xfs.Count; i++)
            {
                mm.SetInstanceTransform(i, xfs[i].xf);
                if (painted) mm.SetInstanceCustomData(i, Paintwork.PartData(xfs[i].xf, xfs[i].o));
            }

            var mmi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat };
            if (tinted && TintOverlay != null) mmi.MaterialOverlay = TintOverlay;
            if (mat is StandardMaterial3D sm && sm.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded)
                mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            node.AddChild(mmi);
        }

        if (surface.Count == 0) return;
        foreach ((int mat, HullMesher.Surface s) in HullMesher.Build(surface, new Grid(this), Conv.CS))
        {
            if (s.Indices.Count == 0) continue;
            BlockDef def = _surfaceDefs[mat];
            Material m = _armorView ? ArmorMaterial(def, true)
                : def.Glass ? Paintwork.Glass(def, _livery, plating: true)
                : _livery != null ? Paintwork.Plating(def, _livery, _waterline) : BlockVisuals.SurfaceMaterial(def);
            var hull = new MeshInstance3D { Mesh = BlockVisuals.HullMesh(s), MaterialOverride = m };
            node.AddChild(hull);
        }
    }
}
