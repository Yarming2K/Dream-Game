using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace DreamGame.Core.Tests;

/// <summary>The kit for building things that look their era: modern fighters and air-defence vehicles.</summary>
public class EraLooksTests
{
    private static VehicleAnalysis A(Blueprint bp) => VehicleAnalysis.Analyze(bp);

    private static Blueprint Swap(Blueprint bp, string name, Func<BlueprintBlock, BlueprintBlock> swap) =>
        Blueprint.FromCellMap(name, bp.ToCellMap().Values.Select(swap).ToList());

    [Fact]
    public void ASweptEdgeLiftsHalfAsMuchAsAFullPanel()
    {
        VehicleAnalysis a = A(ExampleDesigns.Starling());
        int full = a.Surfaces.Count(s => a.Defs[s.Cell].Id == "wing" && Math.Abs(Orientations.Up(s.Orientation).Y) == 1);
        int swept = a.Surfaces.Count(s => a.Defs[s.Cell].Id == "wing_swept" && Math.Abs(Orientations.Up(s.Orientation).Y) == 1);
        int controls = a.Surfaces.Count(s => s.Control && Math.Abs(Orientations.Up(s.Orientation).Y) == 1);
        Assert.True(swept > 20);
        Assert.Equal((full + controls + swept * 0.5f) * Aero.PanelArea, a.Air.WingAreaM2, 3);
    }

    [Fact]
    public void MirroredBuildingFlipsASweptEdgeTheRightWay()
    {
        BlockDef edge = BlockCatalog.Get("wing_swept");
        var cut = new Int3(-1, 0, -1);
        for (int o = 0; o < Orientations.Count; o++)
        {
            Int3 c = Orientations.Apply(o, cut), m = Orientations.Apply(Orientations.MirrorOf(edge, o), cut);
            Assert.Equal(new Int3(-c.X, c.Y, c.Z), m);
            Assert.Equal(Math.Abs(Orientations.Up(o).Y), Math.Abs(Orientations.Up(Orientations.MirrorOf(edge, o)).Y));
        }

        // Ordinary blocks mirror as before.
        Assert.Equal(Orientations.MirrorX(5), Orientations.MirrorOf(BlockCatalog.Get("wing"), 5));
    }

    [Fact]
    public void ANoseConeCutsDragAndLandingGearFoldsAwayInFlight()
    {
        Blueprint starling = ExampleDesigns.Starling();
        Blueprint flatNose = Swap(starling, "flat", b => b.Id == "radome" ? new BlueprintBlock("radar", b.Cell, 0) : b);
        Blueprint fixedGear = Swap(starling, "fixed", b => b.Id switch
        {
            "landing_gear" => new BlueprintBlock("wheel", b.Cell, b.O),
            "landing_gear_steer" => new BlueprintBlock("wheel_steer", b.Cell, b.O),
            _ => b,
        });
        VehicleAnalysis a = A(starling);
        Assert.True(a.Air.Streamlining > A(flatNose).Air.Streamlining + 0.01f);
        Assert.True(a.Air.Streamlining > A(fixedGear).Air.Streamlining);
        Assert.Equal(0.15f, BlockCatalog.Get("nose_cone").DragFace);
    }

    [Fact]
    public void StarlingIsAFullSizeFighterThatFlies()
    {
        VehicleAnalysis a = A(ExampleDesigns.Starling());
        Assert.Equal(MobilityMode.Plane, a.Mode);
        Assert.Empty(a.Disconnected);
        Assert.DoesNotContain(a.Warnings, w => w.Severity != Severity.Info);
        Assert.True(a.Air.CanFly);
        // Real size: about 16 m long with a 12.5 m span.
        Assert.InRange((a.Max.Z - a.Min.Z + 1) * Blueprint.CellSize, 15f, 17f);
        Assert.InRange((a.Max.X - a.Min.X + 1) * Blueprint.CellSize, 11f, 14f);
        Assert.True(a.Air.MaxPitchDeg > 15f, $"pitch {a.Air.MaxPitchDeg:0}°");
        Assert.True(a.Air.TopSpeedMs * 3.6f > 900f, $"top {a.Air.TopSpeedMs * 3.6f:0} km/h");
        Assert.True(Aero.TakeoffRunM(a.Air.StallSpeedMs, a.Air.StaticThrustN, a.MassKg) < Aero.LongTakeoffRunM);
        Assert.Equal(2, a.BuiltJets.Count(j => j.Runs && j.Stats.VariableNozzle && j.Stats.RampIntake && !j.IntakeBlocked));
        Assert.Equal(4, a.Missiles.Count(m => m.Valid && m.Stats.Seeker == SeekerKind.Radar));
        Assert.Equal(3, a.Wheels.Count(w => a.Defs[w.Cell].Retracts));
        Assert.Contains(a.Anchors, c => a.Defs[c].Kind == BlockKind.Radar);
        // Nose wheel ahead of the center of mass, main wheels behind it.
        Assert.True(a.Wheels.Min(w => w.Cell.Z) * Blueprint.CellSize < a.CenterOfMass.Z);
        Assert.True(a.Wheels.Max(w => w.Cell.Z) * Blueprint.CellSize > a.CenterOfMass.Z);
    }

    [Fact]
    public void AShortRunIsNoLongerAWarningForAHeavyJet()
    {
        Assert.True(Aero.TakeoffRunM(54f, 86000f, 9600f) < 300f);
        Assert.True(Aero.TakeoffRunM(54f, 9000f, 9600f) > Aero.LongTakeoffRunM);
        Assert.True(float.IsPositiveInfinity(Aero.TakeoffRunM(54f, 0f, 9600f)));
    }

    [Fact]
    public void ABoxLauncherHoldsSixMissilesRaisedOverTheBack()
    {
        VehicleAnalysis a = A(ExampleDesigns.PalisadeLauncher());
        Assert.Equal(0, a.ErrorCount);
        Assert.Empty(a.Disconnected);
        MissileInfo m = a.Missiles.Single();
        Assert.True(m.Valid, m.Problem);
        Assert.Equal(VlsLoad.Sam, m.Vls);
        BlockDef def = a.Defs[m.Rail];
        Assert.Equal(6, def.AmmoUnits);

        // The launcher points over the back: its missiles leave backward and 30° up.
        Vector3 axis = BoxLauncher.AxisOnVehicle(def, a.Cells[m.Rail].O);
        Assert.True(axis.Z > 0.8f && axis.Y > 0.45f && axis.Y < 0.55f, $"{axis}");
        // Every canister mouth is inside the block's box, near the top at its far end, and they don't overlap.
        (Int3 lo, Int3 hi) = Footprint.LocalRange(def.Size);
        Vector3 half = new Vector3(def.Size.X, def.Size.Y, def.Size.Z) * Blueprint.CellSize * 0.5f;
        var mouths = Enumerable.Range(0, BoxLauncher.Canisters).Select(i => BoxLauncher.Mouth(def, i)).ToList();
        foreach (Vector3 p in mouths)
        {
            Assert.InRange(p.X, -half.X, half.X);
            Assert.InRange(p.Y, 0f, half.Y);
            Assert.InRange(p.Z, -half.Z, 0f);
        }

        for (int i = 0; i < mouths.Count; i++)
        for (int j = i + 1; j < mouths.Count; j++)
            Assert.True(Vector3.Distance(mouths[i], mouths[j]) >= BoxLauncher.PitchM * 0.99f);
        // The top row goes first.
        Assert.Equal(5, BoxLauncher.Next(6));
        Assert.Equal(0, BoxLauncher.Next(1));
    }

    [Fact]
    public void ABoxLauncherWithSomethingInFrontOfItsMouthsCantFire()
    {
        Blueprint bp = ExampleDesigns.PalisadeLauncher();
        BlueprintBlock launcher = bp.Blocks.Single(b => b.Id == "sam_launcher");
        // A plate just behind the truck, level with the top of the raised box.
        var cells = bp.ToCellMap().Values.ToList();
        cells.Add(new BlueprintBlock("armor_light", launcher.Cell + new Int3(0, 2, 4)));
        VehicleAnalysis a = A(Blueprint.FromCellMap("blocked", cells));
        MissileInfo m = a.Missiles.Single();
        Assert.False(m.Valid);
        Assert.Contains("in front of its canister mouths", m.Problem);
    }

    [Fact]
    public void ThePalisadeRadarTruckSeesFarAndCommands()
    {
        VehicleAnalysis a = A(ExampleDesigns.PalisadeRadar());
        Assert.Equal(0, a.ErrorCount);
        Assert.Empty(a.Disconnected);
        Assert.Equal(MobilityMode.Ground, a.Mode);
        Assert.Equal(2, a.Anchors.Count(c => a.Defs[c].Kind == BlockKind.Radar));
        Assert.Contains(a.Anchors, c => a.Defs[c].Kind == BlockKind.CommandConsole);
        Assert.Equal(8, a.Wheels.Count(w => !w.Blocked));
    }

    [Fact]
    public void OnlyTurningRadarsSpin()
    {
        Assert.True(BlockModels.Turns(BlockCatalog.Get("radar")));
        Assert.True(BlockModels.Turns(BlockCatalog.Get("radar_rotating")));
        Assert.False(BlockModels.Turns(BlockCatalog.Get("radar_array")));
        Assert.False(BlockModels.Turns(BlockCatalog.Get("radome")));
        Assert.False(BlockModels.Turns(BlockCatalog.Get("wing")));
    }

    [Fact]
    public void TheNewPartsHaveModelsWithTheirMovingNodes()
    {
        foreach (string id in new[] { "wing_swept", "nose_cone", "radome", "canopy_bubble", "landing_gear", "landing_gear_steer",
                     "radar_array", "radar_rotating", "sam_launcher", "wheel_heavy", "wheel_heavy_steer", "jet_nozzle_large" })
            Assert.NotEmpty(BlockModels.For(BlockCatalog.Get(id)).Parts);

        Assert.Contains(BlockModels.For(BlockCatalog.Get("landing_gear")).Nodes, n => n.Name == "Strut");
        BlockModel launcher = BlockModels.For(BlockCatalog.Get("sam_launcher"));
        for (int i = 0; i < BoxLauncher.Canisters; i++) Assert.Contains(launcher.Nodes, n => n.Name == $"Cap{i}");
        Assert.Contains(BlockModels.For(BlockCatalog.Get("jet_nozzle_large")).Nodes, n => n.Name == "Flame");
    }

    [Fact]
    public void ModernPaintJobsAreThere()
    {
        foreach (string id in new[] { "lowvis", "stealth", "tan", "forest_digital", "desert_digital" })
            Assert.Equal(id, Liveries.Get(id).Id);
        Assert.Equal(LiveryPattern.Digital, Liveries.Get("forest_digital").Pattern);
    }
}
