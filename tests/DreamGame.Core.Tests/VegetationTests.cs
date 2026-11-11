using System;
using System.Linq;
using System.Numerics;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class VegetationTests
{
    /// <summary>Rolling land 3 m up and more, sea along one edge, a steep bank in one corner.</summary>
    private static HeightField Island()
    {
        int n = 257;
        var h = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float wx = x - 128f, wz = z - 128f;
            h[z * n + x] = wx > 100 ? -4f : 3f + 6f * (MathF.Sin(wx * 0.03f) * 0.5f + 0.5f) + (wx < -90 && wz < -90 ? (-90 - wx) * 1.2f : 0f);
        }

        return new HeightField(n, n, 1f, new Vector2(-128, -128), h, -26f);
    }

    private static bool Lane(float x, float z) => MathF.Abs(x) < 15f;

    [Fact]
    public void WoodsGrowWhereTheyMayAndOnlyThere()
    {
        HeightField f = Island();
        var plants = Vegetation.Scatter(f, 7, 3f, Lane);
        Assert.InRange(plants.Count(p => p.IsTree), 60, 3000);
        Assert.Contains(plants, p => p.Kind == PlantKind.Bush);
        Assert.Contains(plants, p => p.IsRock);
        foreach (Plant p in plants)
        {
            Assert.False(Lane(p.X, p.Z), $"{p.Kind} in the lane at {p.X}, {p.Z}");
            Assert.True(f.HeightAt(p.X, p.Z) >= 4.2f, $"{p.Kind} on the beach or in the sea");
        }
    }

    [Fact]
    public void TheSameSeedGivesTheSameIsland()
    {
        HeightField f = Island();
        var a = Vegetation.Scatter(f, 7, 3f, Lane);
        var b = Vegetation.Scatter(f, 7, 3f, Lane);
        var c = Vegetation.Scatter(f, 8, 3f, Lane);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) Assert.Equal((a[i].Kind, a[i].X, a[i].Z, a[i].Scale), (b[i].Kind, b[i].X, b[i].Z, b[i].Scale));
        Assert.NotEqual(a.Count, c.Count);
    }

    [Fact]
    public void HeavyThingsKnockTreesOverLightOnesDont()
    {
        var tree = new Plant { Kind = PlantKind.Pine, Scale = 1f };
        Assert.True(Vegetation.Knocks(tree, 30000f, 3f));   // a tank at walking pace
        Assert.False(Vegetation.Knocks(tree, 1500f, 4f));   // a light car
        Assert.False(Vegetation.Knocks(new Plant { Kind = PlantKind.Boulder, Scale = 1f }, 90000f, 10f));
        Assert.True(Vegetation.Knocks(new Plant { Kind = PlantKind.Bush, Scale = 1f }, 900f, 2f));
    }

    [Fact]
    public void ABlastThrowsTreesAwayFromIt()
    {
        var plants = new System.Collections.Generic.List<Plant>
        {
            new() { Kind = PlantKind.Pine, X = 4, Z = 0, Scale = 1 },
            new() { Kind = PlantKind.Broadleaf, X = 0, Z = -3, Scale = 1 },
            new() { Kind = PlantKind.Pine, X = 40, Z = 0, Scale = 1 },
            new() { Kind = PlantKind.Rock, X = 1, Z = 1, Scale = 1 },
        };
        var hit = Vegetation.Blast(plants, Vector2.Zero, 6f, 1600f);
        Assert.Equal(new[] { 0, 1 }, hit);
        Assert.Equal(MathF.PI / 2, plants[0].FellToward, 3);  // toward +x
        Assert.Equal(MathF.PI, MathF.Abs(plants[1].FellToward), 3); // toward -z
        Assert.False(plants[2].Down);
        Assert.False(plants[3].Down);
    }
}
