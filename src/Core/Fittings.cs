using System;
using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>How many of each working fitting a vehicle carries (only what's attached counts).</summary>
public struct FittingCounts
{
    public int Periscopes, Antennas, Toolboxes, SpareTracks, Vents, SmokeLaunchers, Headlights, Searchlights, Exhausts, Hatches;
}

/// <summary>
/// What the small fittings do. None of them is just for looks: periscopes see further, antennas carry
/// orders further, toolboxes and spare track links let the crew mend the vehicle themselves, vents clear
/// smoke from fires, smoke launchers throw a screen, and lights light.
/// </summary>
public static class Fittings
{
    /// <summary>Extra spotting range per periscope, as a share, and how many count.</summary>
    public const float SightPerPeriscope = 0.15f;
    public const int MaxPeriscopes = 2;

    /// <summary>Extra command range per antenna on a vehicle with a console (m), and how many count.</summary>
    public const float RangePerAntenna = 1000f;
    public const int MaxAntennas = 3;

    /// <summary>Health per second each toolbox lets the crew patch, how many count, and how long the vehicle must go unhit first.</summary>
    public const float RepairHpPerToolbox = 3f;
    public const int MaxToolboxes = 3;
    public const float QuietS = 8f;

    /// <summary>Seconds to refit one lost track or wheel with one set of spare links (two sets halve it).</summary>
    public const float RefitS = 20f;

    /// <summary>How much each vent shortens a fire, and how many count.</summary>
    public const float VentFireCut = 0.15f;
    public const int MaxVents = 3;

    /// <summary>How long a smoke screen hangs, how big each cloud is, and how far ahead it's thrown.</summary>
    public const float SmokeLifeS = 40f, SmokeRadiusM = 7f, SmokeThrowM = 22f;

    public static FittingCounts Count(IEnumerable<BlockDef> defs)
    {
        var c = new FittingCounts();
        foreach (BlockDef d in defs)
            switch (d.Id)
            {
                case "periscope": c.Periscopes++; break;
                case "antenna": c.Antennas++; break;
                case "toolbox":
                case "stowage_rack": c.Toolboxes++; break;
                case "spare_tracks": c.SpareTracks++; break;
                case "vent": c.Vents++; break;
                case "smoke_launcher": c.SmokeLaunchers++; break;
                case "headlight": c.Headlights++; break;
                case "searchlight": c.Searchlights++; break;
                case "exhaust": c.Exhausts++; break;
                case "hatch": c.Hatches++; break;
            }

        return c;
    }

    public static float SightFactor(int periscopes) => 1f + SightPerPeriscope * Math.Clamp(periscopes, 0, MaxPeriscopes);

    public static float CommandRange(int antennas) => Sensors.CommandRangeM + RangePerAntenna * Math.Clamp(antennas, 0, MaxAntennas);

    public static float RepairHpPerS(int toolboxes) => RepairHpPerToolbox * Math.Clamp(toolboxes, 0, MaxToolboxes);

    /// <summary>Seconds between refits (infinite without spare links).</summary>
    public static float RefitEveryS(int spareSets) => spareSets <= 0 ? float.PositiveInfinity : RefitS / Math.Min(spareSets, 2);

    /// <summary>How long a fire burns here compared with on a vehicle with no vents.</summary>
    public static float FireScale(int vents) => 1f - VentFireCut * Math.Clamp(vents, 0, MaxVents);
}
