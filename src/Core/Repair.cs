using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>Rules for patching up vehicles by hand with a welding torch.</summary>
public static class Repair
{
    /// <summary>Health restored per second of welding on a damaged block.</summary>
    public const float HpPerSecond = 110f;

    /// <summary>How long it takes to weld a destroyed block back into place.</summary>
    public static float RebuildSeconds(BlockDef d) => 0.8f + d.MassKg / 140f;

    /// <summary>
    /// A destroyed block can be rebuilt if its cell is empty and it touches a block it would be joined to
    /// (same section, or the turret ring / barrel joint it hangs from).
    /// </summary>
    public static bool CanRebuild(GridModel g, Int3 cell, int section)
    {
        if (g.Blocks.ContainsKey(cell) || section < 0 || section >= g.Sections.Count) return false;
        Section s = g.Sections[section];
        foreach (Int3 d in Int3.Directions)
        {
            Int3 n = cell + d;
            if (!g.Blocks.TryGetValue(n, out BlockInstance b) || !b.Alive) continue;
            if (b.Section == section) return true;
            Section sb = g.Sections[b.Section];
            if ((s.Root == cell && s.Attach == n) || (sb.Root == n && sb.Attach == cell)) return true;
        }

        return false;
    }

    /// <summary>Blocks from the design that are no longer on the vehicle (destroyed or knocked off).</summary>
    public static List<Int3> Missing(VehicleAnalysis design, GridModel g)
    {
        var list = new List<Int3>();
        foreach (Int3 c in design.Cells.Keys)
            if (!design.Disconnected.Contains(c) && !g.Blocks.ContainsKey(c)) list.Add(c);
        return list;
    }
}
