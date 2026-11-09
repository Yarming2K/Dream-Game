using System;
using System.Collections.Generic;

namespace DreamGame.Core;

public enum BlockKind
{
    Armor,
    Frame,
    Engine,
    FuelTank,
    Wheel,
    SteerWheel,
    Track,
    DriverSeat,
    GunnerSeat,
    CommanderSeat,
    Breech,
    Barrel,
    MuzzleBrake,
    Loader,
    Propellant,
    AmmoRack,
    AmmoFeed,
    TurretRing,
}

public enum BlockShape
{
    Cube,

    /// <summary>
    /// Half a cube cut diagonally. Unrotated, the solid part is where local y &lt;= z, so the sloped
    /// face points forward and up: the shape of a tank's front plate.
    /// </summary>
    Wedge,
}

public enum AmmoType
{
    AP,
    HE,
}

/// <summary>Fixed properties of a breech, which sets a gun's caliber.</summary>
public sealed class BreechSpec
{
    public float CaliberMm;
    public float ShellMassKg;

    /// <summary>Muzzle velocity with no barrels or propellant, in m/s.</summary>
    public float BaseVelocity;

    /// <summary>AP penetration at <see cref="BaseVelocity"/>, in mm.</summary>
    public float BasePenMm;

    public float ApDamage;
    public float HeDamage;
    public float HeRadiusM;
    public float ReloadS;

    /// <summary>How much rack space one shell takes.</summary>
    public int ShellUnits;

    /// <summary>Velocity loss per meter: v(x) = v0 * e^(-k x).</summary>
    public float DragK;
}

public sealed class BlockDef
{
    public string Id;
    public string Name;
    public string Category;
    public string Description;
    public BlockKind Kind;
    public BlockShape Shape = BlockShape.Cube;

    public float MassKg;
    public float Hp;
    public float ArmorMm;

    /// <summary>Multiplier on explosive damage (composite armor is weak to HE).</summary>
    public float HeVulnerability = 1f;

    public float PowerKw;
    public float FuelL;
    public float WheelRadiusM;
    public BreechSpec Breech;
    public AmmoType Ammo;
    public int AmmoUnits;

    /// <summary>Whether the block has a physics collider. Wheels, barrels and feeds don't.</summary>
    public bool HasCollider = true;

    /// <summary>Damage and radius of the explosion when this block is destroyed (fuel). Ammo racks scale with what's inside.</summary>
    public float DeathExplosionDamage;
    public float DeathExplosionRadiusM;

    public float ColorR, ColorG, ColorB;
    public float Metallic = 0.6f;
    public float Roughness = 0.6f;

    public bool IsArmorLike => Kind == BlockKind.Armor || Kind == BlockKind.Frame;
    public bool ConductsAmmo => Kind == BlockKind.AmmoRack || Kind == BlockKind.AmmoFeed || Kind == BlockKind.TurretRing;
    public bool IsWheelLike => Kind == BlockKind.Wheel || Kind == BlockKind.SteerWheel || Kind == BlockKind.Track;
    public bool IsSeat => Kind == BlockKind.DriverSeat || Kind == BlockKind.GunnerSeat || Kind == BlockKind.CommanderSeat;
}

public static class BlockCatalog
{
    public static readonly string[] Categories = { "Structure", "Mobility", "Control", "Weapons", "Ammo" };

    public static readonly BreechSpec Light37 = new()
    {
        CaliberMm = 37, ShellMassKg = 0.9f, BaseVelocity = 600, BasePenMm = 45,
        ApDamage = 70, HeDamage = 110, HeRadiusM = 1.2f, ReloadS = 1.8f, ShellUnits = 1, DragK = 0.00012f,
    };

    public static readonly BreechSpec Medium76 = new()
    {
        CaliberMm = 76, ShellMassKg = 6.5f, BaseVelocity = 620, BasePenMm = 95,
        ApDamage = 170, HeDamage = 280, HeRadiusM = 2.2f, ReloadS = 4.5f, ShellUnits = 4, DragK = 0.00008f,
    };

    public static readonly BreechSpec Heavy120 = new()
    {
        CaliberMm = 120, ShellMassKg = 18f, BaseVelocity = 650, BasePenMm = 165,
        ApDamage = 340, HeDamage = 540, HeRadiusM = 3.2f, ReloadS = 8f, ShellUnits = 8, DragK = 0.00005f,
    };

    private static readonly List<BlockDef> Defs = new()
    {
        // Structure
        new BlockDef
        {
            Id = "armor_light", Name = "Light Armor", Category = "Structure", Kind = BlockKind.Armor,
            Description = "Thin steel plate. Cheap and light; stops small arms and glancing hits.",
            MassKg = 60, Hp = 120, ArmorMm = 25, ColorR = 0.42f, ColorG = 0.45f, ColorB = 0.38f,
        },
        new BlockDef
        {
            Id = "armor_light_slope", Name = "Light Armor Slope", Category = "Structure", Kind = BlockKind.Armor,
            Shape = BlockShape.Wedge,
            Description = "Sloped light plate. Angled armor counts as thicker, and steep hits can bounce off.",
            MassKg = 35, Hp = 80, ArmorMm = 25, ColorR = 0.42f, ColorG = 0.45f, ColorB = 0.38f,
        },
        new BlockDef
        {
            Id = "armor_heavy", Name = "Heavy Armor", Category = "Structure", Kind = BlockKind.Armor,
            Description = "Thick rolled steel. Stops most medium guns face-on, but it's heavy.",
            MassKg = 220, Hp = 320, ArmorMm = 80, ColorR = 0.33f, ColorG = 0.36f, ColorB = 0.31f,
            Metallic = 0.7f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "armor_heavy_slope", Name = "Heavy Armor Slope", Category = "Structure", Kind = BlockKind.Armor,
            Shape = BlockShape.Wedge,
            Description = "Sloped heavy plate. The best front armor for its weight.",
            MassKg = 130, Hp = 220, ArmorMm = 80, ColorR = 0.33f, ColorG = 0.36f, ColorB = 0.31f,
            Metallic = 0.7f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "armor_composite", Name = "Composite Armor", Category = "Structure", Kind = BlockKind.Armor,
            Description = "Layered ceramic and steel. Excellent against AP for its weight, but HE wrecks it.",
            MassKg = 150, Hp = 200, ArmorMm = 110, HeVulnerability = 1.6f,
            ColorR = 0.55f, ColorG = 0.5f, ColorB = 0.38f, Metallic = 0.3f, Roughness = 0.75f,
        },
        new BlockDef
        {
            Id = "frame", Name = "Frame", Category = "Structure", Kind = BlockKind.Frame,
            Description = "Light internal structure for filling space and holding parts together. Offers almost no protection.",
            MassKg = 20, Hp = 60, ArmorMm = 5, ColorR = 0.25f, ColorG = 0.25f, ColorB = 0.27f,
            Metallic = 0.8f, Roughness = 0.45f,
        },

        // Mobility
        new BlockDef
        {
            Id = "engine_small", Name = "Small Engine", Category = "Mobility", Kind = BlockKind.Engine,
            Description = "150 kW diesel. Needs a fuel tank somewhere in the vehicle.",
            MassKg = 250, Hp = 100, ArmorMm = 10, PowerKw = 150,
            ColorR = 0.5f, ColorG = 0.3f, ColorB = 0.18f, Metallic = 0.7f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "engine_large", Name = "Large Engine", Category = "Mobility", Kind = BlockKind.Engine,
            Description = "450 kW diesel. The heart of a heavy vehicle.",
            MassKg = 700, Hp = 200, ArmorMm = 15, PowerKw = 450,
            ColorR = 0.55f, ColorG = 0.28f, ColorB = 0.12f, Metallic = 0.7f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "fuel_tank", Name = "Fuel Tank", Category = "Mobility", Kind = BlockKind.FuelTank,
            Description = "Holds 100 L of diesel. Burns and explodes when destroyed, so keep it behind armor.",
            MassKg = 120, Hp = 60, ArmorMm = 5, FuelL = 100,
            DeathExplosionDamage = 160, DeathExplosionRadiusM = 1.6f,
            ColorR = 0.6f, ColorG = 0.12f, ColorB = 0.1f, Metallic = 0.5f, Roughness = 0.55f,
        },
        new BlockDef
        {
            Id = "wheel", Name = "Wheel", Category = "Mobility", Kind = BlockKind.Wheel,
            Description = "Driven wheel with suspension. Fast on flat ground. Put wheels on the bottom row with nothing underneath.",
            MassKg = 80, Hp = 90, ArmorMm = 10, WheelRadiusM = 0.42f, HasCollider = false,
            ColorR = 0.1f, ColorG = 0.1f, ColorB = 0.1f, Metallic = 0.1f, Roughness = 0.9f,
        },
        new BlockDef
        {
            Id = "wheel_steer", Name = "Steering Wheel", Category = "Mobility", Kind = BlockKind.SteerWheel,
            Description = "A wheel that turns for steering. Put these at the front.",
            MassKg = 90, Hp = 90, ArmorMm = 10, WheelRadiusM = 0.42f, HasCollider = false,
            ColorR = 0.12f, ColorG = 0.12f, ColorB = 0.14f, Metallic = 0.1f, Roughness = 0.9f,
        },
        new BlockDef
        {
            Id = "track", Name = "Track Segment", Category = "Mobility", Kind = BlockKind.Track,
            Description = "Tank track with a road wheel. Strong grip and skid steering; slower than wheels. Run them along both sides.",
            MassKg = 120, Hp = 140, ArmorMm = 20, WheelRadiusM = 0.3f, HasCollider = false,
            ColorR = 0.16f, ColorG = 0.15f, ColorB = 0.13f, Metallic = 0.6f, Roughness = 0.8f,
        },

        // Control
        new BlockDef
        {
            Id = "driver_seat", Name = "Driver Seat", Category = "Control", Kind = BlockKind.DriverSeat,
            Description = "Where you drive from. Every vehicle needs one. If it's destroyed, the vehicle is knocked out.",
            MassKg = 90, Hp = 80, ArmorMm = 5, ColorR = 0.2f, ColorG = 0.32f, ColorB = 0.5f,
            Metallic = 0.3f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "gunner_seat", Name = "Gunner Seat", Category = "Control", Kind = BlockKind.GunnerSeat,
            Description = "Whoever sits here aims and fires the guns in the same turret (or the hull guns, if it's in the hull). Empty seats get an AI crew member, so you can drive while your gunner shoots.",
            MassKg = 90, Hp = 80, ArmorMm = 5, ColorR = 0.42f, ColorG = 0.26f, ColorB = 0.2f,
            Metallic = 0.3f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "commander_seat", Name = "Commander Seat", Category = "Control", Kind = BlockKind.CommanderSeat,
            Description = "A periscope with strong zoom. A commander spots targets, so AI gunners find and track them much faster.",
            MassKg = 90, Hp = 80, ArmorMm = 5, ColorR = 0.24f, ColorG = 0.38f, ColorB = 0.3f,
            Metallic = 0.3f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "turret_ring", Name = "Turret Ring", Category = "Control", Kind = BlockKind.TurretRing,
            Description = "Everything you build above it (connected to the block on top) turns as a turret. Must face up. Ammo passes through it.",
            MassKg = 300, Hp = 250, ArmorMm = 40, ColorR = 0.6f, ColorG = 0.6f, ColorB = 0.62f,
            Metallic = 0.9f, Roughness = 0.3f,
        },

        // Weapons
        new BlockDef
        {
            Id = "breech_37", Name = "37 mm Breech", Category = "Weapons", Kind = BlockKind.Breech, Breech = Light37,
            Description = "Light gun. Fast reload, low damage. Barrels go in front of it (the arrow side).",
            MassKg = 150, Hp = 120, ArmorMm = 30, ColorR = 0.22f, ColorG = 0.22f, ColorB = 0.2f,
            Metallic = 0.85f, Roughness = 0.35f,
        },
        new BlockDef
        {
            Id = "breech_76", Name = "76 mm Breech", Category = "Weapons", Kind = BlockKind.Breech, Breech = Medium76,
            Description = "Medium gun. A good all-rounder. Barrels go in front of it (the arrow side).",
            MassKg = 500, Hp = 200, ArmorMm = 40, ColorR = 0.22f, ColorG = 0.22f, ColorB = 0.2f,
            Metallic = 0.85f, Roughness = 0.35f,
        },
        new BlockDef
        {
            Id = "breech_120", Name = "120 mm Breech", Category = "Weapons", Kind = BlockKind.Breech, Breech = Heavy120,
            Description = "Heavy gun. Huge damage and recoil, slow reload. Barrels go in front of it (the arrow side).",
            MassKg = 1200, Hp = 320, ArmorMm = 50, ColorR = 0.22f, ColorG = 0.22f, ColorB = 0.2f,
            Metallic = 0.85f, Roughness = 0.35f,
        },
        new BlockDef
        {
            Id = "barrel", Name = "Barrel Segment", Category = "Weapons", Kind = BlockKind.Barrel,
            Description = "Each segment in a straight line in front of a breech adds velocity and penetration (with diminishing returns), but adds weight and slows the turret.",
            MassKg = 90, Hp = 90, ArmorMm = 20, HasCollider = false, ColorR = 0.2f, ColorG = 0.21f, ColorB = 0.19f,
            Metallic = 0.85f, Roughness = 0.35f,
        },
        new BlockDef
        {
            Id = "muzzle_brake", Name = "Muzzle Brake", Category = "Weapons", Kind = BlockKind.MuzzleBrake,
            Description = "Goes on the end of the barrel. Cuts recoil by 40%.",
            MassKg = 70, Hp = 80, ArmorMm = 20, HasCollider = false, ColorR = 0.18f, ColorG = 0.18f, ColorB = 0.17f,
            Metallic = 0.85f, Roughness = 0.35f,
        },
        new BlockDef
        {
            Id = "loader", Name = "Loader", Category = "Weapons", Kind = BlockKind.Loader,
            Description = "Touching a breech: about 22% faster reload each, up to 3.",
            MassKg = 150, Hp = 90, ArmorMm = 10, ColorR = 0.3f, ColorG = 0.4f, ColorB = 0.3f,
            Metallic = 0.6f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "propellant", Name = "Propellant Charge", Category = "Weapons", Kind = BlockKind.Propellant,
            Description = "Touching a breech: +10% muzzle velocity and more recoil each, up to 2.",
            MassKg = 80, Hp = 60, ArmorMm = 5, DeathExplosionDamage = 120, DeathExplosionRadiusM = 1.2f,
            ColorR = 0.72f, ColorG = 0.55f, ColorB = 0.22f, Metallic = 0.8f, Roughness = 0.35f,
        },

        // Ammo
        new BlockDef
        {
            Id = "ammo_ap", Name = "AP Ammo Rack", Category = "Ammo", Kind = BlockKind.AmmoRack, Ammo = AmmoType.AP,
            AmmoUnits = 24,
            Description = "Armor-piercing shells: punch through armor and wreck what's behind it. Must touch the breech or connect through feeds or other racks. Explodes if destroyed while loaded.",
            MassKg = 120, Hp = 70, ArmorMm = 5, ColorR = 0.7f, ColorG = 0.62f, ColorB = 0.3f,
            Metallic = 0.5f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "ammo_he", Name = "HE Ammo Rack", Category = "Ammo", Kind = BlockKind.AmmoRack, Ammo = AmmoType.HE,
            AmmoUnits = 24,
            Description = "High-explosive shells: blast damage around the impact. Great against light armor and composite. Explodes if destroyed while loaded.",
            MassKg = 120, Hp = 70, ArmorMm = 5, ColorR = 0.65f, ColorG = 0.25f, ColorB = 0.18f,
            Metallic = 0.5f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "ammo_feed", Name = "Ammo Feed", Category = "Ammo", Kind = BlockKind.AmmoFeed,
            Description = "Carries shells between racks and breeches. Use it to keep ammo deep inside the hull.",
            MassKg = 30, Hp = 50, ArmorMm = 5, HasCollider = false, ColorR = 0.45f, ColorG = 0.42f, ColorB = 0.3f,
            Metallic = 0.6f, Roughness = 0.5f,
        },
    };

    private static readonly Dictionary<string, BlockDef> ById = BuildIndex();

    private static Dictionary<string, BlockDef> BuildIndex()
    {
        var map = new Dictionary<string, BlockDef>(StringComparer.Ordinal);
        foreach (BlockDef d in Defs) map.Add(d.Id, d);
        return map;
    }

    public static IReadOnlyList<BlockDef> All => Defs;

    public static BlockDef Get(string id) =>
        ById.TryGetValue(id, out BlockDef d) ? d : throw new KeyNotFoundException($"Unknown block '{id}'.");

    public static bool TryGet(string id, out BlockDef def) => ById.TryGetValue(id ?? "", out def);

    public static List<BlockDef> InCategory(string category)
    {
        var list = new List<BlockDef>();
        foreach (BlockDef d in Defs)
            if (d.Category == category) list.Add(d);
        return list;
    }
}
