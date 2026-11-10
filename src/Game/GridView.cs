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
public sealed partial class GridView
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
        public (int, Int3) Key;
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
    private readonly Livery _livery;
    private readonly float? _waterline;
    private readonly Transform3D[] _toBody;
    private bool _toBodyDirty = true;
    private ulong _bodyFrame = ulong.MaxValue;
    private Transform3D _bodyXf;

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

        public BlockShape ShapeAt(Int3 c, out int orientation)
        {
            BlockInstance b = At(c);
            orientation = b?.Orientation ?? 0;
            return b?.Def.Shape ?? BlockShape.Cube;
        }
    }

    /// <summary>A stable number per block, so each panel gets its own slight tint.</summary>
    public static int Panel(Int3 anchor) => anchor.X * 73856093 ^ anchor.Y * 19349663 ^ anchor.Z * 83492791;

    private int SurfaceIndex(BlockDef def)
    {
        def = BlockModels.SurfaceDefOf(def);
        if (_surfaceIndex.TryGetValue(def, out int i)) return i;
        _surfaceDefs.Add(def);
        return _surfaceIndex[def] = _surfaceDefs.Count - 1;
    }

    /// <param name="shareKey">Grids made from the same design pass the same key (the blueprint) and share their meshes.</param>
    /// <param name="lod">Draw a simpler far model at a distance (vehicles and structures; not short-lived wreckage).</param>
    /// <param name="livery">The paint job (null or factory for the blocks' own colors).</param>
    /// <param name="waterline">For ships: the waterline's height in the vehicle's frame, for the ship paint rules.</param>
    public GridView(PhysicsBody3D body, GridModel model, Func<Int3, float> caliberOf = null, object shareKey = null, bool lod = false,
        Livery livery = null, float? waterline = null)
    {
        Body = body;
        Model = model;
        _caliberOf = caliberOf;
        _livery = livery != null && !livery.IsFactory ? livery : null;
        _waterline = _livery != null ? waterline : null;
        _sections = new Node3D[model.Sections.Count];
        _syncedAngle = new float[model.Sections.Count];
        _toBody = new Transform3D[model.Sections.Count];
        foreach (Section s in model.Sections)
        {
            var node = new Node3D { Name = $"{s.Kind}{s.Id}" };
            Node parent = s.Parent < 0 ? body : _sections[s.Parent];
            parent.AddChild(node);
            node.Transform = Conv.SectionLocal(s);
            _sections[s.Id] = node;
        }

        SetupLod(shareKey, lod);
        _building = true;
        foreach (BlockInstance b in model.Instances) Place(b);
        Flush();
        _building = false;
        BuildAllFar();
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
            chunk = new Chunk { Section = b.Section, Key = key };
            _chunks[key] = chunk;
        }

        chunk.Blocks.Add(b);
        _chunkOf[b] = chunk;

        var nodes = new BlockNodes();
        if (BlockVisuals.IsAnimated(b.Def))
        {
            nodes.Visual = BlockVisuals.Create(b.Def, CaliberOf(b), livery: _livery);
            nodes.Visual.Transform = Conv.BlockTransform(b.Cell, b.Orientation);
            // Axles out to whatever the wheel hangs from (they move with the wheel). Wheels turn with the body's
            // axes whatever way they were placed, so their neighbors are looked for unturned.
            foreach (ModelPart jp in Joints.For(b.Def, b.Cell, b.Def.IsWheelLike ? 0 : b.Orientation, CaliberOf(b), DefAt(b.Section)))
            {
                (Mesh jm, Material jmat, Transform3D jx, _) = BlockVisuals.PartOf(b.Def, jp);
                nodes.Visual.AddChild(new MeshInstance3D { Mesh = jm, MaterialOverride = jmat, Transform = jx });
            }

            ApplyNear(nodes.Visual, NearEnd(b.Section, Tier.Normal));
            _sections[b.Section].AddChild(nodes.Visual);
            FarChanged(b.Section, chunk);
        }
        else
        {
            chunk.VisualDirty = true;
        }

        if (b.Def.HasCollider && b.Def.Shape != BlockShape.Cube)
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
        else FarChanged(b.Section, chunk);
        TouchNeighbors(b);
        if (b.Def.HasCollider && b.Def.Shape == BlockShape.Cube) chunk.ColliderDirty = true;
        _dirty.Add(chunk);
        QueueFlush();
    }

    /// <summary>True if every face of the block is against solid, opaque plate (armor or frame cubes, not glass).</summary>
    private bool Enclosed(BlockInstance b)
    {
        Dictionary<Int3, BlockInstance> cells = Model.Sections[b.Section].Cells;
        foreach (Int3 c in b.Cells)
        foreach (Int3 d in Int3.Directions)
        {
            Int3 n = c + d;
            if (!cells.TryGetValue(n, out BlockInstance o) || !o.Alive) return false;
            if (o == b) continue;
            if (o.Def.Kind is not (BlockKind.Armor or BlockKind.Frame) || o.Def.Glass || o.Def.Shape != BlockShape.Cube) return false;
        }

        return true;
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
        _nearNodes.RemoveAll(n => !GodotObject.IsInstanceValid(n.g));
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

        // A vehicle fresh from a design it shares with others reuses their meshes.
        if (_building && _shared != null && _shared.Chunks.TryGetValue(c.Key, out List<Piece> pieces))
        {
            foreach (Piece piece in pieces)
            {
                GeometryInstance3D g = piece.Instance();
                ApplyNear(g, NearEnd(c.Section, piece.Tier));
                c.Node.AddChild(g);
            }

            return;
        }

        long started = Perf.Now;
        FarChanged(c.Section, c);
        List<Piece> made = _building && _shared != null ? new List<Piece>() : null;
        var groups = new Dictionary<(Mesh, Material, Tier), List<(Transform3D xf, int o)>>();
        var surface = new List<Int3>();
        foreach (BlockInstance b in c.Blocks)
        {
            if (!b.Alive || BlockVisuals.IsAnimated(b.Def)) continue;
            if (BlockModels.IsSurface(b.Def))
            {
                surface.AddRange(b.Cells);
                continue;
            }

            // Machinery walled in on every side by solid plate can't be seen from anywhere: not drawn. It
            // comes back as soon as a neighbor is shot away.
            if (Enclosed(b)) continue;

            Transform3D at = Conv.BlockTransform(b.Cell, b.Orientation);
            Tier blockTier = TierOf(b.Def);
            void AddPart(Mesh mesh, Material own, Transform3D local, Paint finish)
            {
                Material mat = _livery != null && Liveries.Paints(b.Def, finish) ? Paintwork.Part(b.Def, _livery, finish == Paint.SelfDark, _waterline) : own;
                // Small parts (bolts, rails, handles) and the things inside drop out sooner than the rest.
                Tier tier = blockTier == Tier.Normal && IsSmall(mesh, local) ? Tier.Small : blockTier;
                if (!groups.TryGetValue((mesh, mat, tier), out List<(Transform3D, int)> list)) groups[(mesh, mat, tier)] = list = new List<(Transform3D, int)>();
                list.Add((at * local, b.Orientation));
            }

            foreach ((Mesh mesh, Material own, Transform3D local, Paint finish) in BlockVisuals.PartsOf(b.Def, CaliberOf(b)))
                AddPart(mesh, own, local, finish);
            foreach (ModelPart jp in Joints.For(b.Def, b.Cell, b.Orientation, CaliberOf(b), DefAt(c.Section)))
            {
                (Mesh jm, Material jmat, Transform3D jx, Paint jf) = BlockVisuals.PartOf(b.Def, jp);
                AddPart(jm, jmat, jx, jf);
            }
        }

        foreach (((Mesh mesh, Material mat, Tier tier), List<(Transform3D xf, int o)> xfs) in groups)
        {
            // Painted parts carry their own position, so each takes the pattern's color where it is.
            bool painted = mat is ShaderMaterial;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = painted, Mesh = mesh, InstanceCount = xfs.Count };
            for (int i = 0; i < xfs.Count; i++)
            {
                mm.SetInstanceTransform(i, xfs[i].xf);
                if (painted) mm.SetInstanceCustomData(i, Paintwork.PartData(xfs[i].xf, xfs[i].o));
            }

            var piece = new Piece
            {
                Res = mm, Mat = mat, Tier = tier,
                // Small parts' and interior items' shadows can't be made out; leaving them out of the shadow
                // passes saves drawing them again for every shadow split.
                ShadowOff = tier != Tier.Normal || mat is StandardMaterial3D sm && sm.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            made?.Add(piece);
            GeometryInstance3D g = piece.Instance();
            ApplyNear(g, NearEnd(c.Section, tier));
            c.Node.AddChild(g);
        }

        if (surface.Count > 0)
            foreach ((int mat, HullMesher.Surface s) in HullMesher.Build(surface, new SectionGrid(this, c.Section), Conv.CS))
            {
                if (s.Indices.Count == 0) continue;
                var piece = new Piece { Res = BlockVisuals.HullMesh(s), Mat = PlatingMaterial(_surfaceDefs[mat]) };
                made?.Add(piece);
                GeometryInstance3D g = piece.Instance();
                ApplyNear(g, NearEnd(c.Section, Tier.Normal));
                c.Node.AddChild(g);
            }

        if (made != null) _shared.Chunks[c.Key] = made;
        Perf.Add(Perf.Visuals, started);
    }

    /// <summary>What's in a cell of a section (for working out joints), or null.</summary>
    private Func<Int3, BlockDef> DefAt(int section)
    {
        Dictionary<Int3, BlockInstance> cells = Model.Sections[section].Cells;
        return c => cells.TryGetValue(c, out BlockInstance x) && x.Alive ? x.Def : null;
    }

    /// <summary>The material for a merged surface: glass as framed panes, plating in the paint job, or the block's own.</summary>
    private Material PlatingMaterial(BlockDef def)
    {
        if (def.Glass) return Paintwork.Glass(def, _livery, plating: true);
        return _livery != null ? Paintwork.Plating(def, _livery, _waterline) : BlockVisuals.SurfaceMaterial(def);
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
            if (b.Alive && b.Def.HasCollider && b.Def.Shape == BlockShape.Cube)
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
        if (s.Angle == angle) return;
        s.Angle = angle;
        _sections[sectionId].Transform = Conv.SectionLocal(s);
        _toBodyDirty = true;
    }

    /// <summary>
    /// Where a section is in the world, worked out in C# from the body's place at the start of this physics
    /// step and the turret angles, without asking the engine for every section. Good for shell and blast tests.
    /// </summary>
    public Transform3D SectionWorldFast(int id)
    {
        ulong f = Engine.GetPhysicsFrames();
        if (f != _bodyFrame)
        {
            _bodyFrame = f;
            _bodyXf = Body.GlobalTransform;
        }

        if (_toBodyDirty)
        {
            _toBodyDirty = false;
            foreach (Section s in Model.Sections)
                _toBody[s.Id] = (s.Parent >= 0 ? _toBody[s.Parent] : Transform3D.Identity) * Conv.SectionLocal(s);
        }

        return _bodyXf * _toBody[id];
    }

    /// <summary>Whether a ray passes close enough to a section's blocks to be worth walking through them.</summary>
    public bool RayNearSection(Section s, in Transform3D sectionWorld, Vector3 from, Vector3 dir, float len)
    {
        Vector3 lo = s.Min.G() * Conv.CS, hi = s.Max.G() * Conv.CS;
        Vector3 center = sectionWorld * ((lo + hi) * 0.5f);
        float radius = (hi - lo).Length() * 0.5f + Conv.CS * 1.5f;
        float t = Mathf.Clamp((center - from).Dot(dir), 0f, len);
        return (from + dir * t).DistanceSquaredTo(center) <= radius * radius;
    }

    /// <summary>
    /// Moves turret colliders to match the visuals. Physics engines rebuild a body's shape when a collider
    /// moves, so this only happens once a section has turned a few degrees.
    /// </summary>
    private bool[] _syncDirty;

    public void SyncColliders(bool force = false)
    {
        _syncDirty ??= new bool[Model.Sections.Count];
        bool[] dirty = _syncDirty;
        System.Array.Clear(dirty);
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
        return Model.MaxReach() * Conv.CS + Conv.CS * 2f;
    }
}
