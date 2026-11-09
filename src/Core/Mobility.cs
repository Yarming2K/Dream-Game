using System;

namespace DreamGame.Core;

/// <summary>Shared driving numbers, so the builder's top-speed estimate matches how the vehicle actually drives.</summary>
public static class Mobility
{
    public const float DrivetrainEfficiency = 0.7f;
    public const float AirDensity = 1.2f;
    /// <summary>Speed limit: 110 km/h on wheels, 65 km/h on full tracks.</summary>
    public static float GovernorKmh(float trackFraction) => 110f - 45f * trackFraction;
    public const float SteerAngleDeg = 30f;

    /// <summary>Fuel burned per kW per second at full throttle (100 L lasts about 10 minutes at 450 kW).</summary>
    public const float FuelPerKwSecond = 0.00037f;

    public static float RestLength(BlockKind kind) => kind == BlockKind.Track ? 0.22f : 0.3f;

    public static float Grip(BlockKind kind) => kind == BlockKind.Track ? 1.1f : 0.9f;

    public static float RollingResistance(float trackFraction) => 0.018f + (0.06f - 0.018f) * trackFraction;

    public static float ResistanceForce(float massKg, float speed, float crr, float cdA) =>
        crr * massKg * GunMath.Gravity + 0.5f * AirDensity * cdA * speed * speed;

    /// <summary>Speed (m/s) where engine power equals rolling and air resistance, capped by the governor.</summary>
    public static float TopSpeed(float massKg, float powerKw, float crr, float cdA, float governorKmh)
    {
        if (massKg <= 0 || powerKw <= 0) return 0;
        float p = powerKw * 1000f * DrivetrainEfficiency;
        float lo = 0, hi = governorKmh / 3.6f;
        if (ResistanceForce(massKg, hi, crr, cdA) * hi <= p) return hi;
        for (int i = 0; i < 40; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (ResistanceForce(massKg, mid, crr, cdA) * mid > p) hi = mid;
            else lo = mid;
        }

        return lo;
    }

    /// <summary>Turret traverse speed in degrees per second for a turret of this mass.</summary>
    public static float TraverseDegPerSec(float turretMassKg) =>
        50f * Math.Clamp(MathF.Sqrt(3000f / MathF.Max(turretMassKg, 1f)), 0.15f, 1.5f);

    /// <summary>Gun elevation speed in degrees per second for barrels of this mass.</summary>
    public static float ElevationDegPerSec(float barrelMassKg) =>
        25f * Math.Clamp(MathF.Sqrt(800f / MathF.Max(barrelMassKg, 1f)), 0.25f, 1.5f);

    public const float MinElevationDeg = -8f;
    public const float MaxElevationDeg = 20f;
}
