using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class ColliderBoxesTests
{
    private static HashSet<Int3> Covered(List<(Int3 Min, Int3 Size)> boxes)
    {
        var all = new HashSet<Int3>();
        foreach ((Int3 min, Int3 size) in boxes)
            for (int x = 0; x < size.X; x++)
            for (int y = 0; y < size.Y; y++)
            for (int z = 0; z < size.Z; z++)
                Assert.True(all.Add(new Int3(min.X + x, min.Y + y, min.Z + z)), "boxes overlap");
        return all;
    }

    [Fact]
    public void BoxesCoverExactlyTheCells()
    {
        var rng = new Random(7);
        for (int trial = 0; trial < 30; trial++)
        {
            var cells = new HashSet<Int3>();
            int n = rng.Next(1, 400);
            for (int i = 0; i < n; i++) cells.Add(new Int3(rng.Next(-6, 6), rng.Next(-3, 4), rng.Next(-6, 6)));
            Assert.True(cells.SetEquals(Covered(ColliderBoxes.Merge(cells))));
        }
    }

    [Fact]
    public void ASolidBlockIsOneBox()
    {
        var cells = new List<Int3>();
        for (int x = 0; x < 10; x++)
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 7; z++)
            cells.Add(new Int3(x, y, z));
        Assert.Single(ColliderBoxes.Merge(cells));
    }

    [Fact]
    public void SmallMachinesKeepTheirSlopesBigOnesBoxThemIn()
    {
        BlockDef slope = BlockCatalog.All.First(d => d.HasCollider && d.Shape != BlockShape.Cube);
        Assert.False(ColliderBoxes.Merges(slope, bigSection: false));
        Assert.True(ColliderBoxes.Merges(slope, bigSection: true));
        Assert.False(ColliderBoxes.IsBig(new Int3(0, 0, 0), new Int3(20, 6, 30)));
        Assert.True(ColliderBoxes.IsBig(new Int3(0, 0, 0), new Int3(20, 6, 31)));
    }

    [Fact]
    public void TheBattleshipNeedsFarFewerBoxes()
    {
        GridModel g = GridModel.FromAnalysis(VehicleAnalysis.Analyze(ExampleDesigns.Colossus()));
        int shapes = 0;
        foreach (Section s in g.Sections)
        {
            if (s.Cells.Count == 0) continue;
            bool big = ColliderBoxes.IsBig(s.Min, s.Max);
            var groups = new Dictionary<Int3, List<Int3>>();
            foreach (BlockInstance b in g.Instances.Where(b => b.Section == s.Id && b.Def.HasCollider))
            {
                if (!ColliderBoxes.Merges(b.Def, big))
                {
                    shapes++;
                    continue;
                }

                Int3 k = ColliderBoxes.GroupOf(b.Cell, big);
                if (!groups.TryGetValue(k, out List<Int3> list)) groups[k] = list = new List<Int3>();
                list.AddRange(b.Cells);
            }

            shapes += groups.Values.Sum(l => ColliderBoxes.Merge(l).Count);
        }

        // It was about 3,200 (8 m groups, a shape of its own for every sloped block).
        Assert.InRange(shapes, 100, 1400);
    }
}
