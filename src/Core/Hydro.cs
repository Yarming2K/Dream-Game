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

    /// <summary>Every cell water can reach from outside, moving through cells that aren't watertight.</summary>
    private static HashSet<Int3> Outside(Func<Int3, bool> watertight, Int3 min, Int3 max)
    {
        var seen = new HashSet<Int3>();
        var stack = new Stack<Int3>();
        stack.Push(min);
        seen.Add(min);
        while (stack.Count > 0)
        {
            Int3 c = stack.Pop();
            foreach (Int3 d in Int3.Directions)
            {
                Int3 n = c + d;
                if (!Inside(n, min, max) || seen.Contains(n) || watertight(n)) continue;
                seen.Add(n);
                stack.Push(n);
            }
        }

        return seen;
    }

    /// <summary>Finds the sealed air spaces in a design: cells water can't get into from outside.</summary>
    public static List<Compartment> FindCompartments(IReadOnlyDictionary<Int3, BlockDef> cells)
    {
        var result = new List<Compartment>();
        if (cells.Count == 0) return result;
        (Int3 min, Int3 max) = Bounds(cells.Keys);
        bool Watertight(Int3 c) => cells.TryGetValue(c, out BlockDef d) && d.Watertight;
        HashSet<Int3> outside = Outside(Watertight, min, max);

        var assigned = new HashSet<Int3>();
        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int z = min.Z; z <= max.Z; z++)
        {
            var start = new Int3(x, y, z);
            if (Watertight(start) || outside.Contains(start) || assigned.Contains(start)) continue;
            var comp = new Compartment { Id = result.Count };
            var stack = new Stack<Int3>();
            stack.Push(start);
            assigned.Add(start);
            while (stack.Count > 0)
            {
                Int3 c = stack.Pop();
                comp.Cells.Add(c);
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 n = c + d;
                    if (assigned.Contains(n) || Watertight(n) || outside.Contains(n) || !Inside(n, min, max)) continue;
                    assigned.Add(n);
                    stack.Push(n);
                }
            }

            comp.Cells.Sort((a, b) => a.Y.CompareTo(b.Y));
            comp.MinY = comp.Cells[0].Y;
            comp.MaxY = comp.Cells[^1].Y;
            // A non-watertight block inside (a frame, a feed) takes up some of the air space.
            float air = 0;
            foreach (Int3 c in comp.Cells) air += cells.TryGetValue(c, out BlockDef d) ? 1f - d.Displacement : 1f;
            comp.CapacityM3 = air * CellVolume;
            foreach (Int3 c in comp.Cells)
            foreach (Int3 d in Int3.Directions)
                if (cells.TryGetValue(c + d, out BlockDef nd) && nd.Kind == BlockKind.BallastValve)
                    comp.IsBallast = true;
            result.Add(comp);
        }

        return result;
    }

    /// <summary>
    /// After damage: which compartments are now open to the sea, and through how many holes. A hole is a
    /// watertight block from the design that's gone.
    /// </summary>
    public static void UpdateBreaches(List<Compartment> comps, IReadOnlyDictionary<Int3, BlockDef> design,
        Func<Int3, bool> watertightNow)
    {
        if (comps.Count == 0) return;
        (Int3 min, Int3 max) = Bounds(design.Keys);
        HashSet<Int3> outside = Outside(watertightNow, min, max);
        foreach (Compartment comp in comps)
        {
            comp.Breached = comp.Cells.Any(outside.Contains);
            var holes = new HashSet<Int3>();
            if (comp.Breached)
            {
                var own = new HashSet<Int3>(comp.Cells);
                foreach (Int3 c in comp.Cells)
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 n = c + d;
                    if (!own.Contains(n) && design.TryGetValue(n, out BlockDef nd) && nd.Watertight && !watertightNow(n)) holes.Add(n);
                }
            }

            comp.Holes = Math.Max(holes.Count, comp.Breached ? 1 : 0);
            comp.HoleCells.Clear();
            comp.HoleCells.AddRange(holes);
        }
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

    /// <summary>Volume under water (m³) if the surface sits at <paramref name="waterY"/> meters in the design's frame.</summary>
    private static float Submerged(List<(Int3 cell, float volume)> parts, float waterY)
    {
        float cs = Blueprint.CellSize, total = 0;
        foreach ((Int3 c, float v) in parts)
        {
            float bottom = c.Y * cs - cs / 2f;
            total += v * Math.Clamp((waterY - bottom) / cs, 0f, 1f);
        }

        return total;
    }

    /// <summary>How the design floats: displacement, draft and freeboard, and whether its ballast lets it dive and surface.</summary>
    public static HydroStats Analyze(IReadOnlyDictionary<Int3, BlockDef> cells, List<Compartment> comps, float massKg)
    {
        var stats = new HydroStats { Compartments = comps.Count(c => !c.IsBallast) };
        if (cells.Count == 0) return stats;
        float cs = Blueprint.CellSize;
        var parts = BuoyantVolumes(cells, comps).ToList();
        float total = parts.Sum(p => p.volume);
        stats.DisplacementT = total * WaterDensity / 1000f;
        stats.BallastT = comps.Where(c => c.IsBallast).Sum(c => c.CapacityM3) * WaterDensity / 1000f;
        int minY = cells.Keys.Min(c => c.Y), maxY = cells.Keys.Max(c => c.Y);
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
                if (Submerged(parts, mid) < needed) lo = mid;
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
