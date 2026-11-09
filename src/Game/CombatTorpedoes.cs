using System.Collections.Generic;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Torpedoes: they drop into the water, settle just under the surface, turn onto the bearing they were
/// aimed at (the gyro), and run straight until they hit a hull, the shore, or run out of fuel. They go
/// off under the keel too, if a hull passes just above them.
/// </summary>
public partial class Combat
{
    private sealed class TorpedoRun
    {
        public IGridHost Owner;
        public Vector3 Pos;
        public Vector3 Dir;
        public Vector3 Want;
        public float FallSpeed;
        public float Travelled;
        public float WakeTimer;
        public bool ByPlayer;
        public Node3D Visual;
    }

    private readonly List<TorpedoRun> _torpedoes = new();
    private static Mesh _torpedoMesh;
    private static Material _torpedoMat;

    /// <summary>How far above a torpedo a hull can pass and still set it off.</summary>
    private const float ProximityM = 1.1f;

    public void LaunchTorpedo(IGridHost owner, Vector3 pos, Vector3 dir, Vector3? aim, bool byPlayer, Vector3 carrierVelocity)
    {
        var flat = new Vector3(dir.X, 0, dir.Z);
        flat = flat.LengthSquared() > 1e-4f ? flat.Normalized() : Vector3.Forward;
        Vector3 want = flat;
        if (aim.HasValue)
        {
            Vector3 to = aim.Value - pos;
            to.Y = 0;
            if (to.LengthSquared() > 4f) want = to.Normalized();
        }

        _torpedoMesh ??= new CapsuleMesh { Radius = 0.22f, Height = 2.8f, RadialSegments = 12, Rings = 3 };
        _torpedoMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.24f, 0.25f), Metallic = 0.7f, Roughness = 0.4f };
        var visual = new MeshInstance3D { Mesh = _torpedoMesh, MaterialOverride = _torpedoMat };
        AddChild(visual);
        visual.GlobalPosition = pos;

        _torpedoes.Add(new TorpedoRun
        {
            Owner = owner, Pos = pos, Dir = flat, Want = want, ByPlayer = byPlayer, Visual = visual,
            FallSpeed = Mathf.Min(carrierVelocity.Y, 0f),
        });
        Sfx.At(this, pos, SoundId.RocketLaunch, -4f, 0.55f, 14f, delayByDistance: false);
        if (pos.Y < Ocean.Level + 1.5f) Effects.Splash(this, pos, 0.8f);
    }

    private void TorpedoTick(float dt)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        for (int i = _torpedoes.Count - 1; i >= 0; i--)
        {
            TorpedoRun t = _torpedoes[i];
            Vector3 from = t.Pos;
            bool inWater = from.Y <= Ocean.Level;

            // The gyro swings it onto its bearing once it's in the water.
            if (inWater)
            {
                float turn = t.Dir.SignedAngleTo(t.Want, Vector3.Up);
                float max = Mathf.DegToRad(35f) * dt;
                t.Dir = t.Dir.Rotated(Vector3.Up, Mathf.Clamp(turn, -max, max)).Normalized();
            }

            float y;
            if (!inWater)
            {
                t.FallSpeed -= GunMath.Gravity * dt;
                y = from.Y + t.FallSpeed * dt;
            }
            else
            {
                y = Mathf.MoveToward(from.Y, Ocean.Level - Torpedoes.RunDepth, 3f * dt);
            }

            float speed = inWater ? Torpedoes.Speed : 10f;
            Vector3 to = new Vector3(from.X, y, from.Z) + t.Dir * speed * dt;
            if (!inWater && to.Y <= Ocean.Level) Effects.Splash(this, to, 1.1f);
            t.Travelled += from.DistanceTo(to);
            t.Pos = to;

            bool armed = t.Travelled > 25f;
            if (armed && TorpedoStrikes(from, to, t.Travelled < 120f ? t.Owner : null, out Vector3 at))
            {
                Detonate(t, at);
                _torpedoes.RemoveAt(i);
                continue;
            }

            Godot.Collections.Dictionary ground = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, GridRegistry.Layers.World));
            if (ground.Count > 0)
            {
                if (armed) Detonate(t, ground["position"].AsVector3());
                else t.Visual?.QueueFree();
                _torpedoes.RemoveAt(i);
                continue;
            }

            if (t.Travelled > Torpedoes.RangeM)
            {
                t.Visual?.QueueFree();
                _torpedoes.RemoveAt(i);
                continue;
            }

            if (IsInstanceValid(t.Visual))
            {
                t.Visual.GlobalPosition = to;
                // The capsule's long axis is Y; lay it along the direction of travel.
                Vector3 side = t.Dir.Cross(Vector3.Up).Normalized();
                t.Visual.GlobalBasis = new Basis(side, t.Dir, side.Cross(t.Dir));
            }

            t.WakeTimer -= dt;
            if (inWater && t.WakeTimer <= 0)
            {
                t.WakeTimer = 0.09f;
                Effects.Wake(this, to);
            }
        }
    }

    /// <summary>Did the torpedo run into a hull, or pass right under one? <paramref name="spare"/> can't be hit (the boat that just fired it).</summary>
    private bool TorpedoStrikes(Vector3 from, Vector3 to, IGridHost spare, out Vector3 at)
    {
        at = default;
        Vector3 seg = to - from;
        float len = seg.Length();
        if (len < 1e-5f) return false;
        Vector3 dir = seg / len;
        foreach (float lift in new[] { 0f, ProximityM })
        {
            Vector3 o = from + Vector3.Up * lift;
            CollectGridHits(o, dir, len);
            foreach (OrderedHit h in _hits)
            {
                if (h.Owner == spare) continue;
                at = _hitPoints[h];
                return true;
            }
        }

        return false;
    }

    private void Detonate(TorpedoRun t, Vector3 at)
    {
        if (IsInstanceValid(t.Visual)) t.Visual.QueueFree();
        Explode(at, Torpedoes.RadiusM, Torpedoes.Damage, t.ByPlayer, "Torpedo");
    }

    private void ClearTorpedoes()
    {
        foreach (TorpedoRun t in _torpedoes)
            if (IsInstanceValid(t.Visual)) t.Visual.QueueFree();
        _torpedoes.Clear();
    }
}
