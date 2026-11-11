using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

/// <summary>The quick check round a hole gives the same answer as the full check, or says it can't tell.</summary>
public class DetachNearTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string n in new[] { "Warden", "Jackal", "Corsair", "Trident", "Kestrel", "Zephyr" })
            yield return new object[] { n, false };
        foreach (Blueprint b in ExampleDesigns.Buildings.Take(4))
            yield return new object[] { b.Name, true };
    }

    private static string Key(List<BlockInstance> piece) => string.Join(";", piece.Select(b => b.Cell.ToString()).OrderBy(s => s, StringComparer.Ordinal));

    private static string[] Keys(List<List<BlockInstance>> pieces) => pieces.Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesTheFullCheckUnderFire(string name, bool building)
    {
        Blueprint bp = (building ? ExampleDesigns.Buildings : ExampleDesigns.All).First(b => b.Name.StartsWith(name, StringComparison.Ordinal));
        KeepRule rule = building ? KeepRule.Anchored : KeepRule.DriverSeat;
        foreach (int budget in new[] { Connectivity.NearBudget, 40 })
        {
            GridModel g = GridModel.FromAnalysis(VehicleAnalysis.Analyze(bp));
            // Start from what the game would keep.
            foreach (List<BlockInstance> p in Connectivity.FindDetached(g, rule))
                foreach (BlockInstance b in p) g.Remove(b);
            var rng = new Random(name.Length * 31 + budget);
            int unsure = 0, checks = 0;
            for (int shot = 0; shot < 60 && g.InstanceCount > 4; shot++)
            {
                BlockInstance[] alive = g.Instances.ToArray();
                BlockInstance at = alive[rng.Next(alive.Length)];
                int r = rng.Next(0, 4);
                var removed = new List<Int3>();
                foreach (BlockInstance b in alive)
                    if ((b.Cell - at.Cell).ToVector3().Length() <= r)
                    {
                        removed.AddRange(b.Cells);
                        g.Remove(b);
                    }

                List<List<BlockInstance>> full = Connectivity.FindDetached(g, rule);
                List<List<BlockInstance>> near = Connectivity.FindDetachedNear(g, rule, removed, budget);
                checks++;
                if (near == null) unsure++;
                else Assert.Equal(Keys(full), Keys(near));
                foreach (List<BlockInstance> p in full)
                    foreach (BlockInstance b in p) g.Remove(b);
            }

            // With the normal budget it should rarely need the full check on anything this size.
            if (budget == Connectivity.NearBudget) Assert.True(unsure * 4 <= checks, $"{name}: unsure {unsure} of {checks}");
        }
    }

    [Fact]
    public void AHoleInABattleshipIsQuick()
    {
        GridModel g = GridModel.FromAnalysis(VehicleAnalysis.Analyze(ExampleDesigns.Colossus()));
        var rng = new Random(3);
        BlockInstance[] alive = g.Instances.ToArray();
        var sw = new Stopwatch();
        int quick = 0;
        for (int shot = 0; shot < 40; shot++)
        {
            BlockInstance at = alive[rng.Next(alive.Length)];
            if (!at.Alive) continue;
            var removed = new List<Int3>();
            foreach (Int3 c in at.Cells) removed.Add(c);
            foreach (Int3 d in Int3.Directions)
                if (g.Blocks.TryGetValue(at.Cell + d, out BlockInstance nb) && nb.Alive)
                {
                    removed.AddRange(nb.Cells);
                    g.Remove(nb);
                }

            g.Remove(at);
            sw.Start();
            List<List<BlockInstance>> near = Connectivity.FindDetachedNear(g, KeepRule.DriverSeat, removed);
            sw.Stop();
            if (near != null)
            {
                quick++;
                foreach (List<BlockInstance> p in near)
                    foreach (BlockInstance b in p) g.Remove(b);
            }
        }

        Assert.True(quick >= 36, $"only {quick} of 40 answered without the full check");
        // The full check takes over 100 ms here; forty quick ones should take a few milliseconds in all.
        Assert.True(sw.Elapsed.TotalMilliseconds < 200, $"{sw.Elapsed.TotalMilliseconds:0} ms for 40 holes");
    }
}

/// <summary>The running totals a grid keeps match adding it all up again.</summary>
public class GridTotalsTests
{
    [Fact]
    public void TotalsFollowTheBlocks()
    {
        foreach (Blueprint bp in ExampleDesigns.All.Take(14))
        {
            GridModel g = GridModel.FromAnalysis(VehicleAnalysis.Analyze(bp));
            var rng = new Random(5);
            for (int step = 0; step < 30 && g.InstanceCount > 0; step++)
            {
                BlockInstance[] all = g.Instances.ToArray();
                g.Remove(all[rng.Next(all.Length)]);
                float mass = all.Where(b => b.Alive).Sum(b => b.Def.MassKg);
                Assert.Equal(mass, g.TotalMassKg(), mass * 1e-4f + 0.01f);
                Assert.Equal(all.Where(b => b.Alive).Sum(b => b.Def.PowerKw), g.PowerKw, 0.5f);
                Assert.Equal(all.Where(b => b.Alive).Sum(b => b.Def.FuelL), g.FuelL, 0.5f);
                Assert.Equal(all.Count(b => b.Alive && b.Def.Kind == BlockKind.DriverSeat), g.CountOf(BlockKind.DriverSeat));
                if (mass > 0)
                {
                    System.Numerics.Vector3 com = all.Where(b => b.Alive).Aggregate(System.Numerics.Vector3.Zero, (s, b) => s + b.Center * b.Def.MassKg) / mass * Blueprint.CellSize;
                    Assert.True((com - g.CenterOfMass()).Length() < 0.01f, $"{bp.Name}: {com} vs {g.CenterOfMass()}");
                }
            }
        }
    }
}
