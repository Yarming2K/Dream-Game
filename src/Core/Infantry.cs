using System;

namespace DreamGame.Core;

/// <summary>A hand-held weapon. Shots use the same shells and damage rules as vehicle guns.</summary>
public sealed class InfantryWeaponSpec
{
    public string Id;
    public string Name;
    public string Description;
    public int MagSize;
    public int Reserve;
    public float FireInterval;
    public float ReloadS;
    public float SpreadDeg;

    /// <summary>How far the view kicks up per shot, in degrees.</summary>
    public float RecoilDeg;

    public bool Automatic;
    public AmmoType Ammo;
    public GunStats Stats;
    public BreechSpec Breech;

    /// <summary>Health taken from a person hit directly.</summary>
    public float DirectDamage;
}

public static class InfantryWeapons
{
    private static readonly BreechSpec RifleRound = new()
    {
        CaliberMm = 7.62f, ShellMassKg = 0.0095f, BaseVelocity = 850, BasePenMm = 9, ApDamage = 6,
        DragK = 0.0011f, ShellUnits = 1,
    };

    public static readonly InfantryWeaponSpec Rifle = new()
    {
        Id = "rifle", Name = "Battle Rifle",
        Description = "Automatic rifle. Deadly to people, sparks off armor. Good for clearing infantry and popping fuel tanks on thin-skinned vehicles.",
        MagSize = 30, Reserve = 240, FireInterval = 0.1f, ReloadS = 2.2f, SpreadDeg = 0.45f, RecoilDeg = 0.9f,
        Automatic = true, Ammo = AmmoType.AP, Breech = RifleRound, DirectDamage = 34,
        Stats = new GunStats
        {
            CaliberMm = 7.62f, MuzzleVelocity = 850, ApPenMm = 9, ApDamage = 6, ShellMassKg = 0.0095f,
            DragK = 0.0011f, HeDragK = 0.0011f, ReloadS = 0.1f, SpreadDeg = 0.45f, ShellUnits = 1,
        },
    };

    private static readonly BreechSpec RocketMotor = new()
    {
        CaliberMm = 84, ShellMassKg = 3f, BaseVelocity = 170, BasePenMm = 0, HeDamage = 330, HeRadiusM = 1.8f,
        DragK = 0.00015f, ShellUnits = 1,
    };

    public static readonly InfantryWeaponSpec Rocket = new()
    {
        Id = "rocket", Name = "AT Rocket",
        Description = "Shaped-charge rocket. Slow and drops over distance, but punches about 280 mm through armor and blows up inside.",
        MagSize = 1, Reserve = 6, FireInterval = 0.5f, ReloadS = 3.2f, SpreadDeg = 0.25f, RecoilDeg = 4f,
        Automatic = false, Ammo = AmmoType.HE, Breech = RocketMotor, DirectDamage = 200,
        Stats = new GunStats
        {
            CaliberMm = 84, MuzzleVelocity = 170, HeVelocity = 170, HePenMm = 280, HeDamage = 330, HeRadiusM = 1.8f,
            ShellMassKg = 3f, DragK = 0.00015f, HeDragK = 0.00015f, ReloadS = 3.2f, SpreadDeg = 0.25f, ShellUnits = 1,
        },
    };
}

public static class InfantryDamage
{
    public const float MaxHealth = 100f;

    /// <summary>Health a person loses to an explosion <paramref name="distM"/> away. People are hurt further out than blocks.</summary>
    public static float FromBlast(float distM, float radiusM, float damage)
    {
        float reach = radiusM * 1.8f;
        if (distM >= reach || reach <= 0) return 0;
        return damage * 0.45f * MathF.Pow(1f - distM / reach, 1.2f);
    }

    /// <summary>Health lost to a direct hit from a vehicle gun's shell: anything bigger than a rifle is lethal.</summary>
    public static float FromShell(Shell s) => s.CaliberMm < 20 ? 34f : 250f;
}
