using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace DreamGame.Core.Tests;

/// <summary>Overlay styles chosen with the cockpit, and the instruments' arithmetic.</summary>
public class OverlayTests
{
    [Fact]
    public void EachCockpitBringsItsOwnStyle()
    {
        Assert.Equal(OverlayStyle.Glass, Overlays.Chosen(ExampleDesigns.Halcyon()));
        Assert.Equal(OverlayStyle.Glass, Overlays.Chosen(ExampleDesigns.Nightjar()));
        Assert.Equal(OverlayStyle.FighterHud, Overlays.Chosen(ExampleDesigns.Starling()));
        Assert.Equal(OverlayStyle.FighterHud, Overlays.Chosen(ExampleDesigns.Seraph()));
        Assert.Equal(OverlayStyle.Gauges, Overlays.Chosen(ExampleDesigns.Shrike()));
        Assert.Equal(OverlayStyle.Gauges, Overlays.Chosen(ExampleDesigns.Tern()));
        Assert.Equal(OverlayStyle.Tactical, Overlays.Chosen(ExampleDesigns.Warden()));
        Assert.Equal(OverlayStyle.Tactical, Overlays.Chosen(ExampleDesigns.Trident()));
    }

    [Fact]
    public void ADesignsOwnPickWinsAndStylesNotBuiltYetShowAsGlass()
    {
        Blueprint bp = ExampleDesigns.Starling();
        Assert.Null(bp.Overlay);
        Assert.Equal(OverlayStyle.Glass, Overlays.Shown(bp)); // its Fighter HUD isn't built yet
        bp.Overlay = Overlays.Id(OverlayStyle.Classic);
        Assert.Equal(OverlayStyle.Classic, Overlays.Chosen(bp));
        Assert.Equal(OverlayStyle.Classic, Overlays.Shown(bp));
        foreach (OverlayStyle s in Overlays.Choices)
        {
            Assert.Equal(s, Overlays.FromId(Overlays.Id(s)));
            Assert.False(string.IsNullOrWhiteSpace(Overlays.Name(s)));
            Assert.False(string.IsNullOrWhiteSpace(Overlays.Describe(s)));
            Assert.True(Overlays.IsBuilt(Overlays.Shown(s)));
        }

        Assert.Equal(OverlayStyle.Auto, Overlays.FromId("nonsense"));
    }

    [Fact]
    public void TheOverlayIsSavedWithTheDesign()
    {
        Blueprint bp = ExampleDesigns.Warden();
        bp.Overlay = "glass";
        Blueprint back = Blueprint.FromJson(bp.ToJson());
        Assert.Equal("glass", back.Overlay);
        Assert.Equal("glass", bp.Clone().Overlay);
        // Left to the cockpit, nothing is written.
        bp.Overlay = null;
        Assert.DoesNotContain("overlay", bp.ToJson());
    }

    [Fact]
    public void HeadingsRunClockwiseFromNorth()
    {
        Assert.Equal(0f, Instruments.Heading(new Vector3(0, 0, -1)), 3);
        Assert.Equal(90f, Instruments.Heading(new Vector3(1, 0, 0)), 3);
        Assert.Equal(180f, Instruments.Heading(new Vector3(0, 0, 1)), 3);
        Assert.Equal(270f, Instruments.Heading(new Vector3(-1, 0, 0)), 3);
        Assert.Equal(20f, Instruments.Turn(350f, 10f), 3);
        Assert.Equal(-20f, Instruments.Turn(10f, 350f), 3);
    }

    [Fact]
    public void TheRadarArcPlotsWhatsAheadAndDropsWhatsBehind()
    {
        Vector3 here = Vector3.Zero;
        // Straight ahead at half range: middle of the arc, half way up.
        Vector2 ahead = Instruments.RadarPlot(here, 0f, new Vector3(0, 0, -4000), 8000f, 60f).Value;
        Assert.Equal(0f, ahead.X, 3);
        Assert.Equal(0.5f, ahead.Y, 3);
        // Off to the right, on the arc's edge at full range.
        Vector2 edge = Instruments.RadarPlot(here, 0f, new Vector3(MathF.Sin(MathF.PI / 3) * 7999f, 0, -MathF.Cos(MathF.PI / 3) * 7999f), 8000f, 60f).Value;
        Assert.Equal(1f, edge.X, 2);
        Assert.Null(Instruments.RadarPlot(here, 0f, new Vector3(0, 0, 4000), 8000f, 60f));
        Assert.Null(Instruments.RadarPlot(here, 0f, new Vector3(0, 0, -9000), 8000f, 60f));
        // Turned east, something to the east is dead ahead.
        Assert.Equal(0f, Instruments.RadarPlot(here, 90f, new Vector3(3000, 0, 0), 8000f, 60f).Value.X, 3);
    }

    [Fact]
    public void FuelLastsAsLongAsTheBurnAllows()
    {
        Assert.Equal(10f, Instruments.FuelMinutes(600f, 1f), 3);
        Assert.True(float.IsPositiveInfinity(Instruments.FuelMinutes(600f, 0f)));
    }

    [Fact]
    public void TheDamageMapSeesTheVehicleFromAbove()
    {
        var cells = new List<(Int3, bool)>
        {
            (new Int3(0, 0, 0), true), (new Int3(0, 1, 0), false), (new Int3(1, 0, 0), true), (new Int3(1, 0, 3), false),
        };
        var map = Instruments.DamageMap(cells);
        Assert.Equal(3, map.Count);
        Assert.Equal(0.5f, map.Single(c => c.X == 0 && c.Z == 0).Health);
        Assert.Equal(1f, map.Single(c => c.X == 1 && c.Z == 0).Health);
        Assert.Equal(0f, map.Single(c => c.Z == 3).Health);
        // A whole design: every column starts intact.
        Blueprint bp = ExampleDesigns.Halcyon();
        Assert.All(Instruments.DamageMap(bp.ToCellMap().Keys.Select(c => (c, true))), c => Assert.Equal(1f, c.Health));
    }
}
