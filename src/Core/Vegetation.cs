using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

public enum PlantKind : byte
{
    Pine,
    Broadleaf,
    Bush,
    Rock,
    Boulder,
}

/// <summary>One tree, bush or rock on the island.</summary>
public struct Plant
{
    public PlantKind Kind;
    public float X, Z;

    /// <summary>Which way it faces (radians) and how big it is against the model's own size.</summary>
    public float Yaw, Scale;

    /// <summary>Knocked flat or blown down (a tree), crushed (a bush).</summary>
    public bool Down;

    /// <summary>Which way it fell (radians round Y), when it's down.</summary>
    public float FellToward;

    public readonly bool IsTree => Kind is PlantKind.Pine or PlantKind.Broadleaf;
    public readonly bool IsRock => Kind is PlantKind.Rock or PlantKind.Boulder;

    /// <summary>The trunk's (or rock's) radius at this size, for bumping into it.</summary>
    public readonly float RadiusM => Kind switch
    {
        PlantKind.Pine => 0.22f * Scale,
        PlantKind.Broadleaf => 0.3f * Scale,
        PlantKind.Rock => 0.7f * Scale,
        PlantKind.Boulder => 1.4f * Scale,
        _ => 0.9f * Scale,
    };
}

/// <summary>
/// Where the trees, bushes and rocks grow, and what knocks them down. Woods grow in clumps (a noise field), bushes
/// round their edges and scattered in the open, rocks on the slopes. Nothing grows on the beach, under water, on
/// cliffs too steep to hold a tree, or anywhere the caller keeps clear (the firing lane, the airstrip, pads, roads
/// and buildings). The same seed always gives the same island, which multiplayer will need.
/// </summary>
public static class Vegetation
{
    /// <summary>Spacing of the jittered grid plants are tried on, in meters.</summary>
    public const float Spacing = 5f;

    /// <summary>How hard a vehicle must hit a tree to knock it flat: mass times speed (kg m/s).</summary>
    public const float KnockMomentum = 18000f;

    public static List<Plant> Scatter(HeightField field, int seed, float landHeight, Func<float, float, bool> keepClear)
    {
        var plants = new List<Plant>();
        float x0 = field.Origin.X, z0 = field.Origin.Y;
        float x1 = x0 + (field.Width - 1) * field.Spacing, z1 = z0 + (field.Depth - 1) * field.Spacing;
        int nx = (int)((x1 - x0) / Spacing), nz = (int)((z1 - z0) / Spacing);
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            uint h = Hash(ix, iz, seed);
            float x = x0 + (ix + U(h, 0)) * Spacing, z = z0 + (iz + U(h, 1)) * Spacing;
            float y = field.Sample(x, z, out Vector3 n);
            if (y < landHeight + 1.2f) continue; // beach and sea
            float slope = 1f - n.Y;
            float wood = Fbm(x * 0.012f, z * 0.012f, seed);
            float roll = U(h, 2);
            PlantKind? kind = null;
            if (slope > 0.12f && roll < 0.05f + slope * 0.6f) kind = slope > 0.3f || U(h, 5) < 0.4f ? PlantKind.Boulder : PlantKind.Rock;
            else if (slope > 0.45f) continue; // too steep to grow on
            else if (wood > 0.58f && roll < 0.75f) kind = U(h, 3) < Math.Clamp((y - landHeight) / 25f, 0.15f, 0.85f) ? PlantKind.Pine : PlantKind.Broadleaf;
            else if (wood > 0.48f && roll < 0.45f) kind = PlantKind.Bush;
            else if (roll < 0.035f) kind = U(h, 3) < 0.6f ? PlantKind.Bush : U(h, 4) < 0.5f ? PlantKind.Rock : PlantKind.Broadleaf;
            if (kind == null) continue;

            var p = new Plant { Kind = kind.Value, X = x, Z = z, Yaw = U(h, 6) * MathF.Tau, Scale = 0.75f + U(h, 7) * 0.55f };
            if (keepClear(x, z) || keepClear(x + p.RadiusM * 2f, z) || keepClear(x - p.RadiusM * 2f, z) ||
                keepClear(x, z + p.RadiusM * 2f) || keepClear(x, z - p.RadiusM * 2f)) continue;
            plants.Add(p);
        }

        return plants;
    }

    /// <summary>
    /// The world's trees, bushes and rocks in one square patch, from what covers the ground (<paramref name="at"/> gives
    /// height, steepness and cover at a point): woods where the cover says trees grow (pines up toward the tree line,
    /// broadleaf lower down), bushes round them and scattered on dry ground, rocks and boulders on rocky slopes and screes.
    /// Nothing on snow, beaches or anything too steep to hold a tree. The same place always grows the same plants.
    /// </summary>
    public static List<Plant> ScatterWorld(float x0, float z0, float size, int seed, Func<float, float, (float h, float slope, GroundCover c)> at)
    {
        var plants = new List<Plant>();
        int n = (int)(size / Spacing);
        int gx0 = (int)MathF.Floor(x0 / Spacing), gz0 = (int)MathF.Floor(z0 / Spacing);
        for (int iz = 0; iz < n; iz++)
        for (int ix = 0; ix < n; ix++)
        {
            uint h = Hash(gx0 + ix, gz0 + iz, seed);
            float x = (gx0 + ix + U(h, 0)) * Spacing, z = (gz0 + iz + U(h, 1)) * Spacing;
            (float y, float slope, GroundCover c) = at(x, z);
            if (y < 1.6f || c.Snow > 0.4f || c.Sand > 0.5f) continue;
            float wood = Fbm(x * 0.012f, z * 0.012f, seed);
            float roll = U(h, 2);
            float rocky = MathF.Max(c.Rock, c.Gravel * 0.5f);
            PlantKind? kind = null;
            if (rocky > 0.25f && roll < 0.02f + rocky * 0.1f) kind = slope > 0.3f || U(h, 5) < 0.45f ? PlantKind.Boulder : PlantKind.Rock;
            else if (slope > 0.42f) continue;
            else if (c.Forest > 0.02f && wood > 0.62f - c.Forest * 0.3f && roll < MathF.Min(0.8f, c.Forest * 1.5f))
                kind = U(h, 3) < 0.2f + 0.75f * c.Upland ? PlantKind.Pine : PlantKind.Broadleaf;
            else if (wood > 0.5f - c.Forest * 0.2f && roll < 0.3f * (c.Forest + 0.15f)) kind = PlantKind.Bush;
            else if (roll < 0.012f + 0.02f * c.Dry) kind = U(h, 3) < 0.65f ? PlantKind.Bush : PlantKind.Rock;
            if (kind == null) continue;
            plants.Add(new Plant { Kind = kind.Value, X = x, Z = z, Yaw = U(h, 6) * MathF.Tau, Scale = 0.75f + U(h, 7) * 0.55f });
        }

        return plants;
    }

    /// <summary>
    /// A vehicle of <paramref name="massKg"/> moving at <paramref name="speed"/> into a tree: whether it goes over.
    /// A bush always goes flat.
    /// </summary>
    public static bool Knocks(in Plant p, float massKg, float speed) =>
        !p.Down && !p.IsRock && (p.Kind == PlantKind.Bush ? speed > 1f : massKg * speed > KnockMomentum * p.Scale * p.Scale);

    /// <summary>
    /// A blast of <paramref name="radius"/> at <paramref name="center"/>: trees in reach fall away from it (and
    /// bushes go flat). Returns the indices that changed.
    /// </summary>
    public static List<int> Blast(List<Plant> plants, Vector2 center, float radius, float damage)
    {
        var hit = new List<int>();
        float reach = radius * (damage > 1000f ? 1.6f : 1.2f);
        for (int i = 0; i < plants.Count; i++)
        {
            Plant p = plants[i];
            if (p.Down || p.IsRock) continue;
            var d = new Vector2(p.X - center.X, p.Z - center.Y);
            float dist = d.Length();
            if (dist > reach + p.RadiusM) continue;
            p.Down = true;
            p.FellToward = dist > 0.01f ? MathF.Atan2(d.X, d.Y) : p.Yaw;
            plants[i] = p;
            hit.Add(i);
        }

        return hit;
    }

    // ------------------------------------------------------------------ deterministic noise

    private static uint Hash(int x, int z, int seed)
    {
        uint h = (uint)x * 73856093u ^ (uint)z * 19349663u ^ (uint)seed * 83492791u;
        h ^= h >> 13;
        h *= 1274126177u;
        h ^= h >> 16;
        return h;
    }

    /// <summary>A number in [0, 1) from a hash and a channel.</summary>
    private static float U(uint h, int channel)
    {
        uint k = h ^ ((uint)channel * 2654435761u);
        k ^= k >> 15;
        k *= 2246822519u;
        k ^= k >> 13;
        return (k & 0xFFFFFF) / (float)0x1000000;
    }

    private static float Value(float x, float z, int seed)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx);
        fz = fz * fz * (3 - 2 * fz);
        float a = U(Hash(ix, iz, seed), 9), b = U(Hash(ix + 1, iz, seed), 9);
        float c = U(Hash(ix, iz + 1, seed), 9), d = U(Hash(ix + 1, iz + 1, seed), 9);
        return a + (b - a) * fx + (c - a) * fz + (a - b - c + d) * fx * fz;
    }

    private static float Fbm(float x, float z, int seed) =>
        Value(x, z, seed) * 0.55f + Value(x * 2.1f + 13f, z * 2.1f + 7f, seed) * 0.3f + Value(x * 4.3f - 5f, z * 4.3f + 11f, seed) * 0.15f;
}
