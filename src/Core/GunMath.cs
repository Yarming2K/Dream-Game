using System;

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
}

public static class GunMath
{
    public const float Gravity = 9.81f;
    public const int MaxLoaders = 3;
    public const int MaxPropellant = 2;

    /// <summary>Velocity multiplier from barrel length: 1 + 0.9 (1 - e^(-n/5)).</summary>
    public static float BarrelFactor(int barrels) => 1f + 0.9f * (1f - MathF.Exp(-barrels / 5f));

    public static GunStats Compute(BreechSpec b, int barrels, bool brake, int loaders, int propellant)
    {
        int prop = Math.Clamp(propellant, 0, MaxPropellant);
        int load = Math.Clamp(loaders, 0, MaxLoaders);
        float velocity = b.BaseVelocity * BarrelFactor(barrels) * (1f + 0.1f * prop);
        float heVelocity = velocity * 0.85f;
        float recoil = b.ShellMassKg * velocity * 0.5f * (brake ? 0.6f : 1f) * (1f + 0.15f * prop);
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
            SpreadDeg = 0.35f / (1f + 0.08f * barrels),
            RecoilImpulse = recoil,
            ShellMassKg = b.ShellMassKg,
            DragK = b.DragK,
            HeDragK = b.DragK * 1.5f,
            ShellUnits = b.ShellUnits,
        };
    }

    /// <summary>AP penetration (mm) for a shell from this breech arriving at <paramref name="speed"/> m/s.</summary>
    public static float PenAtSpeed(BreechSpec b, float speed) =>
        speed <= 0 ? 0 : b.BasePenMm * MathF.Pow(speed / b.BaseVelocity, 1.2f);

    public static float SpeedAtRange(float v0, float dragK, float meters) => v0 * MathF.Exp(-dragK * meters);

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
}
