using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DreamGame.Core.Tests;

public class JetDesignTests
{
    private static BlockDef D(string id) => BlockCatalog.Get(id);

    private static List<BlockDef> Many(string id, int n) => Enumerable.Repeat(D(id), n).ToList();

    /// <summary>A jet: <paramref name="compressors"/> stages and an intake in front, turbines, afterburners and a nozzle behind.</summary>
    private static JetStats Jet(int compressors, int turbines, int afterburners = 0, bool fan = false, bool ramp = false, bool variable = false)
    {
        var front = Many("jet_compressor", compressors);
        if (fan) front.Add(D("jet_fan"));
        front.Add(D(ramp ? "jet_intake_ramp" : "jet_intake"));
        var back = Many("jet_turbine", turbines).Concat(Many("jet_afterburner", afterburners)).ToList();
        back.Add(D(variable ? "jet_nozzle_variable" : "jet_nozzle"));
        return JetDesign.Compute(D("jet_core"), front, back, out _, out _);
    }

    [Fact]
    public void ACompleteLineRuns()
    {
        var front = new List<BlockDef> { D("jet_compressor"), D("jet_compressor"), D("jet_intake") };
        var back = new List<BlockDef> { D("jet_turbine"), D("jet_nozzle") };
        JetStats s = JetDesign.Compute(D("jet_core"), front, back, out string problem, out bool runs);
        Assert.True(runs);
        Assert.Null(problem);
        Assert.Equal(6, s.Length);
        Assert.Equal(front.Concat(back).Sum(d => d.MassKg) + D("jet_core").MassKg, s.MassKg, 1);
        Assert.False(s.HasAfterburner);
    }

    [Fact]
    public void WithoutAnIntakeOrANozzleItDoesNothing()
    {
        JetDesign.Compute(D("jet_core"), new List<BlockDef> { D("jet_compressor") }, new List<BlockDef> { D("jet_turbine"), D("jet_nozzle") },
            out string p1, out bool r1);
        Assert.False(r1);
        Assert.Contains("intake", p1);
        JetStats s = JetDesign.Compute(D("jet_core"), new List<BlockDef> { D("jet_intake") }, new List<BlockDef> { D("jet_turbine") },
            out string p2, out bool r2);
        Assert.False(r2);
        Assert.Contains("nozzle", p2);
        Assert.Equal(0, s.Thrust(1, 0, true));
    }

    [Fact]
    public void PartsInTheWrongOrderAreFlagged()
    {
        // The intake behind a compressor is fine; a compressor in front of the intake isn't.
        JetDesign.Compute(D("jet_core"), new List<BlockDef> { D("jet_intake"), D("jet_compressor") }, new List<BlockDef> { D("jet_turbine"), D("jet_nozzle") },
            out string problem, out _);
        Assert.Contains("wrong place", problem);
    }

    [Fact]
    public void CompressorsAddPushAndSaveFuelEachALittleLessThanTheLast()
    {
        float Thrust(int n) => Jet(n, (n + 1) / 2).Thrust(1, 0, false);
        float PerKn(int n) => Jet(n, (n + 1) / 2) is var s ? s.FuelLps / (s.DryThrustN / 1000f) : 0;
        Assert.True(Thrust(2) > Thrust(1) && Thrust(4) > Thrust(2) && Thrust(8) > Thrust(4));
        Assert.True(Thrust(2) - Thrust(1) > Thrust(8) - Thrust(7));
        Assert.True(PerKn(8) < PerKn(4) && PerKn(4) < PerKn(1));
    }

    [Fact]
    public void TooFewTurbinesMakeAWeakEngine()
    {
        JetStats ok = Jet(4, 2), starved = Jet(4, 1);
        Assert.True(starved.DryThrustN < ok.DryThrustN * 0.85f);
        JetDesign.Compute(D("jet_core"), Many("jet_compressor", 4).Append(D("jet_intake")).ToList(),
            new List<BlockDef> { D("jet_turbine"), D("jet_nozzle") }, out string problem, out bool runs);
        Assert.True(runs);
        Assert.Contains("turbines", problem);
        // A spare turbine costs a little.
        Assert.True(Jet(4, 3).DryThrustN < ok.DryThrustN);
    }

    [Fact]
    public void AFanPushesHarderWhenSlowButFadesWithSpeed()
    {
        JetStats jet = Jet(4, 2), fan = Jet(4, 3, fan: true);
        Assert.True(fan.Thrust(1, 0, false) > jet.Thrust(1, 0, false) * 1.3f);
        Assert.True(fan.Thrust(1, 300, false) < jet.Thrust(1, 300, false));
        Assert.True(fan.FuelLps / fan.DryThrustN < jet.FuelLps / jet.DryThrustN);
    }

    [Fact]
    public void APlainIntakeChokesPastTheSpeedOfSoundARampDoesNot()
    {
        JetStats plain = Jet(4, 2, variable: true), ramp = Jet(4, 2, ramp: true, variable: true);
        Assert.True(plain.Thrust(1, 200, false) > plain.Thrust(1, 0, false));
        Assert.True(ramp.Thrust(1, 500, false) > plain.Thrust(1, 500, false) * 2f);
        Assert.Equal(1f, JetStats.ReadyMade.SpeedFactor(500));
    }

    [Fact]
    public void AfterburnersNeedAVariableNozzleToGiveTheirAll()
    {
        JetStats none = Jet(4, 2), fixedOne = Jet(4, 2, 1), varOne = Jet(4, 2, 1, variable: true), varTwo = Jet(4, 2, 2, variable: true);
        Assert.False(none.HasAfterburner);
        Assert.Equal(1.55f, varOne.AfterburnerMult, 2);
        Assert.Equal(1.275f, fixedOne.AfterburnerMult, 2);
        Assert.True(varTwo.AfterburnerMult - varOne.AfterburnerMult < varOne.AfterburnerMult - 1f);
        Assert.True(varTwo.FuelPerSecond(1, true) > varOne.FuelPerSecond(1, true));
        Assert.Equal(varOne.FuelLps, varOne.FuelPerSecond(1, false), 3);
    }

    [Fact]
    public void AWellBuiltJetBeatsTheReadyMadeOne()
    {
        // A plain four-stage jet: far more push per tonne, and less fuel per kN.
        JetStats plain = Jet(4, 2), stock = JetStats.ReadyMade;
        Assert.True(plain.DryThrustN / plain.MassKg > 1.4f * stock.DryThrustN / stock.MassKg);
        Assert.True(plain.FuelLps / plain.DryThrustN < stock.FuelLps / stock.DryThrustN);
        // With two afterburner sections and a variable nozzle: more afterburner push per tonne too.
        JetStats hot = Jet(4, 2, 2, ramp: true, variable: true);
        Assert.True(hot.Thrust(1, 0, true) / hot.MassKg > stock.Thrust(1, 0, true) / stock.MassKg);
    }

    [Fact]
    public void TheAnalysisFindsABuiltJetAndCountsItAsAJetEngine()
    {
        Int3 f = Orientations.Forward(0);
        var cells = new List<BlueprintBlock>
        {
            new("jet_core", new Int3(0, 0, 0), 0),
            new("jet_compressor", f, 0),
            new("jet_compressor", f * 2, 0),
            new("jet_intake", f * 3, 0),
            new("jet_turbine", -f, 0),
            new("jet_nozzle", -f * 2, 0),
            new("fuel_tank", new Int3(0, 1, 0), 0),
        };
        VehicleAnalysis a = VehicleAnalysis.Analyze(Blueprint.FromCellMap("jet", cells));
        BuiltJet j = Assert.Single(a.BuiltJets);
        Assert.True(j.Runs);
        Assert.Equal(3, j.Front.Count);
        Assert.Equal(2, j.Back.Count);
        ThrusterInfo t = Assert.Single(a.Thrusters);
        Assert.Equal(BlockKind.JetEngine, t.Kind);
        Assert.Equal(-f * 2, t.FlameCell);
        Assert.Equal(f, t.Direction);

        // Turn the intake round: it no longer counts, and the analysis says why.
        cells[3] = new BlueprintBlock("jet_intake", f * 3, Enumerable.Range(0, 24).First(o => Orientations.Forward(o) != f));
        VehicleAnalysis b = VehicleAnalysis.Analyze(Blueprint.FromCellMap("jet", cells));
        Assert.False(b.BuiltJets[0].Runs);
        Assert.Contains(b.Warnings, w => w.Text.Contains("faces a different way"));
    }
}
