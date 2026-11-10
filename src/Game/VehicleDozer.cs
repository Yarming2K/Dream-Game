using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Dozer blades. Ten times a second, while the vehicle drives forward, each blade cuts the ground under
/// its edge down toward it and heaps the earth just ahead of it, where it's cut again as the blade comes
/// on: so a heap builds up in front and gets pushed along, and it drags on the vehicle the harder the more
/// earth is moving. Lowered (K), the edge is 25 cm below the tracks and every pass goes deeper; raised, it
/// rides 30 cm above them and only shifts heaps. The blade model drops and lifts to match.
/// </summary>
public partial class VehicleBody
{
    /// <summary>The driver has the blade lowered, to dig.</summary>
    public bool BladeDown { get; private set; }

    public bool HasBlade => Analysis.Kit.DozerBlades > 0;

    /// <summary>Earth moved per second just now (m³), for the HUD.</summary>
    public float BladeEarthPerS { get; private set; }

    private float _bladeClock, _bladeDrag, _bladeEdge = Earthworks.BladeRaiseM;
    private float? _contactY;
    private readonly Dictionary<BlockInstance, Vector3> _bladeRest = new();
    private readonly List<BlockInstance> _blades = new();

    public void ToggleBlade()
    {
        if (!HasBlade) return;
        BladeDown = !BladeDown;
        Unpark();
    }

    /// <summary>Where the bottom of the tracks or wheels is, in the vehicle's own frame (at rest).</summary>
    private float ContactY()
    {
        if (_contactY.HasValue) return _contactY.Value;
        float lowest = 0;
        foreach (WheelInfo w in Analysis.Wheels)
            lowest = Mathf.Min(lowest, w.Cell.Y * Conv.CS - w.RadiusM - Mobility.RestLength(w.Kind));
        _contactY = lowest;
        return lowest;
    }

    private void DozeTick(float dt)
    {
        if (!HasBlade || Terrain.Earth == null) return;
        _blades.Clear();
        foreach (BlockInstance b in Model.Instances)
            if (b.Alive && b.Def.Kind == BlockKind.DozerBlade) _blades.Add(b);
        if (_blades.Count == 0) return;

        // The blade drops or lifts to where the driver wants it.
        float want = BladeDown ? -Earthworks.BladeDigM : Earthworks.BladeRaiseM;
        _bladeEdge = Mathf.MoveToward(_bladeEdge, want, dt * 0.7f);
        float edgeY = ContactY() + _bladeEdge;
        foreach (BlockInstance b in _blades)
        {
            if (View.VisualOf(b)?.GetNodeOrNull<Node3D>("Blade") is not { } node) continue;
            if (!_bladeRest.TryGetValue(b, out Vector3 rest)) _bladeRest[b] = rest = node.Position;
            node.Position = rest + Vector3.Up * (edgeY - Bottom(b));
        }

        Vector3 fwd = -GlobalTransform.Basis.Z;
        if (_bladeDrag > 0) ApplyCentralForce(-fwd * _bladeDrag);

        _bladeClock += dt;
        if (_bladeClock < 0.1f) return;
        float step = _bladeClock;
        _bladeClock = 0;
        float speed = LinearVelocity.Dot(fwd);
        if (speed < 0.3f)
        {
            _bladeDrag = 0;
            BladeEarthPerS = 0;
            return;
        }

        Transform3D xf = GlobalTransform;
        var flatFwd = new Vector3(fwd.X, 0, fwd.Z).Normalized();
        var nFwd = new System.Numerics.Vector2(flatFwd.X, flatFwd.Z);
        float moved = 0;
        // Every section cuts first; then, with the whole blade holding its heap in front (so earth can't slump
        // back under it, or between its sections), each heaps what it cut just ahead of itself.
        var heaps = new List<(Vector3 at, float radius, float earth)>();
        var edges = new List<(Vector3 a, Vector3 c)>();
        Vector3 left = default, right = default;
        foreach (BlockInstance b in _blades)
        {
            (int x0, int x1, int z0) = Span(b);
            Vector3 a = xf * new Vector3((x0 - 0.5f) * Conv.CS, edgeY, (z0 - 0.5f) * Conv.CS);
            Vector3 c = xf * new Vector3((x1 + 0.5f) * Conv.CS, edgeY, (z0 - 0.5f) * Conv.CS);
            if (edges.Count == 0 || (a - xf.Origin).Dot(xf.Basis.X) < (left - xf.Origin).Dot(xf.Basis.X)) left = a;
            if (edges.Count == 0 || (c - xf.Origin).Dot(xf.Basis.X) > (right - xf.Origin).Dot(xf.Basis.X)) right = c;
            edges.Add((a, c));
        }

        Terrain.Earth.HoldBehind(new System.Numerics.Vector2(left.X, left.Z), new System.Numerics.Vector2(right.X, right.Z), nFwd, 1.5f);
        foreach ((Vector3 a, Vector3 c) in edges)
        {
            float cut = Terrain.Earth.Cut(new System.Numerics.Vector2(a.X, a.Z), new System.Numerics.Vector2(c.X, c.Z),
                Mathf.Max(0.5f, speed * step * 0.5f + 0.3f), (a.Y + c.Y) * 0.5f, Earthworks.BladeMaxCutM);
            if (cut > 0) heaps.Add(((a + c) * 0.5f + flatFwd * 1.3f, a.DistanceTo(c) * 0.5f + 0.3f, cut));
        }

        if (heaps.Count > 0)
        {
            foreach ((Vector3 at, float radius, float earth) in heaps)
            {
                Terrain.Earth.Pile(new System.Numerics.Vector2(at.X, at.Z), radius, earth);
                moved += earth;
            }

            if (_rng.Randf() < 0.35f) Effects.Dirt(GetParent<Node3D>(), heaps[0].at + Vector3.Up * 0.3f, Vector3.Up, 60f);
        }

        Terrain.Earth.Hold = null;
        BladeEarthPerS = moved / step;
        _bladeDrag = Mathf.Min(BladeEarthPerS * Earthworks.BladeDragNPerM3s, Mass * 4f);
    }

    private static float Bottom(BlockInstance b)
    {
        int y = int.MaxValue;
        foreach (Int3 c in b.Cells) y = System.Math.Min(y, c.Y);
        return (y - 0.5f) * Conv.CS;
    }

    private static (int x0, int x1, int z0) Span(BlockInstance b)
    {
        int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue;
        foreach (Int3 c in b.Cells)
        {
            x0 = System.Math.Min(x0, c.X);
            x1 = System.Math.Max(x1, c.X);
            z0 = System.Math.Min(z0, c.Z);
        }

        return (x0, x1, z0);
    }
}
