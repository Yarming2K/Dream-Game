using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace DreamGame.Core.Tests;

public class GunMathTests
{
    [Fact]
    public void LongerBarrelsHitHarderWithDiminishingReturns()
    {
        GunStats short2 = GunMath.Compute(BlockCatalog.Medium76, 2, false, 0, 0);
        GunStats mid6 = GunMath.Compute(BlockCatalog.Medium76, 6, false, 0, 0);
        GunStats long12 = GunMath.Compute(BlockCatalog.Medium76, 12, false, 0, 0);
        Assert.True(mid6.MuzzleVelocity > short2.MuzzleVelocity);
        Assert.True(mid6.ApPenMm > short2.ApPenMm);
        Assert.True(mid6.MuzzleVelocity - short2.MuzzleVelocity > long12.MuzzleVelocity - mid6.MuzzleVelocity);
    }

    [Fact]
    public void LoadersSpeedUpReloadUpToThree()
    {
        float r0 = GunMath.Compute(BlockCatalog.Heavy120, 4, false, 0, 0).ReloadS;
        float r3 = GunMath.Compute(BlockCatalog.Heavy120, 4, false, 3, 0).ReloadS;
        float r5 = GunMath.Compute(BlockCatalog.Heavy120, 4, false, 5, 0).ReloadS;
        Assert.True(r3 < r0 * 0.5f);
        Assert.Equal(r3, r5);
    }

    [Fact]
    public void MuzzleBrakeCutsRecoil()
    {
        GunStats plain = GunMath.Compute(BlockCatalog.Heavy120, 6, false, 0, 0);
        GunStats braked = GunMath.Compute(BlockCatalog.Heavy120, 6, true, 0, 0);
        Assert.Equal(plain.RecoilImpulse * 0.6f, braked.RecoilImpulse, 1);
    }

    [Fact]
    public void PenetrationDropsWithRange()
    {
        GunStats s = GunMath.Compute(BlockCatalog.Medium76, 6, false, 0, 0);
        float v1000 = GunMath.SpeedAtRange(s.MuzzleVelocity, s.DragK, 1000);
        Assert.True(GunMath.PenAtSpeed(BlockCatalog.Medium76, v1000) < s.ApPenMm);
    }

    [Fact]
    public void ElevationSolverAimsSlightlyUpForADistantFlatTarget()
    {
        Assert.True(GunMath.SolveElevation(800, 500, 0, out float a));
        Assert.InRange(a, 0.001f, 0.05f);
        Assert.False(GunMath.SolveElevation(100, 5000, 0, out _));
    }
}

public class DamageTests
{
    private static OrderedHit Hit(string id, float angle)
    {
        var b = new BlockInstance(BlockCatalog.Get(id), Int3.Zero, 0, 0);
        return new OrderedHit { Block = b, AngleDeg = angle };
    }

    private static Shell Ap(float pen) => new() { Type = AmmoType.AP, PenMm = pen, Damage = 400 };

    [Fact]
    public void ShellBeatsArmorFaceOnButNotWhenSloped()
    {
        var damaged = new List<(OrderedHit, bool)>();
        var reports = new List<HitReport>();
        Assert.Equal(-1, DamageModel.ResolveAp(Ap(100), new[] { Hit("armor_heavy", 0) }, true, damaged, reports));
        Assert.Equal(HitKind.Penetration, reports[0].Kind);

        reports.Clear();
        Assert.Equal(0, DamageModel.ResolveAp(Ap(100), new[] { Hit("armor_heavy", 45) }, true, damaged, reports));
        Assert.Equal(HitKind.NoPenetration, reports[0].Kind);
        Assert.Equal(80f / 0.7071f, reports[0].ArmorMm, 0);
    }

    [Fact]
    public void SteepHitsBounce()
    {
        var reports = new List<HitReport>();
        DamageModel.ResolveAp(Ap(500), new[] { Hit("armor_light", 75) }, true, new List<(OrderedHit, bool)>(), reports);
        Assert.Equal(HitKind.Ricochet, reports[0].Kind);
    }

    [Fact]
    public void PenetrationIsSpentOnEachLayer()
    {
        var hits = new[] { Hit("armor_heavy", 0), Hit("frame", 0), Hit("armor_heavy", 0), Hit("armor_heavy", 0) };
        var reports = new List<HitReport>();
        Shell s = Ap(170);
        int stop = DamageModel.ResolveAp(s, hits, true, new List<(OrderedHit, bool)>(), reports);
        // 170 - 80 (heavy) - 5 (frame) - 80 (heavy) = 5 left, which can't beat the last plate.
        Assert.Equal(3, stop);
        Assert.Equal(new[] { HitKind.Penetration, HitKind.Penetration, HitKind.Penetration, HitKind.NoPenetration },
            reports.Select(r => r.Kind));
        Assert.True(s.Spent);
    }

    [Fact]
    public void BlastDestroysLightArmorNearTheCenterAndSparesHeavyArmorFarAway()
    {
        GridModel wall = GridModel.FromBlueprint(ExampleDesigns.Wall("armor_light", 9, 3));
        var destroyed = new List<BlockInstance>();
        DamageModel.ApplyBlast(wall.Hull, new Vector3(0, 1, -0.6f), 2.2f, 280, destroyed);
        Assert.Contains(destroyed, b => b.Cell == new Int3(0, 1, 0));
        Assert.DoesNotContain(destroyed, b => b.Cell == new Int3(4, 0, 0));

        GridModel heavy = GridModel.FromBlueprint(ExampleDesigns.Wall("armor_heavy", 9, 3));
        destroyed.Clear();
        DamageModel.ApplyBlast(heavy.Hull, new Vector3(0, 1, -0.6f), 2.2f, 280, destroyed);
        Assert.Empty(destroyed);
    }

    [Fact]
    public void BiggerAmmoStoresMakeBiggerExplosions()
    {
        (float small, _) = DamageModel.CookOff(4);
        (float big, float radius) = DamageModel.CookOff(48);
        Assert.True(big > small);
        Assert.True(radius > 1.5f);
        Assert.Equal((0f, 0f), DamageModel.CookOff(0));
    }
}
