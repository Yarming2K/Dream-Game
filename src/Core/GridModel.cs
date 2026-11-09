using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

public enum SectionKind
{
    Hull,
    Turret,
    Gun,
}

/// <summary>Anything that can answer "what shape is in this cell?" for raycasts.</summary>
public interface ICellShapeLookup
{
    bool TryGetShape(Int3 cell, out BlockShape shape, out int orientation);
}

/// <summary>A placed block at runtime, with its current health.</summary>
public sealed class BlockInstance
{
    public BlockDef Def;
    public Int3 Cell;
    public int Orientation;
    public float Hp;
    public int Section;
    public bool Alive = true;

    /// <summary>Rack space still filled with shells (ammo racks only).</summary>
    public float AmmoUnits;

    /// <summary>Free slot for the engine side (e.g. the node that draws this block).</summary>
    public object Tag;

    public BlockInstance(BlockDef def, Int3 cell, int orientation, int section)
    {
        Def = def;
        Cell = cell;
        Orientation = orientation;
        Hp = def.Hp;
        Section = section;
        if (def.Kind == BlockKind.AmmoRack) AmmoUnits = def.AmmoUnits;
    }
}

/// <summary>
/// A part of a grid that moves as one: the hull, a turret, or a gun's barrel. Cells keep their blueprint
/// coordinates; the section turns by <see cref="Angle"/> around <see cref="Axis"/> through <see cref="Pivot"/>
/// (in cell units, relative to the parent section).
/// </summary>
public sealed class Section : ICellShapeLookup
{
    public int Id;
    public SectionKind Kind;
    public int Parent = -1;

    /// <summary>The section's first cell and the cell in the parent it hangs from. Only this pair joins the two.</summary>
    public Int3 Root;
    public Int3 Attach;

    public Vector3 Pivot;
    public Vector3 Axis = Vector3.UnitY;
    public float Angle;

    public readonly Dictionary<Int3, BlockInstance> Cells = new();
    public Int3 Min;
    public Int3 Max;

    public void RecomputeBounds()
    {
        bool first = true;
        foreach (Int3 c in Cells.Keys)
        {
            if (first)
            {
                Min = Max = c;
                first = false;
                continue;
            }

            Min = Int3.Min(Min, c);
            Max = Int3.Max(Max, c);
        }
    }

    public bool TryGetShape(Int3 cell, out BlockShape shape, out int orientation)
    {
        if (Cells.TryGetValue(cell, out BlockInstance b) && b.Alive)
        {
            shape = b.Def.Shape;
            orientation = b.Orientation;
            return true;
        }

        shape = BlockShape.Cube;
        orientation = 0;
        return false;
    }
}

/// <summary>The runtime state of one physical object made of blocks: a vehicle, a structure or a piece of debris.</summary>
public sealed class GridModel
{
    public readonly Dictionary<Int3, BlockInstance> Blocks = new();
    public readonly List<Section> Sections = new();

    public GridModel()
    {
        Sections.Add(new Section { Id = 0, Kind = SectionKind.Hull });
    }

    public Section Hull => Sections[0];

    public BlockInstance Add(BlockDef def, Int3 cell, int orientation, int section = 0)
    {
        var b = new BlockInstance(def, cell, orientation, section);
        Blocks[cell] = b;
        Sections[section].Cells[cell] = b;
        return b;
    }

    public void Add(BlockInstance b)
    {
        Blocks[b.Cell] = b;
        Sections[b.Section].Cells[b.Cell] = b;
    }

    public void Remove(BlockInstance b)
    {
        b.Alive = false;
        Blocks.Remove(b.Cell);
        Sections[b.Section].Cells.Remove(b.Cell);
    }

    public void RecomputeBounds()
    {
        foreach (Section s in Sections) s.RecomputeBounds();
    }

    public float TotalMassKg()
    {
        float m = 0;
        foreach (BlockInstance b in Blocks.Values) m += b.Def.MassKg;
        return m;
    }

    /// <summary>Mass-weighted center in meters, with every section at rest.</summary>
    public Vector3 CenterOfMass()
    {
        float m = 0;
        Vector3 sum = Vector3.Zero;
        foreach (BlockInstance b in Blocks.Values)
        {
            m += b.Def.MassKg;
            sum += b.Cell.ToVector3() * b.Def.MassKg;
        }

        return m > 0 ? sum / m * Blueprint.CellSize : Vector3.Zero;
    }

    /// <summary>True if blocks in cells a and b (face neighbors) hold each other up.</summary>
    public bool AreJoined(BlockInstance a, BlockInstance b)
    {
        if (a.Section == b.Section) return true;
        Section sa = Sections[a.Section];
        Section sb = Sections[b.Section];
        return (sa.Root == a.Cell && sa.Attach == b.Cell) || (sb.Root == b.Cell && sb.Attach == a.Cell);
    }

    /// <summary>Builds the runtime grid for a vehicle, with turrets and guns as their own sections.</summary>
    public static GridModel FromAnalysis(VehicleAnalysis a)
    {
        var g = new GridModel();
        g.Sections.Clear();
        foreach (SectionInfo si in a.Sections)
        {
            g.Sections.Add(new Section
            {
                Id = si.Id, Kind = si.Kind, Parent = si.Parent, Root = si.Root, Attach = si.Attach,
                Pivot = si.Pivot, Axis = si.Axis,
            });
        }

        foreach (KeyValuePair<Int3, BlueprintBlock> kv in a.Cells)
        {
            BlockDef def = BlockCatalog.Get(kv.Value.Id);
            g.Add(def, kv.Key, kv.Value.O, a.SectionOf[kv.Key]);
        }

        g.RecomputeBounds();
        return g;
    }

    /// <summary>A grid with everything in one section (structures and debris).</summary>
    public static GridModel FromBlocks(IEnumerable<(BlockDef def, Int3 cell, int orientation)> blocks)
    {
        var g = new GridModel();
        foreach ((BlockDef def, Int3 cell, int o) in blocks) g.Add(def, cell, o);
        g.RecomputeBounds();
        return g;
    }

    public static GridModel FromBlueprint(Blueprint bp)
    {
        var list = new List<(BlockDef, Int3, int)>();
        foreach (BlueprintBlock b in bp.Blocks) list.Add((BlockCatalog.Get(b.Id), b.Cell, b.O));
        return FromBlocks(list);
    }
}

/// <summary>Adapter so a plain dictionary of blueprint blocks can be raycast (used by the builder).</summary>
public sealed class BlueprintShapeLookup : ICellShapeLookup
{
    private readonly IReadOnlyDictionary<Int3, BlueprintBlock> _cells;

    public BlueprintShapeLookup(IReadOnlyDictionary<Int3, BlueprintBlock> cells) => _cells = cells;

    public bool TryGetShape(Int3 cell, out BlockShape shape, out int orientation)
    {
        if (_cells.TryGetValue(cell, out BlueprintBlock b) && BlockCatalog.TryGet(b.Id, out BlockDef def))
        {
            shape = def.Shape;
            orientation = b.O;
            return true;
        }

        shape = BlockShape.Cube;
        orientation = 0;
        return false;
    }
}

/// <summary>Restricts a lookup to some cells (used to measure armor around a part).</summary>
public sealed class FilteredShapeLookup : ICellShapeLookup
{
    private readonly ICellShapeLookup _inner;
    private readonly Func<Int3, bool> _include;

    public FilteredShapeLookup(ICellShapeLookup inner, Func<Int3, bool> include)
    {
        _inner = inner;
        _include = include;
    }

    public bool TryGetShape(Int3 cell, out BlockShape shape, out int orientation)
    {
        if (_include(cell)) return _inner.TryGetShape(cell, out shape, out orientation);
        shape = BlockShape.Cube;
        orientation = 0;
        return false;
    }
}
