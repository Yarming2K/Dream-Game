using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace DreamGame.Game;

/// <summary>
/// Fleet command's tactical map (M, from a vehicle or post with a command console). A top-down map of
/// the island with your units on it, and enemies only where one of your units can see them (fog of war;
/// those it lost sight of show faded where they were last seen).
/// <list type="bullet">
/// <item>Left click picks a unit, drag picks a box of them (Shift adds). Ctrl+1-9 saves a group, 1-9 picks it.</item>
/// <item>Right click gives the obvious order: on an enemy, attack it; on one of yours, escort it; on open
/// ground or sea, move there in formation. Shift+right click patrols. H holds.</item>
/// <item>The bar along the bottom sets the formation and the rules of engagement, and J jumps into a unit.</item>
/// <item>Deploy (on the right) calls in more units, yours or the enemy's, from the examples and your own designs.</item>
/// </list>
/// Orders only reach units within the console's range (4 km). The game keeps running while the map is open.
/// <para>So you can see what's going on: every unit you command has a callsign and a status icon (moving,
/// in position, closing in, firing, escorting, patrolling, stuck, knocked out), rings when it's firing or
/// being fired on, and a line with an arrowhead and a marker to where it's going. An order shows a pulse
/// where you gave it. The unit log (bottom left) lists what your units report: in position, targets
/// destroyed, under fire, damaged, knocked out, stuck.</para>
/// </summary>
public partial class TacticalMap : Control
{
    /// <summary>The block host with the console you're commanding from (a vehicle or the command post).</summary>
    public Func<IGridHost> ConsoleHost;

    public Func<VehicleBody> PlayerVehicle;
    public Action<VehicleBody> JumpInto;

    /// <summary>Calls in a unit: design, side (0 yours, 1 enemy), where. Returns why it can't, or null.</summary>
    public Func<Blueprint, int, Vector3, string> Deploy;

    /// <summary>Shows a message on the main HUD.</summary>
    public Action<string> Say;

    /// <summary>Called when the map closes itself (the console was lost), so the game can take the mouse back.</summary>
    public Action Closed;

    private static readonly Color Friendly = new(0.35f, 0.62f, 1f), Enemy = new(1f, 0.36f, 0.3f), Mine = new(1f, 0.8f, 0.3f);
    private static readonly Color Sea = new(0.07f, 0.15f, 0.24f);

    private Vector2 _center = new(60, 0);
    private float _zoom = 1.2f;
    private readonly HashSet<VehicleBody> _selected = new();
    private readonly Dictionary<int, List<VehicleBody>> _groups = new();
    private Formation _formation = Formation.Line;
    private bool _dragging, _panning;
    private Vector2 _dragStart, _dragNow;
    private ImageTexture _terrain;
    private int _terrainVersion = -1;
    private float _redraw, _sense;
    private readonly Dictionary<VehicleBody, (Vector3 pos, double seen, bool now)> _spotted = new();
    private Blueprint _placing;
    private int _placingSide;
    private Label _title, _info;
    private RichTextLabel _list;
    private readonly List<Button> _formationButtons = new(), _roeButtons = new();
    private VBoxContainer _deployList;
    private Button _sideButton;
    private readonly List<(string name, Func<Blueprint> make)> _designs = new();
    private RichTextLabel _log;
    private int _logShown = -1;
    private double _logChanged;

    /// <summary>Where orders were just given, for a moment: a pulse on the map.</summary>
    private readonly List<(Vector3 at, double t, Color c)> _pings = new();

    private static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        BuildUi();
    }

    // ------------------------------------------------------------------ opening and closing

    public void Open()
    {
        // The ground may have changed (craters, digging) since the map was last drawn.
        int version = Terrain.Earth?.Version ?? 0;
        if (_terrain == null || version != _terrainVersion)
        {
            _terrain = BuildTerrain();
            _terrainVersion = version;
        }
        Visible = true;
        IGridHost console = ConsoleHost?.Invoke();
        if (console?.Body != null && IsInstanceValid(console.Body))
        {
            Vector3 p = console.Body.GlobalPosition;
            _center = new Vector2(p.X, p.Z);
        }

        _selected.RemoveWhere(v => !Alive(v));
        Sense();
        RefreshDeploy();
        QueueRedraw();
    }

    public void Close()
    {
        Visible = false;
        _placing = null;
        _dragging = _panning = false;
    }

    private static bool Alive(VehicleBody v) => v != null && IsInstanceValid(v) && v.IsInsideTree() && v.Model.Blocks.Count > 0;

    private IEnumerable<VehicleBody> Vehicles() =>
        GridRegistry.Hosts.OfType<VehicleBody>().Where(Alive);

    private Vector3 ConsolePos()
    {
        IGridHost h = ConsoleHost?.Invoke();
        return h?.Body != null && IsInstanceValid(h.Body) ? h.Body.GlobalPosition : Vector3.Zero;
    }

    /// <summary>How far the console's orders reach: further with antennas aboard.</summary>
    private float CommandRange()
    {
        IGridHost h = ConsoleHost?.Invoke();
        if (h == null || h.Model == null) return Sensors.CommandRangeM;
        int antennas = h.Model.Instances.Count(b => b.Alive && b.Def.Id == "antenna");
        return Fittings.CommandRange(antennas);
    }

    /// <summary>One of your AI units that the console's orders reach.</summary>
    private bool InRange(VehicleBody v) => v.GlobalPosition.DistanceTo(ConsolePos()) <= CommandRange();

    private bool Commandable(VehicleBody v) => Alive(v) && v.Faction == 0 && v != PlayerVehicle?.Invoke();

    // ------------------------------------------------------------------ fog of war

    /// <summary>Which enemies any of your units (or you) can see right now; the rest keep their last-seen place.</summary>
    private void Sense()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        List<VehicleBody> all = Vehicles().ToList();
        List<VehicleBody> eyes = all.Where(v => v.Faction == 0).ToList();
        foreach (VehicleBody e in all)
        {
            if (e.Faction == 0) continue;
            Vector3 ep = e.GlobalPosition;
            bool seen = false;
            foreach (VehicleBody f in eyes)
            {
                Vector3 fp = f.GlobalPosition;
                if (fp.DistanceTo(ep) > f.SightM) continue;
                // Hills block sight (the sea and the sky don't).
                if (!WorldQuery.GroundRay(null, fp + Vector3.Up * 3f, ep + Vector3.Up * 2f, out _, out _))
                {
                    seen = true;
                    break;
                }
            }

            if (seen) _spotted[e] = (ep, now, true);
            else if (_spotted.TryGetValue(e, out var old)) _spotted[e] = (old.pos, old.seen, false);
        }

        foreach (VehicleBody k in _spotted.Keys.ToList())
            if (!Alive(k) || now - _spotted[k].seen > 60.0) _spotted.Remove(k);
    }

    // ------------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        if (!Visible) return;
        float dt = (float)delta;
        IGridHost console = ConsoleHost?.Invoke();
        if (console == null)
        {
            Close();
            Closed?.Invoke();
            Say?.Invoke("Command console lost! Your units carry on with their last orders.");
            return;
        }

        // Pan with W A S D (faster zoomed out).
        var pan = new Vector2(
            (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
            (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0));
        if (pan != Vector2.Zero) _center += pan.Normalized() * dt * 600f / _zoom;

        _sense -= dt;
        if (_sense <= 0)
        {
            _sense = 0.5f;
            Sense();
            UpdatePanels();
            UpdateLog();
        }

        _redraw -= dt;
        if (_redraw <= 0)
        {
            _redraw = 0.05f;
            QueueRedraw();
        }
    }

    private Vector2 ToScreen(Vector3 w) => Size / 2f + (new Vector2(w.X, w.Z) - _center) * _zoom;
    private Vector2 ToWorld2(Vector2 s) => (s - Size / 2f) / _zoom + _center;

    private Vector3 ToWorld(Vector2 s)
    {
        Vector2 g = ToWorld2(s);
        float h = WorldQuery.Ground?.HeightAt(g.X, g.Y) ?? 0f;
        return new Vector3(g.X, Mathf.Max(h, Ocean.Level), g.Y);
    }

    // ------------------------------------------------------------------ drawing

    private ImageTexture BuildTerrain()
    {
        HeightField f = WorldQuery.Ground;
        if (f == null) return null;
        var img = Image.CreateEmpty(f.Width, f.Depth, false, Image.Format.Rgb8);
        var light = new Vector3(-1, 1.6f, -1).Normalized();
        for (int z = 0; z < f.Depth; z++)
        for (int x = 0; x < f.Width; x++)
        {
            float h = f.Heights[z * f.Width + x];
            Color c;
            if (h < Ocean.Level)
            {
                float k = Mathf.Clamp(-h / 26f, 0, 1);
                c = new Color(0.17f, 0.34f, 0.46f).Lerp(Sea, k);
            }
            else
            {
                float hl = f.Heights[z * f.Width + Math.Max(x - 1, 0)], hr = f.Heights[z * f.Width + Math.Min(x + 1, f.Width - 1)];
                float hd = f.Heights[Math.Max(z - 1, 0) * f.Width + x], hu = f.Heights[Math.Min(z + 1, f.Depth - 1) * f.Width + x];
                var n = new Vector3(hl - hr, 2f, hd - hu).Normalized();
                float shade = Mathf.Clamp(0.55f + 0.6f * n.Dot(light), 0.6f, 1.2f);
                // Sand only on the beach, below the flat land; the firing lane is dirt, the hills grass turning to rock.
                float wx = f.Origin.X + x * f.Spacing;
                Color land = Mathf.Abs(wx) < Terrain.LaneHalfWidth ? new Color(0.4f, 0.39f, 0.27f) : new Color(0.3f, 0.38f, 0.22f);
                c = h < Terrain.LandHeight - 0.4f ? new Color(0.62f, 0.57f, 0.42f)
                    : land.Lerp(new Color(0.5f, 0.48f, 0.42f), Mathf.Clamp((h - 6f) / 14f, 0, 1));
                c = new Color(c.R * shade, c.G * shade, c.B * shade);
                // Craters and diggings show as raw earth.
                float stirred = Terrain.Earth?.Disturbed[z * f.Width + x] ?? 0f;
                if (stirred > 0) c = c.Lerp(new Color(0.24f, 0.19f, 0.14f), stirred * 0.7f);
            }

            img.SetPixel(x, z, c);
        }

        return ImageTexture.CreateFromImage(img);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Sea);
        HeightField f = WorldQuery.Ground;
        if (_terrain != null && f != null)
        {
            Vector2 a = ToScreen(new Vector3(f.Origin.X, 0, f.Origin.Y));
            Vector2 b = ToScreen(new Vector3(f.Origin.X + (f.Width - 1) * f.Spacing, 0, f.Origin.Y + (f.Depth - 1) * f.Spacing));
            DrawTextureRect(_terrain, new Rect2(a, b - a), false);
            // The airstrip.
            Vector2 s0 = ToScreen(new Vector3(Terrain.StripX - Terrain.StripHalfWidth, 0, -Terrain.StripHalfLength));
            Vector2 s1 = ToScreen(new Vector3(Terrain.StripX + Terrain.StripHalfWidth, 0, Terrain.StripHalfLength));
            DrawRect(new Rect2(s0, s1 - s0), new Color(0.45f, 0.45f, 0.43f, 0.85f));
        }

        Font font = Ui.BodyFont;
        // A grid every 100 m (every 500 m when zoomed out), labeled.
        float step = _zoom < 0.25f ? 500f : 100f;
        Vector2 w0 = ToWorld2(Vector2.Zero), w1 = ToWorld2(Size);
        var grid = new Color(1, 1, 1, 0.06f);
        for (float x = Mathf.Floor(w0.X / step) * step; x <= w1.X; x += step)
            DrawLine(ToScreen(new Vector3(x, 0, w0.Y)), ToScreen(new Vector3(x, 0, w1.Y)), grid);
        for (float z = Mathf.Floor(w0.Y / step) * step; z <= w1.Y; z += step)
            DrawLine(ToScreen(new Vector3(w0.X, 0, z)), ToScreen(new Vector3(w1.X, 0, z)), grid);

        // Command range round the console.
        Vector3 cp = ConsolePos();
        DrawArc(ToScreen(cp), CommandRange() * _zoom, 0, Mathf.Tau, 128, new Color(Mine, 0.35f), 1.5f, true);
        DrawCircle(ToScreen(cp), 5f, Mine);

        VehicleBody me = PlayerVehicle?.Invoke();
        List<VehicleBody> all = Vehicles().ToList();

        // Sight circles of the units you've picked.
        foreach (VehicleBody v in _selected)
            if (Alive(v)) DrawArc(ToScreen(v.GlobalPosition), v.SightM * _zoom, 0, Mathf.Tau, 64, new Color(Friendly, 0.12f), 1f, true);

        // What each of your units is doing: a line to where it's going, with an arrowhead and a marker there.
        foreach (VehicleBody v in all)
        {
            if (v.Faction != 0 || v.Order == OrderKind.None) continue;
            Vector2 at = ToScreen(v.GlobalPosition);
            bool picked = _selected.Contains(v);
            float alpha = picked ? 0.95f : 0.55f;
            switch (v.Order)
            {
                case OrderKind.Move:
                {
                    Color c = v.Stuck ? new Color(CommandLog.Warn, alpha) : new Color(Friendly, alpha);
                    Vector2 to = ToScreen(v.OrderPoint);
                    DrawDashedLine(at, to, c, picked ? 2f : 1.5f, 6f);
                    Arrowhead(at, to, c);
                    DrawArc(to, 5f, 0, Mathf.Tau, 16, c, 1.5f, true);
                    break;
                }
                case OrderKind.Attack when Alive(v.OrderTarget):
                {
                    Vector2 to = ToScreen(v.OrderTarget.GlobalPosition);
                    var c = new Color(Enemy, alpha);
                    DrawLine(at, to, c, picked ? 2f : 1.5f, true);
                    Arrowhead(at, to, c);
                    Brackets(to, 11f, c);
                    break;
                }
                case OrderKind.Escort when Alive(v.OrderTarget):
                    DrawDashedLine(at, ToScreen(v.OrderTarget.GlobalPosition), new Color(0.6f, 0.9f, 1f, alpha), 1.2f, 3f);
                    break;
                case OrderKind.Patrol:
                {
                    var c = new Color(0.7f, 1f, 0.7f, alpha);
                    Vector2 to = ToScreen(v.OrderPoint);
                    DrawDashedLine(at, to, c, 1.2f, 8f);
                    DrawArc(to, 5f, 0, Mathf.Tau, 16, c, 1.2f, true);
                    break;
                }
            }
        }

        // A pulse where an order was just given.
        _pings.RemoveAll(p => Now - p.t > 1.2);
        foreach ((Vector3 pos, double t, Color c) in _pings)
        {
            float k = (float)((Now - t) / 1.2);
            DrawArc(ToScreen(pos), 6f + 30f * k, 0, Mathf.Tau, 32, new Color(c, 1f - k), 2.5f * (1f - k) + 0.5f, true);
        }

        // Units.
        foreach (VehicleBody v in all)
        {
            if (v.Faction == 0) DrawUnit(v, v.GlobalPosition, v == me ? Mine : InRange(v) ? Friendly : Friendly.Darkened(0.45f), 1f, font);
        }

        foreach ((VehicleBody v, (Vector3 pos, double seen, bool now)) in _spotted)
        {
            if (!Alive(v)) continue;
            DrawUnit(v, now ? v.GlobalPosition : pos, Enemy, now ? 1f : 0.4f, font);
            if (!now) DrawString(font, ToScreen(pos) + new Vector2(8, -8), "?", HorizontalAlignment.Left, -1, 13, new Color(Enemy, 0.7f));
        }

        // The box being dragged out.
        if (_dragging && _dragStart.DistanceTo(_dragNow) > 4f)
        {
            var r = new Rect2(_dragStart, _dragNow - _dragStart).Abs();
            DrawRect(r, new Color(1, 1, 1, 0.08f));
            DrawRect(r, new Color(1, 1, 1, 0.7f), false, 1f);
        }

        // Placing a unit: a ghost marker under the cursor.
        if (_placing != null)
        {
            Vector2 m = GetLocalMousePosition();
            DrawCircle(m, 7f, new Color(_placingSide == 0 ? Friendly : Enemy, 0.6f));
            DrawString(font, m + new Vector2(12, 4), $"{_placing.Name}  (click to place, right click to stop)", HorizontalAlignment.Left, -1, 14, Colors.White);
        }

        // Scale bar.
        float bar = step * _zoom;
        var b0 = new Vector2(24, Size.Y - 96);
        DrawLine(b0, b0 + new Vector2(bar, 0), Colors.White, 2f);
        DrawString(font, b0 + new Vector2(0, -6), step >= 1000 ? $"{step / 1000:0} km" : $"{step:0} m", HorizontalAlignment.Left, -1, 13, Colors.White);
    }

    /// <summary>A unit's icon: a block for land, a hull shape for ships, a chevron for aircraft, facing its heading.</summary>
    private void DrawUnit(VehicleBody v, Vector3 pos, Color c, float alpha, Font font)
    {
        Vector2 at = ToScreen(pos);
        if (at.X < -40 || at.Y < -40 || at.X > Size.X + 40 || at.Y > Size.Y + 40) return;
        Vector3 f3 = -v.GlobalTransform.Basis.Z;
        var fwd = new Vector2(f3.X, f3.Z);
        fwd = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : new Vector2(0, -1);
        var right = new Vector2(-fwd.Y, fwd.X);
        var col = new Color(c, alpha);
        // Drawn at its real size from above (no smaller than an icon you can click), offset to where its blocks are.
        Rect2 plan = v.Plan;
        Vector2 mid = plan.GetCenter();
        Vector2 c0 = at + (right * mid.X - fwd * mid.Y) * _zoom;
        float halfLen = plan.Size.Y * 0.5f * _zoom, halfWid = plan.Size.X * 0.5f * _zoom;
        Vector2[] poly;
        if (v.IsAir)
        {
            float s = Mathf.Max(9f, Mathf.Max(halfLen, halfWid));
            poly = new[] { at + fwd * s, at - fwd * s * 0.6f + right * s * 0.8f, at - fwd * s * 0.2f, at - fwd * s * 0.6f - right * s * 0.8f };
        }
        else if (v.IsNaval)
        {
            float len = Mathf.Max(7f, halfLen), half = Mathf.Max(3f, halfWid);
            if (len < 7.5f) c0 = at;
            poly = new[] { c0 + fwd * len, c0 + fwd * len * 0.6f + right * half, c0 - fwd * len + right * half, c0 - fwd * len - right * half, c0 + fwd * len * 0.6f - right * half };
        }
        else
        {
            float len = Mathf.Max(6f, halfLen), half = Mathf.Max(4.5f, halfWid);
            if (len < 6.5f && half < 5f) c0 = at;
            poly = new[] { c0 + fwd * len + right * half, c0 - fwd * len + right * half, c0 - fwd * len - right * half, c0 + fwd * len - right * half };
        }

        // Rings: orange while it's firing, pulsing red while it's under fire.
        if (v.Faction == 0 && alpha >= 1f)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)Now * 6f);
            if (v.UnderFire) DrawArc(at, 13f + 3f * pulse, 0, Mathf.Tau, 24, new Color(CommandLog.Bad, 0.35f + 0.45f * pulse), 2f, true);
            if (v.FiringNow) DrawArc(at, 10f, 0, Mathf.Tau, 20, new Color(1f, 0.7f, 0.3f, 0.85f), 1.5f, true);
        }

        DrawColoredPolygon(poly, col);
        var outline = new Vector2[poly.Length + 1];
        poly.CopyTo(outline, 0);
        outline[^1] = poly[0];
        bool picked = _selected.Contains(v);
        DrawPolyline(outline, picked ? Colors.White : new Color(0, 0, 0, 0.6f * alpha), picked ? 2f : 1f, true);
        if (picked) DrawArc(at, 16f, 0, Mathf.Tau, 24, Colors.White, 1.5f, true);

        // Health bar, and a name when zoomed in.
        float hp = v.Health;
        DrawRect(new Rect2(at + new Vector2(-10, 12), new Vector2(20, 3)), new Color(0, 0, 0, 0.6f * alpha));
        DrawRect(new Rect2(at + new Vector2(-10, 12), new Vector2(20 * hp, 3)), new Color(Ui.Grade(hp, 0.35f, 0.7f), alpha));
        // What it's doing, as an icon beside it.
        if (v.Faction == 0) StatusIcon(v, at + right * 0f + new Vector2(13, -11), alpha);
        // Names when zoomed in, or for a small pick, so a crowded map stays readable.
        if (_zoom > 1.6f || (picked && _selected.Count <= 6))
            DrawString(font, at + new Vector2(-50, 28), v.Faction == 0 ? v.Callsign : v.ShortName, HorizontalAlignment.Center, 100, 12, new Color(1, 1, 1, 0.85f * alpha));
    }

    // ------------------------------------------------------------------ input

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.WheelUp when mb.Pressed:
                    ZoomAt(mb.Position, 1.15f);
                    break;
                case MouseButton.WheelDown when mb.Pressed:
                    ZoomAt(mb.Position, 1f / 1.15f);
                    break;
                case MouseButton.Middle:
                    _panning = mb.Pressed;
                    break;
                case MouseButton.Left when _placing != null:
                    if (mb.Pressed) PlaceUnit(mb.Position);
                    break;
                case MouseButton.Left:
                    if (mb.Pressed)
                    {
                        _dragging = true;
                        _dragStart = _dragNow = mb.Position;
                    }
                    else if (_dragging)
                    {
                        _dragging = false;
                        Select(mb.Position, mb.ShiftPressed);
                    }

                    break;
                case MouseButton.Right when mb.Pressed:
                    if (_placing != null)
                    {
                        _placing = null;
                        break;
                    }

                    OrderAt(mb.Position, mb.ShiftPressed);
                    break;
            }

            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (_panning) _center -= mm.Relative / _zoom;
            if (_dragging) _dragNow = mm.Position;
            AcceptEvent();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        bool ctrl = k.CtrlPressed || k.MetaPressed;
        if (k.PhysicalKeycode >= Key.Key1 && k.PhysicalKeycode <= Key.Key9)
        {
            int n = (int)(k.PhysicalKeycode - Key.Key1) + 1;
            if (ctrl)
            {
                _groups[n] = _selected.ToList();
                Say?.Invoke($"Group {n}: {_selected.Count} unit(s)");
            }
            else if (_groups.TryGetValue(n, out List<VehicleBody> g))
            {
                _selected.Clear();
                foreach (VehicleBody v in g)
                    if (Alive(v)) _selected.Add(v);
                if (_selected.Count > 0)
                {
                    Vector3 mid = _selected.Aggregate(Vector3.Zero, (a, v) => a + v.GlobalPosition) / _selected.Count;
                    if (k.ShiftPressed) _center = new Vector2(mid.X, mid.Z);
                }
            }

            UpdatePanels();
        }
        else
            switch (k.PhysicalKeycode)
            {
                case Key.H:
                    Hold();
                    break;
                case Key.J:
                    Jump();
                    break;
                case Key.Escape when _placing != null:
                    _placing = null;
                    break;
                case Key.F:
                    SetFormation((Formation)(((int)_formation + 1) % 4));
                    break;
                default:
                    return;
            }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>An arrowhead at the end of a line from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private void Arrowhead(Vector2 from, Vector2 to, Color c)
    {
        Vector2 d = to - from;
        if (d.Length() < 18f) return;
        d = d.Normalized();
        var side = new Vector2(-d.Y, d.X);
        Vector2 tip = to - d * 7f;
        DrawColoredPolygon(new[] { tip, tip - d * 9f + side * 4.5f, tip - d * 9f - side * 4.5f }, c);
    }

    /// <summary>Corner brackets round a target.</summary>
    private void Brackets(Vector2 at, float r, Color c)
    {
        float l = r * 0.55f;
        foreach ((float sx, float sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var corner = at + new Vector2(sx * r, sy * r);
            DrawLine(corner, corner - new Vector2(sx * l, 0), c, 1.5f, true);
            DrawLine(corner, corner - new Vector2(0, sy * l), c, 1.5f, true);
        }
    }

    /// <summary>A small icon for what a unit is doing.</summary>
    private void StatusIcon(VehicleBody v, Vector2 p, float alpha)
    {
        VehicleBody.Doing d = v.Activity;
        Color c = d switch
        {
            VehicleBody.Doing.Stuck => CommandLog.Warn,
            VehicleBody.Doing.Out => new Color(0.6f, 0.6f, 0.6f),
            VehicleBody.Doing.Firing or VehicleBody.Doing.Closing => new Color(1f, 0.7f, 0.35f),
            VehicleBody.Doing.Holding => CommandLog.Good,
            _ => new Color(0.85f, 0.92f, 1f),
        };
        c = new Color(c, alpha);
        DrawCircle(p, 6.5f, new Color(0.05f, 0.07f, 0.09f, 0.8f * alpha));
        switch (d)
        {
            case VehicleBody.Doing.Moving:
                DrawColoredPolygon(new[] { p + new Vector2(-2.5f, -3.5f), p + new Vector2(3.5f, 0), p + new Vector2(-2.5f, 3.5f) }, c);
                break;
            case VehicleBody.Doing.Holding:
                DrawRect(new Rect2(p - new Vector2(2.5f, 2.5f), new Vector2(5, 5)), c);
                break;
            case VehicleBody.Doing.Closing:
                DrawArc(p, 3.5f, 0, Mathf.Tau, 12, c, 1.2f, true);
                DrawLine(p + new Vector2(-5, 0), p + new Vector2(5, 0), c, 1f);
                DrawLine(p + new Vector2(0, -5), p + new Vector2(0, 5), c, 1f);
                break;
            case VehicleBody.Doing.Firing:
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.Pi / 2f + Mathf.Pi / 4f;
                    DrawLine(p, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 4.5f, c, 1.5f);
                }

                break;
            case VehicleBody.Doing.Escorting:
                DrawArc(p, 3.5f, Mathf.Pi, Mathf.Tau, 8, c, 1.5f, true);
                DrawLine(p + new Vector2(-3.5f, 0), p + new Vector2(0, 4), c, 1.5f);
                DrawLine(p + new Vector2(3.5f, 0), p + new Vector2(0, 4), c, 1.5f);
                break;
            case VehicleBody.Doing.Patrolling:
                DrawArc(p, 3.5f, 0.3f, Mathf.Tau - 0.6f, 12, c, 1.5f, true);
                break;
            case VehicleBody.Doing.Stuck:
                DrawLine(p + new Vector2(0, -4), p + new Vector2(0, 1), c, 2f);
                DrawCircle(p + new Vector2(0, 3.2f), 1f, c);
                break;
            case VehicleBody.Doing.Out:
                DrawLine(p + new Vector2(-3, -3), p + new Vector2(3, 3), c, 1.5f);
                DrawLine(p + new Vector2(3, -3), p + new Vector2(-3, 3), c, 1.5f);
                break;
            default:
                DrawCircle(p, 1.5f, c);
                break;
        }
    }

    private void ZoomAt(Vector2 screen, float k)
    {
        Vector2 before = ToWorld2(screen);
        _zoom = Mathf.Clamp(_zoom * k, 0.05f, 12f);
        _center += before - ToWorld2(screen);
    }

    /// <summary>The unit under a screen point (yours, or an enemy you can see), if any.</summary>
    private VehicleBody UnitAt(Vector2 screen, bool friendly)
    {
        VehicleBody best = null;
        float bestD = 14f;
        foreach (VehicleBody v in Vehicles())
        {
            if ((v.Faction == 0) != friendly) continue;
            Vector3 pos = v.GlobalPosition;
            if (!friendly)
            {
                if (!_spotted.TryGetValue(v, out var s)) continue;
                pos = s.now ? v.GlobalPosition : s.pos;
            }

            float d = ToScreen(pos).DistanceTo(screen);
            if (d < bestD)
            {
                bestD = d;
                best = v;
            }
        }

        return best;
    }

    private void Select(Vector2 releasedAt, bool add)
    {
        if (!add) _selected.Clear();
        if (_dragStart.DistanceTo(releasedAt) < 5f)
        {
            VehicleBody v = UnitAt(releasedAt, true);
            if (v != null && Commandable(v))
            {
                if (add && _selected.Contains(v)) _selected.Remove(v);
                else _selected.Add(v);
            }
        }
        else
        {
            var r = new Rect2(_dragStart, releasedAt - _dragStart).Abs();
            foreach (VehicleBody v in Vehicles())
                if (Commandable(v) && r.HasPoint(ToScreen(v.GlobalPosition))) _selected.Add(v);
        }

        UpdatePanels();
    }

    /// <summary>The units picked that the console's orders reach (it says how many are out of range).</summary>
    private List<VehicleBody> Reachable()
    {
        List<VehicleBody> units = _selected.Where(Alive).ToList();
        List<VehicleBody> ok = units.Where(InRange).ToList();
        if (ok.Count < units.Count)
            Say?.Invoke($"{units.Count - ok.Count} unit(s) out of command range (4 km): they carry on with their last orders.");
        return ok;
    }

    /// <summary>A right click: attack an enemy, escort a friend, or move (Shift: patrol) to a spot, in formation.</summary>
    private void OrderAt(Vector2 screen, bool patrol)
    {
        List<VehicleBody> units = Reachable();
        if (units.Count == 0) return;
        VehicleBody enemy = UnitAt(screen, false);
        if (enemy != null)
        {
            foreach (VehicleBody v in units) v.GiveOrder(OrderKind.Attack, enemy.GlobalPosition, enemy);
            _pings.Add((enemy.GlobalPosition, Now, Enemy));
            Say?.Invoke($"{Names(units)}: attack the {enemy.ShortName}");
            UpdatePanels();
            return;
        }

        VehicleBody friend = UnitAt(screen, true);
        if (friend != null && !_selected.Contains(friend))
        {
            foreach (VehicleBody v in units) v.GiveOrder(OrderKind.Escort, friend.GlobalPosition, friend);
            _pings.Add((friend.GlobalPosition, Now, new Color(0.6f, 0.9f, 1f)));
            Say?.Invoke($"{Names(units)}: escort {friend.Callsign}");
            UpdatePanels();
            return;
        }

        Vector3 dest = ToWorld(screen);
        bool water = (WorldQuery.Ground?.HeightAt(dest.X, dest.Z) ?? 0f) < Ocean.Level - 1.5f;
        // Ships only go where there's water, land vehicles only where there's land; aircraft anywhere.
        List<VehicleBody> going = units.Where(v => v.IsAir || (v.IsNaval ? water : !water)).ToList();
        int refused = units.Count - going.Count;
        if (going.Count == 0)
        {
            Say?.Invoke(water ? "Land units can't go out to sea." : "Ships can't go on land.");
            return;
        }

        Vector3 mid = going.Aggregate(Vector3.Zero, (a, v) => a + v.GlobalPosition) / going.Count;
        float spacing = going.Max(v => v.Radius) * 2f + (going.Any(v => v.IsNaval) ? 25f : 8f);
        List<NVec2> slots = Formations.Place(_formation, going.Count, spacing, new NVec2(dest.X, dest.Z), new NVec2(dest.X - mid.X, dest.Z - mid.Z));
        int[] assign = Formations.Assign(going.Select(v => new NVec2(v.GlobalPosition.X, v.GlobalPosition.Z)).ToList(), slots);
        for (int i = 0; i < going.Count; i++)
        {
            NVec2 s = slots[assign[i]];
            float h = WorldQuery.Ground?.HeightAt(s.X, s.Y) ?? 0f;
            going[i].GiveOrder(patrol ? OrderKind.Patrol : OrderKind.Move, new Vector3(s.X, Mathf.Max(h, Ocean.Level), s.Y));
        }

        _pings.Add((dest, Now, patrol ? new Color(0.7f, 1f, 0.7f) : Friendly));
        float far = going.Max(v => v.GlobalPosition.DistanceTo(dest));
        Say?.Invoke($"{Names(going)}: {(patrol ? "patrol to" : "move to")} there in {Formations.NameOf(_formation).ToLowerInvariant()}, " +
                    $"{(far >= 1000 ? $"{far / 1000:0.0} km" : $"{far:0} m")} away" + (refused > 0 ? $" ({refused} can't go there)" : ""));
        UpdatePanels();
    }

    /// <summary>"Warden 1", "Warden 1 and Bulwark 2", "Warden 1, Warden 2 and 4 more".</summary>
    private static string Names(List<VehicleBody> units) => units.Count switch
    {
        1 => units[0].Callsign,
        2 => $"{units[0].Callsign} and {units[1].Callsign}",
        3 => $"{units[0].Callsign}, {units[1].Callsign} and {units[2].Callsign}",
        _ => $"{units[0].Callsign}, {units[1].Callsign} and {units.Count - 2} more",
    };

    private void Hold()
    {
        List<VehicleBody> units = Reachable();
        foreach (VehicleBody v in units)
        {
            v.GiveOrder(OrderKind.Hold, v.GlobalPosition);
            _pings.Add((v.GlobalPosition, Now, CommandLog.Good));
        }

        if (units.Count > 0) Say?.Invoke($"{Names(units)}: hold position");
        UpdatePanels();
    }

    private void SetRoe(Engagement roe)
    {
        List<VehicleBody> units = Reachable();
        foreach (VehicleBody v in units) v.Roe = roe;
        if (units.Count > 0)
            Say?.Invoke($"{Names(units)}: " + roe switch
            {
                Engagement.FireAtWill => "fire at will",
                Engagement.ReturnFire => "return fire only",
                _ => "hold fire",
            });
        UpdatePanels();
    }

    private void SetFormation(Formation f)
    {
        _formation = f;
        for (int i = 0; i < _formationButtons.Count; i++) _formationButtons[i].SetPressedNoSignal(i == (int)f);
    }

    private void Jump()
    {
        VehicleBody v = _selected.FirstOrDefault(Alive);
        if (v == null)
        {
            Say?.Invoke("Pick a unit first, then J jumps into it.");
            return;
        }

        Close();
        JumpInto?.Invoke(v);
    }

    private void PlaceUnit(Vector2 screen)
    {
        if (_placing == null || Deploy == null) return;
        Vector3 at = ToWorld(screen);
        string why = Deploy(_placing, _placingSide, at);
        Say?.Invoke(why ?? $"{(_placingSide == 0 ? "Your" : "Enemy")} {_placing.Name.Split(" (")[0]} called in");
    }

    // ------------------------------------------------------------------ panels

    private void BuildUi()
    {
        _title = Ui.Label("TACTICAL MAP", 20, Ui.Accent, "Title");
        AddChild(_title);
        _title.Position = new Vector2(24, 16);
        Label hint = Ui.Small("Left click / drag: pick units   ·   Right click: attack, escort or move   ·   Shift+right click: patrol   ·   H hold   ·   Ctrl+1-9 save a group, 1-9 pick it   ·   F formation   ·   J jump in   ·   Wheel zoom, W A S D pan   ·   M close");
        AddChild(hint);
        hint.Position = new Vector2(24, 48);
        hint.MouseFilter = MouseFilterEnum.Ignore;

        // What's picked, on the left.
        var left = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        AddChild(left);
        left.Position = new Vector2(16, 80);
        left.CustomMinimumSize = new Vector2(300, 0);
        VBoxContainer lc = Ui.Column(6);
        left.AddChild(lc);
        _info = Ui.Heading("No units picked");
        lc.AddChild(_info);
        _list = Ui.Rich();
        _list.CustomMinimumSize = new Vector2(280, 0);
        lc.AddChild(_list);

        // What your units report, bottom left.
        var logBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(logBox);
        logBox.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        logBox.GrowVertical = GrowDirection.Begin;
        logBox.OffsetLeft = 16;
        logBox.OffsetBottom = -120;
        VBoxContainer lb = Ui.Column(4);
        logBox.AddChild(lb);
        lb.AddChild(Ui.Caption("Unit reports"));
        _log = Ui.Rich();
        _log.CustomMinimumSize = new Vector2(380, 0);
        _log.MouseFilter = MouseFilterEnum.Ignore;
        lb.AddChild(_log);

        // Orders along the bottom.
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        AddChild(bar);
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        bar.GrowHorizontal = GrowDirection.Both;
        bar.GrowVertical = GrowDirection.Begin;
        bar.OffsetBottom = -14;
        HBoxContainer row = Ui.Row(6);
        bar.AddChild(row);
        row.AddChild(Ui.Caption("Formation"));
        foreach (Formation f in Enum.GetValues<Formation>())
        {
            Formation pick = f;
            Button b = Ui.Button(Formations.NameOf(f), () => SetFormation(pick), "Formation for the next move (F cycles)", "Pill");
            b.ToggleMode = true;
            b.FocusMode = FocusModeEnum.None;
            _formationButtons.Add(b);
            row.AddChild(b);
        }

        row.AddChild(Ui.Caption("  Fire"));
        foreach ((Engagement roe, string label, string tip) in new[]
                 {
                     (Engagement.FireAtWill, "At will", "Shoot any enemy in reach"),
                     (Engagement.ReturnFire, "Return", "Only shoot back at what shoots at them, or what they were told to attack"),
                     (Engagement.HoldFire, "Hold", "Don't shoot at all"),
                 })
        {
            Engagement pick = roe;
            Button b = Ui.Button(label, () => SetRoe(pick), tip, "Pill");
            b.FocusMode = FocusModeEnum.None;
            _roeButtons.Add(b);
            row.AddChild(b);
        }

        Button hold = Ui.Button("Hold (H)", Hold, "Stop and hold where they are");
        hold.FocusMode = FocusModeEnum.None;
        row.AddChild(hold);
        Button jump = Ui.Button("Jump in (J)", Jump, "Take over the first unit picked", "PrimaryButton");
        jump.FocusMode = FocusModeEnum.None;
        row.AddChild(jump);
        SetFormation(_formation);

        // Deploy, on the right.
        var right = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        AddChild(right);
        right.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        right.GrowHorizontal = GrowDirection.Begin;
        right.OffsetRight = -16;
        right.OffsetTop = 80;
        VBoxContainer rc = Ui.Column(6);
        right.AddChild(rc);
        rc.AddChild(Ui.Heading("Deploy"));
        rc.AddChild(Ui.Wrapped("Call in more units: pick a design, then click the map (land units on land, ships at sea).", 13, Ui.Dim));
        _sideButton = Ui.Button("Side: yours", () =>
        {
            _placingSide = 1 - _placingSide;
            _sideButton.Text = _placingSide == 0 ? "Side: yours" : "Side: enemy";
        }, "Whose side the units you call in fight on");
        _sideButton.FocusMode = FocusModeEnum.None;
        rc.AddChild(_sideButton);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(240, 360), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        rc.AddChild(scroll);
        _deployList = Ui.Column(4);
        _deployList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_deployList);
    }

    /// <summary>The designs that can be called in: the examples, then your own saved designs.</summary>
    private void RefreshDeploy()
    {
        _designs.Clear();
        foreach (Blueprint ex in ExampleDesigns.All.Concat(ExampleDesigns.Buildings))
        {
            Blueprint copy = ex;
            _designs.Add((ex.Name, () => copy));
        }

        const string dir = "user://blueprints";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        foreach (string file in DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(".json")).OrderBy(f => f))
        {
            string path = $"{dir}/{file}";
            _designs.Add((file[..^5], () =>
            {
                try
                {
                    return Blueprint.FromJson(FileAccess.GetFileAsString(path));
                }
                catch (FormatException)
                {
                    return null;
                }
            }));
        }

        foreach (Node n in _deployList.GetChildren()) n.QueueFree();
        foreach ((string name, Func<Blueprint> make) in _designs)
        {
            Func<Blueprint> mk = make;
            Button b = Ui.Button(name, () =>
            {
                _placing = mk();
                if (_placing == null) Say?.Invoke("That design couldn't be read.");
            }, "Pick, then click the map to place");
            b.FocusMode = FocusModeEnum.None;
            b.Alignment = HorizontalAlignment.Left;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _deployList.AddChild(b);
        }
    }

    /// <summary>The last few unit reports, newest at the bottom, older ones dimmer.</summary>
    private void UpdateLog()
    {
        IReadOnlyList<CommandLog.Entry> all = CommandLog.Entries;
        if (all.Count == 0)
        {
            _log.Text = $"[color={Ui.Hex(Ui.Dim)}]Nothing yet. Your units report here when they arrive, find or lose a target, come under fire or get stuck.[/color]";
            return;
        }

        var lines = new List<string>();
        for (int i = Math.Max(0, all.Count - 8); i < all.Count; i++)
        {
            CommandLog.Entry e = all[i];
            double age = Now - e.Time;
            float fade = age < 30 ? 1f : age < 120 ? 0.7f : 0.5f;
            int m = (int)(age / 60), sec = (int)(age % 60);
            string ago = age < 5 ? "now" : m > 0 ? $"{m}m ago" : $"{sec}s ago";
            // Older reports fade toward grey (the text's colors have no transparency).
            Color c = e.Color.Lerp(new Color(0.5f, 0.53f, 0.55f), 1f - fade);
            lines.Add($"[color={Ui.Hex(c)}]{e.Text}[/color]  [color={Ui.Hex(Ui.Dim)}]{ago}[/color]");
        }

        _log.Text = string.Join("\n", lines);
    }

    private void UpdatePanels()
    {
        List<VehicleBody> units = _selected.Where(Alive).ToList();
        _info.Text = units.Count == 0 ? "No units picked" : units.Count == 1 ? units[0].Callsign : $"{units.Count} units picked";
        if (units.Count == 0)
        {
            List<VehicleBody> mine = Vehicles().Where(Commandable).ToList();
            int seen = _spotted.Count(s => s.Value.now);
            string Count(VehicleBody.Doing d, string what) => mine.Count(v => v.Activity == d) is int n and > 0 ? $"{n} {what}" : null;
            string summary = string.Join(", ", new[]
            {
                Count(VehicleBody.Doing.Moving, "moving"), Count(VehicleBody.Doing.Holding, "in position"), Count(VehicleBody.Doing.Firing, "firing"),
                Count(VehicleBody.Doing.Closing, "closing in"), Count(VehicleBody.Doing.Escorting, "escorting"), Count(VehicleBody.Doing.Patrolling, "patrolling"),
                Count(VehicleBody.Doing.Idle, "waiting for orders"),
            }.Where(x => x != null));
            int stuck = mine.Count(v => v.Activity == VehicleBody.Doing.Stuck), under = mine.Count(v => v.UnderFire);
            _list.Text = $"{mine.Count} of your units{(summary.Length > 0 ? ": " + summary : "")}." +
                         (stuck > 0 ? $"\n[color={Ui.Hex(CommandLog.Warn)}]{stuck} can't get through[/color]" : "") +
                         (under > 0 ? $"\n[color={Ui.Hex(CommandLog.Bad)}]{under} under fire[/color]" : "") +
                         $"\n[color={Ui.Hex(Ui.Dim)}]{seen} enemies in sight. Drag a box round some of yours to pick them.[/color]";
            return;
        }

        string Roe(VehicleBody v) => v.Roe switch { Engagement.FireAtWill => "fire at will", Engagement.ReturnFire => "return fire", _ => "hold fire" };
        var lines = new List<string>();
        foreach (VehicleBody v in units.Take(12))
        {
            string range = InRange(v) ? "" : $"  [color={Ui.Hex(Ui.Warn)}]out of range[/color]";
            Color act = v.Stuck ? CommandLog.Warn : v.KnockedOut ? Ui.Dim : v.UnderFire ? CommandLog.Bad : new Color(0.85f, 0.9f, 0.95f);
            lines.Add($"[b]{v.Callsign}[/b]  [color={Ui.Hex(Ui.Grade(v.Health, 0.35f, 0.7f))}]{v.Health * 100:0}%[/color]  " +
                      $"[color={Ui.Hex(act)}]{v.ActivityText}[/color]  [color={Ui.Hex(Ui.Dim)}]{Roe(v)}[/color]{range}");
        }

        if (units.Count > 12) lines.Add($"[color={Ui.Hex(Ui.Dim)}]and {units.Count - 12} more[/color]");
        _list.Text = string.Join("\n", lines);
    }
}
