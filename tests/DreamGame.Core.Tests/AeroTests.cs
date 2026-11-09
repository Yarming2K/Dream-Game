using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DreamGame.Core.Tests;

public class AeroTests
{
    private static VehicleAnalysis A(Blueprint bp) => VehicleAnalysis.Analyze(bp);

    [Fact]
    public void KestrelIsAStablePlaneThatCanGetOffTheGround()
    {
        VehicleAnalysis a = A(ExampleDesigns.Kestrel());
        Assert.Equal(MobilityMode.Plane, a.Mode);
        Assert.True(a.Air.CanFly);
        Assert.InRange(a.Air.StallSpeedMs * 3.6f, 100f, 180f);
        Assert.True(a.Air.TopSpeedMs > a.Air.StallSpeedMs * 1.6f, $"top {a.Air.TopSpeedMs * 3.6f:0} km/h");
        Assert.True(a.Air.StaticMarginM > 0, "the wings should lift behind the center of mass");
        Assert.True(a.Air.HasRoll && a.Air.HasPitch && a.Air.HasYaw);
        Assert.Equal(3, a.Wheels.Count);
        Assert.Equal(2, a.Air.Bombs);
        Assert.Equal(0, a.ErrorCount);
    }

    [Fact]
    public void ControlSurfacesDoWhatTheirPlaceSuggests()
    {
        VehicleAnalysis a = A(ExampleDesigns.Kestrel());
        SurfaceRole RoleAt(int x, int y, int z) => a.Surfaces.Single(s => s.Cell == new Int3(x, y, z)).Role;
        Assert.Equal(SurfaceRole.Aileron, RoleAt(10, 1, 0));
        Assert.Equal(SurfaceRole.Aileron, RoleAt(-8, 1, 0));
        Assert.Equal(SurfaceRole.Elevator, RoleAt(2, 1, 7));
        Assert.Equal(SurfaceRole.Rudder, RoleAt(0, 3, 7));
        Assert.Equal(SurfaceRole.Wing, RoleAt(5, 1, -2));
    }

    [Fact]
    public void HornetHoversWithPowerToSpare()
    {
        VehicleAnalysis a = A(ExampleDesigns.Hornet());
        Assert.Equal(MobilityMode.Helicopter, a.Mode);
        Assert.True(a.Air.HoverThrustN > 1.15f * a.MassKg * GunMath.Gravity);
        Assert.Equal(2, a.Missiles.Count(m => m.Valid));
        Assert.Equal(0, a.ErrorCount);
    }

    [Fact]
    public void WaspIsADroneFlownByRemote()
    {
        VehicleAnalysis a = A(ExampleDesigns.Wasp());
        Assert.Equal(MobilityMode.Hover, a.Mode);
        Assert.True(a.Air.CanFly);
        SeatInfo pilot = Assert.Single(a.Seats);
        Assert.Equal(SeatRole.Driver, pilot.Role);
        Assert.True(pilot.Remote);
        Assert.True(a.Guns[0].FlakRounds > 0);
    }

    [Fact]
    public void ZephyrFloatsOnGasAndCanStillComeDown()
    {
        VehicleAnalysis a = A(ExampleDesigns.Zephyr());
        Assert.Equal(MobilityMode.Airship, a.Mode);
        Assert.True(a.Air.GasLiftKg >= a.MassKg);
        Assert.True(a.Air.GasLiftKg * Aero.GasTrimMin <= a.MassKg);
        Assert.Empty(a.Disconnected);
        Assert.InRange(a.TopSpeedKmh, 50f, 145f);
    }

    [Fact]
    public void MissilesAreOnlyAsGoodAsTheirParts()
    {
        BlockDef motor = BlockCatalog.Get("missile_motor"), warhead = BlockCatalog.Get("missile_warhead");
        BlockDef fins = BlockCatalog.Get("missile_fins"), optical = BlockCatalog.Get("seeker_optical");
        MissileStats basic = Aero.Missile(new[] { motor, warhead, fins, optical });
        MissileStats fast = Aero.Missile(new[] { motor, motor, warhead, fins, optical });
        MissileStats big = Aero.Missile(new[] { motor, warhead, warhead, fins, optical });
        MissileStats rocket = Aero.Missile(new[] { motor, warhead });
        MissileStats finless = Aero.Missile(new[] { motor, warhead, optical });

        Assert.True(fast.BurnoutSpeed > basic.BurnoutSpeed && fast.RangeM > basic.RangeM);
        Assert.True(big.Damage > basic.Damage && big.RadiusM > basic.RadiusM && big.BurnoutSpeed < basic.BurnoutSpeed);
        Assert.Equal(SeekerKind.None, rocket.Seeker);
        Assert.Equal(0f, rocket.MaxTurnAccel);
        Assert.Equal(SeekerKind.Optical, finless.Seeker);
        Assert.Equal(0f, finless.MaxTurnAccel);
        Assert.True(basic.MaxTurnAccel > 0);
    }

    [Fact]
    public void ASeekerHasToGoOnTheNose()
    {
        var bp = Blueprint.FromCellMap("test", new[]
        {
            new BlueprintBlock("missile_rail", new Int3(0, 0, 0), 0),
            new BlueprintBlock("missile_motor", new Int3(0, 0, -1), 0),
            new BlueprintBlock("seeker_optical", new Int3(0, 0, -2), 0),
            new BlueprintBlock("missile_warhead", new Int3(0, 0, -3), 0),
        });
        MissileInfo m = Assert.Single(A(bp).Missiles);
        Assert.False(m.Valid);
        Assert.Contains("nose", m.Problem);
    }

    [Fact]
    public void WingsLiftMoreUntilTheyStall()
    {
        float a5 = Aero.LiftCoefficient(0.087f), stall = Aero.LiftCoefficient(Aero.StallAngle), past = Aero.LiftCoefficient(0.55f);
        Assert.True(a5 > 0 && stall > a5);
        Assert.Equal(Aero.MaxLift, stall, 3);
        Assert.True(past < stall);
        Assert.Equal(-a5, Aero.LiftCoefficient(-0.087f), 4);
        Assert.True(Aero.IsStalled(0.4f) && !Aero.IsStalled(0.1f));
    }

    [Fact]
    public void PropellersPullHardestWhenSlowAndRotorsLiftMoreWithPower()
    {
        float disk = Aero.DiskArea(Aero.PropRadiusM);
        Assert.True(Aero.PropThrust(600e3f, 0, disk) > Aero.PropThrust(600e3f, 80, disk));
        float rotor = Aero.DiskArea(Aero.RotorRadiusM);
        Assert.True(Aero.RotorThrust(800e3f, rotor) > Aero.RotorThrust(400e3f, rotor));
        Assert.True(Aero.StallSpeed(3000, 30) < Aero.StallSpeed(3000, 15));
    }

    [Fact]
    public void BlocksInTheRotorsPathAreFlagged()
    {
        var cells = ExampleDesigns.Hornet().ToCellMap().Values.ToList();
        cells.Add(new BlueprintBlock("airframe", new Int3(4, 4, 0), 0));
        cells.Add(new BlueprintBlock("airframe", new Int3(3, 4, 0), 0));
        cells.Add(new BlueprintBlock("airframe", new Int3(2, 4, 0), 0));
        cells.Add(new BlueprintBlock("airframe", new Int3(1, 4, 0), 0));
        VehicleAnalysis a = A(Blueprint.FromCellMap("blocked", cells));
        Assert.Contains(a.Warnings, w => w.Text.Contains("path of the rotor"));
    }

    [Fact]
    public void FlakFeedsAnAutocannonThatPointsAtTheSky()
    {
        VehicleAnalysis a = A(ExampleDesigns.Skyguard());
        Assert.Equal(2, a.Guns.Count);
        Assert.All(a.Guns, g => Assert.True(g.FlakRounds > 0 && g.HeRounds > 0));
        Assert.True(BlockCatalog.AutoCannon40.MaxElevationDeg >= 80f);
        Assert.True(a.Guns[0].Stats.ReloadS < 0.5f);
    }
}
