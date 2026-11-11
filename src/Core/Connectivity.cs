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
        var visited = new HashSet<BlockInstance>();
        var stack = new Stack<BlockInstance>();
        foreach (BlockInstance start in g.Instances)
        {
            if (!start.Alive || !visited.Add(start)) continue;
            var comp = new List<BlockInstance>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                BlockInstance b = stack.Pop();
                comp.Add(b);
                foreach (Int3 c in b.Cells)
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 n = c + d;
                    if (!g.Blocks.TryGetValue(n, out BlockInstance nb) || nb == b || !nb.Alive || visited.Contains(nb)) continue;
                    if (!g.AreJoined(b, c, nb, n)) continue;
                    visited.Add(nb);
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
                        foreach (Int3 cell in b.Cells) anchored |= cell.Y == 0;
                        if (anchored) break;
                    }

                    if (!anchored) detached.Add(c);
                }

                return detached;

            case KeepRule.DriverSeat:
                int seatComp = -1;
                for (int i = 0; i < comps.Count && seatComp < 0; i++)
                    foreach (BlockInstance b in comps[i])
                        if (b.Def.Kind is BlockKind.DriverSeat or BlockKind.DroneCore)
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

    /// <summary>How many blocks the search near a hole looks at before calling a piece "big" (the main body).</summary>
    public const int NearBudget = 3000;

    /// <summary>
    /// The pieces that came loose when the blocks at <paramref name="removed"/> went, found by searching out from
    /// the hole instead of through the whole grid. A battleship has about 150,000 cells and the full check
    /// (<see cref="FindDetached"/>) takes over 100 ms on it; this usually stops within a few dozen blocks, as soon as
    /// everything round the hole turns out to still be joined up.
    /// Returns null when it can't be sure without the full check: two big pieces (a hull broken in two), a small
    /// piece holding the driver's seat, or a big piece of a building it couldn't trace down to the ground.
    /// The answer matches <see cref="FindDetached"/> whenever it isn't null (for a grid that was in one piece, or
    /// for buildings, all of whose pieces stand on the ground).
    /// </summary>
    public static List<List<BlockInstance>> FindDetachedNear(GridModel g, KeepRule rule, IEnumerable<Int3> removed, int budget = NearBudget)
    {
        // Everything that could have come loose touches the hole.
        var seeds = new List<BlockInstance>();
        var seedSet = new HashSet<BlockInstance>();
        foreach (Int3 c in removed)
        foreach (Int3 d in Int3.Directions)
            if (g.Blocks.TryGetValue(c + d, out BlockInstance nb) && nb.Alive && seedSet.Add(nb))
                seeds.Add(nb);

        var detached = new List<List<BlockInstance>>();
        if (seeds.Count == 0) return detached;

        var owner = new Dictionary<BlockInstance, int>();
        var pieces = new List<NearPiece>();
        foreach (BlockInstance seed in seeds)
        {
            if (owner.ContainsKey(seed)) continue;
            NearPiece p = Trace(g, rule, seed, pieces.Count, owner, seedSet, seeds.Count, budget);
            pieces.Add(p);
            // The first piece reached every block round the hole: nothing was cut off (a building also has to still
            // stand on the ground).
            if (pieces.Count == 1 && p.SeedsReached == seeds.Count && (rule != KeepRule.Anchored || p.Ground))
                return detached;
        }

        // A search that ran into an earlier one's blocks is part of that piece.
        int Root(int i)
        {
            while (pieces[i].MergedInto >= 0) i = pieces[i].MergedInto;
            return i;
        }

        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].MergedInto < 0) continue;
            NearPiece r = pieces[Root(i)];
            r.Seat |= pieces[i].Seat;
            r.Ground |= pieces[i].Ground;
        }

        int big = 0;
        foreach (NearPiece p in pieces)
            if (!p.Complete && p.MergedInto < 0) big++;

        if (big == 0)
        {
            // Every piece is known in full: the same choice as the full check.
            var all = new List<List<BlockInstance>>();
            foreach (NearPiece p in pieces) all.Add(p.Blocks);
            if (rule == KeepRule.Anchored)
            {
                foreach (NearPiece p in pieces)
                    if (!p.Ground) detached.Add(p.Blocks);
                return detached;
            }

            int keep = -1;
            if (rule == KeepRule.DriverSeat)
                for (int i = 0; i < pieces.Count && keep < 0; i++)
                    if (pieces[i].Seat) keep = i;
            if (keep < 0) keep = Heaviest(all);
            for (int i = 0; i < all.Count; i++)
                if (i != keep) detached.Add(all[i]);
            return detached;
        }

        if (big > 1) return null;
        foreach (NearPiece p in pieces)
        {
            if (p.MergedInto >= 0) continue;
            if (!p.Complete)
            {
                if (rule == KeepRule.Anchored && !p.Ground) return null;
                continue;
            }

            if (rule == KeepRule.DriverSeat && p.Seat) return null;
            if (rule != KeepRule.Anchored || !p.Ground) detached.Add(p.Blocks);
        }

        return detached;
    }

    private sealed class NearPiece
    {
        public readonly List<BlockInstance> Blocks = new();
        public bool Complete, Seat, Ground;
        public int SeedsReached;
        public int MergedInto = -1;
    }

    /// <summary>
    /// Follows a piece out from <paramref name="start"/>, nearest blocks first (so it gets round the hole quickly),
    /// until it's all found or it passes the budget. For a building, once the hole is closed it heads downward
    /// first, to find the ground quickly.
    /// </summary>
    private static NearPiece Trace(GridModel g, KeepRule rule, BlockInstance start, int id, Dictionary<BlockInstance, int> owner,
        HashSet<BlockInstance> seeds, int seedCount, int budget)
    {
        var p = new NearPiece();
        bool building = rule == KeepRule.Anchored, downward = false;
        var near = new Queue<BlockInstance>();
        var low = new PriorityQueue<BlockInstance, int>();
        void Push(BlockInstance b)
        {
            owner[b] = id;
            if (seeds.Contains(b)) p.SeedsReached++;
            if (downward) low.Enqueue(b, LowestY(b));
            else near.Enqueue(b);
        }

        Push(start);
        while (near.Count > 0 || low.Count > 0)
        {
            BlockInstance b = near.Count > 0 ? near.Dequeue() : low.Dequeue();
            p.Blocks.Add(b);
            if (b.Def.Kind is BlockKind.DriverSeat or BlockKind.DroneCore) p.Seat = true;
            foreach (Int3 c in b.Cells)
            {
                if (c.Y == 0) p.Ground = true;
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 n = c + d;
                    if (!g.Blocks.TryGetValue(n, out BlockInstance nb) || nb == b || !nb.Alive) continue;
                    if (!g.AreJoined(b, c, nb, n)) continue;
                    if (owner.TryGetValue(nb, out int other))
                    {
                        // Joined to a piece an earlier search stopped short in: this is part of it.
                        if (other != id)
                        {
                            p.MergedInto = other;
                            return p;
                        }

                        continue;
                    }

                    Push(nb);
                }
            }

            // Every block round the hole is in this one piece: nothing else can have come loose. A building also
            // has to still reach the ground, so from here it searches downward.
            if (id == 0 && p.SeedsReached == seedCount)
            {
                if (!building || p.Ground) return p;
                if (!downward)
                {
                    downward = true;
                    while (near.Count > 0)
                    {
                        BlockInstance q = near.Dequeue();
                        low.Enqueue(q, LowestY(q));
                    }
                }
            }

            if (p.Blocks.Count >= budget) return p;
        }

        p.Complete = true;
        return p;
    }

    private static int LowestY(BlockInstance b)
    {
        int y = int.MaxValue;
        foreach (Int3 c in b.Cells) y = System.Math.Min(y, c.Y);
        return y;
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
