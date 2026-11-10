using System;

namespace DreamGame.Core;

/// <summary>
/// Electricity for energy weapons. Generators and reactors make it, Capacitor Banks store it (each
/// generator or reactor also holds about a second of its own output), and railguns and lasers take a
/// charge out for every shot. A weapon that can't get its charge holds fire until there's enough.
/// </summary>
public sealed class PowerBank
{
    /// <summary>The most it can hold, in MJ.</summary>
    public float CapacityMJ { get; private set; }

    /// <summary>Charge in store right now, in MJ.</summary>
    public float StoredMJ { get; private set; }

    /// <summary>What the working generators and reactors make, in MW.</summary>
    public float SupplyMW { get; private set; }

    /// <summary>The part of <see cref="SupplyMW"/> that burns fuel (generators), in MW.</summary>
    public float FuelledMW { get; private set; }

    public float Fraction => CapacityMJ > 0 ? StoredMJ / CapacityMJ : 0f;

    /// <summary>Sets what's aboard (after damage, keeping the charge that still fits). <paramref name="fill"/> tops it up.</summary>
    public void Configure(float supplyMW, float fuelledMW, float capacityMJ, bool fill = false)
    {
        SupplyMW = MathF.Max(supplyMW, 0f);
        FuelledMW = Math.Clamp(fuelledMW, 0f, SupplyMW);
        CapacityMJ = MathF.Max(capacityMJ, 0f);
        StoredMJ = fill ? CapacityMJ : MathF.Min(StoredMJ, CapacityMJ);
    }

    /// <summary>
    /// Charges for <paramref name="dt"/> seconds (only while <paramref name="running"/>). Returns the MJ the
    /// fuel-burning generators made, so the vehicle can burn fuel for it.
    /// </summary>
    public float Charge(float dt, bool running = true)
    {
        if (!running || dt <= 0 || SupplyMW <= 0) return 0f;
        float room = CapacityMJ - StoredMJ;
        if (room <= 0) return 0f;
        float made = MathF.Min(room, SupplyMW * dt);
        StoredMJ += made;
        return made * (FuelledMW / SupplyMW);
    }

    /// <summary>Takes <paramref name="mj"/> out if there's that much in store.</summary>
    public bool TryDraw(float mj)
    {
        if (mj <= 0) return true;
        if (StoredMJ + 1e-4f < mj) return false;
        StoredMJ = MathF.Max(0f, StoredMJ - mj);
        return true;
    }
}

public static class Power
{
    /// <summary>Seconds of its own output that each generator or reactor holds, without any capacitors.</summary>
    public const float SourceBufferS = 1f;

    /// <summary>Fuel a generator burns per MJ it makes, in liters (a diesel at about 35% efficiency).</summary>
    public const float FuelPerMJ = 0.08f;

    /// <summary>How much charge the vehicle can hold, in MJ.</summary>
    public static float CapacityMJ(float storeMJ, float electricKw) => storeMJ + electricKw / 1000f * SourceBufferS;

    /// <summary>Seconds to make one shot's worth of charge, from empty.</summary>
    public static float RechargeS(float chargeMJ, float electricKw) =>
        electricKw <= 0 ? float.PositiveInfinity : chargeMJ / (electricKw / 1000f);
}
