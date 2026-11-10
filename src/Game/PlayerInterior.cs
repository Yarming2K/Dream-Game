using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Moving about aboard vehicles on foot: decks carry you as they move and turn, ladders can be climbed,
/// and the vehicle you're standing on is known (for doors, lamps and the seat you walk up to).
/// </summary>
public partial class PlayerCharacter
{
    private RigidBody3D _deck;
    private Transform3D _deckWas;
    private PhysicsRayQueryParameters3D _deckQuery;

    /// <summary>The vehicle (or wreck) you're standing on, if any.</summary>
    public RigidBody3D Deck => _deck != null && IsInstanceValid(_deck) ? _deck : null;

    /// <summary>
    /// Standing on a vehicle, everything happens in its frame: the deck carries you along as it moves, rolls
    /// and turns (and you turn with it), and your own walking is relative to the deck. That holds at any
    /// speed, which the engine's own moving-platform handling doesn't for a ship at 50 km/h.
    /// </summary>
    private void Ride()
    {
        if (Deck == null || !_deck.IsInsideTree()) return;
        Transform3D now = _deck.GlobalTransform;
        Transform3D delta = now * _deckWas.AffineInverse();
        GlobalPosition = delta * GlobalPosition;
        Vector3 f = delta.Basis * Vector3.Forward;
        f.Y = 0;
        if (f.LengthSquared() > 1e-6f) RotateY(Mathf.Atan2(-f.X, -f.Z));
        _deckWas = now;
    }

    /// <summary>After moving: what's under your feet now. Stepping off a deck you keep its speed; landing on one, you take it off.</summary>
    private void UpdateDeck()
    {
        RigidBody3D under = null;
        if (IsOnFloor() || Climbing)
        {
            _deckQuery ??= new PhysicsRayQueryParameters3D
            {
                CollisionMask = GridRegistry.Layers.Vehicles | GridRegistry.Layers.Debris, CollideWithAreas = false,
            };
            _deckQuery.From = GlobalPosition + Vector3.Up * 0.3f;
            _deckQuery.To = GlobalPosition + Vector3.Down * (Climbing ? 0.2f : 0.45f);
            _deckQuery.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
            Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_deckQuery);
            if (hit.Count > 0 && hit["collider"].AsGodotObject() is RigidBody3D rb && rb is IGridHost) under = rb;
            // On a ladder, you hold on to whatever the ladder is fixed to.
            if (under == null && Climbing && _ladderHost is RigidBody3D lb) under = lb;
        }

        if (under == _deck) return;
        if (Deck != null)
        {
            Vector3 carried = PointVelocity(_deck, GlobalPosition);
            Velocity += new Vector3(carried.X, Mathf.Max(carried.Y, 0f), carried.Z);
        }

        if (under != null)
        {
            Vector3 carried = PointVelocity(under, GlobalPosition);
            Velocity -= new Vector3(carried.X, 0, carried.Z);
            _deckWas = under.GlobalTransform;
        }

        _deck = under;
    }

    private static Vector3 PointVelocity(RigidBody3D b, Vector3 point) =>
        b.LinearVelocity + b.AngularVelocity.Cross(point - b.GlobalTransform * b.CenterOfMass);

    // ---------- Ladders ----------

    private const float ClimbSpeed = 2.6f;
    private IGridHost _ladderHost;

    /// <summary>Holding on to a ladder.</summary>
    public bool Climbing { get; private set; }

    /// <summary>
    /// Next to a ladder and pushing toward it (or already on it), you climb: forward goes up, back goes down,
    /// nothing holds you still; Space lets go. At the top you step off forward.
    /// </summary>
    private bool Climb(float dt, Vector2 input, bool control, ref Vector3 v)
    {
        bool near = LadderNear(GlobalPosition + Vector3.Up * 0.5f, out IGridHost host) || LadderNear(GlobalPosition + Vector3.Up * 1.3f, out host);
        if (!near || !control || (control && Input.IsActionJustPressed("brake")))
        {
            Climbing = false;
            _ladderHost = null;
            return false;
        }

        if (!Climbing && input.Y >= -0.1f) return false;
        Climbing = true;
        _ladderHost = host;
        float want = input.Y < -0.1f ? ClimbSpeed : input.Y > 0.1f ? -ClimbSpeed : 0f;
        v.Y = Mathf.MoveToward(v.Y, want, 20f * dt);
        // A little push toward the ladder keeps you on it, and carries you over the top.
        Vector3 wish = (Transform.Basis * new Vector3(input.X, 0, Mathf.Min(input.Y, 0))).Normalized() * 1.2f;
        v.X = Mathf.MoveToward(v.X, wish.X, 10f * dt);
        v.Z = Mathf.MoveToward(v.Z, wish.Z, 10f * dt);
        _bob += Mathf.Abs(v.Y) * dt * 1.5f;
        return true;
    }

    /// <summary>Is there a ladder within reach of <paramref name="point"/>, on any vehicle or structure?</summary>
    private static bool LadderNear(Vector3 point, out IGridHost host)
    {
        host = null;
        var near = new System.Collections.Generic.List<IGridHost>();
        WorldQuery.HostsNear(point, 1.5f, near);
        foreach (IGridHost h in near)
        {
            if (!IsInstanceValid(h.Body)) continue;
            foreach (Section s in h.Model.Sections)
            {
                if (s.Cells.Count == 0) continue;
                Vector3 local = h.View.SectionWorldFast(s.Id).AffineInverse() * point / Conv.CS;
                var c = new Int3(Mathf.RoundToInt(local.X), Mathf.RoundToInt(local.Y), Mathf.RoundToInt(local.Z));
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!s.Cells.TryGetValue(c + new Int3(dx, 0, dz), out BlockInstance b) || !b.Alive || b.Def.Id is not ("ladder" or "grab_handles")) continue;
                    host = h;
                    return true;
                }
            }
        }

        return false;
    }
}
