using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DreamGame.Core;
using Godot;
using Num = System.Numerics;

namespace DreamGame.Game;

/// <summary>
/// The campaign world's ground in the game (the shape comes from <see cref="WorldMap"/>):
/// <list type="bullet">
/// <item><b>Near you</b>, a 2 km square of ground at 1 m, the same kind of height grid the island uses, so shells,
/// wheels, sight lines, craters, the shovel and the dozer blade all work as they do there. When you've gone 320 m from
/// its middle a new square round you is put together in the background (from the old one where they overlap, and from
/// 64 m tiles of the world as made elsewhere) and swapped in. Ground you dug or blasted is kept when you leave it and
/// put back when you return. Beyond the square, questions about the ground go to the whole world's 20 m grid.</item>
/// <item><b>To the horizon</b>, the ground is drawn in four levels of detail: 64 m tiles at 1 m spacing within about
/// 440 m, then 256 m chunks at 4 m, 1 km chunks at 16 m and 4 km chunks at 64 m, each built on another thread as it's
/// needed. Skirts hanging from each chunk's edges hide the cracks where two levels meet.</item>
/// <item><b>Collision</b> is built only where something needs it: 64 m tiles round every vehicle, structure and you.</item>
/// </list>
/// </summary>
public partial class WorldGround : Node3D
{
    public static WorldGround Instance { get; private set; }
    public WorldMap Map { get; private set; }

    public const int Tile = 64, TileN = Tile + 1;
    private const int WinTiles = 32;
    public const int WinN = WinTiles * Tile + 1;
    private const float Recenter = 320f;

    // ------------------------------------------------------------------ the fine square near you

    private sealed class Window
    {
        public int X0, Z0;
        public float[] H, Orig, Dist, Worn;

        public bool Covers(int tx, int tz) =>
            tx >= X0 / Tile && tx < X0 / Tile + WinTiles && tz >= Z0 / Tile && tz < Z0 / Tile + WinTiles;

        public bool Holds(float x, float z) => x >= X0 && z >= Z0 && x <= X0 + WinN - 1 && z <= Z0 + WinN - 1;
    }

    /// <summary>Ground that was dug or blasted, kept for a 64 m tile while it's out of the square.</summary>
    private sealed class Edit
    {
        public float[] H, Dist, Worn;
    }

    private Window _win;
    private HeightField _field;
    private Earthworks _earth;
    private Task<Window> _job;
    private bool _jobHasSince;
    private int _sinceX0 = int.MaxValue, _sinceZ0 = int.MaxValue, _sinceX1 = int.MinValue, _sinceZ1 = int.MinValue;
    private readonly ConcurrentDictionary<(int, int), float[]> _made = new();
    private readonly Dictionary<(int, int), Edit> _edits = new();
    private readonly HashSet<(int, int)> _touched = new();

    private static int FloorDiv(float v, int d) => (int)MathF.Floor(v / d);

    /// <summary>A 64 m tile of the ground as the world made it (65 × 65 samples, 1 m apart).</summary>
    private float[] Made(int tx, int tz) => _made.GetOrAdd((tx, tz), k =>
    {
        var a = new float[TileN * TileN];
        for (int z = 0; z < TileN; z++)
        for (int x = 0; x < TileN; x++)
            a[z * TileN + x] = Map.HeightAt(k.Item1 * Tile + x, k.Item2 * Tile + z);
        return a;
    });

    /// <summary>Puts together the square with its corner at (<paramref name="x0"/>, <paramref name="z0"/>).</summary>
    private Window Prepare(int x0, int z0, Window old, Dictionary<(int, int), Edit> edits)
    {
        var w = new Window { X0 = x0, Z0 = z0, H = new float[WinN * WinN], Orig = new float[WinN * WinN], Dist = new float[WinN * WinN], Worn = new float[WinN * WinN] };
        int tx0 = x0 / Tile, tz0 = z0 / Tile;
        Parallel.For(0, WinTiles * WinTiles, k =>
        {
            int tx = tx0 + k % WinTiles, tz = tz0 + k / WinTiles;
            bool fromOld = old != null && old.Covers(tx, tz);
            float[] made = fromOld ? null : Made(tx, tz);
            edits.TryGetValue((tx, tz), out Edit e);
            for (int lz = 0; lz < TileN; lz++)
            {
                int gz = tz * Tile + lz - z0, gx = tx * Tile - x0;
                int i = gz * WinN + gx;
                if (fromOld)
                {
                    int oi = (gz + z0 - old.Z0) * WinN + gx + x0 - old.X0;
                    Array.Copy(old.H, oi, w.H, i, TileN);
                    Array.Copy(old.Orig, oi, w.Orig, i, TileN);
                    Array.Copy(old.Dist, oi, w.Dist, i, TileN);
                    Array.Copy(old.Worn, oi, w.Worn, i, TileN);
                    continue;
                }

                int li = lz * TileN;
                Array.Copy(made, li, w.Orig, i, TileN);
                Array.Copy(e?.H ?? made, li, w.H, i, TileN);
                if (e != null)
                {
                    Array.Copy(e.Dist, li, w.Dist, i, TileN);
                    Array.Copy(e.Worn, li, w.Worn, i, TileN);
                }
            }
        });
        return w;
    }

    private static Num.Vector2 CornerFor(Vector3 at) => new(FloorDiv(at.X - (WinN - 1) * 0.5f, Tile) * Tile, FloorDiv(at.Z - (WinN - 1) * 0.5f, Tile) * Tile);

    /// <summary>Starts putting a new square together round <paramref name="at"/>.</summary>
    private void StartJob(Vector3 at)
    {
        Num.Vector2 c = CornerFor(at);
        if (_win != null && _win.X0 == (int)c.X && _win.Z0 == (int)c.Y) return;
        Window old = _win;
        var edits = new Dictionary<(int, int), Edit>(_edits);
        // Tiles dug while out of any square aren't possible; tiles dug inside the old one are copied from it.
        _jobHasSince = false;
        _sinceX0 = _sinceZ0 = int.MaxValue;
        _sinceX1 = _sinceZ1 = int.MinValue;
        int x0 = (int)c.X, z0 = (int)c.Y;
        _job = Task.Run(() => Prepare(x0, z0, old, edits));
    }

    /// <summary>Swaps a finished square in: the ground's grid, its earthworks, and everything that asks them.</summary>
    private void Swap(Window w)
    {
        Window old = _win;
        if (old != null)
        {
            // Changes made while it was being put together.
            if (_jobHasSince)
                for (int z = Math.Max(_sinceZ0, w.Z0); z <= Math.Min(_sinceZ1, w.Z0 + WinN - 1); z++)
                for (int x = Math.Max(_sinceX0, w.X0); x <= Math.Min(_sinceX1, w.X0 + WinN - 1); x++)
                {
                    if (!old.Holds(x, z)) continue;
                    int i = (z - w.Z0) * WinN + x - w.X0, oi = (z - old.Z0) * WinN + x - old.X0;
                    w.H[i] = old.H[oi];
                    w.Dist[i] = old.Dist[oi];
                    w.Worn[i] = old.Worn[oi];
                }

            // Changed tiles leaving the square are kept.
            foreach ((int tx, int tz) in _touched.ToArray())
            {
                if (w.Covers(tx, tz) || !old.Covers(tx, tz)) continue;
                var e = new Edit { H = new float[TileN * TileN], Dist = new float[TileN * TileN], Worn = new float[TileN * TileN] };
                for (int lz = 0; lz < TileN; lz++)
                {
                    int oi = (tz * Tile + lz - old.Z0) * WinN + tx * Tile - old.X0;
                    Array.Copy(old.H, oi, e.H, lz * TileN, TileN);
                    Array.Copy(old.Dist, oi, e.Dist, lz * TileN, TileN);
                    Array.Copy(old.Worn, oi, e.Worn, lz * TileN, TileN);
                }

                _edits[(tx, tz)] = e;
                _touched.Remove((tx, tz));
            }
        }

        _win = w;
        _field = new HeightField(WinN, WinN, 1f, new Num.Vector2(w.X0, w.Z0), w.H, WorldMap.DeepSea) { Beyond = Map.Coarse };
        _earth = new Earthworks(_field, w.Orig, w.Dist, w.Worn);
        WorldQuery.Ground = _field;
        Terrain.UseWorld(_earth);
    }

    /// <summary>Whether the fine ground is ready at a point (after a jump, it takes a moment).</summary>
    public bool Ready(Vector3 at) => _win != null && _win.Holds(at.X, at.Z) && _job == null;

    /// <summary>You've jumped somewhere: the fine ground follows at once.</summary>
    public void JumpTo(Vector3 at)
    {
        if (_job != null) _job.Wait();
        if (_job != null && _job.IsCompletedSuccessfully) Swap(_job.Result);
        _job = null;
        StartJob(at);
    }

    // ------------------------------------------------------------------ building

    public static WorldGround Build(WorldMap map, Vector3 start)
    {
        var g = new WorldGround { Name = "WorldGround", Map = map };
        Instance = g;
        // The first square round where you start, before anything moves.
        Num.Vector2 c = CornerFor(start);
        g.Swap(g.Prepare((int)c.X, (int)c.Y, null, g._edits));
        g._body = new StaticBody3D { Name = "Terrain", CollisionLayer = GridRegistry.Layers.World, CollisionMask = 0 };
        g.AddChild(g._body);
        // A floor under everything, in case anything ever gets below the ground.
        g._body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D(), Position = new Vector3(0, WorldMap.DeepSea - 40f, 0) });
        g._material = Terrain.GroundMaterial();
        // The sea floor beyond the world's edge.
        g.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(90000, 90000) }, Position = new Vector3(0, WorldMap.DeepSea - 6f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.27f, 0.28f, 0.23f), Roughness = 1f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        for (int z = -3; z < 3; z++)
        for (int x = -3; x < 3; x++)
            g._roots.Add(new Chunk { Level = 3, X = x, Z = z });
        return g;
    }

    public override void _Ready() => SetPhysicsProcess(true);

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        _job?.Wait();
    }

    // ------------------------------------------------------------------ each frame

    private double _now;

    public override void _Process(double delta)
    {
        _now += delta;
        Vector3 eye = WorldQuery.Viewer;
        if (_job != null && _job.IsCompleted)
        {
            if (_job.IsCompletedSuccessfully) Swap(_job.Result);
            else GD.PrintErr("[world] building the ground failed: " + _job.Exception?.GetBaseException().Message);
            _job = null;
        }

        if (_job == null && _win != null)
        {
            float cx = _win.X0 + (WinN - 1) * 0.5f, cz = _win.Z0 + (WinN - 1) * 0.5f;
            if (MathF.Abs(eye.X - cx) > Recenter || MathF.Abs(eye.Z - cz) > Recenter) StartJob(eye);
        }

        foreach (Chunk r in _roots) Visit(r, eye);
        TakeMeshes();
        if ((int)(_now * 2) != (int)((_now - delta) * 2)) TrimMade(eye);
    }

    public override void _PhysicsProcess(double delta)
    {
        Flush();
        UpdateCollision();
    }

    /// <summary>Forgets tiles of the ground as made that are far from the square (they're quick to make again).</summary>
    private void TrimMade(Vector3 eye)
    {
        if (_made.Count < 3600) return;
        foreach (var key in _made.Keys.OrderByDescending(k => MathF.Abs(k.Item1 * Tile - eye.X) + MathF.Abs(k.Item2 * Tile - eye.Z)).Take(_made.Count - 3000).ToList())
            if (_win == null || !_win.Covers(key.Item1, key.Item2)) _made.TryRemove(key, out _);
    }

    /// <summary>Takes what the earthworks changed: collision at once, the look a little later, and tells anyone who cares.</summary>
    private void Flush()
    {
        if (_earth == null) return;
        GridRect worn = _earth.TakeWornDirty();
        if (!worn.IsEmpty) Changed(worn, false);
        if (!_earth.HasDirty) return;
        GridRect d = _earth.TakeDirty();
        Changed(d, true);
        Terrain.Notify(new Rect2(_win.X0 + d.X0, _win.Z0 + d.Z0, d.X1 - d.X0 + 1, d.Z1 - d.Z0 + 1));
    }

    private void Changed(GridRect r, bool shape)
    {
        int x0 = _win.X0 + r.X0, z0 = _win.Z0 + r.Z0, x1 = _win.X0 + r.X1, z1 = _win.Z0 + r.Z1;
        if (_job != null)
        {
            _jobHasSince = true;
            _sinceX0 = Math.Min(_sinceX0, x0);
            _sinceZ0 = Math.Min(_sinceZ0, z0);
            _sinceX1 = Math.Max(_sinceX1, x1);
            _sinceZ1 = Math.Max(_sinceZ1, z1);
        }

        for (int tz = FloorDiv(z0 - 1, Tile); tz <= FloorDiv(z1 + 1, Tile); tz++)
        for (int tx = FloorDiv(x0 - 1, Tile); tx <= FloorDiv(x1 + 1, Tile); tx++)
        {
            _touched.Add((tx, tz));
            if (shape && _cols.TryGetValue((tx, tz), out Col c)) c.Shape.MapData = TileHeights(tx, tz);
            if (_tiles.TryGetValue((tx, tz), out Chunk ch)) Rebuild(ch);
        }
    }

    // ------------------------------------------------------------------ collision near things that move

    private sealed class Col
    {
        public HeightMapShape3D Shape;
        public CollisionShape3D Node;
        public double Needed;
    }

    private StaticBody3D _body;
    private readonly Dictionary<(int, int), Col> _cols = new();
    private readonly List<(int, int, float)> _want = new();

    /// <summary>A 64 m tile's heights as they are now: from the square if it's there, otherwise as made (or as left).</summary>
    private float[] TileHeights(int tx, int tz)
    {
        var data = new float[TileN * TileN];
        if (_win != null && _win.Covers(tx, tz))
        {
            for (int z = 0; z < TileN; z++) Array.Copy(_win.H, (tz * Tile + z - _win.Z0) * WinN + tx * Tile - _win.X0, data, z * TileN, TileN);
            return data;
        }

        float[] src = _edits.TryGetValue((tx, tz), out Edit e) ? e.H : Made(tx, tz);
        Array.Copy(src, data, data.Length);
        return data;
    }

    private void UpdateCollision()
    {
        _want.Clear();
        void Need(Vector3 p, float r)
        {
            // Nothing high in the air needs the ground under it.
            if (p.Y - (WorldQuery.Ground?.HeightAt(p.X, p.Z) ?? 0f) > r + 20f) return;
            for (int tz = FloorDiv(p.Z - r, Tile); tz <= FloorDiv(p.Z + r, Tile); tz++)
            for (int tx = FloorDiv(p.X - r, Tile); tx <= FloorDiv(p.X + r, Tile); tx++)
            {
                float dx = MathF.Max(0, MathF.Max(tx * Tile - p.X, p.X - (tx + 1) * Tile));
                float dz = MathF.Max(0, MathF.Max(tz * Tile - p.Z, p.Z - (tz + 1) * Tile));
                _want.Add((tx, tz, MathF.Sqrt(dx * dx + dz * dz)));
            }
        }

        foreach (IGridHost h in GridRegistry.Hosts)
            if (h.Body != null && GodotObject.IsInstanceValid(h.Body) && h.Body.IsInsideTree())
                Need(h.Body.GlobalPosition, h.Radius + 80f);
        Need(WorldQuery.Viewer, 90f);
        if (PlayerAt.HasValue) Need(PlayerAt.Value, 90f);

        int built = 0;
        foreach ((int tx, int tz, float d) in _want.OrderBy(w => w.Item3))
        {
            if (_cols.TryGetValue((tx, tz), out Col c))
            {
                c.Needed = _now;
                continue;
            }

            // Under something: at once. Further out: a few a frame.
            if (d > 24f && built >= 6) continue;
            built++;
            var shape = new HeightMapShape3D { MapWidth = TileN, MapDepth = TileN, MapData = TileHeights(tx, tz) };
            var node = new CollisionShape3D { Shape = shape, Position = new Vector3(tx * Tile + Tile * 0.5f, 0, tz * Tile + Tile * 0.5f) };
            _body.AddChild(node);
            _cols[(tx, tz)] = new Col { Shape = shape, Node = node, Needed = _now };
        }

        foreach (var key in _cols.Where(kv => _now - kv.Value.Needed > 4.0).Select(kv => kv.Key).ToList())
        {
            _cols[key].Node.QueueFree();
            _cols.Remove(key);
        }
    }

    /// <summary>Where you are on foot (collision is kept round you), set by the range each frame.</summary>
    public static Vector3? PlayerAt;

    // ------------------------------------------------------------------ drawing: four levels of detail

    private sealed class Chunk
    {
        public int Level, X, Z;
        public MeshInstance3D Mesh;
        public bool Building, Built, Shown;
        public int Version, BuiltVersion = -1;
        public Chunk[] Kids;
        public float MinY = -200f, MaxY = 1200f;
        public bool Dead;
    }

    private static readonly float[] Size = { 64f, 256f, 1024f, 4096f };
    private static readonly float[] Step = { 1f, 4f, 16f, 64f };

    /// <summary>A chunk of a level splits into the finer level's chunks when you're nearer than this.</summary>
    private static readonly float[] SplitAt = { 0f, 440f, 1600f, 6500f };

    private readonly List<Chunk> _roots = new();
    private readonly Dictionary<(int, int), Chunk> _tiles = new();
    private readonly ConcurrentQueue<(Chunk chunk, int version, MeshArrays arrays)> _done = new();
    private int _running;
    private ShaderMaterial _material;

    private sealed class MeshArrays
    {
        public Vector3[] V, N;
        public Color[] C;
        public Vector2[] Uv, Uv2;
        public int[] I;
        public float MinY, MaxY;
    }

    private static float Distance(Vector3 eye, Chunk c)
    {
        float s = Size[c.Level];
        float x0 = c.X * s, z0 = c.Z * s;
        float dx = MathF.Max(0, MathF.Max(x0 - eye.X, eye.X - (x0 + s)));
        float dz = MathF.Max(0, MathF.Max(z0 - eye.Z, eye.Z - (z0 + s)));
        float dy = MathF.Max(0, MathF.Max(c.MinY - eye.Y, eye.Y - c.MaxY));
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private void Visit(Chunk c, Vector3 eye)
    {
        float d = Distance(eye, c);
        if (c.Level > 0 && d < SplitAt[c.Level])
        {
            c.Kids ??= MakeKids(c);
            bool ready = true;
            foreach (Chunk k in c.Kids)
            {
                Request(k, d);
                ready &= k.Built;
            }

            if (ready)
            {
                Show(c, false);
                foreach (Chunk k in c.Kids) Visit(k, eye);
                return;
            }

            Request(c, d);
            Show(c, true);
            foreach (Chunk k in c.Kids) HideAll(k);
            return;
        }

        if (c.Kids != null && (c.Level == 0 || d > SplitAt[c.Level] * 1.15f || !c.Built))
        {
            if (c.Built)
            {
                foreach (Chunk k in c.Kids) Free(k);
                c.Kids = null;
            }
            else
            {
                // Not drawn yet: keep the finer chunks showing until it is.
                Request(c, d);
                foreach (Chunk k in c.Kids) Visit(k, eye);
                return;
            }
        }
        else if (c.Kids != null && c.Kids.All(k => k.Built))
        {
            // Between splitting and joining: stay as it is.
            Show(c, false);
            foreach (Chunk k in c.Kids) Visit(k, eye);
            return;
        }

        Request(c, d);
        Show(c, true);
    }

    private Chunk[] MakeKids(Chunk c)
    {
        var kids = new Chunk[16];
        for (int i = 0; i < 16; i++)
        {
            var k = new Chunk { Level = c.Level - 1, X = c.X * 4 + i % 4, Z = c.Z * 4 + i / 4, MinY = c.MinY, MaxY = c.MaxY };
            kids[i] = k;
            if (k.Level == 0) _tiles[(k.X, k.Z)] = k;
        }

        return kids;
    }

    private void Show(Chunk c, bool on)
    {
        c.Shown = on;
        if (c.Mesh != null) c.Mesh.Visible = on;
    }

    private void HideAll(Chunk c)
    {
        Show(c, false);
        if (c.Kids != null)
            foreach (Chunk k in c.Kids) HideAll(k);
    }

    private void Free(Chunk c)
    {
        c.Dead = true;
        if (c.Kids != null)
            foreach (Chunk k in c.Kids) Free(k);
        c.Kids = null;
        c.Mesh?.QueueFree();
        c.Mesh = null;
        if (c.Level == 0 && _tiles.TryGetValue((c.X, c.Z), out Chunk t) && t == c) _tiles.Remove((c.X, c.Z));
    }

    private void Rebuild(Chunk c)
    {
        c.Version++;
        Request(c, 0f);
    }

    private readonly List<(Chunk chunk, float d)> _queue = new();

    private void Request(Chunk c, float d)
    {
        if (c.Building || c.Dead || c.BuiltVersion == c.Version) return;
        c.Building = true;
        _queue.Add((c, d));
    }

    /// <summary>Starts the nearest waiting chunks building, and puts finished ones on screen.</summary>
    private void TakeMeshes()
    {
        if (_queue.Count > 0 && _running < 6)
        {
            _queue.Sort((a, b) => a.d.CompareTo(b.d));
            int start = Math.Min(_queue.Count, 6 - _running);
            for (int i = 0; i < start; i++)
            {
                Chunk c = _queue[i].chunk;
                int version = c.Version;
                float[] fine = null, d0 = null, w0 = null;
                if (c.Level == 0) fine = Snapshot(c.X, c.Z, out d0, out w0);
                System.Threading.Interlocked.Increment(ref _running);
                Task.Run(() =>
                {
                    try
                    {
                        _done.Enqueue((c, version, Arrays(c, fine, d0, w0)));
                    }
                    catch (Exception e)
                    {
                        GD.PrintErr("[world] chunk failed: " + e.Message);
                        _done.Enqueue((c, version, null));
                    }
                    finally
                    {
                        System.Threading.Interlocked.Decrement(ref _running);
                    }
                });
            }

            _queue.RemoveRange(0, start);
        }

        for (int n = 0; n < 12 && _done.TryDequeue(out var r); n++)
        {
            Chunk c = r.chunk;
            c.Building = false;
            if (c.Dead || r.arrays == null) continue;
            if (r.version != c.Version)
            {
                Request(c, 0f);
                continue;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = r.arrays.V;
            arrays[(int)Mesh.ArrayType.Normal] = r.arrays.N;
            arrays[(int)Mesh.ArrayType.Color] = r.arrays.C;
            arrays[(int)Mesh.ArrayType.TexUV] = r.arrays.Uv;
            arrays[(int)Mesh.ArrayType.TexUV2] = r.arrays.Uv2;
            arrays[(int)Mesh.ArrayType.Index] = r.arrays.I;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            if (c.Mesh == null)
            {
                c.Mesh = new MeshInstance3D
                {
                    MaterialOverride = _material, Visible = c.Shown,
                    CastShadow = c.Level <= 1 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(c.Mesh);
            }

            c.Mesh.Mesh = mesh;
            c.MinY = r.arrays.MinY;
            c.MaxY = r.arrays.MaxY;
            c.Built = true;
            c.BuiltVersion = r.version;
        }
    }

    /// <summary>A 64 m tile's heights, earth turned and wear, with a sample of apron all round, copied from the square.</summary>
    private float[] Snapshot(int tx, int tz, out float[] dist, out float[] worn)
    {
        const int n = TileN + 2;
        var h = new float[n * n];
        dist = new float[n * n];
        worn = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int gx = tx * Tile + x - 1, gz = tz * Tile + z - 1;
            if (_win != null && _win.Holds(gx, gz))
            {
                int i = (gz - _win.Z0) * WinN + gx - _win.X0;
                h[z * n + x] = _win.H[i];
                dist[z * n + x] = _win.Dist[i];
                worn[z * n + x] = _win.Worn[i];
            }
            else h[z * n + x] = float.NaN; // filled from the world on the worker
        }

        return h;
    }

    /// <summary>A chunk's mesh: 65 × 65 points with normals, what covers the ground at each, and a skirt round its edge.</summary>
    private MeshArrays Arrays(Chunk c, float[] fine, float[] dist, float[] worn)
    {
        const int m = TileN, n = m + 2;
        float step = Step[c.Level], x0 = c.X * Size[c.Level], z0 = c.Z * Size[c.Level];
        var h = new float[n * n];
        var info = new GroundInfo[m * m];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float wx = x0 + (x - 1) * step, wz = z0 + (z - 1) * step;
            bool inner = x >= 1 && z >= 1 && x <= m && z <= m;
            float made = inner ? Map.Sample(wx, wz, out info[(z - 1) * m + x - 1]) : float.NaN;
            float have = fine != null ? fine[z * n + x] : float.NaN;
            h[z * n + x] = !float.IsNaN(have) ? have : !float.IsNaN(made) ? made : Map.HeightAt(wx, wz);
        }

        int skirt = 4 * (m - 1);
        var a = new MeshArrays
        {
            V = new Vector3[m * m + skirt], N = new Vector3[m * m + skirt], C = new Color[m * m + skirt],
            Uv = new Vector2[m * m + skirt], Uv2 = new Vector2[m * m + skirt], MinY = float.MaxValue, MaxY = float.MinValue,
        };
        for (int z = 0; z < m; z++)
        for (int x = 0; x < m; x++)
        {
            int hi = (z + 1) * n + x + 1, vi = z * m + x;
            float y = h[hi];
            a.V[vi] = new Vector3(x0 + x * step, y, z0 + z * step);
            var nrm = new Vector3(h[hi - 1] - h[hi + 1], 2f * step, h[hi - n] - h[hi + n]).Normalized();
            a.N[vi] = nrm;
            GroundCover cov = WorldMap.Cover(info[vi], y, 1f - nrm.Y);
            float dirt = cov.Dirt, turned = 0f;
            if (dist != null)
            {
                float rut = worn[hi];
                dirt = MathF.Max(dirt, rut * 0.9f);
                turned = MathF.Max(dist[hi] * 0.85f, (rut - 0.5f) * 0.6f);
            }

            a.C[vi] = new Color(dirt, cov.Rock, cov.Sand, turned);
            a.Uv[vi] = new Vector2(cov.Snow, cov.Gravel);
            a.Uv2[vi] = new Vector2(cov.RockKind, cov.Dry);
            a.MinY = MathF.Min(a.MinY, y);
            a.MaxY = MathF.Max(a.MaxY, y);
        }

        // The skirt: the edge points again, hung down, so a coarser neighbour's edge never shows a gap.
        float drop = step * 1.5f + 3f;
        int s = m * m;
        var ring = new List<int>(skirt);
        for (int x = 0; x < m - 1; x++) ring.Add(x);
        for (int z = 0; z < m - 1; z++) ring.Add(z * m + m - 1);
        for (int x = m - 1; x > 0; x--) ring.Add((m - 1) * m + x);
        for (int z = m - 1; z > 0; z--) ring.Add(z * m);
        for (int k = 0; k < ring.Count; k++)
        {
            int src = ring[k];
            a.V[s + k] = a.V[src] + Vector3.Down * drop;
            a.N[s + k] = a.N[src];
            a.C[s + k] = a.C[src];
            a.Uv[s + k] = a.Uv[src];
            a.Uv2[s + k] = a.Uv2[src];
        }

        var idx = new List<int>((m - 1) * (m - 1) * 6 + skirt * 6);
        for (int z = 0; z < m - 1; z++)
        for (int x = 0; x < m - 1; x++)
        {
            int i0 = z * m + x, i1 = i0 + 1, i2 = i0 + m, i3 = i2 + 1;
            idx.Add(i0);
            idx.Add(i1);
            idx.Add(i2);
            idx.Add(i1);
            idx.Add(i3);
            idx.Add(i2);
        }

        for (int k = 0; k < ring.Count; k++)
        {
            int t0 = ring[k], t1 = ring[(k + 1) % ring.Count], b0 = s + k, b1 = s + (k + 1) % ring.Count;
            idx.Add(t0);
            idx.Add(b0);
            idx.Add(t1);
            idx.Add(t1);
            idx.Add(b0);
            idx.Add(b1);
        }

        a.I = idx.ToArray();
        return a;
    }
}
