using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The battle drill (F8): two whole armies, blue (your side) and red, spawned on the range to fight each
/// other, for seeing how a big battle runs on your PC. Press F8 again for the big drill (about twice as
/// many, with a battleship on each side), and once more to clear it away. Tanks advance until they have
/// a target in range, then stop and shoot; patrol boats close in at sea; planes circle overhead and
/// helicopters hang back. You can drive any blue vehicle (walk up and press E).
/// </summary>
public partial class TestRange
{
    private readonly List<VehicleBody> _drill = new();
    private readonly Queue<(string design, int faction, Vector3 pos, float yaw)> _drillQueue = new();
    private readonly Dictionary<string, Blueprint> _drillDesigns = new();
    private int _drillLevel;
    private PerfOverlay _perf;
    private Label _drillChip;

    private static readonly Color BlueTeam = new(0.35f, 0.62f, 1f);
    private static readonly Color RedTeam = new(1f, 0.36f, 0.3f);

    private Blueprint DrillDesign(string name, int faction)
    {
        string key = $"{name}:{faction}";
        if (_drillDesigns.TryGetValue(key, out Blueprint bp)) return bp;
        // One blueprint per design and side, so every copy shares its meshes. Red wears its own paint, so
        // the two sides are easy to tell apart.
        bp = name switch
        {
            "Warden" => ExampleDesigns.Warden(),
            "Jackal" => ExampleDesigns.Jackal(),
            "Bulwark" => ExampleDesigns.Bulwark(),
            "Skyguard" => ExampleDesigns.Skyguard(),
            "Corsair" => ExampleDesigns.Corsair(),
            "Kestrel" => ExampleDesigns.Kestrel(),
            "Hornet" => ExampleDesigns.Hornet(),
            _ => ExampleDesigns.Colossus(),
        };
        if (faction == 1)
            bp.Paint = name switch
            {
                "Corsair" or "Colossus" => "dazzle",
                "Kestrel" or "Hornet" => "night",
                _ => "desert",
            };
        return _drillDesigns[key] = bp;
    }

    private void SetupDrill(Control root)
    {
        _perf = new PerfOverlay();
        root.AddChild(_perf);
        _perf.Position = new Vector2(14, 14);

        _drillChip = Ui.Label("", 15, Ui.Info, "Heading");
        root.AddChild(_drillChip);
        _drillChip.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _drillChip.GrowHorizontal = Control.GrowDirection.Both;
        _drillChip.HorizontalAlignment = HorizontalAlignment.Center;
        _drillChip.OffsetTop = 36;
        _drillChip.AddThemeConstantOverride("outline_size", 6);
    }

    private void ToggleDrill()
    {
        _drillLevel = (_drillLevel + 1) % 3;
        ClearDrill();
        if (_drillLevel == 0)
        {
            Big("Battle drill over");
            return;
        }

        bool big = _drillLevel == 2;
        if (!_liveFire) ToggleLiveFire();
        // Land: rows of tanks across the firing lane, facing each other a few hundred meters apart.
        string[] land = { "Warden", "Warden", "Jackal", "Warden", "Bulwark", "Jackal", "Warden", "Skyguard" };
        int rows = big ? 5 : 3, perRow = big ? 14 : 12;
        float step = big ? 7f : 8f;
        for (int side = 0; side < 2; side++)
        {
            float zFront = side == 0 ? 150f : -110f, dirZ = side == 0 ? 1f : -1f;
            for (int r = 0; r < rows; r++)
            for (int i = 0; i < perRow; i++)
            {
                float x = (i - (perRow - 1) / 2f) * step + (r % 2) * step * 0.5f;
                float z = zFront + dirZ * r * 12f;
                if (side == 1 && Mathf.Abs(x) < 7f && Mathf.Abs(z + 180f) < 12f) continue;
                _drillQueue.Enqueue((land[(r * 3 + i) % land.Length], side, new Vector3(x, 0, z), side == 0 ? 0f : 180f));
            }

            // At sea: patrol boats east of the island, and in the big drill a battleship further out.
            int boats = big ? 10 : 5;
            for (int i = 0; i < boats; i++)
            {
                Vector3 at = side == 0 ? new Vector3(250f + (i % 5) * 30f, 0, 140f + (i / 5) * 40f) : new Vector3(330f + (i % 5) * 30f, 0, -320f - (i / 5) * 40f);
                _drillQueue.Enqueue(("Corsair", side, at, side == 0 ? 0f : 180f));
            }

            if (big) _drillQueue.Enqueue(("Colossus", side, side == 0 ? new Vector3(620f, 0, 420f) : new Vector3(760f, 0, -620f), side == 0 ? 0f : 180f));

            // In the air: planes circling over the battle, helicopters hanging back over their own lines.
            int planes = big ? 10 : 4, helis = big ? 4 : 2;
            for (int i = 0; i < planes; i++)
                _drillQueue.Enqueue(("Kestrel", side, new Vector3(side == 0 ? -300f : 300f, 140f + i * 12f, i * 60f - planes * 30f), side == 0 ? 90f : -90f));
            for (int i = 0; i < helis; i++)
                _drillQueue.Enqueue(("Hornet", side, new Vector3((i - helis / 2f) * 30f, 45f, side == 0 ? 205f : -170f), side == 0 ? 0f : 180f));
        }

        Big(big ? "BIG BATTLE DRILL: about 200 vehicles (F8 again to clear)" : "BATTLE DRILL: about 100 vehicles (F8 for the big one)");
        AddFeed($"[color={Ui.Hex(Ui.Dim)}]F3 shows the performance readout. Blue is your side: walk up to any blue vehicle and press E to take it.[/color]", true);
    }

    private void ClearDrill()
    {
        _drillQueue.Clear();
        foreach (VehicleBody v in _drill)
            if (IsInstanceValid(v))
            {
                if (v == _mount) ExitVehicle(silent: true);
                v.Vanish();
            }

        _drill.Clear();
    }

    /// <summary>Spawns a few queued drill vehicles each frame (all at once would stall the game for seconds).</summary>
    private void DrillTick()
    {
        for (int n = 0; n < 3 && _drillQueue.Count > 0; n++)
        {
            (string design, int faction, Vector3 pos, float yaw) = _drillQueue.Dequeue();
            Blueprint bp = DrillDesign(design, faction);
            VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
            VehicleBody v = SpawnUnit(bp, faction, pos, yaw, a);
            Transform3D where = v.Transform;
            Vector3 enemy = faction == 0 ? new Vector3(0, 0, -130f) : new Vector3(0, 0, 160f);
            if (a.IsNaval)
            {
                v.AiDriving = true;
                v.MoveGoal = faction == 0 ? new Vector3(330f, 0, -200f) : new Vector3(260f, 0, 60f);
                if (design == "Colossus") v.MoveGoal = faction == 0 ? new Vector3(660f, 0, -100f) : new Vector3(700f, 0, -100f);
                v.StopToShoot = design == "Colossus" ? 2400f : 700f;
                v.GoalRadius = 40f;
            }
            else if (a.IsAir)
            {
                if (design == "Kestrel")
                {
                    v.Patrols = true;
                    v.PatrolCenter = new Vector3(60f, 0, 0);
                    v.PatrolRadius = 330f;
                    v.PatrolAltitude = pos.Y;
                    v.LinearVelocity = -where.Basis.Z * 62f;
                    v.SetAirThrottle(0.75f);
                }
            }
            else
            {
                v.AiDriving = true;
                v.MoveGoal = new Vector3(pos.X, 0, enemy.Z + (faction == 0 ? 40f : -40f));
                v.StopToShoot = 330f;
                v.GoalRadius = 20f;
            }
        }

        if (_drillLevel == 0)
        {
            _drillChip.Text = "";
            return;
        }

        int blue = 0, red = 0;
        foreach (VehicleBody v in _drill)
        {
            if (!IsInstanceValid(v) || !v.IsInsideTree() || v.Model.Blocks.Count == 0 || !v.CanDrive && !v.IsAir && v.Guns.TrueForAll(g => !g.Alive)) continue;
            if (v.Faction == 0) blue++;
            else red++;
        }

        _drillChip.Text = _drillQueue.Count > 0 ? $"BATTLE DRILL  ·  deploying… {_drillQueue.Count} to go" : $"BATTLE DRILL  ·  [blue] {blue}   vs   [red] {red}";
    }
}
