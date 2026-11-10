using System;

namespace DreamGame.Game.Audio;

/// <summary>
/// Builds the game's sound effects from scratch (filtered noise, sweeps, metallic rings), so there are no
/// audio files to license or download. Plain C# with no engine types; output is mono floats at 44.1 kHz.
/// </summary>
public static class Synth
{
    public const int Rate = 44100;

    private sealed class Rng
    {
        private uint _s;

        public Rng(uint seed) => _s = seed * 2654435761u + 0x9E3779B9u;

        /// <summary>Uniform in [-1, 1).</summary>
        public float Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return _s / 2147483648f - 1f;
        }

        public float Unit() => (Next() + 1f) * 0.5f;
    }

    private struct OnePole
    {
        private float _y;
        private readonly float _a;

        public OnePole(float cutoffHz)
        {
            _a = 1f - MathF.Exp(-2f * MathF.PI * MathF.Min(cutoffHz, Rate * 0.45f) / Rate);
            _y = 0;
        }

        public float Low(float x)
        {
            _y += _a * (x - _y);
            return _y;
        }
    }

    private static float[] Buffer(float seconds) => new float[(int)(seconds * Rate)];

    private static float Env(float t, float attack, float decay) =>
        t < 0 ? 0 : t < attack ? t / attack : MathF.Exp(-(t - attack) / decay);

    /// <summary>Band-limited noise burst: two low-pass poles at <paramref name="highHz"/>, one high-pass at <paramref name="lowHz"/>.</summary>
    private static void Noise(float[] b, Rng r, float start, float attack, float decay, float lowHz, float highHz, float amp)
    {
        var lp1 = new OnePole(highHz);
        var lp2 = new OnePole(highHz);
        var hp = new OnePole(lowHz);
        int i0 = (int)(start * Rate);
        int len = (int)((attack + decay * 7f) * Rate);
        for (int i = 0; i < len && i0 + i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float x = lp2.Low(lp1.Low(r.Next()));
            x -= hp.Low(x);
            b[i0 + i] += x * amp * Env(t, attack, decay);
        }
    }

    /// <summary>A sine whose pitch glides from f0 to f1 (exponentially) over <paramref name="glide"/> seconds.</summary>
    private static void Sweep(float[] b, float start, float f0, float f1, float glide, float attack, float decay, float amp,
        float vibratoHz = 0, float vibratoDepth = 0)
    {
        int i0 = (int)(start * Rate);
        int len = (int)((attack + decay * 7f) * Rate);
        double phase = 0;
        for (int i = 0; i < len && i0 + i < b.Length; i++)
        {
            float t = i / (float)Rate;
            float k = MathF.Min(t / MathF.Max(glide, 1e-4f), 1f);
            float f = f0 * MathF.Pow(f1 / f0, k);
            if (vibratoHz > 0) f *= 1f + vibratoDepth * MathF.Sin(2f * MathF.PI * vibratoHz * t);
            phase += 2.0 * Math.PI * f / Rate;
            b[i0 + i] += (float)Math.Sin(phase) * amp * Env(t, attack, decay);
        }
    }

    /// <summary>A struck-metal partial: a sine that rings down.</summary>
    private static void Ring(float[] b, float start, float hz, float decay, float amp) =>
        Sweep(b, start, hz, hz, 1f, 0.0008f, decay, amp);

    /// <summary>Quiet delayed copies (outdoor slap-back echo), muffled.</summary>
    private static void Echo(float[] b, float delay, float gain, float cutoffHz)
    {
        var copy = (float[])b.Clone();
        var lp = new OnePole(cutoffHz);
        int d = (int)(delay * Rate);
        for (int i = d; i < b.Length; i++) b[i] += lp.Low(copy[i - d]) * gain;
    }

    /// <summary>Soft saturation, normalize to a safe peak, and short fades so nothing clicks.</summary>
    private static float[] Finish(float[] b, float drive, float peak = 0.88f)
    {
        float norm = MathF.Tanh(drive);
        float max = 1e-6f;
        for (int i = 0; i < b.Length; i++)
        {
            b[i] = MathF.Tanh(b[i] * drive) / norm;
            max = MathF.Max(max, MathF.Abs(b[i]));
        }

        float g = peak / max;
        int fadeIn = Math.Min(b.Length, 40), fadeOut = Math.Min(b.Length, Rate / 30);
        for (int i = 0; i < b.Length; i++)
        {
            float f = 1f;
            if (i < fadeIn) f = i / (float)fadeIn;
            int fromEnd = b.Length - 1 - i;
            if (fromEnd < fadeOut) f *= fromEnd / (float)fadeOut;
            b[i] *= g * f;
        }

        return b;
    }

    /// <summary>A tank gun firing. <paramref name="size"/> is about 0.7 for 37 mm, 1 for 76 mm and 1.35 for 120 mm.</summary>
    public static float[] Cannon(float size, uint seed = 1)
    {
        var r = new Rng(seed);
        float[] b = Buffer(2.4f + size * 1.2f);
        Noise(b, r, 0, 0.0003f, 0.005f * size, 1500, 14000, 1.1f);          // supersonic crack
        Noise(b, r, 0, 0.001f, 0.085f * size, 70, 1700 / size, 1.25f);     // muzzle blast
        Sweep(b, 0, 125f / MathF.Sqrt(size), 30f, 0.28f * size, 0.002f, 0.13f * size, 1.15f); // chest thump
        Noise(b, r, 0.008f, 0.03f, 0.5f * size, 25, 240, 0.6f);            // low rumble
        Noise(b, r, 0.02f, 0.01f, 0.06f, 2500, 9000, 0.06f);               // breech clatter
        Echo(b, 0.23f, 0.22f, 900);
        Echo(b, 0.41f, 0.12f, 600);
        return Finish(b, 1.9f);
    }

    /// <summary>An explosion. <paramref name="size"/> about 0.6 (small HE) to 1.6 (ammo cook-off).</summary>
    public static float[] Explosion(float size, uint seed = 2)
    {
        var r = new Rng(seed);
        float[] b = Buffer(3.2f + size * 1.6f);
        Noise(b, r, 0, 0.0005f, 0.012f * size, 700, 12000, 0.9f);
        Noise(b, r, 0, 0.003f, 0.33f * size, 35, 1100 / size, 1.35f);
        Sweep(b, 0, 72f, 22f, 0.6f * size, 0.003f, 0.32f * size, 1.1f);
        Noise(b, r, 0.02f, 0.06f, 1.1f * size, 18, 150, 0.75f);
        // Crackle: sparse little bursts that thin out.
        int pops = (int)(40 * size);
        for (int i = 0; i < pops; i++)
        {
            float t = 0.04f + MathF.Pow(r.Unit(), 2f) * 1.3f * size;
            Noise(b, r, t, 0.0003f, 0.0035f, 1800, 9000, 0.12f + 0.12f * r.Unit());
        }

        Echo(b, 0.27f, 0.25f, 700);
        Echo(b, 0.52f, 0.14f, 450);
        return Finish(b, 2.1f);
    }

    /// <summary>A railgun: an electric snap and a falling zip as the slug leaves, a sonic crack and a hard thump.</summary>
    public static float[] RailShot(uint seed = 19)
    {
        var r = new Rng(seed);
        float[] b = Buffer(2.2f);
        Noise(b, r, 0, 0.0002f, 0.004f, 3000, 16000, 1.2f);                 // the snap of the discharge
        Sweep(b, 0, 5200, 380, 0.12f, 0.0005f, 0.07f, 0.5f);                  // the zip down the rails
        Sweep(b, 0, 2600, 190, 0.14f, 0.0005f, 0.06f, 0.25f, 60f, 0.08f);
        Noise(b, r, 0.002f, 0.0005f, 0.05f, 120, 3500, 0.7f);                 // the crack of the slug
        Sweep(b, 0, 95, 38, 0.2f, 0.002f, 0.12f, 0.8f);                       // thump
        Noise(b, r, 0.01f, 0.02f, 0.35f, 30, 260, 0.35f);                     // rumble
        Echo(b, 0.25f, 0.2f, 1200);
        return Finish(b, 1.8f);
    }

    /// <summary>A laser pulse: a short electric buzz with a crackle on top.</summary>
    public static float[] LaserZap(uint seed = 20)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.28f);
        Sweep(b, 0, 1900, 1100, 0.07f, 0.002f, 0.04f, 0.45f, 120f, 0.15f);
        Sweep(b, 0, 3800, 2200, 0.07f, 0.002f, 0.03f, 0.2f, 120f, 0.15f);
        Noise(b, r, 0, 0.001f, 0.03f, 2500, 12000, 0.25f);
        return Finish(b, 1.4f, 0.7f);
    }

    /// <summary>A shell punching through steel: sharp crack, crunch and a short metallic ring.</summary>
    public static float[] Penetration(uint seed = 3)
    {
        var r = new Rng(seed);
        float[] b = Buffer(1.1f);
        Noise(b, r, 0, 0.0002f, 0.006f, 1500, 15000, 1.0f);
        Noise(b, r, 0, 0.001f, 0.045f, 250, 4200, 0.75f);
        Sweep(b, 0, 190, 85, 0.06f, 0.001f, 0.05f, 0.65f);
        float[] partials = { 613, 1347, 2231, 3489 };
        for (int i = 0; i < partials.Length; i++)
            Ring(b, 0, partials[i] * (1f + 0.02f * r.Next()), 0.17f - i * 0.03f, 0.3f - i * 0.05f);
        return Finish(b, 1.6f);
    }

    /// <summary>A shell stopped by thick armor: a heavy, longer clang.</summary>
    public static float[] Clang(uint seed = 4)
    {
        var r = new Rng(seed);
        float[] b = Buffer(1.6f);
        Noise(b, r, 0, 0.0002f, 0.008f, 900, 12000, 0.9f);
        Sweep(b, 0, 150, 70, 0.08f, 0.001f, 0.07f, 0.8f);
        float[] partials = { 397, 941, 1723, 2689, 3797 };
        for (int i = 0; i < partials.Length; i++)
            Ring(b, 0, partials[i] * (1f + 0.015f * r.Next()), 0.4f - i * 0.06f, 0.34f - i * 0.05f);
        return Finish(b, 1.4f);
    }

    /// <summary>The classic ricochet whine.</summary>
    public static float[] Ricochet(uint seed = 5)
    {
        var r = new Rng(seed);
        float[] b = Buffer(1.0f);
        Noise(b, r, 0, 0.0002f, 0.005f, 1500, 14000, 0.8f);
        Ring(b, 0, 2400, 0.05f, 0.2f);
        Sweep(b, 0.01f, 3100, 1050, 0.55f, 0.012f, 0.32f, 0.5f, 17f, 0.035f);
        Sweep(b, 0.01f, 6200, 2100, 0.55f, 0.012f, 0.2f, 0.12f, 17f, 0.035f);
        return Finish(b, 1.3f);
    }

    /// <summary>A shell hitting the ground: thud, spray of dirt.</summary>
    public static float[] Dirt(uint seed = 6)
    {
        var r = new Rng(seed);
        float[] b = Buffer(1.4f);
        Noise(b, r, 0, 0.001f, 0.11f, 60, 1000, 1.0f);
        Sweep(b, 0, 110, 45, 0.12f, 0.002f, 0.09f, 0.8f);
        for (int i = 0; i < 18; i++)
            Noise(b, r, 0.08f + r.Unit() * 0.7f, 0.001f, 0.01f, 600, 5000, 0.06f + 0.06f * r.Unit());
        return Finish(b, 1.6f);
    }

    /// <summary>A block clunking into place. <paramref name="weight"/> 0 (light) to 1 (heavy).</summary>
    public static float[] Place(float weight, uint seed = 7)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.3f);
        Sweep(b, 0, 230 - 110 * weight, 140 - 70 * weight, 0.05f, 0.001f, 0.05f + 0.03f * weight, 1.0f);
        Noise(b, r, 0, 0.0003f, 0.006f, 500, 7000, 0.55f);
        Ring(b, 0, 880 - 300 * weight, 0.045f, 0.14f);
        return Finish(b, 1.5f, 0.8f);
    }

    public static float[] Remove(uint seed = 8)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.25f);
        Noise(b, r, 0, 0.0005f, 0.018f, 700, 8000, 0.8f);
        Sweep(b, 0, 320, 170, 0.04f, 0.001f, 0.03f, 0.55f);
        return Finish(b, 1.4f, 0.7f);
    }

    /// <summary>A small click for UI and rotating parts.</summary>
    public static float[] Tick(uint seed = 9)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.07f);
        Noise(b, r, 0, 0.0002f, 0.0025f, 2500, 14000, 0.8f);
        Ring(b, 0, 3300, 0.008f, 0.2f);
        return Finish(b, 1.2f, 0.6f);
    }

    /// <summary>A short "can't do that" buzz.</summary>
    public static float[] Error()
    {
        float[] b = Buffer(0.28f);
        foreach (float start in new[] { 0f, 0.12f })
        {
            int i0 = (int)(start * Rate);
            int len = (int)(0.075f * Rate);
            for (int i = 0; i < len && i0 + i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float env = MathF.Min(t / 0.004f, 1f) * MathF.Min((0.075f - t) / 0.01f, 1f);
                float w = 2f * MathF.PI * 150f * t;
                b[i0 + i] += (MathF.Sin(w) + MathF.Sin(3 * w) / 3f + MathF.Sin(5 * w) / 5f) * 0.6f * env;
            }
        }

        return Finish(b, 1.1f, 0.6f);
    }

    /// <summary>The breech closing on a fresh round: click, then a heavier clack.</summary>
    public static float[] Reload(uint seed = 10)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.4f);
        Noise(b, r, 0, 0.0002f, 0.004f, 2000, 11000, 0.7f);
        Ring(b, 0, 1850, 0.03f, 0.25f);
        Noise(b, r, 0.11f, 0.0003f, 0.011f, 700, 7000, 0.9f);
        Ring(b, 0.11f, 930, 0.05f, 0.3f);
        Sweep(b, 0.11f, 170, 120, 0.03f, 0.001f, 0.03f, 0.6f);
        return Finish(b, 1.4f, 0.75f);
    }

    /// <summary>
    /// A seamless one-second diesel loop: a buzzy 25 Hz firing tone with throaty noise pulses. Pitch it up
    /// with speed. The tone repeats a whole number of times and the noise filters are run twice, so the
    /// loop point doesn't click.
    /// </summary>
    public static float[] EngineLoop(uint seed = 11)
    {
        var r = new Rng(seed);
        const float f0 = 25f;
        int n = Rate;
        var b = new float[n];
        var phases = new float[16];
        for (int h = 0; h < phases.Length; h++) phases[h] = r.Unit() * MathF.Tau;

        var noise = new float[n];
        for (int i = 0; i < n; i++) noise[i] = r.Next();
        var lp1 = new OnePole(520);
        var lp2 = new OnePole(520);
        var filtered = new float[n];
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
                filtered[i] = lp2.Low(lp1.Low(noise[i]));

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float cycle = t * f0 * 4f; // four cylinders firing per base cycle
            float pulse = MathF.Exp(-(cycle - MathF.Floor(cycle)) * 5f);
            float tone = 0;
            for (int h = 1; h <= phases.Length; h++)
                tone += MathF.Sin(MathF.Tau * f0 * h * t + phases[h - 1]) / MathF.Pow(h, 0.85f);
            b[i] = tone * (0.55f + 0.45f * pulse) * 0.35f + filtered[i] * pulse * 2.2f;
        }

        float max = 1e-6f;
        foreach (float v in b) max = MathF.Max(max, MathF.Abs(v));
        for (int i = 0; i < n; i++) b[i] = MathF.Tanh(b[i] / max * 1.4f) * 0.7f;
        return b;
    }

    /// <summary>A rifle shot: sharp crack, short bark, a little echo.</summary>
    public static float[] Rifle(uint seed = 12)
    {
        var r = new Rng(seed);
        float[] b = Buffer(1.0f);
        Noise(b, r, 0, 0.0002f, 0.003f, 2000, 15000, 1.0f);
        Noise(b, r, 0, 0.0005f, 0.035f, 200, 4500, 0.95f);
        Sweep(b, 0, 170, 70, 0.03f, 0.001f, 0.03f, 0.6f);
        Noise(b, r, 0.005f, 0.01f, 0.18f, 100, 1200, 0.25f);
        Echo(b, 0.19f, 0.2f, 1500);
        return Finish(b, 1.7f);
    }

    /// <summary>A rocket leaving the tube: thump and a long whoosh.</summary>
    public static float[] RocketLaunch(uint seed = 13)
    {
        var r = new Rng(seed);
        float[] b = Buffer(1.8f);
        Sweep(b, 0, 95, 40, 0.1f, 0.002f, 0.1f, 0.9f);
        Noise(b, r, 0, 0.03f, 0.5f, 300, 5000, 0.9f);
        Noise(b, r, 0, 0.01f, 0.35f, 2000, 12000, 0.35f);
        Echo(b, 0.24f, 0.15f, 1000);
        return Finish(b, 1.5f);
    }

    /// <summary>A bullet pinging off steel.</summary>
    public static float[] BulletMetal(uint seed = 14)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.3f);
        Noise(b, r, 0, 0.0002f, 0.002f, 2500, 15000, 0.8f);
        Ring(b, 0, 2130 * (1f + 0.05f * r.Next()), 0.05f, 0.3f);
        Ring(b, 0, 3790, 0.03f, 0.2f);
        Ring(b, 0, 5230, 0.02f, 0.12f);
        return Finish(b, 1.2f, 0.7f);
    }

    /// <summary>A bullet thudding into dirt.</summary>
    public static float[] BulletDirt(uint seed = 15)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.25f);
        Noise(b, r, 0, 0.0005f, 0.02f, 200, 3000, 0.9f);
        Sweep(b, 0, 140, 80, 0.02f, 0.001f, 0.02f, 0.4f);
        return Finish(b, 1.2f, 0.6f);
    }

    /// <summary>A boot on gravel. Different seeds give different steps.</summary>
    public static float[] Footstep(uint seed)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.2f);
        Noise(b, r, 0, 0.002f, 0.025f, 100, 2500, 0.8f);
        Sweep(b, 0, 90, 60, 0.02f, 0.001f, 0.02f, 0.5f);
        for (int i = 0; i < 6; i++) Noise(b, r, r.Unit() * 0.06f, 0.0003f, 0.004f, 1500, 8000, 0.15f);
        return Finish(b, 1.2f, 0.55f);
    }

    /// <summary>Getting hit: a heavy thud.</summary>
    public static float[] Hurt(uint seed = 16)
    {
        var r = new Rng(seed);
        float[] b = Buffer(0.35f);
        Sweep(b, 0, 120, 60, 0.06f, 0.002f, 0.06f, 1f);
        Noise(b, r, 0, 0.001f, 0.05f, 80, 800, 0.6f);
        return Finish(b, 1.4f, 0.8f);
    }

    /// <summary>A seamless one-second welding loop: crackling arc and hiss.</summary>
    public static float[] WeldLoop(uint seed = 17)
    {
        var r = new Rng(seed);
        int n = Rate;
        var b = new float[n];
        var hissLp = new OnePole(9000);
        var hissHp = new OnePole(2500);
        var noise = new float[n];
        for (int i = 0; i < n; i++) noise[i] = r.Next();
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                float x = hissLp.Low(noise[i]);
                x -= hissHp.Low(x);
                if (pass == 1) b[i] = x * 0.35f + MathF.Sin(MathF.Tau * 120f * i / Rate) * 0.04f;
            }

        // Crackle: short bursts written around the loop so they wrap cleanly.
        for (int k = 0; k < 140; k++)
        {
            int start = (int)(r.Unit() * n);
            float amp = 0.2f + 0.5f * r.Unit() * r.Unit();
            int len = (int)(0.004f * Rate);
            var lp = new OnePole(7000);
            for (int i = 0; i < len; i++)
                b[(start + i) % n] += lp.Low(r.Next()) * amp * MathF.Exp(-i / (0.0012f * Rate));
        }

        float max = 1e-6f;
        foreach (float v in b) max = MathF.Max(max, MathF.Abs(v));
        for (int i = 0; i < n; i++) b[i] = MathF.Tanh(b[i] / max * 1.3f) * 0.6f;
        return b;
    }

    /// <summary>Something heavy hitting water: a slap, a gush and falling spray.</summary>
    public static float[] Splash(uint seed = 18)
    {
        var r = new Rng(seed);
        float[] b = Buffer(1.6f);
        Noise(b, r, 0, 0.001f, 0.02f, 300, 6000, 0.9f);
        Sweep(b, 0, 140, 55, 0.15f, 0.002f, 0.12f, 0.6f);
        Noise(b, r, 0.02f, 0.05f, 0.35f, 150, 2500, 0.6f);
        for (int i = 0; i < 40; i++) Noise(b, r, 0.15f + r.Unit() * 0.9f, 0.001f, 0.012f, 1500, 9000, 0.05f + 0.06f * r.Unit());
        return Finish(b, 1.4f);
    }

    /// <summary>Converts to 16-bit little-endian PCM.</summary>
    public static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Math.Clamp((int)MathF.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        return bytes;
    }
}
