using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace DreamGame.Core;

/// <summary>What an island is made of, which decides its shape, its rock and what grows on it.</summary>
public enum IslandKind : byte
{
    /// <summary>Your starting island: rolling farmland and downs, long beaches, a harbour and an airstrip.</summary>
    Foothold,

    /// <summary>The rival's home island: downs round a central massif.</summary>
    Stronghold,

    /// <summary>A mountain range: snow-capped granite peaks, glacial valleys, fjords and a pass.</summary>
    Alpine,

    /// <summary>A sandstone plateau cut by winding canyons and slot canyons, with mesas and buttes along its edge.</summary>
    Canyonlands,

    /// <summary>A basalt volcano with a crater you can drive into through a breach in its rim.</summary>
    Volcanic,

    /// <summary>Moorland hills worn into branching river valleys.</summary>
    Highlands,

    /// <summary>A small cliff-girt rock with sea stacks round it.</summary>
    RockyIslet,

    /// <summary>A low sandy cay.</summary>
    SandyIslet,
}

/// <summary>One island of the archipelago: an ellipse (bent and roughened by noise) and what it's made of.</summary>
public sealed class Island
{
    public string Name;
    public IslandKind Kind;
    public Vector2 Center;

    /// <summary>Half-lengths along and across the island, in meters.</summary>
    public float A, B;

    /// <summary>Unit direction of the long axis (x east, y south).</summary>
    public Vector2 Axis;

    public int Seed;

    /// <summary>How much of the coast is beach rather than cliff (added to a noise field; positive means more beach).</summary>
    public float Beachy;

    /// <summary>A second, smaller ellipse joined on (a peninsula), in the island's own coordinates; zero size for none.</summary>
    public Vector2 LobeAt;

    public float LobeA, LobeB;

    /// <summary>How far from its center the island still changes the ground (its coast, shelf and sea floor).</summary>
    public float Reach => MathF.Max(A, B) * 1.5f + 3400f;

    public Vector2 ToLocal(Vector2 p)
    {
        Vector2 d = p - Center;
        return new Vector2(d.X * Axis.X + d.Y * Axis.Y, -d.X * Axis.Y + d.Y * Axis.X);
    }

    public Vector2 ToWorld(Vector2 l) => Center + new Vector2(l.X * Axis.X - l.Y * Axis.Y, l.X * Axis.Y + l.Y * Axis.X);
}

/// <summary>The kinds of carved course: valleys (cut before the rain wears the land) and canyons (cut sharp after).</summary>
public enum CourseKind : byte
{
    GlacialValley,
    Fjord,
    RiverValley,
    PassValley,
    Breach,
    Canyon,
    SideCanyon,
    Slot,
}

/// <summary>
/// A valley or canyon: a line from its head down to its mouth, with the height of its floor and the half-width of the
/// flat floor at each point. Valleys have walls that curve up (a U for glacial valleys and fjords, a V for rivers);
/// canyons have walls in steps of cliff and ledge, as deep as the ground they're cut into.
/// </summary>
public sealed class Course
{
    public CourseKind Kind;
    public string Name;
    public int Island;
    public Vector2[] P;
    public float[] Floor;
    public float[] Half;

    /// <summary>Valleys: the wall rises <see cref="WallK"/> × (distance past the floor)^<see cref="WallP"/> meters.</summary>
    public float WallK, WallP;

    /// <summary>Canyons: horizontal width of the wall per meter of depth, and how many cliff-and-ledge steps it has.</summary>
    public float WallRun;

    public int Terraces;

    /// <summary>Farthest from the line the course still cuts the ground.</summary>
    public float Reach;

    public bool IsCanyon => Kind is CourseKind.Canyon or CourseKind.SideCanyon or CourseKind.Slot;

    public float Length
    {
        get
        {
            float l = 0;
            for (int i = 1; i < P.Length; i++) l += Vector2.Distance(P[i - 1], P[i]);
            return l;
        }
    }
}

public enum FeatureKind : byte
{
    Island,
    Range,
    Peak,
    Pass,
    Valley,
    Fjord,
    Canyon,
    Slot,
    Mesa,
    Buttes,
    Volcano,
    Crater,
    Stacks,
    Moor,
    Harbour,
}

/// <summary>A named place, for the map.</summary>
public sealed class Feature
{
    public FeatureKind Kind;
    public string Name;
    public Vector2 At;
    public float Height;
    public int Island;

    public override string ToString() => $"{Kind} {Name} at ({At.X:0}, {At.Y:0}), {Height:0} m";
}

/// <summary>
/// Where you start: a stretch of the foothold island's coast levelled for a supply pad, an airstrip and a helipad, with
/// a dredged harbour off it deep enough for ships and an anchorage further out for big ones.
/// </summary>
public sealed class StartSite
{
    public Vector2 Center;

    /// <summary>Height of the levelled ground.</summary>
    public float Ground;

    /// <summary>Unit directions: along the coast, and out to sea.</summary>
    public Vector2 Along, Seaward;

    /// <summary>Distance from the center to the water's edge, out to sea.</summary>
    public float CoastOffset;

    public float HalfAlong, HalfInland;
    public Vector2 Spawn, Depot, StripCenter, Helipad, SeaSpawn, SeaDepot, Anchorage;
    public float StripHalfLength, StripHalfWidth;

    /// <summary>Site-local coordinates (along, seaward) of a world point.</summary>
    public Vector2 ToLocal(Vector2 p)
    {
        Vector2 d = p - Center;
        return new Vector2(Vector2.Dot(d, Along), Vector2.Dot(d, Seaward));
    }

    public Vector2 ToWorld(float along, float seaward) => Center + Along * along + Seaward * seaward;

    /// <summary>How far out to sea the water's edge is at a point along the site (the beach curves in at the middle).</summary>
    public float ShoreAt(float along) => CoastOffset - 110f * Noise.Smooth(HalfAlong, 0f, MathF.Abs(along));
}

/// <summary>What a point of ground is like, besides its height: for painting it and growing things on it.</summary>
public struct GroundInfo
{
    public IslandKind Kind;

    /// <summary>How much this stretch of coast is beach (0 cliff, 1 beach).</summary>
    public float Beach;

    /// <summary>On the floor of a canyon or valley (gravel and sand), 0 to 1.</summary>
    public float Floor;

    /// <summary>In a canyon's walls, where the rock's layers show, 0 to 1.</summary>
    public float Wall;

    /// <summary>Height above which snow lies here.</summary>
    public float SnowLine;

    /// <summary>On the levelled start site (keep clear of trees).</summary>
    public bool Site;
}

/// <summary>What the ground is covered with at a point, 0 to 1 each, for the ground shader and the trees.</summary>
public struct GroundCover
{
    public float Dirt, Rock, Sand, Snow, Gravel;

    /// <summary>Which rock: 0 grey granite, 0.5 layered red sandstone, 1 black basalt.</summary>
    public float RockKind;

    /// <summary>How dry the grass is: 0 lush green, 1 dry gold.</summary>
    public float Dry;

    /// <summary>How thickly trees grow here, 0 to 1 (before the woods' own clumping).</summary>
    public float Forest;

    /// <summary>Above the tree line, trees are pines; below, broadleaf mixed in. 0 lowland, 1 at the tree line.</summary>
    public float Upland;
}

/// <summary>
/// The campaign's world: a seeded archipelago about 20 × 20 km, each island with its own geology (see
/// <see cref="IslandKind"/>). The ground's height is worked out at any point from the islands' shapes, the valleys cut
/// into them, a grid of how the rain wore it (20 m apart, smoothly interpolated), the canyons cut sharp into it and fine
/// roughness, so it can be sampled at 1 m near you and coarsely far away and the two always agree. The same seed always
/// makes the same world. Generating one takes a few seconds (most of it the erosion).
/// <para>Coordinates: x east, z south (so north is −z), in meters, with the world's center at 0, 0; sea level is 0.</para>
/// </summary>
public sealed partial class WorldMap
{
    public const float Size = 20480f, Half = Size * 0.5f;

    /// <summary>The deep sea floor far from any island.</summary>
    public const float DeepSea = -190f;

    public readonly int Seed;
    public readonly List<Island> Islands = new();
    public readonly List<Course> Courses = new();
    public readonly List<Feature> Features = new();
    public StartSite Start { get; private set; }

    /// <summary>Spacing of the erosion grid (and of <see cref="Coarse"/>).</summary>
    public readonly float GridSpacing;

    public readonly int GridN;

    /// <summary>The whole world's ground at <see cref="GridSpacing"/>, for things far away and the map (no fine roughness).</summary>
    public HeightField Coarse { get; private set; }

    private float[] _delta;
    private CourseIndex _valleys, _canyons;

    private WorldMap(int seed, float gridSpacing)
    {
        Seed = seed;
        GridSpacing = gridSpacing;
        GridN = (int)(Size / gridSpacing) + 1;
    }

    // ------------------------------------------------------------------ asking about the ground

    /// <summary>The ground's height at a point, in meters above the sea.</summary>
    public float HeightAt(float x, float z) => Sample(x, z, out _);

    /// <summary>The ground's height at a point, and what it's like there.</summary>
    public float Sample(float x, float z, out GroundInfo info)
    {
        info = default;
        float h = Relief(x, z, true, ref info);
        h += DeltaAt(x, z);
        h = CarveCanyons(x, z, h, ref info);
        h = Site(x, z, h, ref info);
        h += Detail(x, z, h, in info);
        return h;
    }

    /// <summary>
    /// Fills <paramref name="dst"/> (<paramref name="w"/> × <paramref name="d"/>, row by row) with heights from
    /// (<paramref name="x0"/>, <paramref name="z0"/>) at <paramref name="spacing"/>, using every core.
    /// </summary>
    public void Fill(float[] dst, int w, int d, float x0, float z0, float spacing, GroundInfo[] info = null)
    {
        Parallel.For(0, d, z =>
        {
            for (int x = 0; x < w; x++)
            {
                float h = Sample(x0 + x * spacing, z0 + z * spacing, out GroundInfo gi);
                dst[z * w + x] = h;
                if (info != null) info[z * w + x] = gi;
            }
        });
    }

    /// <summary>
    /// What covers the ground at a point of a given height and steepness (<paramref name="slope"/> is 1 minus the
    /// upward part of the ground's normal: 0 flat, 0.29 at 45°).
    /// </summary>
    public static GroundCover Cover(in GroundInfo info, float h, float slope)
    {
        var c = new GroundCover();
        IslandKind k = info.Kind;
        float steep = Noise.Smooth(0.2f, 0.4f, slope);
        c.Rock = steep;
        c.RockKind = k switch { IslandKind.Canyonlands => 0.5f, IslandKind.Volcanic => 1f, _ => 0f };
        c.Dry = k switch
        {
            IslandKind.Canyonlands => 0.85f,
            IslandKind.Volcanic => 0.45f,
            IslandKind.Highlands => 0.5f,
            IslandKind.Stronghold => 0.3f,
            IslandKind.SandyIslet => 0.6f,
            _ => 0.15f,
        };

        // Bare rock high in the mountains, and on the canyon plateau's slickrock.
        if (k == IslandKind.Alpine) c.Rock = MathF.Max(c.Rock, Noise.Smooth(560f, 760f, h) * 0.8f);
        if (k == IslandKind.Canyonlands) c.Rock = MathF.Max(c.Rock, MathF.Max(info.Wall, Noise.Smooth(140f, 230f, h) * 0.45f));
        if (k == IslandKind.Volcanic) c.Dirt = Noise.Smooth(260f, 480f, h) * 0.9f;
        if (k == IslandKind.Highlands) c.Dirt = Noise.Smooth(250f, 420f, h) * 0.45f;
        if (k == IslandKind.RockyIslet) c.Rock = MathF.Max(c.Rock, 0.35f);

        c.Snow = Noise.Smooth(info.SnowLine - 40f, info.SnowLine + 70f, h) * (1f - Noise.Smooth(0.42f, 0.62f, slope));
        c.Rock *= 1f - c.Snow * 0.85f;

        // Beaches up to a couple of meters above the sea, where the coast is beach; the sea floor is sand too.
        float sand = h < -1.5f ? 1f : Math.Clamp((2.4f - h) / 1.4f, 0f, 1f) * Noise.Smooth(0.15f, 0.5f, info.Beach + 0.25f);
        c.Sand = MathF.Max(sand * (1f - steep), h < -1.5f ? 1f : 0f);
        if (k == IslandKind.SandyIslet) c.Sand = MathF.Max(c.Sand, Math.Clamp((4f - h) / 2.5f, 0, 1));

        // Gravel and sand on the floors of canyons and valleys.
        c.Gravel = info.Floor * (1f - steep);
        if (info.Site) c.Rock = c.Sand = c.Gravel = c.Snow = 0f;

        float treeLine = k == IslandKind.Alpine ? 640f : k == IslandKind.Volcanic ? 420f : 520f;
        c.Upland = Math.Clamp(h / treeLine, 0f, 1f);
        float density = k switch
        {
            IslandKind.Foothold => 0.55f,
            IslandKind.Stronghold => 0.45f,
            IslandKind.Alpine => 0.75f,
            IslandKind.Highlands => 0.3f,
            IslandKind.Volcanic => 0.3f,
            IslandKind.Canyonlands => 0.06f,
            IslandKind.RockyIslet => 0.1f,
            _ => 0.05f,
        };
        c.Forest = density * (1f - Noise.Smooth(treeLine - 80f, treeLine + 20f, h)) * (1f - Noise.Smooth(0.25f, 0.4f, slope)) *
                   (1f - c.Snow) * (1f - c.Sand) * (1f - c.Gravel * 0.8f) * (info.Site ? 0f : 1f) * Noise.Smooth(1.6f, 3.5f, h);
        return c;
    }

    // ------------------------------------------------------------------ the parts of the height

    /// <summary>Every island's shape, coast and sea floor (the highest wins), with the valleys cut into them.</summary>
    private float Relief(float x, float z, bool valleys, ref GroundInfo info)
    {
        float best = DeepFloor(x, z);
        float bestS = float.MinValue;
        var p = new Vector2(x, z);
        for (int i = 0; i < Islands.Count; i++)
        {
            Island isl = Islands[i];
            if (MathF.Abs(x - isl.Center.X) > isl.Reach || MathF.Abs(z - isl.Center.Y) > isl.Reach) continue;
            if (FarOffshore(isl, p)) continue;
            float h = IslandHeight(isl, p, out float s, out float beach);
            if (h > best) best = h;
            if (s > bestS)
            {
                bestS = s;
                info.Kind = isl.Kind;
                info.Beach = beach;
            }
        }

        info.SnowLine = 840f + 70f * Noise.Fbm(x / 900f, z / 900f, Seed + 2, 2);
        if (valleys && _valleys != null) best = _valleys.Carve(x, z, best, this, ref info);
        return best;
    }

    private float DeepFloor(float x, float z) => DeepSea + 6f * Noise.Fbm(x / 2900f, z / 2900f, Seed + 1, 2);

    /// <summary>
    /// A quick check that a point is so far out to sea from an island that the island can't change it (its sea floor
    /// has reached the deep sea's), allowing for how far the island's outline can be bent and warped.
    /// </summary>
    private static bool FarOffshore(Island isl, Vector2 p)
    {
        Vector2 l = isl.ToLocal(p);
        float e = Ellipse(l, isl.A * 1.2f, isl.B * 1.2f, out float g);
        if (isl.LobeA > 0f) e = MathF.Min(e, Ellipse(l - isl.LobeAt, isl.LobeA * 1.3f, isl.LobeB * 1.3f, out _));
        return (e - 1.45f) / g > 2850f;
    }

    private float IslandHeight(Island isl, Vector2 p, out float s, out float beach)
    {
        Vector2 l = isl.ToLocal(p);
        // The whole outline bent a little, so no island is a plain ellipse.
        Vector2 bent = l + new Vector2(0.17f * isl.A * Noise.Fbm(p.X / 2300f, p.Y / 2300f, isl.Seed + 30, 2),
            0.17f * isl.B * Noise.Fbm(p.X / 2300f, p.Y / 2300f, isl.Seed + 31, 2));
        float e = Ellipse(bent, isl.A, isl.B, out float g);
        float shape = 1f - e;
        if (isl.LobeA > 0f)
        {
            float el = Ellipse(bent - isl.LobeAt, isl.LobeA, isl.LobeB, out float gl);
            if (1f - el > shape)
            {
                shape = 1f - el;
                g = gl;
            }
        }

        float warp = 0.2f * Noise.Fbm(p.X / 1150f, p.Y / 1150f, isl.Seed + 1, 3) + 0.06f * Noise.Fbm(p.X / 330f, p.Y / 330f, isl.Seed + 2, 2) +
                     0.022f * Noise.Gradient(p.X / 95f, p.Y / 95f, isl.Seed + 3);
        if (isl.Kind is IslandKind.RockyIslet or IslandKind.SandyIslet) warp *= 1.4f;
        s = shape + warp;
        e = MathF.Sqrt((l.X / isl.A) * (l.X / isl.A) + (l.Y / isl.B) * (l.Y / isl.B));
        float dist = s / g;
        beach = Noise.Fbm(p.X / 1700f, p.Y / 1700f, isl.Seed + 5, 2) * 0.8f + isl.Beachy;
        float beachy = Noise.Smooth(-0.15f, 0.25f, beach);
        if (dist >= 0f)
        {
            float r = InlandHeight(isl, p, l, s, e);
            // How far in the land takes to reach its full height: a gentle rise behind a beach, a cliff elsewhere.
            float w = Noise.Lerp(MathF.Max(7f, r * 0.55f), MathF.Max(170f, r * 1.4f), beachy);
            return MathF.Min(dist * 0.02f, 1.6f) * beachy + r * Noise.Smooth(0f, w, dist);
        }

        float off = -dist;
        float shelf = MathF.Min(off * Noise.Lerp(0.09f, 0.025f, beachy), 24f + 8f * Noise.Fbm(p.X / 600f, p.Y / 600f, isl.Seed + 6, 2));
        float nearShore = -shelf - 6f * (1f - beachy) * Noise.Smooth(0f, 10f, off);
        // Out past the shelf the floor falls away to the deep sea's (exactly, so islands far off can be skipped).
        float h = Noise.Lerp(nearShore, DeepFloor(p.X, p.Y), Noise.Smooth(450f, 2800f, off));
        if (isl.Kind == IslandKind.RockyIslet && off < 260f)
        {
            // Sea stacks: pillars of rock left standing off the cliffs.
            float c = Noise.Cells(p.X / 70f, p.Y / 70f, isl.Seed + 9, out uint id);
            if (Noise.Unit(id) < 0.32f) h = MathF.Max(h, (16f + 26f * Noise.Unit(id * 7919u)) * Noise.Smooth(0.2f, 0.12f, c) - 2f);
        }

        return h;
    }

    /// <summary>How far out of an ellipse a point is (1 on its edge), and how fast that changes per meter.</summary>
    private static float Ellipse(Vector2 l, float a, float b, out float g)
    {
        float eu = l.X / a, ev = l.Y / b;
        float e = MathF.Sqrt(eu * eu + ev * ev);
        g = e > 1e-3f ? MathF.Sqrt(eu * eu / (a * a) + ev * ev / (b * b)) / e : 1f / b;
        return e;
    }

    /// <summary>The island's land, before its coast is shaped: <paramref name="s"/> is 0 at the coast and about 1 in the middle.</summary>
    private float InlandHeight(Island isl, Vector2 p, Vector2 l, float s, float e)
    {
        float q = Noise.Smooth(0f, 0.9f, s);
        int sd = isl.Seed;
        switch (isl.Kind)
        {
            case IslandKind.Foothold:
            case IslandKind.Stronghold:
            {
                float n = Noise.Fbm(p.X / 2300f, p.Y / 2300f, sd + 10, 4) * 0.5f + 0.5f;
                float downs = Noise.Ridged(p.X / 1700f, p.Y / 1700f, sd + 13, 4);
                float hills = q * (55f + 170f * n * n) * (0.45f + 0.75f * downs) + 16f * Noise.Fbm(p.X / 480f, p.Y / 480f, sd + 11, 3) * q;
                if (isl.Kind == IslandKind.Stronghold)
                    hills += 190f * Noise.Smooth(0.45f, 0.95f, s) * (0.6f + 0.4f * n) * (0.7f + 0.5f * Noise.Ridged(p.X / 1300f, p.Y / 1300f, sd + 12, 4));
                return 3f + MathF.Max(0f, hills);
            }
            case IslandKind.Alpine:
            {
                float dv = MathF.Abs(l.Y - RidgeOffset(isl, l.X)) / (isl.B * 0.95f);
                float crest = MathF.Pow(MathF.Max(0f, 1f - dv), 1.35f);
                float along = MathF.Max(0f, 1f - MathF.Pow(MathF.Abs(l.X) / (isl.A * 0.98f), 2.4f));
                float peaks = 0.7f + 0.3f * Noise.Fbm(l.X / 1150f, 3.1f, sd + 13, 3);
                float rn = Noise.Ridged(p.X / 1900f, p.Y / 1900f, sd + 14, 6);
                float massif = 1450f * crest * along * peaks;
                return 18f + massif * (0.5f + 0.7f * rn) + 150f * rn * q;
            }
            case IslandKind.Highlands:
            {
                float n = Noise.Fbm(p.X / 2600f, p.Y / 2600f, sd + 15, 4) * 0.5f + 0.5f;
                float rn = Noise.Ridged(p.X / 1500f, p.Y / 1500f, sd + 16, 5);
                return 14f + MathF.Pow(q, 0.8f) * (110f + 340f * n) * (0.62f + 0.5f * rn);
            }
            case IslandKind.Volcanic:
                return Volcano(isl, l, e, q);
            case IslandKind.Canyonlands:
                return Plateau(isl, p, s, q);
            case IslandKind.RockyIslet:
                return 10f + 75f * MathF.Pow(q, 0.7f) * (0.6f + 0.4f * Noise.Fbm(p.X / 300f, p.Y / 300f, sd + 17, 3));
            default:
                return 1.2f + 5f * q * (0.6f + 0.4f * Noise.Fbm(p.X / 200f, p.Y / 200f, sd + 18, 2));
        }
    }

    /// <summary>How far the alpine ridge's crest wanders off the island's long axis, at a point along it.</summary>
    private static float RidgeOffset(Island isl, float u) => 0.16f * isl.B * Noise.Fbm(u / 2800f, 0.37f, isl.Seed + 12, 3);

    /// <summary>The volcano's crater rim, as a fraction of the island's size, and how deep the crater is.</summary>
    private const float CraterRim = 0.12f, CraterDepth = 250f, ConeHeight = 1050f;

    private float Volcano(Island isl, Vector2 l, float e, float q)
    {
        float cone = ConeHeight * MathF.Pow(MathF.Max(0f, 1f - e * 0.97f), 1.65f);
        float rimH = ConeHeight * MathF.Pow(1f - CraterRim * 0.97f, 1.65f) + 14f;
        float h = cone + 14f * Noise.Smooth(CraterRim * 1.3f, CraterRim, e);
        if (e < CraterRim) h = rimH - CraterDepth * Noise.Smooth(CraterRim, CraterRim * 0.55f, e);
        // A smaller cone on the flank, with its own little crater.
        Vector2 o = l - new Vector2(isl.A * 0.48f, isl.B * 0.22f);
        float r2 = o.Length() / (isl.A * 0.24f);
        if (r2 < 1f)
        {
            float side = 240f * MathF.Pow(1f - r2, 1.5f) + 30f * q;
            if (r2 < 0.16f) side -= 35f * Noise.Smooth(0.16f, 0.08f, r2);
            h = MathF.Max(h, side);
        }

        // Old lava flows: lobes and ridges down the flanks.
        Vector2 w = isl.ToWorld(l);
        h += 28f * q * Noise.Fbm(w.X / 420f, w.Y / 420f, isl.Seed + 19, 4);
        return MathF.Max(h, 4f);
    }

    /// <summary>The canyon island: a sandstone plateau stepping down in ledges to a lower bench, with mesas and buttes left out on it.</summary>
    private float Plateau(Island isl, Vector2 p, float s, float q)
    {
        int sd = isl.Seed;
        float top = 270f + 45f * Noise.Fbm(p.X / 3600f, p.Y / 3600f, sd + 20, 2) + 30f * q;
        float bench = 28f + 12f * Noise.Fbm(p.X / 1100f, p.Y / 1100f, sd + 21, 3);
        float m = s - 0.24f + 0.14f * Noise.Fbm(p.X / 700f, p.Y / 700f, sd + 22, 4) + 0.04f * Noise.Fbm(p.X / 170f, p.Y / 170f, sd + 23, 2);
        float t = Noise.Smooth(0f, 0.2f, m);
        float smooth = bench + (top - bench) * t;
        // The escarpment stepped by the rock's layers; the plateau's top and the bench stay as they are.
        float h = Noise.Lerp(MathF.Max(bench, Strata(smooth)), smooth, MathF.Max(Noise.Smooth(0.85f, 1f, t), Noise.Smooth(0.12f, 0f, t)));
        // Buttes standing alone on the bench: lumps of plateau left behind, ragged in outline.
        if (m < 0.01f)
        {
            float wx = p.X + 170f * Noise.Fbm(p.X / 320f, p.Y / 320f, sd + 25, 2) + 55f * Noise.Gradient(p.X / 85f, p.Y / 85f, sd + 27);
            float wz = p.Y + 170f * Noise.Fbm(p.X / 320f, p.Y / 320f, sd + 26, 2) + 55f * Noise.Gradient(p.X / 85f, p.Y / 85f, sd + 28);
            float c = Noise.Cells(wx / 620f, wz / 460f, sd + 24, out uint id);
            if (Noise.Unit(id) < 0.3f)
            {
                float bt = Noise.Smooth(0.21f, 0.11f, c);
                float bh = bench + (top - bench) * (0.5f + 0.35f * Noise.Unit(id * 31u)) * bt;
                h = MathF.Max(h, Noise.Lerp(MathF.Max(bench, Strata(bh)), bh, Noise.Smooth(0.85f, 1f, bt)));
            }
        }

        return h;
    }

    /// <summary>
    /// Steps of cliff and ledge: <paramref name="x"/> from 0 to 1 rises through <paramref name="steps"/> bands, each a
    /// steep cliff for its first third and a gentler ledge after.
    /// </summary>
    public static float Terrace(float x, int steps)
    {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        float k = x * steps;
        int band = Math.Min((int)k, steps - 1);
        float f = k - band;
        float b = 0.62f * Noise.Smooth(0f, 0.4f, f) + 0.38f * MathF.Max(0f, f - 0.3f) / 0.7f;
        return (band + b) / steps;
    }

    /// <summary>Thickness of the canyon country's rock layers, in meters.</summary>
    public const float StrataM = 32f;

    /// <summary>
    /// The sandstone's layers: each <see cref="StrataM"/> thick, a ledge (rising only a little) then a cliff, the cliff's
    /// share of each layer different (hard and soft rock). Applied to a smooth slope it steps it at the same heights
    /// everywhere, so the layers line up across canyon walls, mesas and buttes like real ones.
    /// </summary>
    public static float Strata(float h)
    {
        float k = MathF.Floor(h / StrataM);
        float f = h / StrataM - k;
        float cliff = 0.35f + 0.3f * Noise.Unit(Noise.Hash((int)k, 7, 991));
        float w = f < 1f - cliff ? 0.15f * f / (1f - cliff) : 0.15f + 0.85f * Noise.Smooth(1f - cliff, 1f, f);
        return (k + w) * StrataM;
    }

    /// <summary>The rain's wear, from the erosion grid (smoothly interpolated between its samples).</summary>
    private float DeltaAt(float x, float z)
    {
        if (_delta == null) return 0f;
        float gx = (x + Half) / GridSpacing, gz = (z + Half) / GridSpacing;
        if (gx < 1f || gz < 1f || gx > GridN - 3 || gz > GridN - 3) return 0f;
        int ix = (int)gx, iz = (int)gz;
        float fx = gx - ix, fz = gz - iz;
        float r0 = Cubic(_delta, ix, iz - 1, fx), r1 = Cubic(_delta, ix, iz, fx);
        float r2 = Cubic(_delta, ix, iz + 1, fx), r3 = Cubic(_delta, ix, iz + 2, fx);
        return CatmullRom(r0, r1, r2, r3, fz);
    }

    private float Cubic(float[] g, int ix, int iz, float fx)
    {
        int i = iz * GridN + ix;
        return CatmullRom(g[i - 1], g[i], g[i + 1], g[i + 2], fx);
    }

    private static float CatmullRom(float a, float b, float c, float d, float t) =>
        b + 0.5f * t * (c - a + t * (2f * a - 5f * b + 4f * c - d + t * (3f * (b - c) + d - a)));

    private float CarveCanyons(float x, float z, float h, ref GroundInfo info) =>
        _canyons == null ? h : _canyons.Carve(x, z, h, this, ref info);

    /// <summary>The start site: levelled ground behind a curved beach, in a round dredged harbour.</summary>
    private float Site(float x, float z, float h, ref GroundInfo info)
    {
        StartSite st = Start;
        if (st == null) return h;
        Vector2 l = st.ToLocal(new Vector2(x, z));
        float c = st.CoastOffset;
        if (MathF.Abs(l.X) > st.HalfAlong + 1500f || l.Y < -st.HalfInland - 400f || l.Y > c + 2000f) return h;
        float along = Noise.Smooth(st.HalfAlong + 300f, st.HalfAlong, MathF.Abs(l.X));
        // The beach curves in, a crescent round the harbour.
        float shore = st.ShoreAt(l.X);
        if (l.Y <= shore)
        {
            // Flat to 60 m short of the water, then the beach down to its edge.
            float k = along * Noise.Smooth(-st.HalfInland - 300f, -st.HalfInland, l.Y);
            float want = l.Y < shore - 60f ? st.Ground : st.Ground * (shore - l.Y) / 60f;
            if (k > 0.5f && l.Y < shore - 60f) info.Site = true;
            return Noise.Lerp(h, want, k);
        }

        // The harbour: a round basin dropping away to 24 m, deeper out at the anchorage.
        float basin = Noise.Smooth(1150f, 820f, Vector2.Distance(l, new Vector2(0f, c + 700f)));
        float ks = MathF.Max(basin, along * Noise.Smooth(shore + 200f, shore + 40f, l.Y));
        float sea = -MathF.Min((l.Y - shore) * 0.14f, 24f + 18f * Noise.Smooth(c + 500f, c + 1300f, l.Y));
        return MathF.Min(h, Noise.Lerp(h, sea, ks));
    }

    /// <summary>Fine roughness: rough on mountains and rock, smooth on beaches, valley floors and the start site.</summary>
    private float Detail(float x, float z, float h, in GroundInfo info)
    {
        float amp = info.Kind switch
        {
            IslandKind.Alpine => 0.45f + 0.9f * Noise.Smooth(150f, 600f, h),
            IslandKind.Highlands => 0.5f,
            IslandKind.Volcanic => 0.7f,
            IslandKind.Canyonlands => 0.35f,
            IslandKind.RockyIslet => 0.6f,
            IslandKind.SandyIslet => 0.12f,
            _ => 0.28f,
        };
        amp *= Noise.Smooth(0.4f, 3.5f, MathF.Abs(h)) * (1f - 0.45f * info.Floor);
        if (info.Site) amp = 0f;
        if (h < -3f) amp = 0.35f;
        if (amp <= 0f) return 0f;
        return amp * (1.7f * Noise.Fbm(x / 46f, z / 46f, Seed + 101, 3) + 0.3f * Noise.Fbm(x / 9f, z / 9f, Seed + 102, 2));
    }

    // ------------------------------------------------------------------ courses: valleys and canyons

    /// <summary>The courses' segments filed in a grid of cells, so a point only looks at the ones near it.</summary>
    private sealed class CourseIndex
    {
        private const float Cell = 128f;
        private readonly int _n;
        private readonly int[][] _cells;
        private readonly List<Course> _courses;
        private readonly int[] _ids;

        public CourseIndex(List<Course> all, Func<Course, bool> which)
        {
            _n = (int)(Size / Cell) + 1;
            var lists = new List<int>[_n * _n];
            _courses = all;
            var ids = new List<int>();
            for (int ci = 0; ci < all.Count; ci++)
            {
                Course c = all[ci];
                if (!which(c)) continue;
                ids.Add(ci);
                for (int si = 0; si + 1 < c.P.Length; si++)
                {
                    Vector2 a = c.P[si], b = c.P[si + 1];
                    float r = c.Reach;
                    int x0 = CellOf(MathF.Min(a.X, b.X) - r), x1 = CellOf(MathF.Max(a.X, b.X) + r);
                    int z0 = CellOf(MathF.Min(a.Y, b.Y) - r), z1 = CellOf(MathF.Max(a.Y, b.Y) + r);
                    for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                        (lists[cz * _n + cx] ??= new List<int>()).Add(ci << 12 | si);
                }
            }

            _ids = ids.ToArray();
            _cells = new int[lists.Length][];
            // In each cell, of each course only the segments that could be nearest to some point in the cell: none
            // farther from the cell's middle than the nearest one plus the cell's diagonal.
            float diag = Cell * 1.4143f;
            var keep = new List<int>();
            for (int i = 0; i < lists.Length; i++)
            {
                List<int> l = lists[i];
                if (l == null) continue;
                var mid = new Vector2(-Half + (i % _n + 0.5f) * Cell, -Half + (i / _n + 0.5f) * Cell);
                keep.Clear();
                int j = 0;
                while (j < l.Count)
                {
                    int ci = l[j] >> 12, start = j;
                    float nearest = float.MaxValue;
                    for (; j < l.Count && l[j] >> 12 == ci; j++) nearest = MathF.Min(nearest, SegDist(all[ci], l[j] & 0xFFF, mid));
                    if (nearest - diag * 0.5f > all[ci].Reach) continue;
                    for (int k = start; k < j; k++)
                        if (SegDist(all[ci], l[k] & 0xFFF, mid) <= nearest + diag) keep.Add(l[k]);
                }

                _cells[i] = keep.Count > 0 ? keep.ToArray() : null;
            }
        }

        private int CellOf(float v) => Math.Clamp((int)((v + Half) / Cell), 0, _n - 1);

        private static float SegDist(Course c, int si, Vector2 p)
        {
            Vector2 a = c.P[si], ab = c.P[si + 1] - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Lowers the ground to every course near the point (each course by its nearest segment).</summary>
        public float Carve(float x, float z, float h, WorldMap map, ref GroundInfo info)
        {
            int[] segs = _cells[CellOf(z) * _n + CellOf(x)];
            if (segs == null) return h;
            var p = new Vector2(x, z);
            // Every course is cut into the same ground, and the lowest wins: a canyon joining another is cut into the
            // plateau, not into the other canyon's walls.
            float h0 = h;
            // Nearest segment of each course (segments of one course are filed together, in order).
            int i = 0;
            while (i < segs.Length)
            {
                int ci = segs[i] >> 12;
                Course c = _courses[ci];
                float bestD = float.MaxValue, bestT = 0f;
                int bestS = 0;
                for (; i < segs.Length && segs[i] >> 12 == ci; i++)
                {
                    int si = segs[i] & 0xFFF;
                    Vector2 a = c.P[si], ab = c.P[si + 1] - a;
                    float t = Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
                    float d = Vector2.DistanceSquared(p, a + ab * t);
                    if (d < bestD)
                    {
                        bestD = d;
                        bestT = t;
                        bestS = si;
                    }
                }

                float dist = MathF.Sqrt(bestD);
                if (dist > c.Reach) continue;
                float floor = Noise.Lerp(c.Floor[bestS], c.Floor[bestS + 1], bestT);
                float half = Noise.Lerp(c.Half[bestS], c.Half[bestS + 1], bestT);
                h = MathF.Min(h, CarveOne(c, x, z, dist, floor, half, h0, ref info));
            }

            return h;
        }

        private static float CarveOne(Course c, float x, float z, float d, float floor, float half, float h, ref GroundInfo info)
        {
            if (!c.IsCanyon)
            {
                float target = floor + 0.4f * MathF.Min(1f, (d / half) * (d / half));
                if (d > half)
                {
                    // Walls uneven along their length, not smooth bowls.
                    float rough = 1f + 0.3f * Noise.Fbm(x / 300f, z / 300f, 4441, 3);
                    target += c.WallK * MathF.Pow(d - half, c.WallP) * rough;
                }
                if (target < h)
                {
                    h = target;
                    info.Floor = MathF.Max(info.Floor, Noise.Smooth(half * 1.6f, half * 0.6f, d) * (c.Kind == CourseKind.Fjord ? 0.4f : 0.7f));
                }

                return h;
            }

            float depth = h - floor;
            if (depth <= 0.5f) return h;
            if (d <= half)
            {
                info.Floor = 1f;
                return floor + 0.25f * (d / half) * (d / half);
            }

            // The wall wobbles in and out, with alcoves and side ravines cut back into it.
            float wobble = 1f + 0.25f * Noise.Fbm(x / 320f, z / 320f, 5101, 2);
            float alcove = Noise.Smooth(0.6f, 0.92f, Noise.Ridged(x / 210f, z / 210f, 5102, 1));
            float run = MathF.Max(depth * c.WallRun * wobble, 1f) * (1f + 0.35f * alcove * (c.Kind == CourseKind.Slot ? 0f : 1f));
            float across = (d - half) / run;
            if (across >= 1f) return h;
            info.Wall = MathF.Max(info.Wall, 1f);
            info.Floor = MathF.Max(info.Floor, Noise.Smooth(0.12f, 0f, across));
            if (c.Kind == CourseKind.Slot) return MathF.Min(h, floor + 0.25f + depth * MathF.Pow(across, 0.55f));
            // A smooth slope from the floor to the rim, stepped by the rock's layers (cliffs over ledges, at the same
            // heights all over the island), meeting the plateau at the rim.
            float slope = floor + 0.25f + depth * MathF.Pow(across, 0.9f);
            float stepped = MathF.Max(floor + 0.25f, Strata(slope));
            return MathF.Min(h, Noise.Lerp(stepped, slope, Noise.Smooth(0.85f, 1f, across)));
        }
    }
}
