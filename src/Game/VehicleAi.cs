using Godot;

namespace DreamGame.Game;

/// <summary>
/// The AI driver: takes a vehicle or ship to a point and stops there. When <see cref="StopToShoot"/> is
/// set it halts as soon as its gunners have something in range, so the crew can shoot steadily.
/// Fleet orders (move, attack, hold) will be built on this.
/// </summary>
public partial class VehicleBody
{
    /// <summary>The AI driver is following orders (otherwise an AI driver just sits with the brakes on).</summary>
    public bool AiDriving;

    /// <summary>Where the AI driver is heading.</summary>
    public Vector3 MoveGoal;

    /// <summary>How close counts as there.</summary>
    public float GoalRadius = 15f;

    /// <summary>Stop when the gunners have a target within this range (0 = keep going regardless).</summary>
    public float StopToShoot;

    /// <summary>True once the AI driver has reached its goal.</summary>
    public bool AtGoal { get; private set; }

    private void AiDrive(float dt)
    {
        Transform3D xf = GlobalTransform;
        Vector3 up = Vector3.Up;
        Vector3 fwd = -xf.Basis.Z;
        fwd.Y = 0;
        Vector3 to = MoveGoal - xf.Origin;
        to.Y = 0;
        float dist = to.Length();
        AtGoal = dist <= GoalRadius;

        bool engaged = StopToShoot > 0 && _aiVehicleTarget != null && IsInstanceValid(_aiVehicleTarget) &&
                       _aiVehicleTarget.GlobalPosition.DistanceTo(xf.Origin) < StopToShoot;
        if (IsNaval)
        {
            // Ships work the telegraph and the rudder: slow down near the goal, stop on it.
            int wanted = AtGoal || engaged ? TelegraphStop : dist < 150f ? TelegraphStop + 2 : TelegraphNames.Length - 1;
            if (Telegraph != wanted) Telegraph = wanted;
            Steer = dist > 1f && fwd.LengthSquared() > 0.001f ? Mathf.Clamp(-SignedAngle(fwd, to, up) * 1.5f, -1f, 1f) : 0f;
            Braking = false;
            return;
        }

        if (AtGoal || engaged || fwd.LengthSquared() < 0.001f)
        {
            Throttle = 0;
            Steer = 0;
            Braking = true;
            return;
        }

        float err = SignedAngle(fwd, to, up);
        Steer = Mathf.Clamp(-err * 2f, -1f, 1f);
        // Ease off to turn on the spot when the goal is well off to one side.
        Throttle = Mathf.Abs(err) > 1.2f ? 0.25f : dist < 40f ? 0.45f : 0.8f;
        Braking = false;
    }

    /// <summary>Angle from <paramref name="a"/> to <paramref name="b"/> about <paramref name="axis"/> (positive = to the left).</summary>
    private static float SignedAngle(Vector3 a, Vector3 b, Vector3 axis) => Mathf.Atan2(axis.Dot(a.Cross(b)), a.Dot(b));
}
