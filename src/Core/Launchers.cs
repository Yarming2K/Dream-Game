using System;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>
/// The SAM box launcher's layout: a box of canisters, two rows of three, hinged at the back of the block and
/// raised toward the front. Its model and the game's launches both use it, so each missile leaves from the
/// mouth of its own canister. Positions are meters in the block's own frame, from the middle of its box.
/// </summary>
public static class BoxLauncher
{
    public const int Rows = 2, Columns = 3, Canisters = Rows * Columns;

    /// <summary>The box of canisters: how long, how wide across, how deep, and the spacing of the canisters.</summary>
    public const float LengthM = 2.8f, WidthM = 1.32f, DepthM = 0.9f, PitchM = 0.42f;

    /// <summary>The hinge at the bottom back of the box, on top of its base.</summary>
    public static readonly Vector3 Pivot = new(0, -0.95f, 1.2f);

    /// <summary>How far the box is raised above level, in radians.</summary>
    public static float Elevation(BlockDef def) => (90f - def.LaunchTiltDeg) * MathF.PI / 180f;

    /// <summary>Along the canisters, toward their mouths: forward and up.</summary>
    public static Vector3 Axis(BlockDef def)
    {
        float e = Elevation(def);
        return new Vector3(0, MathF.Sin(e), -MathF.Cos(e));
    }

    /// <summary>Square to the canisters, from the bottom of the box to its top.</summary>
    public static Vector3 Across(BlockDef def)
    {
        float e = Elevation(def);
        return new Vector3(0, MathF.Cos(e), MathF.Sin(e));
    }

    /// <summary>The middle of canister <paramref name="i"/>'s mouth (0 to 2: bottom row, left to right; 3 to 5: top row).</summary>
    public static Vector3 Mouth(BlockDef def, int i) =>
        Pivot + Axis(def) * LengthM + Across(def) * (DepthM * 0.5f + PitchM * (i / Columns - 0.5f)) +
        new Vector3((i % Columns - 1) * PitchM, 0, 0);

    /// <summary>A canister's mouth on a placed launcher, in meters in the vehicle's frame.</summary>
    public static Vector3 MouthOnVehicle(BlockDef def, Int3 anchor, int orientation, int i) =>
        Footprint.Center(def, anchor, orientation) * Blueprint.CellSize + Orientations.Apply(orientation, Mouth(def, i));

    /// <summary>Which way a placed launcher's missiles leave, in the vehicle's frame.</summary>
    public static Vector3 AxisOnVehicle(BlockDef def, int orientation) => Orientations.Apply(orientation, Axis(def));

    /// <summary>The canister the next missile leaves from, when <paramref name="loaded"/> are left (the top row goes first).</summary>
    public static int Next(int loaded) => Math.Clamp(loaded - 1, 0, Canisters - 1);
}
