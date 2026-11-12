using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamGame.Core;

/// <summary>
/// How hard an aircraft is to spot. Its signature is the share of the usual spotting range an enemy sees it from:
/// 1 for an ordinary aircraft, down to <see cref="Best"/> for one whose outside is radar-absorbent skin all over with
/// its missiles and bombs carried inside. Everything left hanging in the air (a missile on a rail, a bomb rack) gives
/// some of that back. Close in, eyes see anything: no aircraft hides inside <see cref="CloseM"/>.
/// </summary>
public static class Stealth
{
    /// <summary>The lowest signature: a third of the usual range.</summary>
    public const float Best = 0.33f;

    /// <summary>What each missile or bomb out in the air adds back.</summary>
    public const float StorePenalty = 0.06f;

    /// <summary>Within this distance an aircraft is seen whatever its skin (or less, if the eye sees less far than this anyway).</summary>
    public const float CloseM = 800f;

    private static readonly Int3[] Faces =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>Faces of a block that count toward how it looks to radar: the body, not wing panels, wheels or small fittings.</summary>
    private static bool Counts(BlockDef d) =>
        !d.IsLiftingSurface && !d.IsWheelLike && d.Kind != BlockKind.Detail && !d.IsMissilePart && d.Kind != BlockKind.MissileRail && d.Kind != BlockKind.BombRack;

    /// <summary>The share of the body's outside (faces open to the air) that is radar-absorbent, 0 to 1.</summary>
    public static float SkinShare(IReadOnlyDictionary<Int3, BlockDef> defs, ISet<Int3> gone)
    {
        int open = 0, stealthy = 0;
        foreach ((Int3 c, BlockDef d) in defs)
        {
            if (gone.Contains(c) || !Counts(d)) continue;
            foreach (Int3 f in Faces)
            {
                Int3 n = c + f;
                if (defs.ContainsKey(n) && !gone.Contains(n)) continue;
                open++;
                if (d.Stealthy) stealthy++;
            }
        }

        return open > 0 ? (float)stealthy / open : 0f;
    }

    /// <summary>Missiles and bomb racks with a face open to the air (carried outside, not in a bay).</summary>
    public static int ExposedStores(VehicleAnalysis a)
    {
        bool Open(Int3 c, ICollection<Int3> own)
        {
            foreach (Int3 f in Faces)
            {
                Int3 n = c + f;
                if (own.Contains(n)) continue;
                if (!a.Defs.ContainsKey(n) || a.Disconnected.Contains(n)) return true;
            }

            return false;
        }

        int stores = 0;
        foreach (MissileInfo m in a.Missiles.Where(m => !m.IsVls))
        {
            var own = new HashSet<Int3>(m.Parts) { m.Rail };
            if (own.Any(c => Open(c, own))) stores++;
        }

        foreach (Int3 r in a.BombRacks)
            if (Open(r, new[] { r }))
                stores++;
        return stores;
    }

    /// <summary>An aircraft's signature (see the class summary). Anything that doesn't fly is 1.</summary>
    public static float Signature(VehicleAnalysis a)
    {
        if (a.Mode is not (MobilityMode.Plane or MobilityMode.Helicopter or MobilityMode.Hover)) return 1f;
        float skin = SkinShare(a.Defs, a.Disconnected);
        float sig = 1f - (1f - Best) * skin + StorePenalty * ExposedStores(a);
        return Math.Clamp(sig, Best, 1f);
    }

    /// <summary>How far an eye that sees <paramref name="sightM"/> spots an aircraft with this signature.</summary>
    public static float SpotRangeM(float sightM, float signature) =>
        MathF.Max(sightM * Math.Clamp(signature, Best, 1f), MathF.Min(sightM, CloseM));
}
