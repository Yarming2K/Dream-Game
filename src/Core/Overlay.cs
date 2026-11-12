using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>How a vehicle's instruments are drawn while you drive it.</summary>
public enum OverlayStyle
{
    /// <summary>Whatever its cockpit comes with.</summary>
    Auto,

    /// <summary>Smoked-glass panels floating beside the vehicle: flight or drive data, weapons, systems, damage, radar.</summary>
    Glass,

    /// <summary>Green symbology over the view: heading, speed and height tapes, pitch ladder, lock box.</summary>
    FighterHud,

    /// <summary>A reflector gunsight and a period instrument panel.</summary>
    Gauges,

    /// <summary>Compass with hull and turret, damage diagram, ammo cards, crew: land vehicles and ships.</summary>
    Tactical,

    /// <summary>The original text panels in the corners.</summary>
    Classic,
}

/// <summary>Which overlay a vehicle shows, and what each style is.</summary>
public static class Overlays
{
    /// <summary>The styles the game draws so far; any other shows as <see cref="OverlayStyle.Glass"/> until it's built.</summary>
    public static readonly OverlayStyle[] Built = { OverlayStyle.Glass, OverlayStyle.Classic };

    /// <summary>Every style a player can pick, in the order the pickers list them.</summary>
    public static readonly OverlayStyle[] Choices =
        { OverlayStyle.Auto, OverlayStyle.Glass, OverlayStyle.FighterHud, OverlayStyle.Gauges, OverlayStyle.Tactical, OverlayStyle.Classic };

    public static bool IsBuilt(OverlayStyle s) => Array.IndexOf(Built, s) >= 0;

    public static string Name(OverlayStyle s) => s switch
    {
        OverlayStyle.Auto => "From the cockpit",
        OverlayStyle.Glass => "Glass",
        OverlayStyle.FighterHud => "Fighter HUD",
        OverlayStyle.Gauges => "Gauges",
        OverlayStyle.Tactical => "Tactical",
        _ => "Classic",
    };

    public static string Describe(OverlayStyle s) => s switch
    {
        OverlayStyle.Auto => "Whatever the cockpit comes with: the Stealth Canopy brings Glass, the Bubble and Tandem the Fighter HUD, the framed canopies Gauges, and seats in land vehicles and ships Tactical.",
        OverlayStyle.Glass => "Smoked-glass panels floating beside the vehicle: speed and heading, weapons, systems, throttle and fuel, damage, radar, and a card on the target you're locked on.",
        OverlayStyle.FighterHud => "Green symbology over the view: heading, speed and height tapes, a pitch ladder and the lock box. Not built yet: shows as Glass for now.",
        OverlayStyle.Gauges => "A reflector gunsight and a period instrument panel. Not built yet: shows as Glass for now.",
        OverlayStyle.Tactical => "A compass with hull and turret, damage diagram, ammo cards and crew. Not built yet: shows as Glass for now.",
        _ => "The original text panels in the corners, with every key spelled out.",
    };

    /// <summary>The id a design stores (null for <see cref="OverlayStyle.Auto"/>).</summary>
    public static string Id(OverlayStyle s) => s == OverlayStyle.Auto ? null : s.ToString().ToLowerInvariant();

    public static OverlayStyle FromId(string id)
    {
        foreach (OverlayStyle s in Choices)
            if (string.Equals(Id(s), id, StringComparison.OrdinalIgnoreCase)) return s;
        return OverlayStyle.Auto;
    }

    /// <summary>The style a design's cockpit comes with.</summary>
    public static OverlayStyle CockpitDefault(IEnumerable<string> blockIds)
    {
        var ids = new HashSet<string>(blockIds);
        if (ids.Contains("canopy_stealth")) return OverlayStyle.Glass;
        if (ids.Contains("canopy_bubble") || ids.Contains("canopy_tandem")) return OverlayStyle.FighterHud;
        if (ids.Contains("canopy_framed") || ids.Contains("canopy_highback")) return OverlayStyle.Gauges;
        return OverlayStyle.Tactical;
    }

    /// <summary>The style a design asks for: its own pick, or its cockpit's.</summary>
    public static OverlayStyle Chosen(Blueprint bp)
    {
        OverlayStyle pick = FromId(bp?.Overlay);
        return pick != OverlayStyle.Auto ? pick : CockpitDefault(bp?.Blocks.Select(b => b.Id) ?? Enumerable.Empty<string>());
    }

    /// <summary>What actually shows: <see cref="Chosen"/>, or Glass while that style isn't built yet.</summary>
    public static OverlayStyle Shown(Blueprint bp) => Shown(Chosen(bp));

    public static OverlayStyle Shown(OverlayStyle s) => IsBuilt(s) ? s : OverlayStyle.Glass;
}

/// <summary>The instruments' arithmetic: compass headings, radar plots, fuel endurance and the damage map.</summary>
public static class Instruments
{
    /// <summary>
    /// A compass heading in degrees (0 to 360) for a direction in the world: north is −Z (the way designs face
    /// when placed), east is +X.
    /// </summary>
    public static float Heading(Vector3 dir)
    {
        float deg = MathF.Atan2(dir.X, -dir.Z) * 180f / MathF.PI;
        return (deg % 360f + 360f) % 360f;
    }

    /// <summary>The signed difference from heading <paramref name="from"/> to <paramref name="to"/>, −180 to 180 (positive: to the right).</summary>
    public static float Turn(float from, float to) => ((to - from) % 360f + 540f) % 360f - 180f;

    /// <summary>
    /// Where a contact sits on a forward radar arc: x from −1 (far left edge) to 1 (far right), y from 0 (here) to 1 (the
    /// arc's range). Null if it's behind the arc's edges or out of range.
    /// </summary>
    public static Vector2? RadarPlot(Vector3 here, float headingDeg, Vector3 contact, float rangeM, float halfArcDeg)
    {
        Vector3 d = contact - here;
        float dist = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        if (dist > rangeM || dist < 1e-3f) return null;
        float off = Turn(headingDeg, Heading(d));
        if (MathF.Abs(off) > halfArcDeg) return null;
        float a = off * MathF.PI / 180f, r = dist / rangeM;
        return new Vector2(MathF.Sin(a) * r / MathF.Sin(halfArcDeg * MathF.PI / 180f), MathF.Cos(a) * r);
    }

    /// <summary>Minutes the fuel lasts at a burn rate (litres a second); infinite when nothing's burning.</summary>
    public static float FuelMinutes(float fuelL, float litresPerSecond) =>
        litresPerSecond <= 1e-4f ? float.PositiveInfinity : fuelL / litresPerSecond / 60f;

    /// <summary>One column of a vehicle seen from above: how many blocks it had, and how many are still there.</summary>
    public readonly record struct DamageColumn(int X, int Z, int Cells, int Alive)
    {
        public float Health => Cells > 0 ? (float)Alive / Cells : 0f;
    }

    /// <summary>The vehicle seen from above, column by column, for a damage diagram that fits any design.</summary>
    public static List<DamageColumn> DamageMap(IEnumerable<(Int3 cell, bool alive)> cells)
    {
        var cols = new Dictionary<(int, int), (int cells, int alive)>();
        foreach ((Int3 c, bool alive) in cells)
        {
            cols.TryGetValue((c.X, c.Z), out var v);
            cols[(c.X, c.Z)] = (v.cells + 1, v.alive + (alive ? 1 : 0));
        }

        return cols.Select(kv => new DamageColumn(kv.Key.Item1, kv.Key.Item2, kv.Value.cells, kv.Value.alive))
            .OrderBy(c => c.Z).ThenBy(c => c.X).ToList();
    }
}
