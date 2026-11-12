using System;
using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>
/// How a machine's solid cells become physics boxes. Neighbouring cells merge into as few boxes as possible
/// (greedy: a run along X, widened along Z, then stacked up Y), group by group, so a hit only rebuilds its own
/// group. Every box shape on a body costs the physics engine time whenever any of them changes (a turret turning,
/// a block shot off), so a 300 m battleship wants hundreds of boxes, not thousands.
/// </summary>
public static class ColliderBoxes
{
    /// <summary>Cells per side of a collider group on an ordinary section.</summary>
    public const int Group = 16;

    /// <summary>Cells per side of a collider group on a big section.</summary>
    public const int BigGroup = 32;

    /// <summary>
    /// A section this many cells across or more is "big": its boxes merge over bigger groups, and its turret
    /// colliders catch up a few times a second rather than every step.
    /// </summary>
    public const int BigSection = 32;

    public static bool IsBig(Int3 min, Int3 max) =>
        Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)) + 1 >= BigSection;

    /// <summary>Which collider group a cell belongs to.</summary>
    public static Int3 GroupOf(Int3 cell, bool big)
    {
        int size = big ? BigGroup : Group;
        return new Int3(FloorDiv(cell.X, size), FloorDiv(cell.Y, size), FloorDiv(cell.Z, size));
    }

    /// <summary>Boxes (lowest cell, size in cells) that together cover exactly the given cells, without overlapping.</summary>
    public static List<(Int3 Min, Int3 Size)> Merge(IEnumerable<Int3> cells)
    {
        var solid = new HashSet<Int3>(cells);
        var boxes = new List<(Int3, Int3)>();
        if (solid.Count == 0) return boxes;
        var order = new List<Int3>(solid);
        order.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        var used = new HashSet<Int3>();
        bool Free(Int3 p) => solid.Contains(p) && !used.Contains(p);

        bool RowFree(Int3 from, int len)
        {
            for (int x = 0; x < len; x++)
                if (!Free(new Int3(from.X + x, from.Y, from.Z))) return false;
            return true;
        }

        bool LayerFree(Int3 from, int lenX, int lenZ)
        {
            for (int z = 0; z < lenZ; z++)
                if (!RowFree(new Int3(from.X, from.Y, from.Z + z), lenX)) return false;
            return true;
        }

        foreach (Int3 start in order)
        {
            if (used.Contains(start)) continue;
            int dx = 1;
            while (Free(new Int3(start.X + dx, start.Y, start.Z))) dx++;
            int dz = 1;
            while (RowFree(new Int3(start.X, start.Y, start.Z + dz), dx)) dz++;
            int dy = 1;
            while (LayerFree(new Int3(start.X, start.Y + dy, start.Z), dx, dz)) dy++;
            for (int x = 0; x < dx; x++)
            for (int y = 0; y < dy; y++)
            for (int z = 0; z < dz; z++)
                used.Add(new Int3(start.X + x, start.Y + y, start.Z + z));
            boxes.Add((start, new Int3(dx, dy, dz)));
        }

        return boxes;
    }

    /// <summary>
    /// Whether a block collides as part of a merged box: cubes always, and slopes, wedges and corners too when
    /// <paramref name="boxed"/>. Machines that move ride on their wheel rays, tracks or water, so their slopes
    /// only matter for bumps and crashes, where a step of half a metre goes unnoticed; boxing them in makes a
    /// tank 18 shapes instead of 56, and the physics engine tests every shape of every moving body each step.
    /// Buildings keep exact slopes, so their ramps drive smoothly.
    /// </summary>
    public static bool Merges(BlockDef def, bool boxed) => def.HasCollider && (boxed || def.Shape == BlockShape.Cube);

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
