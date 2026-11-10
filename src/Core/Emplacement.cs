namespace DreamGame.Core;

/// <summary>How steady the thing a gun is mounted on is right now.</summary>
public enum Mount
{
    /// <summary>A building: anchored in the ground.</summary>
    Building,

    /// <summary>A ship, or a land vehicle stopped with its stabilizers down.</summary>
    Braced,

    /// <summary>A land vehicle on the move (or with no stabilizers), or an aircraft.</summary>
    Loose,
}

/// <summary>
/// Why you'd build a fort instead of another tank. Every gun on a building is steadier (it spreads less),
/// and heavy weapons are at their best there: a land vehicle can only fire one stopped and braced on its
/// stabilizers, and any vehicle (a ship too) reloads it slower and spreads it more.
/// </summary>
public static class Emplacement
{
    /// <summary>How many stabilizers brace a land vehicle, and how slowly it must be going for them to go down (m/s).</summary>
    public const int StabilizersNeeded = 2;
    public const float BraceSpeedMs = 0.8f;

    /// <summary>Spread on a building (every gun), and heavy weapons' spread and reload on a vehicle.</summary>
    public const float BuildingSpread = 0.6f, VehicleHeavySpread = 2f, VehicleHeavyReload = 2f;

    public static Mount MountOf(bool building, bool naval, bool air, int stabilizers, float speedMs)
    {
        if (building) return Mount.Building;
        if (naval) return Mount.Braced;
        if (!air && stabilizers >= StabilizersNeeded && speedMs < BraceSpeedMs) return Mount.Braced;
        return Mount.Loose;
    }

    public static bool CanFire(bool heavy, Mount m) => !heavy || m != Mount.Loose;

    public static float ReloadFactor(bool heavy, Mount m) => heavy && m != Mount.Building ? VehicleHeavyReload : 1f;

    public static float SpreadFactor(bool heavy, Mount m) => m == Mount.Building ? BuildingSpread : heavy ? VehicleHeavySpread : 1f;
}
