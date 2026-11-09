using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>Something worth showing the player: what a shell did and where.</summary>
public struct CombatEvent
{
    /// <summary>What happened at the first block the shell reached (or the explosion).</summary>
    public HitReport Report;
    public Vector3 Position;
    public bool ByPlayer;
    public int BlocksHit;
    public int BlocksDestroyed;

    /// <summary>Rifle-caliber hit (kept out of the hit feed, which would flood).</summary>
    public bool Small;
}

/// <summary>
/// Flies shells, resolves hits against every block grid using the core damage rules, runs explosions
/// (including ammo and fuel cooking off), and splits off pieces that lose their connection.
/// </summary>
public partial class Combat : Node3D
{
    public static Combat Instance { get; private set; }

    private sealed class Projectile
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public Shell Shell;
        public BreechSpec Breech;
        public float DragK;
        public bool ByPlayer;
        public bool HitSomething;
        public float Age;
        public TracerFx Tracer;
        public CpuParticles3D Trail;
        public float InfantryDamage;
        public Rid Ignore;
        public bool Small;

        /// <summary>A shell that went into the sea: it slows fast and soon stops.</summary>
        public bool Underwater;

        /// <summary>Where the shell really is after this step, when it isn't the end of the segment (it hit the water).</summary>
        public Vector3? Moved;

        /// <summary>Flak: bursts after this many meters (0 = only on contact or near an aircraft).</summary>
        public float FuseM;
        public float Travelled;
        public IGridHost Owner;
    }

    private struct PendingBlast
    {
        public Vector3 Pos;
        public float Radius;
        public float Damage;
        public float Delay;
        public bool ByPlayer;
        public string Cause;
    }

    private readonly List<Projectile> _shells = new();
    private readonly List<PendingBlast> _blasts = new();
    private readonly List<OrderedHit> _hits = new();
    private readonly List<RayCellHit> _cellHits = new();
    private readonly Dictionary<OrderedHit, Vector3> _hitPoints = new(new HitComparer());
    private readonly Dictionary<OrderedHit, Vector3> _hitNormals = new(new HitComparer());
    private readonly RandomNumberGenerator _rng = new();

    public event Action<CombatEvent> Event;

    /// <summary>Every explosion (position, radius, damage), for camera shake.</summary>
    public event Action<Vector3, float, float> Blast;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private sealed class HitComparer : IEqualityComparer<OrderedHit>
    {
        public bool Equals(OrderedHit a, OrderedHit b) => a.Block == b.Block;
        public int GetHashCode(OrderedHit h) => h.Block.GetHashCode();
    }

    /// <summary>
    /// Launches a shell, bullet or rocket. <paramref name="ignore"/> is a body the shot can't hit (the shooter).
    /// Rifle rounds only get a visible tracer when <paramref name="tracer"/> is set.
    /// </summary>
    public void Fire(IGridHost owner, Vector3 pos, Vector3 vel, AmmoType type, in GunStats stats, BreechSpec breech, bool byPlayer,
        float infantryDamage = -1f, Rid ignore = default, bool tracer = true, bool rocket = false, float fuseM = 0f)
    {
        var shell = Shell.FromGun(stats, type, vel.Length(), breech);
        var p = new Projectile
        {
            Pos = pos, Vel = vel, Shell = shell, Breech = breech, ByPlayer = byPlayer,
            DragK = type == AmmoType.AP ? stats.DragK : stats.HeDragK,
            InfantryDamage = infantryDamage >= 0 ? infantryDamage : InfantryDamage.FromShell(shell),
            Ignore = ignore, Small = stats.CaliberMm < 20f, FuseM = fuseM, Owner = owner,
        };
        if (rocket) p.Trail = Effects.RocketTrail(this, pos);
        else if (tracer) p.Tracer = Effects.Tracer(this, stats.CaliberMm, type);
        _shells.Add(p);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        for (int i = _shells.Count - 1; i >= 0; i--)
        {
            Projectile p = _shells[i];
            p.Age += dt;
            Vector3 from = p.Pos;
            float speed = p.Vel.Length();
            p.Vel += Vector3.Down * GunMath.Gravity * (p.Underwater ? 0.2f : 1f) * dt;
            p.Vel *= Mathf.Max(0f, 1f - (p.Underwater ? UnderwaterDrag : p.DragK) * speed * dt);
            if (p.Underwater) p.Shell.PenMm *= p.Vel.Length() / Mathf.Max(speed, 1f);
            Vector3 to = from + p.Vel * dt;
            bool done = p.Shell.Type == AmmoType.Flak && FlakFuse(p, from, ref to);
            if (!done) done = Step(p, from, to);
            if (!done)
            {
                p.Pos = p.Moved ?? to;
                p.Moved = null;
                if (p.Tracer != null) Effects.UpdateTracer(p.Tracer, from, p.Pos);
                Effects.Follow(p.Trail, p.Pos);
                if (p.Age > 8f || p.Pos.Y < -100f || (p.Underwater && p.Vel.Length() < 60f)) done = true;
            }

            if (done)
            {
                Effects.EndTracer(p.Tracer);
                Effects.StopTrail(p.Trail);
                _shells.RemoveAt(i);
            }
        }

        TorpedoTick(dt);
        OrdnanceTick(dt);
        RunBlasts(dt);
    }

    /// <summary>
    /// Flak shells burst when they pass close to an enemy aircraft, or when they've flown as far as the gun
    /// was aimed. Returns true if it went off.
    /// </summary>
    private bool FlakFuse(Projectile p, Vector3 from, ref Vector3 to)
    {
        float len = from.DistanceTo(to);
        if (len < 1e-4f) return false;
        Vector3 dir = (to - from) / len;
        float burstAt = float.PositiveInfinity;
        if (p.FuseM > 0 && p.Travelled + len >= p.FuseM) burstAt = p.FuseM - p.Travelled;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || h == p.Owner || !v.IsAir || v.OnGround || !IsInstanceValid(v)) continue;
            Vector3 c = v.GlobalTransform * v.CenterOfMass;
            float t = Mathf.Clamp((c - from).Dot(dir), 0, len);
            if ((from + dir * t).DistanceTo(c) <= v.Radius * 0.45f + 3.5f) burstAt = Mathf.Min(burstAt, t);
        }

        p.Travelled += len;
        if (float.IsPositiveInfinity(burstAt) || p.Age < 0.05f) return false;

        // Anything solid before the burst point gets hit first, as with any shell.
        Vector3 at = from + dir * burstAt;
        if (Step(p, from, at)) return true;
        Explode(at, p.Shell.HeRadiusM * 2.6f, p.Shell.HeDamage * 1.3f, p.ByPlayer, "Flak", flak: true);
        return true;
    }

    /// <summary>Drag on a shell in water, per meter per (m/s): a few dozen meters and it's spent.</summary>
    private const float UnderwaterDrag = 0.03f;

    /// <summary>Moves a shell along one segment. Returns true when it's used up.</summary>
    private bool Step(Projectile p, Vector3 from, Vector3 to)
    {
        Vector3 seg = to - from;
        float len = seg.Length();
        if (len < 1e-5f) return false;
        Vector3 dir = seg / len;

        float terrainT = float.PositiveInfinity;
        Vector3 terrainPos = Vector3.Zero, terrainNormal = Vector3.Up;
        var query = PhysicsRayQueryParameters3D.Create(from, to, GridRegistry.Layers.World);
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            terrainPos = hit["position"].AsVector3();
            terrainNormal = hit["normal"].AsVector3();
            terrainT = from.DistanceTo(terrainPos);
        }

        // The sea surface: shells splash, HE goes off on it, small arms stop.
        float waterT = float.PositiveInfinity;
        if (!p.Underwater && from.Y > Ocean.Level && to.Y <= Ocean.Level)
            waterT = len * (from.Y - Ocean.Level) / (from.Y - to.Y);
        if (waterT < terrainT)
        {
            terrainT = waterT;
            terrainPos = from + dir * waterT;
            terrainNormal = Vector3.Up;
        }
        else
        {
            waterT = float.PositiveInfinity;
        }

        // People and practice targets.
        float bodyT = float.PositiveInfinity;
        IHittable body = null;
        Vector3 bodyPos = Vector3.Zero;
        var bodyQuery = PhysicsRayQueryParameters3D.Create(from, to, GridRegistry.Layers.Characters | GridRegistry.Layers.Targets);
        if (p.Ignore.IsValid) bodyQuery.Exclude = new Godot.Collections.Array<Rid> { p.Ignore };
        Godot.Collections.Dictionary bodyHit = GetWorld3D().DirectSpaceState.IntersectRay(bodyQuery);
        if (bodyHit.Count > 0 && bodyHit["collider"].AsGodotObject() is IHittable h)
        {
            bodyPos = bodyHit["position"].AsVector3();
            float t = from.DistanceTo(bodyPos);
            if (t < terrainT)
            {
                bodyT = t;
                body = h;
            }
        }

        CollectGridHits(from, dir, Mathf.Min(len, Mathf.Min(terrainT, bodyT)));
        if (_hits.Count == 0 && body != null)
        {
            if (p.Shell.Type == AmmoType.HE) Explode(bodyPos, p.Shell.HeRadiusM, p.Shell.HeDamage, p.ByPlayer, "HE");
            body.TakeHit(p.InfantryDamage, bodyPos, dir, p.ByPlayer);
            return true;
        }

        if (p.Shell.Type == AmmoType.AP)
        {
            if (_hits.Count > 0)
            {
                var damaged = new List<(OrderedHit, bool)>();
                var reports = new List<HitReport>();
                int stop = DamageModel.ResolveAp(p.Shell, _hits, !p.HitSomething, damaged, reports);
                p.HitSomething = true;
                if (reports.Count > 0)
                {
                    OrderedHit first = damaged[0].Item1;
                    Vector3 at = _hitPoints[first];
                    var firstHost = (IGridHost)first.Owner;
                    Node3D surface = IsInstanceValid(firstHost.Body) ? firstHost.View.SectionNode(first.Block.Section) : null;
                    if (p.Small) Effects.BulletImpact(this, at, _hitNormals[first], dir, true, surface);
                    else Effects.Impact(this, at, _hitNormals[first], dir, reports[0].Kind, p.Shell.CaliberMm, surface);
                    Event?.Invoke(new CombatEvent
                    {
                        Report = reports[0], Position = at, ByPlayer = p.ByPlayer, BlocksHit = reports.Count,
                        BlocksDestroyed = damaged.Count(d => d.Item2), Small = p.Small,
                    });
                }

                ApplyDestruction(damaged.Where(d => d.Item2).Select(d => d.Item1), from, dir * p.Shell.CaliberMm * 0.05f);
                if (stop >= 0) return true;
            }

            if (waterT < float.PositiveInfinity)
            {
                Effects.Splash(this, terrainPos, p.Small ? 0.2f : Mathf.Clamp(p.Shell.CaliberMm / 70f, 0.4f, 2.2f));
                if (p.Small) return true;
                p.Underwater = true;
                p.Vel *= 0.45f;
                p.Shell.PenMm *= 0.6f;
                p.Moved = terrainPos + dir * 0.05f;
                return false;
            }

            if (terrainT < float.PositiveInfinity)
            {
                if (p.Underwater) return true;
                if (p.Small) Effects.BulletImpact(this, terrainPos, terrainNormal, dir, false, null);
                else Effects.Dirt(this, terrainPos, terrainNormal, p.Shell.CaliberMm);
                return true;
            }

            return false;
        }

        // HE: burst on the first thing it touches. If it punches through the first plate, it goes off inside.
        if (_hits.Count > 0)
        {
            OrderedHit first = _hits[0];
            Vector3 at = _hitPoints[first];
            float eff = DamageModel.EffectiveArmor(first.Block.Def.ArmorMm, first.AngleDeg);
            Vector3 center = p.Shell.HePenMm >= eff ? at + dir * Conv.CS * 0.7f : at - dir * 0.05f;
            Explode(center, p.Shell.HeRadiusM, p.Shell.HeDamage, p.ByPlayer, "HE shell");
            return true;
        }

        if (waterT < float.PositiveInfinity) Effects.Splash(this, terrainPos, Mathf.Clamp(p.Shell.HeRadiusM * 0.6f, 0.4f, 2.5f));
        if (terrainT < float.PositiveInfinity)
        {
            Explode(terrainPos + terrainNormal * 0.1f, p.Shell.HeRadiusM, p.Shell.HeDamage, p.ByPlayer, "HE shell");
            return true;
        }

        return false;
    }

    /// <summary>Every block along a segment, from every grid, nearest first.</summary>
    private void CollectGridHits(Vector3 from, Vector3 dir, float maxLen)
    {
        _hits.Clear();
        _hitPoints.Clear();
        _hitNormals.Clear();
        foreach (IGridHost h in GridRegistry.Hosts.ToArray())
        {
            if (!IsInstanceValid(h.Body)) continue;
            Vector3 center = h.Body.GlobalPosition;
            float along = Mathf.Clamp((center - from).Dot(dir), 0, maxLen);
            if ((from + dir * along).DistanceTo(center) > h.Radius) continue;

            foreach (Section s in h.Model.Sections)
            {
                if (s.Cells.Count == 0) continue;
                Transform3D inv = h.View.SectionGlobal(s.Id).AffineInverse();
                Vector3 o = inv * from / Conv.CS;
                Vector3 d = (inv.Basis * dir).Normalized();
                _cellHits.Clear();
                GridRaycast.Cast(s, s.Min, s.Max, o.N(), d.N(), maxLen / Conv.CS, _cellHits);
                Basis toWorld = h.View.SectionGlobal(s.Id).Basis;
                foreach (RayCellHit c in _cellHits)
                {
                    if (!s.Cells.TryGetValue(c.Cell, out BlockInstance b) || !b.Alive) continue;
                    Vector3 normal = (toWorld * c.Normal.G()).Normalized();
                    var oh = new OrderedHit
                    {
                        Block = b, Owner = h, DistanceM = c.TEnter * Conv.CS,
                        AngleDeg = DamageModel.ImpactAngleDeg(dir.N(), normal.N()),
                    };
                    // A big block is met once, where the shell first enters it.
                    if (_hitPoints.ContainsKey(oh)) continue;
                    _hits.Add(oh);
                    _hitPoints[oh] = from + dir * oh.DistanceM;
                    _hitNormals[oh] = normal;
                }
            }
        }

        _hits.Sort((a, b) => a.DistanceM.CompareTo(b.DistanceM));
    }

    /// <summary>An explosion: damages blocks in range on every grid and shoves bodies away.</summary>
    public void Explode(Vector3 center, float radius, float damage, bool byPlayer, string cause, bool flak = false)
    {
        // Under water there's no fireball, just a column of spray thrown up at the surface.
        if (center.Y < Ocean.Level - 0.4f) Effects.UnderwaterBlast(this, center, radius);
        else if (flak) Effects.FlakBurst(this, center, radius);
        else Effects.Explosion(this, center, radius);
        Blast?.Invoke(center, radius, damage);
        var destroyedAll = new List<OrderedHit>();
        int hurtCount = 0;
        foreach (IGridHost h in GridRegistry.Hosts.ToArray())
        {
            if (!IsInstanceValid(h.Body)) continue;
            if (h.Body.GlobalPosition.DistanceTo(center) > h.Radius + radius) continue;
            var destroyed = new List<BlockInstance>();
            var hurt = new List<BlockInstance>();
            foreach (Section s in h.Model.Sections)
            {
                if (s.Cells.Count == 0) continue;
                Vector3 local = h.View.SectionGlobal(s.Id).AffineInverse() * center / Conv.CS;
                DamageModel.ApplyBlast(s, local.N(), radius, damage, destroyed, hurt);
            }

            hurtCount += hurt.Count;
            foreach (BlockInstance b in destroyed) destroyedAll.Add(new OrderedHit { Block = b, Owner = h });
            if (h.Body is RigidBody3D rb && hurt.Count > 0)
            {
                Vector3 away = (rb.GlobalPosition - center).Normalized();
                rb.ApplyImpulse(away * Mathf.Min(damage * 6f, rb.Mass * 3f), center - rb.GlobalPosition);
            }
        }

        // People and targets caught in the blast.
        foreach (IBlastable b in Hittables.Blastables.ToArray())
        {
            float dmg = InfantryDamage.FromBlast(b.BlastCenter.DistanceTo(center), radius, damage);
            if (dmg > 0) b.TakeBlast(dmg, center, byPlayer);
        }

        if (hurtCount > 0)
            Event?.Invoke(new CombatEvent
            {
                Report = new HitReport { Kind = HitKind.Explosion, BlockName = cause, Damage = damage, Destroyed = destroyedAll.Count > 0 },
                Position = center, ByPlayer = byPlayer, BlocksHit = hurtCount, BlocksDestroyed = destroyedAll.Count,
            });
        ApplyDestruction(destroyedAll, center, Vector3.Zero, byPlayer, damage);
    }

    /// <summary>
    /// Removes destroyed blocks, queues secondary explosions from ammo and fuel, and turns anything that's
    /// no longer attached into debris.
    /// </summary>
    private void ApplyDestruction(IEnumerable<OrderedHit> destroyed, Vector3 source, Vector3 push, bool byPlayer = true,
        float blastDamage = 0f)
    {
        var byHost = new Dictionary<IGridHost, List<BlockInstance>>();
        foreach (OrderedHit d in destroyed)
        {
            var h = (IGridHost)d.Owner;
            if (!byHost.TryGetValue(h, out List<BlockInstance> list)) byHost[h] = list = new List<BlockInstance>();
            list.Add(d.Block);
        }

        foreach ((IGridHost host, List<BlockInstance> blocks) in byHost)
        {
            if (!IsInstanceValid(host.Body)) continue;
            foreach (BlockInstance b in blocks)
            {
                Vector3 at = host.View.BlockWorldPos(b);
                if (b.Def.Kind == BlockKind.AmmoRack && b.AmmoUnits > 0)
                {
                    (float dmg, float r) = DamageModel.CookOff(b.AmmoUnits);
                    QueueBlast(at, r, dmg, byPlayer, "Ammo cook-off");
                }
                else if (b.Def.Kind == BlockKind.BombRack && b.AmmoUnits >= 1f)
                {
                    QueueBlast(at, Aero.BombRadiusM * 0.8f, Aero.BombDamage * 0.7f, byPlayer, "Bomb");
                }
                else if (b.Def.DeathExplosionDamage > 0)
                {
                    QueueBlast(at, b.Def.DeathExplosionRadiusM, b.Def.DeathExplosionDamage, byPlayer,
                        b.Def.Kind == BlockKind.FuelTank ? "Fuel tank" : "Propellant");
                }

                Effects.Shatter(this, at, b.Def, host.VelocityAt(at), (at - source).Normalized());
                host.Model.Remove(b);
                host.View.RemoveBlock(b);
            }

            SplitOff(host, source, push, blastDamage);
            host.OnBlocksRemoved();
        }
    }

    private void QueueBlast(Vector3 pos, float radius, float damage, bool byPlayer, string cause) =>
        _blasts.Add(new PendingBlast
        {
            Pos = pos, Radius = radius, Damage = damage, ByPlayer = byPlayer, Cause = cause,
            Delay = _rng.RandfRange(0.05f, 0.25f),
        });

    private void RunBlasts(float dt)
    {
        for (int i = _blasts.Count - 1; i >= 0; i--)
        {
            PendingBlast b = _blasts[i];
            b.Delay -= dt;
            if (b.Delay > 0)
            {
                _blasts[i] = b;
                continue;
            }

            _blasts.RemoveAt(i);
            Explode(b.Pos, b.Radius, b.Damage, b.ByPlayer, b.Cause);
        }
    }

    /// <summary>Turns pieces that lost their connection into separate physics bodies.</summary>
    private void SplitOff(IGridHost host, Vector3 source, Vector3 push, float blastDamage)
    {
        List<List<BlockInstance>> pieces = Connectivity.FindDetached(host.Model, host.Keep);
        Node parent = GetParent();
        foreach (List<BlockInstance> piece in pieces)
        {
            foreach (IGrouping<int, BlockInstance> group in piece.GroupBy(b => b.Section))
            {
                Transform3D xf = host.View.SectionGlobal(group.Key);
                var model = new GridModel();
                foreach (BlockInstance b in group)
                {
                    BlockInstance copy = model.Add(b.Def, b.Cell, b.Orientation);
                    copy.Hp = b.Hp;
                    copy.AmmoUnits = b.AmmoUnits;
                }

                model.RecomputeBounds();
                Vector3 centerW = xf * model.CenterOfMass().G();
                Vector3 vel = host.VelocityAt(centerW);
                Vector3 away = (centerW - source).Normalized();
                // Big blasts (ammo cooking off) throw pieces hard: this is what tosses turrets into the air.
                float blast = Mathf.Clamp(blastDamage / 120f, 0f, 4.5f);
                vel += away * _rng.RandfRange(1.5f, 4f) * (1f + blast) + Vector3.Up * blast * 2.2f +
                       push.LimitLength(6f) / Mathf.Max(model.TotalMassKg() / 200f, 1f);
                vel = vel.LimitLength(28f);
                float spinAmount = 2f + blast * 1.5f;
                var spin = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * spinAmount;

                foreach (BlockInstance b in group)
                {
                    host.Model.Remove(b);
                    host.View.RemoveBlock(b);
                }

                if (model.Blocks.Count > 0) parent.AddChild(DebrisBody.Create(model, xf, vel, spin));
            }
        }
    }

    /// <summary>Clears every shell and pending explosion (used when the range resets).</summary>
    public void Clear()
    {
        foreach (Projectile p in _shells) Effects.EndTracer(p.Tracer);
        _shells.Clear();
        _blasts.Clear();
        ClearTorpedoes();
        ClearOrdnance();
    }
}
