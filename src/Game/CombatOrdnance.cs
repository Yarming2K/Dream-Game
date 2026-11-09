using System.Collections.Generic;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Bombs fall (keeping the speed of the plane that dropped them) and go off on whatever they hit. Missiles
/// burn their motor, then coast; guided ones steer with their fins toward the crosshair (optical) or the
/// aircraft they locked on to (radar), and radar missiles go off when they get close.
/// </summary>
public partial class Combat
{
    private sealed class BombRun
    {
        public IGridHost Owner;
        public Vector3 Pos, Vel;
        public bool ByPlayer;
        public float Age;
        public Node3D Visual;
    }

    private sealed class MissileRun
    {
        public VehicleBody Owner;
        public Vector3 Pos, Vel;
        public MissileStats Stats;
        public bool ByPlayer;
        public float Age, Burn, Search;
        public VehicleBody Target;
        public Node3D Visual;
        public CpuParticles3D Trail;
    }

    private readonly List<BombRun> _bombs = new();
    private readonly List<MissileRun> _missiles = new();
    private static Mesh _bombMesh;
    private static Material _bombMat;

    public void DropBomb(IGridHost owner, Vector3 pos, Vector3 vel, bool byPlayer)
    {
        _bombMesh ??= new CapsuleMesh { Radius = 0.18f, Height = 1.0f, RadialSegments = 12, Rings = 3 };
        _bombMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.27f, 0.3f, 0.24f), Metallic = 0.5f, Roughness = 0.5f };
        var visual = new Node3D { Name = "Bomb" };
        visual.AddChild(new MeshInstance3D { Mesh = _bombMesh, MaterialOverride = _bombMat, Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
        AddChild(visual);
        visual.GlobalPosition = pos;
        _bombs.Add(new BombRun { Owner = owner, Pos = pos, Vel = vel, ByPlayer = byPlayer, Visual = visual });
    }

    public void LaunchMissile(VehicleBody owner, Vector3 pos, Vector3 dir, Vector3 carrierVelocity, MissileStats stats, bool byPlayer,
        Node3D visual, VehicleBody target)
    {
        AddChild(visual);
        visual.GlobalPosition = pos;
        _missiles.Add(new MissileRun
        {
            Owner = owner, Pos = pos, Vel = carrierVelocity + dir * 25f, Stats = stats, ByPlayer = byPlayer, Burn = stats.BurnS,
            Target = target, Visual = visual, Trail = stats.BurnS > 0 ? Effects.RocketTrail(this, pos) : null,
        });
        Sfx.At(this, pos, SoundId.RocketLaunch, 0f, 0.9f, 16f, delayByDistance: false);
    }

    private void OrdnanceTick(float dt)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            BombRun b = _bombs[i];
            b.Age += dt;
            Vector3 from = b.Pos;
            float speed = b.Vel.Length();
            b.Vel += Vector3.Down * GunMath.Gravity * dt;
            b.Vel *= Mathf.Max(0, 1f - Aero.BombDragK * speed * dt);
            Vector3 to = from + b.Vel * dt;
            if (Impact(space, from, to, b.Age < 0.6f ? b.Owner : null, out Vector3 at) || b.Age > 60f)
            {
                if (IsInstanceValid(b.Visual)) b.Visual.QueueFree();
                _bombs.RemoveAt(i);
                if (b.Age <= 60f) Explode(at, Aero.BombRadiusM, Aero.BombDamage, b.ByPlayer, "Bomb");
                continue;
            }

            b.Pos = to;
            Orient(b.Visual, to, b.Vel);
        }

        for (int i = _missiles.Count - 1; i >= 0; i--)
        {
            MissileRun m = _missiles[i];
            if (MissileStep(space, m, dt)) continue;
            Effects.StopTrail(m.Trail);
            if (IsInstanceValid(m.Visual)) m.Visual.QueueFree();
            _missiles.RemoveAt(i);
        }
    }

    /// <summary>Moves a missile one tick. Returns false once it's gone off.</summary>
    private bool MissileStep(PhysicsDirectSpaceState3D space, MissileRun m, float dt)
    {
        m.Age += dt;
        float speed = Mathf.Max(m.Vel.Length(), 1f);
        Vector3 vdir = m.Vel / speed;

        // Find something to steer at.
        Vector3? aim = null;
        if (m.Stats.Seeker == SeekerKind.Optical && m.Owner != null && IsInstanceValid(m.Owner)) aim = m.Owner.GuidancePoint(m.ByPlayer);
        else if (m.Stats.Seeker == SeekerKind.Radar)
        {
            if (m.Target == null || !IsInstanceValid(m.Target) || m.Target.Model.Blocks.Count == 0)
            {
                m.Target = null;
                m.Search -= dt;
                if (m.Search <= 0)
                {
                    m.Search = 0.5f;
                    m.Target = VehicleBody.RadarLock(m.Pos, vdir, m.Owner != null && IsInstanceValid(m.Owner) ? m.Owner.Faction : -1, 20f, 3000f);
                }
            }

            if (m.Target != null)
            {
                Vector3 tp = m.Target.GlobalTransform * m.Target.CenterOfMass;
                if (m.Pos.DistanceTo(tp) < Mathf.Max(3.5f, m.Target.Radius * 0.4f))
                {
                    Explode(m.Pos, m.Stats.RadiusM * 1.2f, m.Stats.Damage, m.ByPlayer, "Missile", flak: true);
                    return false;
                }

                aim = GunMath.LeadTarget(m.Pos.N(), tp.N(), m.Target.LinearVelocity.N(), speed).G();
            }
        }

        bool guided = aim.HasValue && m.Stats.MaxTurnAccel > 0 && m.Age > 0.25f;
        if (guided)
        {
            Vector3 want = (aim.Value - m.Pos).Normalized();
            Vector3 lateral = want - vdir * want.Dot(vdir);
            m.Vel += (lateral * speed * 4f).LimitLength(m.Stats.MaxTurnAccel) * dt;
            m.Vel += Vector3.Up * GunMath.Gravity * dt; // the fins hold it up while it's steering
        }

        if (m.Burn > 0)
        {
            m.Burn -= dt;
            m.Vel += vdir * (m.Stats.ThrustN / Mathf.Max(m.Stats.MassKg, 1f)) * dt;
            if (m.Burn <= 0) Effects.StopTrail(m.Trail);
        }

        m.Vel += Vector3.Down * GunMath.Gravity * dt;
        m.Vel *= Mathf.Max(0, 1f - Aero.MissileDragK * speed * dt);
        Vector3 from = m.Pos, to = from + m.Vel * dt;
        if (Impact(space, from, to, m.Age < 0.8f ? m.Owner : null, out Vector3 at))
        {
            Explode(at, m.Stats.RadiusM, m.Stats.Damage, m.ByPlayer, "Missile");
            return false;
        }

        if (m.Age > Aero.MissileLifeS)
        {
            Explode(to, m.Stats.RadiusM * 0.6f, m.Stats.Damage * 0.3f, m.ByPlayer, "Missile", flak: true);
            return false;
        }

        m.Pos = to;
        if (m.Burn > 0) Effects.Follow(m.Trail, to);
        Orient(m.Visual, to, m.Vel);
        return true;
    }

    /// <summary>Does the segment hit a block, the ground or the sea? <paramref name="spare"/> can't be hit (whoever just fired).</summary>
    private bool Impact(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, IGridHost spare, out Vector3 at)
    {
        at = to;
        Vector3 seg = to - from;
        float len = seg.Length();
        if (len < 1e-5f) return false;
        Vector3 dir = seg / len;
        float best = float.PositiveInfinity;

        Godot.Collections.Dictionary ground = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, GridRegistry.Layers.World));
        if (ground.Count > 0) best = from.DistanceTo(ground["position"].AsVector3());
        if (from.Y > Ocean.Level && to.Y <= Ocean.Level) best = Mathf.Min(best, len * (from.Y - Ocean.Level) / (from.Y - to.Y));

        CollectGridHits(from, dir, Mathf.Min(len, best));
        foreach (OrderedHit h in _hits)
        {
            if (h.Owner == spare) continue;
            best = Mathf.Min(best, h.DistanceM);
            break;
        }

        if (float.IsPositiveInfinity(best)) return false;
        at = from + dir * best;
        return true;
    }

    private static void Orient(Node3D visual, Vector3 pos, Vector3 vel)
    {
        if (!IsInstanceValid(visual)) return;
        visual.GlobalPosition = pos;
        if (vel.LengthSquared() < 0.01f) return;
        Vector3 d = vel.Normalized();
        Vector3 up = Mathf.Abs(d.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        visual.GlobalBasis = Basis.LookingAt(d, up);
    }

    private void ClearOrdnance()
    {
        foreach (BombRun b in _bombs)
            if (IsInstanceValid(b.Visual)) b.Visual.QueueFree();
        foreach (MissileRun m in _missiles)
        {
            Effects.StopTrail(m.Trail);
            if (IsInstanceValid(m.Visual)) m.Visual.QueueFree();
        }

        _bombs.Clear();
        _missiles.Clear();
    }
}
