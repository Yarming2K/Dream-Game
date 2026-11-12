using System;

namespace DreamGame.Core;

/// <summary>
/// Rain wearing the land down: many drops are let fall on the height grid one after another, each running downhill,
/// picking up earth where it speeds up and dropping it where it slows, until it dries up or reaches the sea. Over a few
/// hundred thousand drops this cuts branching gullies and valleys into the slopes, sharpens the ridges between them and
/// spreads fans of earth where they come out onto flatter ground: what makes generated mountains look like real ones.
/// Deterministic (one drop at a time, from a seeded generator).
/// </summary>
public static class Erosion
{
    public sealed class Settings
    {
        /// <summary>Heights are divided by this many meters while eroding, so the drops' rules work at any scale.</summary>
        public float HeightUnit = 300f;

        public int Radius = 3;
        public float Inertia = 0.05f;
        public float Capacity = 4f;
        public float MinCapacity = 0.01f;
        public float ErodeRate = 0.3f;
        public float DepositRate = 0.3f;
        public float Evaporation = 0.012f;
        public float Gravity = 4f;
        public int Lifetime = 40;

        /// <summary>Drops only start on land at least this high, and stop (dropping what they carry) once below the sea.</summary>
        public float SeaLevel = 0f;
    }

    /// <summary>Lets <paramref name="drops"/> drops fall on <paramref name="h"/> (<paramref name="w"/> by <paramref name="d"/> samples, row by row).</summary>
    public static void Rain(float[] h, int w, int d, int drops, ulong seed, Settings s = null)
    {
        s ??= new Settings();
        float inv = 1f / s.HeightUnit;
        for (int i = 0; i < h.Length; i++) h[i] *= inv;
        float sea = s.SeaLevel * inv;
        (int[] offsets, float[] weights) = Brush(s.Radius, w);
        int r = s.Radius;
        var rng = new Rng(seed);

        for (int n = 0; n < drops; n++)
        {
            // Start somewhere on land.
            float px = 0, pz = 0;
            bool found = false;
            for (int tries = 0; tries < 8 && !found; tries++)
            {
                px = rng.Range(r + 1, w - r - 2);
                pz = rng.Range(r + 1, d - r - 2);
                found = h[(int)pz * w + (int)px] > sea + 2f * inv;
            }

            if (!found) continue;
            float dx = 0, dz = 0, speed = 1f, water = 1f, sediment = 0f;
            for (int life = 0; life < s.Lifetime; life++)
            {
                int nx = (int)px, nz = (int)pz;
                int node = nz * w + nx;
                float ox = px - nx, oz = pz - nz;
                float height = HeightAndGradient(h, w, px, pz, out float gx, out float gz);

                dx = dx * s.Inertia - gx * (1f - s.Inertia);
                dz = dz * s.Inertia - gz * (1f - s.Inertia);
                float len = MathF.Sqrt(dx * dx + dz * dz);
                if (len < 1e-9f) break;
                dx /= len;
                dz /= len;
                px += dx;
                pz += dz;
                if (px < r + 1 || pz < r + 1 || px >= w - r - 2 || pz >= d - r - 2) break;

                float newHeight = HeightAndGradient(h, w, px, pz, out _, out _);
                float dh = newHeight - height;
                if (newHeight < sea)
                {
                    // Into the sea: what it carries is washed away (so fjords and bays stay open).
                    break;
                }

                float capacity = MathF.Max(-dh * speed * water * s.Capacity, s.MinCapacity);
                if (sediment > capacity || dh > 0)
                {
                    float drop = dh > 0 ? MathF.Min(dh, sediment) : (sediment - capacity) * s.DepositRate;
                    sediment -= drop;
                    Deposit(h, w, node, ox, oz, drop);
                }
                else
                {
                    float take = MathF.Min((capacity - sediment) * s.ErodeRate, -dh);
                    for (int k = 0; k < offsets.Length; k++)
                    {
                        int j = node + offsets[k];
                        float amount = take * weights[k];
                        h[j] -= amount;
                        sediment += amount;
                    }
                }

                speed = MathF.Sqrt(MathF.Max(0f, speed * speed - dh * s.Gravity));
                water *= 1f - s.Evaporation;
            }
        }

        for (int i = 0; i < h.Length; i++) h[i] *= s.HeightUnit;
    }

    private static void Deposit(float[] h, int w, int node, float ox, float oz, float amount)
    {
        if (amount <= 0) return;
        h[node] += amount * (1 - ox) * (1 - oz);
        h[node + 1] += amount * ox * (1 - oz);
        h[node + w] += amount * (1 - ox) * oz;
        h[node + w + 1] += amount * ox * oz;
    }

    private static float HeightAndGradient(float[] h, int w, float px, float pz, out float gx, out float gz)
    {
        int x = (int)px, z = (int)pz;
        float ox = px - x, oz = pz - z;
        int i = z * w + x;
        float nw = h[i], ne = h[i + 1], sw = h[i + w], se = h[i + w + 1];
        gx = (ne - nw) * (1 - oz) + (se - sw) * oz;
        gz = (sw - nw) * (1 - ox) + (se - ne) * ox;
        return nw * (1 - ox) * (1 - oz) + ne * ox * (1 - oz) + sw * (1 - ox) * oz + se * ox * oz;
    }

    /// <summary>The samples round a node a drop wears away, weighted most in the middle (weights add up to 1).</summary>
    private static (int[] offsets, float[] weights) Brush(int radius, int w)
    {
        int n = 0;
        var offs = new int[(2 * radius + 1) * (2 * radius + 1)];
        var wts = new float[offs.Length];
        float sum = 0;
        for (int z = -radius; z <= radius; z++)
        for (int x = -radius; x <= radius; x++)
        {
            float dist = MathF.Sqrt(x * x + z * z);
            if (dist > radius) continue;
            float wt = 1f - dist / (radius + 1e-3f) + 0.05f;
            offs[n] = z * w + x;
            wts[n] = wt;
            sum += wt;
            n++;
        }

        Array.Resize(ref offs, n);
        Array.Resize(ref wts, n);
        for (int i = 0; i < n; i++) wts[i] /= sum;
        return (offs, wts);
    }
}
