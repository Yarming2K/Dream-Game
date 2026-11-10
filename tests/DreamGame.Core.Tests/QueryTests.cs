using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class HeightFieldTests
{
    /// <summary>Rolling hills on a 65 × 65 grid, 1 m apart, centered on the origin, floor at -10.</summary>
    private static HeightField Hills()
    {
        const int n = 65;
        var h = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
            h[z * n + x] = 3f + 4f * MathF.Sin(x * 0.21f) * MathF.Cos(z * 0.17f) + (x % 3) * 0.1f;
        return new HeightField(n, n, 1f, new Vector2(-32, -32), h, -10f);
    }

    [Fact]
    public void HeightMatchesTheSamplesAtTheCorners()
    {
        HeightField f = Hills();
        for (int z = 0; z < f.Depth; z += 7)
        for (int x = 0; x < f.Width; x += 5)
            Assert.Equal(f.Heights[z * f.Width + x], f.HeightAt(-32 + x, -32 + z), 3);
        Assert.Equal(-10f, f.HeightAt(500, 0));
    }

    [Fact]
    public void ARayStraightDownLandsOnTheSurface()
    {
        HeightField f = Hills();
        var rng = new Random(3);
        for (int i = 0; i < 500; i++)
        {
            float x = (float)rng.NextDouble() * 62 - 31, z = (float)rng.NextDouble() * 62 - 31;
            Assert.True(f.Raycast(new Vector3(x, 30, z), new Vector3(x, -30, z), out float d, out Vector3 n));
            Assert.Equal(f.HeightAt(x, z), 30 - d, 2);
            Assert.True(n.Y > 0);
        }
    }

    /// <summary>A slanted ray finds the same first contact as marching along it in tiny steps.</summary>
    [Fact]
    public void SlantedRaysMatchAFineMarch()
    {
        HeightField f = Hills();
        var rng = new Random(11);
        int hits = 0;
        for (int i = 0; i < 400; i++)
        {
            // Kept over the grid: its test edges are sheer cliffs, which the island's (sloping into the seabed) aren't.
            var a = new Vector3((float)rng.NextDouble() * 60 - 30, 4 + (float)rng.NextDouble() * 10, (float)rng.NextDouble() * 60 - 30);
            var b = new Vector3((float)rng.NextDouble() * 60 - 30, (float)rng.NextDouble() * 8 - 3, (float)rng.NextDouble() * 60 - 30);
            if (a.Y < f.HeightAt(a.X, a.Z)) continue;
            bool hit = f.Raycast(a, b, out float d, out _);
            float len = Vector3.Distance(a, b);
            float march = float.PositiveInfinity;
            const int steps = 20000;
            for (int s = 1; s <= steps; s++)
            {
                Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                if (p.Y <= f.HeightAt(p.X, p.Z))
                {
                    march = len * s / steps;
                    break;
                }
            }

            Assert.Equal(!float.IsPositiveInfinity(march), hit);
            if (!hit) continue;
            hits++;
            Assert.InRange(d, march - len / steps * 2 - 0.01f, march + 0.01f);
        }

        Assert.True(hits > 100);
    }

    [Fact]
    public void HighShellsSkipTheGroundAndMissesReportNothing()
    {
        HeightField f = Hills();
        Assert.False(f.Raycast(new Vector3(-30, 40, 0), new Vector3(30, 35, 0), out _, out _));
        Assert.False(f.Raycast(new Vector3(0, 20, 0), new Vector3(0, 25, 0), out _, out _));
        // Out over the floor beyond the grid.
        Assert.True(f.Raycast(new Vector3(200, 5, 0), new Vector3(200, -20, 0), out float d, out _));
        Assert.Equal(15f, d, 3);
    }
}

public class SphereGridTests
{
    private static (SphereGrid grid, List<(Vector3 c, float r)> spheres) Random(int count, int seed, float spread)
    {
        var rng = new Random(seed);
        var grid = new SphereGrid(48f, 8f);
        var list = new List<(Vector3, float)>();
        for (int i = 0; i < count; i++)
        {
            var c = new Vector3((float)rng.NextDouble() * spread - spread / 2, (float)rng.NextDouble() * 60, (float)rng.NextDouble() * spread - spread / 2);
            // Mostly tanks, a few ships, one battleship.
            float r = i == 0 ? 170f : i % 17 == 0 ? 45f : 4f + (float)rng.NextDouble() * 6f;
            list.Add((c, r));
            Assert.Equal(i, grid.Add(c, r));
        }

        return (grid, list);
    }

    [Fact]
    public void SegmentQueriesFindExactlyWhatABruteForceCheckFinds()
    {
        (SphereGrid grid, List<(Vector3 c, float r)> spheres) = Random(400, 5, 3000f);
        var rng = new Random(9);
        var found = new List<int>();
        for (int q = 0; q < 3000; q++)
        {
            var a = new Vector3((float)rng.NextDouble() * 3000 - 1500, (float)rng.NextDouble() * 60, (float)rng.NextDouble() * 3000 - 1500);
            float len = q % 10 == 0 ? 900f : 15f;
            var dir = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.2f - 0.1f, (float)rng.NextDouble() - 0.5f));
            Vector3 b = a + dir * len;
            float pad = q % 3 == 0 ? 3.5f : 0f;
            found.Clear();
            grid.QuerySegment(a, b, pad, found);
            var expect = Enumerable.Range(0, spheres.Count)
                .Where(i => SphereGrid.SegmentDistance(a, b, spheres[i].c) <= spheres[i].r + pad).ToHashSet();
            Assert.Equal(expect.Count, found.Count);
            Assert.True(expect.SetEquals(found));
        }
    }

    [Fact]
    public void SphereQueriesFindExactlyWhatABruteForceCheckFinds()
    {
        (SphereGrid grid, List<(Vector3 c, float r)> spheres) = Random(300, 8, 2000f);
        var rng = new Random(2);
        var found = new List<int>();
        foreach (float radius in new[] { 1f, 6f, 30f, 400f, 5000f })
            for (int q = 0; q < 300; q++)
            {
                var p = new Vector3((float)rng.NextDouble() * 2000 - 1000, (float)rng.NextDouble() * 60, (float)rng.NextDouble() * 2000 - 1000);
                found.Clear();
                grid.QuerySphere(p, radius, found);
                var expect = Enumerable.Range(0, spheres.Count)
                    .Where(i => Vector3.Distance(p, spheres[i].c) <= spheres[i].r + radius).ToHashSet();
                Assert.True(expect.SetEquals(found));
                Assert.Equal(expect.Count, found.Count);
            }
    }

    [Fact]
    public void ClearingStartsOver()
    {
        var grid = new SphereGrid();
        grid.Add(Vector3.Zero, 5f);
        grid.Clear();
        var found = new List<int>();
        grid.QuerySphere(Vector3.Zero, 10f, found);
        Assert.Empty(found);
        Assert.Equal(0, grid.Add(new Vector3(100, 0, 0), 2f));
        grid.QuerySegment(new Vector3(90, 0, 0), new Vector3(110, 0, 0), 0f, found);
        Assert.Equal(new[] { 0 }, found);
    }
}
