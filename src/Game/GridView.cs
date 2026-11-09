using System;
using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Puts a <see cref="GridModel"/> on screen and into physics, in a way that holds up for machines of
/// hundreds of thousands of blocks:
/// <list type="bullet">
/// <item>One node per section, so turrets and guns can turn.</item>
/// <item>Blocks are drawn in batches: per chunk of 16³ cells, one MultiMesh for each mesh and material.
/// Blocks that animate (wheels, propellers, rotors, control surfaces, bomb racks) keep their own nodes.</item>
/// <item>Plain armor, frames and gas cells are merged into continuous hull surfaces (see <see cref="HullMesher"/>),
/// so a wall of blocks reads as one plate with chamfered outer edges and shaded corners.</item>
/// <item>Colliders are merged: each chunk's solid cells become as few boxes as possible. Wedges keep their
/// own shapes.</item>
/// </list>
/// When blocks are destroyed or welded back, only their chunks are rebuilt, once per frame.
/// </summary>
public sealed class GridView
{
    private const int ChunkSize = 16;

    private struct BlockNodes
    {
        public Node3D Visual;
        public CollisionShape3D Collider;
    }

    private sealed class Chunk
    {
        public int Section;
        public Node3D Node;
        public readonly HashSet<BlockInstance> Blocks = new();
        public readonly List<(CollisionShape3D shape, Transform3D local)> Colliders = new();
        public bool VisualDirty, ColliderDirty;
    }

    public readonly PhysicsBody3D Body;
    public readonly GridModel Model;
    private readonly Node3D[] _sections;
    private readonly float[] _syncedAngle;
    private readonly Dictionary<BlockInstance, BlockNodes> _nodes = new();
    private readonly Dictionary<(int, Int3), Chunk> _chunks = new();
    private readonly Dictionary<BlockInstance, Chunk> _chunkOf = new();
    private readonly HashSet<Chunk> _dirty = new();
    private readonly Func<Int3, float> _caliberOf;
    private readonly List<BlockDef> _surfaceDefs = new();
    private readonly Dictionary<BlockDef, int> _surfaceIndex = new();
    private bool _flushQueued;

    /// <summary>One section of the grid, as the hull mesher sees it.</summary>
    private sealed class SectionGrid : IHullGrid
    {
        private readonly GridView _v;
        private readonly Dictionary<Int3, BlockInstance> _cells;

        public SectionGrid(GridView v, int section)
        {
            _v = v;
            _cells = v.Model.Sections[section].Cells;
        }

        private BlockInstance At(Int3 c) => _cells.TryGetValue(c, out BlockInstance b) && b.Alive ? b : null;
        public int SurfaceAt(Int3 c) => At(c) is { } b && BlockModels.IsSurface(b.Def) ? _v.SurfaceIndex(b.Def) : -1;
        public int PanelAt(Int3 c) => At(c) is { } b ? Panel(b.Cell) : 0;
        public bool CoversFace(Int3 c, Int3 dir) => At(c) is { } b && BlockVisuals.CoversFace(b.Def, b.Orientation, dir);
        public bool Occupied(Int3 c) => At(c) != null;
        public float BevelOf(int material) => BlockModels.SurfaceBevel(_v._surfaceDefs[material]);
    }

    /// <summary>A stable number per block, so each panel gets its own slight tint.</summary>
    public static int Panel(Int3 anchor) => anchor.X * 73856093 ^ anchor.Y * 19349663 ^ anchor.Z * 83492791;

    private int SurfaceIndex(BlockDef def)
    {
        if (_surfaceIndex.TryGetValue(def, out int i)) return i;
        _surfaceDefs.Add(def);
        return _surfaceIndex[def] = _surfaceDefs.Count - 1;
    }

    public GridView(PhysicsBody3D body, GridModel model, Func<Int3, float> caliberOf = null)
    {
        Body = body;
        Model = model;
        _caliberOf = caliberOf;
        _sections = new Node3D[model.Sections.Count];
        _syncedAngle = new float[model.Sections.Count];
        foreach (Section s in model.Sections)
        {
            var node = new Node3D { Name = $"{s.Kind}{s.Id}" };
            Node parent = s.Parent < 0 ? body : _sections[s.Parent];
            parent.AddChild(node);
            node.Transform = Conv.SectionLocal(s);
            _sections[s.Id] = node;
        }

        foreach (BlockInstance b in model.Instances) Place(b);
        Flush();
    }

    /// <summary>How many draw batches and collider shapes this grid uses (for checking performance).</summary>
    public (int batches, int shapes, int animated) Stats()
    {
        int batches = 0, shapes = 0;
        foreach (Chunk c in _chunks.Values)
        {
            batches += c.Node?.GetChildCount() ?? 0;
            shapes += c.Colliders.Count;
        }

        foreach (BlockNodes n in _nodes.Values)
            if (n.Collider != null) shapes++;
        return (batches, shapes, _nodes.Count);
    }

    /// <summary>Puts a block that was added after spawning (welded back on) on screen and into physics.</summary>
    public void AddBlock(BlockInstance b)
    {
        if (_chunkOf.ContainsKey(b)) return;
        Place(b);
        TouchNeighbors(b);
        QueueFlush();
    }

    private static Int3 ChunkKey(Int3 cell) =>
        new(FloorDiv(cell.X, ChunkSize), FloorDiv(cell.Y, ChunkSize), FloorDiv(cell.Z, ChunkSize));

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    private float CaliberOf(BlockInstance b) => _caliberOf?.Invoke(b.Cell) ?? 76f;

    private void Place(BlockInstance b)
    {
        var key = (b.Section, ChunkKey(b.Cell));
        if (!_chunks.TryGetValue(key, out Chunk chunk))
        {
            chunk = new Chunk { Section = b.Section };
            _chunks[key] = chunk;
        }

        chunk.Blocks.Add(b);
        _chunkOf[b] = chunk;

        var nodes = new BlockNodes();
        if (BlockVisuals.IsAnimated(b.Def))
        {
            nodes.Visual = BlockVisuals.Create(b.Def, CaliberOf(b));
            nodes.Visual.Transform = Conv.BlockTransform(b.Cell, b.Orientation);
            _sections[b.Section].AddChild(nodes.Visual);
        }
        else
        {
            chunk.VisualDirty = true;
        }

        if (b.Def.HasCollider && b.Def.Shape == BlockShape.Wedge)
        {
            nodes.Collider = new CollisionShape3D { Shape = BlockVisuals.ShapeOf(b.Def) };
            nodes.Collider.Transform = SectionToBody(b.Section) * Conv.BlockTransform(b.Cell, b.Orientation);
            Body.AddChild(nodes.Collider);
        }
        else if (b.Def.HasCollider)
        {
            chunk.ColliderDirty = true;
        }

        if (nodes.Visual != null || nodes.Collider != null) _nodes[b] = nodes;
        if (chunk.VisualDirty || chunk.ColliderDirty) _dirty.Add(chunk);
    }

    public void RemoveBlock(BlockInstance b)
    {
        if (_nodes.Remove(b, out BlockNodes n))
        {
            if (n.Visual != null && GodotObject.IsInstanceValid(n.Visual)) n.Visual.QueueFree();
            if (n.Collider != null && GodotObject.IsInstanceValid(n.Collider))
            {
                Body.RemoveChild(n.Collider);
                n.Collider.QueueFree();
            }
        }

        if (!_chunkOf.Remove(b, out Chunk chunk)) return;
        chunk.Blocks.Remove(b);
        if (!BlockVisuals.IsAnimated(b.Def)) chunk.VisualDirty = true;
        TouchNeighbors(b);
        if (b.Def.HasCollider && b.Def.Shape != BlockShape.Wedge) chunk.ColliderDirty = true;
        _dirty.Add(chunk);
        QueueFlush();
    }

    /// <summary>
    /// Marks for redrawing every chunk holding a block next to this one (diagonals too): their hull
    /// surfaces, chamfers and corner shading change when a neighbor comes or goes.
    /// </summary>
    private void TouchNeighbors(BlockInstance b)
    {
        Dictionary<Int3, BlockInstance> cells = Model.Sections[b.Section].Cells;
        foreach (Int3 c in b.Cells)
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            if (!cells.TryGetValue(c + new Int3(x, y, z), out BlockInstance nb) || nb == b) continue;
            if (!_chunkOf.TryGetValue(nb, out Chunk ch) || ch.VisualDirty || !BlockModels.IsSurface(nb.Def)) continue;
            ch.VisualDirty = true;
            _dirty.Add(ch);
        }
    }

    private void QueueFlush()
    {
        if (_flushQueued) return;
        _flushQueued = true;
        Callable.From(Flush).CallDeferred();
    }

    /// <summary>Rebuilds the batches and merged colliders of every chunk that changed.</summary>
    public void Flush()
    {
        _flushQueued = false;
        if (!GodotObject.IsInstanceValid(Body)) return;
        foreach (Chunk c in _dirty)
        {
            if (c.VisualDirty) RebuildVisual(c);
            if (c.ColliderDirty) RebuildColliders(c);
            c.VisualDirty = c.ColliderDirty = false;
        }

        _dirty.Clear();
    }

    private void RebuildVisual(Chunk c)
    {
        if (c.Node == null)
        {
            c.Node = new Node3D { Name = "Batch" };
            _sections[c.Section].AddChild(c.Node);
        }

        foreach (Node child in c.Node.GetChildren())
        {
            c.Node.RemoveChild(child);
            child.QueueFree();
        }

        var groups = new Dictionary<(Mesh, Material), List<Transform3D>>();
        var surface = new List<Int3>();
        foreach (BlockInstance b in c.Blocks)
        {
            if (!b.Alive || BlockVisuals.IsAnimated(b.Def)) continue;
            if (BlockModels.IsSurface(b.Def))
            {
                surface.AddRange(b.Cells);
                continue;
            }

            Transform3D at = Conv.BlockTransform(b.Cell, b.Orientation);
            foreach ((Mesh mesh, Material mat, Transform3D local) in BlockVisuals.Parts(b.Def, CaliberOf(b)))
            {
                if (!groups.TryGetValue((mesh, mat), out List<Transform3D> list)) groups[(mesh, mat)] = list = new List<Transform3D>();
                list.Add(at * local);
            }
        }

        foreach (((Mesh mesh, Material mat), List<Transform3D> xfs) in groups)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xfs.Count };
            for (int i = 0; i < xfs.Count; i++) mm.SetInstanceTransform(i, xfs[i]);
            var mmi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat };
            if (mat is StandardMaterial3D sm && sm.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded)
                mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            c.Node.AddChild(mmi);
        }

        if (surface.Count == 0) return;
        foreach ((int mat, HullMesher.Surface s) in HullMesher.Build(surface, new SectionGrid(this, c.Section), Conv.CS))
        {
            if (s.Indices.Count == 0) continue;
            c.Node.AddChild(new MeshInstance3D { Mesh = BlockVisuals.HullMesh(s), MaterialOverride = BlockVisuals.SurfaceMaterial(_surfaceDefs[mat]) });
        }
    }

    /// <summary>Merges the chunk's solid cells into boxes: runs along X, then rows along Z, then layers along Y.</summary>
    private void RebuildColliders(Chunk c)
    {
        foreach ((CollisionShape3D shape, _) in c.Colliders)
        {
            if (!GodotObject.IsInstanceValid(shape)) continue;
            Body.RemoveChild(shape);
            shape.QueueFree();
        }

        c.Colliders.Clear();
        var solid = new HashSet<Int3>();
        foreach (BlockInstance b in c.Blocks)
            if (b.Alive && b.Def.HasCollider && b.Def.Shape != BlockShape.Wedge)
                foreach (Int3 cell in b.Cells) solid.Add(cell);
        if (solid.Count == 0) return;

        var cells = new List<Int3>(solid);
        cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        var used = new HashSet<Int3>();
        bool Free(Int3 p) => solid.Contains(p) && !used.Contains(p);
        Transform3D toBody = SectionToBody(c.Section);
        foreach (Int3 start in cells)
        {
            if (used.Contains(start)) continue;
            int dx = 1;
            while (Free(start + new Int3(dx, 0, 0))) dx++;
            int dz = 1;
            while (RowFree(start + new Int3(0, 0, dz), dx)) dz++;
            int dy = 1;
            while (LayerFree(start + new Int3(0, dy, 0), dx, dz)) dy++;
            for (int x = 0; x < dx; x++)
            for (int y = 0; y < dy; y++)
            for (int z = 0; z < dz; z++)
                used.Add(start + new Int3(x, y, z));

            var size = new Vector3(dx, dy, dz) * Conv.CS;
            Vector3 center = (start.G() + new Vector3(dx - 1, dy - 1, dz - 1) * 0.5f) * Conv.CS;
            var local = new Transform3D(Basis.Identity, center);
            var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = toBody * local };
            Body.AddChild(shape);
            c.Colliders.Add((shape, local));
        }

        bool RowFree(Int3 from, int len)
        {
            for (int x = 0; x < len; x++)
                if (!Free(from + new Int3(x, 0, 0))) return false;
            return true;
        }

        bool LayerFree(Int3 from, int lenX, int lenZ)
        {
            for (int z = 0; z < lenZ; z++)
                if (!RowFree(from + new Int3(0, 0, z), lenX)) return false;
            return true;
        }
    }

    public Node3D SectionNode(int id) => _sections[id];

    /// <summary>The block's own node, for blocks that animate (wheels, propellers, flaps...). Null for batched blocks.</summary>
    public Node3D VisualOf(BlockInstance b) => _nodes.TryGetValue(b, out BlockNodes n) ? n.Visual : null;

    /// <summary>Where a section is in the world right now.</summary>
    public Transform3D SectionGlobal(int id) => _sections[id].GlobalTransform;

    /// <summary>A section's placement relative to the body, combining every turn above it.</summary>
    public Transform3D SectionToBody(int id)
    {
        Transform3D t = Transform3D.Identity;
        for (int s = id; s >= 0; s = Model.Sections[s].Parent)
            t = Conv.SectionLocal(Model.Sections[s]) * t;
        return t;
    }

    public void SetAngle(int sectionId, float angle)
    {
        Section s = Model.Sections[sectionId];
        s.Angle = angle;
        _sections[sectionId].Transform = Conv.SectionLocal(s);
    }

    /// <summary>
    /// Moves turret colliders to match the visuals. Physics engines rebuild a body's shape when a collider
    /// moves, so this only happens once a section has turned a few degrees.
    /// </summary>
    public void SyncColliders(bool force = false)
    {
        var dirty = new bool[Model.Sections.Count];
        bool any = false;
        foreach (Section s in Model.Sections)
        {
            bool parentDirty = s.Parent >= 0 && dirty[s.Parent];
            if (s.Kind == SectionKind.Hull && !force) continue;
            if (!force && !parentDirty && MathF.Abs(s.Angle - _syncedAngle[s.Id]) < Mathf.DegToRad(3f)) continue;
            dirty[s.Id] = true;
            _syncedAngle[s.Id] = s.Angle;
            any = true;
        }

        if (!any) return;
        foreach (Chunk c in _chunks.Values)
        {
            if (!dirty[c.Section]) continue;
            Transform3D toBody = SectionToBody(c.Section);
            foreach ((CollisionShape3D shape, Transform3D local) in c.Colliders)
                if (GodotObject.IsInstanceValid(shape)) shape.Transform = toBody * local;
        }

        foreach (KeyValuePair<BlockInstance, BlockNodes> kv in _nodes)
        {
            if (kv.Value.Collider == null || !dirty[kv.Key.Section]) continue;
            kv.Value.Collider.Transform = SectionToBody(kv.Key.Section) * Conv.BlockTransform(kv.Key.Cell, kv.Key.Orientation);
        }
    }

    /// <summary>World-space center of a block, following any turret or gun turn.</summary>
    public Vector3 BlockWorldPos(BlockInstance b) => SectionGlobal(b.Section) * (b.Center.G() * Conv.CS);

    /// <summary>Radius around the body origin that contains every block, however its turrets are turned.</summary>
    public float BoundingRadius()
    {
        float r = 0, pivots = 0;
        foreach (Int3 c in Model.Blocks.Keys) r = MathF.Max(r, Conv.CellPos(c).Length());
        foreach (Section s in Model.Sections) pivots = MathF.Max(pivots, s.Pivot.Length() * Conv.CS);
        return r + 2f * pivots + Conv.CS * 2f;
    }
}
