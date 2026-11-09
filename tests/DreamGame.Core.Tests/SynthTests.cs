using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Game.Audio;
using Xunit;

namespace DreamGame.Core.Tests;

public class SynthTests
{
    public static IEnumerable<object[]> Sounds() => new (string, Func<float[]>)[]
    {
        ("cannon 37", () => Synth.Cannon(0.7f)), ("cannon 120", () => Synth.Cannon(1.35f)),
        ("explosion small", () => Synth.Explosion(0.6f)), ("explosion large", () => Synth.Explosion(1.6f)),
        ("penetration", () => Synth.Penetration()), ("clang", () => Synth.Clang()), ("ricochet", () => Synth.Ricochet()),
        ("dirt", () => Synth.Dirt()), ("place", () => Synth.Place(0.5f)), ("remove", () => Synth.Remove()),
        ("tick", () => Synth.Tick()), ("error", Synth.Error), ("reload", () => Synth.Reload()),
        ("engine", () => Synth.EngineLoop()),
    }.Select(p => new object[] { p.Item1, p.Item2 });

    [Theory]
    [MemberData(nameof(Sounds))]
    public void SoundsAreAudibleAndNeverClip(string name, Func<float[]> make)
    {
        float[] s = make();
        Assert.True(s.Length > 1000, name);
        Assert.All(s, v => Assert.True(float.IsFinite(v)));
        float peak = s.Max(MathF.Abs);
        Assert.InRange(peak, 0.3f, 0.95f);
        double rms = Math.Sqrt(s.Average(v => (double)v * v));
        Assert.True(rms > 0.005, $"{name} is nearly silent");
        if (name != "engine") Assert.True(Math.Abs(s[0]) < 0.1f, $"{name} starts with a click"); // the loop is checked separately
    }

    [Fact]
    public void EngineLoopJoinsWithoutAClick()
    {
        float[] s = Synth.EngineLoop();
        float jump = Math.Abs(s[0] - s[^1]);
        float typical = Enumerable.Range(1, s.Length - 1).Select(i => Math.Abs(s[i] - s[i - 1])).Max();
        Assert.True(jump <= typical * 1.5f, $"loop jump {jump} vs biggest normal step {typical}");
    }

    [Fact]
    public void BiggerGunsBoomLonger()
    {
        Assert.True(Synth.Cannon(1.35f).Length > Synth.Cannon(0.7f).Length);
        Assert.Equal(Synth.Cannon(1f, 5), Synth.Cannon(1f, 5));
    }
}
