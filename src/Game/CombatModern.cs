using System;
using System.Collections.Generic;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Modern and future weapons: laser beams that burn whatever they touch, point defense knocking missiles
/// and bombs out of the air, cruise missiles that fly themselves to a spot on the map, and the shock wave
/// of a really big blast.
/// </summary>
public partial class Combat
{
    /// <summary>Blasts at least this big get the full treatment: a fireball, a rising cloud, and a shock wave that shoves everything.</summary>
    public const float BigBlastM = 7f;

    private struct BeamFx
    {
        public Vector3 From, To;
        public float Life;
    }

    private readonly List<BeamFx> _beams = new();
    private static readonly Color BeamColor = new(1f, 0.25f, 0.18f, 0.9f);

    /// <summary>A missile or bomb was shot down (where, and whether the player's side did it).</summary>
    public event Action<Vector3, bool> ShotDown;

    // ---------------------------------------------------------------- lasers

    /// <summary>
    /// One laser pulse: burns the first thing along the line (a missile or bomb, a block, a person) at full
    /// strength out to <paramref name="rangeM"/>, weaker beyond as the beam spreads. Returns where it stopped.
    /// </summary>
    public Vector3 FireBeam(VehicleBody owner, Vector3 from, Vector3 dir, float rangeM, float damage, bool byPlayer)
    {
        float maxLen = Mathf.Max(rangeM * 3f, 50f);
        float stop = maxLen;
        Vector3 normal = -dir;
        bool ground = WorldQuery.GroundRay(this, from, from + dir * maxLen, out Vector3 gp, out Vector3 gn);
        if (ground)
        {
            stop = from.DistanceTo(gp);
            normal = gn;
        }

        if (from.Y > Ocean.Level && dir.Y < -1e-3f)
        {
            float water = (from.Y - Ocean.Level) / -dir.Y;
            if (water < stop)
            {
                stop = water;
                ground = false;
            }
        }

        float Strength(float d) => d <= rangeM ? 1f : Mathf.Pow(rangeM / d, 2f);
        int faction = owner != null && IsInstanceValid(owner) ? owner.Faction : -1;

        // Missiles and bombs in the way.
        if (FirstOrdnanceAlong(from, dir, stop, 1.4f, faction, out object run, out float od))
        {
            Vector3 at = from + dir * od;
            DamageOrdnance(run, damage * Strength(od) * 2f, byPlayer);
            AddBeam(from, at);
            return at;
        }

        // Blocks: the first one that isn't its own.
        CollectGridHits(from, dir, stop);
        foreach (OrderedHit h in _hits)
        {
            if (h.Owner == owner) continue;
            Vector3 at = _hitPoints[h];
            var host = (IGridHost)h.Owner;
            if (host is VehicleBody victim) victim.NoteAttack(owner);
            float burn = DamageModel.LaserBurn(h.Block.Def, damage * Strength(h.DistanceM));
            bool destroyed = DamageModel.ApplyDamage(h.Block, burn);
            if (!destroyed && host is VehicleBody burner) burner.HitBy(h.Block, burn * 2f);
            Effects.LaserBurn(this, at, _hitNormals[h], destroyed);
            if (destroyed)
            {
                Event?.Invoke(new CombatEvent
                {
                    Report = new HitReport { Kind = HitKind.Penetration, BlockName = h.Block.Def.Name, Damage = burn, Destroyed = true },
                    Position = at, ByPlayer = byPlayer, BlocksHit = 1, BlocksDestroyed = 1, Small = true,
                });
                ApplyDestruction(new[] { h }, from, dir * 0.5f, byPlayer);
            }

            AddBeam(from, at);
            return at;
        }

        // People and practice targets.
        Vector3 end = from + dir * stop;
        if (WorldQuery.PeopleNear(from, end, 2f))
        {
            _bodyQuery ??= new PhysicsRayQueryParameters3D { CollisionMask = GridRegistry.Layers.Characters | GridRegistry.Layers.Targets };
            _bodyQuery.From = from;
            _bodyQuery.To = end;
            _bodyQuery.Exclude = null;
            Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_bodyQuery);
            if (hit.Count > 0 && hit["collider"].AsGodotObject() is IHittable person)
            {
                Vector3 at = hit["position"].AsVector3();
                person.TakeHit(damage * 1.5f * Strength(from.DistanceTo(at)), at, dir, byPlayer);
                AddBeam(from, at);
                return at;
            }
        }

        if (ground) Effects.LaserBurn(this, end, normal, false);
        AddBeam(from, end);
        return end;
    }

    private void AddBeam(Vector3 from, Vector3 to)
    {
        // A pulse shows for a little longer than the gap to the next one, so a firing laser is a steady beam.
        _beams.Add(new BeamFx { From = from, To = to, Life = 0.12f });
    }

    private void DrawBeams(float dt)
    {
        for (int i = _beams.Count - 1; i >= 0; i--)
        {
            BeamFx b = _beams[i];
            b.Life -= dt;
            if (b.Life <= 0)
            {
                _beams[i] = _beams[^1];
                _beams.RemoveAt(_beams.Count - 1);
                continue;
            }

            _beams[i] = b;
            _tracers.Beam(b.From, b.To, 0.035f, new Color(BeamColor.R, BeamColor.G, BeamColor.B, Mathf.Min(1f, b.Life / 0.06f)), 0.9f);
        }
    }

    // ---------------------------------------------------------------- point defense

    /// <summary>The first enemy missile or bomb within <paramref name="reach"/> m of a line, and how far along it is.</summary>
    private bool FirstOrdnanceAlong(Vector3 from, Vector3 dir, float len, float reach, int faction, out object run, out float dist)
    {
        run = null;
        dist = float.PositiveInfinity;
        foreach (MissileRun m in _missiles)
            if (!m.Dead && (faction < 0 || m.Faction != faction)) Consider(m, m.Pos, ref run, ref dist);
        foreach (BombRun b in _bombs)
            if (!b.Dead && (faction < 0 || b.Faction != faction)) Consider(b, b.Pos, ref run, ref dist);
        return run != null;

        void Consider(object o, Vector3 p, ref object best, ref float bestD)
        {
            float t = (p - from).Dot(dir);
            if (t < 0 || t > len || t >= bestD) return;
            if ((from + dir * t).DistanceSquaredTo(p) > reach * reach) return;
            best = o;
            bestD = t;
        }
    }

    /// <summary>A shell passing close enough to an enemy missile or bomb hits it (and is used up).</summary>
    private bool ShootDown(Projectile p, Vector3 from, Vector3 to)
    {
        if (_missiles.Count == 0 && _bombs.Count == 0) return false;
        float len = from.DistanceTo(to);
        if (len < 1e-4f) return false;
        int faction = p.Owner is VehicleBody v && IsInstanceValid(v) ? v.Faction : -1;
        float reach = p.Shell.CaliberMm <= 40f ? 1.5f : 2.5f;
        if (!FirstOrdnanceAlong(from, (to - from) / len, len, reach, faction, out object run, out float d)) return false;
        float dmg = p.Shell.Type == AmmoType.AP ? p.Shell.Damage : p.Shell.HeDamage;
        if (p.Shell.Type is AmmoType.HE or AmmoType.Flak) dmg *= 1.5f;
        DamageOrdnance(run, dmg, p.ByPlayer);
        Vector3 at = from + (to - from) / len * d;
        if (p.Shell.Type is AmmoType.HE or AmmoType.Flak) Effects.FlakBurst(this, at, Mathf.Max(p.Shell.HeRadiusM, 0.6f));
        return true;
    }

    private void DamageOrdnance(object run, float damage, bool byPlayer)
    {
        Vector3 at;
        float radius;
        switch (run)
        {
            case MissileRun m:
                m.Hp -= damage;
                if (m.Hp > 0 || m.Dead) return;
                m.Dead = true;
                at = m.Pos;
                radius = m.Stats.RadiusM;
                break;
            case BombRun b:
                b.Hp -= damage;
                if (b.Hp > 0 || b.Dead) return;
                b.Dead = true;
                at = b.Pos;
                radius = Aero.BombRadiusM;
                break;
            default:
                return;
        }

        // Knocked apart in the air: the warhead only partly goes off.
        Effects.FlakBurst(this, at, Mathf.Clamp(radius * 0.6f, 1.5f, 6f));
        Explode(at, radius * 0.4f, 60f, byPlayer, "Intercepted", flak: true);
        ShotDown?.Invoke(at, byPlayer);
    }

    /// <summary>
    /// The enemy missile or bomb that point defense on <paramref name="ship"/> should shoot at: the nearest one
    /// within <paramref name="range"/> that's coming its way (or already close).
    /// </summary>
    public bool NearestThreat(VehicleBody ship, float range, out Vector3 pos, out Vector3 vel)
    {
        pos = vel = Vector3.Zero;
        Vector3 c = ship.GlobalTransform * ship.CenterOfMass;
        float best = range;
        bool found = false;
        foreach (MissileRun m in _missiles)
            if (!m.Dead && m.Faction != ship.Faction) Consider(m.Pos, m.Vel, ref pos, ref vel);
        foreach (BombRun b in _bombs)
            if (!b.Dead && b.Faction != ship.Faction) Consider(b.Pos, b.Vel, ref pos, ref vel);
        return found;

        void Consider(Vector3 p, Vector3 v, ref Vector3 bp, ref Vector3 bv)
        {
            Vector3 rel = p - c;
            float d = rel.Length();
            if (d >= best) return;
            float v2 = Mathf.Max(v.LengthSquared(), 1f);
            float t = Mathf.Clamp(-rel.Dot(v) / v2, 0f, 12f);
            float miss = (rel + v * t).Length();
            if (d > 250f && miss > ship.Radius + 80f) return;
            best = d;
            bp = p;
            bv = v;
            found = true;
        }
    }

    // ---------------------------------------------------------------- cruise missiles

    /// <summary>
    /// Where a cruise missile steers: out at a cruising height (higher the further it has to go, and always
    /// well clear of the ground or sea under it), then down onto its target for the last stretch.
    /// </summary>
    private static Vector3 CruiseAim(MissileRun m)
    {
        Vector3 goal = m.Goal.Value;
        Vector3 flat = goal - m.Pos;
        flat.Y = 0;
        float dist = flat.Length();
        if (dist < 450f) return goal;
        float under = Mathf.Max(WorldQuery.Ground?.HeightAt(m.Pos.X, m.Pos.Z) ?? 0f, Ocean.Level);
        float cruise = Mathf.Max(goal.Y + Mathf.Clamp(dist * 0.08f, 40f, 160f), under + 60f);
        Vector3 ahead = m.Pos + flat / dist * Mathf.Min(dist - 350f, 500f);
        return new Vector3(ahead.X, cruise, ahead.Z);
    }

    private static Mesh _vlsBody, _vlsNose, _vlsFin;
    private static Material _vlsPaint, _vlsDark;

    /// <summary>What a missile out of a deck cell looks like in flight: a long white body, a pointed nose and fins.</summary>
    public static Node3D VlsMissileVisual(VlsLoad load)
    {
        bool strike = load == VlsLoad.Strike;
        float r = strike ? 0.17f : 0.12f, len = strike ? 3.6f : 2.6f;
        _vlsBody ??= new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 14, Rings = 1 };
        _vlsNose ??= new CylinderMesh { TopRadius = 0, BottomRadius = 1, Height = 1, RadialSegments = 14, Rings = 1 };
        _vlsFin ??= new BoxMesh { Size = new Vector3(1, 1, 1) };
        _vlsPaint ??= new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.83f, 0.8f), Metallic = 0.3f, Roughness = 0.5f };
        _vlsDark ??= new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.21f, 0.22f), Metallic = 0.5f, Roughness = 0.5f };
        var root = new Node3D { Name = "Missile" };
        // The visual points along -Z, like every other missile.
        var along = new Basis(Vector3.Right, -Mathf.Pi / 2);
        root.AddChild(new MeshInstance3D
        {
            Mesh = _vlsBody, MaterialOverride = _vlsPaint,
            Transform = new Transform3D(along * Basis.FromScale(new Vector3(r, len, r)), Vector3.Zero),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = _vlsNose, MaterialOverride = strike ? _vlsPaint : _vlsDark,
            Transform = new Transform3D(along * Basis.FromScale(new Vector3(r, r * 4f, r)), new Vector3(0, 0, -len * 0.5f - r * 2f)),
        });
        for (int i = 0; i < 4; i++)
        {
            var spin = new Basis(Vector3.Back, i * Mathf.Pi / 2);
            root.AddChild(new MeshInstance3D
            {
                Mesh = _vlsFin, MaterialOverride = _vlsDark,
                Transform = new Transform3D(spin * Basis.FromScale(new Vector3(0.03f, r * 2.6f, 0.4f)), spin * new Vector3(0, r * 1.6f, len * 0.42f)),
            });
            if (!strike) continue;
            // Cruise missiles also have small wings amidships.
            root.AddChild(new MeshInstance3D
            {
                Mesh = _vlsFin, MaterialOverride = _vlsPaint,
                Transform = new Transform3D(spin * Basis.FromScale(new Vector3(0.025f, r * 4f, 0.5f)), spin * new Vector3(0, r * 2.4f, -0.1f)),
            });
        }

        return root;
    }

    // ---------------------------------------------------------------- big blasts

    /// <summary>The shock wave of a big blast: every body nearby gets shoved away from it, less the further it is.</summary>
    private void ShockwavePush(Vector3 center, float radius, float damage, List<IGridHost> alreadyHit)
    {
        float reach = radius * 2.5f;
        var around = new List<IGridHost>();
        WorldQuery.HostsNear(center, reach, around);
        foreach (IGridHost h in around)
        {
            if (h.Body is not RigidBody3D rb || !IsInstanceValid(rb)) continue;
            Vector3 to = rb.GlobalPosition - center;
            float d = to.Length();
            float k = Mathf.Clamp(1f - d / reach, 0f, 1f);
            if (k <= 0) continue;
            if (h is VehicleBody vb) vb.Unpark();
            // Thrown up a little as well as away, as a blast off the ground would.
            Vector3 away = (d > 0.1f ? to / d : Vector3.Up) + Vector3.Up * 0.35f;
            float shove = Mathf.Min(damage * 4f * k * k, rb.Mass * 6f * k);
            rb.ApplyImpulse(away.Normalized() * shove, center - rb.GlobalPosition);
        }
    }
}
