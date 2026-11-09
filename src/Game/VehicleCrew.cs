using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

public enum Occupant
{
    Empty,
    Ai,
    Player,
}

/// <summary>A crew seat at runtime: which block it is and who's sitting in it.</summary>
public sealed class SeatRt
{
    public SeatInfo Info;
    public BlockInstance Block;
    public Occupant Occupant;
    public SeatRole Role => Info.Role;
}

/// <summary>What the local player is doing this tick while aboard. Kept as plain data so it can later come over the network.</summary>
public struct VehicleControls
{
    public float Throttle;
    public float Steer;
    public bool Brake;
    public bool HasAim;
    public Vector3 Aim;
    public bool Fire;

    /// <summary>Aircraft: climb (+1) or descend (-1), and the direction the player wants to fly (from the camera).</summary>
    public float Lift;
    public Vector3 AimDir;
}

public enum WeldState
{
    Nothing,
    Repairing,
    AlreadyFixed,
    Rebuilding,
    Rebuilt,
    CantAttach,
}

/// <summary>The block (or missing block) a welding torch is pointed at.</summary>
public struct WeldTarget
{
    public bool Missing;
    public BlockInstance Block;
    public Int3 Cell;
    public BlockDef Def;
    public Vector3 Point;
    public Vector3 Normal;
    public float Distance;
}

/// <summary>Crew seats, AI gunners, field repairs and resupply for a vehicle.</summary>
public partial class VehicleBody
{
    public readonly List<SeatRt> Seats = new();
    public SeatRt PlayerSeat { get; private set; }
    public VehicleControls PlayerInput;

    /// <summary>When false the AI crew holds fire unless the player tells them where to shoot.</summary>
    public bool AiCrewActive = true;

    /// <summary>
    /// With the player aboard, AI gunners aim where the player aims and fire when the player fires.
    /// Turn it off ("weapons free") and they pick their own targets.
    /// </summary>
    public bool GunnerFollowsPlayer = true;

    /// <summary>Raised when the seat the player is in gets destroyed.</summary>
    public event Action PlayerSeatDestroyed;

    private VehicleBody _aiVehicleTarget;
    private ICharacter _aiCharacterTarget;
    private float _aiRetarget;
    private float _aiOnTarget;
    private Vector3 _aiOffset;
    private Vector3 _aiAim;
    private bool _aiHasAim;

    private readonly Dictionary<Int3, Node3D> _repairGhosts = new();
    private readonly Dictionary<Int3, float> _rebuildProgress = new();
    private static Material _ghostCan, _ghostCant;

    private void SetupSeats()
    {
        foreach (SeatInfo si in Analysis.Seats)
        {
            if (!Model.Blocks.TryGetValue(si.Cell, out BlockInstance b)) continue;
            Seats.Add(new SeatRt { Info = si, Block = b, Occupant = Occupant.Ai });
        }
    }

    public bool SeatAlive(SeatRt s) =>
        s.Block.Alive && Model.Blocks.TryGetValue(s.Info.Cell, out BlockInstance b) && b == s.Block;

    public SeatRt DriverSeat() => Seats.FirstOrDefault(s => s.Role == SeatRole.Driver && SeatAlive(s));

    public bool CommanderAlive => Seats.Any(s => s.Role == SeatRole.Commander && SeatAlive(s) && s.Occupant != Occupant.Empty);

    public bool HasFreeSeat => Seats.Any(s => SeatAlive(s) && s.Occupant != Occupant.Player);

    /// <summary>Gets the player into a seat (their preferred role if it's free, else the driver's, else any).</summary>
    public SeatRt Enter(SeatRole? preferred = null)
    {
        List<SeatRt> free = Seats.Where(s => SeatAlive(s) && s.Occupant != Occupant.Player).ToList();
        if (free.Count == 0) return null;
        SeatRt seat = (preferred.HasValue ? free.FirstOrDefault(s => s.Role == preferred.Value) : null) ??
                      free.FirstOrDefault(s => s.Role == SeatRole.Driver) ?? free[0];
        seat.Occupant = Occupant.Player;
        PlayerSeat = seat;
        return seat;
    }

    /// <summary>The player gets out; the AI crew member who'd stepped aside takes the seat back.</summary>
    public void Exit()
    {
        if (PlayerSeat == null) return;
        if (SeatAlive(PlayerSeat)) PlayerSeat.Occupant = Occupant.Ai;
        PlayerSeat = null;
        PlayerInput = default;
    }

    /// <summary>Moves the player to another seat of this role, if there's a working one.</summary>
    public SeatRt SwitchSeat(SeatRole role)
    {
        if (PlayerSeat == null) return null;
        SeatRt target = Seats.FirstOrDefault(s => s.Role == role && SeatAlive(s) && s != PlayerSeat);
        if (target == null) return null;
        PlayerSeat.Occupant = Occupant.Ai;
        target.Occupant = Occupant.Player;
        PlayerSeat = target;
        return target;
    }

    public Vector3 SeatWorldPos(SeatRt s) => View.BlockWorldPos(s.Block);

    private void CheckPlayerSeat()
    {
        if (PlayerSeat == null || SeatAlive(PlayerSeat)) return;
        PlayerSeat = null;
        PlayerInput = default;
        PlayerSeatDestroyed?.Invoke();
    }

    /// <summary>
    /// Who's firing this gun: its assigned seat if someone's in it, otherwise another manned gunner seat,
    /// otherwise the driver. Null if nobody can.
    /// </summary>
    public SeatRt ControllerOf(GunRt g)
    {
        bool Manned(SeatRt s) => s != null && SeatAlive(s) && s.Occupant != Occupant.Empty;
        if (Analysis.GunController.TryGetValue(g.Index, out Int3 cell))
        {
            SeatRt assigned = Seats.FirstOrDefault(s => s.Info.Cell == cell);
            if (Manned(assigned)) return assigned;
        }

        SeatRt gunner = Seats.FirstOrDefault(s => s.Role == SeatRole.Gunner && Manned(s));
        if (gunner != null) return gunner;
        SeatRt driver = DriverSeat();
        return Manned(driver) ? driver : null;
    }

    /// <summary>True when this gun goes where the player points: they're its gunner, or their AI gunner is following orders.</summary>
    public bool PlayerDirects(GunRt g)
    {
        SeatRt c = ControllerOf(g);
        if (c == null) return false;
        return c.Occupant == Occupant.Player || (c.Occupant == Occupant.Ai && PlayerSeat != null && GunnerFollowsPlayer);
    }

    private bool AimFor(GunRt g, out Vector3 aim)
    {
        aim = default;
        SeatRt c = ControllerOf(g);
        if (c == null) return false;
        if (PlayerDirects(g))
        {
            aim = PlayerInput.Aim;
            return PlayerInput.HasAim;
        }

        aim = _aiAim;
        return _aiHasAim;
    }

    // ---------- AI gunner ----------

    private void CrewTick(float dt)
    {
        bool aiGuns = AiCrewActive && Guns.Any(g => g.Alive && ControllerOf(g)?.Occupant == Occupant.Ai && !PlayerDirects(g));
        if (!aiGuns)
        {
            _aiHasAim = false;
            _aiVehicleTarget = null;
            _aiCharacterTarget = null;
            return;
        }

        _aiRetarget -= dt;
        if (_aiRetarget <= 0 || !AiTargetValid())
        {
            _aiRetarget = 0.7f;
            PickAiTarget();
        }

        if (!AiTargetValid())
        {
            _aiHasAim = false;
            return;
        }

        Vector3 targetPos, targetVel;
        if (_aiVehicleTarget != null)
        {
            targetPos = _aiVehicleTarget.GlobalTransform * _aiVehicleTarget.CenterOfMass;
            targetVel = _aiVehicleTarget.LinearVelocity;
        }
        else
        {
            targetPos = _aiCharacterTarget.AimCenter;
            targetVel = _aiCharacterTarget.Velocity;
        }

        GunRt lead = Guns.FirstOrDefault(g => g.Alive && g.Info.GunSection >= 0 && ControllerOf(g)?.Occupant == Occupant.Ai && !PlayerDirects(g));
        if (lead == null)
        {
            _aiHasAim = false;
            return;
        }

        // Pick the right shell: armor-piercing for vehicles, high explosive for people.
        bool playerHasGuns = Guns.Any(PlayerDirects);
        if (!playerHasGuns)
            SelectedAmmo = _aiVehicleTarget == null ? AmmoType.HE
                : _aiVehicleTarget.IsAir && !_aiVehicleTarget.OnGround && HasFlak ? AmmoType.Flak : AmmoType.AP;

        (Vector3 muzzle, _) = Muzzle(lead);
        float speed = ShellSpeed(lead, SelectedAmmo);
        Vector3 aim = GunMath.LeadTarget(muzzle.N(), targetPos.N(), targetVel.N(), speed).G();

        // Gunners start a little off and settle in; a commander makes them quicker and steadier.
        bool commander = CommanderAlive;
        _aiOnTarget += dt;
        float settle = commander ? 2.5f : 5f;
        float k = Mathf.Clamp(_aiOnTarget / settle, 0, 1);
        float radius = Mathf.Lerp(commander ? 1.6f : 3f, 0.3f, k);
        _aiAim = aim + _aiOffset * radius;
        _aiHasAim = true;

        float react = commander ? 0.5f : 1.2f;
        if (_aiOnTarget < react) return;
        float tolerance = _aiVehicleTarget != null ? 1.2f : 2f;
        foreach (GunRt g in Guns)
        {
            if (!g.Alive || g.ReloadLeft > 0 || g.AimError > tolerance) continue;
            if (ControllerOf(g)?.Occupant != Occupant.Ai || PlayerDirects(g)) continue;
            FireGun(g, false);
        }
    }

    private bool AiTargetValid()
    {
        if (_aiVehicleTarget != null)
            return IsInstanceValid(_aiVehicleTarget) && _aiVehicleTarget.IsInsideTree() && _aiVehicleTarget.Model.Blocks.Count > 0;
        return _aiCharacterTarget != null && _aiCharacterTarget.Targetable;
    }

    private void PickAiTarget()
    {
        Vector3 eye = GlobalTransform * CenterOfMass + Vector3.Up * 1.5f;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { GetRid() };
        float best = float.MaxValue;
        VehicleBody bestVehicle = null;
        ICharacter bestCharacter = null;

        bool Visible(Vector3 point, Rid target)
        {
            var q = PhysicsRayQueryParameters3D.Create(eye, point, GridRegistry.Layers.All | GridRegistry.Layers.Characters, exclude);
            Godot.Collections.Dictionary hit = space.IntersectRay(q);
            return hit.Count == 0 || hit["rid"].AsRid() == target;
        }

        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || v == this || v.Faction == Faction || v.Model.Blocks.Count == 0) continue;
            Vector3 p = v.GlobalTransform * v.CenterOfMass;
            float d = eye.DistanceTo(p);
            if (d > 800f || d >= best || !Visible(p, v.GetRid())) continue;
            best = d;
            bestVehicle = v;
            bestCharacter = null;
        }

        foreach (ICharacter c in Hittables.Characters)
        {
            if (!c.Targetable || c.Faction == Faction) continue;
            float d = eye.DistanceTo(c.AimCenter);
            // People are a lower priority than vehicles unless they're much closer.
            if (d > 350f || d * 1.6f >= best || !Visible(c.AimCenter, c.BodyRid)) continue;
            best = d * 1.6f;
            bestCharacter = c;
            bestVehicle = null;
        }

        bool changed = bestVehicle != _aiVehicleTarget || bestCharacter != _aiCharacterTarget;
        _aiVehicleTarget = bestVehicle;
        _aiCharacterTarget = bestCharacter;
        if (!changed) return;
        _aiOnTarget = 0;
        _aiOffset = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.4f, 0.6f), _rng.RandfRange(-1, 1)).Normalized();
    }

    // ---------- Field repairs ----------

    private sealed class MissingLookup : ICellShapeLookup
    {
        private readonly VehicleBody _v;
        private readonly HashSet<Int3> _cells;

        public MissingLookup(VehicleBody v, HashSet<Int3> cells)
        {
            _v = v;
            _cells = cells;
        }

        public bool TryGetShape(Int3 cell, out BlockShape shape, out int orientation)
        {
            if (_cells.Contains(cell) && _v.Analysis.Cells.TryGetValue(cell, out BlueprintBlock b))
            {
                shape = BlockCatalog.Get(b.Id).Shape;
                orientation = b.O;
                return true;
            }

            shape = BlockShape.Cube;
            orientation = 0;
            return false;
        }
    }

    private Dictionary<int, HashSet<Int3>> MissingBySection()
    {
        // Every cell a missing block used to fill (big blocks fill several), by section.
        var map = new Dictionary<int, HashSet<Int3>>();
        foreach (Int3 anchor in Repair.Missing(Analysis, Model))
        {
            int s = Analysis.SectionOf[anchor];
            if (!map.TryGetValue(s, out HashSet<Int3> set)) map[s] = set = new HashSet<Int3>();
            BlueprintBlock bp = Analysis.Cells[anchor];
            foreach (Int3 c in Footprint.Cells(Analysis.Defs[anchor], anchor, bp.O))
                if (!Model.Blocks.ContainsKey(c)) set.Add(c);
        }

        return map;
    }

    /// <summary>Finds the nearest block, or missing block, along a ray (for the welding torch).</summary>
    public bool WeldPick(Vector3 from, Vector3 dir, float maxLen, out WeldTarget target)
    {
        target = default;
        float best = maxLen;
        bool found = false;
        Dictionary<int, HashSet<Int3>> missing = MissingBySection();
        var hits = new List<RayCellHit>(1);
        foreach (Section s in Model.Sections)
        {
            Transform3D g = View.SectionGlobal(s.Id);
            Transform3D inv = g.AffineInverse();
            System.Numerics.Vector3 o = (inv * from / Conv.CS).N();
            System.Numerics.Vector3 d = (inv.Basis * dir).Normalized().N();

            hits.Clear();
            if (s.Cells.Count > 0) GridRaycast.Cast(s, s.Min, s.Max, o, d, maxLen / Conv.CS, hits, firstOnly: true);
            if (hits.Count > 0 && hits[0].TEnter * Conv.CS < best && s.Cells.TryGetValue(hits[0].Cell, out BlockInstance b))
            {
                best = hits[0].TEnter * Conv.CS;
                target = new WeldTarget
                {
                    Block = b, Cell = b.Cell, Def = b.Def, Distance = best, Point = from + dir * best,
                    Normal = (g.Basis * hits[0].Normal.G()).Normalized(),
                };
                found = true;
            }

            if (!missing.TryGetValue(s.Id, out HashSet<Int3> gone)) continue;
            hits.Clear();
            GridRaycast.Cast(new MissingLookup(this, gone), s.Min, s.Max, o, d, maxLen / Conv.CS, hits, firstOnly: true);
            if (hits.Count == 0 || hits[0].TEnter * Conv.CS >= best) continue;
            best = hits[0].TEnter * Conv.CS;
            target = new WeldTarget
            {
                Missing = true, Cell = Analysis.Cells[hits[0].Cell].Cell, Def = BlockCatalog.Get(Analysis.Cells[hits[0].Cell].Id), Distance = best,
                Point = from + dir * best, Normal = (g.Basis * hits[0].Normal.G()).Normalized(),
            };
            found = true;
        }

        return found;
    }

    /// <summary>One tick of welding. Returns what happened and how far along it is (0 to 1).</summary>
    public (WeldState state, float progress) Weld(WeldTarget t, float dt)
    {
        if (!t.Missing)
        {
            BlockInstance b = t.Block;
            if (b == null || !b.Alive) return (WeldState.Nothing, 0);
            if (b.Hp >= b.Def.Hp) return (WeldState.AlreadyFixed, 1);
            b.Hp = Mathf.Min(b.Def.Hp, b.Hp + Repair.HpPerSecond * dt);
            return (WeldState.Repairing, b.Hp / b.Def.Hp);
        }

        if (!Repair.CanRebuild(Model, Analysis, t.Cell)) return (WeldState.CantAttach, 0);
        float done = (_rebuildProgress.TryGetValue(t.Cell, out float p) ? p : 0) + dt;
        float need = Repair.RebuildSeconds(t.Def);
        if (done < need)
        {
            _rebuildProgress[t.Cell] = done;
            return (WeldState.Rebuilding, done / need);
        }

        _rebuildProgress.Remove(t.Cell);
        return TryRebuild(t.Cell) ? (WeldState.Rebuilt, 1) : (WeldState.CantAttach, 0);
    }

    /// <summary>Welds a destroyed block (by its anchor cell) back into place, the way the design had it.</summary>
    public bool TryRebuild(Int3 cell)
    {
        if (!Analysis.Cells.TryGetValue(cell, out BlueprintBlock bp) || bp.Cell != cell || Model.Blocks.ContainsKey(cell)) return false;
        int section = Analysis.SectionOf[cell];
        if (!Repair.CanRebuild(Model, Analysis, cell)) return false;

        BlockDef def = BlockCatalog.Get(bp.Id);
        BlockInstance b = Model.Add(def, cell, bp.O, section);
        b.AmmoUnits = 0; // a rebuilt rack is empty until resupplied
        View.AddBlock(b);

        if (def.IsWheelLike)
        {
            Wheels.Clear();
            SetupWheels();
        }

        foreach (SeatRt s in Seats.Where(s => s.Info.Cell == cell))
        {
            s.Block = b;
            s.Occupant = Occupant.Ai; // a fresh crew member climbs in
        }

        foreach (GunRt g in Guns.Where(g => g.Info.Breech == cell)) g.Breech = b;
        foreach (TubeRt t in Tubes.Where(t => t.Info.Cell == cell)) t.Block = b;
        if (_repairGhosts.Remove(cell, out Node3D ghost)) ghost.QueueFree();
        Radius = Mathf.Max(Radius, View.BoundingRadius());
        RefreshStats();
        return true;
    }

    /// <summary>Shows see-through outlines of missing blocks while the player holds a welder nearby.</summary>
    public void SetRepairPreview(bool on)
    {
        if (!on)
        {
            foreach (Node3D g in _repairGhosts.Values)
                if (IsInstanceValid(g)) g.QueueFree();
            _repairGhosts.Clear();
            return;
        }

        _ghostCan ??= Shaders.Hologram(new Color(0.35f, 0.75f, 1f), 0.9f);
        _ghostCant ??= Shaders.Hologram(new Color(0.45f, 0.5f, 0.6f), 0.45f);
        var missing = new HashSet<Int3>(Repair.Missing(Analysis, Model));
        foreach (Int3 c in _repairGhosts.Keys.Where(c => !missing.Contains(c)).ToList())
        {
            if (IsInstanceValid(_repairGhosts[c])) _repairGhosts[c].QueueFree();
            _repairGhosts.Remove(c);
        }

        foreach (Int3 c in missing)
        {
            BlueprintBlock bp = Analysis.Cells[c];
            int section = Analysis.SectionOf[c];
            if (!_repairGhosts.TryGetValue(c, out Node3D ghost))
            {
                float caliber = Analysis.Guns.FirstOrDefault(g => g.Barrels.Contains(c) || g.Brake == c)?.Stats.CaliberMm ?? 76f;
                ghost = BlockVisuals.Create(BlockCatalog.Get(bp.Id), caliber);
                ghost.Transform = Conv.BlockTransform(c, bp.O);
                View.SectionNode(section).AddChild(ghost);
                _repairGhosts[c] = ghost;
            }

            BlockVisuals.SetOverride(ghost, Repair.CanRebuild(Model, Analysis, c) ? _ghostCan : _ghostCant);
        }
    }

    /// <summary>Destroyed blocks waiting to be welded back (fired missiles don't count: supply points rebuild those).</summary>
    public int MissingCount => Repair.Missing(Analysis, Model).Count(c => !Analysis.Defs[c].IsMissilePart);

    /// <summary>Refills ammo racks and fuel a little at a time (while parked at a supply point).</summary>
    public bool Resupply(float dt)
    {
        bool changed = false;
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive || b.Def.Kind != BlockKind.AmmoRack || b.AmmoUnits >= b.Def.AmmoUnits) continue;
            b.AmmoUnits = Mathf.Min(b.Def.AmmoUnits, b.AmmoUnits + 8f * dt);
            changed = true;
        }

        if (FuelL < FuelCapacityL)
        {
            FuelL = Mathf.Min(FuelCapacityL, FuelL + 30f * dt);
            changed = true;
        }

        return ResupplyOrdnance(dt) || changed;
    }
}
