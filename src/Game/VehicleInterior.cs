using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The working parts inside a vehicle or building: doors that slide open for anyone walking up and shut
/// behind them, and lamps that light the spaces near the camera (only so many in the whole world at once,
/// because every light costs the renderer).
/// </summary>
public sealed class InteriorParts
{
    private sealed class DoorRt
    {
        public BlockInstance Block;
        public Node3D Panel;
        public CollisionShape3D Shut;
        public float Open, Width;
        public bool Want;
    }

    private readonly IGridHost _host;
    private readonly List<DoorRt> _doors = new();
    private readonly List<BlockInstance> _lamps = new();
    private readonly List<BlockInstance> _services = new();

    /// <summary>The working interior items aboard: bunks, first aid, lockers, workbenches, supply crates.</summary>
    public IReadOnlyList<BlockInstance> Services
    {
        get
        {
            if (_stamp != _host.Model.Blocks.Count) Refresh();
            return _services;
        }
    }
    private readonly Dictionary<BlockInstance, OmniLight3D> _lampLights = new();
    private int _stamp = -1;
    private float _doorClock, _lampClock;

    /// <summary>Lamps lit across the whole world right now.</summary>
    private static int _lampsLit;

    private const int MaxLamps = 18;
    private const float LampRange = 45f;

    public InteriorParts(IGridHost host) => _host = host;

    /// <summary>Finds the doors and lamps (again, after blocks come and go).</summary>
    private void Refresh()
    {
        _stamp = _host.Model.Blocks.Count;
        foreach (DoorRt d in _doors)
            if (GodotObject.IsInstanceValid(d.Shut)) d.Shut.QueueFree();
        _doors.Clear();
        _lamps.Clear();
        _services.Clear();
        foreach (BlockInstance b in _host.Model.Instances)
        {
            if (!b.Alive) continue;
            if (b.Def.Kind == BlockKind.Lamp) _lamps.Add(b);
            if (b.Def.Kind is BlockKind.Bunk or BlockKind.FirstAid or BlockKind.Locker or BlockKind.Workbench or BlockKind.SupplyCrate) _services.Add(b);
            if (b.Def.Kind != BlockKind.Door) continue;
            var size = new Vector3(b.Def.Size.X, b.Def.Size.Y, b.Def.Size.Z) * Conv.CS;
            var shut = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(size.X - 0.2f, size.Y - 0.14f, 0.14f) } };
            _host.Body.AddChild(shut);
            shut.Transform = _host.View.SectionToBody(b.Section) * new Transform3D(Conv.BlockTransform(b.Cell, b.Orientation).Basis, b.Center.G() * Conv.CS);
            _doors.Add(new DoorRt { Block = b, Panel = _host.View.VisualOf(b)?.GetNodeOrNull<Node3D>("Panel"), Shut = shut, Width = size.X - 0.2f });
        }

        foreach (BlockInstance b in new List<BlockInstance>(_lampLights.Keys))
            if (!b.Alive || !_lamps.Contains(b)) Douse(b);
    }

    public void Tick(float dt)
    {
        if (_stamp != _host.Model.Blocks.Count) Refresh();
        if (_doors.Count == 0 && _lamps.Count == 0 && _lampLights.Count == 0) return;

        // Doors: anyone within a couple of meters opens them.
        _doorClock += dt;
        bool check = _doorClock >= 0.1f;
        if (check) _doorClock = 0;
        foreach (DoorRt d in _doors)
        {
            if (!d.Block.Alive) continue;
            if (check)
            {
                Vector3 at = _host.View.BlockWorldPos(d.Block);
                d.Want = false;
                foreach (ICharacter c in Hittables.Characters)
                    if (c.Alive && c.AimCenter.DistanceSquaredTo(at) < 2.2f * 2.2f)
                    {
                        d.Want = true;
                        break;
                    }
            }

            d.Open = Mathf.MoveToward(d.Open, d.Want ? 1f : 0f, dt * 2.5f);
            d.Shut.Disabled = d.Open > 0.6f;
            float k = d.Open * d.Open * (3f - 2f * d.Open);
            if (d.Panel != null && GodotObject.IsInstanceValid(d.Panel)) d.Panel.Position = new Vector3(d.Width * 0.95f * k, 0, 0);
        }

        // Lamps: the ones nearest the camera get a light, up to a budget shared by everything.
        _lampClock -= dt;
        if (_lampClock > 0) return;
        _lampClock = 0.5f + GD.Randf() * 0.1f;
        foreach (BlockInstance b in _lamps)
        {
            bool near = b.Alive && _host.View.BlockWorldPos(b).DistanceSquaredTo(WorldQuery.Viewer) < LampRange * LampRange;
            bool lit = _lampLights.ContainsKey(b);
            if (near && !lit && _lampsLit < MaxLamps) Light(b);
            else if (!near && lit) Douse(b);
        }
    }

    private void Light(BlockInstance b)
    {
        var light = new OmniLight3D
        {
            LightColor = new Color(1f, 0.86f, 0.66f), LightEnergy = 1.6f, OmniRange = 6.5f, OmniAttenuation = 1.3f, ShadowEnabled = false,
        };
        _host.View.SectionNode(b.Section).AddChild(light);
        light.Position = b.Center.G() * Conv.CS;
        _lampLights[b] = light;
        _lampsLit++;
    }

    private void Douse(BlockInstance b)
    {
        if (!_lampLights.Remove(b, out OmniLight3D light)) return;
        _lampsLit--;
        if (GodotObject.IsInstanceValid(light)) light.QueueFree();
    }

    /// <summary>Turns every lamp off (when the vehicle or building goes away).</summary>
    public void Clear()
    {
        foreach (BlockInstance b in new List<BlockInstance>(_lampLights.Keys)) Douse(b);
    }
}

public partial class VehicleBody
{
    private InteriorParts _interior;

    private void InteriorTick(float dt) => (_interior ??= new InteriorParts(this)).Tick(dt);

    /// <summary>Bunks, first aid, lockers, workbenches and supply crates aboard.</summary>
    public IReadOnlyList<BlockInstance> Services => (_interior ??= new InteriorParts(this)).Services;

    public override void _ExitTree()
    {
        _interior?.Clear();
        GridRegistry.Remove(this);
    }
}

public partial class StructureBody
{
    private InteriorParts _interior;

    public override void _PhysicsProcess(double delta) => (_interior ??= new InteriorParts(this)).Tick((float)delta);

    public IReadOnlyList<BlockInstance> Services => (_interior ??= new InteriorParts(this)).Services;
}
