using System;

namespace DreamGame.Core;

/// <summary>
/// Seeded noise for making terrain, using only plain arithmetic, so the same seed gives the same world on every
/// machine (multiplayer and the simplified far-off battles will need that). Gradient noise is about -1 to 1 and
/// smooth; one unit of input is one "cell" of the noise, so scale the inputs by the size of feature wanted.
/// </summary>
public static class Noise
{
    public static uint Hash(int x, int z, int seed)
    {
        uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return h;
    }

    /// <summary>A number in [0, 1) from a hash.</summary>
    public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

    private static float Grad(uint h, float x, float z) => (h & 7) switch
    {
        0 => x + z,
        1 => x - z,
        2 => -x + z,
        3 => -x - z,
        4 => x * 1.4142f,
        5 => -x * 1.4142f,
        6 => z * 1.4142f,
        _ => -z * 1.4142f,
    };

    /// <summary>Smooth gradient noise, about -1 to 1, zero at whole-number points.</summary>
    public static float Gradient(float x, float z, int seed)
    {
        int ix = FastFloor(x), iz = FastFloor(z);
        float fx = x - ix, fz = z - iz;
        float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        float uz = fz * fz * fz * (fz * (fz * 6f - 15f) + 10f);
        float a = Grad(Hash(ix, iz, seed), fx, fz);
        float b = Grad(Hash(ix + 1, iz, seed), fx - 1f, fz);
        float c = Grad(Hash(ix, iz + 1, seed), fx, fz - 1f);
        float d = Grad(Hash(ix + 1, iz + 1, seed), fx - 1f, fz - 1f);
        float ab = a + (b - a) * ux, cd = c + (d - c) * ux;
        return (ab + (cd - ab) * uz) * 0.72f;
    }

    /// <summary>Layers of gradient noise, each twice as fine and half as strong: about -1 to 1.</summary>
    public static float Fbm(float x, float z, int seed, int octaves, float lacunarity = 2.03f, float gain = 0.5f)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += Gradient(x, z, seed + o * 1013) * amp;
            norm += amp;
            amp *= gain;
            // Rotate a little between octaves so the layers don't line up along the axes.
            float nx = x * 0.8f - z * 0.6f, nz = x * 0.6f + z * 0.8f;
            x = nx * lacunarity + 17.3f;
            z = nz * lacunarity - 9.1f;
        }

        return sum / norm;
    }

    /// <summary>
    /// Ridged noise for mountains: sharp crests where the noise crosses zero, each finer layer strongest on the crests
    /// of the one before. 0 to about 1.
    /// </summary>
    public static float Ridged(float x, float z, int seed, int octaves, float lacunarity = 2.1f, float gain = 0.5f)
    {
        float sum = 0f, amp = 1f, norm = 0f, weight = 1f;
        for (int o = 0; o < octaves; o++)
        {
            float n = 1f - MathF.Abs(Gradient(x, z, seed + o * 733) * 1.25f);
            n = Math.Clamp(n, 0f, 1f);
            n *= n;
            n *= weight;
            weight = Math.Clamp(n * 1.6f, 0f, 1f);
            sum += n * amp;
            norm += amp;
            amp *= gain;
            float nx = x * 0.8f - z * 0.6f, nz = x * 0.6f + z * 0.8f;
            x = nx * lacunarity + 5.7f;
            z = nz * lacunarity + 11.9f;
        }

        return sum / norm;
    }

    /// <summary>
    /// Distance to the nearest of a scatter of points (one per cell, jittered), and the hash of that point: for
    /// rounded lumps such as buttes and lava domes.
    /// </summary>
    public static float Cells(float x, float z, int seed, out uint id)
    {
        int ix = FastFloor(x), iz = FastFloor(z);
        float best = float.MaxValue;
        id = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            uint h = Hash(ix + dx, iz + dz, seed);
            float px = ix + dx + 0.15f + Unit(h) * 0.7f, pz = iz + dz + 0.15f + Unit(h * 0x9E3779B9u + 1u) * 0.7f;
            float d = (px - x) * (px - x) + (pz - z) * (pz - z);
            if (d < best)
            {
                best = d;
                id = h;
            }
        }

        return MathF.Sqrt(best);
    }

    public static int FastFloor(float x)
    {
        int i = (int)x;
        return x < i ? i - 1 : i;
    }

    public static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

/// <summary>A small, fast, seeded random number generator (SplitMix64): the same seed gives the same numbers everywhere.</summary>
public struct Rng
{
    private ulong _s;

    public Rng(ulong seed) => _s = seed * 0x9E3779B97F4A7C15ul + 0x632BE59BD9B4E019ul;

    public ulong Next()
    {
        ulong z = _s += 0x9E3779B97F4A7C15ul;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
        return z ^ (z >> 31);
    }

    /// <summary>A number in [0, 1).</summary>
    public float Float() => (Next() >> 40) * (1f / 16777216f);

    public float Range(float a, float b) => a + (b - a) * Float();

    public int Int(int maxExclusive) => (int)((Next() >> 33) % (ulong)maxExclusive);
}
