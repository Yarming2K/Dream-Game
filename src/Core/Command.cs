using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>What a commanded unit has been told to do.</summary>
public enum OrderKind
{
    /// <summary>No orders: sits where it is and its gunners fight as their rules allow.</summary>
    None,

    /// <summary>Go to a point (in formation with the rest of its group), shooting on the way.</summary>
    Move,

    /// <summary>Close on one enemy and keep every gun on it.</summary>
    Attack,

    /// <summary>Stay beside a friendly unit and protect it.</summary>
    Escort,

    /// <summary>Go back and forth between two points.</summary>
    Patrol,

    /// <summary>Stop and hold this spot.</summary>
    Hold,
}

/// <summary>When a unit's gunners may open fire.</summary>
public enum Engagement
{
    /// <summary>At any enemy in reach.</summary>
    FireAtWill,

    /// <summary>Only at what's shooting at them, or what they were told to attack.</summary>
    ReturnFire,

    /// <summary>Not at all (sneaking past, or holding back).</summary>
    HoldFire,
}

public enum Formation
{
    /// <summary>Side by side, facing the way they're going: every gun can bear forward.</summary>
    Line,

    /// <summary>An arrowhead, the leader at the point.</summary>
    Wedge,

    /// <summary>Nose to tail: narrow, for roads, passes and channels.</summary>
    Column,

    /// <summary>A block, rows of four.</summary>
    Box,
}

/// <summary>
/// Formations: where each unit of a group goes when the group is sent somewhere. Slots are worked out
/// in the formation's own frame (x to the right, y forward) and then turned to face the way the group
/// is heading, centered on the destination.
/// </summary>
public static class Formations
{
    public static string NameOf(Formation f) => f switch
    {
        Formation.Line => "Line abreast",
        Formation.Wedge => "Wedge",
        Formation.Column => "Column",
        _ => "Box",
    };

    /// <summary>Each unit's place in the formation's own frame (x right, y forward), <paramref name="spacing"/> meters apart.</summary>
    public static List<Vector2> Slots(Formation f, int count, float spacing)
    {
        var slots = new List<Vector2>(count);
        switch (f)
        {
            case Formation.Line:
                for (int i = 0; i < count; i++) slots.Add(new Vector2((i - (count - 1) / 2f) * spacing, 0));
                break;
            case Formation.Column:
                for (int i = 0; i < count; i++) slots.Add(new Vector2(0, -i * spacing));
                break;
            case Formation.Wedge:
                slots.Add(Vector2.Zero);
                for (int i = 1; i < count; i++)
                {
                    int rank = (i + 1) / 2;
                    float side = i % 2 == 1 ? -1 : 1;
                    slots.Add(new Vector2(side * rank * spacing * 0.8f, -rank * spacing * 0.8f));
                }

                break;
            default:
            {
                int perRow = Math.Min(4, Math.Max(1, count));
                for (int i = 0; i < count; i++)
                {
                    int row = i / perRow, col = i % perRow;
                    int inRow = Math.Min(perRow, count - row * perRow);
                    slots.Add(new Vector2((col - (inRow - 1) / 2f) * spacing, -row * spacing));
                }

                break;
            }
        }

        // Center the formation on the destination.
        Vector2 mid = Vector2.Zero;
        foreach (Vector2 s in slots) mid += s;
        if (slots.Count > 0) mid /= slots.Count;
        for (int i = 0; i < slots.Count; i++) slots[i] -= mid;
        return slots;
    }

    /// <summary>
    /// World positions (x, z) for a group sent to <paramref name="dest"/> heading along <paramref name="heading"/>
    /// (a direction on the ground; anything near zero means north, -z).
    /// </summary>
    public static List<Vector2> Place(Formation f, int count, float spacing, Vector2 dest, Vector2 heading)
    {
        Vector2 fwd = heading.LengthSquared() > 1e-6f ? Vector2.Normalize(heading) : new Vector2(0, -1);
        // On the ground (x, z): forward is fwd; right is fwd turned a quarter clockwise seen from above.
        var right = new Vector2(-fwd.Y, fwd.X);
        var result = new List<Vector2>(count);
        foreach (Vector2 s in Slots(f, count, spacing)) result.Add(dest + right * s.X + fwd * s.Y);
        return result;
    }

    /// <summary>
    /// Matches units to slots so nobody crosses the whole formation to get to theirs: each slot in turn
    /// (from the front) takes the nearest unit still free. Returns, for each unit, the index of its slot.
    /// </summary>
    public static int[] Assign(IReadOnlyList<Vector2> units, IReadOnlyList<Vector2> slots)
    {
        var result = new int[units.Count];
        var free = new List<int>();
        for (int i = 0; i < units.Count; i++) free.Add(i);
        for (int s = 0; s < slots.Count && free.Count > 0; s++)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < free.Count; k++)
            {
                float d = Vector2.DistanceSquared(units[free[k]], slots[s]);
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }

            result[free[best]] = s;
            free.RemoveAt(best);
        }

        return result;
    }
}

/// <summary>Who can see what: how far each kind of unit spots others, and how far orders reach.</summary>
public static class Sensors
{
    /// <summary>How far a unit spots enemies, in meters, by how it moves (higher up and with radar, further).</summary>
    public static float SightM(MobilityMode mode, bool hasRadar, bool hasConsole)
    {
        float m = mode switch
        {
            MobilityMode.Ship => 2500f,
            MobilityMode.Submarine => 1500f,
            MobilityMode.Plane or MobilityMode.Helicopter or MobilityMode.Hover or MobilityMode.Airship => 3000f,
            _ => 1000f,
        };
        if (hasRadar) m *= 1.8f;
        if (hasConsole) m = MathF.Max(m * 1.25f, 2500f);
        return m;
    }

    /// <summary>How far a command console's orders reach.</summary>
    public const float CommandRangeM = 4000f;
}
