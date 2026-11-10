using System;
using System.Linq;
using System.Numerics;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class EarthworksTests
{
    /// <summary>A flat field 64 m square at 3 m, 1 m spacing, centred on the origin.</summary>
    private static Earthworks Flat(float height = 3f, int n = 65)
    {
        var h = Enumerable.Repeat(height, n * n).ToArray();
        return new Earthworks(new HeightField(n, n, 1f, new Vector2(-(n - 1) / 2f, -(n - 1) / 2f), h, -26f));
    }

    private static GridRect All(Earthworks e) => new(0, 0, e.Field.Width - 1, e.Field.Depth - 1);

    [Fact]
    public void ACraterIsABowlWithARim()
    {
        Earthworks e = Flat();
        float removed = e.Crater(Vector2.Zero, 5f, 2f);
        Assert.True(removed > 20f);
        // Deepest at the middle, a little under 2 m once it has slumped.
        float mid = e.Field.HeightAt(0, 0);
        Assert.InRange(mid, 3f - 2.05f, 3f - 1.2f);
        // A rim, higher than the ground round it, just outside the bowl.
        Assert.True(e.Field.HeightAt(6.2f, 0) > 3.05f);
        // Far away, untouched.
        Assert.Equal(3f, e.Field.HeightAt(20, 20));
        Assert.True(e.Disturbed[e.Field.Width / 2 * e.Field.Width + e.Field.Width / 2] > 0.9f);
    }

    [Fact]
    public void HalfACratersEarthEndsUpInItsRim()
    {
        Earthworks e = Flat();
        float before = e.VolumeIn(All(e));
        float removed = e.Crater(new Vector2(2, -3), 4f, 1.6f);
        float after = e.VolumeIn(All(e));
        Assert.Equal(before - removed * (1f - Earthworks.RimShare), after, 1);
    }

    [Fact]
    public void DiggingAndPilingKeepTheEarth()
    {
        Earthworks e = Flat();
        float before = e.VolumeIn(All(e));
        float dug = e.Dig(new Vector2(-4, 0), 1.5f, 0.6f);
        Assert.True(dug > 0.5f);
        Assert.Equal(before - dug, e.VolumeIn(All(e)), 2);
        float placed = e.Pile(new Vector2(6, 2), 2f, dug);
        Assert.Equal(dug, placed, 3);
        Assert.Equal(before, e.VolumeIn(All(e)), 2);
        Assert.True(e.Field.HeightAt(6, 2) > 3.1f);
    }

    [Fact]
    public void ADigCanBeCappedByVolume()
    {
        Earthworks e = Flat();
        float got = e.Dig(Vector2.Zero, 2f, 1f, maxVolume: 0.3f);
        Assert.Equal(0.3f, got, 3);
    }

    [Fact]
    public void SlopesSlumpToTheLimit()
    {
        Earthworks e = Flat();
        // A tall narrow heap that couldn't stand.
        e.Pile(Vector2.Zero, 1.2f, 12f);
        float[] h = e.Field.Heights;
        int w = e.Field.Width;
        float worst = 0;
        for (int z = 1; z < w - 1; z++)
        for (int x = 1; x < w - 1; x++)
            worst = MathF.Max(worst, MathF.Abs(h[z * w + x] - h[z * w + x + 1]));
        Assert.True(worst <= e.MaxStepM + 0.35f, $"steepest step {worst}");
    }

    [Fact]
    public void NothingDigsThroughBedrock()
    {
        Earthworks e = Flat();
        e.MaxDigM = 4f;
        for (int i = 0; i < 12; i++) e.Crater(Vector2.Zero, 3f, 2f);
        Assert.True(e.Field.HeightAt(0, 0) >= 3f - 4f - 1e-3f);
    }

    [Fact]
    public void LockedGroundStaysPut()
    {
        Earthworks e = Flat();
        // A building's footprint: grid samples 28..36 each way (the middle 8 m).
        e.Locked = (x, z) => x >= 28 && x <= 36 && z >= 28 && z <= 36;
        e.Crater(new Vector2(1, 1), 6f, 2.5f);
        for (int z = 28; z <= 36; z++)
        for (int x = 28; x <= 36; x++)
            Assert.Equal(3f, e.Field.Heights[z * e.Field.Width + x]);
        // Outside it, the crater still happened.
        Assert.True(e.Field.HeightAt(-6f, 0) < 3f || e.Field.HeightAt(-6f, 0) > 3f);
    }

    [Fact]
    public void ABladeCutsDownToItsEdge()
    {
        Earthworks e = Flat();
        e.Pile(new Vector2(0, 0), 3f, 10f);
        float top = e.Field.HeightAt(0, 0);
        float cut = e.Cut(new Vector2(-1, -3), new Vector2(-1, 3), 2f, 3.2f, 5f);
        Assert.True(cut > 0.5f);
        Assert.True(e.Field.HeightAt(-1, 0) < top);
        Assert.True(e.Field.HeightAt(-1, 0) <= 3.2f + e.MaxStepM * 2);
        // Ground already below the edge isn't touched.
        Assert.Equal(0f, e.Cut(new Vector2(20, -3), new Vector2(20, 3), 2f, 3.2f, 5f));
    }

    [Fact]
    public void CratersGrowWithTheBlastAndShrinkWithHeight()
    {
        (float shellR, float shellD) = Earthworks.CraterSize(280, 2.2f, 0);
        (float bombR, float bombD) = Earthworks.CraterSize(1600, 6.5f, 0);
        (float mortarR, float mortarD) = Earthworks.CraterSize(6500, 13f, 0);
        Assert.True(shellR < bombR && bombR < mortarR);
        Assert.True(shellD < bombD && bombD < mortarD);
        Assert.InRange(bombR, 4f, 6f);
        Assert.InRange(bombD, 1.5f, 2.5f);
        (float highR, float highD) = Earthworks.CraterSize(1600, 6.5f, 4f);
        Assert.True(highR < bombR && highD < bombD);
        Assert.Equal((0f, 0f), Earthworks.CraterSize(1600, 6.5f, 7f));
    }

    [Fact]
    public void ShellsAndSightLinesSeeTheChange()
    {
        Earthworks e = Flat();
        e.Pile(new Vector2(0, 0), 4f, 40f);
        Assert.True(e.Field.MaxHeight > 3.5f);
        // A berm now blocks a sight line that skimmed the old flat ground.
        Assert.True(e.Field.Raycast(new Vector3(-20, 3.6f, 0), new Vector3(20, 3.6f, 0), out _, out _));
    }

    [Fact]
    public void ChangesAreTrackedAndRepeatable()
    {
        Earthworks a = Flat(), b = Flat();
        foreach (Earthworks e in new[] { a, b })
        {
            e.Crater(new Vector2(3, 3), 4f, 1.5f);
            e.Dig(new Vector2(-8, 2), 1f, 0.4f);
            e.Pile(new Vector2(10, -5), 2f, 3f);
        }

        Assert.Equal(a.Field.Heights, b.Field.Heights);
        Assert.True(a.Version > 0);
        GridRect dirty = a.TakeDirty();
        Assert.False(dirty.IsEmpty);
        Assert.True(a.TakeDirty().IsEmpty);
        Assert.True(dirty.X0 <= 32 - 8 && dirty.X1 >= 32 + 10);
    }

    [Fact]
    public void ABladeHoldsItsHeapInFront()
    {
        Earthworks e = Flat();
        // A blade edge along x from -2 to 2 at z = 0, facing -z; its heap goes 1.3 m ahead.
        e.HoldBehind(new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, -1), 2f);
        e.Pile(new Vector2(0, -1.3f), 1.05f, 6f);
        e.Hold = null;
        // Nothing slumped back under the blade or behind it.
        for (int z = 0; z <= 2; z++)
        for (int x = -2; x <= 2; x++)
            Assert.Equal(3f, e.Field.HeightAt(x, z), 3);
        Assert.True(e.Field.HeightAt(0, -1.3f) > 3.5f);
    }
}
