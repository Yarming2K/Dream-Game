using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Draws a lot of blocks cheaply, for the builder: blocks are grouped into chunks of 16³ cells, and each
/// chunk draws one MultiMesh per mesh and material. Changing a block only redraws its chunk, at most once
/// a frame, so editing stays quick even on a 300 m design.
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
    private readonly Dictionary<Int3, HashSet<Int3>> _chunks = new();
    private readonly Dictionary<Int3, Node3D> _chunkNodes = new();
    private readonly HashSet<Int3> _dirty = new();

    /// <summary>Draw builder markers (facing arrows) on the blocks.</summary>
    public bool Hints = true;

    public Material TintOverlay;

    public int Count => _entries.Count;

    private static Int3 ChunkOf(Int3 c)
    {
        static int F(int a) => a >= 0 ? a / ChunkSize : -((-a + ChunkSize - 1) / ChunkSize);
        return new Int3(F(c.X), F(c.Y), F(c.Z));
    }

    public void Set(Int3 anchor, BlockDef def, int orientation, float caliber = 76f, bool tinted = false)
    {
        _entries[anchor] = new Entry { Def = def, Orientation = orientation, Caliber = caliber, Tinted = tinted };
        Int3 key = ChunkOf(anchor);
        if (!_chunks.TryGetValue(key, out HashSet<Int3> set)) _chunks[key] = set = new HashSet<Int3>();
        set.Add(anchor);
        _dirty.Add(key);
    }

    public void Remove(Int3 anchor)
    {
        if (!_entries.Remove(anchor)) return;
        Int3 key = ChunkOf(anchor);
        if (_chunks.TryGetValue(key, out HashSet<Int3> set)) set.Remove(anchor);
        _dirty.Add(key);
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
    }

    public void Clear()
    {
        foreach (Int3 key in _chunks.Keys) _dirty.Add(key);
        _entries.Clear();
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
        foreach (Int3 a in anchors)
        {
            Entry e = _entries[a];
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
    }
}
