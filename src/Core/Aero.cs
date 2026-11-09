using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamGame.Core;

/// <summary>What a control surface does, worked out from where it sits and which way it faces.</summary>
public enum SurfaceRole
{
    /// <summary>A plain wing panel: lift only.</summary>
    Wing,
    Aileron,
    Elevator,
    Rudder,
}

public enum SeekerKind
{
    /// <summary>No seeker: an unguided rocket.</summary>
    None,

    /// <summary>Flies to wherever the crew is aiming (TV or laser guided). Works against anything.</summary>
    Optical,

    /// <summary>Locks on to the nearest enemy aircraft in front of it and chases it. Ignores ground targets.</summary>
    Radar,
}

/// <summary>A lifting surface: a wing panel or a control surface.</summary>
public sealed class SurfaceInfo
{
    public Int3 Cell;
    public int Orientation;
    public SurfaceRole Role;
    public bool Control;
}

/// <summary>Something that pushes: an air propeller, a jet, a rotor or a lift fan.</summary>
public sealed class ThrusterInfo
{
    public Int3 Cell;
    public BlockKind Kind;
    public int Orientation;

    /// <summary>Which way it pushes, in the design's frame (rotors push along their up axis).</summary>
    public Int3 Direction;
}

public struct MissileStats
{
    public float MassKg;
    public float ThrustN;
    public float BurnS;
    public float Damage;
    public float RadiusM;

    /// <summary>Hardest turn the fins can pull (m/s²). Zero means it can't steer.</summary>
    public float MaxTurnAccel;
    public SeekerKind Seeker;

    /// <summary>Speed when the motor burns out (m/s), and roughly how far it reaches.</summary>
    public float BurnoutSpeed;
    public float RangeM;
}

/// <summary>A missile on a rail: the parts lined up in front of the rail are the missile.</summary>
public sealed class MissileInfo
{
    public Int3 Rail;
    public Int3 Forward;
    public int Section;

    /// <summary>The missile's parts, from the rail outward (so the last one is the nose).</summary>
    public readonly List<Int3> Parts = new();
    public MissileStats Stats;
    public bool Valid;
    public string Problem;
}

public struct AirStats
{
    public float WingAreaM2;
    public float StallSpeedMs;
    public float TopSpeedMs;
    public float StaticThrustN;

    /// <summary>Most upward push from rotors, lift fans and upward jets together (N).</summary>
    public float HoverThrustN;
    public float GasLiftKg;

    /// <summary>Where the horizontal surfaces lift, along the length (meters, design frame), and how far behind the center of mass that is.</summary>
    public float LiftCenterZ;
    public float StaticMarginM;
    public bool HasRoll, HasPitch, HasYaw;
    public bool CanFly;
    public int Bombs;
}

/// <summary>Flying: lift and drag of wing panels, propellers and rotors, gas cells, bombs and missiles.</summary>
public static class Aero
{
    public const float AirDensity = 1.225f;
    public static readonly float PanelArea = Blueprint.CellSize * Blueprint.CellSize;

    /// <summary>Lift per radian of angle of attack, and the most lift a panel makes before it stalls.</summary>
    public const float LiftSlope = 4.8f;
    public const float MaxLift = 1.4f;
    public static readonly float StallAngle = MaxLift / LiftSlope;

    /// <summary>How far control surfaces swing (radians).</summary>
    public const float ControlThrow = 0.35f;

    public const float PropRadiusM = 1.3f;
    public const float RotorRadiusM = 4.5f;
    public const float FanRadiusM = 1.0f;
    public const float PropEfficiency = 0.8f;

    /// <summary>Rotors and fans don't turn all their power into lift (the "figure of merit").</summary>
    public const float LiftEfficiency = 0.7f;

    /// <summary>Drag a spinning rotor or fan adds when flying along (m², drag coefficient included).</summary>
    public const float RotorDragArea = 2.5f, FanDragArea = 0.6f;

    public const float JetThrustN = 16000f;
    public const float JetFuelPerSecond = 0.9f;

    /// <summary>
    /// What one gas cell holds up. Far more than real gas would at this size (a real half-meter cell of
    /// helium lifts about 0.15 kg), so airships stay a buildable size.
    /// </summary>
    public const float GasCellLiftKg = 20f;

    /// <summary>Airships hold their height by venting and topping up gas: they can trim lift between these.</summary>
    public const float GasTrimMin = 0.6f, GasTrimMax = 1f;

    public const float BombDamage = 1600f;
    public const float BombRadiusM = 6.5f;
    public const float BombDragK = 0.00005f;

    public static float DiskArea(float radius) => MathF.PI * radius * radius;

    /// <summary>Lift coefficient of a panel at angle of attack <paramref name="alpha"/> (radians): straight up to the stall, then it falls away.</summary>
    public static float LiftCoefficient(float alpha)
    {
        float a = MathF.Abs(alpha);
        if (a > MathF.PI / 2) a = MathF.PI - a;
        float cl = a <= StallAngle
            ? LiftSlope * a
            : MathF.Max(MaxLift - 1.6f * (a - StallAngle), 0.9f * MathF.Sin(2f * a));
        return MathF.Sign(alpha) * MathF.Sign(MathF.Cos(alpha)) * cl;
    }

    public static float DragCoefficient(float alpha, float cl)
    {
        float s = MathF.Sin(alpha);
        return 0.02f + 0.045f * cl * cl + (MathF.Abs(alpha) > StallAngle ? 1.1f * s * s : 0f);
    }

    public static bool IsStalled(float alpha) => MathF.Abs(alpha) > StallAngle && MathF.Abs(alpha) < MathF.PI - StallAngle;

    /// <summary>Thrust (N) of a propeller turning <paramref name="powerW"/> watts at an airspeed: strong when slow, fading as it goes faster.</summary>
    public static float PropThrust(float powerW, float airspeed, float diskArea)
    {
        if (powerW <= 0) return 0;
        float p = powerW * PropEfficiency;
        float stat = MathF.Pow(2f * AirDensity * diskArea * p * p, 1f / 3f);
        return MathF.Min(stat, p / MathF.Max(airspeed, 1f));
    }

    /// <summary>Upward thrust (N) of a rotor or fan hovering on <paramref name="powerW"/> watts.</summary>
    public static float RotorThrust(float powerW, float diskArea) =>
        powerW <= 0 ? 0 : LiftEfficiency * MathF.Pow(2f * AirDensity * diskArea * powerW * powerW, 1f / 3f);

    /// <summary>Slowest speed the wings can hold the weight up at (m/s).</summary>
    public static float StallSpeed(float massKg, float wingArea) =>
        wingArea <= 0 ? float.PositiveInfinity : MathF.Sqrt(2f * massKg * GunMath.Gravity / (AirDensity * wingArea * MaxLift));

    /// <summary>Drag (N) in level flight at <paramref name="speed"/>: the body pushing through the air, plus what it costs the wings to hold the weight up.</summary>
    public static float LevelDrag(float speed, float massKg, float wingArea, float bodyDragArea)
    {
        float q = 0.5f * AirDensity * speed * speed;
        if (q < 1e-3f) return 0;
        float cl = wingArea > 0 ? MathF.Min(massKg * GunMath.Gravity / (q * wingArea), MaxLift) : 0;
        return q * (bodyDragArea + wingArea * DragCoefficient(cl / LiftSlope, cl));
    }

    /// <summary>Top speed in level flight (m/s): where thrust runs out against drag.</summary>
    public static float TopSpeed(Func<float, float> thrustAt, float massKg, float wingArea, float bodyDragArea)
    {
        float best = 0;
        for (float v = 5f; v <= 260f; v += 1f)
        {
            if (thrustAt(v) < LevelDrag(v, massKg, wingArea, bodyDragArea)) break;
            best = v;
        }

        return best;
    }

    /// <summary>Works out what a missile built from these parts (rail outward, nose last) can do.</summary>
    public static MissileStats Missile(IReadOnlyList<BlockDef> parts)
    {
        var s = new MissileStats();
        int motors = 0, warheads = 0, fins = 0;
        foreach (BlockDef d in parts)
        {
            s.MassKg += d.MassKg;
            switch (d.Kind)
            {
                case BlockKind.MissileMotor: motors++; break;
                case BlockKind.MissileWarhead: warheads++; break;
                case BlockKind.MissileFins: fins++; break;
                case BlockKind.MissileSeeker: s.Seeker = d.Seeker; break;
            }
        }

        s.ThrustN = 6000f * motors;
        s.BurnS = motors > 0 ? 2.2f + 0.8f * (motors - 1) : 0;
        s.Damage = 650f * warheads;
        s.RadiusM = warheads > 0 ? 2.6f + 0.6f * (warheads - 1) : 0;
        s.MaxTurnAccel = s.Seeker == SeekerKind.None ? 0 : MathF.Min(60f * fins, 300f);
        float mass = MathF.Max(s.MassKg, 1f);
        s.BurnoutSpeed = s.ThrustN / mass * s.BurnS * 0.85f;
        s.RangeM = s.BurnoutSpeed * (s.BurnS * 0.5f + 5f);
        return s;
    }

    /// <summary>Drag on a missile in flight: v' = -k v².</summary>
    public const float MissileDragK = 0.00025f;

    public const float MissileLifeS = 15f;
}
