using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DreamGame.Core;

public sealed class BlueprintBlock
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("z")] public int Z { get; set; }

    /// <summary>Orientation index, 0-23. See <see cref="Orientations"/>.</summary>
    [JsonPropertyName("o")] public int O { get; set; }

    [JsonIgnore] public Int3 Cell => new(X, Y, Z);

    public BlueprintBlock() { }

    public BlueprintBlock(string id, Int3 cell, int orientation = 0)
    {
        Id = id;
        X = cell.X;
        Y = cell.Y;
        Z = cell.Z;
        O = orientation;
    }
}

/// <summary>A saved design. Stored as JSON so it can be shared as a file.</summary>
public sealed class Blueprint
{
    public const int CurrentFormat = 1;

    /// <summary>Size of one grid cell in meters.</summary>
    public const float CellSize = 0.5f;

    [JsonPropertyName("format")] public int Format { get; set; } = CurrentFormat;
    [JsonPropertyName("name")] public string Name { get; set; } = "Untitled";
    [JsonPropertyName("blocks")] public List<BlueprintBlock> Blocks { get; set; } = new();

    /// <summary>The paint job's id (see <see cref="Liveries"/>), or null for the default (factory colors; naval grey on ships).</summary>
    [JsonPropertyName("paint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Paint { get; set; }

    /// <summary>How the instruments are drawn while it's driven (see <see cref="Overlays"/>), or null to go with the cockpit.</summary>
    [JsonPropertyName("overlay")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Overlay { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Reads a blueprint. Unknown block types and out-of-range orientations are dropped and listed in
    /// <paramref name="problems"/>, so files from newer versions still load.
    /// </summary>
    public static Blueprint FromJson(string json, List<string> problems = null)
    {
        Blueprint bp;
        try
        {
            bp = JsonSerializer.Deserialize<Blueprint>(json, Options);
        }
        catch (JsonException e)
        {
            throw new FormatException("That file isn't a valid blueprint: " + e.Message, e);
        }

        if (bp == null) throw new FormatException("That file is empty.");
        bp.Blocks ??= new List<BlueprintBlock>();
        if (bp.Format > CurrentFormat)
            problems?.Add($"Made with a newer version (format {bp.Format}); some parts may be missing.");

        var seen = new HashSet<Int3>();
        var kept = new List<BlueprintBlock>(bp.Blocks.Count);
        var unknown = new HashSet<string>();
        int overlaps = 0;
        foreach (BlueprintBlock b in bp.Blocks)
        {
            if (b == null) continue;
            if (!BlockCatalog.TryGet(b.Id, out BlockDef def))
            {
                unknown.Add(b.Id ?? "(none)");
                continue;
            }

            if (!Orientations.IsValid(b.O)) b.O = 0;
            Int3[] cells = Footprint.CellArray(def, b.Cell, b.O);
            bool clash = false;
            foreach (Int3 c in cells) clash |= seen.Contains(c);
            if (clash)
            {
                overlaps++;
                continue;
            }

            foreach (Int3 c in cells) seen.Add(c);
            kept.Add(b);
        }

        if (overlaps > 0) problems?.Add($"{overlaps} overlapping block(s) were skipped.");

        foreach (string id in unknown) problems?.Add($"Unknown block type '{id}' was skipped.");
        bp.Blocks = kept;
        bp.Format = CurrentFormat;
        return bp;
    }

    /// <summary>Every cell the design fills, each pointing at the block that fills it (big blocks appear under all their cells).</summary>
    public Dictionary<Int3, BlueprintBlock> ToCellMap()
    {
        var map = new Dictionary<Int3, BlueprintBlock>();
        foreach (BlueprintBlock b in Blocks)
        {
            if (!BlockCatalog.TryGet(b.Id, out BlockDef def))
            {
                map.TryAdd(b.Cell, b);
                continue;
            }

            foreach (Int3 c in Footprint.Cells(def, b.Cell, b.O)) map.TryAdd(c, b);
        }

        return map;
    }

    /// <summary>Builds a blueprint from blocks (a cell map's values are fine: big blocks are only kept once).</summary>
    public static Blueprint FromCellMap(string name, IEnumerable<BlueprintBlock> blocks)
    {
        var bp = new Blueprint { Name = name };
        var anchors = new HashSet<Int3>();
        foreach (BlueprintBlock b in blocks)
            if (anchors.Add(b.Cell)) bp.Blocks.Add(new BlueprintBlock(b.Id, b.Cell, b.O));
        // Stable order keeps saved files tidy and diffs small.
        bp.Blocks.Sort((a, c) =>
        {
            int r = a.Y.CompareTo(c.Y);
            if (r != 0) return r;
            r = a.Z.CompareTo(c.Z);
            return r != 0 ? r : a.X.CompareTo(c.X);
        });
        return bp;
    }

    public Blueprint Clone()
    {
        Blueprint c = FromCellMap(Name, Blocks);
        c.Paint = Paint;
        c.Overlay = Overlay;
        return c;
    }
}
