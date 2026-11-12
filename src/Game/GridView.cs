using System;
using System.Collections.Generic;
using System.Linq;
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
        public bool VisualDirty;
    }

    /// <summary>The blocks whose collision is merged into boxes together (see <see cref="ColliderBoxes"/>).</summary>
    private sealed class ColliderGroup
    {
        public int Section;
        public readonly HashSet<BlockInstance> Blocks = new();
        public readonly List<(CollisionShape3D shape, Transform3D local)> Colliders = new();
    }

    private readonly Dictionary<(int, Int3), ColliderGroup> _colGroups = new();
    private readonly Dictionary<BlockInstance, ColliderGroup> _colOf = new();
    private readonly HashSet<ColliderGroup> _colDirty = new();

    /// <summary>Per section: big enough that its boxes merge over bigger groups.</summary>
    private bool[] _bigCollision;

    /// <summary>Slopes, wedges and corners collide as whole cells (see <see cref="ColliderBoxes.Merges"/>).</summary>
    private readonly bool _boxSlopes;

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
        Livery livery = null, float? waterline = null, float wear = 0.7f, float mud = 0.5f, bool boxSlopes = false)
    {
        _boxSlopes = boxSlopes;
        _wear = wear;
        _mud = mud;
        Body = body;
        Model = model;
        _caliberOf = caliberOf;
        _livery = livery != null && !livery.IsFactory ? livery : null;
        _waterline = _livery != null ? waterline : null;
        _sections = new Node3D[model.Sections.Count];
        _syncedAngle = new float[model.Sections.Count];
        _bigCollision = new bool[model.Sections.Count];
        foreach (Section s in model.Sections)
            _bigCollision[s.Id] = s.Cells.Count > 0 && ColliderBoxes.IsBig(s.Min, s.Max);
        _heavyCollision = _bigCollision.Any(x => x);
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
        foreach (Chunk c in _chunks.Values) batches += c.Node?.GetChildCount() ?? 0;
        foreach (ColliderGroup g in _colGroups.Values) shapes += g.Colliders.Count;

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

        bool big = _bigCollision[b.Section];
        if (ColliderBoxes.Merges(b.Def, _boxSlopes || big))
        {
            var gk = (b.Section, ColliderBoxes.GroupOf(b.Cell, big));
            if (!_colGroups.TryGetValue(gk, out ColliderGroup g)) _colGroups[gk] = g = new ColliderGroup { Section = b.Section };
            g.Blocks.Add(b);
            _colOf[b] = g;
            _colDirty.Add(g);
        }
        else if (b.Def.HasCollider)
        {
            nodes.Collider = new CollisionShape3D { Shape = BlockVisuals.ShapeOf(b.Def) };
            nodes.Collider.Transform = SectionToBody(b.Section) * Conv.BlockTransform(b.Cell, b.Orientation);
            Body.AddChild(nodes.Collider);
        }

        if (nodes.Visual != null || nodes.Collider != null) _nodes[b] = nodes;
        if (chunk.VisualDirty) _dirty.Add(chunk);
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

        if (_colOf.Remove(b, out ColliderGroup group))
        {
            group.Blocks.Remove(b);
            _colDirty.Add(group);
        }

        if (!_chunkOf.Remove(b, out Chunk chunk))
        {
            QueueFlush();
            return;
        }

        chunk.Blocks.Remove(b);
        if (!BlockVisuals.IsAnimated(b.Def)) chunk.VisualDirty = true;
        else FarChanged(b.Section, chunk);
        TouchNeighbors(b);
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
            c.VisualDirty = false;
        }

        _dirty.Clear();
        foreach (ColliderGroup g in _colDirty) RebuildColliders(g);
        _colDirty.Clear();
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
                GeometryInstance3D g = Weathered(piece.Instance());
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

        // Every part that shares a material and a detail tier is baked into one mesh, already in place: one draw
        // call per material (and one per shadow split) instead of one per kind of part. A battleship near the camera
        // was about 2,700 batches this way.
        var byMaterial = new Dictionary<(Material, Tier), List<(Mesh, List<(Transform3D, int)>)>>();
        foreach (((Mesh mesh, Material mat, Tier tier), List<(Transform3D xf, int o)> xfs) in groups)
        {
            if (!byMaterial.TryGetValue((mat, tier), out var list)) byMaterial[(mat, tier)] = list = new List<(Mesh, List<(Transform3D, int)>)>();
            list.Add((mesh, xfs));
        }

        foreach (((Material mat, Tier tier), var parts) in byMaterial)
        {
            ArrayMesh baked = Bake(parts);
            if (baked == null) continue;
            var piece = new Piece
            {
                Res = baked, Mat = mat, Tier = tier,
                // Small parts' and interior items' shadows can't be made out; leaving them out of the shadow
                // passes saves drawing them again for every shadow split.
                ShadowOff = tier != Tier.Normal || mat is StandardMaterial3D sm && sm.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            made?.Add(piece);
            GeometryInstance3D g = Weathered(piece.Instance());
            ApplyNear(g, NearEnd(c.Section, tier));
            c.Node.AddChild(g);
        }

        if (surface.Count > 0)
            foreach ((int mat, HullMesher.Surface s) in HullMesher.Build(surface, new SectionGrid(this, c.Section), Conv.CS))
            {
                if (s.Indices.Count == 0) continue;
                var piece = new Piece { Res = BlockVisuals.HullMesh(s), Mat = PlatingMaterial(_surfaceDefs[mat]) };
                made?.Add(piece);
                GeometryInstance3D g = Weathered(piece.Instance());
                ApplyNear(g, NearEnd(c.Section, Tier.Normal));
                c.Node.AddChild(g);
            }

        if (made != null) _shared.Chunks[c.Key] = made;
        Perf.Add(Perf.Visuals, started);
    }

    private readonly float _wear, _mud;

    private static readonly Dictionary<Mesh, (Vector3[] v, Vector3[] n, Color[] c, int[] i)> BakeData = new();

    /// <summary>A mesh's vertices, normals, colours and triangles (all its surfaces together), kept for baking.</summary>
    private static (Vector3[] v, Vector3[] n, Color[] c, int[] i) DataOf(Mesh mesh)
    {
        if (BakeData.TryGetValue(mesh, out var d)) return d;
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var c = new List<Color>();
        var idx = new List<int>();
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arr = mesh.SurfaceGetArrays(s);
            Vector3[] sv = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector3[] sn = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
            Color[] sc = arr[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? Array.Empty<Color>() : arr[(int)Mesh.ArrayType.Color].AsColorArray();
            int[] si = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? Array.Empty<int>() : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
            int b = v.Count;
            v.AddRange(sv);
            for (int k = 0; k < sv.Length; k++)
            {
                n.Add(k < sn.Length ? sn[k] : Vector3.Up);
                c.Add(k < sc.Length ? sc[k] : Colors.White);
            }

            if (si.Length == 0)
                for (int k = 0; k < sv.Length; k++) idx.Add(b + k);
            else
                foreach (int k in si) idx.Add(b + k);
        }

        return BakeData[mesh] = (v.ToArray(), n.ToArray(), c.ToArray(), idx.ToArray());
    }

    /// <summary>Copies every placed instance of each mesh into one mesh in the section's frame.</summary>
    private static ArrayMesh Bake(List<(Mesh mesh, List<(Transform3D xf, int o)> xfs)> parts)
    {
        int nv = 0, ni = 0;
        foreach ((Mesh mesh, List<(Transform3D, int)> xfs) in parts)
        {
            var d = DataOf(mesh);
            nv += d.v.Length * xfs.Count;
            ni += d.i.Length * xfs.Count;
        }

        if (nv == 0 || ni == 0) return null;
        var verts = new Vector3[nv];
        var norms = new Vector3[nv];
        var cols = new Color[nv];
        var idx = new int[ni];
        int vo = 0, io = 0;
        foreach ((Mesh mesh, List<(Transform3D xf, int o)> xfs) in parts)
        {
            var d = DataOf(mesh);
            foreach ((Transform3D xf, _) in xfs)
            {
                Basis nb = xf.Basis.Inverse().Transposed();
                for (int k = 0; k < d.v.Length; k++)
                {
                    verts[vo + k] = xf * d.v[k];
                    norms[vo + k] = (nb * d.n[k]).Normalized();
                    cols[vo + k] = d.c[k];
                }

                for (int k = 0; k < d.i.Length; k++) idx[io + k] = vo + d.i[k];
                vo += d.v.Length;
                io += d.i.Length;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = norms;
        arrays[(int)Mesh.ArrayType.Color] = cols;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh2 = new ArrayMesh();
        mesh2.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh2;
    }

    /// <summary>How worn and how muddy this machine is (the shader's per-instance settings).</summary>
    private GeometryInstance3D Weathered(GeometryInstance3D g)
    {
        if (g.MaterialOverride is ShaderMaterial)
        {
            g.SetInstanceShaderParameter("wear_amount", _wear);
            g.SetInstanceShaderParameter("mud_amount", _mud);
        }

        return g;
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
        return _livery != null && !def.Unpainted ? Paintwork.Plating(def, _livery, _waterline) : BlockVisuals.SurfaceMaterial(def);
    }

    /// <summary>Merges the chunk's solid cells into boxes: runs along X, then rows along Z, then layers along Y.</summary>
    private void RebuildColliders(ColliderGroup g)
    {
        foreach ((CollisionShape3D shape, _) in g.Colliders)
        {
            if (!GodotObject.IsInstanceValid(shape)) continue;
            Body.RemoveChild(shape);
            shape.QueueFree();
        }

        g.Colliders.Clear();
        var solid = new List<Int3>();
        foreach (BlockInstance b in g.Blocks)
            if (b.Alive)
                solid.AddRange(b.Cells);
        if (solid.Count == 0) return;
        Transform3D toBody = SectionToBody(g.Section);
        foreach ((Int3 min, Int3 size) in ColliderBoxes.Merge(solid))
        {
            Vector3 extent = new Vector3(size.X, size.Y, size.Z) * Conv.CS;
            Vector3 center = (min.G() + new Vector3(size.X - 1, size.Y - 1, size.Z - 1) * 0.5f) * Conv.CS;
            var local = new Transform3D(Basis.Identity, center);
            var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = extent }, Transform = toBody * local };
            Body.AddChild(shape);
            g.Colliders.Add((shape, local));
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
    private bool _heavyCollision;
    private ulong _lastSyncMs;
    private const ulong HeavySyncMs = 400;

    public void SyncColliders(bool force = false)
    {
        // Every collider move makes the physics engine rebuild the body's whole shape. A big machine has hundreds
        // of boxes and dozens of turrets tracking targets, so it catches its turret colliders up a few times a
        // second instead of nearly every step (they trail the guns by a fraction of a second; nothing notices).
        if (!force && _heavyCollision)
        {
            ulong now = Time.GetTicksMsec();
            if (now - _lastSyncMs < HeavySyncMs) return;
            _lastSyncMs = now;
        }

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
        foreach (ColliderGroup g in _colGroups.Values)
        {
            if (!dirty[g.Section]) continue;
            Transform3D toBody = SectionToBody(g.Section);
            foreach ((CollisionShape3D shape, Transform3D local) in g.Colliders)
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
