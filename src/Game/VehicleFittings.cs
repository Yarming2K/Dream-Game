using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The small fittings at work: lights (H) and a searchlight that follows the aim, smoke launchers (Z) that
/// throw a screen nobody can see through, exhausts that smoke under load, toolboxes and spare track links
/// the crew mend the vehicle with once the shooting stops, vents that clear smoke from fires, and
/// periscopes that see further.
/// </summary>
public partial class VehicleBody
{
    private int _periscopes;
    private double _lastHitAt = -999;
    private float _repairClock, _refitClock, _exhaustClock, _aiSmokeWait;
    private readonly List<(BlockInstance block, SpotLight3D light, bool search)> _lights = new();

    /// <summary>Headlights and searchlights are on.</summary>
    public bool LightsOn { get; private set; }

    /// <summary>Smoke launcher racks still loaded.</summary>
    public int SmokeReady => Model.Instances.Count(b => b.Alive && b.Def.Id == "smoke_launcher" && b.AmmoUnits >= 1f);

    public bool HasLights => Model.Instances.Any(b => b.Alive && b.Def.Id is "headlight" or "searchlight");

    private double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Not hit for a while: the crew can get out the tools.</summary>
    private bool Quiet => Now - _lastHitAt > Fittings.QuietS;

    private void RefreshFittings()
    {
        _periscopes = Model.Instances.Count(b => b.Alive && b.Def.Id == "periscope");
        _fires.BurnScale = Fittings.FireScale(Model.Instances.Count(b => b.Alive && b.Def.Id == "vent"));
        if (LightsOn) BuildLights();
    }

    private void FittingsTick(float dt)
    {
        // Field repairs: toolboxes patch damage, spare links refit lost tracks and wheels.
        _repairClock += dt;
        if (_repairClock >= 1f)
        {
            float step = _repairClock;
            _repairClock = 0;
            if (CrewAboard && Quiet && _fires.Count == 0) FieldRepair(step);
        }

        AimSearchlights();
        Exhausts(dt);
        _aiSmokeWait -= dt;
    }

    private void FieldRepair(float dt)
    {
        int boxes = Model.Instances.Count(b => b.Alive && b.Def.Id is "toolbox" or "stowage_rack");
        float hp = Fittings.RepairHpPerS(boxes) * dt;
        if (hp > 0)
        {
            BlockInstance worst = null;
            float worstShare = 1f;
            foreach (BlockInstance b in Model.Instances)
            {
                if (!b.Alive || b.Def.Hp <= 0) continue;
                float share = b.Hp / b.Def.Hp;
                if (share >= worstShare) continue;
                worstShare = share;
                worst = b;
            }

            if (worst != null) worst.Hp = Mathf.Min(worst.Def.Hp, worst.Hp + hp);
        }

        int spares = Model.Instances.Count(b => b.Alive && b.Def.Id == "spare_tracks");
        float every = Fittings.RefitEveryS(spares);
        if (float.IsPositiveInfinity(every)) return;
        _refitClock += dt;
        if (_refitClock < every) return;
        foreach (WheelInfo w in Analysis.Wheels)
        {
            if (Model.Blocks.ContainsKey(w.Cell) || !Repair.CanRebuild(Model, Analysis, w.Cell)) continue;
            if (!TryRebuild(w.Cell)) continue;
            _refitClock = 0;
            Status?.Invoke("Crew refitted a track");
            return;
        }
    }

    // ---------- Lights ----------

    public void ToggleLights()
    {
        LightsOn = !LightsOn;
        if (LightsOn) BuildLights();
        else ClearLights();
    }

    private void ClearLights()
    {
        foreach ((_, SpotLight3D l, _) in _lights)
            if (IsInstanceValid(l)) l.QueueFree();
        _lights.Clear();
    }

    private void BuildLights()
    {
        ClearLights();
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive || b.Def.Id is not ("headlight" or "searchlight")) continue;
            bool search = b.Def.Id == "searchlight";
            var light = new SpotLight3D
            {
                LightColor = search ? new Color(0.95f, 0.97f, 1f) : new Color(1f, 0.93f, 0.8f),
                LightEnergy = search ? 6f : 2.5f, SpotRange = search ? 140f : 35f, SpotAngle = search ? 7f : 28f,
                SpotAttenuation = 1.1f, ShadowEnabled = false,
            };
            View.SectionNode(b.Section).AddChild(light);
            Transform3D at = Conv.BlockTransform(b.Cell, b.Orientation);
            // A headlight shines the way it faces; a searchlight is turned toward the aim each step.
            light.Transform = new Transform3D(at.Basis, b.Center.G() * Conv.CS + at.Basis * new Vector3(0, 0, -0.2f));
            _lights.Add((b, light, search));
        }
    }

    private void AimSearchlights()
    {
        if (!LightsOn || _lights.Count == 0) return;
        Vector3? aim = PlayerSeat != null && PlayerInput.HasAim ? PlayerInput.Aim : _aiHasAim ? _aiAim : null;
        foreach ((BlockInstance b, SpotLight3D l, bool search) in _lights)
        {
            if (!IsInstanceValid(l)) continue;
            l.Visible = b.Alive;
            if (!search || !aim.HasValue || !b.Alive) continue;
            Vector3 from = l.GlobalPosition;
            if (from.DistanceSquaredTo(aim.Value) < 1f) continue;
            Vector3 up = Mathf.Abs((aim.Value - from).Normalized().Y) > 0.95f ? Vector3.Right : Vector3.Up;
            l.LookAt(aim.Value, up);
        }
    }

    // ---------- Smoke ----------

    /// <summary>Fires every loaded smoke launcher: clouds thrown ahead of the launchers that block sight for a while.</summary>
    public bool FireSmoke()
    {
        var racks = Model.Instances.Where(b => b.Alive && b.Def.Id == "smoke_launcher" && b.AmmoUnits >= 1f).ToList();
        if (racks.Count == 0) return false;
        Node3D world = GetParent<Node3D>();
        Vector3 fwd = -GlobalTransform.Basis.Z;
        fwd.Y = 0;
        fwd = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : Vector3.Forward;
        Vector3 side = fwd.Cross(Vector3.Up);
        Vector3 center = GlobalTransform * CenterOfMass;
        foreach (BlockInstance b in racks)
        {
            b.AmmoUnits = 0;
            Vector3 at = View.BlockWorldPos(b);
            // Each rack throws to its own side, so a pair makes a wall across the front.
            float across = Mathf.Clamp((at - center).Dot(side), -1f, 1f);
            Vector3 land = at + fwd * Fittings.SmokeThrowM + side * across * 9f;
            if (WorldQuery.GroundRay(world, land + Vector3.Up * 20f, land + Vector3.Down * 40f, out Vector3 g, out _)) land = g;
            land.Y = Mathf.Max(land.Y, Ocean.Level);
            land += Vector3.Up * 2.5f;
            WorldQuery.AddSmoke(land, Fittings.SmokeRadiusM, Fittings.SmokeLifeS);
            Effects.SmokeScreen(world, at, land, Fittings.SmokeRadiusM, Fittings.SmokeLifeS);
        }

        Sfx.At(world, center, SoundId.RocketLaunch, -4f, 1.6f, 14f, delayByDistance: false);
        return true;
    }

    /// <summary>AI crews pop smoke when they've been hit hard.</summary>
    private void AiSmoke()
    {
        if (PlayerSeat != null || _aiSmokeWait > 0 || !UnderFire || Health > 0.8f) return;
        _aiSmokeWait = 45f;
        FireSmoke();
    }

    private void ReloadSmoke(float dt)
    {
        foreach (BlockInstance b in Model.Instances)
            if (b.Alive && b.Def.Id is "smoke_launcher" or "countermeasures" && b.AmmoUnits < 1f) b.AmmoUnits = Mathf.Min(1f, b.AmmoUnits + dt / 5f);
    }

    // ---------- Flares and chaff ----------

    /// <summary>Flare and chaff launchers still loaded.</summary>
    public int DecoysReady => Model.Instances.Count(b => b.Alive && b.Def.Id == "countermeasures" && b.AmmoUnits >= 1f);

    /// <summary>A radar-guided missile is closing in: fire one loaded flare and chaff launcher. Returns whether one fired.</summary>
    public bool FireDecoys()
    {
        BlockInstance rack = Model.Instances.FirstOrDefault(b => b.Alive && b.Def.Id == "countermeasures" && b.AmmoUnits >= 1f);
        if (rack == null) return false;
        rack.AmmoUnits = 0;
        Node3D world = GetParent<Node3D>();
        Vector3 at = View.BlockWorldPos(rack);
        Effects.Decoys(world, at, LinearVelocity);
        Sfx.At(world, at, SoundId.RocketLaunch, -8f, 2.2f, 10f, delayByDistance: false);
        if (PlayerSeat != null) StatusNow("Missile warning: flares and chaff away");
        return true;
    }

    // ---------- Exhausts ----------

    private void Exhausts(float dt)
    {
        _exhaustClock -= dt;
        if (_exhaustClock > 0) return;
        _exhaustClock = 0.14f;
        if (!CanDrive || PowerKw <= 0 || FuelL <= 0 || GlobalPosition.DistanceSquaredTo(WorldQuery.Viewer) > 90f * 90f) return;
        float load = IsAir ? _soundLoad : Mathf.Max(Mathf.Abs(Throttle), Mathf.Abs(Steer) * 0.6f);
        if (load < 0.25f) return;
        Node3D world = GetParent<Node3D>();
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive || b.Def.Id != "exhaust") continue;
            Transform3D sec = View.SectionGlobal(b.Section);
            Basis basis = sec.Basis * Conv.BlockTransform(b.Cell, b.Orientation).Basis;
            Vector3 outlet = sec * (b.Center.G() * Conv.CS) + basis * new Vector3(0.27f, -0.15f, 0.06f);
            Effects.ExhaustPuff(world, outlet, (basis * Vector3.Right).Normalized(), load);
        }
    }
}
