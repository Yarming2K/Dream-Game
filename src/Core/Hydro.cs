using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamGame.Core;

/// <summary>A sealed air space inside a hull. Ships float on these; holes flood them.</summary>
public sealed class Compartment
{
    public int Id;

    /// <summary>Its cells, lowest first (water fills from the bottom).</summary>
    public readonly List<Int3> Cells = new();

    public float CapacityM3;

    /// <summary>Water inside right now, in m³.</summary>
    public float Water;

    /// <summary>Open to the sea through a hole.</summary>
    public bool Breached;

    /// <summary>How many destroyed hull blocks border it (bigger holes flood faster).</summary>
    public int Holes;

    /// <summary>Where those holes are (design cells), so flooding can tell whether they're under water.</summary>
    public readonly List<Int3> HoleCells = new();

    /// <summary>Touches a ballast valve, so it's flooded and blown on purpose.</summary>
    public bool IsBallast;

    public int MinY;
    public int MaxY;

    public float FillFraction => CapacityM3 > 0 ? Math.Clamp(Water / CapacityM3, 0f, 1f) : 0f;
}

public struct HydroStats
{
    /// <summary>Everything that floats: the most water the vessel can push aside (ballast empty), in tonnes.</summary>
    public float DisplacementT;

    public bool Floats;

    /// <summary>Height of the waterline above the bottom of the lowest block, in meters.</summary>
    public float DraftM;

    /// <summary>Height of the top of the hull above the waterline, in meters.</summary>
    public float FreeboardM;

    /// <summary>Waterline height in the design's frame (meters), for drawing it in the builder.</summary>
    public float WaterlineY;

    public float BallastT;
    public bool CanDive;
    public bool CanSurface;
    public int Compartments;
}

/// <summary>Where the water inside a compartment stands.</summary>
public static class Flood
{
    /// <summary>
    /// Height of the water surface in a compartment, in design cells (the middle of the lowest cell is its
    /// y; a full layer reaches half a cell above). Negative infinity when it's dry. Water fills the lowest
    /// cells first, a layer at a time.
    /// </summary>
    public static float SurfaceY(Compartment c)
    {
        if (c.Cells.Count == 0 || c.Water <= 0) return float.NegativeInfinity;
        float n = c.FillFraction * c.Cells.Count;
        int below = 0;
        int i = 0;
        while (i < c.Cells.Count)
        {
            int y = c.Cells[i].Y, layer = 0;
            while (i + layer < c.Cells.Count && c.Cells[i + layer].Y == y) layer++;
            if (n <= below + layer) return y - 0.5f + (n - below) / layer;
            below += layer;
            i += layer;
        }

        return c.Cells[^1].Y + 0.5f;
    }
}

/// <summary>Floating, flooding and diving.</summary>
public static class Hydro
{
    public const float WaterDensity = 1025f;
    public static readonly float CellVolume = Blueprint.CellSize * Blueprint.CellSize * Blueprint.CellSize;

    /// <summary>Flood water per second through one destroyed hull block, at 1 m below the surface (m³/s).</summary>
    public const float InflowPerHole = 0.25f;

    /// <summary>Water one bilge pump moves per second (m³/s).</summary>
    public const float PumpRate = 0.12f;

    /// <summary>How fast a ballast valve floods or blows its tank (m³/s).</summary>
    public const float ValveRate = 1.5f;

    private static (Int3 min, Int3 max) Bounds(IEnumerable<Int3> cells)
    {
        Int3 min = default, max = default;
        bool first = true;
        foreach (Int3 c in cells)
        {
            if (first)
            {
                min = max = c;
                first = false;
            }
            else
            {
                min = Int3.Min(min, c);
                max = Int3.Max(max, c);
            }
        }

        return (min - new Int3(1, 1, 1), max + new Int3(1, 1, 1));
    }

    private static bool Inside(Int3 c, Int3 min, Int3 max) =>
        c.X >= min.X && c.Y >= min.Y && c.Z >= min.Z && c.X <= max.X && c.Y <= max.Y && c.Z <= max.Z;

    /// <summary>A box of cells stored as a flat array, for flood fills over big hulls.</summary>
    private readonly struct Grid
    {
        public readonly Int3 Min, Dims;

        public Grid(Int3 min, Int3 max)
        {
            Min = min;
            Dims = max - min + new Int3(1, 1, 1);
        }

        public int Count => Dims.X * Dims.Y * Dims.Z;
        public int Index(Int3 c) => ((c.Z - Min.Z) * Dims.Y + (c.Y - Min.Y)) * Dims.X + (c.X - Min.X);

        public Int3 Cell(int i)
        {
            int x = i % Dims.X, rest = i / Dims.X;
            return new Int3(Min.X + x, Min.Y + rest % Dims.Y, Min.Z + rest / Dims.Y);
        }

        public bool Inside(Int3 c) =>
            c.X >= Min.X && c.Y >= Min.Y && c.Z >= Min.Z && c.X < Min.X + Dims.X && c.Y < Min.Y + Dims.Y && c.Z < Min.Z + Dims.Z;
    }

    /// <summary>Finds the sealed air spaces in a design: cells water can't get into from outside.</summary>
    public static List<Compartment> FindCompartments(IReadOnlyDictionary<Int3, BlockDef> cells)
    {
        var result = new List<Compartment>();
        if (cells.Count == 0) return result;
        (Int3 min, Int3 max) = Bounds(cells.Keys);
        var grid = new Grid(min, max);

        // 0 = open, 1 = watertight wall, 2 = reached from outside, 3 = in a compartment.
        var state = new byte[grid.Count];
        foreach ((Int3 c, BlockDef d) in cells)
            if (d.Watertight) state[grid.Index(c)] = 1;

        var stack = new Stack<int>();
        void Flood(int start, byte mark, List<Int3> into)
        {
            state[start] = mark;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                Int3 c = grid.Cell(i);
                into?.Add(c);
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 n = c + d;
                    if (!grid.Inside(n)) continue;
                    int ni = grid.Index(n);
                    if (state[ni] != 0) continue;
                    state[ni] = mark;
                    stack.Push(ni);
                }
            }
        }

        Flood(grid.Index(min), 2, null);
        var compOf = new int[state.Length];
        for (int i = 0; i < state.Length; i++)
        {
            if (state[i] != 0) continue;
            var comp = new Compartment { Id = result.Count };
            Flood(i, 3, comp.Cells);
            foreach (Int3 c in comp.Cells) compOf[grid.Index(c)] = result.Count;
            comp.Cells.Sort((a, b) => a.Y.CompareTo(b.Y));
            comp.MinY = comp.Cells[0].Y;
            comp.MaxY = comp.Cells[^1].Y;
            comp.CapacityM3 = comp.Cells.Count * CellVolume;
            result.Add(comp);
        }

        // Blocks inside a compartment (machinery, frames, feeds) take up some of its air; a ballast valve
        // makes the compartments it touches ballast tanks.
        foreach ((Int3 c, BlockDef d) in cells)
        {
            int i = grid.Index(c);
            if (state[i] == 3) result[compOf[i]].CapacityM3 -= d.Displacement * CellVolume;
            if (d.Kind != BlockKind.BallastValve) continue;
            foreach (Int3 dir in Int3.Directions)
            {
                Int3 n = c + dir;
                if (grid.Inside(n) && state[grid.Index(n)] == 3) result[compOf[grid.Index(n)]].IsBallast = true;
            }
        }

        return result;
    }

    /// <summary>
    /// After damage: which compartments are now open to the sea, and through which holes. A hole is a
    /// watertight block from the design that's gone. Water gets from the sea to a compartment through holes
    /// that touch the open water outside, each other, or another breached compartment, so this only looks
    /// at the destroyed blocks (it never floods the whole hull), which keeps it quick on huge ships.
    /// <paramref name="compartmentAt"/> answers which compartment a cell belongs to (built from the list if not given).
    /// </summary>
    public static void UpdateBreaches(List<Compartment> comps, IReadOnlyDictionary<Int3, BlockDef> design,
        Func<Int3, bool> watertightNow, Func<Int3, Compartment> compartmentAt = null)
    {
        if (comps.Count == 0) return;
        if (compartmentAt == null)
        {
            var map = new Dictionary<Int3, Compartment>();
            foreach (Compartment c in comps)
            foreach (Int3 cell in c.Cells)
                map[cell] = c;
            compartmentAt = cell => map.TryGetValue(cell, out Compartment found) ? found : null;
        }

        // Union-find over: the sea (0), each compartment (1..n), and each hole.
        var holes = new List<Int3>();
        foreach ((Int3 cell, BlockDef d) in design)
            if (d.Watertight && !watertightNow(cell)) holes.Add(cell);
        var holeIndex = new Dictionary<Int3, int>(holes.Count);
        for (int i = 0; i < holes.Count; i++) holeIndex[holes[i]] = comps.Count + 1 + i;
        var compIndex = new Dictionary<Compartment, int>(comps.Count);
        for (int i = 0; i < comps.Count; i++) compIndex[comps[i]] = i + 1;
        int[] parent = new int[comps.Count + 1 + holes.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }

        void Union(int a, int b) => parent[Find(a)] = Find(b);

        var touching = new List<(Compartment comp, Int3 hole)>();
        foreach (Int3 h in holes)
        {
            int self = holeIndex[h];
            foreach (Int3 dir in Int3.Directions)
            {
                Int3 n = h + dir;
                Compartment c = compartmentAt(n);
                if (c != null && compIndex.TryGetValue(c, out int ci))
                {
                    Union(self, ci);
                    touching.Add((c, h));
                }
                else if (holeIndex.TryGetValue(n, out int other)) Union(self, other);
                else if (!(design.TryGetValue(n, out BlockDef nd) && nd.Watertight)) Union(self, 0); // open water (or a flooded passage to it)
            }
        }

        int sea = Find(0);
        foreach (Compartment comp in comps)
        {
            comp.Breached = Find(compIndex[comp]) == sea;
            comp.HoleCells.Clear();
        }

        foreach ((Compartment comp, Int3 hole) in touching)
            if (comp.Breached && !comp.HoleCells.Contains(hole)) comp.HoleCells.Add(hole);
        foreach (Compartment comp in comps) comp.Holes = comp.Breached ? Math.Max(comp.HoleCells.Count, 1) : 0;
    }

    /// <summary>
    /// One tick of flooding: breached compartments take on water through holes that are under water, pumps
    /// push it out, and ballast valves flood or blow their tanks on command.
    /// <paramref name="holeDepth"/> gives how far below the surface a compartment's deepest hole is (meters,
    /// negative if it's above the waterline). <paramref name="floodLimit"/>, if given, is the most water a
    /// breached compartment can hold (water inside can't rise above the sea outside); extra drains out.
    /// </summary>
    public static void Step(List<Compartment> comps, float dt, Func<Compartment, float> holeDepth, int pumps, int ballastCommand,
        Func<Compartment, float> floodLimit = null)
    {
        float pumpBudget = pumps * PumpRate * dt;
        foreach (Compartment c in comps)
        {
            if (c.IsBallast)
            {
                if (ballastCommand > 0) c.Water = Math.Min(c.CapacityM3, c.Water + ValveRate * dt);
                else if (ballastCommand < 0) c.Water = Math.Max(0, c.Water - ValveRate * dt);
            }

            if (!c.Breached) continue;
            float depth = holeDepth(c);
            float limit = floodLimit != null ? Math.Clamp(floodLimit(c), 0f, c.CapacityM3) : c.CapacityM3;
            float flow = InflowPerHole * c.Holes * MathF.Sqrt(Math.Max(Math.Abs(depth), 0.25f)) * dt;
            if (c.Water < limit && depth > 0) c.Water = Math.Min(limit, c.Water + flow);
            else if (c.Water > limit && !c.IsBallast) c.Water = Math.Max(limit, c.Water - flow);
        }

        // Pumps work on the worst flooding first (ballast tanks are left alone).
        foreach (Compartment c in comps.Where(c => !c.IsBallast && c.Water > 0).OrderByDescending(c => c.FillFraction))
        {
            if (pumpBudget <= 0) break;
            float take = Math.Min(pumpBudget, c.Water);
            c.Water -= take;
            pumpBudget -= take;
        }
    }

    /// <summary>
    /// The buoyant pieces of a design: each block's own displacement plus each compartment's air, as
    /// (cell, volume m³) pairs. Ballast and flood water are taken out of the compartments they're in,
    /// filling from the bottom up.
    /// </summary>
    public static IEnumerable<(Int3 cell, float volume)> BuoyantVolumes(IReadOnlyDictionary<Int3, BlockDef> cells,
        IReadOnlyList<Compartment> comps, bool ballastFull = false)
    {
        foreach ((Int3 c, BlockDef d) in cells)
            if (d.Displacement > 0) yield return (c, d.Displacement * CellVolume);

        foreach (Compartment comp in comps)
        {
            float water = comp.IsBallast && ballastFull ? comp.CapacityM3 : comp.Water;
            float perCell = comp.CapacityM3 / Math.Max(comp.Cells.Count, 1);
            int flooded = (int)MathF.Round(water / Math.Max(perCell, 1e-6f));
            for (int i = flooded; i < comp.Cells.Count; i++) yield return (comp.Cells[i], perCell);
        }
    }

    /// <summary>Buoyant volume in each row of cells (rows from <paramref name="minY"/> up), which is all that matters for an upright hull.</summary>
    private static float[] VolumeByRow(IReadOnlyDictionary<Int3, BlockDef> cells, IReadOnlyList<Compartment> comps, int minY, int maxY)
    {
        var rows = new float[maxY - minY + 1];
        foreach ((Int3 c, BlockDef d) in cells)
            if (d.Displacement > 0) rows[c.Y - minY] += d.Displacement * CellVolume;
        foreach (Compartment comp in comps)
        {
            float perCell = comp.CapacityM3 / Math.Max(comp.Cells.Count, 1);
            int flooded = (int)MathF.Round(comp.Water / Math.Max(perCell, 1e-6f));
            for (int i = flooded; i < comp.Cells.Count; i++) rows[comp.Cells[i].Y - minY] += perCell;
        }

        return rows;
    }

    /// <summary>Volume under water (m³) if the surface sits at <paramref name="waterY"/> meters in the design's frame.</summary>
    private static float Submerged(float[] rows, int minY, float waterY)
    {
        float cs = Blueprint.CellSize, total = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            float bottom = (minY + i) * cs - cs / 2f;
            total += rows[i] * Math.Clamp((waterY - bottom) / cs, 0f, 1f);
        }

        return total;
    }

    /// <summary>How the design floats: displacement, draft and freeboard, and whether its ballast lets it dive and surface.</summary>
    public static HydroStats Analyze(IReadOnlyDictionary<Int3, BlockDef> cells, List<Compartment> comps, float massKg)
    {
        var stats = new HydroStats { Compartments = comps.Count(c => !c.IsBallast) };
        if (cells.Count == 0) return stats;
        float cs = Blueprint.CellSize;
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (Int3 c in cells.Keys)
        {
            minY = Math.Min(minY, c.Y);
            maxY = Math.Max(maxY, c.Y);
        }

        float[] rows = VolumeByRow(cells, comps, minY, maxY);
        float total = rows.Sum();
        stats.DisplacementT = total * WaterDensity / 1000f;
        stats.BallastT = comps.Where(c => c.IsBallast).Sum(c => c.CapacityM3) * WaterDensity / 1000f;
        float bottom = minY * cs - cs / 2f, top = maxY * cs + cs / 2f;
        stats.Floats = total * WaterDensity > massKg;
        stats.CanSurface = stats.Floats;
        stats.CanDive = stats.BallastT > 0 && (total * WaterDensity - stats.BallastT * 1000f) <= massKg * 1.01f;

        float needed = massKg / WaterDensity;
        float lo = bottom, hi = top;
        if (!stats.Floats)
        {
            stats.WaterlineY = top;
        }
        else
        {
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) / 2f;
                if (Submerged(rows, minY, mid) < needed) lo = mid;
                else hi = mid;
            }

            stats.WaterlineY = (lo + hi) / 2f;
        }

        stats.DraftM = stats.WaterlineY - bottom;
        stats.FreeboardM = Math.Max(0, top - stats.WaterlineY);
        return stats;
    }

    /// <summary>Top speed (m/s) for a ship: engine power through its propellers against hull resistance.</summary>
    public static float TopSpeed(float powerKw, float beamM, float draftM, float lengthM, float governorMs = 60f / 3.6f)
    {
        if (powerKw <= 0) return 0;
        float p = powerKw * 1000f * 0.6f;
        float k = 0.5f * WaterDensity * (0.2f * beamM * draftM + 0.004f * lengthM * (beamM + 2f * draftM));
        float v = MathF.Pow(p / Math.Max(k, 1f), 1f / 3f);
        return Math.Min(v, governorMs);
    }
}

/// <summary>Torpedoes, fired from tubes and fed from torpedo racks.</summary>
public static class Torpedoes
{
    public const float Speed = 28f;
    public const float RangeM = 1500f;
    public const float RunDepth = 1.6f;
    public const float Damage = 1300f;
    public const float RadiusM = 3.6f;
    public const float PenMm = 80f;
    public const int Units = 8;
    public const float ReloadS = 14f;
}
