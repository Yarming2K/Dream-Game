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

    /// <summary>Every cell it fills (just <see cref="Cell"/> for ordinary blocks).</summary>
    public readonly Int3[] Cells;

    /// <summary>Its middle, in grid cells (where its mass sits).</summary>
    public readonly Vector3 Center;

    public BlockInstance(BlockDef def, Int3 cell, int orientation, int section)
    {
        Def = def;
        Cell = cell;
        Orientation = orientation;
        Hp = def.Hp;
        Section = section;
        if (def.Kind is BlockKind.AmmoRack or BlockKind.BombRack) AmmoUnits = def.AmmoUnits;
        Cells = Footprint.CellArray(def, cell, orientation);
        Center = def.IsMultiCell ? Footprint.Center(def, cell, orientation) : cell.ToVector3();
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
    /// <summary>What fills each cell. A big block appears under every cell it covers; use <see cref="Instances"/> to visit each block once.</summary>
    public readonly Dictionary<Int3, BlockInstance> Blocks = new();
    public readonly List<Section> Sections = new();
    private readonly Dictionary<Int3, BlockInstance> _byAnchor = new();

    /// <summary>Each block once.</summary>
    public IEnumerable<BlockInstance> Instances => _byAnchor.Values;
    public int InstanceCount => _byAnchor.Count;

    public GridModel()
    {
        Sections.Add(new Section { Id = 0, Kind = SectionKind.Hull });
    }

    public Section Hull => Sections[0];

    public BlockInstance Add(BlockDef def, Int3 cell, int orientation, int section = 0)
    {
        var b = new BlockInstance(def, cell, orientation, section);
        Add(b);
        return b;
    }

    public void Add(BlockInstance b)
    {
        if (_byAnchor.TryGetValue(b.Cell, out BlockInstance old) && old != b) Tally(old, -1);
        if (!_byAnchor.TryGetValue(b.Cell, out old) || old != b) Tally(b, +1);
        foreach (Int3 c in b.Cells)
        {
            Blocks[c] = b;
            Sections[b.Section].Cells[c] = b;
        }

        _byAnchor[b.Cell] = b;
    }

    public void Remove(BlockInstance b)
    {
        b.Alive = false;
        foreach (Int3 c in b.Cells)
        {
            if (Blocks.TryGetValue(c, out BlockInstance here) && here == b) Blocks.Remove(c);
            Sections[b.Section].Cells.Remove(c);
        }

        if (_byAnchor.TryGetValue(b.Cell, out BlockInstance a) && a == b)
        {
            _byAnchor.Remove(b.Cell);
            Tally(b, -1);
        }
    }

    // Running totals, kept as blocks come and go, so nothing has to add up a 30,000-block ship after every hit.
    private double _mass, _power, _fuel;
    private double _momentX, _momentY, _momentZ;
    private readonly Dictionary<BlockKind, int> _kinds = new();

    private void Tally(BlockInstance b, int sign)
    {
        double m = b.Def.MassKg * sign;
        _mass += m;
        Vector3 c = b.Center;
        _momentX += c.X * m;
        _momentY += c.Y * m;
        _momentZ += c.Z * m;
        _power += b.Def.PowerKw * sign;
        _fuel += b.Def.FuelL * sign;
        _kinds.TryGetValue(b.Def.Kind, out int n);
        _kinds[b.Def.Kind] = n + sign;
    }

    /// <summary>How many blocks of this kind it has.</summary>
    public int CountOf(BlockKind kind) => _kinds.TryGetValue(kind, out int n) ? n : 0;

    /// <summary>The power of all its engines, generators and reactors together.</summary>
    public float PowerKw => (float)Math.Max(_power, 0);

    /// <summary>What all its fuel tanks hold, full.</summary>
    public float FuelL => (float)Math.Max(_fuel, 0);

    /// <summary>The block whose anchor is <paramref name="anchor"/>, if it's there.</summary>
    public bool TryGetInstance(Int3 anchor, out BlockInstance b) => _byAnchor.TryGetValue(anchor, out b);

    /// <summary>
    /// How far (in cells) any cell can get from the origin, however the turrets and guns are turned. A cell on a
    /// turret swings round the turret's pivot, and a gun's round its own pivot, which the turret carries: so the
    /// furthest it can get is its distance from its pivot, plus each pivot's distance from the one it turns round,
    /// down to the hull.
    /// </summary>
    public float MaxReach()
    {
        float reach = 0;
        foreach (Section s in Sections)
        {
            bool turns = s.Kind != SectionKind.Hull && s.Parent >= 0;
            Vector3 about = turns ? s.Pivot : Vector3.Zero;
            float swing = turns ? PivotReach(s) : 0f;
            foreach (Int3 c in s.Cells.Keys)
                reach = MathF.Max(reach, (c.ToVector3() - about).Length() + swing);
        }

        return reach;
    }

    /// <summary>How far a turning section's pivot can get from the origin, through the sections it rides on.</summary>
    private float PivotReach(Section s)
    {
        float d = 0;
        Section at = s;
        for (int guard = 0; guard < Sections.Count; guard++)
        {
            if (at.Parent < 0 || at.Parent >= Sections.Count || Sections[at.Parent].Kind == SectionKind.Hull) break;
            Section up = Sections[at.Parent];
            d += (at.Pivot - up.Pivot).Length();
            at = up;
        }

        return d + at.Pivot.Length();
    }

    public void RecomputeBounds()
    {
        foreach (Section s in Sections) s.RecomputeBounds();
    }

    public float TotalMassKg() => _byAnchor.Count == 0 ? 0f : (float)Math.Max(_mass, 0);

    /// <summary>Mass-weighted center in meters, with every section at rest.</summary>
    public Vector3 CenterOfMass() => _mass > 1e-6 && _byAnchor.Count > 0
        ? new Vector3((float)(_momentX / _mass), (float)(_momentY / _mass), (float)(_momentZ / _mass)) * Blueprint.CellSize
        : Vector3.Zero;

    /// <summary>True if blocks touching at cells <paramref name="ac"/> and <paramref name="bc"/> (face neighbors) hold each other up.</summary>
    public bool AreJoined(BlockInstance a, Int3 ac, BlockInstance b, Int3 bc)
    {
        if (a.Section == b.Section) return true;
        Section sa = Sections[a.Section];
        Section sb = Sections[b.Section];
        return (sa.Root == ac && sa.Attach == bc) || (sb.Root == bc && sb.Attach == ac);
    }

    public bool AreJoined(BlockInstance a, BlockInstance b) => AreJoined(a, a.Cell, b, b.Cell);

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

        foreach (Int3 anchor in a.Anchors)
        {
            BlueprintBlock bb = a.Cells[anchor];
            g.Add(BlockCatalog.Get(bb.Id), anchor, bb.O, a.SectionOf[anchor]);
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
