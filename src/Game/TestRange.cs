using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The test range: drive your design, shoot armor walls, a bunker and parked vehicles, and see exactly
/// what each shot did.
/// </summary>
public partial class TestRange : Node3D
{
    public event Action ExitRequested;

    private static readonly Vector3 SpawnPoint = new(0, 0, 120);

    private Blueprint _bp;
    private VehicleBody _player;
    private Combat _combat;
    private readonly List<Node3D> _targets = new();

    private Node3D _pivot;
    private SpringArm3D _arm;
    private Camera3D _cam;
    private float _yaw;
    private float _pitch = -0.28f;
    private float _dist = 13f;
    private bool _zoom;
    private bool _holdFire;
    private float _lastKick;
    private readonly List<(Vector3 point, bool ready)> _impacts = new();

    private RichTextLabel _status;
    private RichTextLabel _guns;
    private RichTextLabel _feed;
    private Label _big;
    private float _bigTime;
    private PanelContainer _help;
    private Label _resume;
    private ReticleOverlay _reticle;
    private readonly List<(string text, float age)> _feedLines = new();

    // Camera feel
    private float _trauma;
    private float _fovKick;
    private float _shakeTime;
    private readonly FastNoiseLite _shakeNoise = new() { Frequency = 2.2f, Seed = 9 };

    private sealed class GunRow
    {
        public VehicleBody.GunRt Gun;
        public Label State;
        public Label Ammo;
        public StatBar Reload;
    }

    private VBoxContainer _gunList;
    private readonly List<GunRow> _gunRows = new();

    public void Setup(Blueprint bp) => _bp = bp;

    public override void _Ready()
    {
        Sfx.Prewarm();
        Scenery.AddSkyAndSun(this, true);
        AddChild(Terrain.Build());
        AddRamps();
        AddMarkers();
        _combat = new Combat { Name = "Combat" };
        AddChild(_combat);
        _combat.Event += OnCombatEvent;
        _combat.Blast += OnBlast;

        _pivot = new Node3D { Name = "CameraPivot" };
        AddChild(_pivot);
        _arm = new SpringArm3D
        {
            SpringLength = _dist, Margin = 0.3f, CollisionMask = GridRegistry.Layers.World | GridRegistry.Layers.Structures,
        };
        _pivot.AddChild(_arm);
        _cam = new Camera3D { Fov = 70f, Far = 3000f };
        _arm.AddChild(_cam);
        _cam.MakeCurrent();

        BuildHud();
        SpawnTargets();
        SpawnPlayer();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // ---------- World ----------

    private void AddRamps()
    {
        var concrete = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.54f, 0.5f), Roughness = 0.9f };
        (float x, float deg)[] ramps = { (-24f, 12f), (-34f, 22f), (-44f, 32f) };
        foreach ((float x, float deg) in ramps)
        {
            float a = Mathf.DegToRad(deg);
            const float length = 14f, width = 7f, thick = 0.8f;
            var size = new Vector3(width, thick, length);
            var body = new StaticBody3D { CollisionLayer = GridRegistry.Layers.World, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = concrete });
            var basePos = new Vector3(x, 0, 104f);
            var center = basePos + new Vector3(0, Mathf.Sin(a) * length / 2f - thick * 0.5f * Mathf.Cos(a), -Mathf.Cos(a) * length / 2f);
            body.Transform = new Transform3D(new Basis(Vector3.Right, a), center);
            AddChild(body);
            AddChild(Sign($"{deg:0}° slope", new Vector3(x, 1.5f, 106f), 64));
        }
    }

    private void AddMarkers()
    {
        for (int d = 50; d <= 400; d += 50)
        {
            AddChild(Sign($"{d} m", new Vector3(-50f, 1.2f, SpawnPoint.Z - d), 96));
            AddChild(Sign($"{d} m", new Vector3(50f, 1.2f, SpawnPoint.Z - d), 96));
        }
    }

    private static Label3D Sign(string text, Vector3 pos, int size) => new()
    {
        Text = text, FontSize = size, PixelSize = 0.012f, Position = pos,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 12,
        Modulate = new Color(1f, 0.95f, 0.8f),
    };

    private void SpawnTargets()
    {
        foreach (Node3D t in _targets)
        {
            if (!IsInstanceValid(t)) continue;
            if (t is IGridHost h) h.Vanish();
            else t.QueueFree();
        }

        _targets.Clear();
        _combat?.Clear();

        float wallZ = SpawnPoint.Z - 60f;
        AddStructure(ExampleDesigns.Wall("armor_light", 7, 5), new Vector3(-24, 0, wallZ), 0, "Light armor\n25 mm");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 7, 5), new Vector3(-8, 0, wallZ), 0, "Heavy armor\n80 mm");
        AddStructure(ExampleDesigns.Wall("armor_composite", 7, 5), new Vector3(8, 0, wallZ), 0, "Composite\n110 mm, weak to HE");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 7, 5), new Vector3(24, 0, wallZ), 45, "Heavy armor\nangled 45°");
        AddStructure(ExampleDesigns.Bunker(), new Vector3(0, 0, SpawnPoint.Z - 160f), 180, "Bunker\n(ammo inside)");
        AddStructure(ExampleDesigns.Wall("armor_heavy", 9, 6), new Vector3(0, 0, SpawnPoint.Z - 400f), 0, "400 m");

        AddDummy(ExampleDesigns.Warden(), new Vector3(-26, 0, SpawnPoint.Z - 120f), 35f);
        AddDummy(ExampleDesigns.Jackal(), new Vector3(26, 0, SpawnPoint.Z - 110f), -60f);
    }

    private void AddStructure(Blueprint bp, Vector3 pos, float yawDeg, string label)
    {
        StructureBody s = StructureBody.Create(bp, new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), pos), label);
        AddChild(s);
        int top = bp.Blocks.Max(b => b.Y);
        Label3D sign = Sign(label, new Vector3(0, (top + 1) * Conv.CS + 1.2f, 0), 72);
        s.AddChild(sign);
        _targets.Add(s);
    }

    private void AddDummy(Blueprint bp, Vector3 pos, float yawDeg)
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
        var where = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), pos + Vector3.Up * VehicleBody.SpawnHeight(a));
        VehicleBody v = VehicleBody.Create(bp, where, false);
        AddChild(v);
        _targets.Add(v);
    }

    private void SpawnPlayer()
    {
        if (_player != null && IsInstanceValid(_player)) _player.Vanish();
        VehicleAnalysis a = VehicleAnalysis.Analyze(_bp);
        var where = new Transform3D(Basis.Identity, SpawnPoint + Vector3.Up * VehicleBody.SpawnHeight(a));
        _player = VehicleBody.Create(_bp, where, true);
        _player.Status += Big;
        _player.Fired += OnFired;
        AddChild(_player);
        BuildGunRows();
        _arm.ClearExcludedObjects();
        _arm.AddExcludedObject(_player.GetRid());
        _pivot.GlobalPosition = where.Origin + Vector3.Up * 2f;
        _yaw = 0;
    }

    private bool PlayerOk => _player != null && IsInstanceValid(_player) && _player.IsInsideTree();

    // ---------- Frame ----------

    public override void _PhysicsProcess(double delta)
    {
        if (!PlayerOk) return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        _player.Throttle = captured ? Input.GetAxis("move_back", "move_forward") : 0f;
        _player.Steer = captured ? Input.GetAxis("steer_left", "steer_right") : 0f;
        _player.Braking = captured && Input.IsActionPressed("brake");

        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
        Vector3 o = _cam.GlobalPosition;
        Vector3 d = -_cam.GlobalTransform.Basis.Z;
        Godot.Collections.Dictionary hit = space.IntersectRay(
            PhysicsRayQueryParameters3D.Create(o, o + d * 2500f, GridRegistry.Layers.All, exclude));
        _player.AimPoint = hit.Count > 0 ? hit["position"].AsVector3() : o + d * 1500f;
        _player.HasAim = true;

        if (!Input.IsActionPressed("fire")) _holdFire = false;
        if (captured && !_holdFire && Input.IsActionPressed("fire")) _player.FireReady();

        PredictImpacts(space, exclude);
    }

    private void PredictImpacts(PhysicsDirectSpaceState3D space, Godot.Collections.Array<Rid> exclude)
    {
        _impacts.Clear();
        foreach (VehicleBody.GunRt g in _player.Guns)
        {
            if (!g.Alive || g.Info.GunSection < 0) continue;
            (Vector3 p, Vector3 dir) = _player.Muzzle(g);
            bool ap = _player.SelectedAmmo == AmmoType.AP;
            Vector3 v = dir * (ap ? g.Stats.MuzzleVelocity : g.Stats.HeVelocity) + _player.LinearVelocity;
            float k = ap ? g.Stats.DragK : g.Stats.HeDragK;
            const float step = 0.04f;
            for (int i = 0; i < 70; i++)
            {
                float speed = v.Length();
                v += Vector3.Down * GunMath.Gravity * step;
                v *= Mathf.Max(0, 1f - k * speed * step);
                Vector3 next = p + v * step;
                Godot.Collections.Dictionary h = space.IntersectRay(
                    PhysicsRayQueryParameters3D.Create(p, next, GridRegistry.Layers.All, exclude));
                if (h.Count > 0)
                {
                    p = h["position"].AsVector3();
                    break;
                }

                p = next;
            }

            _impacts.Add((p, g.ReloadLeft <= 0 && (_player.Rounds(g, AmmoType.AP) + _player.Rounds(g, AmmoType.HE)) > 0));
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (PlayerOk)
        {
            Vector3 target = _player.GlobalTransform * _player.CenterOfMass + Vector3.Up * 2f;
            _pivot.GlobalPosition = _pivot.GlobalPosition.Lerp(target, Mathf.Min(1f, dt * 14f));
        }

        _pivot.Rotation = new Vector3(_pitch, _yaw, 0);
        _arm.SpringLength = _zoom ? Mathf.Min(_dist, 8f) : _dist;

        // Shake comes from "trauma" that fades; squaring it keeps small bumps subtle and big blasts violent.
        _trauma = Mathf.Max(0, _trauma - dt * 1.4f);
        _fovKick = Mathf.Max(0, _fovKick - dt * 22f);
        _shakeTime += dt;
        float shake = _trauma * _trauma * (_zoom ? 0.35f : 1f);
        float n1 = _shakeNoise.GetNoise2D(_shakeTime * 60f, 0), n2 = _shakeNoise.GetNoise2D(0, _shakeTime * 60f);
        float n3 = _shakeNoise.GetNoise2D(_shakeTime * 60f, 100f);
        _cam.HOffset = n1 * 0.35f * shake;
        _cam.VOffset = n2 * 0.35f * shake;
        _cam.Rotation = new Vector3(n2 * 0.03f * shake, n1 * 0.03f * shake, n3 * 0.05f * shake);
        float baseFov = Mathf.Lerp(_cam.Fov - _lastKick, _zoom ? 18f : 70f, Mathf.Min(1f, dt * 10f));
        _lastKick = _fovKick * (_zoom ? 0.3f : 1f);
        _cam.Fov = baseFov + _lastKick;
        UpdateHud(dt);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (e is InputEventMouseMotion mm && captured)
        {
            float sens = _zoom ? 0.0009f : 0.0028f;
            _yaw -= mm.Relative.X * sens;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * sens, -1.35f, 0.55f);
            return;
        }

        if (e is InputEventMouseButton mb)
        {
            if (!captured && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                _holdFire = true;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _dist = Mathf.Clamp(_dist - 1.2f, 5f, 40f);
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _dist = Mathf.Clamp(_dist + 1.2f, 5f, 40f);
            if (mb.ButtonIndex == MouseButton.Right) _zoom = mb.Pressed && captured;
            return;
        }

        if (e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _help.Visible = false;
            return;
        }

        if (e.IsActionPressed("toggle_ammo") && PlayerOk) _player.ToggleAmmo();
        else if (e.IsActionPressed("recover") && PlayerOk) _player.Recover();
        else if (e.IsActionPressed("respawn")) SpawnPlayer();
        else if (e.IsActionPressed("reset_targets"))
        {
            SpawnTargets();
            Big("Targets reset");
        }
        else if (e.IsActionPressed("to_builder")) ExitRequested?.Invoke();
        else if (e.IsActionPressed("help")) _help.Visible = !_help.Visible;
    }

    // ---------- HUD ----------

    private void BuildHud()
    {
        Control root = Ui.Root(this);
        _reticle = new ReticleOverlay { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_reticle);
        _reticle.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var statusPanel = new PanelContainer();
        root.AddChild(statusPanel);
        statusPanel.Position = new Vector2(12, 12);
        _status = Ui.Rich();
        _status.CustomMinimumSize = new Vector2(330, 0);
        statusPanel.AddChild(_status);

        var gunPanel = new PanelContainer();
        root.AddChild(gunPanel);
        gunPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        gunPanel.GrowHorizontal = Control.GrowDirection.Both;
        gunPanel.GrowVertical = Control.GrowDirection.Begin;
        gunPanel.OffsetBottom = -12;
        var gunCol = Ui.Column(6);
        gunPanel.AddChild(gunCol);
        _guns = Ui.Rich();
        _guns.CustomMinimumSize = new Vector2(480, 0);
        gunCol.AddChild(_guns);
        _gunList = Ui.Column(4);
        gunCol.AddChild(_gunList);

        var feedPanel = new PanelContainer();
        root.AddChild(feedPanel);
        feedPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        feedPanel.GrowHorizontal = Control.GrowDirection.Begin;
        feedPanel.OffsetRight = -12;
        feedPanel.OffsetTop = 12;
        _feed = Ui.Rich();
        _feed.CustomMinimumSize = new Vector2(380, 0);
        feedPanel.AddChild(_feed);

        Label hint = Ui.Label("F1 help   ·   Tab back to the builder", 13, Ui.Dim);
        root.AddChild(hint);
        hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        hint.GrowVertical = Control.GrowDirection.Begin;
        hint.OffsetLeft = 14;
        hint.OffsetBottom = -10;

        _big = Ui.Label("", 26, Ui.Accent);
        root.AddChild(_big);
        _big.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _big.GrowHorizontal = Control.GrowDirection.Both;
        _big.HorizontalAlignment = HorizontalAlignment.Center;
        _big.OffsetTop = 90;
        _big.AddThemeConstantOverride("outline_size", 6);
        _big.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));

        _resume = Ui.Label("Mouse released. Click to keep playing.", 20, Ui.Text);
        root.AddChild(_resume);
        _resume.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _resume.GrowHorizontal = Control.GrowDirection.Both;
        _resume.HorizontalAlignment = HorizontalAlignment.Center;
        _resume.OffsetTop = 60;

        _help = new PanelContainer { Visible = false };
        root.AddChild(_help);
        _help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        RichTextLabel text = Ui.Rich();
        text.CustomMinimumSize = new Vector2(560, 0);
        text.Text =
            "[b][color=#ffc74d]Driving[/color][/b]\n" +
            "W / S throttle · A / D steer (tanks can turn on the spot) · Space brake\n" +
            "R flips you back upright · Backspace respawns at the start\n\n" +
            "[b][color=#ffc74d]Shooting[/color][/b]\n" +
            "Aim with the mouse; turrets turn toward the crosshair and guns elevate on their own.\n" +
            "The circles show where each gun will actually hit right now: green when loaded.\n" +
            "Left click fires · Right click zooms · Mouse wheel moves the camera · Q switches AP / HE\n\n" +
            "[b][color=#ffc74d]Reading hits[/color][/b]\n" +
            "[color=#73f27f]PEN[/color] the shell got through: first number is its penetration, second is the armor it beat.\n" +
            "[color=#ff6b61]NO PEN[/color] the armor stopped it. Angled armor counts as thicker.\n" +
            "[color=#ffcc66]RICOCHET[/color] it hit at a steep angle and bounced off.\n" +
            "HE explodes on contact and is best against light armor and composite.\n" +
            "Hit an ammo rack or fuel tank and it may go up.\n\n" +
            "T resets the targets · Tab goes back to the builder · Esc frees the mouse · F1 closes this";
        _help.AddChild(text);
    }

    private void Big(string text)
    {
        _big.Text = text;
        _bigTime = 3f;
    }

    private void UpdateHud(float dt)
    {
        _resume.Visible = Input.MouseMode != Input.MouseModeEnum.Captured && !_help.Visible;
        if (_bigTime > 0)
        {
            _bigTime -= dt;
            _big.Modulate = new Color(1, 1, 1, Mathf.Clamp(_bigTime, 0, 1));
        }

        for (int i = _feedLines.Count - 1; i >= 0; i--)
        {
            _feedLines[i] = (_feedLines[i].text, _feedLines[i].age + dt);
            if (_feedLines[i].age > 8f) _feedLines.RemoveAt(i);
        }

        _feed.Text = _feedLines.Count == 0
            ? $"[color={Ui.Hex(Ui.Dim)}]Hits show up here.[/color]"
            : string.Join("\n", _feedLines.Select(l => l.text));
        _feed.GetParent<Control>().Visible = true;

        _reticle.Marks.Clear();
        if (!PlayerOk)
        {
            _status.Text = "Vehicle destroyed. Backspace to respawn.";
            _guns.Text = "";
            _reticle.QueueRedraw();
            return;
        }

        _reticle.HitFlash = Mathf.Max(0, _reticle.HitFlash - dt * 3f);
        foreach ((Vector3 p, bool ready) in _impacts)
            if (!_cam.IsPositionBehind(p)) _reticle.Marks.Add((_cam.UnprojectPosition(p), ready));
        _reticle.QueueRedraw();

        var sb = new StringBuilder();
        sb.Append($"[b]{_player.DisplayName}[/b]\n");
        sb.Append($"[font_size=22][b]{Mathf.Abs(_player.SpeedKmh):0}[/b][/font_size] km/h   ");
        sb.Append($"Fuel {_player.FuelL:0}/{_player.FuelCapacityL:0} L   Engines {_player.PowerKw:0} kW\n");
        if (!_player.DriverAlive) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Driver knocked out. Backspace to respawn.[/color]\n");
        else if (_player.PowerKw <= 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Engines destroyed.[/color]\n");
        else if (_player.FuelL <= 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]Out of fuel.[/color]\n");
        else if (_player.Wheels.Count == 0) sb.Append($"[color={Ui.Hex(Ui.Bad)}]No wheels left.[/color]\n");
        if (_player.IsUpsideDownOrStuck) sb.Append($"[color={Ui.Hex(Ui.Warn)}]Tipped over: press R to get back up.[/color]\n");
        _status.Text = sb.ToString().TrimEnd('\n');

        var gs = new StringBuilder();
        bool ap = _player.SelectedAmmo == AmmoType.AP;
        gs.Append($"Ammo: {(ap ? "[b][color=#ffc74d][AP][/color][/b]  HE" : "AP  [b][color=#ffc74d][HE][/color][/b]")}   [color={Ui.Hex(Ui.Dim)}](Q switches)[/color]\n");
        if (_player.Guns.Count == 0) gs.Append($"[color={Ui.Hex(Ui.Dim)}]No guns on this design.[/color]");
        _guns.Text = gs.ToString().TrimEnd('\n');

        foreach (GunRow row in _gunRows)
        {
            VehicleBody.GunRt g = row.Gun;
            int apRounds = _player.Rounds(g, AmmoType.AP), heRounds = _player.Rounds(g, AmmoType.HE);
            if (!g.Alive) Paint(row.State, "DESTROYED", Ui.Bad);
            else if (apRounds + heRounds == 0) Paint(row.State, "OUT OF AMMO", Ui.Bad);
            else if (g.ReloadLeft > 0) Paint(row.State, $"RELOADING {g.ReloadLeft:0.0}s", Ui.Warn);
            else Paint(row.State, "READY", Ui.Good);
            float progress = g.Alive ? 1f - g.ReloadLeft / Mathf.Max(g.Stats.ReloadS, 0.01f) : 0f;
            row.Reload.Set(progress, 1f, g.ReloadLeft > 0 ? Ui.Warn : Ui.Good);
            row.Ammo.Text = $"AP {apRounds}   HE {heRounds}   ·   {g.Stats.ApPenMm:0} mm pen";
        }
    }

    private static void Paint(Label l, string text, Color c)
    {
        l.Text = text;
        l.AddThemeColorOverride("font_color", c);
    }

    private void BuildGunRows()
    {
        foreach (Node n in _gunList.GetChildren()) n.QueueFree();
        _gunRows.Clear();
        foreach (VehicleBody.GunRt g in _player.Guns)
        {
            PanelContainer card = Ui.Card();
            VBoxContainer col = Ui.Column(4);
            card.AddChild(col);
            HBoxContainer top = Ui.Row(10);
            col.AddChild(top);
            top.AddChild(Ui.Heading(g.Label));
            var row = new GunRow { Gun = g, State = Ui.Caption("READY", Ui.Good), Ammo = Ui.Small(""), Reload = new StatBar() };
            top.AddChild(row.State);
            top.AddChild(Ui.Spacer());
            top.AddChild(row.Ammo);
            col.AddChild(row.Reload);
            _gunList.AddChild(card);
            _gunRows.Add(row);
        }
    }

    private void OnFired(VehicleBody.GunRt g)
    {
        float c = g.Stats.CaliberMm;
        _trauma = Mathf.Min(1f, _trauma + 0.22f + c / 450f);
        _fovKick += 2f + c / 28f;
    }

    private void OnBlast(Vector3 pos, float radius, float damage)
    {
        float d = _cam.GlobalPosition.DistanceTo(pos);
        float reach = Mathf.Max(radius * 18f, 12f);
        float k = Mathf.Clamp(damage / 450f, 0.15f, 1.2f) * Mathf.Clamp(1f - d / reach, 0, 1);
        _trauma = Mathf.Min(1f, _trauma + k * 0.75f);
    }

    private void OnCombatEvent(CombatEvent e)
    {
        if (!e.ByPlayer) return;
        if (e.Report.Kind == HitKind.Penetration || e.BlocksDestroyed > 0)
        {
            _reticle.HitFlash = 1f;
            Sfx.Play2D(this, SoundId.Tick, -6f, 1.35f);
        }

        HitReport r = e.Report;
        string destroyed = e.BlocksDestroyed > 0 ? $"  · {e.BlocksDestroyed} destroyed" : "";
        (string word, string line, Color c) = r.Kind switch
        {
            HitKind.Penetration => ("PEN", $"PEN  {r.BlockName}  {r.PenMm:0} vs {r.ArmorMm:0} mm{destroyed}", Ui.Good),
            HitKind.NoPenetration => ("NO PEN",
                $"NO PEN  {r.BlockName}  {r.PenMm:0} vs {r.ArmorMm:0} mm{(r.AngleDeg >= 15 ? $" (angled {r.AngleDeg:0}°)" : "")}", Ui.Bad),
            HitKind.Ricochet => ("RICOCHET", $"RICOCHET  {r.AngleDeg:0}° off {r.BlockName}", Ui.Warn),
            _ => ("BOOM", $"BOOM  {r.BlockName}{destroyed}", Ui.Accent),
        };
        _feedLines.Add(($"[color={Ui.Hex(c)}]{line}[/color]", 0f));
        while (_feedLines.Count > 8) _feedLines.RemoveAt(0);
        FloatText(e.Position, word, c);
    }

    private void FloatText(Vector3 pos, string text, Color c)
    {
        var fx = new FxNode { Life = 1.6f };
        AddChild(fx);
        fx.GlobalPosition = pos;
        float dist = _cam.GlobalPosition.DistanceTo(pos);
        var label = new Label3D
        {
            Text = text, FontSize = 40, OutlineSize = 10, Modulate = c, NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0009f * Mathf.Max(dist, 4f),
        };
        fx.AddChild(label);
        fx.Animate = u =>
        {
            label.Position = Vector3.Up * (0.4f + u * 0.06f * dist);
            label.Modulate = new Color(c.R, c.G, c.B, 1f - u * u);
        };
    }
}
