using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Godot;
using NVec = System.Numerics.Vector3;

namespace DreamGame.Game;

/// <summary>The Glass overlay's look: colors, fonts, and the frosted smoked-glass material behind every panel.</summary>
public static class GlassStyle
{
    public static readonly Color Cyan = new(0.5f, 0.89f, 1f);
    public static readonly Color CyanDim = new(0.5f, 0.89f, 1f, 0.6f);
    public static readonly Color CyanFaint = new(0.5f, 0.89f, 1f, 0.16f);
    public static readonly Color Amber = new(1f, 0.71f, 0.28f);
    public static readonly Color Red = new(1f, 0.36f, 0.31f);
    public static readonly Color Text = new(0.91f, 0.96f, 0.98f);
    public static readonly Color Off = new(0.37f, 0.42f, 0.45f);

    /// <summary>The glass's own tint: dark blue-grey, and how much of it covers the blurred view behind.</summary>
    public static readonly Color Tint = new(0.03f, 0.055f, 0.08f, 0.5f);

    private static Font _label, _number;
    private static ShaderMaterial _glass;

    /// <summary>Spaced capitals for labels.</summary>
    public static Font Label
    {
        get
        {
            if (_label != null) return _label;
            var v = new FontVariation { BaseFont = Ui.HeadingBoldFont };
            v.SetSpacing(TextServer.SpacingType.Glyph, 2);
            return _label = v;
        }
    }

    /// <summary>Numbers and names.</summary>
    public static Font Number => _number ??= Ui.HeadingBoldFont;

    private const string GlassCode = @"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float blur = 2.6;
uniform vec3 line_color : source_color = vec3(0.5, 0.89, 1.0);

void fragment() {
	// Smoked glass: the view behind, blurred, under the panel's own tint, with faint scan lines.
	vec3 behind = textureLod(screen_tex, SCREEN_UV, blur).rgb;
	vec3 c = mix(behind, COLOR.rgb, COLOR.a);
	float line = step(0.75, fract(FRAGCOORD.y / 4.0)) * 0.035;
	COLOR = vec4(c + line_color * line, 1.0);
}
";

    public static ShaderMaterial GlassMaterial => _glass ??= new ShaderMaterial { Shader = new Shader { Code = GlassCode } };

    /// <summary>A panel's outline: a rectangle with its top-right and bottom-left corners cut off.</summary>
    public static Vector2[] Chamfered(Vector2 size, float cut = 14f) => new[]
    {
        new Vector2(0, 0), new Vector2(size.X - cut, 0), new Vector2(size.X, cut), new Vector2(size.X, size.Y),
        new Vector2(cut, size.Y), new Vector2(0, size.Y - cut),
    };

    public static void Write(CanvasItem ci, Vector2 pos, string s, int size, Color c, Font font = null,
        HorizontalAlignment align = HorizontalAlignment.Left, float width = -1) =>
        ci.DrawString(font ?? Number, pos, s, align, width, size, c);

    public static void Caption(CanvasItem ci, Vector2 pos, string s, Color? c = null, int size = 11,
        HorizontalAlignment align = HorizontalAlignment.Left, float width = -1) =>
        ci.DrawString(Label, pos, s.ToUpperInvariant(), align, width, size, c ?? CyanDim);
}

/// <summary>
/// One panel of smoked glass: the blurred view behind (on its own child, so text isn't blurred), a thin border with
/// cyan brackets on two corners, and whatever <see cref="Painter"/> draws on top.
/// </summary>
public partial class GlassPanel : Control
{
    public Action<Control> Painter;
    private readonly Face _face;

    public GlassPanel(Vector2 size)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = size;
        CustomMinimumSize = size;
        var back = new Back { MouseFilter = MouseFilterEnum.Ignore, Material = GlassStyle.GlassMaterial };
        AddChild(back);
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _face = new Face { MouseFilter = MouseFilterEnum.Ignore, Owner2 = this };
        AddChild(_face);
        _face.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public void Redraw() => _face.QueueRedraw();

    private partial class Back : Control
    {
        public override void _Draw() => DrawColoredPolygon(GlassStyle.Chamfered(Size), GlassStyle.Tint);
    }

    private partial class Face : Control
    {
        public GlassPanel Owner2;

        public override void _Draw()
        {
            Vector2 s = Size;
            Vector2[] edge = GlassStyle.Chamfered(s);
            DrawPolyline(edge.Append(edge[0]).ToArray(), new Color(GlassStyle.Cyan, 0.28f), 1f, true);
            Color c = GlassStyle.Cyan;
            DrawPolyline(new[] { new Vector2(0, 12), Vector2.Zero, new Vector2(12, 0) }, c, 2f);
            DrawPolyline(new[] { new Vector2(s.X - 12, s.Y), s, new Vector2(s.X, s.Y - 12) }, c, 2f);
            Owner2.Painter?.Invoke(this);
        }
    }
}

/// <summary>What the Glass overlay is given each frame besides the vehicle itself.</summary>
public struct GlassFeed
{
    public VehicleBody Vehicle;
    public Camera3D Camera;

    /// <summary>The enemy aircraft a radar missile would chase now, if any.</summary>
    public VehicleBody Lock;

    /// <summary>Warnings worth a glance, most urgent first: text and color.</summary>
    public List<(string text, Color color)> Alerts;
}

/// <summary>
/// The Glass overlay: smoked-glass panels floating beside the vehicle you're driving, joined to it by a thin line,
/// with motion (speed, height, heading), weapons, systems, throttle and fuel, a damage map seen from above and a
/// forward radar arc; a card beside the target you're locked on; and a strip of alerts across the top.
/// </summary>
public partial class HudGlass : Control
{
    private const float ColW = 240f, Gap = 16f;

    private readonly Control _cluster;
    private readonly GlassPanel _motion, _weapons, _systems, _engine, _damage, _radar, _lockCard, _alerts;
    private GlassFeed _f;
    private Vector2 _clusterPos, _anchor, _lockPos;
    private bool _placed, _lockOn, _anchorOn;
    private float _scale = 1f;

    // Readings smoothed or sampled over time.
    private float _burnLps, _lastFuel = -1f, _lockRange = -1f, _closing;
    private float _mapTimer;
    private List<Instruments.DamageColumn> _map = new();
    private int _mapMinX, _mapMaxX, _mapMinZ, _mapMaxZ;
    private float _contactTimer;
    private int _smokeTotal, _smokeReady, _decoyTotal, _decoyReady;
    private readonly List<(Vector2 at, bool air, bool friend, bool locked)> _contacts = new();

    public HudGlass()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _cluster = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_cluster);
        _motion = Panel(0, 0, 206, PaintMotion);
        _weapons = Panel(1, 0, 206, PaintWeapons);
        _systems = Panel(0, 222, 200, PaintSystems);
        _engine = Panel(1, 222, 200, PaintEngine);
        _damage = Panel(0, 438, 152, PaintDamage);
        _radar = Panel(1, 438, 152, PaintRadar);
        _lockCard = new GlassPanel(new Vector2(260, 92)) { Painter = PaintLock, Visible = false };
        AddChild(_lockCard);
        _alerts = new GlassPanel(new Vector2(460, 40)) { Painter = PaintAlerts, Visible = false };
        AddChild(_alerts);
    }

    private GlassPanel Panel(int col, float y, float h, Action<Control> paint)
    {
        var p = new GlassPanel(new Vector2(ColW, h)) { Painter = paint, Position = new Vector2(col * (ColW + Gap), y) };
        _cluster.AddChild(p);
        return p;
    }

    private Vector2 ClusterSize = new(ColW * 2 + Gap, 590);
    private int _layout = -1;

    /// <summary>
    /// Stacks the panels this vehicle needs in two columns, each into whichever column is shorter: no weapons panel on
    /// something unarmed, no systems panel when there's nothing to switch.
    /// </summary>
    private void Layout(bool weapons, bool systems)
    {
        int key = (weapons ? 1 : 0) | (systems ? 2 : 0);
        if (key == _layout) return;
        _layout = key;
        _weapons.Visible = weapons;
        _systems.Visible = systems;
        var cols = new float[2];
        foreach (GlassPanel p in new[] { _motion, _weapons, _systems, _engine, _damage, _radar })
        {
            if (!p.Visible) continue;
            int c = cols[1] < cols[0] ? 1 : 0;
            p.Position = new Vector2(c * (ColW + Gap), cols[c]);
            cols[c] += p.Size.Y + Gap;
        }

        ClusterSize = new Vector2(ColW * 2 + Gap, Mathf.Max(cols[0], cols[1]) - Gap);
    }

    /// <summary>Takes this frame's readings, moves the panels beside the vehicle and redraws them.</summary>
    public void Refresh(in GlassFeed f, float dt)
    {
        _f = f;
        VehicleBody v = f.Vehicle;
        if (v == null || !IsInstanceValid(v) || f.Camera == null) return;
        Layout(v.Guns.Count > 0 || v.Analysis.Missiles.Count > 0 || v.Analysis.BombRacks.Count > 0, Systems(v).Count > 0);
        Vector2 view = GetViewportRect().Size;
        _scale = Mathf.Clamp(view.Y / 1080f, 0.6f, 1.6f) * 0.9f;
        _cluster.Scale = new Vector2(_scale, _scale);

        // Where the vehicle is on screen: the panels hang beside it, on whichever side has more room.
        Vector3 com = v.GlobalTransform * v.CenterOfMass;
        _anchorOn = !f.Camera.IsPositionBehind(com);
        if (_anchorOn) _anchor = f.Camera.UnprojectPosition(com);
        Vector2 size = ClusterSize * _scale;
        bool left = !_anchorOn || _anchor.X > view.X * 0.42f;
        float margin = 150f * _scale;
        var want = new Vector2(left ? _anchor.X - margin - size.X : _anchor.X + margin, _anchor.Y - size.Y * 0.62f);
        want.X = Mathf.Clamp(want.X, 16f, view.X - size.X - 16f);
        want.Y = Mathf.Clamp(want.Y, 70f, view.Y - size.Y - 16f);
        _clusterPos = _placed ? _clusterPos.Lerp(want, Mathf.Min(1f, dt * 6f)) : want;
        _placed = true;
        _cluster.Position = _clusterPos;

        // Fuel burn, sampled, for how long it lasts.
        if (_lastFuel >= 0 && dt > 0)
        {
            float burn = Mathf.Max(0, (_lastFuel - v.FuelL) / dt);
            if (burn < 50f) _burnLps = Mathf.Lerp(_burnLps, burn, Mathf.Min(1f, dt * 0.5f));
        }

        _lastFuel = v.FuelL;

        // The damage map every half second; contacts four times a second.
        _mapTimer -= dt;
        if (_mapTimer <= 0)
        {
            _mapTimer = 0.5f;
            BuildDamageMap(v);
            // (Counted here, not every frame: a big ship has thousands of blocks.)
            _smokeTotal = _smokeReady = _decoyTotal = _decoyReady = 0;
            foreach (BlockInstance b in v.Model.Instances)
            {
                if (b.Def.Id == "smoke_launcher")
                {
                    _smokeTotal++;
                    if (b.Alive && b.AmmoUnits >= 1f) _smokeReady++;
                }
                else if (b.Def.Id == "countermeasures")
                {
                    _decoyTotal++;
                    if (b.Alive && b.AmmoUnits >= 1f) _decoyReady++;
                }
            }
        }

        _contactTimer -= dt;
        if (_contactTimer <= 0)
        {
            _contactTimer = 0.25f;
            FindContacts(v, f.Lock);
        }

        // The lock card beside the target.
        _lockOn = f.Lock != null && IsInstanceValid(f.Lock) && f.Lock.Model.Blocks.Count > 0;
        if (_lockOn)
        {
            Vector3 at = f.Lock.GlobalTransform * f.Lock.CenterOfMass;
            float range = at.DistanceTo(com);
            if (_lockRange > 0 && dt > 0) _closing = Mathf.Lerp(_closing, (_lockRange - range) / dt * 3.6f, Mathf.Min(1f, dt * 2f));
            _lockRange = range;
            bool behind = f.Camera.IsPositionBehind(at);
            _lockPos = behind ? new Vector2(view.X * 0.5f, 120f) : f.Camera.UnprojectPosition(at);
            _lockPos = new Vector2(Mathf.Clamp(_lockPos.X, 60f, view.X - 340f), Mathf.Clamp(_lockPos.Y, 80f, view.Y - 140f));
            _lockCard.Position = _lockPos + new Vector2(52f, -46f);
            _lockCard.Redraw();
        }
        else _lockRange = -1f;

        _lockCard.Visible = _lockOn;

        _alerts.Visible = f.Alerts != null && f.Alerts.Count > 0;
        if (_alerts.Visible)
        {
            int rows = Mathf.Min(f.Alerts.Count, 3);
            _alerts.Size = _alerts.CustomMinimumSize = new Vector2(520, 16 + rows * 24);
            _alerts.Position = new Vector2((view.X - 520f) * 0.5f, 18f);
            _alerts.Redraw();
        }

        foreach (GlassPanel p in new[] { _motion, _weapons, _systems, _engine, _damage, _radar }) p.Redraw();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_f.Vehicle == null) return;
        // The leader line from the panels to the vehicle, ending in a small diamond on it.
        if (_anchorOn)
        {
            Vector2 size = ClusterSize * _scale;
            bool left = _clusterPos.X + size.X * 0.5f < _anchor.X;
            var from = new Vector2(left ? _clusterPos.X + size.X : _clusterPos.X, _clusterPos.Y + size.Y * 0.84f);
            DrawLine(from, _anchor, new Color(GlassStyle.Cyan, 0.65f), 1.2f, true);
            Vector2[] diamond = { _anchor + new Vector2(0, -6), _anchor + new Vector2(6, 0), _anchor + new Vector2(0, 6), _anchor + new Vector2(-6, 0), _anchor + new Vector2(0, -6) };
            DrawPolyline(diamond, GlassStyle.Cyan, 2f, true);
        }

        // Brackets round the locked target.
        if (_lockOn)
        {
            const float b = 30f, l = 12f;
            Color r = GlassStyle.Red;
            foreach ((int sx, int sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var corner = _lockPos + new Vector2(sx * b, sy * b);
                DrawPolyline(new[] { corner - new Vector2(sx * l, 0), corner, corner - new Vector2(0, sy * l) }, r, 2.4f);
            }

            DrawCircle(_lockPos, 3f, r);
        }
    }

    // ------------------------------------------------------------------ the panels

    private static string Fmt(float v) => v.ToString("0");

    private void PaintMotion(Control c)
    {
        VehicleBody v = _f.Vehicle;
        bool air = v.IsAir, naval = v.IsNaval;
        string title = v.IsBuilding ? "POST" : air ? (v.FlightMode == MobilityMode.Plane ? "FLIGHT" : "HOVER") : naval ? (v.IsSubmarine ? "HELM · DIVE" : "HELM") : "DRIVE";
        GlassStyle.Caption(c, new Vector2(16, 24), title);
        float speed = air ? v.AirspeedKmh : Mathf.Abs(v.SpeedKmh);
        Row(c, 62, "SPD", Fmt(speed), "KM/H");
        if (air) Row(c, 100, "ALT", Fmt(v.AltitudeM), "M");
        else if (v.IsSubmarine) Row(c, 100, "DEPTH", Fmt(v.DepthM), "M");
        else if (naval) Row(c, 100, "ORDER", v.TelegraphName, "", 18);
        else Row(c, 100, "POWER", Fmt(v.UsablePowerKw), "KW", 26);
        if (air)
        {
            GlassStyle.Caption(c, new Vector2(16, 124), $"VS {v.ClimbRate:+0;-0}");
            GlassStyle.Caption(c, new Vector2(96, 124), $"AGL {v.HeightAboveGroundM:0}");
        }

        // Heading tape: ten-degree ticks, the heading boxed in the middle, amber caret under it.
        Vector3 fwd = -v.GlobalTransform.Basis.Z;
        float hdg = Instruments.Heading(new NVec(fwd.X, fwd.Y, fwd.Z));
        const float tapeY = 168f, mid = 120f, perDeg = 2.6f;
        c.DrawLine(new Vector2(16, tapeY), new Vector2(224, tapeY), GlassStyle.CyanDim, 1.2f);
        for (int d = (int)(hdg - 40) / 5 * 5; d <= hdg + 40; d += 5)
        {
            float x = mid + (d - hdg) * perDeg;
            if (x < 16 || x > 224) continue;
            bool big = (d % 10 + 10) % 10 == 0;
            c.DrawLine(new Vector2(x, tapeY), new Vector2(x, tapeY - (big ? 8 : 4)), GlassStyle.CyanDim, 1.2f);
            int shown = ((d % 360) + 360) % 360;
            if (shown % 30 == 0 && Mathf.Abs(x - mid) > 26)
                GlassStyle.Write(c, new Vector2(x - 20, tapeY - 12), shown == 0 ? "N" : shown == 90 ? "E" : shown == 180 ? "S" : shown == 270 ? "W" : (shown / 10).ToString("00"),
                    10, GlassStyle.CyanDim, GlassStyle.Label, HorizontalAlignment.Center, 40);
        }

        GlassStyle.Write(c, new Vector2(mid - 30, tapeY - 12), hdg.ToString("000"), 14, GlassStyle.Text, null, HorizontalAlignment.Center, 60);
        c.DrawColoredPolygon(new[] { new Vector2(mid, tapeY + 2), new Vector2(mid - 6, tapeY + 12), new Vector2(mid + 6, tapeY + 12) }, GlassStyle.Amber);
    }

    private static void Row(Control c, float y, string label, string value, string unit, int size = 32)
    {
        GlassStyle.Caption(c, new Vector2(16, y - 6), label, GlassStyle.Text);
        float w = GlassStyle.Number.GetStringSize(unit, HorizontalAlignment.Left, -1, 12).X + (unit.Length > 0 ? 6 : 0);
        GlassStyle.Write(c, new Vector2(60, y), value, size, GlassStyle.Text, null, HorizontalAlignment.Right, 164 - w);
        if (unit.Length > 0) GlassStyle.Caption(c, new Vector2(224 - w + 4, y), unit, GlassStyle.CyanDim, 11);
    }

    private void PaintWeapons(Control c)
    {
        VehicleBody v = _f.Vehicle;
        string ammo = v.SelectedAmmo switch { AmmoType.HE => "HE", AmmoType.Flak => "FLAK", _ => "AP" };
        GlassStyle.Caption(c, new Vector2(16, 24), "WEAPONS");
        if (v.Guns.Count > 0) GlassStyle.Caption(c, new Vector2(140, 24), $"{ammo}  [Q]", GlassStyle.Amber, 11, HorizontalAlignment.Right, 84);
        var rows = new List<(string name, string count, float bar, Color barColor, bool sel)>();
        foreach (IGrouping<string, VehicleBody.GunRt> group in v.Guns.GroupBy(g => g.Label))
        {
            var alive = group.Where(g => g.Alive).ToList();
            if (alive.Count == 0)
            {
                rows.Add((group.Key.ToUpperInvariant(), "OUT", 0, GlassStyle.Red, false));
                continue;
            }

            VehicleBody.GunRt g0 = alive[0];
            bool beam = g0.Info.BreechDef.Breech.Beam;
            float reload = 1f - alive.Min(g => g.ReloadLeft) / Mathf.Max(g0.Stats.ReloadS, 0.01f);
            string count = beam ? (v.HasElectric ? $"{v.Electric.Fraction * 100f:0}%" : "—") : v.Rounds(g0, v.SelectedAmmo).ToString();
            rows.Add(((alive.Count > 1 ? $"{alive.Count}× " : "") + group.Key.ToUpperInvariant(), count,
                beam ? (v.HasElectric ? v.Electric.Fraction : 0) : Mathf.Clamp(reload, 0, 1), GlassStyle.Cyan, rows.Count == 0));
        }

        if (v.Analysis.Missiles.Count > 0)
            rows.Insert(0, (v.IsAir ? "MISSILES" : "MISSILES [X]", v.MissilesReady.ToString(), v.MissilesReady > 0 ? 1f : 0f, GlassStyle.Cyan, v.IsAir));
        if (v.Analysis.BombRacks.Count > 0) rows.Add(("BOMBS [B]", v.BombsLeft.ToString(), v.BombsLeft > 0 ? 1f : 0f, GlassStyle.Cyan, false));
        if (rows.Count == 0)
        {
            GlassStyle.Caption(c, new Vector2(16, 64), "NO WEAPONS", GlassStyle.Off);
            return;
        }

        float y = 40f;
        foreach (var r in rows.Take(3))
        {
            if (r.sel) c.DrawRect(new Rect2(10, y - 4, 220, 44), new Color(GlassStyle.Cyan, 0.08f));
            if (r.sel) c.DrawRect(new Rect2(10, y - 4, 220, 44), GlassStyle.Cyan, false, 1f);
            GlassStyle.Write(c, new Vector2(18, y + 16), r.name, 14, GlassStyle.Text, GlassStyle.Label, HorizontalAlignment.Left, 150);
            GlassStyle.Write(c, new Vector2(150, y + 22), r.count, 20, r.barColor == GlassStyle.Red ? GlassStyle.Red : GlassStyle.Text, null, HorizontalAlignment.Right, 72);
            c.DrawRect(new Rect2(18, y + 26, 120, 5), GlassStyle.CyanFaint);
            c.DrawRect(new Rect2(18, y + 26, 120 * r.bar, 5), r.bar >= 0.999f ? r.barColor : GlassStyle.Amber);
            y += 50f;
        }

        if (rows.Count > 3) GlassStyle.Caption(c, new Vector2(16, 196), $"+{rows.Count - 3} MORE", GlassStyle.CyanDim, 10);
        else if (_f.Lock != null) GlassStyle.Caption(c, new Vector2(16, 196), "LOCKED · IN RANGE", GlassStyle.Amber, 11);
    }

    /// <summary>The systems the vehicle has, each a status light with its key.</summary>
    private List<(string text, Color led, string key)> Systems(VehicleBody v)
    {
        var list = new List<(string, Color, string)>();
        if (v.HasRetractingGear) list.Add((v.GearDown ? "GEAR DOWN" : "GEAR UP", v.GearDown ? GlassStyle.Amber : GlassStyle.Cyan, "AUTO"));
        if (v.HasVectoring) list.Add(("VECTORING", GlassStyle.Amber, "AUTO"));
        if (v.HasBayDoors) list.Add((v.BayOpen ? "BAY OPEN" : "BAY SHUT", v.BayOpen ? GlassStyle.Amber : GlassStyle.Cyan, "X"));
        if (v.HasLights) list.Add((v.LightsOn ? "LIGHTS ON" : "LIGHTS OFF", v.LightsOn ? GlassStyle.Amber : GlassStyle.Off, "H"));
        if (_smokeTotal > 0) list.Add(($"SMOKE {_smokeReady}", _smokeReady > 0 ? GlassStyle.Cyan : GlassStyle.Off, "Z"));
        if (_decoyTotal > 0) list.Add(($"FLARES {_decoyReady}", _decoyReady > 0 ? GlassStyle.Cyan : GlassStyle.Off, "AUTO"));
        if (v.HasBlade) list.Add((v.BladeDown ? "BLADE DOWN" : "BLADE UP", v.BladeDown ? GlassStyle.Amber : GlassStyle.Cyan, "K"));
        if (v.Seats.Any(s => s.Role == SeatRole.Gunner))
            list.Add((v.Roe == Engagement.HoldFire ? "HOLD FIRE" : v.Roe == Engagement.ReturnFire ? "RETURN FIRE" : v.GunnerFollowsPlayer ? "GUNS: MY AIM" : "GUNS: FREE",
                v.Roe == Engagement.HoldFire ? GlassStyle.Red : GlassStyle.Cyan, "O"));
        return list;
    }

    private void PaintSystems(Control c)
    {
        VehicleBody v = _f.Vehicle;
        GlassStyle.Caption(c, new Vector2(16, 24), "SYSTEMS");
        var list = Systems(v);
        if (list.Count == 0) GlassStyle.Caption(c, new Vector2(16, 60), "NOTHING TO SWITCH", GlassStyle.Off);
        float y = 36f;
        foreach ((string text, Color led, string key) in list.Take(4))
        {
            c.DrawRect(new Rect2(16, y, 208, 32), new Color(GlassStyle.Cyan, 0.06f));
            c.DrawRect(new Rect2(16, y, 208, 32), new Color(GlassStyle.Cyan, 0.3f), false, 1f);
            c.DrawRect(new Rect2(26, y + 12, 8, 8), led);
            GlassStyle.Caption(c, new Vector2(42, y + 21), text, led == GlassStyle.Off ? GlassStyle.Off : GlassStyle.Text, 12);
            float kw = GlassStyle.Label.GetStringSize(key, HorizontalAlignment.Left, -1, 10).X + 8;
            c.DrawRect(new Rect2(216 - kw, y + 8, kw, 16), new Color(GlassStyle.Cyan, 0.35f), false, 1f);
            GlassStyle.Caption(c, new Vector2(216 - kw, y + 20), key, GlassStyle.CyanDim, 10, HorizontalAlignment.Center, kw);
            y += 39f;
        }
    }

    private void PaintEngine(Control c)
    {
        VehicleBody v = _f.Vehicle;
        bool plane = v.IsAir && v.FlightMode == MobilityMode.Plane;
        GlassStyle.Caption(c, new Vector2(16, 24), plane ? "THROTTLE · FUEL" : v.IsNaval ? "TELEGRAPH · FUEL" : "POWER · FUEL");
        float first = plane ? v.AirThrottle
            : v.IsNaval ? v.Telegraph / (float)(VehicleBody.TelegraphNames.Length - 1)
            : v.PowerKw > 0 ? v.UsablePowerKw / v.PowerKw : 0f;
        float fuel = v.FuelCapacityL > 0 ? v.FuelL / v.FuelCapacityL : 0f;
        Segments(c, new Vector2(24, 40), first, plane && v.HasJets ? 0.85f : 2f);
        Segments(c, new Vector2(84, 40), fuel, 2f, fuel < 0.15f);
        GlassStyle.Caption(c, new Vector2(20, 190), plane ? "THR" : v.IsNaval ? "TEL" : "PWR", GlassStyle.CyanDim, 10);
        GlassStyle.Caption(c, new Vector2(80, 190), "FUEL", GlassStyle.CyanDim, 10);
        if (plane)
        {
            GlassStyle.Caption(c, new Vector2(140, 56), v.Afterburning ? "AB" : v.EmergencyPower ? "EMRG" : "THR", v.Afterburning || v.EmergencyPower ? GlassStyle.Amber : GlassStyle.CyanDim);
            GlassStyle.Write(c, new Vector2(140, 84), $"{v.AirThrottle * 100f:0}%", 22, GlassStyle.Text);
        }
        else
        {
            GlassStyle.Caption(c, new Vector2(140, 56), v.IsNaval ? "ORDER" : "ENGINE");
            GlassStyle.Write(c, new Vector2(140, 84), v.IsNaval ? $"{first * 100f:0}%" : $"{v.UsablePowerKw:0}", 22, GlassStyle.Text);
        }

        GlassStyle.Caption(c, new Vector2(140, 116), "FUEL");
        GlassStyle.Write(c, new Vector2(140, 142), Fmt(v.FuelL), 22, fuel < 0.15f ? GlassStyle.Red : GlassStyle.Text);
        float minutes = Instruments.FuelMinutes(v.FuelL, _burnLps);
        GlassStyle.Caption(c, new Vector2(140, 162), float.IsInfinity(minutes) || minutes > 999 ? "IDLE" : $"≈ {minutes:0} MIN", GlassStyle.CyanDim, 10);
        if (v.HasElectric)
        {
            GlassStyle.Caption(c, new Vector2(140, 184), $"CHG {v.Electric.Fraction * 100f:0}%", v.Electric.Fraction < 0.2f ? GlassStyle.Amber : GlassStyle.CyanDim, 10);
        }
    }

    /// <summary>A vertical bar of eleven segments, lit up to <paramref name="value"/>, amber from <paramref name="amberFrom"/> up (an afterburner's range).</summary>
    private static void Segments(Control c, Vector2 at, float value, float amberFrom, bool low = false)
    {
        const int n = 11;
        for (int i = 0; i < n; i++)
        {
            float t = (i + 1) / (float)n;
            bool lit = value >= t - 0.5f / n;
            Color col = !lit ? GlassStyle.CyanFaint : low ? GlassStyle.Red : t > amberFrom ? GlassStyle.Amber : GlassStyle.Cyan;
            c.DrawRect(new Rect2(at.X, at.Y + (n - 1 - i) * 12.5f, 44, 9), col);
        }
    }

    private void BuildDamageMap(VehicleBody v)
    {
        var cells = v.Analysis.Cells.Keys.Select(cell => (cell, v.Model.Blocks.TryGetValue(cell, out BlockInstance b) && b.Alive));
        _map = Instruments.DamageMap(cells);
        if (_map.Count == 0) return;
        _mapMinX = _map.Min(m => m.X);
        _mapMaxX = _map.Max(m => m.X);
        _mapMinZ = _map.Min(m => m.Z);
        _mapMaxZ = _map.Max(m => m.Z);
    }

    private void PaintDamage(Control c)
    {
        VehicleBody v = _f.Vehicle;
        GlassStyle.Caption(c, new Vector2(16, 24), "DAMAGE");
        if (_map.Count == 0) return;
        // The vehicle from above, nose up, each column of blocks a square: cyan whole, amber hit, red gone.
        var box = new Rect2(16, 32, 208, 86);
        float w = _mapMaxX - _mapMinX + 1, h = _mapMaxZ - _mapMinZ + 1;
        float s = Mathf.Min(box.Size.X / w, box.Size.Y / h);
        var origin = box.Position + (box.Size - new Vector2(w, h) * s) * 0.5f;
        float pad = s > 3f ? 0.6f : 0f;
        int hit = 0, gone = 0;
        foreach (Instruments.DamageColumn col in _map)
        {
            Color fill = col.Health >= 0.999f ? new Color(GlassStyle.Cyan, 0.32f) : col.Health > 0f ? GlassStyle.Amber : GlassStyle.Red;
            if (col.Health < 0.999f && col.Health > 0f) hit++;
            if (col.Health <= 0f) gone++;
            c.DrawRect(new Rect2(origin + new Vector2(col.X - _mapMinX, col.Z - _mapMinZ) * s, new Vector2(s - pad, s - pad)), fill);
        }

        int crew = v.Seats.Count(s2 => v.SeatAlive(s2) && s2.Occupant != Occupant.Empty), seats = v.Seats.Count;
        string line = $"CREW {crew}/{seats}";
        if (v.FiresBurning > 0) line += $" · FIRE {v.FiresBurning}";
        GlassStyle.Caption(c, new Vector2(16, 140), line, v.FiresBurning > 0 || crew < seats ? GlassStyle.Amber : GlassStyle.CyanDim, 11);
        string state = gone > 0 ? $"{gone} GONE" : hit > 0 ? $"{hit} HIT" : "INTACT";
        GlassStyle.Caption(c, new Vector2(120, 140), state, gone > 0 ? GlassStyle.Red : hit > 0 ? GlassStyle.Amber : GlassStyle.CyanDim, 11, HorizontalAlignment.Right, 104);
    }

    private float RadarRange(VehicleBody v) => Mathf.Clamp(v.SightM, 1000f, 12000f);

    private void FindContacts(VehicleBody v, VehicleBody locked)
    {
        _contacts.Clear();
        Vector3 here = v.GlobalPosition;
        Vector3 fwd = -v.GlobalTransform.Basis.Z;
        float hdg = Instruments.Heading(new NVec(fwd.X, fwd.Y, fwd.Z)), range = RadarRange(v);
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody o || o == v || !IsInstanceValid(o) || o.Model.Blocks.Count == 0) continue;
            bool friend = o.Faction == v.Faction;
            if (!friend && !Spotting.IsSpotted(v.Faction, o)) continue;
            Vector3 p = o.GlobalPosition;
            System.Numerics.Vector2? at = Instruments.RadarPlot(new NVec(here.X, here.Y, here.Z), hdg, new NVec(p.X, p.Y, p.Z), range, 60f);
            if (at == null) continue;
            _contacts.Add((new Vector2(at.Value.X, at.Value.Y), o.IsAir && !o.OnGround, friend, o == locked));
        }
    }

    private void PaintRadar(Control c)
    {
        VehicleBody v = _f.Vehicle;
        float range = RadarRange(v);
        GlassStyle.Caption(c, new Vector2(16, 24), $"{(v.RadarAlive ? "RADAR" : "SIGHT")} · {range / 1000f:0} KM");
        var o = new Vector2(120, 140);
        const float r = 104f, half = Mathf.Pi / 3f;
        Color line = new(GlassStyle.Cyan, 0.35f);
        foreach (float f in new[] { 1f, 0.66f, 0.33f })
            c.DrawArc(o, r * f, -Mathf.Pi / 2 - half, -Mathf.Pi / 2 + half, 24, line, 1f, true);
        c.DrawLine(o, o + new Vector2(Mathf.Sin(-half), -Mathf.Cos(half)) * r, line, 1f, true);
        c.DrawLine(o, o + new Vector2(Mathf.Sin(half), -Mathf.Cos(half)) * r, line, 1f, true);
        c.DrawLine(o, o + new Vector2(0, -r), line, 1f, true);
        float sin = Mathf.Sin(half);
        foreach ((Vector2 at, bool air, bool friend, bool locked) in _contacts)
        {
            Vector2 p = o + new Vector2(at.X * sin, -at.Y) * r;
            Color col = friend ? GlassStyle.Cyan : GlassStyle.Red;
            if (air) c.DrawColoredPolygon(new[] { p + new Vector2(0, -5), p + new Vector2(5, 4), p + new Vector2(-5, 4) }, col);
            else c.DrawRect(new Rect2(p - new Vector2(3.5f, 3.5f), new Vector2(7, 7)), col);
            if (locked)
            {
                c.DrawRect(new Rect2(p - new Vector2(7, 7), new Vector2(14, 14)), GlassStyle.Red, false, 2f);
                GlassStyle.Caption(c, p + new Vector2(10, 4), "LOCK", GlassStyle.Red, 10);
            }
        }
    }

    private void PaintLock(Control c)
    {
        VehicleBody t = _f.Lock;
        if (t == null || !IsInstanceValid(t)) return;
        GlassStyle.Caption(c, new Vector2(14, 22), "LOCK · " + t.NameForYou, GlassStyle.Red, 11);
        GlassStyle.Write(c, new Vector2(14, 56), (_lockRange / 1000f).ToString("0.0"), 24, GlassStyle.Text);
        GlassStyle.Caption(c, new Vector2(64, 56), "KM");
        GlassStyle.Write(c, new Vector2(120, 56), $"{_closing:+0;-0}", 18, GlassStyle.Text, null, HorizontalAlignment.Right, 80);
        GlassStyle.Caption(c, new Vector2(204, 56), "KM/H");
        GlassStyle.Caption(c, new Vector2(14, 80), $"ALT {t.GlobalPosition.Y:0} M", GlassStyle.CyanDim, 10);
    }

    private void PaintAlerts(Control c)
    {
        if (_f.Alerts == null) return;
        float y = 26f;
        foreach ((string text, Color color) in _f.Alerts.Take(3))
        {
            c.DrawRect(new Rect2(14, y - 11, 8, 8), color);
            GlassStyle.Caption(c, new Vector2(30, y), text, color, 12);
            y += 24f;
        }
    }
}
