using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

public enum LiveryPattern
{
    /// <summary>No paint job: every block keeps its own color.</summary>
    Factory,
    Plain,

    /// <summary>Soft-edged patches of two more colors (woodland, desert, arctic).</summary>
    Blotches,

    /// <summary>The same patches, snapped to a grid of small squares.</summary>
    Digital,

    /// <summary>Bold slanted bands (dazzle, tiger stripes).</summary>
    Stripes,

    /// <summary>
    /// Aircraft countershading: soft patches of the first two colors on top and down the sides, and the third on
    /// every surface that faces the ground (the belly and the undersides of the wings), so it's dark against the
    /// ground seen from above and pale against the sky seen from below.
    /// </summary>
    Countershade,
}

/// <summary>A vehicle's paint job. Colors are 0-1 sRGB.</summary>
public sealed class Livery
{
    public string Id;
    public string Name;
    public string Description;
    public LiveryPattern Pattern;
    public Vector3 Base, Second, Third;

    /// <summary>Roughly how big the pattern's patches are, in meters.</summary>
    public float Scale = 2.5f;

    /// <summary>A bare-metal finish instead of paint: how metallic and how rough it is (below 0: ordinary paint).</summary>
    public float Metal = -1f, Rough = -1f;

    public bool IsBareMetal => Metal >= 0;

    public bool IsFactory => Pattern == LiveryPattern.Factory;
}

/// <summary>
/// The paint jobs a design can wear, and the pattern math. The same math runs in the game's paint shader
/// (written to match line for line, including the integer hash), so the far models and the previews show
/// the same patches as the real thing.
/// <para>Ships get ship rules on top of any paint job but the factory one: red anti-fouling below the
/// waterline, a black boot-top stripe along it, and grey decks.</para>
/// </summary>
public static class Liveries
{
    public static readonly Vector3 AntiFouling = new(0.45f, 0.13f, 0.1f);
    public static readonly Vector3 BootTop = new(0.08f, 0.08f, 0.08f);
    public static readonly Vector3 Deck = new(0.33f, 0.33f, 0.32f);

    /// <summary>Half the height of the boot-top stripe, in meters.</summary>
    public const float BootTopHalfM = 0.25f;

    private static Vector3 C(float r, float g, float b) => new(r, g, b);

    public static readonly List<Livery> All = new()
    {
        new() { Id = "factory", Name = "Factory", Pattern = LiveryPattern.Factory, Description = "No paint: every block shows its own color." },
        new() { Id = "olive", Name = "Olive drab", Pattern = LiveryPattern.Plain, Base = C(0.36f, 0.38f, 0.24f), Description = "One flat army green." },
        new()
        {
            Id = "woodland", Name = "Woodland", Pattern = LiveryPattern.Blotches, Base = C(0.33f, 0.37f, 0.22f), Second = C(0.24f, 0.19f, 0.13f),
            Third = C(0.11f, 0.11f, 0.1f), Scale = 1.5f, Description = "Green, brown and black patches for forests and fields.",
        },
        new()
        {
            Id = "desert", Name = "Desert", Pattern = LiveryPattern.Blotches, Base = C(0.7f, 0.62f, 0.45f), Second = C(0.55f, 0.44f, 0.3f),
            Third = C(0.4f, 0.34f, 0.25f), Scale = 2.8f, Description = "Sand with tan and brown patches.",
        },
        new()
        {
            Id = "arctic", Name = "Arctic", Pattern = LiveryPattern.Blotches, Base = C(0.86f, 0.87f, 0.86f), Second = C(0.6f, 0.62f, 0.63f),
            Third = C(0.36f, 0.38f, 0.38f), Scale = 3f, Description = "White, with grey patches where the whitewash has worn.",
        },
        new()
        {
            Id = "urban", Name = "Urban digital", Pattern = LiveryPattern.Digital, Base = C(0.52f, 0.53f, 0.53f), Second = C(0.33f, 0.34f, 0.35f),
            Third = C(0.17f, 0.18f, 0.19f), Scale = 1.8f, Description = "Greys in small squares, for streets and concrete.",
        },
        new()
        {
            Id = "tiger", Name = "Tiger stripes", Pattern = LiveryPattern.Stripes, Base = C(0.62f, 0.5f, 0.3f), Second = C(0.3f, 0.32f, 0.2f),
            Third = C(0.12f, 0.11f, 0.1f), Scale = 1.6f, Description = "Bold slanted bands of tan, green and black.",
        },
        new()
        {
            Id = "naval", Name = "Naval grey", Pattern = LiveryPattern.Plain, Base = C(0.5f, 0.53f, 0.55f),
            Description = "Haze grey. On ships, red below the waterline, a black stripe along it and grey decks.",
        },
        new()
        {
            Id = "dazzle", Name = "Dazzle", Pattern = LiveryPattern.Stripes, Base = C(0.78f, 0.8f, 0.8f), Second = C(0.24f, 0.3f, 0.38f),
            Third = C(0.1f, 0.11f, 0.13f), Scale = 4.5f, Description = "Slashing stripes that make it hard to judge a ship's heading and speed.",
        },
        new()
        {
            Id = "night", Name = "Night", Pattern = LiveryPattern.Blotches, Base = C(0.17f, 0.19f, 0.22f), Second = C(0.11f, 0.12f, 0.15f),
            Third = C(0.24f, 0.26f, 0.3f), Scale = 3.5f, Description = "Dark blue-greys for night raids.",
        },
        new()
        {
            Id = "sky", Name = "Sky", Pattern = LiveryPattern.Plain, Base = C(0.5f, 0.56f, 0.62f), Description = "Pale blue-grey, for aircraft seen from below.",
        },
        new()
        {
            Id = "lowvis", Name = "Low-vis grey", Pattern = LiveryPattern.Blotches, Base = C(0.6f, 0.62f, 0.64f), Second = C(0.46f, 0.48f, 0.51f),
            Third = C(0.46f, 0.48f, 0.51f), Scale = 4.5f, Description = "Two soft greys in big patches, the scheme of modern fighters: hard to pick out against the sky at any height.",
        },
        new()
        {
            Id = "stealth", Name = "Stealth grey", Pattern = LiveryPattern.Plain, Base = C(0.33f, 0.34f, 0.36f),
            Description = "One flat dark grey, like the coating on the newest aircraft.",
        },
        new()
        {
            Id = "tan", Name = "Desert tan", Pattern = LiveryPattern.Plain, Base = C(0.72f, 0.64f, 0.5f),
            Description = "One flat sand color, the plain modern finish of trucks and launchers in dry country.",
        },
        new()
        {
            Id = "forest_digital", Name = "Forest digital", Pattern = LiveryPattern.Digital, Base = C(0.35f, 0.39f, 0.26f), Second = C(0.22f, 0.25f, 0.17f),
            Third = C(0.4f, 0.34f, 0.24f), Scale = 2f, Description = "Greens and brown in small squares: modern army camouflage for woods and fields.",
        },
        new()
        {
            Id = "desert_digital", Name = "Desert digital", Pattern = LiveryPattern.Digital, Base = C(0.74f, 0.66f, 0.5f), Second = C(0.6f, 0.5f, 0.36f),
            Third = C(0.5f, 0.43f, 0.32f), Scale = 2f, Description = "Sand and tans in small squares: modern army camouflage for deserts.",
        },
        new()
        {
            Id = "olive_grey", Name = "Olive drab over grey", Pattern = LiveryPattern.Countershade, Base = C(0.3f, 0.31f, 0.2f), Second = C(0.27f, 0.28f, 0.18f),
            Third = C(0.56f, 0.57f, 0.56f), Scale = 3.5f,
            Description = "Second World War aircraft: flat olive drab on top and down the sides, neutral grey underneath.",
        },
        new()
        {
            Id = "temperate", Name = "Green and earth over sky", Pattern = LiveryPattern.Countershade, Base = C(0.26f, 0.3f, 0.19f), Second = C(0.4f, 0.32f, 0.21f),
            Third = C(0.67f, 0.7f, 0.6f), Scale = 3.5f,
            Description = "Second World War fighters: big patches of dark green and dark earth on top, a pale duck-egg green underneath.",
        },
        new()
        {
            Id = "grey_mottle", Name = "Grey mottle over blue", Pattern = LiveryPattern.Countershade, Base = C(0.4f, 0.42f, 0.43f), Second = C(0.3f, 0.33f, 0.31f),
            Third = C(0.62f, 0.69f, 0.75f), Scale = 2.2f,
            Description = "Second World War fighters: two greys in soft patches on top, light blue underneath.",
        },
        new()
        {
            Id = "natural_metal", Name = "Natural metal", Pattern = LiveryPattern.Plain, Base = C(0.74f, 0.76f, 0.78f), Metal = 0.9f, Rough = 0.26f,
            Description = "Bare polished aluminium, the shine of late-war propeller fighters and the first jets: lighter and faster without paint.",
        },
        new()
        {
            Id = "sea_camo", Name = "Jungle over white", Pattern = LiveryPattern.Countershade, Base = C(0.42f, 0.39f, 0.29f), Second = C(0.26f, 0.31f, 0.2f),
            Third = C(0.8f, 0.8f, 0.77f), Scale = 3f,
            Description = "Cold War jets over jungle: tan and green on top, off-white underneath.",
        },
        new()
        {
            Id = "prototype", Name = "Prototype", Pattern = LiveryPattern.Stripes, Base = C(0.86f, 0.87f, 0.88f), Second = C(0.15f, 0.16f, 0.18f),
            Third = C(0.68f, 0.7f, 0.72f), Scale = 4f,
            Description = "A test aircraft's paint: white and pale grey with bold black slashes, to show its shape and motion clearly on camera.",
        },
    };

    public static Livery Get(string id)
    {
        foreach (Livery l in All)
            if (l.Id == id) return l;
        return All[0];
    }

    /// <summary>A design's paint job: the one it was given, or for ships with none, naval grey.</summary>
    public static Livery For(Blueprint bp, bool naval) =>
        !string.IsNullOrEmpty(bp?.Paint) ? Get(bp.Paint) : naval ? Get("naval") : All[0];

    /// <summary>Whether a part of a block gets painted (its own color does; steel, rubber, glass and lamps don't, nor do unpainted materials).</summary>
    public static bool Paints(BlockDef def, Paint finish) => !def.Glass && !def.Unpainted && finish is Paint.Self or Paint.SelfDark;

    // ------------------------------------------------------------- pattern math (mirrored in the paint shader)

    private static uint Hash(int x, int y, int z)
    {
        uint h = unchecked(((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ ((uint)z * 83492791u));
        h ^= h >> 13;
        h = unchecked(h * 1274126177u);
        h ^= h >> 16;
        return h;
    }

    private static float H01(int x, int y, int z) => (Hash(x, y, z) & 65535u) / 65535f;

    public static float Noise(Vector3 q)
    {
        int x = (int)MathF.Floor(q.X), y = (int)MathF.Floor(q.Y), z = (int)MathF.Floor(q.Z);
        float fx = q.X - x, fy = q.Y - y, fz = q.Z - z;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        fz = fz * fz * (3 - 2 * fz);
        static float L(float a, float b, float t) => a + (b - a) * t;
        float a = L(L(H01(x, y, z), H01(x + 1, y, z), fx), L(H01(x, y + 1, z), H01(x + 1, y + 1, z), fx), fy);
        float b = L(L(H01(x, y, z + 1), H01(x + 1, y, z + 1), fx), L(H01(x, y + 1, z + 1), H01(x + 1, y + 1, z + 1), fx), fy);
        return L(a, b, fz);
    }

    public static float Fbm(Vector3 q) =>
        Noise(q) * 0.6f + Noise(q * 2.03f + new Vector3(17f, 3f, 9f)) * 0.3f + Noise(q * 4.07f + new Vector3(-7f, 11f, 5f)) * 0.1f;

    /// <summary>The paint job's color at a point on the vehicle (meters, in the vehicle's own frame).</summary>
    /// <summary>
    /// The color at a point on a surface facing <paramref name="normal"/>: as <see cref="ColorAt(Livery, Vector3)"/>, except that a
    /// countershaded paint job turns pale on surfaces that face the ground.
    /// </summary>
    public static Vector3 ColorAt(Livery l, Vector3 p, Vector3 normal) =>
        l.Pattern == LiveryPattern.Countershade && normal.Y < -CountershadeBelow ? l.Third : ColorAt(l, p);

    /// <summary>A countershaded paint job's pale underside begins where the surface faces at least this far down (−normal.Y).</summary>
    public const float CountershadeBelow = 0.3f;

    public static Vector3 ColorAt(Livery l, Vector3 p)
    {
        switch (l.Pattern)
        {
            case LiveryPattern.Countershade:
                // The top: soft patches of two colors (or one, where they're the same).
                return Fbm(p / l.Scale) >= 0.55f ? l.Second : l.Base;
            case LiveryPattern.Plain:
            case LiveryPattern.Factory:
                return l.Base;
            case LiveryPattern.Stripes:
            {
                float s = MathF.Sin((p.X * 0.8f + p.Z * 0.45f + p.Y * 0.3f) / l.Scale * MathF.PI + Fbm(p / l.Scale * 0.7f) * 4f);
                return s > 0.35f ? l.Second : s < -0.45f ? l.Third : l.Base;
            }

            default:
            {
                Vector3 q = p / l.Scale;
                if (l.Pattern == LiveryPattern.Digital)
                {
                    Vector3 cell = new(MathF.Floor(p.X / (l.Scale * 0.12f)), MathF.Floor(p.Y / (l.Scale * 0.12f)), MathF.Floor(p.Z / (l.Scale * 0.12f)));
                    q = cell * 0.12f;
                }

                float n1 = Fbm(q), n2 = Fbm(q + new Vector3(31.7f, 7.3f, 13.1f));
                Vector3 c = l.Base;
                if (n1 >= 0.55f) c = l.Second;
                if (n2 >= 0.62f) c = l.Third;
                return c;
            }
        }
    }

    /// <summary>
    /// The color at a point on a ship's hull: anti-fouling below the waterline, the boot-top stripe along it,
    /// grey on decks (faces pointing up), the paint job elsewhere. <paramref name="waterlineY"/> is in the same frame as <paramref name="p"/>.
    /// </summary>
    public static Vector3 ShipColorAt(Livery l, Vector3 p, Vector3 normal, float waterlineY, bool isPlating)
    {
        if (p.Y < waterlineY - BootTopHalfM) return AntiFouling;
        if (p.Y < waterlineY + BootTopHalfM) return BootTop;
        if (isPlating && normal.Y > 0.75f) return Deck;
        return ColorAt(l, p);
    }
}
