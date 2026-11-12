using DreamGame.Core;
using Godot;
using Num = System.Numerics;

namespace DreamGame.Game;

/// <summary>
/// The range in the campaign world: travelling to a place picked on the map (M, then a name in the list), for looking
/// round the world's mountains, valleys and canyons without driving all the way.
/// </summary>
public partial class TestRange
{
    private Node3D _arrival;
    private Transform3D _arrivalAt;
    private float _arrivalHeld;

    /// <summary>
    /// Goes to a place: a ground vehicle (or you on foot) to the nearest gentle dry ground, a ship to the nearest deep
    /// water, an aircraft 260 m above it and flying. It's held still there until the fine ground under it is ready.
    /// </summary>
    private void Travel(Num.Vector2 to)
    {
        if (!InWorld) return;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        VehicleBody v = Mounted ? _mount : null;
        Num.Vector2 p;
        float y;
        if (v != null && v.IsNaval)
        {
            p = Nearest(to, q => _world.Coarse.HeightAt(q.X, q.Y) < -14f);
            y = VehicleBody.FloatHeight(v.Analysis);
        }
        else if (v != null && v.IsAir)
        {
            p = to;
            y = Mathf.Max(_world.HeightAt(to.X, to.Y), Ocean.Level) + 260f;
        }
        else
        {
            p = Nearest(to, Gentle);
            y = _world.HeightAt(p.X, p.Y) + (v != null ? VehicleBody.SpawnHeight(v.Analysis) + 0.4f : 1.2f);
        }

        var at = new Vector3(p.X, y, p.Y);
        WorldGround.Instance?.JumpTo(at);
        Node3D who = v != null ? v : _me;
        v?.Unpark();
        // Keep the way it was facing, levelled.
        Vector3 fwd = -who.GlobalBasis.Z;
        float yaw = Mathf.Atan2(-fwd.X, -fwd.Z);
        _arrival = who;
        _arrivalAt = new Transform3D(new Basis(Vector3.Up, yaw), at);
        _arrivalHeld = 0f;
        _trauma = 0f;
        Big("On the way…");
    }

    /// <summary>The nearest point (searching outward in rings, up to 4 km) that suits.</summary>
    private static Num.Vector2 Nearest(Num.Vector2 from, System.Func<Num.Vector2, bool> ok)
    {
        if (ok(from)) return from;
        for (float r = 30f; r < 4000f; r += 30f)
        {
            int n = Mathf.Max(8, (int)(r / 15f));
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.Tau / n;
                var q = from + new Num.Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (ok(q)) return q;
            }
        }

        return from;
    }

    /// <summary>Dry ground no steeper than about 12° across 8 m.</summary>
    private bool Gentle(Num.Vector2 q)
    {
        float h = _world.HeightAt(q.X, q.Y);
        if (h < 1.5f) return false;
        float hx = _world.HeightAt(q.X + 8f, q.Y), hz = _world.HeightAt(q.X, q.Y + 8f), hx2 = _world.HeightAt(q.X - 8f, q.Y);
        return Mathf.Abs(hx - h) < 1.7f && Mathf.Abs(hz - h) < 1.7f && Mathf.Abs(hx2 - h) < 1.7f;
    }

    /// <summary>Holds what just travelled where it arrived until the ground under it is ready, then lets it go.</summary>
    private void HoldArrival(float dt)
    {
        if (_arrival == null) return;
        if (!IsInstanceValid(_arrival))
        {
            _arrival = null;
            return;
        }

        _arrivalHeld += dt;
        bool ready = WorldGround.Instance == null || WorldGround.Instance.Ready(_arrivalAt.Origin);
        if (ready && _arrival is VehicleBody { IsAir: false, IsNaval: false } gv && WorldQuery.Ground != null)
        {
            // The fine ground is here now: stand exactly on it.
            Vector3 o = _arrivalAt.Origin;
            _arrivalAt.Origin = new Vector3(o.X, WorldQuery.Ground.HeightAt(o.X, o.Z) + VehicleBody.SpawnHeight(gv.Analysis) + 0.4f, o.Z);
        }
        else if (ready && _arrival == _me && WorldQuery.Ground != null)
        {
            Vector3 o = _arrivalAt.Origin;
            _arrivalAt.Origin = new Vector3(o.X, WorldQuery.Ground.HeightAt(o.X, o.Z) + 1.2f, o.Z);
        }

        if (_arrival is RigidBody3D rb)
        {
            rb.GlobalTransform = _arrivalAt;
            rb.LinearVelocity = Vector3.Zero;
            rb.AngularVelocity = Vector3.Zero;
        }
        else if (_arrival is CharacterBody3D cb)
        {
            cb.GlobalPosition = _arrivalAt.Origin;
            cb.Velocity = Vector3.Zero;
        }

        if (!ready || _arrivalHeld < 0.6f) return;
        if (_arrival is VehicleBody { IsAir: true } av)
        {
            // Planes arrive flying; helicopters hovering.
            if (av.Analysis.Mode == MobilityMode.Plane)
            {
                av.LinearVelocity = -av.GlobalBasis.Z * 75f;
                av.SetAirThrottle(0.8f);
            }
        }

        _arrival = null;
        Big("Arrived");
    }
}
