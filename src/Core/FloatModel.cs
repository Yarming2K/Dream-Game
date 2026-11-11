using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>
/// What a hull floats on, grouped into bricks so that even a 300 m ship costs only a few thousand pieces
/// per physics tick. Each brick knows how much solid material it holds and how much sealed air from each
/// compartment; flood water fills each compartment layer by layer from the bottom. Small vehicles get
/// one-cell bricks, which is exact.
/// </summary>
public sealed class FloatModel
{
    public sealed class Brick
    {
        /// <summary>Middle of the brick's contents, in meters, design frame.</summary>
        public Vector3 Center;
        public float HeightM;

        /// <summary>Volume of block material (m³), counted again whenever blocks change.</summary>
        public float Solid;

        /// <summary>Everything the brick takes up (m³), for water drag.</summary>
        public float Bulk;

        internal readonly List<(int comp, int minY, int[] cells)> Air = new();
        internal int SolidCells;
    }

    private sealed class Layers
    {
        public int MinY;
        public int[] Cells;
        public float PerCell;
        public Vector3 Centroid;
        public float Level;
    }

    public readonly List<Brick> Bricks = new();
    public readonly int BrickSize;
    private readonly List<Compartment> _comps;
    private readonly Layers[] _layers;
    private readonly Dictionary<Int3, Brick> _byKey = new();
    private readonly Int3 _min, _dims;
    private readonly short[] _compAt;

    /// <summary>
    /// Sizes bricks so a design of <paramref name="cells"/> cells makes roughly 2,500 of them at most, and a boat
    /// of more than a few hundred cells uses 1 m bricks rather than one per half-metre cell. Each brick is worked
    /// out every physics step: a patrol boat on half-metre bricks cost as much as a battleship, and ten of them in
    /// a battle were most of the "Ships" time.
    /// </summary>
    public static int BrickSizeFor(int cells) =>
        cells < 600 ? 1 : Math.Clamp(Math.Max(2, (int)MathF.Round(MathF.Cbrt(cells / 2500f))), 1, 12);

    public FloatModel(IEnumerable<(Int3 cell, float volume)> solids, List<Compartment> comps, int brickSize = 0)
    {
        _comps = comps;
        var solidList = new List<(Int3 cell, float volume)>(solids);
        int compCells = 0;
        foreach (Compartment c in comps) compCells += c.Cells.Count;
        BrickSize = brickSize > 0 ? brickSize : BrickSizeFor(solidList.Count + compCells);

        // A dense map of which compartment each cell is in (compact even for huge hulls).
        bool any = false;
        Int3 min = default, max = default;
        foreach (Compartment c in comps)
        foreach (Int3 cell in c.Cells)
        {
            if (!any)
            {
                min = max = cell;
                any = true;
            }
            else
            {
                min = Int3.Min(min, cell);
                max = Int3.Max(max, cell);
            }
        }

        _min = min;
        _dims = any ? max - min + new Int3(1, 1, 1) : Int3.Zero;
        _compAt = new short[Math.Max(_dims.X * _dims.Y * _dims.Z, 0)];
        Array.Fill(_compAt, (short)-1);

        _layers = new Layers[comps.Count];
        var sums = new Dictionary<Brick, (Vector3 sum, int n, int minY, int maxY)>();
        for (int ci = 0; ci < comps.Count; ci++)
        {
            Compartment c = comps[ci];
            var l = new Layers { MinY = c.MinY, Cells = new int[c.MaxY - c.MinY + 1], PerCell = c.CapacityM3 / Math.Max(c.Cells.Count, 1) };
            var perBrick = new Dictionary<Brick, int[]>();
            Vector3 centroid = Vector3.Zero;
            foreach (Int3 cell in c.Cells)
            {
                _compAt[Index(cell)] = (short)ci;
                l.Cells[cell.Y - c.MinY]++;
                centroid += cell.ToVector3();
                Brick b = BrickAt(cell, sums);
                if (!perBrick.TryGetValue(b, out int[] counts)) perBrick[b] = counts = new int[l.Cells.Length];
                counts[cell.Y - c.MinY]++;
            }

            l.Centroid = c.Cells.Count > 0 ? centroid / c.Cells.Count * Blueprint.CellSize : Vector3.Zero;
            _layers[ci] = l;
            foreach ((Brick b, int[] counts) in perBrick)
            {
                b.Air.Add((ci, c.MinY, counts));
                int n = 0;
                foreach (int k in counts) n += k;
                b.Bulk += n * l.PerCell;
            }
        }

        foreach ((Int3 cell, float v) in solidList)
        {
            Brick b = BrickAt(cell, sums);
            b.Solid += v;
            b.SolidCells++;
            b.Bulk += v;
        }

        foreach ((Brick b, (Vector3 sum, int n, int minY, int maxY)) in sums)
        {
            b.Center = sum / Math.Max(n, 1) * Blueprint.CellSize;
            b.HeightM = (maxY - minY + 1) * Blueprint.CellSize;
        }

        UpdateLevels();
    }

    private int Index(Int3 c)
    {
        Int3 d = c - _min;
        return (d.Z * _dims.Y + d.Y) * _dims.X + d.X;
    }

    private Int3 Key(Int3 cell)
    {
        int s = BrickSize;
        static int F(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        return new Int3(F(cell.X, s), F(cell.Y, s), F(cell.Z, s));
    }

    private Brick BrickAt(Int3 cell, Dictionary<Brick, (Vector3 sum, int n, int minY, int maxY)> sums)
    {
        Int3 key = Key(cell);
        if (!_byKey.TryGetValue(key, out Brick b))
        {
            b = new Brick();
            _byKey[key] = b;
            Bricks.Add(b);
        }

        if (sums != null)
        {
            sums.TryGetValue(b, out (Vector3 sum, int n, int minY, int maxY) s);
            if (s.n == 0) s.minY = s.maxY = cell.Y;
            sums[b] = (s.sum + cell.ToVector3(), s.n + 1, Math.Min(s.minY, cell.Y), Math.Max(s.maxY, cell.Y));
        }

        return b;
    }

    /// <summary>The compartment a cell belongs to, or null.</summary>
    public Compartment CompartmentAt(Int3 cell)
    {
        if (_compAt.Length == 0) return null;
        Int3 d = cell - _min;
        if (d.X < 0 || d.Y < 0 || d.Z < 0 || d.X >= _dims.X || d.Y >= _dims.Y || d.Z >= _dims.Z) return null;
        short i = _compAt[Index(cell)];
        return i >= 0 ? _comps[i] : null;
    }

    /// <summary>Counts block material again after blocks are destroyed or rebuilt.</summary>
    public void SetSolids(IEnumerable<(Int3 cell, float volume)> solids)
    {
        foreach (Brick b in Bricks)
        {
            b.Bulk -= b.Solid;
            b.Solid = 0;
            b.SolidCells = 0;
        }

        foreach ((Int3 cell, float v) in solids)
        {
            Int3 key = Key(cell);
            if (!_byKey.TryGetValue(key, out Brick b))
            {
                b = new Brick { Center = cell.ToVector3() * Blueprint.CellSize, HeightM = Blueprint.CellSize * BrickSize };
                _byKey[key] = b;
                Bricks.Add(b);
            }

            b.Solid += v;
            b.SolidCells++;
            b.Bulk += v;
        }
    }

    /// <summary>Works out how high the water stands in each compartment (fills from the bottom layer up).</summary>
    public void UpdateLevels()
    {
        for (int i = 0; i < _comps.Count; i++)
        {
            Layers l = _layers[i];
            float w = _comps[i].Water;
            l.Level = l.MinY;
            for (int y = 0; y < l.Cells.Length; y++)
            {
                float cap = l.Cells[y] * l.PerCell;
                if (cap <= 0)
                {
                    l.Level = l.MinY + y + 1;
                    continue;
                }

                if (w >= cap)
                {
                    w -= cap;
                    l.Level = l.MinY + y + 1;
                    continue;
                }

                l.Level = l.MinY + y + w / cap;
                break;
            }
        }
    }

    /// <summary>Water level in a compartment, in cell rows: row y is flooded when the level is above y + 0.5.</summary>
    public float Level(Compartment c) => _layers[_comps.IndexOf(c)].Level;

    /// <summary>Is the water in this compartment up over the middle of this row of cells?</summary>
    public bool Flooded(Compartment c, int cellY) => Level(c) > cellY + 0.5f;

    /// <summary>Sealed air left in a brick right now (m³).</summary>
    public float AirIn(Brick b)
    {
        float air = 0;
        foreach ((int ci, int minY, int[] cells) in b.Air)
        {
            Layers l = _layers[ci];
            for (int y = 0; y < cells.Length; y++)
            {
                if (cells[y] == 0) continue;
                float flooded = Math.Clamp(l.Level - (minY + y), 0f, 1f);
                air += cells[y] * l.PerCell * (1f - flooded);
            }
        }

        return air;
    }

    /// <summary>
    /// The most water a breached compartment can hold with the sea outside at <paramref name="seaY"/>:
    /// <paramref name="worldY"/> turns a height in the compartment (meters, design frame, at its middle) into world height.
    /// </summary>
    public float FloodLimit(Compartment c, Func<Vector3, float> worldY, float seaY)
    {
        Layers l = _layers[_comps.IndexOf(c)];
        float cs = Blueprint.CellSize, limit = 0;
        for (int y = 0; y < l.Cells.Length; y++)
        {
            if (l.Cells[y] == 0) continue;
            float h = worldY(new Vector3(l.Centroid.X, (l.MinY + y) * cs, l.Centroid.Z));
            limit += l.Cells[y] * l.PerCell * Math.Clamp((seaY - (h - cs * 0.5f)) / cs, 0f, 1f);
        }

        return limit;
    }
}
