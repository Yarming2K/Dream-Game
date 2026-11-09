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
    ShipPropeller,
    Rudder,
    BallastValve,
    BilgePump,
    LeadBallast,
    TorpedoTube,
    Wing,
    ControlSurface,
    AirPropeller,
    JetEngine,
    Rotor,
    LiftFan,
    GasCell,
    BombRack,
    MissileRail,
    MissileSeeker,
    MissileWarhead,
    MissileMotor,
    MissileFins,
    DroneCore,
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
    Torpedo,

    /// <summary>Air-burst shells for shooting down aircraft: they go off near one, or at the range you aimed at.</summary>
    Flak,
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

    /// <summary>How far up the gun can point. Anti-aircraft guns point nearly straight up.</summary>
    public float MaxElevationDeg = 20f;
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

    /// <summary>Whether water can get past this block. Hulls are made of watertight blocks; frames and feeds let water through.</summary>
    public bool Watertight = true;

    /// <summary>
    /// How much of its cell this block fills with something that pushes water aside (0 to 1). Unless a block
    /// says otherwise (sealed tanks, tyres) that's just its own steel, so armor sinks on its own: ships float
    /// on the sealed air inside their hulls.
    /// </summary>
    public float Displacement
    {
        get => _displacement >= 0 ? _displacement : Math.Clamp(MassKg / (SteelDensity * CellVolume), 0f, 1f);
        set => _displacement = value;
    }

    private float _displacement = -1f;
    public const float SteelDensity = 7850f;
    private const float CellVolume = Blueprint.CellSize * Blueprint.CellSize * Blueprint.CellSize;

    /// <summary>Damage and radius of the explosion when this block is destroyed (fuel). Ammo racks scale with what's inside.</summary>
    public float DeathExplosionDamage;
    public float DeathExplosionRadiusM;

    /// <summary>Gas cells: weight they hold up (kg). Jets: push (N). Missile seekers: what they home on.</summary>
    public float LiftKg;
    public float ThrustN;
    public SeekerKind Seeker;

    public float ColorR, ColorG, ColorB;
    public float Metallic = 0.6f;
    public float Roughness = 0.6f;

    public bool IsArmorLike => Kind == BlockKind.Armor || Kind == BlockKind.Frame;
    public bool ConductsAmmo => Kind == BlockKind.AmmoRack || Kind == BlockKind.AmmoFeed || Kind == BlockKind.TurretRing;
    public bool IsWheelLike => Kind == BlockKind.Wheel || Kind == BlockKind.SteerWheel || Kind == BlockKind.Track;
    public bool IsSeat => Kind == BlockKind.DriverSeat || Kind == BlockKind.GunnerSeat || Kind == BlockKind.CommanderSeat ||
                          Kind == BlockKind.DroneCore;
    public bool IsMissilePart => Kind is BlockKind.MissileSeeker or BlockKind.MissileWarhead or BlockKind.MissileMotor or BlockKind.MissileFins;
    public bool IsLiftingSurface => Kind is BlockKind.Wing or BlockKind.ControlSurface;
}

public static class BlockCatalog
{
    public static readonly string[] Categories = { "Structure", "Mobility", "Sea", "Air", "Control", "Weapons", "Ammo" };

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

    /// <summary>A quick-firing 40 mm: light hits, but a lot of them, and it points almost straight up.</summary>
    public static readonly BreechSpec AutoCannon40 = new()
    {
        CaliberMm = 40, ShellMassKg = 0.9f, BaseVelocity = 700, BasePenMm = 42,
        ApDamage = 55, HeDamage = 80, HeRadiusM = 1.4f, ReloadS = 0.3f, ShellUnits = 1, DragK = 0.00014f,
        MaxElevationDeg = 85f,
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
            Watertight = false,
        },

        // Mobility
        new BlockDef
        {
            Id = "engine_small", Name = "Small Engine", Category = "Mobility", Kind = BlockKind.Engine,
            Description = "150 kW diesel. Needs a fuel tank somewhere in the vehicle.",
            MassKg = 250, Hp = 100, ArmorMm = 10, PowerKw = 150,
            ColorR = 0.5f, ColorG = 0.3f, ColorB = 0.18f, Metallic = 0.7f, Roughness = 0.5f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "engine_large", Name = "Large Engine", Category = "Mobility", Kind = BlockKind.Engine,
            Description = "450 kW diesel. The heart of a heavy vehicle.",
            MassKg = 700, Hp = 200, ArmorMm = 15, PowerKw = 450,
            ColorR = 0.55f, ColorG = 0.28f, ColorB = 0.12f, Metallic = 0.7f, Roughness = 0.5f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "fuel_tank", Name = "Fuel Tank", Category = "Mobility", Kind = BlockKind.FuelTank,
            Description = "Holds 100 L of diesel. Burns and explodes when destroyed, so keep it behind armor.",
            MassKg = 120, Hp = 60, ArmorMm = 5, FuelL = 100,
            DeathExplosionDamage = 160, DeathExplosionRadiusM = 1.6f,
            ColorR = 0.6f, ColorG = 0.12f, ColorB = 0.1f, Metallic = 0.5f, Roughness = 0.55f,
            Watertight = false, Displacement = 1f, // a sealed tank of fuel, lighter than water
        },
        new BlockDef
        {
            Id = "wheel", Name = "Wheel", Category = "Mobility", Kind = BlockKind.Wheel,
            Description = "Driven wheel with suspension. Fast on flat ground. Put wheels on the bottom row with nothing underneath.",
            MassKg = 80, Hp = 90, ArmorMm = 10, WheelRadiusM = 0.42f, HasCollider = false,
            ColorR = 0.1f, ColorG = 0.1f, ColorB = 0.1f, Metallic = 0.1f, Roughness = 0.9f,
            Watertight = false, Displacement = 0.5f,
        },
        new BlockDef
        {
            Id = "wheel_steer", Name = "Steering Wheel", Category = "Mobility", Kind = BlockKind.SteerWheel,
            Description = "A wheel that turns for steering. Put these at the front.",
            MassKg = 90, Hp = 90, ArmorMm = 10, WheelRadiusM = 0.42f, HasCollider = false,
            ColorR = 0.12f, ColorG = 0.12f, ColorB = 0.14f, Metallic = 0.1f, Roughness = 0.9f,
            Watertight = false, Displacement = 0.5f,
        },
        new BlockDef
        {
            Id = "track", Name = "Track Segment", Category = "Mobility", Kind = BlockKind.Track,
            Description = "Tank track with a road wheel. Strong grip and skid steering; slower than wheels. Run them along both sides.",
            MassKg = 120, Hp = 140, ArmorMm = 20, WheelRadiusM = 0.3f, HasCollider = false,
            ColorR = 0.16f, ColorG = 0.15f, ColorB = 0.13f, Metallic = 0.6f, Roughness = 0.8f,
            Watertight = false, Displacement = 0.5f,
        },

        // Control
        new BlockDef
        {
            Id = "driver_seat", Name = "Driver Seat", Category = "Control", Kind = BlockKind.DriverSeat,
            Description = "Where you drive from. Every vehicle needs one. If it's destroyed, the vehicle is knocked out.",
            MassKg = 90, Hp = 80, ArmorMm = 5, ColorR = 0.2f, ColorG = 0.32f, ColorB = 0.5f,
            Metallic = 0.3f, Roughness = 0.6f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "gunner_seat", Name = "Gunner Seat", Category = "Control", Kind = BlockKind.GunnerSeat,
            Description = "Whoever sits here aims and fires the guns in the same turret (or the hull guns, if it's in the hull). Empty seats get an AI crew member, so you can drive while your gunner shoots.",
            MassKg = 90, Hp = 80, ArmorMm = 5, ColorR = 0.42f, ColorG = 0.26f, ColorB = 0.2f,
            Metallic = 0.3f, Roughness = 0.6f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "commander_seat", Name = "Commander Seat", Category = "Control", Kind = BlockKind.CommanderSeat,
            Description = "A periscope with strong zoom. A commander spots targets, so AI gunners find and track them much faster.",
            MassKg = 90, Hp = 80, ArmorMm = 5, ColorR = 0.24f, ColorG = 0.38f, ColorB = 0.3f,
            Metallic = 0.3f, Roughness = 0.6f,
            Watertight = false,
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
            Watertight = false,
        },
        new BlockDef
        {
            Id = "breech_76", Name = "76 mm Breech", Category = "Weapons", Kind = BlockKind.Breech, Breech = Medium76,
            Description = "Medium gun. A good all-rounder. Barrels go in front of it (the arrow side).",
            MassKg = 500, Hp = 200, ArmorMm = 40, ColorR = 0.22f, ColorG = 0.22f, ColorB = 0.2f,
            Metallic = 0.85f, Roughness = 0.35f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "breech_120", Name = "120 mm Breech", Category = "Weapons", Kind = BlockKind.Breech, Breech = Heavy120,
            Description = "Heavy gun. Huge damage and recoil, slow reload. Barrels go in front of it (the arrow side).",
            MassKg = 1200, Hp = 320, ArmorMm = 50, ColorR = 0.22f, ColorG = 0.22f, ColorB = 0.2f,
            Metallic = 0.85f, Roughness = 0.35f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "barrel", Name = "Barrel Segment", Category = "Weapons", Kind = BlockKind.Barrel,
            Description = "Each segment in a straight line in front of a breech adds velocity and penetration (with diminishing returns), but adds weight and slows the turret.",
            MassKg = 90, Hp = 90, ArmorMm = 20, HasCollider = false, ColorR = 0.2f, ColorG = 0.21f, ColorB = 0.19f,
            Metallic = 0.85f, Roughness = 0.35f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "muzzle_brake", Name = "Muzzle Brake", Category = "Weapons", Kind = BlockKind.MuzzleBrake,
            Description = "Goes on the end of the barrel. Cuts recoil by 40%.",
            MassKg = 70, Hp = 80, ArmorMm = 20, HasCollider = false, ColorR = 0.18f, ColorG = 0.18f, ColorB = 0.17f,
            Metallic = 0.85f, Roughness = 0.35f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "loader", Name = "Loader", Category = "Weapons", Kind = BlockKind.Loader,
            Description = "Touching a breech: about 22% faster reload each, up to 3.",
            MassKg = 150, Hp = 90, ArmorMm = 10, ColorR = 0.3f, ColorG = 0.4f, ColorB = 0.3f,
            Metallic = 0.6f, Roughness = 0.5f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "propellant", Name = "Propellant Charge", Category = "Weapons", Kind = BlockKind.Propellant,
            Description = "Touching a breech: +10% muzzle velocity and more recoil each, up to 2.",
            MassKg = 80, Hp = 60, ArmorMm = 5, DeathExplosionDamage = 120, DeathExplosionRadiusM = 1.2f,
            ColorR = 0.72f, ColorG = 0.55f, ColorB = 0.22f, Metallic = 0.8f, Roughness = 0.35f,
            Watertight = false,
        },

        // Ammo
        new BlockDef
        {
            Id = "ammo_ap", Name = "AP Ammo Rack", Category = "Ammo", Kind = BlockKind.AmmoRack, Ammo = AmmoType.AP,
            AmmoUnits = 24,
            Description = "Armor-piercing shells: punch through armor and wreck what's behind it. Must touch the breech or connect through feeds or other racks. Explodes if destroyed while loaded.",
            MassKg = 120, Hp = 70, ArmorMm = 5, ColorR = 0.7f, ColorG = 0.62f, ColorB = 0.3f,
            Metallic = 0.5f, Roughness = 0.5f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "ammo_he", Name = "HE Ammo Rack", Category = "Ammo", Kind = BlockKind.AmmoRack, Ammo = AmmoType.HE,
            AmmoUnits = 24,
            Description = "High-explosive shells: blast damage around the impact. Great against light armor and composite. Explodes if destroyed while loaded.",
            MassKg = 120, Hp = 70, ArmorMm = 5, ColorR = 0.65f, ColorG = 0.25f, ColorB = 0.18f,
            Metallic = 0.5f, Roughness = 0.5f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "ammo_torpedo", Name = "Torpedo Rack", Category = "Ammo", Kind = BlockKind.AmmoRack, Ammo = AmmoType.Torpedo,
            AmmoUnits = 16,
            Description = "Holds two torpedoes. Must touch a Torpedo Tube or connect to it through ammo feeds or other racks. Explodes if destroyed while loaded.",
            MassKg = 400, Hp = 90, ArmorMm = 5, ColorR = 0.32f, ColorG = 0.4f, ColorB = 0.46f, Metallic = 0.6f, Roughness = 0.45f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "ammo_feed", Name = "Ammo Feed", Category = "Ammo", Kind = BlockKind.AmmoFeed,
            Description = "Carries shells between racks and breeches. Use it to keep ammo deep inside the hull.",
            MassKg = 30, Hp = 50, ArmorMm = 5, HasCollider = false, ColorR = 0.45f, ColorG = 0.42f, ColorB = 0.3f,
            Metallic = 0.6f, Roughness = 0.5f,
            Watertight = false,
        },

        // Sea
        new BlockDef
        {
            Id = "ship_propeller", Name = "Ship Propeller", Category = "Sea", Kind = BlockKind.ShipPropeller,
            Description = "Pushes the ship the way its arrow points, using engine power. It only works under water, so put it below the waterline, usually at the stern.",
            MassKg = 200, Hp = 120, ArmorMm = 15, HasCollider = false, Watertight = false,
            ColorR = 0.72f, ColorG = 0.55f, ColorB = 0.25f, Metallic = 0.9f, Roughness = 0.3f,
        },
        new BlockDef
        {
            Id = "rudder", Name = "Rudder", Category = "Sea", Kind = BlockKind.Rudder,
            Description = "Steers the ship. Put it behind a propeller, below the waterline. Turned on its side (G) it becomes a dive plane, which helps a moving submarine change depth.",
            MassKg = 120, Hp = 120, ArmorMm = 15, HasCollider = false, Watertight = false,
            ColorR = 0.3f, ColorG = 0.32f, ColorB = 0.3f, Metallic = 0.7f, Roughness = 0.45f,
        },
        new BlockDef
        {
            Id = "ballast_valve", Name = "Ballast Valve", Category = "Sea", Kind = BlockKind.BallastValve,
            Description = "Makes the sealed space it touches a ballast tank: flood it to dive, blow it out to surface. Wall off empty compartments low in the hull and give each one a valve.",
            MassKg = 80, Hp = 80, ArmorMm = 20, ColorR = 0.25f, ColorG = 0.45f, ColorB = 0.55f, Metallic = 0.7f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "bilge_pump", Name = "Bilge Pump", Category = "Sea", Kind = BlockKind.BilgePump,
            Description = "Pumps flood water out of the hull. Pumps can't beat a big hole: weld the hole shut first, then they clear the water.",
            MassKg = 90, Hp = 60, ArmorMm = 5, ColorR = 0.2f, ColorG = 0.36f, ColorB = 0.6f, Metallic = 0.6f, Roughness = 0.45f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "lead_ballast", Name = "Lead Ballast", Category = "Sea", Kind = BlockKind.LeadBallast,
            Description = "Very heavy and compact. Low in the hull it keeps ships from rolling over, and it lets submarines sit at the right weight to dive.",
            MassKg = 1400, Hp = 400, ArmorMm = 30, ColorR = 0.36f, ColorG = 0.37f, ColorB = 0.4f, Metallic = 0.5f, Roughness = 0.7f,
        },
        new BlockDef
        {
            Id = "torpedo_tube", Name = "Torpedo Tube", Category = "Sea", Kind = BlockKind.TorpedoTube,
            Description = "Launches a torpedo the way its arrow points. Torpedoes run just under the surface and blow holes below the waterline. Feed it from Torpedo Racks.",
            MassKg = 600, Hp = 200, ArmorMm = 30, ColorR = 0.3f, ColorG = 0.33f, ColorB = 0.36f, Metallic = 0.8f, Roughness = 0.35f,
        },

        // Air
        new BlockDef
        {
            Id = "airframe", Name = "Airframe", Category = "Air", Kind = BlockKind.Armor,
            Description = "Light aluminium skin for fuselages and gondolas. Very light, but bullets go straight through. Sealed, so a flying boat's hull floats.",
            MassKg = 10, Hp = 45, ArmorMm = 4, ColorR = 0.62f, ColorG = 0.64f, ColorB = 0.62f, Metallic = 0.75f, Roughness = 0.35f,
        },
        new BlockDef
        {
            Id = "wing", Name = "Wing Panel", Category = "Air", Kind = BlockKind.Wing,
            Description = "Makes lift when air flows over it edge-on. Lay panels flat in rows for wings, stand them up (G) for a tail fin. More panels lift more, but the plane still needs speed: it stalls if it's too slow or pitched up too far.",
            MassKg = 5, Hp = 40, ArmorMm = 3, Watertight = false, ColorR = 0.55f, ColorG = 0.58f, ColorB = 0.52f, Metallic = 0.6f, Roughness = 0.45f,
        },
        new BlockDef
        {
            Id = "control_surface", Name = "Control Surface", Category = "Air", Kind = BlockKind.ControlSurface,
            Description = "A hinged panel that steers in the air. What it does depends on where it is: at the wingtips it rolls (aileron), on the tailplane it pitches (elevator), standing up on the tail it turns (rudder).",
            MassKg = 6, Hp = 40, ArmorMm = 3, Watertight = false, ColorR = 0.7f, ColorG = 0.52f, ColorB = 0.26f, Metallic = 0.6f, Roughness = 0.45f,
        },
        new BlockDef
        {
            Id = "air_propeller", Name = "Air Propeller", Category = "Air", Kind = BlockKind.AirPropeller,
            Description = "Pulls the aircraft the way its arrow points, using engine power. Strong at low speed, weaker the faster you go. Its blades sweep 1.3 m around it, so leave room.",
            MassKg = 60, Hp = 60, ArmorMm = 5, HasCollider = false, Watertight = false,
            ColorR = 0.2f, ColorG = 0.2f, ColorB = 0.2f, Metallic = 0.5f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "aero_engine", Name = "Aero Engine", Category = "Air", Kind = BlockKind.Engine,
            Description = "A light, powerful engine built for aircraft: 800 kW for half a tonne. Thirsty, and lightly protected.",
            MassKg = 520, Hp = 90, ArmorMm = 8, PowerKw = 800, Watertight = false,
            ColorR = 0.34f, ColorG = 0.33f, ColorB = 0.3f, Metallic = 0.75f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "jet_engine", Name = "Jet Engine", Category = "Air", Kind = BlockKind.JetEngine,
            Description = "Pushes the way its arrow points with 16 kN, all on its own: it needs fuel but no other engine. Keeps pushing at any speed, so jets are fast. Point it up and it lifts.",
            MassKg = 600, Hp = 120, ArmorMm = 10, ThrustN = 16000, Watertight = false,
            ColorR = 0.3f, ColorG = 0.3f, ColorB = 0.32f, Metallic = 0.85f, Roughness = 0.3f,
        },
        new BlockDef
        {
            Id = "rotor", Name = "Rotor", Category = "Air", Kind = BlockKind.Rotor,
            Description = "A helicopter rotor: lifts straight up along its arrow using engine power. The blades sweep 4.5 m around it, so keep the space clear. One big engine lifts about three tonnes.",
            MassKg = 250, Hp = 120, ArmorMm = 10, HasCollider = false, Watertight = false,
            ColorR = 0.22f, ColorG = 0.23f, ColorB = 0.22f, Metallic = 0.6f, Roughness = 0.45f,
        },
        new BlockDef
        {
            Id = "lift_fan", Name = "Lift Fan", Category = "Air", Kind = BlockKind.LiftFan,
            Description = "A ducted fan that pushes along its arrow using engine power. Point four of them up at the corners for a drone or VTOL; they steer it by pushing harder on one side.",
            MassKg = 50, Hp = 60, ArmorMm = 6, Watertight = false, ColorR = 0.28f, ColorG = 0.3f, ColorB = 0.3f, Metallic = 0.7f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "gas_cell", Name = "Gas Cell", Category = "Air", Kind = BlockKind.GasCell,
            Description = "A bag of lifting gas: each one holds up 20 kg. Fill a big envelope with them for an airship. Shot full of holes, they leak their lift away.",
            MassKg = 4, Hp = 25, ArmorMm = 1, LiftKg = 20, Watertight = false, Displacement = 1f,
            ColorR = 0.78f, ColorG = 0.76f, ColorB = 0.68f, Metallic = 0.05f, Roughness = 0.8f,
        },
        new BlockDef
        {
            Id = "drone_core", Name = "Drone Core", Category = "Control", Kind = BlockKind.DroneCore,
            Description = "Lets it be flown or driven by remote instead of from a seat. You fly it from wherever you're standing (look at it and press E), so losing it costs no one.",
            MassKg = 30, Hp = 40, ArmorMm = 5, Watertight = false, ColorR = 0.2f, ColorG = 0.45f, ColorB = 0.32f, Metallic = 0.6f, Roughness = 0.45f,
        },
        new BlockDef
        {
            Id = "breech_40ac", Name = "40 mm Autocannon", Category = "Weapons", Kind = BlockKind.Breech, Breech = AutoCannon40,
            Description = "A quick-firing 40 mm: about three rounds a second, and it can point almost straight up. Load it with Flak to shoot down aircraft.",
            MassKg = 120, Hp = 90, ArmorMm = 15, Watertight = false, ColorR = 0.24f, ColorG = 0.25f, ColorB = 0.22f, Metallic = 0.8f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "bomb_rack", Name = "Bomb Rack", Category = "Weapons", Kind = BlockKind.BombRack, AmmoUnits = 1,
            Description = "Carries one 250 kg bomb, hung underneath. Drop it with B: the diamond on screen shows where it will land. Blows up if shot while loaded.",
            MassKg = 290, Hp = 60, ArmorMm = 5, HasCollider = false, Watertight = false,
            ColorR = 0.3f, ColorG = 0.32f, ColorB = 0.28f, Metallic = 0.6f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "missile_rail", Name = "Missile Rail", Category = "Weapons", Kind = BlockKind.MissileRail,
            Description = "Launches the missile built in front of it (the arrow side): line up missile parts on it, a motor and a warhead at least, and a seeker on the nose to guide it. Fire with F. Missiles are rebuilt at supply points.",
            MassKg = 40, Hp = 60, ArmorMm = 8, Watertight = false, ColorR = 0.32f, ColorG = 0.33f, ColorB = 0.3f, Metallic = 0.7f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "seeker_optical", Name = "Optical Seeker", Category = "Weapons", Kind = BlockKind.MissileSeeker, Seeker = SeekerKind.Optical,
            Description = "Missile nose. The missile flies to wherever you keep the crosshair, so steer it all the way in. Works against anything.",
            MassKg = 15, Hp = 20, ArmorMm = 2, HasCollider = false, Watertight = false, ColorR = 0.25f, ColorG = 0.28f, ColorB = 0.32f, Metallic = 0.6f, Roughness = 0.3f,
        },
        new BlockDef
        {
            Id = "seeker_radar", Name = "Radar Seeker", Category = "Weapons", Kind = BlockKind.MissileSeeker, Seeker = SeekerKind.Radar,
            Description = "Missile nose. Locks on to the enemy aircraft nearest your crosshair and chases it on its own (fire and forget). Can't see ground targets.",
            MassKg = 18, Hp = 20, ArmorMm = 2, HasCollider = false, Watertight = false, ColorR = 0.85f, ColorG = 0.85f, ColorB = 0.8f, Metallic = 0.2f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "missile_warhead", Name = "Warhead", Category = "Weapons", Kind = BlockKind.MissileWarhead,
            Description = "Missile part: the bang. Each one adds damage and blast size, and weight.",
            MassKg = 35, Hp = 30, ArmorMm = 3, HasCollider = false, Watertight = false,
            DeathExplosionDamage = 260, DeathExplosionRadiusM = 2f,
            ColorR = 0.45f, ColorG = 0.42f, ColorB = 0.2f, Metallic = 0.5f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "missile_motor", Name = "Rocket Motor", Category = "Weapons", Kind = BlockKind.MissileMotor,
            Description = "Missile part: thrust. Each motor adds push and burns a little longer, for more speed and range.",
            MassKg = 30, Hp = 30, ArmorMm = 3, HasCollider = false, Watertight = false,
            DeathExplosionDamage = 90, DeathExplosionRadiusM = 1.2f,
            ColorR = 0.6f, ColorG = 0.6f, ColorB = 0.58f, Metallic = 0.5f, Roughness = 0.45f,
        },
        new BlockDef
        {
            Id = "missile_fins", Name = "Missile Fins", Category = "Weapons", Kind = BlockKind.MissileFins,
            Description = "Missile part: steering. More fins turn harder. A guided missile without fins can't turn at all.",
            MassKg = 6, Hp = 20, ArmorMm = 2, HasCollider = false, Watertight = false,
            ColorR = 0.4f, ColorG = 0.42f, ColorB = 0.4f, Metallic = 0.6f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "ammo_flak", Name = "Flak Rack", Category = "Ammo", Kind = BlockKind.AmmoRack, Ammo = AmmoType.Flak,
            AmmoUnits = 24,
            Description = "Air-burst shells for any gun: they go off when they pass close to an aircraft, or at the range you're aiming at, filling the sky with shrapnel. Explodes if destroyed while loaded.",
            MassKg = 120, Hp = 70, ArmorMm = 5, Watertight = false, ColorR = 0.35f, ColorG = 0.38f, ColorB = 0.42f, Metallic = 0.5f, Roughness = 0.5f,
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
