using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DreamGame.Core.Tests;

public class FloatModelTests
{
    private static Dictionary<Int3, BlockDef> Hull(int sx, int sy, int sz)
    {
        var cells = new Dictionary<Int3, BlockDef>();
        BlockDef d = BlockCatalog.Get("armor_light");
        for (int x = 0; x < sx; x++)
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
            if (x == 0 || y == 0 || z == 0 || x == sx - 1 || y == sy - 1 || z == sz - 1) cells[new Int3(x, y, z)] = d;
        return cells;
    }

    private static IEnumerable<(Int3, float)> Solids(Dictionary<Int3, BlockDef> cells) =>
        cells.Select(kv => (kv.Key, kv.Value.Displacement * Hydro.CellVolume));

    private static float Total(FloatModel m) => m.Bricks.Sum(b => b.Solid + m.AirIn(b));

    [Fact]
    public void BricksHoldTheSameVolumeAsTheCellsTheyGroup()
    {
        var hull = Hull(8, 6, 12);
        List<Compartment> comps = Hydro.FindCompartments(hull);
        float exact = Hydro.BuoyantVolumes(hull, comps).Sum(p => p.volume);
        var small = new FloatModel(Solids(hull), comps, 1);
        var coarse = new FloatModel(Solids(hull), comps, 4);
        Assert.Equal(exact, Total(small), 2);
        Assert.Equal(exact, Total(coarse), 2);
        Assert.True(coarse.Bricks.Count < small.Bricks.Count / 10);
        Assert.Same(comps[0], small.CompartmentAt(new Int3(3, 3, 5)));
        Assert.Null(small.CompartmentAt(new Int3(0, 3, 5)));
    }

    [Fact]
    public void AHugeHullStillMakesOnlyAFewThousandBricks()
    {
        var hull = Hull(200, 30, 40);
        List<Compartment> comps = Hydro.FindCompartments(hull);
        var m = new FloatModel(Solids(hull), comps);
        Assert.True(m.BrickSize > 1);
        Assert.InRange(m.Bricks.Count, 1, 4000);
    }

    [Fact]
    public void FloodWaterFillsFromTheBottom()
    {
        var hull = Hull(6, 8, 6);
        List<Compartment> comps = Hydro.FindCompartments(hull);
        var m = new FloatModel(Solids(hull), comps, 1);
        Compartment c = comps[0];
        float dry = Total(m);
        c.Water = c.CapacityM3 * 0.5f;
        m.UpdateLevels();
        Assert.Equal(dry - c.Water, Total(m), 2);
        Assert.True(m.Flooded(c, c.MinY));
        Assert.False(m.Flooded(c, c.MaxY));
        // The low bricks have lost their air, the high ones haven't.
        FloatModel.Brick low = m.Bricks.Where(b => b.Bulk > b.Solid).OrderBy(b => b.Center.Y).First();
        FloatModel.Brick high = m.Bricks.Where(b => b.Bulk > b.Solid).OrderBy(b => b.Center.Y).Last();
        Assert.Equal(0f, m.AirIn(low), 3);
        Assert.True(m.AirIn(high) > 0);
    }
}
