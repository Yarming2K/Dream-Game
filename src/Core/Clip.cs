using System;
using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>
/// A copied piece of a design (the builder's clipboard, or a saved part): its blocks, moved so the
/// piece's lowest corner is at (0, 0, 0). It can be turned in quarter turns and flipped, and placed
/// anywhere, big blocks included.
/// </summary>
public sealed class Clip
{
    public readonly List<BlueprintBlock> Blocks = new();

    /// <summary>The size of the box round every cell the piece fills.</summary>
    public Int3 Size { get; private set; }

    public int Count => Blocks.Count;

    /// <summary>A clip of these blocks (each kept whole), moved to start at the origin.</summary>
    public static Clip From(IEnumerable<BlueprintBlock> blocks)
    {
        var c = new Clip();
        foreach (BlueprintBlock b in blocks)
            if (BlockCatalog.TryGet(b.Id, out _)) c.Blocks.Add(new BlueprintBlock(b.Id, b.Cell, b.O));
        c.Normalize();
        return c;
    }

    private void Normalize()
    {
        if (Blocks.Count == 0)
        {
            Size = default;
            return;
        }

        Int3 lo = default, hi = default;
        bool first = true;
        foreach (BlueprintBlock b in Blocks)
            foreach (Int3 cell in Footprint.Cells(BlockCatalog.Get(b.Id), b.Cell, b.O))
            {
                lo = first ? cell : Int3.Min(lo, cell);
                hi = first ? cell : Int3.Max(hi, cell);
                first = false;
            }

        foreach (BlueprintBlock b in Blocks)
        {
            b.X -= lo.X;
            b.Y -= lo.Y;
            b.Z -= lo.Z;
        }

        Size = hi - lo + new Int3(1, 1, 1);
    }

    /// <summary>The piece turned a quarter turn (or several) round the X, Y or Z axis (0, 1, 2).</summary>
    public Clip Turned(int axis, int quarterTurns)
    {
        int r = axis switch
        {
            0 => Orientations.RotateAroundX(0, quarterTurns),
            1 => Orientations.RotateAroundY(0, quarterTurns),
            _ => Orientations.RotateAroundZ(0, quarterTurns),
        };
        var c = new Clip();
        foreach (BlueprintBlock b in Blocks)
        {
            int o = axis switch
            {
                0 => Orientations.RotateAroundX(b.O, quarterTurns),
                1 => Orientations.RotateAroundY(b.O, quarterTurns),
                _ => Orientations.RotateAroundZ(b.O, quarterTurns),
            };
            c.Blocks.Add(new BlueprintBlock(b.Id, Orientations.Apply(r, b.Cell), o));
        }

        c.Normalize();
        return c;
    }

    /// <summary>The piece's mirror image (left and right swapped).</summary>
    public Clip Flipped()
    {
        var c = new Clip();
        foreach (BlueprintBlock b in Blocks)
        {
            BlockDef def = BlockCatalog.Get(b.Id);
            c.Blocks.Add(new BlueprintBlock(b.Id, Footprint.MirrorAnchor(def, b.Cell, b.O), Orientations.MirrorOf(def, b.O)));
        }

        c.Normalize();
        return c;
    }

    /// <summary>Where the piece's bottom middle goes when it's placed with that point at <paramref name="baseCell"/>.</summary>
    public Int3 Origin(Int3 baseCell) => baseCell - new Int3(Size.X / 2, 0, Size.Z / 2);

    /// <summary>Every block of the piece placed with its bottom middle at <paramref name="baseCell"/>.</summary>
    public IEnumerable<(BlockDef def, Int3 anchor, int o)> PlacedAt(Int3 baseCell)
    {
        Int3 at = Origin(baseCell);
        foreach (BlueprintBlock b in Blocks) yield return (BlockCatalog.Get(b.Id), at + b.Cell, b.O);
    }

    /// <summary>The piece as a blueprint (to save as a part).</summary>
    public Blueprint ToBlueprint(string name) => Blueprint.FromCellMap(name, Blocks);
}
