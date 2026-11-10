using System.Linq;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class FittingsTests
{
    [Fact]
    public void NoDetailPartIsJustForLooks()
    {
        foreach (BlockDef d in BlockCatalog.All.Where(d => d.Kind == BlockKind.Detail))
            Assert.DoesNotContain("for looks", d.Description.ToLowerInvariant());
    }

    [Fact]
    public void FittingsAddUpToALimit()
    {
        Assert.Equal(1f, Fittings.SightFactor(0));
        Assert.True(Fittings.SightFactor(2) > Fittings.SightFactor(1));
        Assert.Equal(Fittings.SightFactor(2), Fittings.SightFactor(5));
        Assert.Equal(Sensors.CommandRangeM + 3 * Fittings.RangePerAntenna, Fittings.CommandRange(9));
        Assert.Equal(0f, Fittings.RepairHpPerS(0));
        Assert.True(float.IsPositiveInfinity(Fittings.RefitEveryS(0)));
        Assert.True(Fittings.RefitEveryS(2) < Fittings.RefitEveryS(1));
        Assert.True(Fittings.FireScale(3) < Fittings.FireScale(0));
    }

    [Fact]
    public void TheWardensKitIsCounted()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Warden());
        Assert.Equal(2, a.Kit.Hatches);
        Assert.Equal(1, a.Kit.Periscopes);
        Assert.Equal(2, a.Kit.SmokeLaunchers);
        Assert.Equal(4, a.Kit.Toolboxes);
        Assert.Equal(2, a.Kit.SpareTracks);
        Assert.Equal(2, a.Kit.Exhausts);
    }

    [Fact]
    public void VentsShortenFires()
    {
        var plain = new FireSim();
        var vented = new FireSim { BurnScale = Fittings.FireScale(3) };
        BlockDef fuel = BlockCatalog.Get("fuel_tank");
        plain.Ignite(new Int3(0, 0, 0), fuel);
        vented.Ignite(new Int3(0, 0, 0), fuel);
        var dmg = new System.Collections.Generic.List<(Int3, float)>();
        var lit = new System.Collections.Generic.List<Int3>();
        (BlockDef, Int3)? At(Int3 c) => c == new Int3(0, 0, 0) ? (fuel, c) : null;
        for (int i = 0; i < (int)(Fire.BurnS * Fittings.FireScale(3) / 0.1f) + 2; i++)
        {
            plain.Step(0.1f, At, _ => false, false, dmg, lit);
            vented.Step(0.1f, At, _ => false, false, dmg, lit);
        }

        Assert.Equal(1, plain.Count);
        Assert.Equal(0, vented.Count);
    }
}
