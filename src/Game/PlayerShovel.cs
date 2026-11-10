using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The shovel (4): left click digs where you're looking, up to a few meters away, and the earth goes on
/// your shovel (you can carry 2 m³); dig on with it full and the earth is thrown ahead of the hole as a
/// parapet. Right click piles what you're carrying where you're looking. A foxhole takes about ten seconds;
/// a firing pit for a tank, a minute or so.
/// </summary>
public partial class PlayerCharacter
{
    public const float ShovelReachM = 3.6f, CarryM3 = 2f;
    private const float ScoopM3 = 0.22f, ScoopEveryS = 0.4f;

    /// <summary>Earth on the shovel, in m³.</summary>
    public float EarthCarried { get; private set; }

    /// <summary>What the shovel just did, for the HUD.</summary>
    public string ShovelNote { get; private set; } = "";

    private float _scoopWait;
    private float _swing;
    private Node3D _shovelModel;

    /// <summary>Where the shovel would bite: the ground you're looking at, within reach.</summary>
    private bool ShovelPoint(out Vector3 at)
    {
        at = default;
        if (WorldQuery.Ground == null || Terrain.Earth == null) return false;
        Vector3 from = Camera.GlobalPosition, dir = -Camera.GlobalTransform.Basis.Z;
        if (!WorldQuery.Ground.Raycast(from.N(), (from + dir * ShovelReachM).N(), out float dist, out _)) return false;
        at = from + dir * dist;
        return true;
    }

    private void ShovelTick(float dt, bool dig, bool pile)
    {
        _scoopWait -= dt;
        _swing = Mathf.MoveToward(_swing, 0, dt * 3f);
        if (_shovelModel != null) _shovelModel.Position = new Vector3(0.22f, -0.3f - _swing * 0.12f, -0.42f - _swing * 0.08f);
        if (!dig && !pile)
        {
            ShovelNote = "";
            return;
        }

        if (_scoopWait > 0) return;
        _scoopWait = ScoopEveryS;
        if (!ShovelPoint(out Vector3 at))
        {
            ShovelNote = "Too far: get closer to the ground you want to dig";
            return;
        }

        var p = new System.Numerics.Vector2(at.X, at.Z);
        Node3D world = GetParent<Node3D>();
        if (dig)
        {
            float room = CarryM3 - EarthCarried;
            float got = Terrain.Earth.Dig(p, 0.95f, 0.3f, ScoopM3);
            if (got <= 0.001f)
            {
                ShovelNote = "Bedrock, or ground that's built on: nothing to dig";
                return;
            }

            float keep = Mathf.Min(got, room);
            EarthCarried += keep;
            // With the shovel full, the earth goes ahead of the hole, building a parapet.
            if (got - keep > 0.001f)
            {
                Vector3 ahead = at + Flat(at - GlobalPosition).Normalized() * 1.8f;
                Terrain.Earth.Pile(new System.Numerics.Vector2(ahead.X, ahead.Z), 1.3f, got - keep);
            }

            ShovelNote = EarthCarried >= CarryM3 - 0.01f ? "Shovel full: earth goes on the parapet ahead" : "Digging";
        }
        else
        {
            if (EarthCarried <= 0.001f)
            {
                ShovelNote = "Nothing on the shovel: dig some earth first (left click)";
                return;
            }

            float placed = Terrain.Earth.Pile(p, 1.1f, Mathf.Min(ScoopM3, EarthCarried));
            if (placed <= 0.001f)
            {
                ShovelNote = "Can't pile earth there";
                return;
            }

            EarthCarried -= placed;
            ShovelNote = "Piling";
        }

        _swing = 1f;
        Effects.Dirt(world, at, Vector3.Up, 40f);
        Sfx.At(world, at, SoundId.Dirt, -8f, (float)GD.RandRange(0.85, 1.1), 6f, delayByDistance: false);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.Y = 0;
        return v.LengthSquared() > 1e-4f ? v : Vector3.Forward;
    }
}
