using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace DreamGame.Core.Tests;

/// <summary>Aircraft across the eras: the kit for each era's look and the real-scale examples built with it.</summary>
public class AircraftErasTests
{
    private static VehicleAnalysis A(Blueprint bp) => VehicleAnalysis.Analyze(bp);

    private static Blueprint Swap(Blueprint bp, Func<BlueprintBlock, BlueprintBlock> swap) =>
        Blueprint.FromCellMap(bp.Name + " (changed)", bp.ToCellMap().Values.Select(swap).ToList());

    private static readonly Func<Blueprint>[] Eras =
    {
        ExampleDesigns.Shrike, ExampleDesigns.Swallow, ExampleDesigns.Tern, ExampleDesigns.Nightjar, ExampleDesigns.Halcyon, ExampleDesigns.Seraph,
    };

    [Fact]
    public void EveryEraExampleIsAFullSizePlaneThatFliesCleanly()
    {
        foreach (Func<Blueprint> make in Eras)
        {
            Blueprint bp = make();
            VehicleAnalysis a = A(bp);
            Assert.Equal(MobilityMode.Plane, a.Mode);
            Assert.Empty(a.Disconnected);
            Assert.DoesNotContain(a.Warnings, w => w.Severity != Severity.Info);
            Assert.True(a.Air.CanFly, bp.Name);
            Assert.True(a.Air.MaxPitchDeg > 9f, $"{bp.Name}: pitch {a.Air.MaxPitchDeg:0}°");
            Assert.True(Aero.TakeoffRunM(a.Air.StallSpeedMs, a.Air.StaticThrustN, a.MassKg) < Aero.LongTakeoffRunM, bp.Name);
            Assert.True(a.Air.HasRoll && a.Air.HasPitch && a.Air.HasYaw, bp.Name);
            Assert.All(a.Guns, g => Assert.True(g.Valid, $"{bp.Name}: a gun isn't ready"));
            Assert.All(a.Missiles, m => Assert.True(m.Valid, $"{bp.Name}: {m.Problem}"));
            // Real fighter sizes: 10 to 18 m long, 8 to 16 m across.
            float length = (a.Max.Z - a.Min.Z + 1) * Blueprint.CellSize, span = (a.Max.X - a.Min.X + 1) * Blueprint.CellSize;
            Assert.InRange(length, 9.5f, 18f);
            Assert.InRange(span, 8f, 16f);
        }
    }

    [Fact]
    public void TheErasGetFasterFromPropellerToJet()
    {
        float Top(Blueprint bp) => A(bp).Air.TopSpeedMs;
        float prop = Top(ExampleDesigns.Shrike()), early = Top(ExampleDesigns.Swallow()), delta = Top(ExampleDesigns.Tern());
        Assert.True(prop < early && early < delta, $"{prop * 3.6f:0} < {early * 3.6f:0} < {delta * 3.6f:0} km/h");
        Assert.True(A(ExampleDesigns.Nightjar()).Air.TopSpeedMs > delta);
    }

    [Fact]
    public void ThePropellerFighterSitsOnItsTailWheel()
    {
        VehicleAnalysis a = A(ExampleDesigns.Shrike());
        ThrusterInfo prop = a.Thrusters.Single();
        Assert.Equal(1.7f, prop.BladeRadiusM);
        Assert.Equal(1600f, a.PowerKw);
        WheelInfo tail = a.Wheels.Single(w => a.Defs[w.Cell].Id == "landing_gear_tail");
        // Main wheels ahead of the center of mass, the tail wheel behind it and a row higher: it sits nose up.
        Assert.True(tail.Cell.Z * Blueprint.CellSize > a.CenterOfMass.Z);
        Assert.All(a.Wheels.Where(w => w != tail), w => Assert.True(w.Cell.Z * Blueprint.CellSize < a.CenterOfMass.Z));
        Assert.All(a.Wheels.Where(w => w != tail), w => Assert.True(w.Cell.Y < tail.Cell.Y));
        Assert.True(a.Wheels.All(w => a.Defs[w.Cell].Retracts));
    }

    [Fact]
    public void ABiggerPropellerPushesHarderOnTheSamePower()
    {
        Blueprint big = ExampleDesigns.Shrike();
        Blueprint small = Swap(big, b => b.Id == "air_propeller_large" ? new BlueprintBlock("air_propeller", b.Cell, b.O) : b);
        Assert.True(A(big).Air.StaticThrustN > A(small).Air.StaticThrustN * 1.15f);
    }

    [Fact]
    public void TheEarlyJetHasOneSimpleJetAndTheDeltaAnAfterburner()
    {
        BuiltJet early = A(ExampleDesigns.Swallow()).BuiltJets.Single();
        Assert.True(early.Runs);
        Assert.False(early.Stats.HasAfterburner);
        Assert.False(early.Stats.RampIntake);
        BuiltJet delta = A(ExampleDesigns.Tern()).BuiltJets.Single();
        Assert.True(delta.Runs && delta.Stats.HasAfterburner && delta.Stats.RampIntake && delta.Stats.VariableNozzle);
    }

    [Fact]
    public void ATaillessDeltaPitchesWithElevonsAndAFlyingWingTurnsWithThem()
    {
        VehicleAnalysis delta = A(ExampleDesigns.Tern());
        Assert.Contains(delta.Surfaces, s => s.Role == SurfaceRole.Elevon);
        Assert.DoesNotContain(delta.Surfaces, s => s.Role == SurfaceRole.Elevator);
        // A plane with a tailplane keeps its elevators and ailerons.
        Assert.DoesNotContain(A(ExampleDesigns.Starling()).Surfaces, s => s.Role == SurfaceRole.Elevon);
        Assert.DoesNotContain(A(ExampleDesigns.Shrike()).Surfaces, s => s.Role == SurfaceRole.Elevon);

        VehicleAnalysis wing = A(ExampleDesigns.Seraph());
        Assert.DoesNotContain(wing.Surfaces, s => s.Role == SurfaceRole.Rudder);
        Assert.True(wing.Air.HasYaw);
    }

    [Fact]
    public void CanardsAheadOfTheCenterOfMassPitchTheForwardSweptFighter()
    {
        VehicleAnalysis a = A(ExampleDesigns.Halcyon());
        var canards = a.Surfaces.Where(s => s.Role == SurfaceRole.Elevator && s.Cell.Z * Blueprint.CellSize < a.CenterOfMass.Z - 2f).ToList();
        Assert.True(canards.Count >= 6);
        // Forward sweep: the wingtip's leading edge is well ahead of the root's.
        var wing = a.Surfaces.Where(s => !s.Control && s.Cell.X >= 4 && Math.Abs(Orientations.Up(s.Orientation).Y) == 1 && s.Cell.Z > -8).ToList();
        int tipX = wing.Max(s => s.Cell.X);
        Assert.True(wing.Where(s => s.Cell.X == tipX).Min(s => s.Cell.Z) <= wing.Where(s => s.Cell.X == 4).Min(s => s.Cell.Z) - 3);
        Assert.Contains(a.Guns, g => g.Valid && g.BreechDef.Breech.Beam);
    }

    [Fact]
    public void SwivellingNozzlesHelpThePlanePointItsNose()
    {
        Blueprint vectoring = ExampleDesigns.Halcyon();
        Blueprint plain = Swap(vectoring, b => b.Id == "jet_nozzle_vectoring" ? new BlueprintBlock("jet_nozzle_large", b.Cell, b.O) : b);
        VehicleAnalysis v = A(vectoring), p = A(plain);
        Assert.True(v.Air.VectorPitchNm > 0 && v.Air.VectorYaw);
        Assert.Equal(0f, p.Air.VectorPitchNm);
        Assert.True(v.Air.MaxPitchDeg > p.Air.MaxPitchDeg);
        Assert.All(v.BuiltJets, j => Assert.Equal(20f, j.Stats.VectorDeg));
        // The flat nozzle only swings up and down.
        Assert.All(A(ExampleDesigns.Nightjar()).BuiltJets, j => Assert.False(j.Stats.VectorYaw));
    }

    [Fact]
    public void AStealthFighterIsSpottedFromAThirdOfTheRangeUntilItHangsMissilesOutside()
    {
        Blueprint bp = ExampleDesigns.Nightjar();
        VehicleAnalysis a = A(bp);
        Assert.Equal(0, Stealth.ExposedStores(a));
        Assert.InRange(a.Signature, Stealth.Best, 0.45f);
        Assert.Equal(1f, A(ExampleDesigns.Starling()).Signature);

        // The same jet in plain airframe isn't stealthy.
        VehicleAnalysis plain = A(Swap(bp, b => b.Id.StartsWith("airframe_stealth") ? new BlueprintBlock(b.Id.Replace("airframe_stealth", "airframe"), b.Cell, b.O) : b));
        Assert.True(plain.Signature > a.Signature + 0.3f);

        // Missiles hung under the wing give some of it back.
        var cells = bp.ToCellMap().Values.ToList();
        foreach (int x in new[] { -8, 8 })
        {
            cells.Add(new BlueprintBlock("missile_rail", new Int3(x, 1, 5)));
            cells.Add(new BlueprintBlock("missile_motor", new Int3(x, 1, 4)));
            cells.Add(new BlueprintBlock("missile_warhead", new Int3(x, 1, 3)));
        }

        VehicleAnalysis loaded = A(Blueprint.FromCellMap("loaded", cells));
        Assert.Equal(2, Stealth.ExposedStores(loaded));
        Assert.Equal(a.Signature + 2 * Stealth.StorePenalty, loaded.Signature, 3);

        // Close in, anything is seen; far off, a stealthy aircraft only at a third of the range.
        Assert.Equal(Stealth.CloseM, Stealth.SpotRangeM(2000f, Stealth.Best));
        Assert.Equal(5400f * Stealth.Best, Stealth.SpotRangeM(5400f, 0.1f), 2);
        Assert.Equal(5400f * 0.4f, Stealth.SpotRangeM(5400f, 0.4f), 2);
        Assert.Equal(500f, Stealth.SpotRangeM(500f, 0.33f));
    }

    [Fact]
    public void WeaponsBayDoorsCloseUnderTheMissiles()
    {
        foreach (Blueprint bp in new[] { ExampleDesigns.Nightjar(), ExampleDesigns.Halcyon(), ExampleDesigns.Seraph() })
        {
            VehicleAnalysis a = A(bp);
            foreach (MissileInfo m in a.Missiles)
                Assert.All(m.Parts.Append(m.Rail), c => Assert.Equal("bay_door", a.Defs[c + new Int3(0, -1, 0)].Id));
            Assert.Equal(0, Stealth.ExposedStores(a));
        }

        Assert.Contains(BlockModels.For(BlockCatalog.Get("bay_door")).Nodes, n => n.Name == "Door");
    }

    [Fact]
    public void TheLongEdgePiecesMakeOnePanelBetweenThemAndTheRoundTipIsAQuarterCircle()
    {
        Assert.Equal(1f, BlockCatalog.Get("wing_taper").LiftArea + BlockCatalog.Get("wing_taper_tip").LiftArea, 3);
        Assert.Equal(MathF.PI / 4f, BlockCatalog.Get("wing_round").LiftArea, 2);
        foreach (string id in new[] { "wing_round", "wing_taper", "wing_taper_tip" })
        {
            Assert.Equal(BlockKind.Wing, BlockCatalog.Get(id).Kind);
            Assert.Equal(1, BlockCatalog.Get(id).MirrorAxis);
        }

        // A run of long edges along a delta's leading edge: 63° (two cells back for every one out).
        VehicleAnalysis tern = A(ExampleDesigns.Tern());
        // (Outboard of the intake trunks, which cover the first pieces.)
        var edge = tern.Surfaces.Where(s => tern.Defs[s.Cell].Id == "wing_taper_tip" && s.Cell.X >= 4).OrderBy(s => s.Cell.X).ToList();
        Assert.True(edge.Count >= 4);
        for (int i = 1; i < edge.Count; i++) Assert.Equal(2, edge[i].Cell.Z - edge[i - 1].Cell.Z);
    }

    [Fact]
    public void AirRunsNoseToTailAcrossEveryFlatOrUprightPanelHoweverItsTurned()
    {
        for (int o = 0; o < Orientations.Count; o++)
        {
            Int3 up = Orientations.Up(o);
            if (up.Z == 0) Assert.Equal(new Int3(0, 0, -1), Aero.ChordOf(o));
            else Assert.Equal(Orientations.Forward(o), Aero.ChordOf(o));
        }
    }

    [Fact]
    public void ThePlanformHelperSweepsEdgesTheWayItSays()
    {
        // Leading edge of the stealth fighter's wing: one cell back for every cell out (45°); trailing edge one cell
        // forward for every two out (27°).
        VehicleAnalysis a = A(ExampleDesigns.Nightjar());
        var wing = a.Surfaces.Where(s => s.Cell.X >= 4 && s.Cell.Y == 2 && s.Cell.Z >= -4 && s.Cell.Z <= 10 && !a.Defs[s.Cell].Id.StartsWith("control")).ToList();
        int Lead(int x) => wing.Where(s => s.Cell.X == x).Min(s => s.Cell.Z);
        for (int x = 5; x <= 11; x++) Assert.Equal(1, Lead(x) - Lead(x - 1));
    }

    [Fact]
    public void TheNewPartsHaveModelsAndMovingNodes()
    {
        foreach (string id in new[] { "air_propeller_large", "aero_engine_radial", "canopy_framed", "landing_gear_tail", "wing_round", "wing_taper",
                     "wing_taper_tip", "jet_nozzle_vectoring", "jet_nozzle_2d", "bay_door", "exhaust_stacks", "formation_light", "airframe_stealth" })
            Assert.NotEmpty(BlockModels.For(BlockCatalog.Get(id)).Parts);

        Assert.Contains(BlockModels.For(BlockCatalog.Get("air_propeller_large")).Nodes, n => n.Name == "Spinner");
        foreach (string id in new[] { "jet_nozzle_vectoring", "jet_nozzle_2d" })
        {
            BlockModel m = BlockModels.For(BlockCatalog.Get(id));
            Assert.Contains(m.Nodes, n => n.Name == "Petals");
            Assert.Contains(m.Nodes, n => n.Name == "Flame");
        }

        Assert.Contains(BlockModels.For(BlockCatalog.Get("formation_light")).Parts, p => p.Paint == Paint.Glow);
        Assert.True(BlockCatalog.Get("airframe_stealth_slope").Stealthy);
        Assert.Equal(1500f, BlockCatalog.Get("aero_engine_radial").PowerKw);
    }

    [Fact]
    public void CountershadedPaintIsPaleUnderneathAndBareMetalShines()
    {
        Livery l = Liveries.Get("olive_grey");
        Assert.Equal(LiveryPattern.Countershade, l.Pattern);
        var p = new Vector3(1.3f, 0.4f, -2f);
        Assert.Equal(l.Third, Liveries.ColorAt(l, p, new Vector3(0, -1, 0)));
        Assert.NotEqual(l.Third, Liveries.ColorAt(l, p, new Vector3(0, 1, 0)));
        Assert.NotEqual(l.Third, Liveries.ColorAt(l, p, new Vector3(1, 0, 0)));
        // Other paint jobs don't care which way a surface faces.
        Livery grey = Liveries.Get("lowvis");
        Assert.Equal(Liveries.ColorAt(grey, p), Liveries.ColorAt(grey, p, new Vector3(0, -1, 0)));

        Assert.True(Liveries.Get("natural_metal").IsBareMetal);
        Assert.False(Liveries.Get("olive_grey").IsBareMetal);
        foreach (string id in new[] { "temperate", "grey_mottle", "sea_camo", "prototype" }) Assert.Equal(id, Liveries.Get(id).Id);
    }
}
