using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Fleet command on the range: the command post beside the depot, finding the console you're commanding
/// from (your vehicle's, or one you're standing next to), the tactical map (M), jumping into units, and
/// calling units in.
/// </summary>
public partial class TestRange
{
    private static readonly Vector3 CommandPostAt = new(-14, Ground, 132);
    private StructureBody _commandPost;
    private TacticalMap _map;
    private readonly Dictionary<(Blueprint, int), Blueprint> _sideDesigns = new();

    private void SetupCommand(Control root)
    {
        _map = new TacticalMap
        {
            ConsoleHost = ActiveConsole,
            PlayerVehicle = () => Mounted ? _mount : null,
            JumpInto = JumpInto,
            Deploy = DeployUnit,
            Say = msg => AddFeed($"[color={Ui.Hex(Ui.Info)}]{msg}[/color]", true),
            Closed = () =>
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                Big("Command console lost");
            },
        };
        root.AddChild(_map);
    }

    private void AddCommandPost()
    {
        _commandPost = StructureBody.Create(ExampleDesigns.CommandPost(), new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), CommandPostAt), "Command post");
        AddChild(_commandPost);
        _commandPost.AddChild(Sign("COMMAND POST\nstand here, press M", new Vector3(0, 3.6f, 0), 60));
    }

    private static bool HasConsole(GridModel m) => m.Instances.Any(b => b.Alive && b.Def.Kind == BlockKind.CommandConsole);

    /// <summary>
    /// The console you can command from: your vehicle's if it has one, otherwise one you're standing next to
    /// (on a friendly vehicle or the command post). Null if there isn't one.
    /// </summary>
    private IGridHost ActiveConsole()
    {
        if (Mounted) return _mount.ConsoleAlive ? _mount : null;
        if (_me == null || !_me.Alive) return null;
        Vector3 me = _me.GlobalPosition;
        if (_commandPost != null && IsInstanceValid(_commandPost) && _commandPost.GlobalPosition.DistanceTo(me) < 12f && HasConsole(_commandPost.Model))
            return _commandPost;
        foreach (IGridHost h in GridRegistry.Hosts)
            if (h is VehicleBody v && v.Faction == 0 && v.ConsoleAlive && IsInstanceValid(v) && v.GlobalPosition.DistanceTo(me) < v.Radius + 6f)
                return v;
        return null;
    }

    private void ToggleMap()
    {
        if (_map.Visible)
        {
            _map.Close();
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return;
        }

        if (ActiveConsole() == null)
        {
            Big("No command console here");
            AddFeed($"[color={Ui.Hex(Ui.Dim)}]Command from a vehicle with a command console (build one in: Control), or stand at the command post by the depot.[/color]", true);
            return;
        }

        _map.Open();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Takes over one of your units from the map: you move into its driver's seat.</summary>
    private void JumpInto(VehicleBody v)
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        if (v == null || !IsInstanceValid(v)) return;
        if (Mounted) ExitVehicle(silent: true);
        HookVehicle(v);
        EnterVehicle(v, SeatRole.Driver);
    }

    /// <summary>
    /// A design for one side. The enemy wears its own paint (desert on land, dazzle at sea, night in the
    /// air) so you can tell them apart; one copy per design and side, so their meshes are shared.
    /// </summary>
    private Blueprint SideDesign(Blueprint bp, int faction)
    {
        if (_sideDesigns.TryGetValue((bp, faction), out Blueprint d)) return d;
        d = bp;
        if (faction == 1)
        {
            d = bp.Clone();
            bool naval = VehicleAnalysis.Analyze(bp).IsNaval;
            d.Paint = naval ? "dazzle" : bp.Name.Contains("Kestrel") || bp.Name.Contains("Hornet") ? "night" : "desert";
        }

        return _sideDesigns[(bp, faction)] = d;
    }

    /// <summary>Calls a unit in from the map. Returns why it can't go there, or null.</summary>
    private string DeployUnit(Blueprint bp, int faction, Vector3 at)
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
        float h = WorldQuery.Ground?.HeightAt(at.X, at.Z) ?? 0f;
        if (a.IsNaval && h > Ocean.Level - 3f) return "Ships need open water: place it out at sea.";
        if (!a.IsNaval && !a.IsAir && h < Ocean.Level + 0.5f) return "Land vehicles need dry land.";
        if (a.Seats.Count == 0) return "That design has no seats, so nobody can drive it.";
        float yaw = faction == 0 ? 0f : 180f;
        Vector3 pos = a.IsAir ? new Vector3(at.X, Mathf.Max(h, 0f) + (a.Mode == MobilityMode.Plane ? 140f : 40f), at.Z) : at;
        VehicleBody v = SpawnUnit(SideDesign(bp, faction), faction, pos, yaw, a);
        if (a.IsAir && a.Mode == MobilityMode.Plane)
        {
            v.Patrols = true;
            v.PatrolCenter = at;
            v.PatrolRadius = 260f;
            v.PatrolAltitude = pos.Y;
            v.LinearVelocity = -v.Transform.Basis.Z * 62f;
            v.SetAirThrottle(0.75f);
        }
        else if (!a.IsAir) v.GiveOrder(OrderKind.Hold, v.GlobalPosition);

        return null;
    }

    /// <summary>
    /// Puts an AI unit on the field for one side, standing on the ground, floating on its waterline or in
    /// the air, with a colored marker over it (blue yours, red the enemy's).
    /// </summary>
    private VehicleBody SpawnUnit(Blueprint bp, int faction, Vector3 pos, float yawDeg, VehicleAnalysis a = null)
    {
        a ??= VehicleAnalysis.Analyze(bp);
        Vector3 at = pos;
        if (a.IsNaval) at.Y = VehicleBody.FloatHeight(a);
        else if (a.Mode == MobilityMode.Ground) at.Y = (WorldQuery.Ground?.HeightAt(pos.X, pos.Z) ?? Ground) + VehicleBody.SpawnHeight(a);
        var where = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), at);
        VehicleBody v = VehicleBody.Create(bp, where, false, a);
        v.Faction = faction;
        v.AiCrewActive = true;
        AddChild(v);
        v.AddChild(new Label3D
        {
            Text = "◆", FontSize = 40, PixelSize = 0.01f, FixedSize = true, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = faction == 0 ? BlueTeam : RedTeam, OutlineSize = 6, Position = Vector3.Up * (v.Radius * 0.6f + 2f),
            NoDepthTest = false,
        });
        _drill.Add(v);
        return v;
    }
}
