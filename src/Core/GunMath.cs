using System;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>Performance of a gun built from a breech plus attached parts.</summary>
public struct GunStats
{
    public float CaliberMm;
    public float MuzzleVelocity;
    public float HeVelocity;
    public float ApPenMm;
    public float HePenMm;
    public float ApDamage;
    public float HeDamage;
    public float HeRadiusM;
    public float ReloadS;
    public float SpreadDeg;
    public float RecoilImpulse;
    public float ShellMassKg;
    public float DragK;
    public float HeDragK;
    public int ShellUnits;

    /// <summary>Rounds per shot: a gatling's burst, or a rocket launcher's salvo (fired one after another).</summary>
    public int Burst;

    /// <summary>Electric charge per shot, in MJ (energy weapons; 0 for guns).</summary>
    public float ChargeMJ;

    /// <summary>Lasers: range at full strength, in meters.</summary>
    public float BeamRangeM;
}

public static class GunMath
{
    public const float Gravity = 9.81f;
    public const int MaxLoaders = 3;
    public const int MaxPropellant = 2;

    /// <summary>Velocity multiplier from barrel length: 1 + 0.9 (1 - e^(-n/5)).</summary>
    public static float BarrelFactor(int barrels) => 1f + 0.9f * (1f - MathF.Exp(-barrels / 5f));

    /// <summary>Seconds between rockets in a salvo.</summary>
    public const float RippleS = 0.18f;

    public static GunStats Compute(BreechSpec b, int barrels, bool brake, int loaders, int propellant)
    {
        // Propellant is for guns that burn powder: railguns, lasers and rockets get nothing from it.
        int prop = b.Rail || b.Beam || b.Rocket ? 0 : Math.Clamp(propellant, 0, MaxPropellant);
        int load = Math.Clamp(loaders, 0, MaxLoaders);
        // Rockets leave at their own speed (the tubes only hold more of them); lasers don't fly at all.
        bool selfPropelled = b.Rocket || b.Beam;
        float velocity = selfPropelled ? b.BaseVelocity : b.BaseVelocity * BarrelFactor(barrels) * (1f + 0.1f * prop);
        float heVelocity = b.Rocket ? velocity : velocity * 0.85f;
        float recoil = selfPropelled ? 0f : b.ShellMassKg * velocity * 0.5f * (brake ? 0.6f : 1f) * (1f + 0.15f * prop);
        float spread = b.Rocket ? 1.4f : b.Beam ? 0f : 0.35f / (1f + 0.08f * barrels);
        return new GunStats
        {
            CaliberMm = b.CaliberMm,
            MuzzleVelocity = velocity,
            HeVelocity = heVelocity,
            ApPenMm = PenAtSpeed(b, velocity),
            HePenMm = b.CaliberMm * 0.3f,
            ApDamage = b.ApDamage,
            HeDamage = b.HeDamage,
            HeRadiusM = b.HeRadiusM,
            ReloadS = b.ReloadS * MathF.Pow(0.78f, load),
            SpreadDeg = spread,
            RecoilImpulse = recoil,
            ShellMassKg = b.ShellMassKg,
            DragK = b.DragK,
            HeDragK = b.Rocket ? b.DragK : b.DragK * 1.5f,
            ShellUnits = b.ShellUnits,
            Burst = b.Rocket ? 2 * Math.Max(barrels, 1) : Math.Max(b.Burst, 1),
            ChargeMJ = b.ChargeMJ,
            BeamRangeM = b.Beam ? b.BeamRangeM * BarrelFactor(barrels) : 0f,
        };
    }

    /// <summary>AP penetration (mm) for a shell from this breech arriving at <paramref name="speed"/> m/s.</summary>
    public static float PenAtSpeed(BreechSpec b, float speed) =>
        speed <= 0 ? 0 : b.BasePenMm * MathF.Pow(speed / b.BaseVelocity, 1.2f);

    public static float SpeedAtRange(float v0, float dragK, float meters) => v0 * MathF.Exp(-dragK * meters);

    /// <summary>
    /// Where to aim to hit a moving target: where it will be when the shell arrives (a few refinement passes).
    /// </summary>
    public static Vector3 LeadTarget(Vector3 shooter, Vector3 target, Vector3 targetVelocity, float shellSpeed, int passes = 3)
    {
        Vector3 aim = target;
        for (int i = 0; i < passes; i++)
        {
            float t = Vector3.Distance(shooter, aim) / MathF.Max(shellSpeed, 1f);
            aim = target + targetVelocity * t;
        }

        return aim;
    }

    /// <summary>
    /// Low-arc launch angle (radians above horizontal) to hit a point <paramref name="horizontal"/> meters away
    /// and <paramref name="height"/> meters up, ignoring drag. False if out of range.
    /// </summary>
    public static bool SolveElevation(float speed, float horizontal, float height, out float angle)
    {
        const float g = Gravity;
        if (horizontal < 0.01f)
        {
            angle = height >= 0 ? MathF.PI / 2 : -MathF.PI / 2;
            return true;
        }

        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * horizontal * horizontal + 2 * height * v2);
        if (disc < 0)
        {
            angle = MathF.PI / 4;
            return false;
        }

        angle = MathF.Atan((v2 - MathF.Sqrt(disc)) / (g * horizontal));
        return true;
    }

    /// <summary>The high, plunging arc to the same point (mortars). False if out of range.</summary>
    public static bool SolveElevationHigh(float speed, float horizontal, float height, out float angle)
    {
        const float g = Gravity;
        if (horizontal < 0.01f)
        {
            angle = MathF.PI / 2;
            return true;
        }

        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * horizontal * horizontal + 2 * height * v2);
        if (disc < 0)
        {
            angle = MathF.PI / 4;
            return false;
        }

        angle = MathF.Atan((v2 + MathF.Sqrt(disc)) / (g * horizontal));
        return true;
    }
}
