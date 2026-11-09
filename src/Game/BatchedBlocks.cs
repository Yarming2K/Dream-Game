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
    }

    private int SurfaceIndex(BlockDef def)
    {
        if (_surfaceIndex.TryGetValue(def, out int i)) return i;
        _surfaceDefs.Add(def);
        return _surfaceIndex[def] = _surfaceDefs.Count - 1;
    }

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
        var groups = new Dictionary<(Mesh, Material, bool), List<Transform3D>>();
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
            foreach ((Mesh mesh, Material mat, Transform3D local) in BlockVisuals.Parts(e.Def, e.Caliber, Hints))
            {
                var gk = (mesh, mat, e.Tinted);
                if (!groups.TryGetValue(gk, out List<Transform3D> list)) groups[gk] = list = new List<Transform3D>();
                list.Add(at * local);
            }
        }

        foreach (((Mesh mesh, Material mat, bool tinted), List<Transform3D> xfs) in groups)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xfs.Count };
            for (int i = 0; i < xfs.Count; i++) mm.SetInstanceTransform(i, xfs[i]);
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
            node.AddChild(new MeshInstance3D { Mesh = BlockVisuals.HullMesh(s), MaterialOverride = BlockVisuals.SurfaceMaterial(_surfaceDefs[mat]) });
        }
    }
}
