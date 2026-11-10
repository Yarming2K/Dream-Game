using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>
/// Which cells a block covers. Most blocks fill one cell; big ones (a turbine, a heavy plate, a gas envelope)
/// fill a box of cells around the cell they're placed on (the anchor), turned with the block.
/// </summary>
public static class Footprint
{
    /// <summary>The block's box in its own frame, as cell offsets from the anchor (centered, rounding toward +).</summary>
    public static (Int3 lo, Int3 hi) LocalRange(Int3 size)
    {
        var lo = new Int3(-(size.X - 1) / 2, -(size.Y - 1) / 2, -(size.Z - 1) / 2);
        return (lo, lo + size - new Int3(1, 1, 1));
    }

    public static IEnumerable<Int3> Cells(BlockDef def, Int3 anchor, int orientation)
    {
        if (!def.IsMultiCell)
        {
            yield return anchor;
            yield break;
        }

        (Int3 lo, Int3 hi) = LocalRange(def.Size);
        for (int x = lo.X; x <= hi.X; x++)
        for (int y = lo.Y; y <= hi.Y; y++)
        for (int z = lo.Z; z <= hi.Z; z++)
            yield return anchor + Orientations.Apply(orientation, new Int3(x, y, z));
    }

    public static Int3[] CellArray(BlockDef def, Int3 anchor, int orientation)
    {
        if (!def.IsMultiCell) return new[] { anchor };
        var list = new List<Int3>(def.Volume);
        list.AddRange(Cells(def, anchor, orientation));
        return list.ToArray();
    }

    /// <summary>The middle of the block's box, in its own frame (cells, relative to the anchor).</summary>
    public static Vector3 LocalCenter(BlockDef def)
    {
        (Int3 lo, Int3 hi) = LocalRange(def.Size);
        return (lo.ToVector3() + hi.ToVector3()) * 0.5f;
    }

    /// <summary>The middle of a placed block, in grid cells.</summary>
    public static Vector3 Center(BlockDef def, Int3 anchor, int orientation) =>
        anchor.ToVector3() + Orientations.Apply(orientation, LocalCenter(def));

    /// <summary>How many cells the block reaches from its anchor in world direction <paramref name="dir"/> (0 for a single cell).</summary>
    /// <summary>The anchor of a block's mirror image across X = 0 (big blocks with an even size need nudging to land exactly).</summary>
    public static Int3 MirrorAnchor(BlockDef def, Int3 anchor, int o)
    {
        var m = new Int3(-anchor.X, anchor.Y, anchor.Z);
        if (!def.IsMultiCell) return m;
        int mo = Orientations.MirrorX(o);
        int want = int.MaxValue, have = int.MaxValue;
        foreach (Int3 c in Cells(def, anchor, o)) want = System.Math.Min(want, -c.X);
        foreach (Int3 c in Cells(def, m, mo)) have = System.Math.Min(have, c.X);
        return m + new Int3(want - have, 0, 0);
    }

    public static int Reach(BlockDef def, int orientation, Int3 dir)
    {
        int best = 0;
        foreach (Int3 c in Cells(def, Int3.Zero, orientation))
            best = System.Math.Max(best, c.X * dir.X + c.Y * dir.Y + c.Z * dir.Z);
        return best;
    }
}
