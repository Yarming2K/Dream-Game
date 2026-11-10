using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>A hand weapon the player is carrying, with its ammo.</summary>
public sealed class CarriedWeapon
{
    public InfantryWeaponSpec Spec;
    public int Mag;
    public int Reserve;
    public float Cooldown;
    public float ReloadLeft;
    public int ShotsFired;
    public bool IsShovel;
    public bool IsWelder => Spec == null && !IsShovel;
    public bool IsGun => Spec != null;
    public string Name => IsShovel ? "Shovel" : Spec?.Name ?? "Welding Torch";
}

/// <summary>
/// The player on foot, in first person: walk, sprint, jump, shoot, and weld vehicles back together.
/// Getting in and out of vehicles is handled by the test range.
/// </summary>
public partial class PlayerCharacter : CharacterBody3D, ICharacter
{
    public const float EyeHeight = 1.62f;
    private const float WalkSpeed = 4.6f, SprintSpeed = 7.4f, JumpSpeed = 5.4f, Gravity = 16f;
    private const float WeldRange = 3.4f;
    private const float SwimSpeed = 2.8f;

    /// <summary>In water deeper than about chest height: the player swims instead of walking.</summary>
    public bool Swimming { get; private set; }

    /// <summary>The camera is below the surface.</summary>
    public bool EyesUnderwater => Active && Camera.GlobalPosition.Y < Ocean.Level;

    public Camera3D Camera { get; private set; }
    public float Health { get; private set; } = InfantryDamage.MaxHealth;
    public bool Alive => Health > 0;

    /// <summary>Only a person out in the open can be targeted (inside a vehicle, the vehicle is the target).</summary>
    public bool Targetable => Alive && (Active || Remote);

    /// <summary>Standing still, flying a drone by remote: still out in the open, and still a target.</summary>
    public bool Remote { get; private set; }
    public bool Active { get; private set; } = true;
    public int Faction => 0;
    public Vector3 AimCenter => GlobalPosition + Vector3.Up * 1.1f;
    public Vector3 BlastCenter => AimCenter;
    public Rid BodyRid => GetRid();

    public readonly List<CarriedWeapon> Weapons = new();
    public int Slot { get; private set; }
    public CarriedWeapon Current => Weapons[Slot];

    /// <summary>What the welder is doing right now, for the HUD.</summary>
    public (WeldState state, float progress, string block) WeldStatus { get; private set; }

    /// <summary>Raised with the damage taken (for the red flash on screen).</summary>
    public event Action<float> Hurt;

    public event Action Died;
    public event Action<CarriedWeapon> Shot;

    private Node3D _head;
    private float _pitch;
    private bool _aiming;
    private float _recoil;
    private float _bob;
    private float _stepDistance;
    private int _stepIndex;
    private bool _wasOnFloor = true;
    private float _fallSpeed;
    private float _weldSparkTimer;
    private AudioStreamPlayer3D _weldSound;
    private readonly HashSet<VehicleBody> _previewing = new();
    private readonly RandomNumberGenerator _rng = new();

    private Node3D _viewRoot;
    private readonly List<Node3D> _viewModels = new();
    private readonly List<Node3D> _muzzles = new();
    private Node3D _rocketTip;
    private OmniLight3D _torchLight;

    public override void _Ready()
    {
        CollisionLayer = GridRegistry.Layers.Characters;
        CollisionMask = GridRegistry.Layers.World | GridRegistry.Layers.Vehicles | GridRegistry.Layers.Structures |
                        GridRegistry.Layers.Debris;
        FloorMaxAngle = Mathf.DegToRad(50f);
        FloorSnapLength = 0.3f;
        // Moving decks are handled by Ride (in the deck's own frame), not by the engine's platform velocity.
        PlatformFloorLayers = 0;
        PlatformOnLeave = PlatformOnLeaveEnum.DoNothing;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.32f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0),
        });

        _head = new Node3D { Name = "Head", Position = new Vector3(0, EyeHeight, 0) };
        AddChild(_head);
        Camera = new Camera3D { Fov = 75f, Near = 0.02f, Far = 3000f };
        _head.AddChild(Camera);

        Weapons.Add(new CarriedWeapon { Spec = InfantryWeapons.Rifle, Mag = 30, Reserve = InfantryWeapons.Rifle.Reserve });
        Weapons.Add(new CarriedWeapon { Spec = InfantryWeapons.Rocket, Mag = 1, Reserve = InfantryWeapons.Rocket.Reserve });
        Weapons.Add(new CarriedWeapon());
        Weapons.Add(new CarriedWeapon { IsShovel = true });
        BuildViewModels();
        SelectSlot(0);

        _weldSound = Sfx.Loop(this, SoundId.Weld, -10f);
        _weldSound.Autoplay = false;
        _weldSound.Stop();
        Hittables.Add(this);
    }

    public override void _ExitTree() => Hittables.Remove(this);

    /// <summary>Stands the player still while they fly a drone (on), or hands control back (off).</summary>
    public void SetRemote(bool on)
    {
        Remote = on;
        if (!on)
        {
            SetActive(true);
            return;
        }

        Active = false;
        Visible = true;
        Velocity = Vector3.Zero;
        StopWelding();
    }

    /// <summary>Turns the walking player on (on foot) or off (sitting in a vehicle).</summary>
    public void SetActive(bool active)
    {
        if (active) Remote = false;
        Active = active;
        Visible = active;
        CollisionLayer = active ? GridRegistry.Layers.Characters : 0;
        CollisionMask = active ? GridRegistry.Layers.World | GridRegistry.Layers.Vehicles | GridRegistry.Layers.Structures | GridRegistry.Layers.Debris : 0;
        Velocity = Vector3.Zero;
        _deck = null;
        if (!active) StopWelding();
        if (active) Camera.MakeCurrent();
    }

    public void Respawn(Vector3 at, float yaw)
    {
        Health = InfantryDamage.MaxHealth;
        GlobalPosition = at;
        Rotation = new Vector3(0, yaw, 0);
        _pitch = 0;
        _head.Rotation = Vector3.Zero;
        foreach (CarriedWeapon w in Weapons.Where(w => w.IsGun))
        {
            w.Mag = w.Spec.MagSize;
            w.Reserve = w.Spec.Reserve;
            w.ReloadLeft = 0;
        }
    }

    public void LookYaw(float yaw)
    {
        Rotation = new Vector3(0, yaw, 0);
        _pitch = 0;
        _head.Rotation = Vector3.Zero;
    }

    // ---------- Input ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Active || !Alive) return;
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sens = _aiming ? 0.0013f : 0.0024f;
            RotateY(-mm.Relative.X * sens);
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * sens, -1.5f, 1.5f);
            _head.Rotation = new Vector3(_pitch, 0, 0);
            return;
        }

        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right } rb) _aiming = rb.Pressed && Current.IsGun;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.PhysicalKeycode == Key.Key1) SelectSlot(0);
            else if (k.PhysicalKeycode == Key.Key2) SelectSlot(1);
            else if (k.PhysicalKeycode == Key.Key3) SelectSlot(2);
            else if (k.PhysicalKeycode == Key.Key4) SelectSlot(3);
            else if (k.PhysicalKeycode == Key.R) StartReload();
        }
    }

    private void SelectSlot(int i)
    {
        if (i == Slot && _viewModels.Count > 0 && _viewModels[i].Visible) return;
        Slot = i;
        for (int v = 0; v < _viewModels.Count; v++) _viewModels[v].Visible = v == i;
        _aiming = false;
        StopWelding();
        if (_viewRoot != null)
        {
            _viewRoot.Position = new Vector3(0, -0.25f, 0);
            if (IsInsideTree()) Sfx.Play2D(this, SoundId.Tick, -10f, 0.8f);
        }
    }

    private void StartReload()
    {
        CarriedWeapon w = Current;
        if (!w.IsGun || w.ReloadLeft > 0 || w.Mag >= w.Spec.MagSize || w.Reserve <= 0) return;
        w.ReloadLeft = w.Spec.ReloadS;
        Sfx.Play2D(this, SoundId.Reload, -6f, 0.85f);
    }

    // ---------- Frame ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!Active) return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        bool control = captured && Alive;

        // Standing on a vehicle, it carries you first; then you walk relative to its deck.
        Ride();

        // Movement
        Vector3 v = Velocity;
        bool wasSwimming = Swimming;
        Swimming = GlobalPosition.Y < Ocean.Level - 1.15f;
        if (Swimming && !wasSwimming && v.Y < -3f) Effects.Splash(GetParent<Node3D>(), GlobalPosition, 0.7f);
        Vector2 input = control ? Input.GetVector("steer_left", "steer_right", "move_forward", "move_back") : Vector2.Zero;
        if (Swimming)
        {
            Swim(dt, ref v, input, control);
            Velocity = v;
            _fallSpeed = 0;
            MoveAndSlide();
            UpdateDeck();
            WeaponsTick(dt, control);
            return;
        }

        if (Climb(dt, input, control, ref v))
        {
            Velocity = v;
            _fallSpeed = 0;
            MoveAndSlide();
            UpdateDeck();
            WeaponsTick(dt, control);
            return;
        }

        if (!IsOnFloor()) v.Y -= Gravity * dt;
        bool sprint = control && Input.IsPhysicalKeyPressed(Key.Shift) && input.Y < 0 && !_aiming;
        float speed = sprint ? SprintSpeed : _aiming ? WalkSpeed * 0.55f : WalkSpeed;
        Vector3 wish = (Transform.Basis * new Vector3(input.X, 0, input.Y)).Normalized() * speed;
        float accel = IsOnFloor() ? 40f : 7f;
        v.X = Mathf.MoveToward(v.X, wish.X, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, accel * dt);
        if (control && IsOnFloor() && Input.IsActionJustPressed("brake")) v.Y = JumpSpeed;
        Velocity = v;
        _fallSpeed = Mathf.Min(_fallSpeed, v.Y);
        MoveAndSlide();
        UpdateDeck();

        Footsteps(dt, new Vector2(Velocity.X, Velocity.Z).Length());
        WeaponsTick(dt, control);
    }

    /// <summary>
    /// Swimming: you float with your head just out of the water. Space swims up and out (climb onto a beach
    /// or a low deck), C dives. Slow, and no sprinting.
    /// </summary>
    private void Swim(float dt, ref Vector3 v, Vector2 input, bool control)
    {
        Vector3 wish = (Transform.Basis * new Vector3(input.X, 0, input.Y)).Normalized() * SwimSpeed;
        v.X = Mathf.MoveToward(v.X, wish.X, 6f * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, 6f * dt);
        float floatY = Ocean.Level - 1.35f;
        float wantUp = Mathf.Clamp((floatY - GlobalPosition.Y) * 2f, -1.5f, 1.5f);
        if (control && Input.IsActionPressed("brake")) wantUp = 3.2f;
        else if (control && Input.IsActionPressed("dive")) wantUp = -2f;
        v.Y = Mathf.MoveToward(v.Y, wantUp, 9f * dt);
        _bob += new Vector2(v.X, v.Z).Length() * dt * 1.2f;
    }

    private void WeaponsTick(float dt, bool control)
    {
        foreach (CarriedWeapon w in Weapons)
        {
            w.Cooldown = Mathf.Max(0, w.Cooldown - dt);
            if (w.ReloadLeft <= 0) continue;
            w.ReloadLeft -= dt;
            if (w.ReloadLeft > 0) continue;
            int take = Math.Min(w.Spec.MagSize - w.Mag, w.Reserve);
            w.Mag += take;
            w.Reserve -= take;
            w.ReloadLeft = 0;
        }

        bool trigger = control && Input.IsActionPressed("fire");
        if (Current.IsShovel)
        {
            UpdateRepairPreviews(false);
            ShovelTick(dt, trigger, control && Input.IsMouseButtonPressed(MouseButton.Right));
        }
        else if (Current.IsWelder)
        {
            UpdateRepairPreviews(true);
            if (trigger) WeldTick(dt);
            else StopWelding();
        }
        else
        {
            UpdateRepairPreviews(false);
            CarriedWeapon w = Current;
            bool pull = w.Spec.Automatic ? trigger : control && Input.IsActionJustPressed("fire");
            if (pull) TryFire(w);
        }

        Camera.Fov = Mathf.Lerp(Camera.Fov, _aiming ? (Current.Spec == InfantryWeapons.Rocket ? 40f : 52f) : 75f, Mathf.Min(1f, dt * 12f));
        _recoil = Mathf.MoveToward(_recoil, 0, dt * 6f);
        _head.Rotation = new Vector3(_pitch + Mathf.DegToRad(_recoil), 0, 0);
        AnimateViewModel(dt);
    }

    private void Footsteps(float dt, float horizontalSpeed)
    {
        bool onFloor = IsOnFloor();
        if (onFloor && !_wasOnFloor && _fallSpeed < -4f) Step(-4f);
        if (onFloor) _fallSpeed = 0;
        _wasOnFloor = onFloor;
        if (!onFloor || horizontalSpeed < 0.5f) return;
        _stepDistance += horizontalSpeed * dt;
        _bob += horizontalSpeed * dt * 2.2f;
        float stride = horizontalSpeed > 6f ? 2.1f : 1.6f;
        if (_stepDistance < stride) return;
        _stepDistance = 0;
        Step(horizontalSpeed > 6f ? -9f : -13f);
    }

    private void Step(float volume)
    {
        _stepIndex = (_stepIndex + 1) % 3;
        SoundId id = _stepIndex == 0 ? SoundId.Step1 : _stepIndex == 1 ? SoundId.Step2 : SoundId.Step3;
        Sfx.Play2D(this, id, volume, _rng.RandfRange(0.9f, 1.1f));
    }

    private void TryFire(CarriedWeapon w)
    {
        if (w.Cooldown > 0 || w.ReloadLeft > 0 || Combat.Instance == null) return;
        if (w.Mag <= 0)
        {
            if (w.Reserve > 0) StartReload();
            else Sfx.Play2D(this, SoundId.Tick, -6f, 0.6f);
            return;
        }

        w.Mag--;
        w.Cooldown = w.Spec.FireInterval;
        w.ShotsFired++;
        Vector3 forward = -Camera.GlobalTransform.Basis.Z;
        float spread = Mathf.DegToRad(w.Spec.SpreadDeg * (_aiming ? 0.35f : 1f) * (IsOnFloor() ? 1f : 3f));
        Vector3 side = forward.Cross(Vector3.Up).Normalized();
        if (side.LengthSquared() < 0.01f) side = Vector3.Right;
        Vector3 dir = forward.Rotated(side, spread * Mathf.Sqrt(_rng.Randf())).Rotated(forward, _rng.RandfRange(0, Mathf.Tau)).Normalized();
        Vector3 origin = Camera.GlobalPosition + forward * 0.4f;
        bool rocket = w.Spec == InfantryWeapons.Rocket;
        float speed = rocket ? w.Spec.Stats.HeVelocity : w.Spec.Stats.MuzzleVelocity;
        Combat.Instance.Fire(null, origin, dir * speed + Velocity, w.Spec.Ammo, w.Spec.Stats, w.Spec.Breech, true,
            w.Spec.DirectDamage, GetRid(), tracer: !rocket && w.ShotsFired % 3 == 0, rocket: rocket);

        Node3D world = GetParent<Node3D>();
        Vector3 muzzle = _muzzles[Slot].GlobalPosition;
        if (rocket) Effects.Backblast(world, Camera.GlobalPosition - forward * 0.9f, forward);
        else
        {
            Effects.RifleFlash(world, muzzle, forward);
            Sfx.At(world, muzzle, SoundId.Rifle, -2f, _rng.RandfRange(0.95f, 1.05f), 14f, delayByDistance: false);
        }

        _recoil += w.Spec.RecoilDeg * (_aiming ? 0.6f : 1f);
        RotateY(_rng.RandfRange(-1f, 1f) * Mathf.DegToRad(w.Spec.RecoilDeg * 0.25f));
        _viewRoot.Position += new Vector3(0, 0.01f, rocket ? 0.12f : 0.05f);
        if (rocket && _rocketTip != null) _rocketTip.Visible = false;
        Shot?.Invoke(w);
    }

    // ---------- Welding ----------

    private bool PickWeld(out VehicleBody vehicle, out WeldTarget target)
    {
        vehicle = null;
        target = default;
        Vector3 from = Camera.GlobalPosition;
        Vector3 dir = -Camera.GlobalTransform.Basis.Z;
        float best = WeldRange;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || v.Faction != Faction || !IsInstanceValid(v)) continue;
            if (v.GlobalPosition.DistanceTo(from) > v.Radius + WeldRange) continue;
            if (!v.WeldPick(from, dir, best, out WeldTarget t) || t.Distance >= best) continue;
            best = t.Distance;
            vehicle = v;
            target = t;
        }

        return vehicle != null;
    }

    private void WeldTick(float dt)
    {
        if (!PickWeld(out VehicleBody v, out WeldTarget t))
        {
            WeldStatus = (WeldState.Nothing, 0, null);
            StopWelding();
            return;
        }

        (WeldState state, float progress) = v.Weld(t, dt);
        WeldStatus = (state, progress, t.Def?.Name);
        bool working = state is WeldState.Repairing or WeldState.Rebuilding or WeldState.Rebuilt or WeldState.Extinguishing;
        if (!working)
        {
            StopWelding();
            return;
        }

        if (!_weldSound.Playing) _weldSound.Play();
        _weldSound.GlobalPosition = t.Point;
        _torchLight.Visible = true;
        _torchLight.LightEnergy = 1.5f + _rng.Randf() * 1.5f;
        _weldSparkTimer -= dt;
        if (_weldSparkTimer <= 0)
        {
            _weldSparkTimer = 0.06f;
            Effects.WeldSparks(GetParent<Node3D>(), t.Point, t.Normal);
        }

        if (state == WeldState.Rebuilt) Sfx.Play2D(this, SoundId.PlaceHeavy, -4f);
    }

    private void StopWelding()
    {
        if (_weldSound != null && _weldSound.Playing) _weldSound.Stop();
        if (_torchLight != null) _torchLight.Visible = false;
        if (!Current.IsWelder) WeldStatus = (WeldState.Nothing, 0, null);
    }

    /// <summary>Friendly vehicles nearby show their missing blocks while the torch is out.</summary>
    private void UpdateRepairPreviews(bool show)
    {
        var near = new HashSet<VehicleBody>();
        if (show)
            foreach (IGridHost h in GridRegistry.Hosts)
                if (h is VehicleBody v && v.Faction == Faction && IsInstanceValid(v) && v.GlobalPosition.DistanceTo(GlobalPosition) < v.Radius + 10f)
                    near.Add(v);

        foreach (VehicleBody v in _previewing.Where(v => !near.Contains(v)).ToList())
        {
            if (IsInstanceValid(v)) v.SetRepairPreview(false);
            _previewing.Remove(v);
        }

        foreach (VehicleBody v in near)
        {
            v.SetRepairPreview(true);
            _previewing.Add(v);
        }

        if (!show && Current.IsWelder == false) WeldStatus = (WeldState.Nothing, 0, null);
    }

    /// <summary>The friendly vehicle the player is looking at, if it's close enough to get into.</summary>
    public VehicleBody LookedAtVehicle(float range)
    {
        Vector3 from = Camera.GlobalPosition;
        Vector3 to = from - Camera.GlobalTransform.Basis.Z * range;
        var q = PhysicsRayQueryParameters3D.Create(from, to, GridRegistry.Layers.Vehicles, new Godot.Collections.Array<Rid> { GetRid() });
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count > 0 && hit["collider"].AsGodotObject() is VehicleBody v ? v : null;
    }

    // ---------- Damage ----------

    public void TakeHit(float damage, Vector3 point, Vector3 dir, bool byPlayer) => Damage(damage);

    public void TakeBlast(float damage, Vector3 from, bool byPlayer)
    {
        if (!Active) return; // inside a vehicle, its armor is what protects you
        Damage(damage);
        Vector3 push = (GlobalPosition - from).Normalized() * Mathf.Min(damage * 0.12f, 9f);
        Velocity += push + Vector3.Up * Mathf.Min(damage * 0.04f, 4f);
    }

    public void Damage(float amount)
    {
        if (!Alive || amount <= 0) return;
        Health = Mathf.Max(0, Health - amount);
        Hurt?.Invoke(amount);
        Sfx.Play2D(this, SoundId.Hurt, -2f, _rng.RandfRange(0.9f, 1.1f));
        _recoil += Mathf.Min(amount * 0.08f, 6f);
        if (Health <= 0)
        {
            StopWelding();
            Died?.Invoke();
        }
    }

    public void Heal(float amount) => Health = Mathf.Min(InfantryDamage.MaxHealth, Health + amount);

    public bool RefillAmmo(float dt)
    {
        bool changed = false;
        foreach (CarriedWeapon w in Weapons.Where(w => w.IsGun && w.Reserve < w.Spec.Reserve))
        {
            w.Reserve = Math.Min(w.Spec.Reserve, w.Reserve + Math.Max(1, (int)(w.Spec.Reserve * dt * 0.3f)));
            changed = true;
        }

        return changed;
    }

    // ---------- First-person models ----------

    private static StandardMaterial3D Mat(Color c, float metal = 0.7f, float rough = 0.45f) =>
        BlockVisuals.Material($"vm:{c.ToHtml()}", c, metal, rough);

    private static void Part(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rotDeg = null)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat, Position = pos, RotationDegrees = rotDeg ?? Vector3.Zero,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private void BuildViewModels()
    {
        _viewRoot = new Node3D { Name = "ViewModel" };
        Camera.AddChild(_viewRoot);
        StandardMaterial3D dark = Mat(new Color(0.12f, 0.12f, 0.12f));
        StandardMaterial3D olive = Mat(new Color(0.3f, 0.33f, 0.22f), 0.3f, 0.6f);
        StandardMaterial3D steel = Mat(new Color(0.55f, 0.56f, 0.58f), 0.9f, 0.3f);

        // Rifle
        var rifle = new Node3D { Position = new Vector3(0.2f, -0.19f, -0.42f) };
        Part(rifle, new BoxMesh { Size = new Vector3(0.05f, 0.08f, 0.42f) }, dark, Vector3.Zero);
        Part(rifle, new BoxMesh { Size = new Vector3(0.045f, 0.06f, 0.2f) }, olive, new Vector3(0, -0.01f, 0.27f));
        Part(rifle, new CylinderMesh { TopRadius = 0.011f, BottomRadius = 0.011f, Height = 0.32f }, steel, new Vector3(0, 0.012f, -0.36f), new Vector3(90, 0, 0));
        Part(rifle, new BoxMesh { Size = new Vector3(0.035f, 0.13f, 0.06f) }, dark, new Vector3(0, -0.09f, -0.06f), new Vector3(-12, 0, 0));
        Part(rifle, new BoxMesh { Size = new Vector3(0.03f, 0.03f, 0.1f) }, steel, new Vector3(0, 0.055f, -0.02f));
        var rifleMuzzle = new Node3D { Position = new Vector3(0, 0.012f, -0.53f) };
        rifle.AddChild(rifleMuzzle);

        // Rocket launcher, on the shoulder
        var tube = new Node3D { Position = new Vector3(0.24f, -0.13f, -0.25f) };
        Part(tube, new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 1.0f }, olive, new Vector3(0, 0, -0.05f), new Vector3(90, 0, 0));
        Part(tube, new BoxMesh { Size = new Vector3(0.04f, 0.14f, 0.05f) }, dark, new Vector3(0, -0.1f, 0.05f));
        Part(tube, new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.1f) }, dark, new Vector3(-0.07f, 0.04f, -0.1f));
        _rocketTip = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.05f, Height = 0.16f }, MaterialOverride = Mat(new Color(0.35f, 0.3f, 0.2f)),
            Position = new Vector3(0, 0, -0.62f), RotationDegrees = new Vector3(-90, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        tube.AddChild(_rocketTip);
        var tubeMuzzle = new Node3D { Position = new Vector3(0, 0, -0.6f) };
        tube.AddChild(tubeMuzzle);

        // Welding torch
        var torch = new Node3D { Position = new Vector3(0.2f, -0.2f, -0.38f) };
        Part(torch, new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.035f, Height = 0.22f }, Mat(new Color(0.55f, 0.15f, 0.1f)), Vector3.Zero, new Vector3(90, 0, 0));
        Part(torch, new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.014f, Height = 0.14f }, steel, new Vector3(0, 0, -0.17f), new Vector3(90, 0, 0));
        Part(torch, new SphereMesh { Radius = 0.012f, Height = 0.024f }, BlockVisuals.Emissive(new Color(0.6f, 0.85f, 1f), 3f), new Vector3(0, 0, -0.25f));
        var torchTip = new Node3D { Position = new Vector3(0, 0, -0.25f) };
        torch.AddChild(torchTip);
        _torchLight = new OmniLight3D { LightColor = new Color(0.65f, 0.85f, 1f), OmniRange = 3f, Visible = false };
        torchTip.AddChild(_torchLight);

        // Shovel: a wooden haft and a steel blade, held low.
        var shovel = new Node3D { Position = new Vector3(0.22f, -0.3f, -0.42f), RotationDegrees = new Vector3(-28, 8, 0) };
        Part(shovel, new CylinderMesh { TopRadius = 0.016f, BottomRadius = 0.018f, Height = 0.75f }, Mat(new Color(0.42f, 0.3f, 0.18f), 0f, 0.8f),
            new Vector3(0, 0, 0.05f), new Vector3(90, 0, 0));
        Part(shovel, new BoxMesh { Size = new Vector3(0.17f, 0.012f, 0.21f) }, steel, new Vector3(0, -0.01f, -0.42f));
        Part(shovel, new BoxMesh { Size = new Vector3(0.1f, 0.03f, 0.03f) }, dark, new Vector3(0, 0, 0.42f));
        _shovelModel = shovel;
        var shovelTip = new Node3D { Position = new Vector3(0, 0, -0.5f) };
        shovel.AddChild(shovelTip);

        foreach ((Node3D model, Node3D muzzle) in new[] { (rifle, rifleMuzzle), (tube, tubeMuzzle), (torch, torchTip), (shovel, shovelTip) })
        {
            model.Visible = false;
            _viewRoot.AddChild(model);
            _viewModels.Add(model);
            _muzzles.Add(muzzle);
        }
    }

    private void AnimateViewModel(float dt)
    {
        float speed = new Vector2(Velocity.X, Velocity.Z).Length();
        float bobAmount = IsOnFloor() ? Mathf.Clamp(speed / SprintSpeed, 0, 1) * (_aiming ? 0.3f : 1f) : 0.2f;
        var bob = new Vector3(Mathf.Sin(_bob) * 0.012f, -Mathf.Abs(Mathf.Cos(_bob)) * 0.012f, 0) * bobAmount;
        Vector3 aimShift = _aiming ? new Vector3(-0.2f, 0.06f, 0.08f) : Vector3.Zero;
        if (Slot == 1 && _aiming) aimShift = new Vector3(-0.24f, 0.08f, 0.05f);
        _viewRoot.Position = _viewRoot.Position.Lerp(bob + aimShift, Mathf.Min(1f, dt * 14f));
        if (_rocketTip != null) _rocketTip.Visible = Weapons[1].Mag > 0 && Weapons[1].ReloadLeft <= 0;
        // Reloading dips the weapon.
        if (Current.IsGun && Current.ReloadLeft > 0) _viewRoot.Position = _viewRoot.Position.Lerp(new Vector3(0, -0.12f, 0.05f), Mathf.Min(1f, dt * 10f));
    }
}
