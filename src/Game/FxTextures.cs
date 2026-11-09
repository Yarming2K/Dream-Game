using System;
using Godot;

namespace DreamGame.Game;

/// <summary>Soft particle and decal textures, generated once at startup (no image files needed).</summary>
public static class FxTextures
{
    private static ImageTexture _puff, _spark, _flare, _ring, _scorch, _hole, _scuff;
    private static readonly FastNoiseLite Noise = new() { Frequency = 0.09f, FractalOctaves = 3, Seed = 3 };

    private static ImageTexture Make(int size, Func<float, float, float, Color> pixel)
    {
        Image img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
            float r = MathF.Sqrt(u * u + v * v);
            float n = Noise.GetNoise2D(x, y) * 0.5f + 0.5f;
            Color c = pixel(r, n, MathF.Atan2(v, u));
            c.A = Math.Clamp(c.A, 0f, 1f);
            img.SetPixel(x, y, c);
        }

        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>A soft, slightly lumpy round puff for smoke, dust and fire.</summary>
    public static Texture2D Puff => _puff ??= Make(96, (r, n, _) =>
    {
        float a = 1f - Smooth(0.2f, 1f, r + (n - 0.5f) * 0.35f);
        return new Color(1, 1, 1, a * (0.7f + 0.3f * n));
    });

    /// <summary>A hot dot with a quick falloff, for sparks and embers.</summary>
    public static Texture2D Spark => _spark ??= Make(32, (r, _, _) =>
        new Color(1, 1, 1, MathF.Exp(-r * r * 9f)));

    /// <summary>A bright core with a wide glow, for flashes.</summary>
    public static Texture2D Flare => _flare ??= Make(128, (r, _, _) =>
        new Color(1, 1, 1, MathF.Min(1f, MathF.Exp(-r * 5f) * 0.9f + MathF.Exp(-r * r * 60f))));

    /// <summary>A thin soft ring, for blast shockwaves.</summary>
    public static Texture2D Ring => _ring ??= Make(128, (r, n, _) =>
    {
        float d = (r - 0.78f) / 0.11f;
        return new Color(1, 1, 1, MathF.Exp(-d * d) * (0.75f + 0.25f * n));
    });

    /// <summary>A blackened blast mark with a ragged edge, for the ground.</summary>
    public static Texture2D Scorch => _scorch ??= Make(128, (r, n, ang) =>
    {
        float edge = r + (n - 0.5f) * 0.45f + MathF.Sin(ang * 7f) * 0.04f;
        float a = (1f - Smooth(0.35f, 0.95f, edge)) * 0.92f;
        float dark = 0.03f + 0.06f * Smooth(0f, 0.8f, r);
        return new Color(dark, dark * 0.92f, dark * 0.85f, a);
    });

    /// <summary>A punched hole with a burnt, bright-metal rim, for penetrations.</summary>
    public static Texture2D Hole => _hole ??= Make(64, (r, n, _) =>
    {
        if (r < 0.2f) return new Color(0.01f, 0.01f, 0.01f, 1f);
        float rim = MathF.Exp(-MathF.Pow((r - 0.27f) / 0.06f, 2f));
        float burn = 1f - Smooth(0.25f, 0.85f, r + (n - 0.5f) * 0.3f);
        var c = new Color(0.05f, 0.045f, 0.04f).Lerp(new Color(0.6f, 0.55f, 0.48f), rim * 0.7f);
        return new Color(c.R, c.G, c.B, MathF.Max(burn * 0.85f, rim));
    });

    /// <summary>A grey scuff, for shells that didn't get through.</summary>
    public static Texture2D Scuff => _scuff ??= Make(64, (r, n, _) =>
    {
        float a = (1f - Smooth(0.15f, 0.9f, r + (n - 0.5f) * 0.4f)) * 0.75f;
        float g = 0.25f + 0.25f * n;
        return new Color(g, g, g * 0.95f, a);
    });
}
