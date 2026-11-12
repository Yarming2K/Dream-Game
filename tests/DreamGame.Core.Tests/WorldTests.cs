using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

/// <summary>The campaign world's geology: its islands, mountains, valleys, fjords, canyons and the start site.</summary>
public class WorldTests
{
    // Made once and shared: generating takes a few seconds.
    private static readonly Lazy<WorldMap> Shared = new(() => WorldMap.Generate(1));
    private static WorldMap W => Shared.Value;

    private static Course First(CourseKind k) => W.Courses.First(c => c.Kind == k);

    private static Vector2 Along(Course c, float t, out Vector2 dir)
    {
        int i = Math.Clamp((int)(t * (c.P.Length - 1)), 1, c.P.Length - 2);
        dir = Vector2.Normalize(c.P[i + 1] - c.P[i - 1]);
        return c.P[i];
    }

    [Fact]
    public void Noise_is_bounded_and_repeatable()
    {
        for (int i = 0; i < 2000; i++)
        {
            float x = i * 0.37f - 300f, z = i * 0.53f + 11f;
            float g = Noise.Gradient(x, z, 5);
            Assert.InRange(g, -1.01f, 1.01f);
            Assert.Equal(g, Noise.Gradient(x, z, 5));
            Assert.InRange(Noise.Ridged(x, z, 5, 5), 0f, 1f);
            Assert.InRange(Noise.Fbm(x, z, 5, 4), -1.01f, 1.01f);
        }

        Assert.NotEqual(Noise.Gradient(1.3f, 2.7f, 1), Noise.Gradient(1.3f, 2.7f, 2));
    }

    [Fact]
    public void The_same_seed_makes_the_same_world_and_another_seed_a_different_one()
    {
        var quick = new WorldMap.Options { GridSpacing = 40f, DropsPerLandSample = 0.5f };
        WorldMap a = WorldMap.Generate(7, quick), b = WorldMap.Generate(7, quick), c = WorldMap.Generate(8, quick);
        int differ = 0;
        for (int i = 0; i < 400; i++)
        {
            float x = -9000f + i * 45f, z = 8000f - i * 40f;
            Assert.Equal(a.HeightAt(x, z), b.HeightAt(x, z));
            if (MathF.Abs(a.HeightAt(x, z) - c.HeightAt(x, z)) > 1f) differ++;
        }

        Assert.True(differ > 100, $"only {differ} of 400 points differ between seeds");
        Assert.Equal(a.Features.Select(f => f.Name), b.Features.Select(f => f.Name));
    }

    [Fact]
    public void It_is_an_archipelago_of_six_to_ten_islands_with_sea_between_them()
    {
        Assert.InRange(W.Islands.Count, 6, 10);
        foreach (IslandKind k in new[] { IslandKind.Foothold, IslandKind.Stronghold, IslandKind.Alpine, IslandKind.Canyonlands, IslandKind.Volcanic, IslandKind.Highlands })
            Assert.Contains(W.Islands, i => i.Kind == k);

        // Every pair of big islands has open water between them.
        var big = W.Islands.Where(i => i.Kind is not (IslandKind.RockyIslet or IslandKind.SandyIslet)).ToList();
        foreach (Island a in big)
        foreach (Island b in big)
        {
            if (a == b) continue;
            float deepest = float.MaxValue;
            for (int k = 0; k <= 200; k++)
            {
                Vector2 p = Vector2.Lerp(a.Center, b.Center, k / 200f);
                deepest = MathF.Min(deepest, W.Coarse.HeightAt(p.X, p.Y));
            }

            Assert.True(deepest < -5f, $"{a.Name} and {b.Name} are joined (deepest {deepest:0} m)");
        }

        // About a fifth to two fifths of the world is land.
        int land = 0, all = 0;
        for (float z = -10000f; z < 10000f; z += 200f)
        for (float x = -10000f; x < 10000f; x += 200f, all++)
            if (W.Coarse.HeightAt(x, z) > 0f) land++;
        Assert.InRange(land / (float)all, 0.15f, 0.45f);
    }

    [Fact]
    public void The_mountains_rise_to_snowy_peaks()
    {
        Feature top = W.Features.Where(f => f.Kind == FeatureKind.Peak).OrderByDescending(f => f.Height).First();
        Assert.InRange(top.Height, 850f, 1500f);
        float h = W.Sample(top.At.X, top.At.Y, out GroundInfo info);
        GroundCover c = WorldMap.Cover(info, h, 0.1f);
        Assert.True(c.Snow > 0.5f, $"no snow on {top.Name} at {h:0} m (snow line {info.SnowLine:0} m)");
        Assert.Equal(0f, c.Forest);
    }

    [Fact]
    public void Fjords_let_the_sea_far_in_between_high_walls()
    {
        Course f = W.Courses.Where(c => c.Kind == CourseKind.Fjord).OrderByDescending(c => c.Length).First();
        bool found = false;
        for (float t = 0.35f; t < 0.8f && !found; t += 0.02f)
        {
            Vector2 p = Along(f, t, out Vector2 d);
            var side = new Vector2(-d.Y, d.X);
            float water = W.HeightAt(p.X, p.Y);
            float wallA = W.HeightAt(p.X + side.X * 500f, p.Y + side.Y * 500f);
            float wallB = W.HeightAt(p.X - side.X * 500f, p.Y - side.Y * 500f);
            if (water < -5f && wallA > 80f && wallB > 80f) found = true;
        }

        Assert.True(found, $"{f.Name}: nowhere is there sea between land 80 m high on both sides");
    }

    [Fact]
    public void A_pass_crosses_the_range_low_enough_and_gentle_enough_to_drive()
    {
        List<Course> pass = W.Courses.Where(c => c.Kind == CourseKind.PassValley).ToList();
        Assert.Equal(2, pass.Count);
        Assert.Equal(pass[0].P[0], pass[1].P[0]); // they meet at the top
        float peak = W.Features.Where(f => f.Kind == FeatureKind.Peak).Max(f => f.Height);
        Assert.True(pass[0].Floor[0] < peak * 0.55f, $"pass {pass[0].Floor[0]:0} m is too high under peaks of {peak:0} m");
        foreach (Course c in pass)
        {
            float rise = c.Floor[0] - c.Floor[^1];
            Assert.True(rise / c.Length < 0.2f, $"{c.Name} climbs {rise / c.Length:P0} on average");
        }
    }

    [Fact]
    public void The_main_canyon_is_deep_with_steep_layered_walls_and_reaches_the_sea()
    {
        Course c = First(CourseKind.Canyon);
        Vector2 mid = Along(c, 0.4f, out Vector2 d);
        var side = new Vector2(-d.Y, d.X);
        float floor = W.HeightAt(mid.X, mid.Y);
        float rimA = 0, rimB = 0;
        for (float o = 0; o < 600f; o += 10f)
        {
            rimA = MathF.Max(rimA, W.HeightAt(mid.X + side.X * o, mid.Y + side.Y * o));
            rimB = MathF.Max(rimB, W.HeightAt(mid.X - side.X * o, mid.Y - side.Y * o));
        }

        Assert.True(MathF.Min(rimA, rimB) - floor > 120f, $"canyon only {MathF.Min(rimA, rimB) - floor:0} m deep");
        // Somewhere in its walls a cliff: 15 m of rise in 6 m.
        bool cliff = false;
        for (float o = 10f; o < 400f && !cliff; o += 1f)
        {
            float a = W.HeightAt(mid.X + side.X * o, mid.Y + side.Y * o), b = W.HeightAt(mid.X + side.X * (o + 6f), mid.Y + side.Y * (o + 6f));
            cliff = b - a > 15f;
        }

        Assert.True(cliff, "no cliff in the canyon wall");
        // Its floor runs down to the sea.
        Assert.True(c.Floor.Min() <= 0f);
        Assert.True(W.HeightAt(c.P[^1].X, c.P[^1].Y) < 0f);
    }

    [Fact]
    public void Slot_canyons_are_a_few_meters_wide_and_deep()
    {
        bool found = false;
        foreach (Course c in W.Courses.Where(c => c.Kind == CourseKind.Slot))
        for (float t = 0.2f; t < 0.7f && !found; t += 0.05f)
        {
            Vector2 p = Along(c, t, out Vector2 d);
            var side = new Vector2(-d.Y, d.X);
            float h = W.HeightAt(p.X, p.Y);
            float a = W.HeightAt(p.X + side.X * 12f, p.Y + side.Y * 12f), b = W.HeightAt(p.X - side.X * 12f, p.Y - side.Y * 12f);
            float nearA = W.HeightAt(p.X + side.X * 1.2f, p.Y + side.Y * 1.2f), nearB = W.HeightAt(p.X - side.X * 1.2f, p.Y - side.Y * 1.2f);
            if (MathF.Min(a, b) - h > 15f && MathF.Abs(nearA - h) < 1.5f && MathF.Abs(nearB - h) < 1.5f) found = true;
        }

        Assert.True(found, "no slot canyon 15 m deep within 12 m and flat-floored across 2.4 m");
    }

    [Fact]
    public void The_volcano_has_a_crater_and_a_breach_into_it()
    {
        Feature crater = W.Features.First(f => f.Kind == FeatureKind.Crater);
        Feature cone = W.Features.First(f => f.Kind == FeatureKind.Volcano);
        Assert.True(cone.Height - W.HeightAt(crater.At.X, crater.At.Y) > 100f);
        Course breach = First(CourseKind.Breach);
        Assert.True(breach.Floor[0] - breach.Floor[^1] > 200f);
        Assert.True((breach.Floor[0] - breach.Floor[^1]) / breach.Length < 0.25f);
    }

    [Fact]
    public void The_start_site_is_level_with_deep_water_off_it()
    {
        StartSite s = W.Start;
        foreach (Vector2 p in new[] { s.Spawn, s.Depot, s.Helipad, s.StripCenter, s.StripCenter + s.Along * s.StripHalfLength, s.StripCenter - s.Along * s.StripHalfLength })
            Assert.InRange(W.HeightAt(p.X, p.Y), s.Ground - 0.3f, s.Ground + 0.3f);
        Assert.True(W.HeightAt(s.SeaSpawn.X, s.SeaSpawn.Y) < -15f);
        Assert.True(W.HeightAt(s.SeaDepot.X, s.SeaDepot.Y) < -15f);
        Assert.True(W.HeightAt(s.Anchorage.X, s.Anchorage.Y) < -25f);
        float h = W.Sample(s.Spawn.X, s.Spawn.Y, out GroundInfo info);
        Assert.True(info.Site);
        Assert.Equal(0f, WorldMap.Cover(info, h, 0f).Forest);
    }

    [Fact]
    public void Every_place_has_its_own_name()
    {
        List<string> names = W.Features.Select(f => f.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (FeatureKind k in new[] { FeatureKind.Island, FeatureKind.Range, FeatureKind.Peak, FeatureKind.Pass, FeatureKind.Valley, FeatureKind.Fjord,
                     FeatureKind.Canyon, FeatureKind.Slot, FeatureKind.Mesa, FeatureKind.Volcano, FeatureKind.Crater, FeatureKind.Harbour })
            Assert.Contains(W.Features, f => f.Kind == k);
        Assert.Equal(W.Islands.Count, W.Features.Count(f => f.Kind == FeatureKind.Island));
    }

    [Fact]
    public void Filling_a_grid_gives_the_same_heights_as_asking_point_by_point()
    {
        var g = new float[33 * 33];
        W.Fill(g, 33, 33, 3100f, 5200f, 1.5f);
        for (int z = 0; z < 33; z += 4)
        for (int x = 0; x < 33; x += 4)
            Assert.Equal(W.HeightAt(3100f + x * 1.5f, 5200f + z * 1.5f), g[z * 33 + x]);
    }

    [Fact]
    public void The_coarse_world_agrees_with_the_fine_ground()
    {
        float err = 0;
        int n = 0;
        for (float z = -9000; z < 9000; z += 731f)
        for (float x = -9000; x < 9000; x += 677f)
        {
            float fine = W.HeightAt(x, z);
            if (fine < 0) continue;
            err += MathF.Abs(W.Coarse.HeightAt(x, z) - fine);
            n++;
        }

        Assert.True(err / n < 6f, $"coarse differs from fine by {err / n:0.0} m on average");
    }

    [Fact]
    public void Ground_cover_follows_height_and_steepness()
    {
        var alpine = new GroundInfo { Kind = IslandKind.Alpine, SnowLine = 840f, Beach = 1f };
        Assert.True(WorldMap.Cover(alpine, 1000f, 0.05f).Snow > 0.9f);
        Assert.True(WorldMap.Cover(alpine, 300f, 0.45f).Rock > 0.9f);
        Assert.True(WorldMap.Cover(alpine, 1f, 0.02f).Sand > 0.5f);
        Assert.True(WorldMap.Cover(alpine, 200f, 0.05f).Forest > 0.4f);
        Assert.Equal(0.5f, WorldMap.Cover(new GroundInfo { Kind = IslandKind.Canyonlands, SnowLine = 840f }, 100f, 0.4f).RockKind);
        Assert.Equal(1f, WorldMap.Cover(new GroundInfo { Kind = IslandKind.Volcanic, SnowLine = 840f }, 100f, 0.4f).RockKind);
    }

    [Fact]
    public void Rock_strata_step_at_the_same_heights_everywhere()
    {
        // Monotonic, matching at layer boundaries, and mostly flat (ledges) for the lower part of each layer.
        float last = WorldMap.Strata(0f);
        for (float h = 0.5f; h < 400f; h += 0.5f)
        {
            float s = WorldMap.Strata(h);
            Assert.True(s >= last - 1e-3f);
            last = s;
        }

        Assert.Equal(WorldMap.StrataM * 3f, WorldMap.Strata(WorldMap.StrataM * 3f), 3);
        Assert.True(WorldMap.Strata(WorldMap.StrataM * 3f + 5f) - WorldMap.StrataM * 3f < 2f);
    }

    [Fact]
    public void Rain_wears_gullies_into_a_slope_and_is_repeatable()
    {
        const int n = 96;
        float[] Cone()
        {
            var h = new float[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                h[z * n + x] = MathF.Max(0f, 300f - MathF.Sqrt((x - 48f) * (x - 48f) + (z - 48f) * (z - 48f)) * 7f);
            return h;
        }

        float[] a = Cone(), b = Cone();
        Erosion.Rain(a, n, n, 6000, 42);
        Erosion.Rain(b, n, n, 6000, 42);
        Assert.Equal(a, b);
        // Round the cone, the ground is no longer the same height all the way round: gullies and spurs.
        float min = float.MaxValue, max = float.MinValue;
        for (int k = 0; k < 64; k++)
        {
            float ang = k * MathF.Tau / 64f;
            int x = (int)(48f + MathF.Cos(ang) * 20f), z = (int)(48f + MathF.Sin(ang) * 20f);
            min = MathF.Min(min, a[z * n + x]);
            max = MathF.Max(max, a[z * n + x]);
        }

        Assert.True(max - min > 5f, $"only {max - min:0.0} m of relief round the cone");
    }

    [Fact]
    public void A_fine_grid_hands_rays_beyond_its_edge_to_the_coarse_ground()
    {
        // Coarse: a 100 m high wall at x = 500. Fine: flat ground near the middle.
        var coarse = new HeightField(101, 101, 20f, new Vector2(-1000f, -1000f), Enumerable.Range(0, 101 * 101).Select(i => (i % 101) * 20f - 1000f >= 500f ? 100f : 0f).ToArray(), -50f);
        var fine = new HeightField(101, 101, 1f, new Vector2(-50f, -50f), new float[101 * 101], -50f) { Beyond = coarse };
        Assert.Equal(100f, fine.HeightAt(600f, 0f));
        Assert.Equal(0f, fine.HeightAt(10f, 0f));
        // A level shot from the fine grid hits the wall beyond it.
        Assert.True(fine.Raycast(new Vector3(0f, 50f, 0f), new Vector3(900f, 50f, 0f), out float d, out _));
        Assert.InRange(d, 480f, 501f);
        // A shot down into the fine grid hits its flat ground, not the coarse ground under it.
        Assert.True(fine.Raycast(new Vector3(-200f, 20f, 0f), new Vector3(10f, -1f, 0f), out d, out _));
        Assert.InRange(new Vector3(-200f, 20f, 0f).X + d * 210f / new Vector3(210f, -21f, 0f).Length(), -1f, 1f);
    }

    [Fact]
    public void A_crater_beside_a_cliff_leaves_the_cliff_standing()
    {
        // A 20 m cliff along x = 30 (rock), flat ground below it.
        const int n = 61;
        var h = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
            h[z * n + x] = x >= 30 ? 20f : 0f;
        var f = new HeightField(n, n, 1f, Vector2.Zero, h, -10f);
        var e = new Earthworks(f);
        e.Crater(new Vector2(26f, 30f), 3f, 1.5f);
        // The cliff top a few meters along from the crater is still at full height.
        Assert.Equal(20f, f.HeightAt(31f, 40f), 2);
        Assert.Equal(20f, f.HeightAt(31f, 22f), 2);
    }
}
