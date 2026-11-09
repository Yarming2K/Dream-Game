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

    private static readonly Vector3 SpawnPoint = new(0, 0, 120);
    private static readonly Vector3 DepotCenter = new(16, 0, 124);
    private const float DepotRadius = 9f;

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

    private sealed class GunRow
    {
        public VehicleBody.GunRt Gun;
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
        Sfx.Prewarm();
        Scenery.AddSkyAndSun(this, true);
        AddChild(Terrain.Build());
        AddRamps();
        AddMarkers();
        AddDepot();
        AddPopupTargets();
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

        SpawnVehicle();
        EnterVehicle(_vehicle, SeatRole.Driver);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // ---------- World ----------

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
            var basePos = new Vector3(x, 0, 104f);
            var center = basePos + new Vector3(0, Mathf.Sin(a) * length / 2f - thick * 0.5f * Mathf.Cos(a), -Mathf.Cos(a) * length / 2f);
            body.Transform = new Transform3D(new Basis(Vector3.Right, a), center);
            AddChild(body);
            AddChild(Sign($"{deg:0}° slope", new Vector3(x, 1.5f, 106f), 64));
        }
    }

    private void AddMarkers()
    {
        for (int d = 50; d <= 400; d += 50)
        {
            AddChild(Sign($"{d} m", new Vector3(-50f, 1.2f, SpawnPoint.Z - d), 96));
            AddChild(Sign($"{d} m", new Vector3(50f, 1.2f, SpawnPoint.Z - d), 96));
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

    private void AddPopupTargets()
    {
        (float x, float d)[] spots = { (30f, 15f), (36f, 30f), (31f, 50f), (38f, 75f), (33f, 110f) };
        foreach ((float x, float d) in spots)
        {
            var t = new PopupTarget { DistanceM = d };
            AddChild(t);
            t.GlobalPosition = new Vector3(x, 0, SpawnPoint.Z - d);
            t.Rotation = new Vector3(0, Mathf.Atan2(SpawnPoint.X - x, d) * 0.5f, 0);
            t.KnockedDown += target => AddFeed($"[color={Ui.Hex(Ui.Good)}]TARGET DOWN  {target.DistanceM:0} m[/color]", true);
        }

        AddChild(Sign("Pop-up targets", new Vector3(34f, 3f, SpawnPoint.Z - 8f), 64));
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
        AddStructure(ExampleDesigns.Wall("armor_light", 7, 5), new Vector3(-24, 0, wallZ), 0, "Light armor\n25 mm");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 7, 5), new Vector3(-8, 0, wallZ), 0, "Heavy armor\n80 mm");
        AddStructure(ExampleDesigns.Wall("armor_composite", 7, 5), new Vector3(8, 0, wallZ), 0, "Composite\n110 mm, weak to HE");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 7, 5), new Vector3(24, 0, wallZ), 45, "Heavy armor\nangled 45°");
        AddStructure(ExampleDesigns.Bunker(), new Vector3(0, 0, SpawnPoint.Z - 160f), 180, "Bunker\n(ammo inside)");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 9, 6), new Vector3(0, 0, SpawnPoint.Z - 400f), 0, "400 m");

        AddDummy(ExampleDesigns.Warden(), new Vector3(-26, 0, SpawnPoint.Z - 120f), 35f);
        AddDummy(ExampleDesigns.Jackal(), new Vector3(26, 0, SpawnPoint.Z - 110f), -60f);
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
        var where = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), pos + Vector3.Up * VehicleBody.SpawnHeight(a));
        VehicleBody v = VehicleBody.Create(bp, where, false);
        v.AiCrewActive = _liveFire;
        AddChild(v);
        _targets.Add(v);
    }

    private void SpawnVehicle()
    {
        if (_mount != null && _mount == _vehicle) ExitVehicle(silent: true);
        if (_vehicle != null && IsInstanceValid(_vehicle)) _vehicle.Vanish();
        VehicleAnalysis a = VehicleAnalysis.Analyze(_bp);
        var where = new Transform3D(Basis.Identity, SpawnPoint + Vector3.Up * VehicleBody.SpawnHeight(a));
        _vehicle = VehicleBody.Create(_bp, where, true);
        _vehicle.AiCrewActive = _liveFire;
        VehicleBody v = _vehicle;
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
            Big("Your seat was hit!");
            _mount = null;
            _me.Damage(999f);
        };
        AddChild(v);
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
        _me.SetActive(false);
        _me.GlobalPosition = v.SeatWorldPos(seat);
        _cam.MakeCurrent();
        _arm.ClearExcludedObjects();
        _arm.AddExcludedObject(v.GetRid());
        _pivot.GlobalPosition = v.GlobalTransform * v.CenterOfMass + Vector3.Up * 2f;
        Vector3 f = -v.GlobalTransform.Basis.Z;
        _yaw = Mathf.Atan2(-f.X, -f.Z);
        BuildGunRows();
        Sfx.Play2D(this, SoundId.PlaceLight, -6f, 0.8f);
        Big($"{Crew.Name(seat.Role)} seat");
    }

    private void ExitVehicle(bool silent = false)
    {
        if (!Mounted)
        {
            _mount = null;
            return;
        }

        VehicleBody v = _mount;
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
            _mount.PlayerInput = new VehicleControls
            {
                Throttle = captured && driving ? Input.GetAxis("move_back", "move_forward") : 0f,
                Steer = captured && driving ? Input.GetAxis("steer_left", "steer_right") : 0f,
                Brake = captured && driving && Input.IsActionPressed("brake"),
                HasAim = true,
                Aim = hit.Count > 0 ? hit["position"].AsVector3() : o + d * 1500f,
                Fire = captured && !_holdFire && Input.IsActionPressed("fire"),
            };
            _me.GlobalPosition = _mount.PlayerSeat != null ? _mount.SeatWorldPos(_mount.PlayerSeat) : _me.GlobalPosition;
            PredictImpacts(space, exclude);
        }
        else
        {
            _impacts.Clear();
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
            Vector3 p = v.GlobalPosition;
            if (new Vector2(p.X - DepotCenter.X, p.Z - DepotCenter.Z).Length() > DepotRadius) continue;
            bool changed = v.Resupply(dt);
            if (v == _mount && changed) _resupplying = true;
        }

        if (_me.Active && _me.Alive && _me.GlobalPosition.DistanceTo(DepotCenter) < DepotRadius)
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
        foreach (VehicleBody.GunRt g in _mount.Guns)
        {
            if (!g.Alive || g.Info.GunSection < 0 || !_mount.PlayerDirects(g)) continue;
            (Vector3 p, Vector3 dir) = _mount.Muzzle(g);
            bool ap = _mount.SelectedAmmo == AmmoType.AP;
            Vector3 v = dir * (ap ? g.Stats.MuzzleVelocity : g.Stats.HeVelocity) + _mount.LinearVelocity;
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

                p = next;
            }

            _impacts.Add((p, g.ReloadLeft <= 0 && (_mount.Rounds(g, AmmoType.AP) + _mount.Rounds(g, AmmoType.HE)) > 0));
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Mounted)
        {
            Vector3 target = _mount.GlobalTransform * _mount.CenterOfMass + Vector3.Up * 2f;
            _pivot.GlobalPosition = _pivot.GlobalPosition.Lerp(target, Mathf.Min(1f, dt * 14f));
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
        active.HOffset = n1 * 0.35f * shake;
        active.VOffset = n2 * 0.35f * shake;
        _cam.Rotation = new Vector3(n2 * 0.03f * shake, n1 * 0.03f * shake, n3 * 0.05f * shake);

        bool commander = Mounted && _mount.PlayerSeat?.Role == SeatRole.Commander;
        float zoomFov = commander ? 8f : 18f;
        float baseFov = Mathf.Lerp(_cam.Fov - _lastKick, _zoom ? zoomFov : 70f, Mathf.Min(1f, dt * 10f));
        _lastKick = _fovKick * (_zoom ? 0.3f : 1f);
        _cam.Fov = baseFov + _lastKick;

        _hurtFlash = Mathf.Max(0, _hurtFlash - dt * 1.2f);
        UpdateHud(dt);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (e is InputEventMouseMotion mm && captured && Mounted)
        {
            float sens = _zoom ? (_mount.PlayerSeat?.Role == SeatRole.Commander ? 0.0004f : 0.0009f) : 0.0028f;
            _yaw -= mm.Relative.X * sens;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * sens, -1.35f, 0.55f);
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
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _dist = Mathf.Clamp(_dist - 1.2f, 5f, 40f);
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _dist = Mathf.Clamp(_dist + 1.2f, 5f, 40f);
            if (mb.ButtonIndex == MouseButton.Right) _zoom = mb.Pressed && captured;
            return;
        }

        if (e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _help.Visible = false;
            return;
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
        else if (e.IsActionPressed("respawn")) RespawnAll();
        else if (e.IsActionPressed("reset_targets"))
        {
            SpawnTargets();
            Big("Targets reset");
        }
        else if (e.IsActionPressed("live_fire")) ToggleLiveFire();
        else if (e.IsActionPressed("to_builder")) ExitRequested?.Invoke();
        else if (e.IsActionPressed("help")) _help.Visible = !_help.Visible;
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

        Label hint = Ui.Small("F1 help   ·   E get in / out   ·   L live fire   ·   Tab builder");
        root.AddChild(hint);
        hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        hint.GrowVertical = Control.GrowDirection.Begin;
        hint.OffsetLeft = 14;
        hint.OffsetBottom = -10;
        hint.AddThemeConstantOverride("outline_size", 4);

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
            "[b][color=#ffb845]On foot[/color][/b]\n" +
            "W A S D move · Shift sprint · Space jump · Mouse look · Right click aim\n" +
            "1 rifle · 2 AT rocket · 3 welding torch · Left click fire · R reload\n" +
            "The torch repairs damaged blocks and rebuilds destroyed ones (blue outlines): aim at them and hold the trigger.\n" +
            "E get into the vehicle you're looking at. The supply pad heals you and refills ammo and fuel.\n\n" +
            "[b][color=#ffb845]Range[/color][/b]\n" +
            "L live fire: the parked vehicles' crews shoot back · T reset targets · Backspace respawn · Tab builder · Esc free the mouse\n" +
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
        foreach (VehicleBody.GunRt g in _mount.Guns)
        {
            PanelContainer card = Ui.Card();
            VBoxContainer col = Ui.Column(4);
            card.AddChild(col);
            HBoxContainer top = Ui.Row(10);
            col.AddChild(top);
            top.AddChild(Ui.Heading(g.Label));
            var row = new GunRow { Gun = g, State = Ui.Caption("READY", Ui.Good), Ammo = Ui.Small(""), Reload = new StatBar() };
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
        sb.Append($"[font_size=22][b]{Mathf.Abs(v.SpeedKmh):0}[/b][/font_size] km/h   ");
        sb.Append($"Fuel {v.FuelL:0}/{v.FuelCapacityL:0} L   Engines {v.PowerKw:0} kW\n");
        if (!v.DriverAlive) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Driver knocked out. Get out (E) and weld the seat back, or Backspace.[/color]\n");
        else if (v.PowerKw <= 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Engines destroyed.[/color]\n");
        else if (v.FuelL <= 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Out of fuel: drive onto the supply pad.[/color]\n");
        else if (v.Wheels.Count == 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]No wheels left.[/color]\n");
        if (v.IsUpsideDownOrStuck) sb.Append($"[color={Ui.Hex(Ui.Warn)}]Tipped over: press R to get back up.[/color]\n");
        if (_resupplying) sb.Append($"[color={Ui.Hex(Ui.Good)}]Resupplying...[/color]\n");
        int missing = v.MissingCount;
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
        gs.Append($"Ammo: {(ap ? "[b][color=#ffb845][AP][/color][/b]  HE" : "AP  [b][color=#ffb845][HE][/color][/b]")}   [color={Ui.Hex(Ui.Dim)}](Q switches)[/color]");
        if (v.Guns.Count == 0) gs.Append($"\n[color={Ui.Hex(Ui.Dim)}]No guns on this design.[/color]");
        _guns.Text = gs.ToString();

        foreach (GunRow row in _gunRows)
        {
            VehicleBody.GunRt g = row.Gun;
            int apRounds = v.Rounds(g, AmmoType.AP), heRounds = v.Rounds(g, AmmoType.HE);
            SeatRt who = v.ControllerOf(g);
            if (!g.Alive) Paint(row.State, "DESTROYED", Ui.Bad);
            else if (who == null) Paint(row.State, "NO GUNNER", Ui.Bad);
            else if (apRounds + heRounds == 0) Paint(row.State, "OUT OF AMMO", Ui.Bad);
            else if (g.ReloadLeft > 0) Paint(row.State, $"RELOADING {g.ReloadLeft:0.0}s", Ui.Warn);
            else Paint(row.State, v.PlayerDirects(g) ? "READY" : "AI GUNNER", v.PlayerDirects(g) ? Ui.Good : Ui.Info);
            float progress = g.Alive ? 1f - g.ReloadLeft / Mathf.Max(g.Stats.ReloadS, 0.01f) : 0f;
            row.Reload.Set(progress, 1f, g.ReloadLeft > 0 ? Ui.Warn : Ui.Good);
            row.Ammo.Text = $"AP {apRounds}   HE {heRounds}   ·   {g.Stats.ApPenMm:0} mm pen";
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
            : looked.HasFreeSeat ? $"E  get into {looked.DisplayName}" : "No free seats";
        if (_resupplying) _prompt.Text = (_prompt.Text.Length > 0 ? _prompt.Text + "\n" : "") + "Resupplying...";
    }

    private void OnFired(VehicleBody.GunRt g)
    {
        float c = g.Stats.CaliberMm;
        _trauma = Mathf.Min(1f, _trauma + 0.22f + c / 450f);
        _fovKick += 2f + c / 28f;
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
