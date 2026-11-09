using System.Collections.Generic;

namespace DreamGame.Core;

public enum KeepRule
{
    /// <summary>Keep the piece with a working driver seat (vehicles). Falls back to the heaviest piece.</summary>
    DriverSeat,

    /// <summary>Keep every piece touching the ground row, y = 0 (structures).</summary>
    Anchored,

    /// <summary>Keep the heaviest piece (debris).</summary>
    Heaviest,
}

public static class Connectivity
{
    /// <summary>Groups alive blocks into pieces that hold together.</summary>
    public static List<List<BlockInstance>> Components(GridModel g)
    {
        var result = new List<List<BlockInstance>>();
        var visited = new HashSet<Int3>();
        var stack = new Stack<BlockInstance>();
        foreach (BlockInstance start in g.Blocks.Values)
        {
            if (!start.Alive || !visited.Add(start.Cell)) continue;
            var comp = new List<BlockInstance>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                BlockInstance b = stack.Pop();
                comp.Add(b);
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 n = b.Cell + d;
                    if (visited.Contains(n)) continue;
                    if (!g.Blocks.TryGetValue(n, out BlockInstance nb) || !nb.Alive) continue;
                    if (!g.AreJoined(b, nb)) continue;
                    visited.Add(n);
                    stack.Push(nb);
                }
            }

            result.Add(comp);
        }

        return result;
    }

    /// <summary>The pieces that have lost their connection and should fall off.</summary>
    public static List<List<BlockInstance>> FindDetached(GridModel g, KeepRule rule)
    {
        List<List<BlockInstance>> comps = Components(g);
        var detached = new List<List<BlockInstance>>();
        if (comps.Count == 0) return detached;

        switch (rule)
        {
            case KeepRule.Anchored:
                foreach (List<BlockInstance> c in comps)
                {
                    bool anchored = false;
                    foreach (BlockInstance b in c)
                    {
                        if (b.Cell.Y != 0) continue;
                        anchored = true;
                        break;
                    }

                    if (!anchored) detached.Add(c);
                }

                return detached;

            case KeepRule.DriverSeat:
                int seatComp = -1;
                for (int i = 0; i < comps.Count && seatComp < 0; i++)
                    foreach (BlockInstance b in comps[i])
                        if (b.Def.Kind == BlockKind.DriverSeat)
                        {
                            seatComp = i;
                            break;
                        }

                int keep = seatComp >= 0 ? seatComp : Heaviest(comps);
                for (int i = 0; i < comps.Count; i++)
                    if (i != keep) detached.Add(comps[i]);
                return detached;

            default:
                int heavy = Heaviest(comps);
                for (int i = 0; i < comps.Count; i++)
                    if (i != heavy) detached.Add(comps[i]);
                return detached;
        }
    }

    private static int Heaviest(List<List<BlockInstance>> comps)
    {
        int best = 0;
        float bestMass = -1;
        for (int i = 0; i < comps.Count; i++)
        {
            float m = 0;
            foreach (BlockInstance b in comps[i]) m += b.Def.MassKg;
            if (m <= bestMass) continue;
            bestMass = m;
            best = i;
        }

        return best;
    }
}
