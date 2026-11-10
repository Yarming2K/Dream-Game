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

    /// <summary>Cosmetic parts (hatches, lights, stowage, railings): a little mass, no other effect.</summary>
    Detail,

    /// <summary>Screens and radios: from a vehicle or post with one, the tactical map commands your other units.</summary>
    CommandConsole,

    /// <summary>A search radar: everything aboard sees further.</summary>
    Radar,

    /// <summary>Makes electricity for energy weapons, burning fuel while it charges.</summary>
    Generator,

    /// <summary>Makes a great deal of electricity with no fuel. Goes up like a bomb if it's destroyed.</summary>
    Reactor,

    /// <summary>Stores charge for energy weapons.</summary>
    Capacitor,

    /// <summary>A vertical launch cell: one missile inside, fired straight up out of the deck.</summary>
    VlsCell,

    /// <summary>A sliding door, two cells wide and four high: it opens for people and shuts behind them.</summary>
    Door,

    /// <summary>A flight of steps up one cell: walkable.</summary>
    Stairs,

    /// <summary>A light fitting: lights up the space around it.</summary>
    Lamp,
}

/// <summary>What a vertical launch cell holds.</summary>
public enum VlsLoad
{
    None,

    /// <summary>A cruise missile that flies itself to a spot on the ground and hits it with a huge warhead.</summary>
    Strike,

    /// <summary>An anti-aircraft missile that locks on to an enemy aircraft and chases it.</summary>
    Sam,
}

public enum BlockShape
{
    Cube,

    /// <summary>
    /// Half a cube cut diagonally. Unrotated, the solid part is where local y &lt;= z, so the sloped
    /// face points forward and up: the shape of a tank's front plate.
    /// </summary>
    Wedge,

    /// <summary>The lower half of a slope half as steep as <see cref="Wedge"/> (it takes two blocks, this and <see cref="ShallowHigh"/>).</summary>
    ShallowLow,

    /// <summary>The upper half of a shallow slope.</summary>
    ShallowHigh,

    /// <summary>Where a front slope and a side slope meet round the outside of a corner.</summary>
    Corner,

    /// <summary>Where two slopes meet round the inside of a corner.</summary>
    InnerCorner,

    /// <summary>A quarter round: the front top edge rounded off.</summary>
    Round,

    /// <summary>The bottom half of a cube.</summary>
    Half,

    /// <summary>A quarter-cell plate against the back face.</summary>
    Plate,

    /// <summary>An eight-sided section running front to back.</summary>
    Octagon,
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

    /// <summary>What goes in front of it: null for ordinary barrels, or "rail", "lens" or "tube" (see <see cref="BlockDef.BarrelTag"/>).</summary>
    public string BarrelTag;

    /// <summary>Electric charge each shot takes from the vehicle's power, in megajoules (energy weapons).</summary>
    public float ChargeMJ;

    /// <summary>Railgun: fires solid slugs (from AP racks) with magnets.</summary>
    public bool Rail;

    /// <summary>Laser: a beam that burns the first thing it touches. No ammo at all.</summary>
    public bool Beam;

    /// <summary>Rockets: each launch tube in front adds two rockets to the salvo, fired one after another.</summary>
    public bool Rocket;

    /// <summary>Point defense: its gunners shoot down missiles and bombs coming at their vehicle before anything else.</summary>
    public bool PointDefense;

    /// <summary>Rounds fired for each one taken from the rack (a gatling fires a burst).</summary>
    public int Burst = 1;

    /// <summary>Lasers: how far the beam does full damage before it spreads out (lenses add to it).</summary>
    public float BeamRangeM;

    /// <summary>What the weapon is called instead of its caliber ("Railgun").</summary>
    public string Label;
}

public sealed class BlockDef
{
    public string Id;
    public string Name;
    public string Category;
    public string Description;
    public BlockKind Kind;
    public BlockShape Shape = BlockShape.Cube;

    /// <summary>How many cells it fills in each direction of its own frame. Big blocks share one health pool.</summary>
    public Int3 Size = new(1, 1, 1);
    public int Volume => Size.X * Size.Y * Size.Z;
    public bool IsMultiCell => Volume > 1;

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
        get => _displacement >= 0 ? _displacement : Math.Clamp(MassKg / Volume / (SteelDensity * CellVolume), 0f, 1f);
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

    /// <summary>
    /// For a block that comes in several shapes (armor, glazing): the id of its plain cube version. The
    /// builder shows only that one in its list, and cycles through the shapes with a key.
    /// </summary>
    public string BaseId;

    /// <summary>Glass: drawn as framed panes (cockpits, bridges, windows).</summary>
    public bool Glass;

    /// <summary>Barrel-like blocks: the kind of breech they go on, matching <see cref="BreechSpec.BarrelTag"/> (null for gun barrels).</summary>
    public string BarrelTag;

    /// <summary>Generators and reactors: the electricity they make, in kW.</summary>
    public float ElectricKw;

    /// <summary>Capacitor Banks: the charge they hold, in MJ.</summary>
    public float StoreMJ;

    /// <summary>VLS cells: the missile inside.</summary>
    public VlsLoad Vls;
    public bool ConductsAmmo => Kind == BlockKind.AmmoRack || Kind == BlockKind.AmmoFeed || Kind == BlockKind.TurretRing;
    public bool IsWheelLike => Kind == BlockKind.Wheel || Kind == BlockKind.SteerWheel || Kind == BlockKind.Track;
    public bool IsSeat => Kind == BlockKind.DriverSeat || Kind == BlockKind.GunnerSeat || Kind == BlockKind.CommanderSeat ||
                          Kind == BlockKind.DroneCore;
    public bool IsMissilePart => Kind is BlockKind.MissileSeeker or BlockKind.MissileWarhead or BlockKind.MissileMotor or BlockKind.MissileFins;
    public bool IsLiftingSurface => Kind is BlockKind.Wing or BlockKind.ControlSurface;
}

public static class BlockCatalog
{
    public static readonly string[] Categories = { "Structure", "Mobility", "Sea", "Air", "Control", "Weapons", "Ammo", "Power", "Interior", "Details" };

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

    /// <summary>A cruiser's gun. Needs a long barrel (12 or so heavy segments) to reach full power.</summary>
    public static readonly BreechSpec Heavy203 = new()
    {
        CaliberMm = 203, ShellMassKg = 120f, BaseVelocity = 420, BasePenMm = 150,
        ApDamage = 900, HeDamage = 1400, HeRadiusM = 5f, ReloadS = 15f, ShellUnits = 24, DragK = 0.00003f,
        MaxElevationDeg = 40f,
    };

    /// <summary>A battleship's main gun: a tonne of shell, from a breech three cells across.</summary>
    public static readonly BreechSpec Naval406 = new()
    {
        CaliberMm = 406, ShellMassKg = 1200f, BaseVelocity = 450, BasePenMm = 270,
        ApDamage = 3200, HeDamage = 4800, HeRadiusM = 9f, ReloadS = 30f, ShellUnits = 96, DragK = 0.000015f,
        MaxElevationDeg = 40f,
    };

    /// <summary>A quick-firing 40 mm: light hits, but a lot of them, and it points almost straight up.</summary>
    public static readonly BreechSpec AutoCannon40 = new()
    {
        CaliberMm = 40, ShellMassKg = 0.9f, BaseVelocity = 700, BasePenMm = 42,
        ApDamage = 55, HeDamage = 80, HeRadiusM = 1.4f, ReloadS = 0.3f, ShellUnits = 1, DragK = 0.00014f,
        MaxElevationDeg = 85f,
    };

    /// <summary>
    /// Railgun: a 40 mm slug thrown by magnets at well over 2 km/s. It flies flat and hits as hard as a big gun,
    /// but every shot drains 20 MJ from the vehicle's power.
    /// </summary>
    public static readonly BreechSpec Rail40 = new()
    {
        CaliberMm = 40, ShellMassKg = 5f, BaseVelocity = 1400, BasePenMm = 180,
        ApDamage = 380, HeDamage = 0, HeRadiusM = 0, ReloadS = 4f, ShellUnits = 2, DragK = 0.00002f,
        MaxElevationDeg = 35f, BarrelTag = "rail", ChargeMJ = 20f, Rail = true, Label = "Railgun",
    };

    /// <summary>Laser: ten pulses a second, each burning the first block the beam touches. Takes 5 MW while it fires.</summary>
    public static readonly BreechSpec Laser = new()
    {
        CaliberMm = 12, ShellMassKg = 0, BaseVelocity = 100000, BasePenMm = 1,
        ApDamage = 22, ReloadS = 0.1f, ShellUnits = 1, MaxElevationDeg = 88f,
        BarrelTag = "lens", ChargeMJ = 0.5f, Beam = true, PointDefense = true, BeamRangeM = 1000f, Label = "Laser",
    };

    /// <summary>Close-in weapon system: a 20 mm gatling firing bursts of five, for shooting down missiles and bombs.</summary>
    public static readonly BreechSpec Gatling20 = new()
    {
        CaliberMm = 20, ShellMassKg = 0.1f, BaseVelocity = 700, BasePenMm = 28,
        ApDamage = 22, HeDamage = 30, HeRadiusM = 0.8f, ReloadS = 0.1f, ShellUnits = 1, DragK = 0.0002f,
        MaxElevationDeg = 88f, Burst = 5, PointDefense = true, Label = "20 mm CIWS",
    };

    /// <summary>Rocket artillery: unguided 122 mm rockets, rippled off in salvos at targets kilometers away.</summary>
    public static readonly BreechSpec Rocket122 = new()
    {
        CaliberMm = 122, ShellMassKg = 60f, BaseVelocity = 330, BasePenMm = 30,
        ApDamage = 0, HeDamage = 560, HeRadiusM = 5f, ReloadS = 12f, ShellUnits = 2, DragK = 0.00003f,
        MaxElevationDeg = 55f, BarrelTag = "tube", Rocket = true, Label = "Rockets",
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
            Id = "glazing", Name = "Glazing", Category = "Structure", Kind = BlockKind.Armor, Glass = true,
            Description = "Armored glass in a frame: cockpits, bridges and windows. Comes in every armor shape (press V), so a canopy can be built pane by pane. Stops splinters, not shells.",
            MassKg = 16, Hp = 40, ArmorMm = 8, ColorR = 0.08f, ColorG = 0.11f, ColorB = 0.13f, Metallic = 0.55f, Roughness = 0.12f,
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
            Id = "pilot_seat", Name = "Pilot Seat", Category = "Control", Kind = BlockKind.DriverSeat,
            Description = "An open seat with a control stick and instruments, made to sit under a canopy: build the canopy round it from glazing (press V for slopes and corners). Works like a driver seat.",
            MassKg = 70, Hp = 70, ArmorMm = 3, ColorR = 0.22f, ColorG = 0.24f, ColorB = 0.25f, Metallic = 0.4f, Roughness = 0.6f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "command_console", Name = "Command Console", Category = "Control", Kind = BlockKind.CommandConsole,
            Description = "Map tables, screens and radios. Aboard a vehicle with one (or standing at a command post), press M for the tactical map: pick units and give them orders. Orders reach 4 km. If it's destroyed, your units carry on with their last orders.",
            MassKg = 120, Hp = 90, ArmorMm = 5, ColorR = 0.22f, ColorG = 0.24f, ColorB = 0.26f, Metallic = 0.4f, Roughness = 0.55f,
            Watertight = false,
        },
        new BlockDef
        {
            Id = "radar", Name = "Search Radar", Category = "Control", Kind = BlockKind.Radar,
            Description = "A radar mast. Everything aboard spots enemies nearly twice as far, which shows more of them on the tactical map.",
            MassKg = 80, Hp = 50, ArmorMm = 3, HasCollider = false, ColorR = 0.5f, ColorG = 0.52f, ColorB = 0.53f, Metallic = 0.5f, Roughness = 0.5f,
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


        // Big blocks, for machines of scale. Each fills a box of cells and shares one health pool.
        new BlockDef
        {
            Id = "hull_plate", Name = "Hull Plate (4×1×4)", Category = "Structure", Kind = BlockKind.Armor, Size = new Int3(4, 1, 4),
            Description = "Sixteen cells of light plate in one piece, for laying big hulls, decks and floors quickly. Same protection as Light Armor.",
            MassKg = 960, Hp = 1500, ArmorMm = 25, ColorR = 0.42f, ColorG = 0.45f, ColorB = 0.38f,
        },
        new BlockDef
        {
            Id = "armor_block", Name = "Heavy Armor Block (2×2×2)", Category = "Structure", Kind = BlockKind.Armor, Size = new Int3(2, 2, 2),
            Description = "A meter-thick cube of rolled steel: 160 mm, twice heavy armor, with eight times the health.",
            MassKg = 1760, Hp = 2600, ArmorMm = 160, ColorR = 0.33f, ColorG = 0.36f, ColorB = 0.31f,
        },
        new BlockDef
        {
            Id = "belt_armor", Name = "Belt Armor (4×4×2)", Category = "Structure", Kind = BlockKind.Armor, Size = new Int3(4, 4, 2),
            Description = "Battleship armor: a 2×2 m slab a meter deep, 340 mm thick. Stops anything short of a battleship's own guns.",
            MassKg = 9800, Hp = 11000, ArmorMm = 340, ColorR = 0.3f, ColorG = 0.32f, ColorB = 0.3f,
        },
        new BlockDef
        {
            Id = "turbine", Name = "Marine Turbine (2×2×3)", Category = "Mobility", Kind = BlockKind.Engine, Size = new Int3(2, 2, 3),
            Description = "6,000 kW of steam turbine for big ships and land battleships. Twelve of these push a 300 m battleship along.",
            MassKg = 12000, Hp = 1400, ArmorMm = 20, PowerKw = 6000, Watertight = false,
            ColorR = 0.4f, ColorG = 0.38f, ColorB = 0.33f, Metallic = 0.8f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "fuel_bunker", Name = "Fuel Bunker (2×2×2)", Category = "Mobility", Kind = BlockKind.FuelTank, Size = new Int3(2, 2, 2),
            Description = "2,000 L of fuel oil. Burns and explodes when destroyed.",
            MassKg = 1800, Hp = 600, ArmorMm = 8, FuelL = 2000, DeathExplosionDamage = 700, DeathExplosionRadiusM = 4f,
            Watertight = false, Displacement = 1f, ColorR = 0.55f, ColorG = 0.12f, ColorB = 0.1f, Metallic = 0.5f, Roughness = 0.55f,
        },
        new BlockDef
        {
            Id = "heavy_track", Name = "Heavy Track (1×3×3)", Category = "Mobility", Kind = BlockKind.Track, Size = new Int3(1, 3, 3),
            Description = "A 1.5 m road wheel and track for land battleships. Run long lines of them down both sides.",
            MassKg = 2400, Hp = 1600, ArmorMm = 40, WheelRadiusM = 0.7f, HasCollider = false, Watertight = false, Displacement = 0.5f,
            ColorR = 0.16f, ColorG = 0.15f, ColorB = 0.13f, Metallic = 0.6f, Roughness = 0.8f,
        },
        new BlockDef
        {
            Id = "great_screw", Name = "Great Screw (3×3×1)", Category = "Sea", Kind = BlockKind.ShipPropeller, Size = new Int3(3, 3, 1),
            Description = "A battleship's propeller, 1.5 m across. Like a Ship Propeller, it pushes the way its arrow points, only under water.",
            MassKg = 3000, Hp = 900, ArmorMm = 25, HasCollider = false, Watertight = false,
            ColorR = 0.72f, ColorG = 0.55f, ColorB = 0.25f, Metallic = 0.9f, Roughness = 0.3f,
        },
        new BlockDef
        {
            Id = "great_rudder", Name = "Great Rudder (1×3×3)", Category = "Sea", Kind = BlockKind.Rudder, Size = new Int3(1, 3, 3),
            Description = "A big ship's rudder. Put it behind the screws, below the waterline.",
            MassKg = 2600, Hp = 1000, ArmorMm = 30, HasCollider = false, Watertight = false,
            ColorR = 0.3f, ColorG = 0.32f, ColorB = 0.3f, Metallic = 0.7f, Roughness = 0.45f,
        },
        new BlockDef
        {
            Id = "breech_203", Name = "203 mm Breech", Category = "Weapons", Kind = BlockKind.Breech, Breech = Heavy203,
            Description = "A cruiser's gun. Give it a long run of Heavy Barrels (a dozen or so) and feed it from Shell Magazines.",
            MassKg = 6000, Hp = 900, ArmorMm = 60, Watertight = false, ColorR = 0.22f, ColorG = 0.22f, ColorB = 0.2f,
            Metallic = 0.8f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "breech_406", Name = "406 mm Breech (3×3×3)", Category = "Weapons", Kind = BlockKind.Breech, Breech = Naval406, Size = new Int3(3, 3, 3),
            Description = "A battleship's main gun. Heavy Barrels line up from the middle of its front face; it wants twenty or so. Reloads in 30 s; each shell weighs a tonne.",
            MassKg = 60000, Hp = 6000, ArmorMm = 80, Watertight = false, ColorR = 0.22f, ColorG = 0.22f, ColorB = 0.2f,
            Metallic = 0.8f, Roughness = 0.4f,
        },
        new BlockDef
        {
            Id = "heavy_barrel", Name = "Heavy Barrel", Category = "Weapons", Kind = BlockKind.Barrel,
            Description = "A barrel segment for the big guns: much heavier than an ordinary one, and drawn as thick as the gun it belongs to.",
            MassKg = 2500, Hp = 700, ArmorMm = 60, HasCollider = false, Watertight = false,
            ColorR = 0.2f, ColorG = 0.21f, ColorB = 0.19f, Metallic = 0.85f, Roughness = 0.35f,
        },
        new BlockDef
        {
            Id = "magazine_ap", Name = "AP Shell Magazine (2×2×2)", Category = "Ammo", Kind = BlockKind.AmmoRack, Ammo = AmmoType.AP, Size = new Int3(2, 2, 2),
            AmmoUnits = 768,
            Description = "A big gun's armor-piercing shells: 32 for a 203 mm, 8 for a 406 mm. Keep it deep under the armor: if it goes up, so does the ship.",
            MassKg = 6000, Hp = 600, ArmorMm = 10, Watertight = false, ColorR = 0.7f, ColorG = 0.62f, ColorB = 0.3f,
            Metallic = 0.5f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "magazine_he", Name = "HE Shell Magazine (2×2×2)", Category = "Ammo", Kind = BlockKind.AmmoRack, Ammo = AmmoType.HE, Size = new Int3(2, 2, 2),
            AmmoUnits = 768,
            Description = "A big gun's high-explosive shells: 32 for a 203 mm, 8 for a 406 mm.",
            MassKg = 6000, Hp = 600, ArmorMm = 10, Watertight = false, ColorR = 0.65f, ColorG = 0.25f, ColorB = 0.18f,
            Metallic = 0.5f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "ballast_block", Name = "Ballast Block (2×2×2)", Category = "Sea", Kind = BlockKind.LeadBallast, Size = new Int3(2, 2, 2),
            Description = "Eight cells of lead: 11 t. Big ships carry a bed of these along the keel to sit low and steady in the water.",
            MassKg = 11200, Hp = 3200, ArmorMm = 30, ColorR = 0.36f, ColorG = 0.37f, ColorB = 0.4f, Metallic = 0.5f, Roughness = 0.7f,
        },
        new BlockDef
        {
            Id = "gas_envelope", Name = "Gas Envelope (4×4×4)", Category = "Air", Kind = BlockKind.GasCell, Size = new Int3(4, 4, 4),
            Description = "64 gas cells' worth in one bag: holds up 1.3 t. Stack them for giant airships.",
            MassKg = 256, Hp = 900, ArmorMm = 1, LiftKg = 1280, Watertight = false, Displacement = 1f,
            ColorR = 0.78f, ColorG = 0.76f, ColorB = 0.68f, Metallic = 0.05f, Roughness = 0.8f,
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

        // Power
        new BlockDef
        {
            Id = "generator", Name = "Generator", Category = "Power", Kind = BlockKind.Generator,
            Description = "Makes 1.5 MW of electricity for railguns and lasers, burning fuel while it charges. The charge is kept in Capacitor Banks.",
            MassKg = 400, Hp = 110, ArmorMm = 10, ElectricKw = 1500,
            ColorR = 0.36f, ColorG = 0.38f, ColorB = 0.42f, Metallic = 0.7f, Roughness = 0.45f, Watertight = false,
        },
        new BlockDef
        {
            Id = "capacitor", Name = "Capacitor Bank", Category = "Power", Kind = BlockKind.Capacitor,
            Description = "Holds 12 MJ of charge. A railgun takes a big gulp every shot, so a few of these let it fire as fast as it reloads. Discharges with a bang if it's destroyed.",
            MassKg = 220, Hp = 70, ArmorMm = 8, StoreMJ = 12, DeathExplosionDamage = 150, DeathExplosionRadiusM = 1.5f,
            ColorR = 0.2f, ColorG = 0.3f, ColorB = 0.42f, Metallic = 0.6f, Roughness = 0.4f, Watertight = false,
        },
        new BlockDef
        {
            Id = "reactor", Name = "Fusion Reactor (3×3×3)", Category = "Power", Kind = BlockKind.Reactor, Size = new Int3(3, 3, 3),
            Description = "Makes 40 MW of electricity with no fuel at all: enough to keep lasers burning and railguns firing. Bury it deep and armor it well: if it's destroyed it goes up in an enormous blast.",
            MassKg = 22000, Hp = 1400, ArmorMm = 60, ElectricKw = 40000, DeathExplosionDamage = 9000, DeathExplosionRadiusM = 26f,
            ColorR = 0.3f, ColorG = 0.33f, ColorB = 0.38f, Metallic = 0.8f, Roughness = 0.35f,
        },

        // Modern and future weapons
        new BlockDef
        {
            Id = "rail_breech", Name = "Railgun Breech", Category = "Weapons", Kind = BlockKind.Breech, Breech = Rail40,
            Description = "Throws 40 mm slugs at over 2 km/s with magnets: flat, fast and very hard-hitting. Put Rail Segments in front (the arrow side) and feed it slugs from AP racks. Every shot takes 20 MJ, so it needs a Generator or Reactor and Capacitor Banks.",
            MassKg = 900, Hp = 220, ArmorMm = 40, Watertight = false,
            ColorR = 0.2f, ColorG = 0.22f, ColorB = 0.26f, Metallic = 0.85f, Roughness = 0.3f,
        },
        new BlockDef
        {
            Id = "rail", Name = "Rail Segment", Category = "Weapons", Kind = BlockKind.Barrel, BarrelTag = "rail",
            Description = "Goes in a straight line in front of a Railgun Breech. Each one adds speed and punch, like barrels on a gun.",
            MassKg = 110, Hp = 100, ArmorMm = 20, HasCollider = false, Watertight = false,
            ColorR = 0.22f, ColorG = 0.23f, ColorB = 0.27f, Metallic = 0.85f, Roughness = 0.3f,
        },
        new BlockDef
        {
            Id = "laser_emitter", Name = "Laser Emitter", Category = "Weapons", Kind = BlockKind.Breech, Breech = Laser,
            Description = "A beam that burns through whatever it touches at the speed of light: no drop, no lead, no ammo. Needs a Focusing Lens in front and a lot of power (5 MW while it fires). Its gunners burn incoming missiles and bombs out of the sky first.",
            MassKg = 450, Hp = 120, ArmorMm = 20, Watertight = false,
            ColorR = 0.3f, ColorG = 0.32f, ColorB = 0.36f, Metallic = 0.75f, Roughness = 0.35f,
        },
        new BlockDef
        {
            Id = "laser_lens", Name = "Focusing Lens", Category = "Weapons", Kind = BlockKind.Barrel, BarrelTag = "lens",
            Description = "Goes in front of a Laser Emitter. Each one focuses the beam further, so it keeps its full strength out to a longer range.",
            MassKg = 40, Hp = 50, ArmorMm = 10, HasCollider = false, Watertight = false,
            ColorR = 0.32f, ColorG = 0.34f, ColorB = 0.38f, Metallic = 0.7f, Roughness = 0.3f,
        },
        new BlockDef
        {
            Id = "breech_20gat", Name = "20 mm Gatling (CIWS)", Category = "Weapons", Kind = BlockKind.Breech, Breech = Gatling20,
            Description = "A spinning gun firing fifty rounds a second, made for shooting down missiles and bombs: its gunners go after anything incoming first. Put a barrel or three in front.",
            MassKg = 280, Hp = 100, ArmorMm = 15, Watertight = false,
            ColorR = 0.8f, ColorG = 0.8f, ColorB = 0.78f, Metallic = 0.4f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "rocket_pod", Name = "Rocket Launcher", Category = "Weapons", Kind = BlockKind.Breech, Breech = Rocket122,
            Description = "Rocket artillery. Put Launch Tubes in front of it (the arrow side): each adds two rockets to a salvo. Feed it from HE racks, aim at the ground kilometers away, and it ripples off the whole salvo.",
            MassKg = 350, Hp = 120, ArmorMm = 15, Watertight = false,
            ColorR = 0.3f, ColorG = 0.32f, ColorB = 0.27f, Metallic = 0.6f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "rocket_tube", Name = "Launch Tubes", Category = "Weapons", Kind = BlockKind.Barrel, BarrelTag = "tube",
            Description = "A pack of two rocket tubes, in a line in front of a Rocket Launcher.",
            MassKg = 70, Hp = 60, ArmorMm = 6, HasCollider = false, Watertight = false,
            ColorR = 0.33f, ColorG = 0.35f, ColorB = 0.3f, Metallic = 0.5f, Roughness = 0.55f,
        },
        new BlockDef
        {
            Id = "vls_strike", Name = "VLS Cell: Strike Missile", Category = "Weapons", Kind = BlockKind.VlsCell, Vls = VlsLoad.Strike, AmmoUnits = 1,
            Description = "A cruise missile in a deck cell, launched straight up. It flies itself to the spot under your crosshair when you fire (up to about 12 km), skims in low and lands a huge warhead. Nothing may sit on its hatch. Fire with F.",
            MassKg = 900, Hp = 90, ArmorMm = 12, Watertight = false,
            ColorR = 0.36f, ColorG = 0.38f, ColorB = 0.38f, Metallic = 0.6f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "vls_sam", Name = "VLS Cell: Air-Defense Missile", Category = "Weapons", Kind = BlockKind.VlsCell, Vls = VlsLoad.Sam, AmmoUnits = 1,
            Description = "An anti-aircraft missile in a deck cell, launched straight up. It locks on to the enemy aircraft nearest your crosshair and runs it down on its own. Nothing may sit on its hatch. Fire with F.",
            MassKg = 260, Hp = 70, ArmorMm = 12, Watertight = false,
            ColorR = 0.36f, ColorG = 0.38f, ColorB = 0.38f, Metallic = 0.6f, Roughness = 0.5f,
        },

        // Interior
        new BlockDef
        {
            Id = "door", Name = "Door (2×4)", Category = "Interior", Kind = BlockKind.Door, Size = new Int3(2, 4, 1),
            Description = "A sliding door 1 m wide and 2 m tall, for getting about inside ships, bunkers and big machines. It opens by itself when someone walks up to it and shuts behind them. Watertight when shut, so it can sit in a bulkhead.",
            MassKg = 180, Hp = 180, ArmorMm = 12, HasCollider = false, ColorR = 0.4f, ColorG = 0.42f, ColorB = 0.44f, Metallic = 0.7f, Roughness = 0.45f,
        },
        new BlockDef
        {
            Id = "stairs", Name = "Stairs", Category = "Interior", Kind = BlockKind.Stairs, Shape = BlockShape.Wedge,
            Description = "A flight of four steps rising one cell, toward the back. Put them in a line, each one up and back from the last, for a staircase between decks.",
            MassKg = 30, Hp = 60, ArmorMm = 5, Watertight = false, ColorR = 0.36f, ColorG = 0.37f, ColorB = 0.38f, Metallic = 0.7f, Roughness = 0.5f,
        },
        new BlockDef
        {
            Id = "lamp", Name = "Lamp", Category = "Interior", Kind = BlockKind.Lamp,
            Description = "A caged light fitting. Unturned it hangs from the ceiling; turn it to fix it to a wall. It lights the space around it when you're near.",
            MassKg = 4, Hp = 15, ArmorMm = 2, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.3f, ColorG = 0.31f, ColorB = 0.3f, Metallic = 0.6f, Roughness = 0.5f,
        },

        // Details
        new BlockDef
        {
            Id = "hatch", Name = "Hatch", Category = "Details", Kind = BlockKind.Detail,
            Description = "A crew hatch with a hinged lid. Purely for looks: put them where the crew would climb in.",
            MassKg = 25, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.3f, ColorG = 0.32f, ColorB = 0.27f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "periscope", Name = "Periscope", Category = "Details", Kind = BlockKind.Detail,
            Description = "A vision block for the crew to see out of. For looks.",
            MassKg = 12, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.25f, ColorG = 0.27f, ColorB = 0.24f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "headlight", Name = "Headlight", Category = "Details", Kind = BlockKind.Detail,
            Description = "A lamp in a wire guard, facing forward. For looks.",
            MassKg = 6, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.3f, ColorG = 0.3f, ColorB = 0.3f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "toolbox", Name = "Stowage Box", Category = "Details", Kind = BlockKind.Detail,
            Description = "A box for tools and kit, bolted to the outside. For looks.",
            MassKg = 30, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.33f, ColorG = 0.36f, ColorB = 0.27f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "spare_tracks", Name = "Spare Track Links", Category = "Details", Kind = BlockKind.Detail,
            Description = "Spare links hung on the armor. For looks (real crews hoped they'd stop a shell).",
            MassKg = 40, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.2f, ColorG = 0.2f, ColorB = 0.2f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "antenna", Name = "Antenna", Category = "Details", Kind = BlockKind.Detail,
            Description = "A whip antenna on an insulated base. For looks.",
            MassKg = 4, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.2f, ColorG = 0.2f, ColorB = 0.2f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "railing", Name = "Railing", Category = "Details", Kind = BlockKind.Detail,
            Description = "A guard rail along the back edge of the cell: decks, walkways, bridges. For looks.",
            MassKg = 8, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.55f, ColorG = 0.57f, ColorB = 0.58f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "ladder", Name = "Ladder", Category = "Interior", Kind = BlockKind.Detail,
            Description = "Rungs against the back face: walk up to it and push forward to climb, back to climb down, Space to let go. Stack them for a tall climb, inside or out.",
            MassKg = 10, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.5f, ColorG = 0.52f, ColorB = 0.53f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "smoke_launcher", Name = "Smoke Launchers", Category = "Details", Kind = BlockKind.Detail,
            Description = "A rack of three smoke dischargers. For looks.",
            MassKg = 18, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.3f, ColorG = 0.32f, ColorB = 0.27f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "exhaust", Name = "Exhaust", Category = "Details", Kind = BlockKind.Detail,
            Description = "A muffler with a heat shield and a pipe. For looks.",
            MassKg = 15, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.25f, ColorG = 0.24f, ColorB = 0.22f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "searchlight", Name = "Searchlight", Category = "Details", Kind = BlockKind.Detail,
            Description = "A big lamp on a turning yoke: ships, towers, airfields. For looks.",
            MassKg = 30, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.45f, ColorG = 0.47f, ColorB = 0.48f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "vent", Name = "Vent Grille", Category = "Details", Kind = BlockKind.Detail,
            Description = "Louvers over an engine or a compartment. For looks.",
            MassKg = 8, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.22f, ColorG = 0.22f, ColorB = 0.22f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "grab_handles", Name = "Grab Handles", Category = "Details", Kind = BlockKind.Detail,
            Description = "Handles for the crew to hang on to. For looks.",
            MassKg = 3, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.5f, ColorG = 0.5f, ColorB = 0.5f, Metallic = 0.5f, Roughness = 0.6f,
        },
        new BlockDef
        {
            Id = "stowage_rack", Name = "Stowage Rack", Category = "Details", Kind = BlockKind.Detail,
            Description = "A rack with bedrolls and kit bags. For looks.",
            MassKg = 25, Hp = 25, ArmorMm = 3, HasCollider = false, Watertight = false, Displacement = 0f,
            ColorR = 0.28f, ColorG = 0.3f, ColorB = 0.22f, Metallic = 0.5f, Roughness = 0.6f,
        },
    };

    /// <summary>The shapes armor and glazing come in, in the order the builder cycles through them, and their id suffixes.</summary>
    public static readonly (BlockShape Shape, string Suffix)[] ShapeVariants =
    {
        (BlockShape.Wedge, "slope"), (BlockShape.ShallowLow, "shallow"), (BlockShape.ShallowHigh, "shallow_high"),
        (BlockShape.Corner, "corner"), (BlockShape.InnerCorner, "inner_corner"), (BlockShape.Round, "round"),
        (BlockShape.Half, "half"), (BlockShape.Plate, "plate"), (BlockShape.Octagon, "octagon"),
    };

    private static readonly string[] ShapedBases = { "armor_light", "armor_heavy", "armor_composite", "glazing", "airframe" };

    // After the lists above: static fields are set up in the order they're written.
    private static readonly Dictionary<string, BlockDef> ById = BuildIndex();

    private static string ShapeHint(BlockShape s) => s switch
    {
        BlockShape.Wedge => "Angled armor counts as thicker, and steep hits can bounce off.",
        BlockShape.ShallowLow => "The low half of a long, shallow slope: put the high half behind it. Half as steep as a plain slope, so it counts as even thicker.",
        BlockShape.ShallowHigh => "The high half of a long, shallow slope: put the low half in front of it.",
        BlockShape.Corner => "Joins a front slope to a side slope round the outside of a corner.",
        BlockShape.InnerCorner => "Joins two slopes round the inside of a corner.",
        BlockShape.Round => "A rounded edge, for hulls and turrets that curve.",
        BlockShape.Half => "Half height: steps, decks and low walls.",
        BlockShape.Plate => "A quarter-cell plate: skirts, gun shields, fins and bulkheads. Same thickness of armor, a quarter of the weight.",
        BlockShape.Octagon => "An eight-sided section: pipes, funnels and round hulls.",
        _ => "",
    };

    private static void AddShapeVariants()
    {
        foreach (string id in ShapedBases)
        {
            BlockDef b = Defs.Find(d => d.Id == id);
            if (b == null) continue;
            b.BaseId = id;
            foreach ((BlockShape shape, string suffix) in ShapeVariants)
            {
                string vid = $"{id}_{suffix}";
                BlockDef existing = Defs.Find(d => d.Id == vid);
                if (existing != null)
                {
                    existing.BaseId = id;
                    continue;
                }

                float vol = Shapes.Volume(shape);
                Defs.Add(new BlockDef
                {
                    Id = vid, Name = $"{b.Name}: {Shapes.NameOf(shape).ToLowerInvariant()}", Category = b.Category, Kind = b.Kind, Shape = shape,
                    BaseId = id, Glass = b.Glass, Description = $"{b.Description.Split('.')[0]}, shaped. {ShapeHint(shape)}",
                    MassKg = MathF.Round(b.MassKg * vol * 1.05f), Hp = MathF.Round(b.Hp * MathF.Max(vol, 0.35f)), ArmorMm = b.ArmorMm,
                    HeVulnerability = b.HeVulnerability, Watertight = b.Watertight,
                    ColorR = b.ColorR, ColorG = b.ColorG, ColorB = b.ColorB, Metallic = b.Metallic, Roughness = b.Roughness,
                });
            }
        }
    }

    private static Dictionary<string, BlockDef> BuildIndex()
    {
        AddShapeVariants();
        var map = new Dictionary<string, BlockDef>(StringComparer.Ordinal);
        foreach (BlockDef d in Defs) map.Add(d.Id, d);
        return map;
    }

    /// <summary>Every shape a block comes in, plain cube first (just the block itself if it has no shapes).</summary>
    public static List<BlockDef> VariantsOf(BlockDef def)
    {
        if (def.BaseId == null) return new List<BlockDef> { def };
        var list = new List<BlockDef> { Get(def.BaseId) };
        foreach ((_, string suffix) in ShapeVariants)
            if (TryGet($"{def.BaseId}_{suffix}", out BlockDef v)) list.Add(v);
        return list;
    }

    public static IReadOnlyList<BlockDef> All => Defs;

    public static BlockDef Get(string id) =>
        ById.TryGetValue(id, out BlockDef d) ? d : throw new KeyNotFoundException($"Unknown block '{id}'.");

    public static bool TryGet(string id, out BlockDef def) => ById.TryGetValue(id ?? "", out def);

    /// <summary>The blocks the builder lists under a category (shape variants appear only as their plain version).</summary>
    public static List<BlockDef> InCategory(string category)
    {
        var list = new List<BlockDef>();
        foreach (BlockDef d in Defs)
            if (d.Category == category && (d.BaseId == null || d.BaseId == d.Id)) list.Add(d);
        return list;
    }
}
