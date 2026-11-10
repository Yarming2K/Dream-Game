using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The test range: drive or walk around, shoot armor walls, a bunker, parked vehicles and pop-up targets,
/// patch your vehicle up with the welder, and resupply on the pad. Live fire makes the parked vehicles
/// shoot back.
/// </summary>
public partial class TestRange : Node3D
{
    public event Action ExitRequested;

    private const float Ground = Terrain.LandHeight;
    private static readonly Vector3 SpawnPoint = new(0, Ground, 120);
    private static readonly Vector3 DepotCenter = new(16, Ground, 124);
    private const float DepotRadius = 9f;

    // The sea off the east coast: ships start here, and a supply ring floats behind them.
    private static readonly Vector3 SeaSpawn = new(240, Ocean.Level, 60);
    private static readonly Vector3 SeaDepotCenter = new(240, Ocean.Level, 100);
    private const float SeaDepotRadius = 18f;

    // Big ships (anything over 80 m) start further out, at the fleet anchorage, which also resupplies them.
    private static readonly Vector3 Anchorage = new(470, Ocean.Level, 0);
    private const float AnchorageRadius = 90f;
    private const float BigShipLength = 80f;
    private float _missingTimer;
    private int _missing;

    // The airfield in the western hills.
    private static readonly Vector3 StripStart = new(Terrain.StripX, Ground, Terrain.StripHalfLength - 18f);
    private static readonly Vector3 Apron = new(Terrain.StripX, Ground, Terrain.StripHalfLength - 2f);
    private const float ApronRadius = 16f;
    private static readonly Vector3 HelipadCenter = new(Terrain.Helipad.X, Ground, Terrain.Helipad.Y);

    private bool _remote;
    private bool _bombQueued, _vtolQueued;
    private Vector3? _bombImpact;
    private VehicleBody _lock;

    private Godot.Environment _env;
    private float _airFogDensity;
    private Color _airFogColor;
    private ColorRect _underwaterTint;
    private bool _torpedoQueued, _missileQueued;

    private Blueprint _bp;
    private VehicleBody _vehicle;
    private VehicleBody _mount;
    private PlayerCharacter _me;
    private Combat _combat;
    private readonly List<Node3D> _targets = new();
    private bool _liveFire;
    private float _deadTime = -1f;
    private bool _resupplying;
    private bool _interactQueued;
    private VehicleBody _lookedAt;

    // Vehicle camera
    private Node3D _pivot;
    private SpringArm3D _arm;
    private Camera3D _cam;
    private float _yaw;
    private float _pitch = -0.28f;
    private float _dist = 13f;
    private bool _zoom;
    private bool _holdFire;
    private float _lastKick;
    private readonly List<(Vector3 point, bool ready)> _impacts = new();

    // Camera feel
    private float _trauma;
    private float _fovKick;
    private float _shakeTime;
    private readonly FastNoiseLite _shakeNoise = new() { Frequency = 2.2f, Seed = 9 };

    // HUD
    private Control _vehicleHud, _footHud;
    private RichTextLabel _status, _guns, _feed, _crew;
    private Label _big, _resume, _prompt, _liveChip;
    private float _bigTime;
    private PanelContainer _help;
    private ReticleOverlay _reticle;
    private ColorRect _vignette;
    private float _hurtFlash;
    private readonly List<(string text, float age)> _feedLines = new();

    /// <summary>One line of the weapons HUD: every gun of one kind.</summary>
    private sealed class GunRow
    {
        public List<VehicleBody.GunRt> Guns = new();
        public Label Name;
        public Label State;
        public Label Ammo;
        public StatBar Reload;
    }

    private VBoxContainer _gunList;
    private readonly List<GunRow> _gunRows = new();
    private Label _healthLabel, _weaponName, _weaponAmmo, _slots, _weldLabel;
    private StatBar _healthBar, _weaponReload, _weldBar;
    private PanelContainer _weldPanel;

    public void Setup(Blueprint bp) => _bp = bp;

    public override void _Ready()
    {
        WorldQuery.Reset();
        Sfx.Prewarm();
        _env = Scenery.AddSkyAndSun(this, true);
        _airFogDensity = _env.FogDensity;
        _airFogColor = _env.FogLightColor;
        AddChild(Terrain.Build());
        AddChild(Ocean.Build());
        AddRamps();
        AddMarkers();
        AddDepot();
        AddSeaDepot();
        AddAirfield();
        AddPopupTargets();
        RegisterProps(this);
        _combat = new Combat { Name = "Combat" };
        AddChild(_combat);
        _combat.Event += OnCombatEvent;
        _combat.Blast += OnBlast;

        _pivot = new Node3D { Name = "CameraPivot" };
        AddChild(_pivot);
        _arm = new SpringArm3D
        {
            SpringLength = _dist, Margin = 0.3f, CollisionMask = GridRegistry.Layers.World | GridRegistry.Layers.Structures,
        };
        _pivot.AddChild(_arm);
        _cam = new Camera3D { Fov = 70f, Far = 3000f };
        _arm.AddChild(_cam);

        BuildHud();
        SpawnTargets();

        _me = new PlayerCharacter { Name = "Player" };
        AddChild(_me);
        _me.GlobalPosition = DepotCenter + new Vector3(-4, 0.2f, 0);
        _me.Died += OnDied;
        _me.Hurt += dmg =>
        {
            _hurtFlash = Mathf.Min(1f, _hurtFlash + dmg / 60f);
            _trauma = Mathf.Min(1f, _trauma + dmg / 120f);
        };

        AddCommandPost();
        SpawnVehicle();
        EnterVehicle(_vehicle, SeatRole.Driver);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _ExitTree() => WorldQuery.Reset();

    // ---------- World ----------

    /// <summary>Tells the fast world queries about solid scenery that isn't the ground (ramps, crates).</summary>
    private static void RegisterProps(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is StaticBody3D sb && sb.Name != "Terrain" && (sb.CollisionLayer & GridRegistry.Layers.World) != 0) WorldQuery.AddProps(sb);
            else if (c is not IGridHost && c is not PhysicsBody3D) RegisterProps(c);
        }
    }

    private void AddRamps()
    {
        var concrete = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.54f, 0.5f), Roughness = 0.9f };
        (float x, float deg)[] ramps = { (-24f, 12f), (-34f, 22f), (-44f, 32f) };
        foreach ((float x, float deg) in ramps)
        {
            float a = Mathf.DegToRad(deg);
            const float length = 14f, width = 7f, thick = 0.8f;
            var size = new Vector3(width, thick, length);
            var body = new StaticBody3D { CollisionLayer = GridRegistry.Layers.World, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = concrete });
            var basePos = new Vector3(x, Ground, 104f);
            var center = basePos + new Vector3(0, Mathf.Sin(a) * length / 2f - thick * 0.5f * Mathf.Cos(a), -Mathf.Cos(a) * length / 2f);
            body.Transform = new Transform3D(new Basis(Vector3.Right, a), center);
            AddChild(body);
            AddChild(Sign($"{deg:0}° slope", new Vector3(x, Ground + 1.5f, 106f), 64));
        }
    }

    private void AddMarkers()
    {
        for (int d = 50; d <= 300; d += 50)
        {
            AddChild(Sign($"{d} m", new Vector3(-50f, Ground + 1.2f, SpawnPoint.Z - d), 96));
            AddChild(Sign($"{d} m", new Vector3(50f, Ground + 1.2f, SpawnPoint.Z - d), 96));
        }
    }

    private static Label3D Sign(string text, Vector3 pos, int size) => new()
    {
        Text = text, FontSize = size, PixelSize = 0.012f, Position = pos, Font = Ui.HeadingBoldFont,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 12,
        Modulate = new Color(1f, 0.95f, 0.8f),
    };

    /// <summary>A supply pad: park on it to rearm and refuel, stand on it to heal and restock.</summary>
    private void AddDepot()
    {
        var root = new Node3D { Name = "Depot", Position = DepotCenter };
        AddChild(root);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = DepotRadius, BottomRadius = DepotRadius, Height = 0.06f, RadialSegments = 48 },
            Position = new Vector3(0, 0.03f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.31f, 0.28f), Roughness = 0.95f },
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = DepotRadius - 0.25f, OuterRadius = DepotRadius, Rings = 48, RingSegments = 6 },
            Position = new Vector3(0, 0.07f, 0), Scale = new Vector3(1, 0.15f, 1),
            MaterialOverride = BlockVisuals.Emissive(new Color(1f, 0.72f, 0.27f), 1.2f),
        });
        StandardMaterial3D crate = BlockVisuals.Material("crate", new Color(0.36f, 0.4f, 0.26f), 0.2f, 0.75f);
        var crates = new StaticBody3D { CollisionLayer = GridRegistry.Layers.World, CollisionMask = 0, Position = new Vector3(DepotRadius + 1.6f, 0, 0) };
        root.AddChild(crates);
        foreach (Vector3 c in new[] { new Vector3(0, 0.45f, -1f), new Vector3(0, 0.45f, 0.1f), new Vector3(0, 1.35f, -0.45f), new Vector3(0.95f, 0.45f, -0.5f) })
        {
            crates.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.9f, 1.05f) }, MaterialOverride = crate, Position = c });
            crates.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.9f, 1.05f) }, Position = c });
        }

        root.AddChild(Sign("SUPPLY\npark or stand here", new Vector3(0, 3.2f, 0), 64));
    }

    /// <summary>A ring of buoys at sea: stop a ship inside it to rearm and refuel.</summary>
    private void AddSeaDepot()
    {
        var root = new Node3D { Name = "SeaDepot", Position = SeaDepotCenter };
        AddChild(root);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = SeaDepotRadius - 0.35f, OuterRadius = SeaDepotRadius, Rings = 72, RingSegments = 6 },
            Position = new Vector3(0, 0.12f, 0), Scale = new Vector3(1, 0.3f, 1),
            MaterialOverride = BlockVisuals.Emissive(new Color(1f, 0.72f, 0.27f), 1.2f),
        });
        var red = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.16f, 0.12f), Roughness = 0.6f };
        var white = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.9f, 0.86f), Roughness = 0.6f };
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f;
            var at = new Vector3(Mathf.Cos(a) * SeaDepotRadius, 0, Mathf.Sin(a) * SeaDepotRadius);
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.6f, Height = 1.2f }, MaterialOverride = red, Position = at + Vector3.Up * 0.2f });
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.3f, Height = 1.1f }, MaterialOverride = white, Position = at + Vector3.Up * 1.3f });
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f }, Position = at + Vector3.Up * 1.95f,
                MaterialOverride = BlockVisuals.Emissive(new Color(1f, 0.72f, 0.27f), 3f),
            });
        }

        root.AddChild(Sign("NAVAL SUPPLY\nstop inside the ring", new Vector3(0, 7f, 0), 72));

        // The fleet anchorage, for ships too big for the buoy ring: a wide ring of buoys far offshore.
        var fleet = new Node3D { Name = "Anchorage", Position = Anchorage };
        AddChild(fleet);
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.Tau / 24f;
            var at = new Vector3(Mathf.Cos(a) * AnchorageRadius, 0, Mathf.Sin(a) * AnchorageRadius);
            fleet.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.8f, BottomRadius = 1.1f, Height = 2f }, MaterialOverride = red, Position = at + Vector3.Up * 0.4f });
            fleet.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f }, Position = at + Vector3.Up * 1.8f,
                MaterialOverride = BlockVisuals.Emissive(new Color(1f, 0.72f, 0.27f), 3f),
            });
        }

        fleet.AddChild(Sign("FLEET ANCHORAGE\nbig ships start and resupply here", new Vector3(0, 30f, -AnchorageRadius), 200));
    }

    /// <summary>A concrete airstrip with centerline and threshold markings, an apron that resupplies, and a helipad.</summary>
    private void AddAirfield()
    {
        var concrete = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.5f, 0.48f), Roughness = 0.95f };
        var paint = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.88f), Roughness = 0.8f };
        float len = Terrain.StripHalfLength * 2f, w = Terrain.StripHalfWidth * 1.6f;
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w, 0.1f, len) }, MaterialOverride = concrete,
            Position = new Vector3(Terrain.StripX, Ground - 0.02f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        for (float z = -Terrain.StripHalfLength + 30f; z < Terrain.StripHalfLength - 30f; z += 24f)
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.02f, 10f) }, MaterialOverride = paint,
                Position = new Vector3(Terrain.StripX, Ground + 0.04f, z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        foreach (float end in new[] { -1f, 1f })
            for (int i = -3; i <= 3; i++)
                AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(1.4f, 0.02f, 12f) }, MaterialOverride = paint,
                    Position = new Vector3(Terrain.StripX + i * 2.6f, Ground + 0.04f, end * (Terrain.StripHalfLength - 12f)),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });

        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = ApronRadius - 0.3f, OuterRadius = ApronRadius, Rings = 64, RingSegments = 6 },
            Position = Apron + Vector3.Up * 0.06f, Scale = new Vector3(1, 0.15f, 1),
            MaterialOverride = BlockVisuals.Emissive(new Color(1f, 0.72f, 0.27f), 1.2f),
        });
        AddChild(Sign("AIRSTRIP\ntaxi back here to rearm", Apron + new Vector3(ApronRadius + 3f, 4f, 0), 64));

        // Helipad: a round pad with an H.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = Terrain.HelipadRadius, BottomRadius = Terrain.HelipadRadius, Height = 0.1f, RadialSegments = 48 },
            MaterialOverride = concrete, Position = HelipadCenter + Vector3.Up * 0.01f,
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = Terrain.HelipadRadius - 0.5f, OuterRadius = Terrain.HelipadRadius - 0.1f, Rings = 48, RingSegments = 6 },
            Position = HelipadCenter + Vector3.Up * 0.08f, Scale = new Vector3(1, 0.15f, 1),
            MaterialOverride = BlockVisuals.Emissive(new Color(1f, 0.72f, 0.27f), 1.2f),
        });
        foreach (var (size, offset) in new[] { (new Vector3(0.9f, 0.02f, 7f), new Vector3(-2.2f, 0, 0)), (new Vector3(0.9f, 0.02f, 7f), new Vector3(2.2f, 0, 0)), (new Vector3(3.5f, 0.02f, 0.9f), Vector3.Zero) })
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = paint, Position = HelipadCenter + offset + Vector3.Up * 0.08f });
        AddChild(Sign("HELIPAD\nland here to rearm", HelipadCenter + new Vector3(0, 5f, -Terrain.HelipadRadius - 2f), 64));
    }

    private static bool AtDepot(Vector3 p)
    {
        float land = new Vector2(p.X - DepotCenter.X, p.Z - DepotCenter.Z).Length();
        float sea = new Vector2(p.X - SeaDepotCenter.X, p.Z - SeaDepotCenter.Z).Length();
        float apron = new Vector2(p.X - Apron.X, p.Z - Apron.Z).Length();
        float pad = new Vector2(p.X - HelipadCenter.X, p.Z - HelipadCenter.Z).Length();
        bool low = p.Y < Ground + 6f;
        float anchorage = new Vector2(p.X - Anchorage.X, p.Z - Anchorage.Z).Length();
        return (land <= DepotRadius && p.Y > DepotCenter.Y - 3f && low) || sea <= SeaDepotRadius || anchorage <= AnchorageRadius ||
               (apron <= ApronRadius && low) || (pad <= Terrain.HelipadRadius && low);
    }

    private void AddPopupTargets()
    {
        (float x, float d)[] spots = { (30f, 15f), (36f, 30f), (31f, 50f), (38f, 75f), (33f, 110f) };
        foreach ((float x, float d) in spots)
        {
            var t = new PopupTarget { DistanceM = d };
            AddChild(t);
            t.GlobalPosition = new Vector3(x, Ground, SpawnPoint.Z - d);
            t.Rotation = new Vector3(0, Mathf.Atan2(SpawnPoint.X - x, d) * 0.5f, 0);
            t.KnockedDown += target => AddFeed($"[color={Ui.Hex(Ui.Good)}]TARGET DOWN  {target.DistanceM:0} m[/color]", true);
        }

        AddChild(Sign("Pop-up targets", new Vector3(34f, Ground + 3f, SpawnPoint.Z - 8f), 64));
    }

    private void SpawnTargets()
    {
        foreach (Node3D t in _targets)
        {
            if (!IsInstanceValid(t)) continue;
            if (t is IGridHost h) h.Vanish();
            else t.QueueFree();
        }

        _targets.Clear();
        _combat?.Clear();

        float wallZ = SpawnPoint.Z - 60f;
        AddStructure(ExampleDesigns.Wall("armor_light", 7, 5), new Vector3(-24, Ground, wallZ), 0, "Light armor\n25 mm");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 7, 5), new Vector3(-8, Ground, wallZ), 0, "Heavy armor\n80 mm");
        AddStructure(ExampleDesigns.Wall("armor_composite", 7, 5), new Vector3(8, Ground, wallZ), 0, "Composite\n110 mm, weak to HE");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 7, 5), new Vector3(24, Ground, wallZ), 45, "Heavy armor\nangled 45°");
        AddStructure(ExampleDesigns.Bunker(), new Vector3(0, Ground, SpawnPoint.Z - 160f), 180, "Bunker\n(ammo inside)");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 9, 6), new Vector3(0, Ground, SpawnPoint.Z - 300f), 0, "300 m");

        AddDummy(ExampleDesigns.Warden(), new Vector3(-26, Ground, SpawnPoint.Z - 120f), 35f);
        AddDummy(ExampleDesigns.Jackal(), new Vector3(26, Ground, SpawnPoint.Z - 110f), -60f);

        // At sea: an enemy patrol boat lying broadside on, and a submarine on the surface further out.
        AddDummy(ExampleDesigns.Corsair(), new Vector3(SeaSpawn.X + 70f, 0, SeaSpawn.Z - 260f), 90f);
        AddDummy(ExampleDesigns.Barracuda(), new Vector3(SeaSpawn.X + 160f, 0, SeaSpawn.Z - 420f), 30f);

        // In the air: a target plane flying circuits round the island, a helicopter hovering over the
        // eastern hills, and an airship off the north-east coast.
        AddFlyer(ExampleDesigns.Kestrel(), new Vector3(380f, 150f, 0f), 0f, Vector3.Forward * 62f, patrol: true);
        AddFlyer(ExampleDesigns.Hornet(), new Vector3(95f, 50f, -40f), -90f, Vector3.Zero);
        AddFlyer(ExampleDesigns.Zephyr(), new Vector3(170f, 100f, -170f), 200f, Vector3.Zero);
    }

    private void AddFlyer(Blueprint bp, Vector3 pos, float yawDeg, Vector3 velocity, bool patrol = false)
    {
        var where = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), pos);
        VehicleBody v = VehicleBody.Create(bp, where, false);
        v.AiCrewActive = _liveFire;
        v.Patrols = patrol;
        v.PatrolCenter = Vector3.Zero;
        v.PatrolRadius = 380f;
        v.PatrolAltitude = pos.Y;
        v.LinearVelocity = velocity;
        if (patrol) v.SetAirThrottle(0.75f);
        AddChild(v);
        _targets.Add(v);
    }

    private void AddStructure(Blueprint bp, Vector3 pos, float yawDeg, string label)
    {
        StructureBody s = StructureBody.Create(bp, new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), pos), label);
        AddChild(s);
        int top = bp.Blocks.Max(b => b.Y);
        s.AddChild(Sign(label, new Vector3(0, (top + 1) * Conv.CS + 1.2f, 0), 72));
        _targets.Add(s);
    }

    private void AddDummy(Blueprint bp, Vector3 pos, float yawDeg)
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
        Vector3 at = a.IsNaval ? new Vector3(pos.X, VehicleBody.FloatHeight(a), pos.Z) : pos + Vector3.Up * VehicleBody.SpawnHeight(a);
        var where = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), at);
        VehicleBody v = VehicleBody.Create(bp, where, false, a);
        v.AiCrewActive = _liveFire;
        AddChild(v);
        _targets.Add(v);
    }

    private void SpawnVehicle()
    {
        if (_mount != null && _mount == _vehicle) ExitVehicle(silent: true);
        if (_vehicle != null && IsInstanceValid(_vehicle)) _vehicle.Vanish();
        VehicleAnalysis a = VehicleAnalysis.Analyze(_bp);
        // Ships and submarines start at sea, planes on the airstrip, helicopters and drones on the helipad,
        // airships floating above it; everything else on the firing line.
        Vector3 at = a.Mode switch
        {
            MobilityMode.Ship or MobilityMode.Submarine when (a.Max.Z - a.Min.Z + 1) * Conv.CS > BigShipLength =>
                new Vector3(Anchorage.X, VehicleBody.FloatHeight(a), Anchorage.Z),
            MobilityMode.Ship or MobilityMode.Submarine => new Vector3(SeaSpawn.X, VehicleBody.FloatHeight(a), SeaSpawn.Z),
            MobilityMode.Plane => StripStart + Vector3.Up * VehicleBody.SpawnHeight(a),
            MobilityMode.Helicopter or MobilityMode.Hover => HelipadCenter + Vector3.Up * VehicleBody.SpawnHeight(a),
            MobilityMode.Airship => HelipadCenter + Vector3.Up * 30f,
            _ => SpawnPoint + Vector3.Up * VehicleBody.SpawnHeight(a),
        };
        var where = new Transform3D(Basis.Identity, at);
        _vehicle = VehicleBody.Create(_bp, where, true, a);
        _vehicle.AiCrewActive = _liveFire;
        HookVehicle(_vehicle);
        AddChild(_vehicle);
    }

    private readonly HashSet<VehicleBody> _hooked = new();

    /// <summary>Listens to a vehicle the player may ride: its status messages, its guns firing, and its seat being hit.</summary>
    private void HookVehicle(VehicleBody v)
    {
        if (!_hooked.Add(v)) return;
        v.TreeExiting += () => _hooked.Remove(v);
        v.Status += msg =>
        {
            if (v == _mount) Big(msg);
        };
        v.Fired += g =>
        {
            if (v == _mount) OnFired(g);
        };
        v.PlayerSeatDestroyed += () =>
        {
            if (v != _mount) return;
            _mount = null;
            if (_remote)
            {
                // Flying by remote: you lose the drone, not your life.
                _remote = false;
                _me.SetRemote(false);
                Big("Drone lost");
                return;
            }

            Big("Your seat was hit!");
            _me.Damage(999f);
        };
    }

    // ---------- Getting in and out ----------

    private bool Mounted => _mount != null && IsInstanceValid(_mount) && _mount.IsInsideTree();

    private void EnterVehicle(VehicleBody v, SeatRole? role)
    {
        SeatRt seat = v.Enter(role);
        if (seat == null)
        {
            Big("No free seats");
            return;
        }

        _mount = v;
        _remote = seat.Info.Remote;
        if (_remote) _me.SetRemote(true);
        else
        {
            _me.SetActive(false);
            _me.GlobalPosition = v.SeatWorldPos(seat);
        }

        _cam.MakeCurrent();
        _dist = v.IsAir ? Mathf.Max(_dist, v.Radius * 1.6f + 8f) : v.Radius > 15f ? Mathf.Max(_dist, v.Radius * 1.3f) : _dist;
        _arm.ClearExcludedObjects();
        _arm.AddExcludedObject(v.GetRid());
        _pivot.GlobalPosition = v.GlobalTransform * v.CenterOfMass + Vector3.Up * 2f;
        Vector3 f = -v.GlobalTransform.Basis.Z;
        _yaw = Mathf.Atan2(-f.X, -f.Z);
        BuildGunRows();
        Sfx.Play2D(this, SoundId.PlaceLight, -6f, 0.8f);
        Big(_remote ? $"Flying {v.DisplayName} by remote" : $"{Crew.Name(seat.Role)} seat");
    }

    private void ExitVehicle(bool silent = false)
    {
        if (!Mounted)
        {
            _mount = null;
            return;
        }

        VehicleBody v = _mount;
        if (_remote)
        {
            // Back to your own eyes, where you were standing.
            v.Exit();
            _mount = null;
            _remote = false;
            if (_me.Alive) _me.SetRemote(false);
            if (!silent) Sfx.Play2D(this, SoundId.PlaceLight, -6f, 1.1f);
            return;
        }

        Vector3 spot = FindExitSpot(v);
        v.Exit();
        _mount = null;
        _me.GlobalPosition = spot;
        _me.LookYaw(_yaw);
        if (_me.Alive) _me.SetActive(true);
        if (!silent) Sfx.Play2D(this, SoundId.PlaceLight, -6f, 1.1f);
    }

    /// <summary>A clear spot beside the vehicle to step out to: left, right, behind, in front, or on top.</summary>
    private Vector3 FindExitSpot(VehicleBody v)
    {
        Transform3D xf = v.GlobalTransform;
        VehicleAnalysis a = v.Analysis;
        float halfW = (a.Max.X - a.Min.X + 1) * Conv.CS * 0.5f, halfL = (a.Max.Z - a.Min.Z + 1) * Conv.CS * 0.5f;
        Vector3 center = xf * new Vector3((a.Min.X + a.Max.X) * 0.5f * Conv.CS, 0, (a.Min.Z + a.Max.Z) * 0.5f * Conv.CS);
        Vector3 right = xf.Basis.X.Normalized(), back = xf.Basis.Z.Normalized();
        Vector3[] candidates =
        {
            center - right * (halfW + 1.2f), center + right * (halfW + 1.2f), center + back * (halfL + 1.4f),
            center - back * (halfL + 1.4f),
        };
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f };

        // Boats: step out onto the deck rather than into the sea.
        if (v.IsNaval)
        {
            float cs = Conv.CS;
            foreach (float along in new[] { 0f, -0.22f, 0.22f, -0.35f, 0.35f, -0.1f, 0.1f })
            {
                float z = Mathf.Lerp(a.Min.Z, a.Max.Z, 0.5f + along) * cs;
                Vector3 from = xf * new Vector3((a.Min.X + a.Max.X) * 0.5f * cs, (a.Max.Y + 4) * cs, z);
                Godot.Collections.Dictionary deck = space.IntersectRay(
                    PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * (a.Max.Y - a.Min.Y + 8) * cs, GridRegistry.Layers.Vehicles));
                if (deck.Count == 0 || deck["collider"].AsGodotObject() != v || deck["normal"].AsVector3().Y < 0.7f) continue;
                Vector3 spot = deck["position"].AsVector3();
                var q = new PhysicsShapeQueryParameters3D
                {
                    Shape = shape, Transform = new Transform3D(Basis.Identity, spot + Vector3.Up * 1.0f), CollisionMask = GridRegistry.Layers.All,
                };
                if (space.IntersectShape(q, 1).Count == 0) return spot + Vector3.Up * 0.05f;
            }
        }

        foreach (Vector3 c in candidates)
        {
            Godot.Collections.Dictionary down = space.IntersectRay(
                PhysicsRayQueryParameters3D.Create(c + Vector3.Up * 3f, c + Vector3.Down * 6f, GridRegistry.Layers.World));
            Vector3 ground = down.Count > 0 ? down["position"].AsVector3() : c;
            var q = new PhysicsShapeQueryParameters3D
            {
                Shape = shape, Transform = new Transform3D(Basis.Identity, ground + Vector3.Up * 1.0f),
                CollisionMask = GridRegistry.Layers.All,
            };
            if (space.IntersectShape(q, 1).Count == 0) return ground + Vector3.Up * 0.05f;
        }

        return xf * new Vector3(0, (a.Max.Y + 2) * Conv.CS, 0);
    }

    private void TrySwitchSeat(SeatRole role)
    {
        if (!Mounted) return;
        SeatRt s = _mount.SwitchSeat(role);
        if (s == null)
        {
            Big($"No {Crew.Name(role).ToLowerInvariant()} seat");
            Sfx.Play2D(this, SoundId.Error, -10f);
            return;
        }

        BuildGunRows();
        Sfx.Play2D(this, SoundId.Tick, -6f);
        Big($"{Crew.Name(role)} seat");
    }

    private void OnDied()
    {
        if (Mounted) _mount.Exit();
        _mount = null;
        _remote = false;
        _me.SetActive(false);
        _deadTime = 4f;
        Big("You were killed");
    }

    private void RespawnOnFoot()
    {
        _deadTime = -1f;
        _me.Respawn(DepotCenter + new Vector3(-5, 0.3f, 3), 0f);
        _me.SetActive(true);
        Big("Back on your feet at the supply pad");
    }

    // ---------- Frame ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (!Input.IsActionPressed("fire")) _holdFire = false;

        if (Mounted)
        {
            bool driving = _mount.PlayerSeat?.Role == SeatRole.Driver;
            PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
            var exclude = new Godot.Collections.Array<Rid> { _mount.GetRid() };
            Vector3 o = _cam.GlobalPosition;
            Vector3 d = -_cam.GlobalTransform.Basis.Z;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(
                o, o + d * 2500f, GridRegistry.Layers.All | GridRegistry.Layers.Characters | GridRegistry.Layers.Targets, exclude));
            bool naval = _mount.IsNaval;
            bool air = _mount.IsAir;
            bool telegraph = naval || _mount.FlightMode == MobilityMode.Airship;
            bool hover = _mount.FlightMode is MobilityMode.Helicopter or MobilityMode.Hover or MobilityMode.Airship;
            float lift = captured && driving && air ? (Input.IsActionPressed("brake") ? 1f : 0f) - (Input.IsActionPressed("dive") ? 1f : 0f) : 0f;
            _mount.PlayerInput = new VehicleControls
            {
                // Ships and airships take their speed from the telegraph (W / S taps, in _UnhandledInput).
                Throttle = captured && driving && !telegraph ? Input.GetAxis("move_back", "move_forward") : 0f,
                Steer = captured && driving ? Input.GetAxis("steer_left", "steer_right") : 0f,
                Brake = captured && driving && !naval && !hover && Input.IsActionPressed("brake"),
                Lift = lift,
                AimDir = d,
                HasAim = true,
                Aim = hit.Count > 0 ? hit["position"].AsVector3() : o + d * 1500f,
                Fire = captured && !_holdFire && Input.IsActionPressed("fire"),
            };
            if (captured && driving && _mount.IsSubmarine)
            {
                if (Input.IsActionPressed("dive")) _mount.ChangeDepth(3f, dt);
                if (Input.IsActionPressed("brake")) _mount.ChangeDepth(-5f, dt);
            }

            // F fires torpedoes from anything with tubes, otherwise missiles; X always fires missiles.
            if (_torpedoQueued)
            {
                _torpedoQueued = false;
                if (_mount.Tubes.Count == 0 && _mount.Analysis.Missiles.Count > 0) LaunchMissile();
                else LaunchTorpedo();
            }

            if (_missileQueued)
            {
                _missileQueued = false;
                LaunchMissile();
            }

            if (_bombQueued)
            {
                _bombQueued = false;
                if (_mount.DropBomb(true)) AddFeed($"[color={Ui.Hex(Ui.Info)}]BOMB AWAY  ·  {_mount.BombsLeft} left[/color]", true);
                else
                {
                    Big(_mount.Analysis.BombRacks.Count == 0 ? "No bomb racks" : "Out of bombs");
                    Sfx.Play2D(this, SoundId.Error, -10f);
                }
            }

            if (_vtolQueued)
            {
                _vtolQueued = false;
                if (_mount.ToggleVtol()) Big(_mount.FlightMode == MobilityMode.Plane ? "Wing-borne flight" : "Hovering");
            }

            _bombImpact = _mount.BombsLeft > 0 ? _mount.BombImpact(space) : null;
            bool radar = _mount.Analysis.Missiles.Any(m => m.Valid && m.Stats.Seeker == SeekerKind.Radar) && _mount.MissilesReady > 0;
            _lock = radar ? VehicleBody.RadarLock(o, d, _mount.Faction, 25f, 4000f) : null;

            if (!_remote) _me.GlobalPosition = _mount.PlayerSeat != null ? _mount.SeatWorldPos(_mount.PlayerSeat) : _me.GlobalPosition;
            PredictImpacts(space, exclude);
        }
        else
        {
            _impacts.Clear();
            _torpedoQueued = _missileQueued = _bombQueued = _vtolQueued = false;
            _bombImpact = null;
            _lock = null;
        }

        _lookedAt = !Mounted && _me.Active && _me.Alive ? _me.LookedAtVehicle(5f) : null;
        if (_interactQueued)
        {
            _interactQueued = false;
            Interact();
        }

        // The supply pad.
        _resupplying = false;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || v.Faction != 0 || !IsInstanceValid(v)) continue;
            if (!AtDepot(v.GlobalPosition)) continue;
            bool changed = v.Resupply(dt);
            if (v == _mount && changed) _resupplying = true;
        }

        if (_me.Active && _me.Alive && AtDepot(_me.GlobalPosition))
        {
            bool healed = _me.Health < InfantryDamage.MaxHealth;
            _me.Heal(25f * dt);
            if (_me.RefillAmmo(dt) || healed) _resupplying = true;
        }

        if (_deadTime > 0)
        {
            _deadTime -= dt;
            if (_deadTime <= 0) RespawnOnFoot();
        }
    }

    private void PredictImpacts(PhysicsDirectSpaceState3D space, Godot.Collections.Array<Rid> exclude)
    {
        _impacts.Clear();
        // With a lot of guns, mark where a few of each kind will land (the ones nearest to being on target).
        const int perKind = 3;
        IEnumerable<VehicleBody.GunRt> shown = _mount.Guns.Count <= 6
            ? _mount.Guns
            : _gunRows.SelectMany(r => r.Guns.Where(g => g.Alive && g.Info.GunSection >= 0 && _mount.PlayerDirects(g)).OrderBy(g => g.AimError).Take(perKind));
        foreach (VehicleBody.GunRt g in shown)
        {
            if (!g.Alive || g.Info.GunSection < 0 || !_mount.PlayerDirects(g)) continue;
            (Vector3 p, Vector3 dir) = _mount.Muzzle(g);
            bool ap = _mount.SelectedAmmo == AmmoType.AP;
            Vector3 v = dir * VehicleBody.ShellSpeed(g, _mount.SelectedAmmo) + _mount.LinearVelocity;
            float k = ap ? g.Stats.DragK : g.Stats.HeDragK;
            const float step = 0.04f;
            for (int i = 0; i < 70; i++)
            {
                float speed = v.Length();
                v += Vector3.Down * GunMath.Gravity * step;
                v *= Mathf.Max(0, 1f - k * speed * step);
                Vector3 next = p + v * step;
                Godot.Collections.Dictionary h = space.IntersectRay(
                    PhysicsRayQueryParameters3D.Create(p, next, GridRegistry.Layers.All, exclude));
                if (h.Count > 0)
                {
                    p = h["position"].AsVector3();
                    break;
                }

                if (p.Y > Ocean.Level && next.Y <= Ocean.Level)
                {
                    p = p.Lerp(next, (p.Y - Ocean.Level) / (p.Y - next.Y));
                    break;
                }

                p = next;
            }

            _impacts.Add((p, g.ReloadLeft <= 0 && (_mount.Rounds(g, AmmoType.AP) + _mount.Rounds(g, AmmoType.HE) + _mount.Rounds(g, AmmoType.Flak)) > 0));
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Mounted)
        {
            Vector3 target = _mount.GlobalTransform * _mount.CenterOfMass + Vector3.Up * 2f;
            // Aircraft are too quick for a lagging camera: stay right on them.
            _pivot.GlobalPosition = _mount.IsAir ? target : _pivot.GlobalPosition.Lerp(target, Mathf.Min(1f, dt * 14f));
        }

        _pivot.Rotation = new Vector3(_pitch, _yaw, 0);
        _arm.SpringLength = _zoom ? Mathf.Min(_dist, 8f) : _dist;

        // Shake comes from "trauma" that fades; squaring it keeps small bumps subtle and big blasts violent.
        _trauma = Mathf.Max(0, _trauma - dt * 1.4f);
        _fovKick = Mathf.Max(0, _fovKick - dt * 22f);
        _shakeTime += dt;
        float shake = _trauma * _trauma * (_zoom ? 0.35f : 1f);
        float n1 = _shakeNoise.GetNoise2D(_shakeTime * 60f, 0), n2 = _shakeNoise.GetNoise2D(0, _shakeTime * 60f);
        float n3 = _shakeNoise.GetNoise2D(_shakeTime * 60f, 100f);
        Camera3D active = Mounted ? _cam : _me.Camera;
        WorldQuery.Viewer = active.GlobalPosition;
        DrillTick();
        UpdateUnderwater(active);
        active.HOffset = n1 * 0.35f * shake;
        active.VOffset = n2 * 0.35f * shake;
        _cam.Rotation = new Vector3(n2 * 0.03f * shake, n1 * 0.03f * shake, n3 * 0.05f * shake);

        bool commander = Mounted && _mount.PlayerSeat?.Role == SeatRole.Commander;
        float zoomFov = commander ? 8f : 18f;
        float baseFov = Mathf.Lerp(_cam.Fov - _lastKick, _zoom ? zoomFov : 70f, Mathf.Min(1f, dt * 10f));
        _lastKick = _fovKick * (_zoom ? 0.3f : 1f);
        _cam.Fov = baseFov + _lastKick;

        // Zoomed in, far vehicles keep their full detail further out.
        float lodScale = _zoom && Mounted ? Mathf.Clamp(70f / zoomFov, 1f, 6f) : 1f;
        if (!Mathf.IsEqualApprox(lodScale, GridView.LodScale))
        {
            GridView.LodScale = lodScale;
            foreach (IGridHost h in GridRegistry.Hosts)
                if (h.View != null) h.View.RefreshLod();
        }

        _hurtFlash = Mathf.Max(0, _hurtFlash - dt * 1.2f);
        UpdateHud(dt);
    }

    /// <summary>Below the surface the world turns murky green-blue and close.</summary>
    private void UpdateUnderwater(Camera3D cam)
    {
        bool under = cam.GlobalPosition.Y < Ocean.Level - 0.05f;
        _underwaterTint.Visible = under;
        _env.FogDensity = under ? 0.075f : _airFogDensity;
        _env.FogLightColor = under ? new Color(0.05f, 0.22f, 0.26f) : _airFogColor;
        _env.FogSkyAffect = under ? 1f : 0.3f;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // The tactical map takes the keys while it's open; M or Esc closes it.
        if (_map != null && _map.Visible)
        {
            if (e.IsActionPressed("tactical_map") || (e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape }))
            {
                ToggleMap();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (e.IsActionPressed("tactical_map"))
        {
            ToggleMap();
            return;
        }

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (e is InputEventMouseMotion mm && captured && Mounted)
        {
            float sens = _zoom ? (_mount.PlayerSeat?.Role == SeatRole.Commander ? 0.0004f : 0.0009f) : 0.0028f;
            _yaw -= mm.Relative.X * sens;
            float up = _mount.IsAir ? 1.45f : 0.55f;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * sens, -1.45f, up);
            return;
        }

        if (e is InputEventMouseButton mb)
        {
            if (!captured && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                _holdFire = true;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (!Mounted) return;
            float far = Mathf.Max(60f, _mount.Radius * 2.5f);
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _dist = Mathf.Clamp(_dist * 0.9f, 5f, far);
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _dist = Mathf.Clamp(_dist * 1.1f, 5f, far);
            if (mb.ButtonIndex == MouseButton.Right) _zoom = mb.Pressed && captured;
            return;
        }

        if (e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _help.Visible = false;
            return;
        }

        // Ships and airships: W / S move the engine telegraph a notch, Space orders all stop (ships).
        if (captured && Mounted && (_mount.IsNaval || _mount.FlightMode == MobilityMode.Airship) && _mount.PlayerSeat?.Role == SeatRole.Driver)
        {
            int notch = e.IsActionPressed("move_forward") ? 1 : e.IsActionPressed("move_back") ? -1 : 0;
            if (notch != 0)
            {
                _mount.StepTelegraph(notch);
                Big(_mount.TelegraphName);
                return;
            }

            if (_mount.IsNaval && !_mount.IsSubmarine && e.IsActionPressed("brake"))
            {
                _mount.AllStop();
                Big(_mount.TelegraphName);
                return;
            }
        }

        if (Mounted && e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.PhysicalKeycode == Key.Key1) TrySwitchSeat(SeatRole.Driver);
            else if (k.PhysicalKeycode == Key.Key2) TrySwitchSeat(SeatRole.Gunner);
            else if (k.PhysicalKeycode == Key.Key3) TrySwitchSeat(SeatRole.Commander);
        }

        if (e.IsActionPressed("interact")) _interactQueued = true; // handled in the physics step, where ray tests are safe
        else if (e.IsActionPressed("toggle_ammo") && Mounted) _mount.ToggleAmmo();
        else if (e.IsActionPressed("recover") && Mounted) _mount.Recover();
        else if (e.IsActionPressed("gunner_mode") && Mounted) ToggleGunnerMode();
        else if (e.IsActionPressed("torpedo") && Mounted) _torpedoQueued = true;
        else if (e.IsActionPressed("missile") && Mounted) _missileQueued = true;
        else if (e.IsActionPressed("bomb") && Mounted) _bombQueued = true;
        else if (e.IsActionPressed("vtol") && Mounted) _vtolQueued = true;
        else if (e.IsActionPressed("respawn")) RespawnAll();
        else if (e.IsActionPressed("reset_targets"))
        {
            SpawnTargets();
            Big("Targets reset");
        }
        else if (e.IsActionPressed("live_fire")) ToggleLiveFire();
        else if (e.IsActionPressed("battle_drill")) ToggleDrill();
        else if (e.IsActionPressed("perf_overlay")) _perf.Toggle();
        else if (e.IsActionPressed("to_builder")) ExitRequested?.Invoke();
        else if (e.IsActionPressed("help")) _help.Visible = !_help.Visible;
    }

    private void LaunchMissile()
    {
        VehicleBody v = _mount;
        if (v.FireMissile(true, v.PlayerInput.Aim))
        {
            MissileInfo next = v.Analysis.Missiles.FirstOrDefault(m => m.Valid);
            string how = next?.Stats.Seeker switch
            {
                SeekerKind.Optical => "keep the crosshair on the target",
                SeekerKind.Radar => _lock != null ? $"locked on {_lock.DisplayName}" : "no lock: it will look for one",
                _ => "unguided",
            };
            AddFeed($"[color={Ui.Hex(Ui.Info)}]MISSILE AWAY  ·  {how}[/color]", true);
            return;
        }

        Big("No missile ready");
        Sfx.Play2D(this, SoundId.Error, -10f);
    }

    private void LaunchTorpedo()
    {
        VehicleBody v = _mount;
        if (v.FireTorpedo(true, v.PlayerInput.Aim))
        {
            AddFeed($"[color={Ui.Hex(Ui.Info)}]TORPEDO AWAY  ·  {v.TorpedoesLeft} left[/color]", true);
            _trauma = Mathf.Min(1f, _trauma + 0.15f);
            return;
        }

        Big(v.Tubes.Count == 0 ? "No torpedo tubes" : v.Tubes.All(t => !t.Alive) ? "Torpedo tubes destroyed"
            : v.TorpedoesLeft == 0 ? "Out of torpedoes" : "Tubes reloading");
        Sfx.Play2D(this, SoundId.Error, -10f);
    }

    private void Interact()
    {
        if (!_me.Alive) return;
        if (Mounted)
        {
            ExitVehicle();
            return;
        }

        VehicleBody v = _lookedAt;
        if (v == null || !IsInstanceValid(v)) return;
        if (v.Faction != 0)
        {
            Big("That's not one of yours");
            return;
        }

        EnterVehicle(v, null);
    }

    private void ToggleGunnerMode()
    {
        _mount.GunnerFollowsPlayer = !_mount.GunnerFollowsPlayer;
        Big(_mount.GunnerFollowsPlayer ? "Gunner: following your aim" : "Gunner: weapons free (picks its own targets)");
        if (!_mount.GunnerFollowsPlayer && !_liveFire) AddFeed($"[color={Ui.Hex(Ui.Dim)}]Turn on live fire (L) for AI gunners to shoot on their own.[/color]", true);
    }

    private void ToggleLiveFire()
    {
        _liveFire = !_liveFire;
        foreach (IGridHost h in GridRegistry.Hosts)
            if (h is VehicleBody v) v.AiCrewActive = _liveFire;
        Big(_liveFire ? "LIVE FIRE: the parked vehicles shoot back" : "Live fire off");
        Sfx.Play2D(this, _liveFire ? SoundId.Reload : SoundId.Tick, -4f);
    }

    private void RespawnAll()
    {
        _deadTime = -1f;
        if (!_me.Alive) _me.Respawn(SpawnPoint, 0f);
        SpawnVehicle();
        EnterVehicle(_vehicle, SeatRole.Driver);
    }

    // ---------- HUD ----------

    private void BuildHud()
    {
        Control root = Ui.Root(this);
        _underwaterTint = new ColorRect { Color = new Color(0.03f, 0.2f, 0.24f, 0.32f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        root.AddChild(_underwaterTint);
        _underwaterTint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _vignette = new ColorRect { Color = new Color(0.6f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_vignette);
        _vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _reticle = new ReticleOverlay { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_reticle);
        _reticle.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _vehicleHud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_vehicleHud);
        _vehicleHud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _footHud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_footHud);
        _footHud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Vehicle: status (top left), crew (under it), guns (bottom middle).
        VBoxContainer left = Ui.Column(8);
        _vehicleHud.AddChild(left);
        left.Position = new Vector2(12, 12);
        var statusPanel = new PanelContainer();
        left.AddChild(statusPanel);
        _status = Ui.Rich();
        _status.CustomMinimumSize = new Vector2(330, 0);
        statusPanel.AddChild(_status);
        var crewPanel = new PanelContainer();
        left.AddChild(crewPanel);
        _crew = Ui.Rich();
        _crew.CustomMinimumSize = new Vector2(330, 0);
        crewPanel.AddChild(_crew);

        var gunPanel = new PanelContainer();
        _vehicleHud.AddChild(gunPanel);
        gunPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        gunPanel.GrowHorizontal = Control.GrowDirection.Both;
        gunPanel.GrowVertical = Control.GrowDirection.Begin;
        gunPanel.OffsetBottom = -12;
        VBoxContainer gunCol = Ui.Column(6);
        gunPanel.AddChild(gunCol);
        _guns = Ui.Rich();
        _guns.CustomMinimumSize = new Vector2(480, 0);
        gunCol.AddChild(_guns);
        _gunList = Ui.Column(4);
        gunCol.AddChild(_gunList);

        // On foot: health (bottom left), weapon (bottom right), welder (bottom middle).
        var healthPanel = new PanelContainer();
        _footHud.AddChild(healthPanel);
        healthPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        healthPanel.GrowVertical = Control.GrowDirection.Begin;
        healthPanel.OffsetLeft = 14;
        healthPanel.OffsetBottom = -40;
        VBoxContainer hcol = Ui.Column(4);
        hcol.CustomMinimumSize = new Vector2(240, 0);
        healthPanel.AddChild(hcol);
        hcol.AddChild(Ui.Caption("Health"));
        _healthLabel = Ui.Value("100");
        hcol.AddChild(_healthLabel);
        _healthBar = new StatBar();
        hcol.AddChild(_healthBar);

        var weaponPanel = new PanelContainer();
        _footHud.AddChild(weaponPanel);
        weaponPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        weaponPanel.GrowHorizontal = Control.GrowDirection.Begin;
        weaponPanel.GrowVertical = Control.GrowDirection.Begin;
        weaponPanel.OffsetRight = -14;
        weaponPanel.OffsetBottom = -14;
        VBoxContainer wcol = Ui.Column(4);
        wcol.CustomMinimumSize = new Vector2(260, 0);
        weaponPanel.AddChild(wcol);
        _weaponName = Ui.Heading("Rifle", Ui.AccentBright);
        wcol.AddChild(_weaponName);
        _weaponAmmo = Ui.Value("30 / 240");
        wcol.AddChild(_weaponAmmo);
        _weaponReload = new StatBar();
        wcol.AddChild(_weaponReload);
        _slots = Ui.Small("");
        wcol.AddChild(_slots);

        _weldPanel = new PanelContainer { Visible = false };
        _footHud.AddChild(_weldPanel);
        _weldPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _weldPanel.GrowHorizontal = Control.GrowDirection.Both;
        _weldPanel.GrowVertical = Control.GrowDirection.Begin;
        _weldPanel.OffsetBottom = -110;
        VBoxContainer weldCol = Ui.Column(4);
        weldCol.CustomMinimumSize = new Vector2(300, 0);
        _weldPanel.AddChild(weldCol);
        _weldLabel = Ui.Label("", 14);
        weldCol.AddChild(_weldLabel);
        _weldBar = new StatBar();
        weldCol.AddChild(_weldBar);

        _prompt = Ui.Label("", 18, Ui.AccentBright, "Heading");
        _footHud.AddChild(_prompt);
        _prompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _prompt.GrowHorizontal = Control.GrowDirection.Both;
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt.OffsetTop = 46;
        _prompt.AddThemeConstantOverride("outline_size", 6);

        // Shared: hit feed, live-fire tag, messages, hints, help.
        var feedPanel = new PanelContainer();
        root.AddChild(feedPanel);
        feedPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        feedPanel.GrowHorizontal = Control.GrowDirection.Begin;
        feedPanel.OffsetRight = -12;
        feedPanel.OffsetTop = 12;
        _feed = Ui.Rich();
        _feed.CustomMinimumSize = new Vector2(380, 0);
        feedPanel.AddChild(_feed);

        _liveChip = Ui.Label("", 15, Ui.Bad, "Heading");
        root.AddChild(_liveChip);
        _liveChip.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _liveChip.GrowHorizontal = Control.GrowDirection.Both;
        _liveChip.HorizontalAlignment = HorizontalAlignment.Center;
        _liveChip.OffsetTop = 14;
        _liveChip.AddThemeConstantOverride("outline_size", 6);

        Label hint = Ui.Small("F1 help   ·   E get in / out   ·   M tactical map   ·   F torpedo / missile   ·   B bomb   ·   L live fire   ·   F8 battle drill   ·   F3 performance   ·   Tab builder");
        root.AddChild(hint);
        hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        hint.GrowVertical = Control.GrowDirection.Begin;
        hint.OffsetLeft = 14;
        hint.OffsetBottom = -10;
        hint.AddThemeConstantOverride("outline_size", 4);

        SetupDrill(root);
        SetupCommand(root);

        _big = Ui.Label("", 26, Ui.Accent, "Title");
        root.AddChild(_big);
        _big.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _big.GrowHorizontal = Control.GrowDirection.Both;
        _big.HorizontalAlignment = HorizontalAlignment.Center;
        _big.OffsetTop = 90;
        _big.AddThemeConstantOverride("outline_size", 8);

        _resume = Ui.Label("Mouse released. Click to keep playing.", 20);
        root.AddChild(_resume);
        _resume.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _resume.GrowHorizontal = Control.GrowDirection.Both;
        _resume.HorizontalAlignment = HorizontalAlignment.Center;
        _resume.OffsetTop = 90;
        _resume.AddThemeConstantOverride("outline_size", 6);

        _help = new PanelContainer { Visible = false };
        root.AddChild(_help);
        _help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        VBoxContainer helpCol = Ui.Column(8);
        _help.AddChild(helpCol);
        helpCol.AddChild(Ui.Title("TEST RANGE"));
        RichTextLabel text = Ui.Rich();
        text.CustomMinimumSize = new Vector2(620, 0);
        text.Text =
            "[b][color=#ffb845]In a vehicle[/color][/b]\n" +
            "W / S throttle · A / D steer (tanks turn on the spot) · Space brake · R flip upright\n" +
            "Mouse aims: turrets turn and guns elevate on their own. The circles show where each gun will land (green = loaded).\n" +
            "Left click fire · Right click zoom (the commander seat zooms much further) · Q switch AP / HE\n" +
            "1 / 2 / 3 switch to the driver, gunner or commander seat · G gunner follows your aim / picks its own targets\n" +
            "E get out\n\n" +
            "[b][color=#ffb845]At sea[/color][/b]\n" +
            "Ships and submarines start off the east coast. W / S move the engine telegraph a notch (it holds the speed, so you can leave the wheel for a gun) · A / D rudder · Space all stop\n" +
            "Submarines: hold C to order the boat deeper, hold Space to come up. The crew floods and blows the ballast tanks to hold the depth you order.\n" +
            "F fires a torpedo from any seat. It turns onto the bearing you're aiming at, then runs straight just under the surface. Torpedoes sink ships by flooding them. X fires missiles, if the ship carries any.\n" +
            "Holes below the waterline flood compartments: get out, weld them shut (3), and the bilge pumps clear the water. Stop inside the buoy ring to rearm and refuel.\n" +
            "In the water you swim: Space up, C down, E to climb into a boat you're looking at.\n\n" +
            "[b][color=#ffb845]In the air[/color][/b]\n" +
            "Planes start on the airstrip in the western hills: W / S set the throttle, and the plane flies toward wherever you point the mouse (A / D roll, Space brakes on the ground). Open the throttle, let it run, and climb once you're past stall speed.\n" +
            "Helicopters and drones start on the helipad: W A S D move, Space / C climb and descend, and they turn to face where you look. Let go and they stop and hover. V switches a VTOL between hovering and wing-borne flight.\n" +
            "Airships: W / S telegraph, A / D turn, Space / C order it higher or lower. B drops a bomb where the diamond is. F (or X) fires a missile: optical ones fly to your crosshair, radar ones chase the aircraft in the red box.\n" +
            "Q cycles AP, HE and FLAK: flak bursts near aircraft, or at the range you're aiming at. Drones are flown by remote: you stay standing where you were.\n\n" +
            "[b][color=#ffb845]On foot[/color][/b]\n" +
            "W A S D move · Shift sprint · Space jump · Mouse look · Right click aim\n" +
            "1 rifle · 2 AT rocket · 3 welding torch · Left click fire · R reload\n" +
            "The torch repairs damaged blocks and rebuilds destroyed ones (blue outlines): aim at them and hold the trigger.\n" +
            "E get into the vehicle you're looking at. The supply pad heals you and refills ammo and fuel.\n\n" +
            "[b][color=#ffb845]Range[/color][/b]\n" +
            "L live fire: the parked vehicles' crews shoot back · T reset targets · Backspace respawn · Tab builder · Esc free the mouse\n" +
            "M tactical map (fleet command), from a vehicle with a command console or at the command post by the depot: pick units, right click to attack, escort or move them in formation; J jumps into one; Deploy calls more in\n" +
            "F8 battle drill: two armies (about 100 vehicles; again for about 200, again to clear) fight across the range · F3 performance readout\n" +
            "[color=#73f27f]PEN[/color] got through · [color=#ff6b61]NO PEN[/color] armor stopped it (angled armor counts as thicker) · " +
            "[color=#ffcc66]RICOCHET[/color] bounced off a steep angle";
        helpCol.AddChild(text);
        helpCol.AddChild(Ui.Button("Back", () => _help.Visible = false, null, "PrimaryButton"));
    }

    private void Big(string text)
    {
        _big.Text = text;
        _bigTime = 3f;
    }

    private void AddFeed(string bbcode, bool front = false)
    {
        _feedLines.Add((bbcode, 0f));
        while (_feedLines.Count > 8) _feedLines.RemoveAt(0);
    }

    private static void Paint(Label l, string text, Color c)
    {
        l.Text = text;
        l.AddThemeColorOverride("font_color", c);
    }

    private void BuildGunRows()
    {
        foreach (Node n in _gunList.GetChildren()) n.QueueFree();
        _gunRows.Clear();
        if (!Mounted) return;
        // One row per kind of gun, biggest first, so a battleship's 84 guns take three lines.
        foreach (IGrouping<string, VehicleBody.GunRt> kind in _mount.Guns.GroupBy(g => g.Label).OrderByDescending(k => k.First().Stats.CaliberMm))
        {
            PanelContainer card = Ui.Card();
            VBoxContainer col = Ui.Column(4);
            card.AddChild(col);
            HBoxContainer top = Ui.Row(10);
            col.AddChild(top);
            var row = new GunRow { Name = Ui.Heading(kind.Key), State = Ui.Caption("READY", Ui.Good), Ammo = Ui.Small(""), Reload = new StatBar() };
            row.Guns.AddRange(kind);
            if (row.Guns.Count > 1) row.Name.Text = $"{kind.Key} × {row.Guns.Count}";
            top.AddChild(row.Name);
            top.AddChild(row.State);
            top.AddChild(Ui.Spacer());
            top.AddChild(row.Ammo);
            col.AddChild(row.Reload);
            _gunList.AddChild(card);
            _gunRows.Add(row);
        }
    }

    private void UpdateHud(float dt)
    {
        _resume.Visible = Input.MouseMode != Input.MouseModeEnum.Captured && !_help.Visible;
        if (_bigTime > 0)
        {
            _bigTime -= dt;
            _big.Modulate = new Color(1, 1, 1, Mathf.Clamp(_bigTime, 0, 1));
        }

        for (int i = _feedLines.Count - 1; i >= 0; i--)
        {
            _feedLines[i] = (_feedLines[i].text, _feedLines[i].age + dt);
            if (_feedLines[i].age > 8f) _feedLines.RemoveAt(i);
        }

        _feed.Text = _feedLines.Count == 0
            ? $"[color={Ui.Hex(Ui.Dim)}]Hits show up here.[/color]"
            : string.Join("\n", _feedLines.Select(l => l.text));
        _liveChip.Text = _liveFire ? "LIVE FIRE" : "";

        float lowHealth = _me.Alive && _me.Active ? Mathf.Clamp(1f - _me.Health / 50f, 0, 1) * 0.25f : 0f;
        _vignette.Color = new Color(0.6f, 0, 0, Mathf.Clamp(_hurtFlash * 0.45f + lowHealth, 0, 0.6f));

        bool mounted = Mounted;
        _vehicleHud.Visible = mounted;
        _footHud.Visible = !mounted && _me.Alive;
        _reticle.HitFlash = Mathf.Max(0, _reticle.HitFlash - dt * 3f);
        _reticle.Marks.Clear();
        Camera3D cam = mounted ? _cam : _me.Camera;
        foreach ((Vector3 p, bool ready) in _impacts)
            if (!cam.IsPositionBehind(p)) _reticle.Marks.Add((cam.UnprojectPosition(p), ready));
        _reticle.BombMark = mounted && _bombImpact is Vector3 bomb && !cam.IsPositionBehind(bomb) ? cam.UnprojectPosition(bomb) : null;
        Vector3? lockAt = mounted && _lock != null && IsInstanceValid(_lock) ? _lock.GlobalTransform * _lock.CenterOfMass : null;
        _reticle.LockMark = lockAt is Vector3 la && !cam.IsPositionBehind(la) ? cam.UnprojectPosition(la) : null;
        _reticle.QueueRedraw();

        if (!_me.Alive)
        {
            _footHud.Visible = false;
            return;
        }

        if (mounted) UpdateVehicleHud();
        else UpdateFootHud();
    }

    private void UpdateVehicleHud()
    {
        VehicleBody v = _mount;
        var sb = new StringBuilder();
        sb.Append($"[b]{v.DisplayName}[/b]\n");
        if (v.IsNaval) NavalStatus(v, sb);
        else if (v.IsAir) AirStatus(v, sb);
        else sb.Append($"[font_size=22][b]{Mathf.Abs(v.SpeedKmh):0}[/b][/font_size] km/h   ");
        sb.Append($"Fuel {v.FuelL:0}/{v.FuelCapacityL:0} L   Engines {v.UsablePowerKw:0} kW\n");
        if (!v.DriverAlive) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Driver knocked out. Get out (E) and weld the seat back, or Backspace.[/color]\n");
        else if (v.PowerKw <= 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Engines destroyed.[/color]\n");
        else if (v.EngineDrowned) sb.Append($"[color={Ui.Hex(Ui.Bad)}]{(v.IsNaval ? "Engine room flooded: pump it out or weld the holes." : "Engine flooded: it won't run under water. Backspace to respawn.")}[/color]\n");
        else if (v.FuelL <= 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Out of fuel: {(v.IsNaval ? "stop inside the buoy ring." : "drive onto the supply pad.")}[/color]\n");
        else if (!v.IsNaval && !v.IsAir && v.Wheels.Count == 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]No wheels left.[/color]\n");
        else if (v.IsNaval && v.PropsAlive == 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Propellers destroyed.[/color]\n");
        if (!v.IsNaval && v.Submerged > 0.25f) sb.Append($"[color={Ui.Hex(Ui.Warn)}]Deep water: land vehicles sink. Back out while the engine still runs.[/color]\n");
        if (v.IsUpsideDownOrStuck && (!v.IsAir || v.OnGround)) sb.Append($"[color={Ui.Hex(Ui.Warn)}]Tipped over: press R to get back up.[/color]\n");
        if (_resupplying) sb.Append($"[color={Ui.Hex(Ui.Good)}]Resupplying...[/color]\n");
        _missingTimer -= (float)GetProcessDeltaTime();
        if (_missingTimer <= 0)
        {
            _missingTimer = 0.5f;
            _missing = v.MissingCount;
        }

        int missing = _missing;
        if (missing > 0) sb.Append($"[color={Ui.Hex(Ui.Dim)}]{missing} block(s) missing: get out and weld them back (3).[/color]\n");
        _status.Text = sb.ToString().TrimEnd('\n');

        var crew = new StringBuilder("[b]CREW[/b]\n");
        int n = 0;
        foreach (SeatRt s in v.Seats)
        {
            n++;
            string who = !v.SeatAlive(s) ? $"[color={Ui.Hex(Ui.Bad)}]knocked out[/color]"
                : s.Occupant == Occupant.Player ? $"[color={Ui.Hex(Ui.Accent)}]You[/color]"
                : s.Occupant == Occupant.Ai ? "AI crew" : $"[color={Ui.Hex(Ui.Dim)}]empty[/color]";
            string key = s.Role == SeatRole.Driver ? "1" : s.Role == SeatRole.Gunner ? "2" : "3";
            crew.Append($"[color={Ui.Hex(Ui.Dim)}]{key}[/color]  {Crew.Name(s.Role)}: {who}\n");
        }

        if (n == 0) crew.Append("No seats.\n");
        if (v.Seats.Any(s => s.Role == SeatRole.Gunner))
            crew.Append($"[color={Ui.Hex(Ui.Dim)}]G  gunner {(v.GunnerFollowsPlayer ? "follows your aim" : "picks its own targets")}[/color]");
        _crew.Text = crew.ToString().TrimEnd('\n');

        var gs = new StringBuilder();
        bool ap = v.SelectedAmmo == AmmoType.AP;
        string Pick(AmmoType t, string name) => v.SelectedAmmo == t ? $"[b][color=#ffb845][{name}][/color][/b]" : name;
        gs.Append($"Ammo: {Pick(AmmoType.AP, "AP")}  {Pick(AmmoType.HE, "HE")}{(v.HasFlak ? "  " + Pick(AmmoType.Flak, "FLAK") : "")}   [color={Ui.Hex(Ui.Dim)}](Q switches)[/color]");
        if (v.Guns.Count == 0) gs.Append($"\n[color={Ui.Hex(Ui.Dim)}]No guns on this design.[/color]");
        _guns.Text = gs.ToString();

        foreach (GunRow row in _gunRows)
        {
            int apRounds = 0, heRounds = 0, flakRounds = 0, alive = 0, crewed = 0, loaded = 0, ready = 0, mine = 0, masked = 0;
            float nextReload = float.MaxValue, progress = 0;
            bool hasFlak = false;
            var counted = new HashSet<Int3>();
            foreach (VehicleBody.GunRt g in row.Guns)
            {
                hasFlak |= g.Info.FlakRacks.Count > 0;
                if (!g.Alive) continue;
                alive++;
                // Guns that share racks share their rounds: count each rack once.
                if (counted.Add(g.Info.ApRacks.Count > 0 ? g.Info.ApRacks[0] : g.Info.Breech)) apRounds += v.Rounds(g, AmmoType.AP);
                if (counted.Add(g.Info.HeRacks.Count > 0 ? g.Info.HeRacks[0] : g.Info.Breech + Int3.Up)) heRounds += v.Rounds(g, AmmoType.HE);
                if (counted.Add(g.Info.FlakRacks.Count > 0 ? g.Info.FlakRacks[0] : g.Info.Breech + Int3.Down)) flakRounds += v.Rounds(g, AmmoType.Flak);
                if (v.ControllerOf(g) == null) continue;
                crewed++;
                bool directs = v.PlayerDirects(g);
                if (directs) mine++;
                progress += 1f - g.ReloadLeft / Mathf.Max(g.Stats.ReloadS, 0.01f);
                if (g.ReloadLeft > 0)
                {
                    nextReload = Mathf.Min(nextReload, g.ReloadLeft);
                    continue;
                }

                loaded++;
                if (g.Masked) masked++;
                else if (!directs || g.AimError <= 2.5f) ready++;
            }

            bool one = row.Guns.Count == 1;
            if (alive == 0) Paint(row.State, "DESTROYED", Ui.Bad);
            else if (crewed == 0) Paint(row.State, one ? "NO GUNNER" : "NO GUNNERS", Ui.Bad);
            else if (apRounds + heRounds + flakRounds == 0) Paint(row.State, "OUT OF AMMO", Ui.Bad);
            else if (one && loaded == 0) Paint(row.State, $"RELOADING {nextReload:0.0}s", Ui.Warn);
            else if (one) Paint(row.State, masked > 0 ? "BLOCKED BY OWN HULL" : mine > 0 ? "READY" : "AI GUNNER", masked > 0 ? Ui.Warn : mine > 0 ? Ui.Good : Ui.Info);
            else if (loaded == 0) Paint(row.State, $"RELOADING {nextReload:0.0}s", Ui.Warn);
            else Paint(row.State, mine > 0 ? $"{ready} BEAR · {loaded} LOADED" : $"AI GUNNERS · {loaded} LOADED", mine > 0 ? (ready > 0 ? Ui.Good : Ui.Warn) : Ui.Info);
            row.Reload.Set(crewed > 0 ? progress / crewed : 0f, 1f, loaded == 0 ? Ui.Warn : Ui.Good);
            float pen = row.Guns[0].Stats.ApPenMm;
            row.Ammo.Text = $"AP {apRounds}   HE {heRounds}{(hasFlak ? $"   FLAK {flakRounds}" : "")}   ·   {pen:0} mm pen";
        }
    }

    /// <summary>Airspeed, height, climb, throttle or lift, stall warnings, bombs and missiles.</summary>
    private void AirStatus(VehicleBody v, StringBuilder sb)
    {
        string dim = Ui.Hex(Ui.Dim);
        sb.Append($"[font_size=22][b]{v.AirspeedKmh:0}[/b][/font_size] km/h   ");
        sb.Append($"[b]{v.HeightAboveGroundM:0}[/b] m up [color={dim}]({v.AltitudeM:0} m above sea)[/color]   ");
        sb.Append($"climb {v.ClimbRate:+0.0;-0.0} m/s\n");
        switch (v.FlightMode)
        {
            case MobilityMode.Plane:
                sb.Append($"Throttle [b]{v.AirThrottle * 100f:0}%[/b]   [color={dim}]W / S throttle · mouse steers · A / D roll{(v.OnGround ? " · Space brakes" : "")}[/color]\n");
                if (v.Stalled) sb.Append($"[color={Ui.Hex(Ui.Bad)}][b]STALL[/b]: push the nose down and open the throttle.[/color]\n");
                else if (!v.OnGround && v.AirspeedKmh < v.Analysis.Air.StallSpeedMs * 3.6f * 1.1f)
                    sb.Append($"[color={Ui.Hex(Ui.Warn)}]Slow: it stalls below about {v.Analysis.Air.StallSpeedMs * 3.6f:0} km/h.[/color]\n");
                else if (v.OnGround && v.AirspeedKmh < v.Analysis.Air.StallSpeedMs * 3.6f)
                    sb.Append($"[color={dim}]Full throttle, then pull the mouse up once you pass {v.Analysis.Air.StallSpeedMs * 3.6f * 0.9f:0} km/h.[/color]\n");
                if (!v.OnGround && v.HeightAboveGroundM < 40f && v.ClimbRate < -8f) sb.Append($"[color={Ui.Hex(Ui.Bad)}][b]PULL UP[/b][/color]\n");
                if (v.CanSwitchVtol) sb.Append($"[color={dim}]V switches back to hovering[/color]\n");
                break;
            case MobilityMode.Airship:
                sb.Append($"[b][color=#ffb845]{v.TelegraphName}[/color][/b]   holding {v.HoldAltitude:0} m   gas {v.LiftOutput * 100f:0}%\n");
                sb.Append($"[color={dim}]W / S telegraph · A / D turn · Space / C higher / lower[/color]\n");
                break;
            default:
                sb.Append($"Lift [b]{v.LiftOutput * 100f:0}%[/b]   [color={dim}]W A S D move · Space / C up and down · it faces where you look[/color]\n");
                if (v.LiftOutput > 0.97f) sb.Append($"[color={Ui.Hex(Ui.Warn)}]At full power: it can't climb any harder.[/color]\n");
                if (v.CanSwitchVtol) sb.Append($"[color={dim}]V switches to wing-borne flight[/color]\n");
                break;
        }

        if (v.Analysis.BombRacks.Count > 0) sb.Append($"Bombs [b]{v.BombsLeft}[/b] [color={dim}]B drops · the diamond shows where[/color]   ");
        if (v.Analysis.Missiles.Count > 0)
        {
            sb.Append($"Missiles [b]{v.MissilesReady}[/b] [color={dim}]{(v.Tubes.Count > 0 ? "X" : "F or X")} fires[/color]");
            if (_lock != null) sb.Append($"   [color={Ui.Hex(Ui.Bad)}]LOCK {_lock.DisplayName}[/color]");
        }

        if (v.Analysis.BombRacks.Count > 0 || v.Analysis.Missiles.Count > 0) sb.Append('\n');
    }

    /// <summary>Speed in knots, the telegraph, rudder, depth and ballast, flooding, and torpedoes.</summary>
    private static void NavalStatus(VehicleBody v, StringBuilder sb)
    {
        string dim = Ui.Hex(Ui.Dim);
        float kn = Mathf.Abs(v.SpeedKmh) / 1.852f;
        sb.Append($"[font_size=22][b]{kn:0}[/b][/font_size] kn [color={dim}]({Mathf.Abs(v.SpeedKmh):0} km/h)[/color]   ");
        sb.Append($"[b][color=#ffb845]{v.TelegraphName}[/color][/b]\n");
        float deg = v.RudderAngle * 35f;
        string rudder = Mathf.Abs(deg) < 2f ? "amidships" : $"{Mathf.Abs(deg):0}° {(deg < 0 ? "port" : "starboard")}";
        sb.Append($"Rudder {rudder}   ");
        if (v.IsSubmarine)
        {
            string depth = v.DepthM <= 0.2f ? "surfaced" : $"{v.DepthM:0.0} m";
            string order = v.TargetDepth < 0.1f ? "surface" : $"{v.TargetDepth:0} m";
            string tanks = v.BallastCommand > 0 ? "flooding" : v.BallastCommand < 0 ? "blowing" : "holding";
            sb.Append($"\nDepth [b]{depth}[/b]  [color={dim}]ordered {order}[/color]   Ballast {v.BallastFill * 100f:0}% [color={dim}]{tanks}[/color]");
            if (v.ValvesAlive == 0) sb.Append($"  [color={Ui.Hex(Ui.Bad)}]valves destroyed[/color]");
        }

        sb.Append('\n');
        float cap = v.FloodCapacity;
        if (cap > 0)
        {
            var parts = v.Analysis.Compartments.Where(c => !c.IsBallast && c.Cells.Count > 8).ToList();
            float flood = v.FloodWater / cap;
            if (flood > 0.005f || v.TakingWater)
            {
                string word = v.TakingWater ? "TAKING WATER" : "Flooded";
                sb.Append($"[color={Ui.Hex(v.TakingWater ? Ui.Bad : Ui.Warn)}]{word} {flood * 100f:0}%[/color]  ");
                foreach (Compartment c in parts)
                {
                    Color col = c.FillFraction > 0.6f ? Ui.Bad : c.FillFraction > 0.05f ? Ui.Warn : Ui.Good;
                    sb.Append($"[color={Ui.Hex(col)}]■[/color]");
                }

                sb.Append($"  [color={dim}]{v.PumpsAlive} pump(s)[/color]\n");
            }
        }

        if (v.Tubes.Count > 0)
        {
            int ready = v.Tubes.Count(t => t.Alive && t.ReloadLeft <= 0);
            float next = v.Tubes.Where(t => t.Alive && t.ReloadLeft > 0).Select(t => t.ReloadLeft).DefaultIfEmpty(0).Min();
            sb.Append($"Torpedoes [b]{v.TorpedoesLeft}[/b]   tubes ready {ready}/{v.Tubes.Count(t => t.Alive)}");
            if (ready == 0 && next > 0) sb.Append($" [color={dim}](next in {next:0}s)[/color]");
            sb.Append($"   [color={dim}]F fires[/color]\n");
        }
    }

    private void UpdateFootHud()
    {
        _healthLabel.Text = $"{_me.Health:0}";
        _healthBar.Set(_me.Health, InfantryDamage.MaxHealth, Ui.Grade(_me.Health, 30, 60));

        CarriedWeapon w = _me.Current;
        _weaponName.Text = w.Name;
        if (w.IsWelder)
        {
            _weaponAmmo.Text = "Hold to weld";
            _weaponReload.Set(0, 1, Ui.Info);
        }
        else
        {
            _weaponAmmo.Text = w.ReloadLeft > 0 ? "Reloading..." : $"{w.Mag} / {w.Reserve}";
            _weaponReload.Set(w.ReloadLeft > 0 ? 1f - w.ReloadLeft / w.Spec.ReloadS : (float)w.Mag / w.Spec.MagSize, 1f,
                w.ReloadLeft > 0 ? Ui.Warn : Ui.Accent);
        }

        string[] names = { "1 Rifle", "2 Rocket", "3 Torch" };
        _slots.Text = string.Join("   ", names.Select((s, i) => i == _me.Slot ? $"[{s}]" : s));

        (WeldState state, float progress, string block) = _me.WeldStatus;
        _weldPanel.Visible = w.IsWelder && state != WeldState.Nothing;
        if (_weldPanel.Visible)
        {
            string what = block ?? "block";
            (string text, Color c) = state switch
            {
                WeldState.Repairing => ($"Repairing {what}", Ui.Info),
                WeldState.Rebuilding => ($"Rebuilding {what}", Ui.Info),
                WeldState.Rebuilt => ($"{what} rebuilt", Ui.Good),
                WeldState.AlreadyFixed => ($"{what} is in good shape", Ui.Good),
                _ => ($"{what} has nothing to attach to yet: rebuild what's under it first", Ui.Warn),
            };
            _weldLabel.Text = text;
            _weldLabel.AddThemeColorOverride("font_color", c);
            _weldBar.Set(progress, 1f, c);
        }

        VehicleBody looked = _lookedAt != null && IsInstanceValid(_lookedAt) ? _lookedAt : null;
        _prompt.Text = looked == null ? "" : looked.Faction != 0 ? "Enemy vehicle"
            : !looked.HasFreeSeat ? "No free seats"
            : looked.Seats.All(s => s.Info.Remote) ? $"E  fly {looked.DisplayName} by remote" : $"E  get into {looked.DisplayName}";
        if (_me.Swimming && _prompt.Text.Length == 0) _prompt.Text = "Swimming  ·  Space up  ·  C down";
        if (_resupplying) _prompt.Text = (_prompt.Text.Length > 0 ? _prompt.Text + "\n" : "") + "Resupplying...";
    }

    private void OnFired(VehicleBody.GunRt g)
    {
        float c = g.Stats.CaliberMm;
        _trauma = Mathf.Min(1f, _trauma + 0.22f + c / 450f);
        // A whole broadside going off at once kicks no harder than its biggest gun twice over.
        _fovKick = Mathf.Min(_fovKick + 2f + c / 28f, Mathf.Max(_fovKick, 2f * (2f + c / 28f)));
    }

    private void OnBlast(Vector3 pos, float radius, float damage)
    {
        Camera3D cam = Mounted ? _cam : _me.Camera;
        float d = cam.GlobalPosition.DistanceTo(pos);
        float reach = Mathf.Max(radius * 18f, 12f);
        float k = Mathf.Clamp(damage / 450f, 0.15f, 1.2f) * Mathf.Clamp(1f - d / reach, 0, 1);
        _trauma = Mathf.Min(1f, _trauma + k * 0.75f);
    }

    private void OnCombatEvent(CombatEvent e)
    {
        if (!e.ByPlayer) return;
        if (e.Report.Kind == HitKind.Penetration || e.BlocksDestroyed > 0)
        {
            _reticle.HitFlash = 1f;
            Sfx.Play2D(this, SoundId.Tick, -6f, 1.35f);
        }

        if (e.Small) return;
        HitReport r = e.Report;
        string destroyed = e.BlocksDestroyed > 0 ? $"  · {e.BlocksDestroyed} destroyed" : "";
        (string word, string line, Color c) = r.Kind switch
        {
            HitKind.Penetration => ("PEN", $"PEN  {r.BlockName}  {r.PenMm:0} vs {r.ArmorMm:0} mm{destroyed}", Ui.Good),
            HitKind.NoPenetration => ("NO PEN",
                $"NO PEN  {r.BlockName}  {r.PenMm:0} vs {r.ArmorMm:0} mm{(r.AngleDeg >= 15 ? $" (angled {r.AngleDeg:0}°)" : "")}", Ui.Bad),
            HitKind.Ricochet => ("RICOCHET", $"RICOCHET  {r.AngleDeg:0}° off {r.BlockName}", Ui.Warn),
            _ => ("BOOM", $"BOOM  {r.BlockName}{destroyed}", Ui.Accent),
        };
        AddFeed($"[color={Ui.Hex(c)}]{line}[/color]");
        FloatText(e.Position, word, c);
    }

    private void FloatText(Vector3 pos, string text, Color c)
    {
        var fx = new FxNode { Life = 1.6f };
        AddChild(fx);
        fx.GlobalPosition = pos;
        Camera3D cam = Mounted ? _cam : _me.Camera;
        float dist = cam.GlobalPosition.DistanceTo(pos);
        var label = new Label3D
        {
            Text = text, FontSize = 40, OutlineSize = 10, Modulate = c, NoDepthTest = true, Font = Ui.HeadingBoldFont,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0009f * Mathf.Max(dist, 4f),
        };
        fx.AddChild(label);
        fx.Animate = u =>
        {
            label.Position = Vector3.Up * (0.4f + u * 0.06f * dist);
            label.Modulate = new Color(c.R, c.G, c.B, 1f - u * u);
        };
    }
}
