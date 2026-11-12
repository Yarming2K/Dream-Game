using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace DreamGame.Core;

public sealed partial class WorldMap
{
    public sealed class Options
    {
        /// <summary>Spacing of the erosion grid, in meters (coarser is quicker; tests use it).</summary>
        public float GridSpacing = 20f;

        /// <summary>Rain drops let fall per land sample of the erosion grid.</summary>
        public float DropsPerLandSample = 2.5f;
    }

    /// <summary>Makes the world for a seed. Takes a few seconds; the same seed always gives the same world.</summary>
    public static WorldMap Generate(int seed, Options o = null)
    {
        o ??= new Options();
        var w = new WorldMap(seed, o.GridSpacing);
        var names = new PlaceNames(seed);
        w.Layout(names);
        w.PlaceStart();
        w.MakeValleys(names);
        w._valleys = new CourseIndex(w.Courses, c => !c.IsCanyon);
        w.Erode(o);
        w.MakeCanyons(names);
        w._canyons = new CourseIndex(w.Courses, c => c.IsCanyon);
        w.Coarse = w.BuildCoarse();
        w.NameFeatures(names);
        return w;
    }

    // ------------------------------------------------------------------ the islands

    private void Layout(PlaceNames names)
    {
        var rng = new Rng((ulong)(uint)Seed * 7919ul + 11ul);
        // Where each island sits, roughly: your foothold in the south-west, the rival's in the north-east, the mountains
        // between, the canyons south-east, the volcano east, the highlands north-west, and islets in the gaps.
        (IslandKind kind, float x, float z, float a, float b, float ax, float az, float beachy)[] slots =
        {
            (IslandKind.Foothold, -5800f, 5300f, 2700f, 2000f, 1f, -0.5f, 0.4f),
            (IslandKind.Alpine, -1300f, -2500f, 4700f, 2100f, 1f, 0.35f, -0.35f),
            (IslandKind.Highlands, -7100f, -6900f, 2300f, 1700f, 0.6f, 1f, -0.05f),
            (IslandKind.Stronghold, 6500f, -6800f, 2500f, 2000f, 1f, 0.3f, 0.2f),
            (IslandKind.Volcanic, 6300f, -500f, 1750f, 1650f, 1f, 0.1f, -0.05f),
            (IslandKind.Canyonlands, 4200f, 5600f, 3100f, 2400f, 1f, 0.6f, -0.45f),
            (IslandKind.RockyIslet, -1500f, 3000f, 650f, 420f, 1f, 0.4f, -0.5f),
            (IslandKind.SandyIslet, 900f, 8700f, 520f, 380f, 1f, -0.2f, 1f),
            (IslandKind.RockyIslet, -9100f, -800f, 480f, 350f, 0.3f, 1f, -0.4f),
            (IslandKind.RockyIslet, 9000f, 2700f, 560f, 400f, 0.8f, 1f, -0.4f),
        };
        foreach (var s in slots)
        {
            bool small = s.kind is IslandKind.RockyIslet or IslandKind.SandyIslet;
            float jitter = small ? 300f : 380f;
            var axis = Vector2.Normalize(new Vector2(s.ax + rng.Range(-0.25f, 0.25f), s.az + rng.Range(-0.25f, 0.25f)));
            Islands.Add(new Island
            {
                Kind = s.kind,
                Name = names.Next(),
                Center = new Vector2(s.x + rng.Range(-jitter, jitter), s.z + rng.Range(-jitter, jitter)),
                A = s.a * rng.Range(0.92f, 1.08f),
                B = s.b * rng.Range(0.92f, 1.08f),
                Axis = axis,
                Seed = Seed * 31 + Islands.Count * 977 + 5,
                Beachy = s.beachy,
            });
            Island isl = Islands[^1];
            if (!small && s.kind != IslandKind.Volcanic)
            {
                // A peninsula off one side.
                float su = rng.Float() < 0.5f ? -1f : 1f, sv = rng.Float() < 0.5f ? -1f : 1f;
                isl.LobeAt = new Vector2(su * isl.A * rng.Range(0.45f, 0.7f), sv * isl.B * rng.Range(0.45f, 0.75f));
                isl.LobeA = isl.A * rng.Range(0.3f, 0.45f);
                isl.LobeB = isl.B * rng.Range(0.35f, 0.5f);
            }
        }
    }

    /// <summary>The island's own height at a point, with no valleys or wear.</summary>
    private float Bare(Island isl, Vector2 p) => IslandHeight(isl, p, out _, out _);

    /// <summary>Walks from <paramref name="from"/> along <paramref name="dir"/> until the island's coast.</summary>
    private Vector2 CoastFrom(Island isl, Vector2 from, Vector2 dir)
    {
        Vector2 p = from;
        for (int i = 0; i < 2000; i++)
        {
            if (Bare(isl, p) < 0f) return p;
            p += dir * 10f;
        }

        return p;
    }

    private void PlaceStart()
    {
        Island isl = Islands.First(i => i.Kind == IslandKind.Foothold);
        // Facing the rest of the archipelago.
        Vector2 seaward = Vector2.Normalize(-isl.Center);
        Vector2 coast = CoastFrom(isl, isl.Center, seaward);
        const float c = 420f;
        var st = new StartSite
        {
            Seaward = seaward,
            Along = new Vector2(-seaward.Y, seaward.X),
            Center = coast - seaward * c,
            CoastOffset = c,
            Ground = 3f,
            HalfAlong = 600f,
            HalfInland = 430f,
            StripHalfLength = 300f,
            StripHalfWidth = 18f,
        };
        st.Spawn = st.ToWorld(0f, 140f);
        st.Depot = st.ToWorld(-70f, 175f);
        st.StripCenter = st.ToWorld(0f, -175f);
        st.Helipad = st.ToWorld(240f, 160f);
        st.SeaSpawn = st.ToWorld(0f, c + 230f);
        st.SeaDepot = st.ToWorld(60f, c + 270f);
        st.Anchorage = st.ToWorld(0f, c + 1050f);
        Start = st;
    }

    // ------------------------------------------------------------------ valleys, cut before the rain

    private void MakeValleys(PlaceNames names)
    {
        var rng = new Rng((ulong)(uint)Seed * 104729ul + 3ul);
        for (int ii = 0; ii < Islands.Count; ii++)
        {
            Island isl = Islands[ii];
            switch (isl.Kind)
            {
                case IslandKind.Alpine:
                    AlpineValleys(ii, isl, ref rng);
                    break;
                case IslandKind.Highlands:
                    Radial(ii, isl, 4, 0.3f, 0.5f, 22f, 34f, 0.42f, 1.15f, ref rng, null);
                    break;
                case IslandKind.Stronghold:
                    Radial(ii, isl, 3, 0.3f, 0.4f, 26f, 40f, 0.25f, 1.2f, ref rng, null);
                    break;
                case IslandKind.Foothold:
                    // Away from the start site.
                    Radial(ii, isl, 3, 0.22f, 0.4f, 26f, 44f, 0.2f, 1.2f, ref rng, Start.Seaward);
                    break;
                case IslandKind.Volcanic:
                    Breach(ii, isl, ref rng);
                    break;
            }
        }

        foreach (Course c in Courses) c.Name ??= NameFor(c, names);
    }

    private static Vector2 Rotate(Vector2 v, float cos, float sin) => new(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);

    private void AlpineValleys(int ii, Island isl, ref Rng rng)
    {
        float[] fracs = { -0.62f, -0.27f, 0.1f, 0.47f };
        int passAt = 1 + rng.Int(2);
        for (int side = -1; side <= 1; side += 2)
        for (int k = 0; k < fracs.Length; k++)
        {
            bool pass = k == passAt;
            float u = (fracs[k] + rng.Range(-0.05f, 0.05f)) * isl.A;
            if (pass) u = fracs[k] * isl.A;
            float ridge = RidgeOffset(isl, u);
            Vector2 head = isl.ToWorld(new Vector2(u, ridge + side * (pass ? 0f : 240f)));
            float crest = Bare(isl, isl.ToWorld(new Vector2(u, ridge)));
            Vector2 dirLocal = Vector2.Normalize(new Vector2(pass ? 0.75f : fracs[k] * 0.45f, side));
            Vector2 dir = isl.ToWorld(dirLocal) - isl.Center;
            bool fjord = !pass && rng.Float() < 0.6f;
            CourseKind kind = pass ? CourseKind.PassValley : fjord ? CourseKind.Fjord : CourseKind.GlacialValley;
            float headFloor = pass ? MathF.Max(260f, crest * 0.32f) : crest * 0.48f;
            Course c = pass
                ? Valley(kind, ii, isl, head, dir, headFloor, 50f, 70f, 0.09f, 1.4f, 0.65f, 700f, rng.Int(100000))
                : fjord
                    ? Valley(kind, ii, isl, head, dir, headFloor, 140f, 200f, 0.05f, 1.55f, 0.55f, 1100f, rng.Int(100000))
                    : Valley(kind, ii, isl, head, dir, headFloor, 70f, 110f, 0.06f, 1.5f, 0.6f, 900f, rng.Int(100000));
            Courses.Add(c);
        }
    }

    /// <summary>River valleys running out from near an island's middle, evenly round it (not toward <paramref name="avoid"/>).</summary>
    private void Radial(int ii, Island isl, int count, float headFrac, float headDepth, float half0, float half1, float k, float p, ref Rng rng, Vector2? avoid)
    {
        // Turning a whole circle in equal steps, without trigonometry: a fixed rotation applied again and again.
        Vector2 dir = Vector2.Normalize(new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f)) + new Vector2(0.01f, 0f));
        float step = MathF.Tau / count;
        float cos = MathF.Cos(step), sin = MathF.Sin(step);
        for (int i = 0; i < count; i++, dir = Rotate(dir, cos, sin))
        {
            if (avoid.HasValue && Vector2.Dot(dir, avoid.Value) > -0.2f) continue;
            // Heads scattered, not all from the middle, and each valley bending its own way.
            Vector2 side = new(-dir.Y, dir.X);
            Vector2 head = isl.Center + dir * isl.MathMinAxis() * headFrac * rng.Range(0.6f, 1.5f) + side * isl.MathMinAxis() * rng.Range(-0.3f, 0.3f);
            Vector2 run = Vector2.Normalize(dir + side * rng.Range(-0.5f, 0.5f));
            float floor = Bare(isl, head) * headDepth;
            Courses.Add(Valley(CourseKind.RiverValley, ii, isl, head, run, floor, half0, half1, k, p, 0.9f, 900f, rng.Int(100000)));
        }
    }

    /// <summary>A gorge from the volcano's crater out through its rim and round its flank to the sea: the way in.</summary>
    private void Breach(int ii, Island isl, ref Rng rng)
    {
        Vector2 d = Vector2.Normalize(new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f)) + new Vector2(0.01f, 0f));
        float r = (isl.A + isl.B) * 0.5f;
        Vector2[] way =
        {
            isl.Center,
            isl.Center + d * r * CraterRim * 1.4f,
            isl.Center + Rotate(d, 0.5736f, 0.8192f) * r * 0.62f,
            isl.Center + Rotate(d, -0.2588f, 0.9659f) * r * 1.25f,
        };
        var pts = new List<Vector2> { way[0] };
        for (int i = 1; i < way.Length; i++)
        {
            Vector2 a = way[i - 1], b = way[i];
            int n = Math.Max(1, (int)(Vector2.Distance(a, b) / 30f));
            for (int j = 1; j <= n; j++) pts.Add(Vector2.Lerp(a, b, j / (float)n));
        }

        Smooth(pts, 6);
        float top = Bare(isl, isl.Center) + 2f;
        float coastT = CoastFraction(pts, p => Bare(isl, p));
        var c = new Course
        {
            Kind = CourseKind.Breach, Island = ii, P = pts.ToArray(), WallK = 0.6f, WallP = 1.1f, Reach = 420f,
            Floor = new float[pts.Count], Half = new float[pts.Count],
        };
        float[] t = ArcFractions(pts);
        for (int i = 0; i < pts.Count; i++)
        {
            c.Floor[i] = t[i] < coastT ? Noise.Lerp(top, -1f, t[i] / coastT) : -1f - 20f * (t[i] - coastT);
            c.Half[i] = Noise.Lerp(16f, 26f, t[i]);
        }

        Courses.Add(c);
    }

    /// <summary>
    /// A valley from <paramref name="head"/> heading roughly along <paramref name="dir"/> down to the coast and on into
    /// the sea, wandering as it goes.
    /// </summary>
    private Course Valley(CourseKind kind, int ii, Island isl, Vector2 head, Vector2 dir, float headFloor, float half0, float half1,
        float k, float p, float wander, float wavelength, int seed)
    {
        dir = Vector2.Normalize(dir);
        var pts = new List<Vector2> { head };
        Vector2 at = head;
        float walked = 0f;
        int coastAt = -1;
        const float step = 60f;
        for (int i = 0; i < 300; i++)
        {
            Vector2 side = new(-dir.Y, dir.X);
            Vector2 d = Vector2.Normalize(dir + side * wander * Noise.Fbm(walked / wavelength, 0.5f, seed, 2));
            at += d * step;
            walked += step;
            pts.Add(at);
            if (coastAt < 0 && Bare(isl, at) < 0f) coastAt = pts.Count - 1;
            if (coastAt >= 0 && (pts.Count - 1 - coastAt) * step > 200f) break;
        }

        Smooth(pts, 2);
        float[] t = ArcFractions(pts);
        float coastT = coastAt > 0 ? t[coastAt] : 0.9f;
        var c = new Course
        {
            Kind = kind, Island = ii, P = pts.ToArray(), WallK = k, WallP = p,
            Floor = new float[pts.Count], Half = new float[pts.Count],
        };
        // Out to where the walls are a kilometer high.
        c.Reach = MathF.Min(half1 + MathF.Pow(1000f / k, 1f / p), 800f);
        for (int i = 0; i < pts.Count; i++)
        {
            float ti = t[i];
            float f;
            if (kind == CourseKind.Fjord)
            {
                // The sea reaches far up a fjord: its floor is below sea level for the last half of the way to the coast.
                float tw = 0.32f * coastT;
                f = ti < tw ? headFloor * MathF.Pow(1f - ti / tw, 1.25f) : -70f * Noise.Smooth(tw, coastT, ti);
            }
            else if (ti < coastT)
            {
                float pw = kind == CourseKind.PassValley ? 1f : kind == CourseKind.RiverValley ? 1.6f : 1.3f;
                f = -0.6f + (headFloor + 0.6f) * MathF.Pow(1f - ti / coastT, pw);
            }
            else
            {
                f = -0.6f - 14f * (ti - coastT) / MathF.Max(1e-3f, 1f - coastT);
            }

            c.Floor[i] = f;
            c.Half[i] = Noise.Lerp(half0, half1, ti);
        }

        return c;
    }

    private static float[] ArcFractions(List<Vector2> pts)
    {
        var t = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) t[i] = t[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
        float total = MathF.Max(t[^1], 1e-3f);
        for (int i = 0; i < t.Length; i++) t[i] /= total;
        return t;
    }

    private static float CoastFraction(List<Vector2> pts, Func<Vector2, float> height)
    {
        float[] t = ArcFractions(pts);
        for (int i = pts.Count / 3; i < pts.Count; i++)
            if (height(pts[i]) < 0f) return MathF.Max(t[i], 0.05f);
        return 0.95f;
    }

    /// <summary>Rounds off a line's corners, keeping its ends.</summary>
    private static void Smooth(List<Vector2> pts, int passes)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            var copy = pts.ToArray();
            for (int i = 1; i + 1 < pts.Count; i++) pts[i] = copy[i] * 0.5f + (copy[i - 1] + copy[i + 1]) * 0.25f;
        }
    }

    // ------------------------------------------------------------------ the rain

    private void Erode(Options o)
    {
        int n = GridN;
        var pre = new float[n * n];
        Parallel.For(0, n, z =>
        {
            var info = new GroundInfo();
            for (int x = 0; x < n; x++) pre[z * n + x] = Relief(-Half + x * GridSpacing, -Half + z * GridSpacing, true, ref info);
        });
        var wet = (float[])pre.Clone();
        int land = 0;
        foreach (float h in pre)
            if (h > 2f) land++;
        // The drop rules were made for a grid about 20 m apart; on a coarser grid each drop does proportionally more.
        var s = new Erosion.Settings { HeightUnit = 220f * GridSpacing / 20f };
        Erosion.Rain(wet, n, n, (int)(land * o.DropsPerLandSample), (ulong)(uint)Seed * 6364136223846793005ul + 1442695040888963407ul, s);
        var delta = new float[n * n];
        // The rain's wear fades out toward the sea, so the coasts keep the cliffs and beaches they were given instead
        // of being combed into gullies all along their edge.
        for (int i = 0; i < delta.Length; i++) delta[i] = Math.Clamp(wet[i] - pre[i], -160f, 80f) * Noise.Smooth(2f, 110f, pre[i]);
        // One gentle blur takes the drops' speckle out.
        _delta = new float[n * n];
        Parallel.For(1, n - 1, z =>
        {
            for (int x = 1; x < n - 1; x++)
            {
                int i = z * n + x;
                _delta[i] = delta[i] * 0.5f + (delta[i - 1] + delta[i + 1] + delta[i - n] + delta[i + n]) * 0.125f;
            }
        });
    }

    // ------------------------------------------------------------------ canyons, cut sharp after the rain

    /// <summary>The ground before canyons are cut: the islands, their valleys and the rain's wear.</summary>
    private float Worn(float x, float z)
    {
        var info = new GroundInfo();
        return Relief(x, z, true, ref info) + DeltaAt(x, z);
    }

    private void MakeCanyons(PlaceNames names)
    {
        var rng = new Rng((ulong)(uint)Seed * 15485863ul + 7ul);
        for (int ii = 0; ii < Islands.Count; ii++)
        {
            Island isl = Islands[ii];
            if (isl.Kind != IslandKind.Canyonlands) continue;
            // The main canyon winds from the middle of the plateau out to the sea at the island's end.
            Vector2 head = isl.ToWorld(new Vector2(-0.5f * isl.A, -0.12f * isl.B));
            Vector2 mouth = isl.ToWorld(new Vector2(1.12f * isl.A, 0.18f * isl.B));
            List<Vector2> main = Wander(head, mouth, 25f, 1.1f, 480f, rng.Int(100000), 700);
            float coastT = CoastFraction(main, p => Worn(p.X, p.Y));
            float[] t = ArcFractions(main);
            var mc = new Course
            {
                Kind = CourseKind.Canyon, Island = ii, P = main.ToArray(), WallRun = 0.95f, Terraces = 4, Reach = 520f,
                Floor = new float[main.Count], Half = new float[main.Count],
            };
            for (int i = 0; i < main.Count; i++)
            {
                mc.Floor[i] = t[i] < coastT ? -4f + 124f * MathF.Pow(1f - t[i] / coastT, 1.25f) : -4f - 10f * (t[i] - coastT);
                mc.Half[i] = Noise.Lerp(20f, 50f, t[i]) * (1f + 0.4f * Noise.Fbm(t[i] * 14f, 0.3f, Seed + 71, 2));
            }

            Courses.Add(mc);

            // Side canyons join it from either side, pointing downstream as tributaries do.
            float[] at = { 0.28f, 0.5f, 0.7f };
            var sides = new List<Course>();
            for (int k = 0; k < at.Length; k++)
            {
                int j = (int)(at[k] * (main.Count - 1));
                Vector2 down = Vector2.Normalize(main[Math.Min(j + 3, main.Count - 1)] - main[Math.Max(j - 3, 0)]);
                float sideSign = k % 2 == 0 ? 1f : -1f;
                Vector2 dir = Vector2.Normalize(new Vector2(-down.Y, down.X) * 0.85f * sideSign - down * 0.5f);
                Course sc = Tributary(CourseKind.SideCanyon, ii, main[j], dir, mc.Floor[j], rng.Range(1500f, 2200f), 0.95f, 380f,
                    rng.Int(100000), 9f, 16f, 0.9f, 3);
                if (sc != null)
                {
                    Courses.Add(sc);
                    sides.Add(sc);
                }
            }

            // Slot canyons: narrow twisting clefts in the plateau that drop into a bigger canyon.
            for (int k = 0; k < 3 && sides.Count > 0; k++)
            {
                Course host = k < sides.Count ? sides[k] : mc;
                int j = (int)(host.P.Length * rng.Range(0.35f, 0.6f));
                Vector2 down = Vector2.Normalize(host.P[Math.Min(j + 2, host.P.Length - 1)] - host.P[Math.Max(j - 2, 0)]);
                float sideSign = rng.Float() < 0.5f ? 1f : -1f;
                Vector2 dir = Vector2.Normalize(new Vector2(-down.Y, down.X) * sideSign - down * 0.3f);
                Course slot = Slot(ii, host.P[j], dir, host.Floor[j], rng.Range(650f, 950f), rng.Int(100000));
                if (slot != null) Courses.Add(slot);
            }
        }

        foreach (Course c in Courses) c.Name ??= NameFor(c, names);
    }

    /// <summary>A line from <paramref name="from"/> to <paramref name="to"/>, meandering.</summary>
    private static List<Vector2> Wander(Vector2 from, Vector2 to, float step, float wander, float wavelength, int seed, int max)
    {
        var pts = new List<Vector2> { from };
        Vector2 at = from;
        float walked = 0;
        for (int i = 0; i < max && Vector2.Distance(at, to) > step * 1.5f; i++)
        {
            Vector2 dir = Vector2.Normalize(to - at);
            Vector2 side = new(-dir.Y, dir.X);
            // Less wander near the end, so it arrives.
            float near = Noise.Smooth(0f, 600f, Vector2.Distance(at, to));
            at += Vector2.Normalize(dir + side * wander * near * Noise.Fbm(walked / wavelength, 0.5f, seed, 2) * 1.6f) * step;
            walked += step;
            pts.Add(at);
        }

        Smooth(pts, 3);
        return pts;
    }

    /// <summary>A side canyon from a junction on its host up into the plateau (stored head first, junction last).</summary>
    private Course Tributary(CourseKind kind, int ii, Vector2 junction, Vector2 dir, float junctionFloor, float length, float wander,
        float wavelength, int seed, float halfHead, float halfMouth, float wallRun, int terraces)
    {
        var pts = new List<Vector2> { junction };
        Vector2 at = junction;
        const float step = 25f;
        for (float walked = 0; walked < length; walked += step)
        {
            Vector2 side = new(-dir.Y, dir.X);
            at += Vector2.Normalize(dir + side * wander * Noise.Fbm(walked / wavelength, 0.5f, seed, 2) * 1.6f) * step;
            if (Worn(at.X, at.Y) < 120f) break; // off the plateau
            pts.Add(at);
        }

        if (pts.Count < 12) return null;
        Smooth(pts, 3);
        pts.Reverse();
        float[] t = ArcFractions(pts);
        float headFloor = junctionFloor + 105f;
        var c = new Course
        {
            Kind = kind, Island = ii, P = pts.ToArray(), WallRun = wallRun, Terraces = terraces, Reach = 420f,
            Floor = new float[pts.Count], Half = new float[pts.Count],
        };
        for (int i = 0; i < pts.Count; i++)
        {
            c.Floor[i] = junctionFloor + (headFloor - junctionFloor) * MathF.Pow(1f - t[i], 1.4f);
            c.Half[i] = Noise.Lerp(halfHead, halfMouth, t[i]);
        }

        return c;
    }

    /// <summary>
    /// A slot canyon: a cleft a few meters wide and up to 35 m deep, starting as a shallow crack on the plateau, deepening
    /// as it twists along, then dropping steeply (a pour-off) into the canyon it feeds.
    /// </summary>
    private Course Slot(int ii, Vector2 junction, Vector2 dir, float junctionFloor, float length, int seed)
    {
        var pts = new List<Vector2> { junction };
        Vector2 at = junction;
        const float step = 8f;
        for (float walked = 0; walked < length; walked += step)
        {
            Vector2 side = new(-dir.Y, dir.X);
            at += Vector2.Normalize(dir + side * 0.75f * Noise.Fbm(walked / 160f, 0.5f, seed, 3) * 1.6f) * step;
            pts.Add(at);
        }

        Smooth(pts, 2);
        pts.Reverse();
        float[] t = ArcFractions(pts);
        var c = new Course
        {
            Kind = CourseKind.Slot, Island = ii, P = pts.ToArray(), WallRun = 0.12f, Terraces = 0, Reach = 40f,
            Floor = new float[pts.Count], Half = new float[pts.Count],
        };
        float lowest = float.MaxValue;
        for (int i = 0; i < pts.Count; i++)
        {
            float surface = Worn(pts[i].X, pts[i].Y);
            float depth = 3.5f + 32f * Noise.Smooth(0f, 0.55f, t[i]);
            float f = surface - depth;
            f = Noise.Lerp(f, junctionFloor, Noise.Smooth(0.72f, 1f, t[i]));
            // Never climbing on the way down.
            lowest = MathF.Min(lowest, f);
            c.Floor[i] = lowest;
            c.Half[i] = Noise.Lerp(1.6f, 2.6f, t[i]);
        }

        return c;
    }

    // ------------------------------------------------------------------ the finished world

    private float BaseHeight(float x, float z)
    {
        var info = new GroundInfo();
        float h = Relief(x, z, true, ref info) + DeltaAt(x, z);
        h = CarveCanyons(x, z, h, ref info);
        return Site(x, z, h, ref info);
    }

    private HeightField BuildCoarse()
    {
        int n = GridN;
        var h = new float[n * n];
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++) h[z * n + x] = BaseHeight(-Half + x * GridSpacing, -Half + z * GridSpacing);
        });
        return new HeightField(n, n, GridSpacing, new Vector2(-Half, -Half), h, DeepSea);
    }

    private string NameFor(Course c, PlaceNames names)
    {
        string n = names.Next();
        return c.Kind switch
        {
            CourseKind.Fjord => n + names.Pick(" Fjord", " Sound", " Fjord"),
            CourseKind.GlacialValley => names.Pick(n + " Valley", "Glen " + n, n + " Dale"),
            CourseKind.RiverValley => names.Pick(n + " Valley", n + " Vale", n + " Dale"),
            CourseKind.PassValley => n + " Valley",
            CourseKind.Breach => "the " + n + " Breach",
            CourseKind.Slot => n + " Slot",
            _ => n + " Canyon",
        };
    }

    private void NameFeatures(PlaceNames names)
    {
        HeightField g = Coarse;
        for (int ii = 0; ii < Islands.Count; ii++)
        {
            Island isl = Islands[ii];
            float top = HighestNear(isl.Center, MathF.Max(isl.A, isl.B), out Vector2 topAt);
            string label = isl.Kind switch
            {
                IslandKind.RockyIslet => isl.Name + " Rock",
                IslandKind.SandyIslet => isl.Name + " Cay",
                _ => isl.Name + " Island",
            };
            Features.Add(new Feature { Kind = FeatureKind.Island, Name = label, At = isl.Center, Height = top, Island = ii });
            switch (isl.Kind)
            {
                case IslandKind.Alpine:
                    Features.Add(new Feature { Kind = FeatureKind.Range, Name = "the " + names.Next() + " Range", At = isl.ToWorld(new Vector2(-0.3f * isl.A, 0f)), Height = top, Island = ii });
                    foreach ((Vector2 at, float h) in Peaks(isl, 5, 1300f))
                        Features.Add(new Feature { Kind = FeatureKind.Peak, Name = names.Pick("Mount " + names.Next(), names.Next() + " Peak", "the " + names.Next() + " Horn"), At = at, Height = h, Island = ii });
                    break;
                case IslandKind.Highlands:
                    Features.Add(new Feature { Kind = FeatureKind.Moor, Name = names.Next() + " Moor", At = isl.Center, Height = g.HeightAt(isl.Center.X, isl.Center.Y), Island = ii });
                    foreach ((Vector2 at, float h) in Peaks(isl, 2, 1200f))
                        Features.Add(new Feature { Kind = FeatureKind.Peak, Name = names.Next() + names.Pick(" Fell", " Tor", " Pike"), At = at, Height = h, Island = ii });
                    break;
                case IslandKind.Stronghold:
                    foreach ((Vector2 at, float h) in Peaks(isl, 1, 1000f))
                        Features.Add(new Feature { Kind = FeatureKind.Peak, Name = names.Next() + " Hill", At = at, Height = h, Island = ii });
                    break;
                case IslandKind.Volcanic:
                {
                    string v = names.Next();
                    Features.Add(new Feature { Kind = FeatureKind.Volcano, Name = "Mount " + v, At = topAt, Height = top, Island = ii });
                    Features.Add(new Feature { Kind = FeatureKind.Crater, Name = v + " Crater", At = isl.Center, Height = g.HeightAt(isl.Center.X, isl.Center.Y), Island = ii });
                    break;
                }
                case IslandKind.Canyonlands:
                {
                    Vector2 mesa = isl.ToWorld(new Vector2(-0.15f * isl.A, 0.35f * isl.B));
                    Features.Add(new Feature { Kind = FeatureKind.Mesa, Name = names.Next() + " Mesa", At = mesa, Height = g.HeightAt(mesa.X, mesa.Y), Island = ii });
                    Vector2? butte = FindButte(isl);
                    if (butte.HasValue)
                        Features.Add(new Feature { Kind = FeatureKind.Buttes, Name = "the " + names.Next() + " Buttes", At = butte.Value, Height = g.HeightAt(butte.Value.X, butte.Value.Y), Island = ii });
                    break;
                }
                case IslandKind.RockyIslet:
                    Features.Add(new Feature { Kind = FeatureKind.Stacks, Name = isl.Name + " Stacks", At = isl.ToWorld(new Vector2(isl.A * 1.15f, 0f)), Height = 30f, Island = ii });
                    break;
            }
        }

        foreach (Course c in Courses)
        {
            FeatureKind k = c.Kind switch
            {
                CourseKind.Fjord => FeatureKind.Fjord,
                CourseKind.Slot => FeatureKind.Slot,
                CourseKind.Canyon or CourseKind.SideCanyon or CourseKind.Breach => FeatureKind.Canyon,
                _ => FeatureKind.Valley,
            };
            // Labelled partway down, where it's well formed.
            Vector2 at = c.P[(int)(c.P.Length * (c.Kind == CourseKind.Fjord ? 0.55f : 0.4f))];
            Features.Add(new Feature { Kind = k, Name = c.Name, At = at, Height = g.HeightAt(at.X, at.Y), Island = c.Island });
            if (c.Kind == CourseKind.PassValley && !Features.Any(f => f.Kind == FeatureKind.Pass))
                Features.Add(new Feature { Kind = FeatureKind.Pass, Name = names.Next() + " Pass", At = c.P[0], Height = g.HeightAt(c.P[0].X, c.P[0].Y), Island = c.Island });
        }

        Island home = Islands.First(i => i.Kind == IslandKind.Foothold);
        Features.Add(new Feature { Kind = FeatureKind.Harbour, Name = names.Next() + " Harbour", At = Start.ToWorld(0f, Start.CoastOffset), Height = 0f, Island = Islands.IndexOf(home) });
    }

    private float HighestNear(Vector2 c, float r, out Vector2 at)
    {
        HeightField g = Coarse;
        float best = float.MinValue;
        at = c;
        for (float z = c.Y - r; z <= c.Y + r; z += GridSpacing * 2)
        for (float x = c.X - r; x <= c.X + r; x += GridSpacing * 2)
        {
            float h = g.HeightAt(x, z);
            if (h > best)
            {
                best = h;
                at = new Vector2(x, z);
            }
        }

        return best;
    }

    /// <summary>The highest summits on an island, at least <paramref name="apart"/> meters from each other.</summary>
    private List<(Vector2 at, float h)> Peaks(Island isl, int count, float apart)
    {
        HeightField g = Coarse;
        var all = new List<(Vector2 at, float h)>();
        float r = MathF.Max(isl.A, isl.B);
        float step = GridSpacing * 2;
        for (float z = isl.Center.Y - r; z <= isl.Center.Y + r; z += step)
        for (float x = isl.Center.X - r; x <= isl.Center.X + r; x += step)
        {
            float h = g.HeightAt(x, z);
            if (h < 150f) continue;
            // A summit: higher than everything round it out to 300 m.
            bool top = true;
            for (int k = 0; k < 8 && top; k++)
            {
                float ox = (k % 3 - 1) * 300f, oz = (k / 3 - 1) * 300f;
                if (k == 4) { ox = 300f; oz = 300f; }
                if (g.HeightAt(x + ox, z + oz) > h) top = false;
            }

            if (top && Islands.IndexOf(isl) == OwnerOf(new Vector2(x, z))) all.Add((new Vector2(x, z), h));
        }

        var picked = new List<(Vector2, float)>();
        foreach ((Vector2 at, float h) in all.OrderByDescending(p => p.h))
        {
            if (picked.Any(p => Vector2.Distance(p.Item1, at) < apart)) continue;
            picked.Add((at, h));
            if (picked.Count >= count) break;
        }

        return picked;
    }

    /// <summary>Which island a point belongs to (the one it's most inside).</summary>
    public int OwnerOf(Vector2 p)
    {
        int best = -1;
        float bestS = float.MinValue;
        for (int i = 0; i < Islands.Count; i++)
        {
            IslandHeight(Islands[i], p, out float s, out _);
            if (s > bestS)
            {
                bestS = s;
                best = i;
            }
        }

        return best;
    }

    private Vector2? FindButte(Island isl)
    {
        HeightField g = Coarse;
        float r = MathF.Max(isl.A, isl.B);
        for (float z = isl.Center.Y - r; z <= isl.Center.Y + r; z += 60f)
        for (float x = isl.Center.X - r; x <= isl.Center.X + r; x += 60f)
        {
            float h = g.HeightAt(x, z);
            if (h < 130f) continue;
            // Standing alone: low bench all round it a few hundred meters out.
            int low = 0;
            for (int k = 0; k < 8; k++)
            {
                float ox = (k % 3 - 1) * 380f, oz = (k / 3 - 1) * 380f;
                if (k == 4) { ox = 380f; oz = 380f; }
                if (g.HeightAt(x + ox, z + oz) < 70f) low++;
            }

            if (low >= 7) return new Vector2(x, z);
        }

        return null;
    }
}

internal static class IslandExtensions
{
    public static float MathMinAxis(this Island i) => MathF.Min(i.A, i.B);
}
