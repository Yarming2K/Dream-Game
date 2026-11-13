using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Godot;
using NVec = System.Numerics.Vector3;

namespace DreamGame.Game;

/// <summary>What the Ring overlay is given each frame besides the vehicle itself.</summary>
public struct RingFeed
{
    public VehicleBody Vehicle;
    public Camera3D Camera;

    /// <summary>The enemy aircraft a radar missile would chase now, if any.</summary>
    public VehicleBody Lock;

    /// <summary>Warnings worth a glance, most urgent first: text and color.</summary>
    public List<(string text, Color color)> Alerts;

    /// <summary>Where it asks which missiles are coming at the vehicle.</summary>
    public Combat Combat;
}

/// <summary>
/// The Ring overlay: nothing boxed in. Speed on the left of a ring round the aim point, height (or engine, telegraph,
/// depth) on the right, heading across the top; throttle and fuel as thin arcs inside it; weapons in one line under
/// it. Contacts sit round its edge where they are, a missile coming at you lights up the ring on its side, and the
/// vehicle's damage map shows beside it for a few seconds after a hit.
/// </summary>
public partial class HudRing : Control
{
    // Speed and height arcs (degrees round the ring, 0 = up, clockwise), and the heading arc across the top.
    private const float SpeedFrom = 230f, SpeedTo = 310f, RightFrom = 50f, RightTo = 130f, HeadHalf = 28f;

    private static readonly Color Shade = new(0f, 0.02f, 0.04f, 0.5f);
    private static readonly Color Ink = new(0.94f, 0.98f, 1f);
    private static readonly Color InkDim = new(0.94f, 0.98f, 1f, 0.55f);

    private RingFeed _f;
    private Vector2 _c;
    private float _s = 1f, _r = 180f, _t;

    // Sampled a few times a second (a big ship has thousands of blocks to count).
    private float _sampleTimer, _mapTimer;
    private int _missiles, _bombs, _smokeTotal, _smokeReady, _decoyTotal, _decoyReady;
    private readonly List<(float bearing, bool air, bool friend, bool locked)> _contacts = new();
    private readonly List<Vector3> _incoming = new();
    private readonly List<float> _threats = new();

    // Damage: the map from above, and how long it stays up after a hit.
    private List<Instruments.DamageColumn> _map = new();
    private int _mapMinX, _mapMaxX, _mapMinZ, _mapMaxZ, _lastAlive = -1, _lastFires;
    private float _hurtShow;

    private float _burnLps, _lastFuel = -1f, _lockRange = -1f, _closing;
    private Vector2 _lockPos;
    private bool _lockOn;

    public HudRing() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>Takes this frame's readings and redraws.</summary>
    public void Refresh(in RingFeed f, float dt)
    {
        _f = f;
        _t += dt;
        VehicleBody v = f.Vehicle;
        if (v == null || !IsInstanceValid(v) || f.Camera == null) return;
        Vector2 view = Size;
        _s = Mathf.Clamp(view.Y / 1080f, 0.6f, 1.6f);
        _r = 180f * _s;
        _c = view * 0.5f;

        if (_lastFuel >= 0 && dt > 0)
        {
            float burn = Mathf.Max(0, (_lastFuel - v.FuelL) / dt);
            if (burn < 50f) _burnLps = Mathf.Lerp(_burnLps, burn, Mathf.Min(1f, dt * 0.5f));
        }

        _lastFuel = v.FuelL;

        _sampleTimer -= dt;
        if (_sampleTimer <= 0)
        {
            _sampleTimer = 0.25f;
            Sample(v, f.Lock);
        }

        _mapTimer -= dt;
        if (_mapTimer <= 0)
        {
            _mapTimer = 0.5f;
            BuildDamageMap(v);
        }

        _hurtShow = Mathf.Max(0, _hurtShow - dt);

        // Missiles chasing us: which way each one is, round the ring.
        _threats.Clear();
        f.Combat?.IncomingMissiles(v, _incoming);
        foreach (Vector3 m in _incoming) _threats.Add(Bearing(f.Camera, m));

        _lockOn = f.Lock != null && IsInstanceValid(f.Lock) && f.Lock.Model.Blocks.Count > 0;
        if (_lockOn)
        {
            Vector3 at = f.Lock.GlobalTransform * f.Lock.CenterOfMass, com = v.GlobalTransform * v.CenterOfMass;
            float range = at.DistanceTo(com);
            if (_lockRange > 0 && dt > 0) _closing = Mathf.Lerp(_closing, (_lockRange - range) / dt * 3.6f, Mathf.Min(1f, dt * 2f));
            _lockRange = range;
            _lockOn = !f.Camera.IsPositionBehind(at);
            if (_lockOn) _lockPos = f.Camera.UnprojectPosition(at);
        }
        else _lockRange = -1f;

        QueueRedraw();
    }

    /// <summary>Which way something is from where the camera looks, as an angle round the ring: 0 ahead (top), 90 right, 180 behind (bottom).</summary>
    private static float Bearing(Camera3D cam, Vector3 at)
    {
        Basis b = cam.GlobalTransform.Basis;
        Vector3 d = at - cam.GlobalPosition;
        Vector3 right = b.X, fwd = -b.Z;
        fwd.Y = 0;
        right.Y = 0;
        if (fwd.LengthSquared() < 1e-6f) fwd = -b.Y;
        return Mathf.RadToDeg(Mathf.Atan2(d.Dot(right.Normalized()), d.Dot(fwd.Normalized())));
    }

    private void Sample(VehicleBody v, VehicleBody locked)
    {
        _missiles = v.Analysis.Missiles.Count > 0 ? v.MissilesReady : 0;
        _bombs = v.Analysis.BombRacks.Count > 0 ? v.BombsLeft : 0;
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

        // Contacts within sight or radar: round the ring's edge where they are.
        _contacts.Clear();
        if (_f.Camera == null) return;
        float range = Mathf.Clamp(v.SightM, 1000f, 12000f);
        Vector3 here = v.GlobalPosition;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody o || o == v || !IsInstanceValid(o) || o.Model.Blocks.Count == 0) continue;
            bool friend = o.Faction == v.Faction;
            if (!friend && !Spotting.IsSpotted(v.Faction, o)) continue;
            Vector3 d = o.GlobalPosition - here;
            if (new Vector2(d.X, d.Z).Length() > range) continue;
            _contacts.Add((Bearing(_f.Camera, o.GlobalPosition), o.IsAir && !o.OnGround, friend, o == locked));
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
        int alive = _map.Sum(m => m.Alive);
        // A new hit, or a new fire: show the damage for a few seconds.
        if ((_lastAlive >= 0 && alive < _lastAlive) || v.FiresBurning > _lastFires) _hurtShow = 4.5f;
        _lastAlive = alive;
        _lastFires = v.FiresBurning;
    }

    // ------------------------------------------------------------------ drawing helpers

    private Vector2 P(float r, float deg)
    {
        float a = Mathf.DegToRad(deg);
        return _c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r;
    }

    private void Arc(float r, float from, float to, Color col, float w)
    {
        if (to <= from) return;
        float a0 = Mathf.DegToRad(from - 90f), a1 = Mathf.DegToRad(to - 90f);
        int segs = Mathf.Max(4, (int)((to - from) / 3f));
        DrawArc(_c, r, a0, a1, segs, new Color(Shade, Shade.A * col.A), w + 3f * _s, true);
        DrawArc(_c, r, a0, a1, segs, col, w, true);
    }

    private void Line(Vector2 a, Vector2 b, Color col, float w)
    {
        DrawLine(a, b, new Color(Shade, Shade.A * col.A), w + 3f * _s, true);
        DrawLine(a, b, col, w, true);
    }

    private void Text(Vector2 pos, string s, float size, Color col, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1, Font font = null)
    {
        font ??= GlassStyle.Number;
        int px = Mathf.Max(8, (int)(size * _s));
        if (align != HorizontalAlignment.Left && width < 0)
        {
            float w = font.GetStringSize(s, HorizontalAlignment.Left, -1, px).X;
            pos.X -= align == HorizontalAlignment.Center ? w * 0.5f : w;
            align = HorizontalAlignment.Left;
        }

        DrawStringOutline(font, pos, s, align, width, px, Mathf.Max(3, (int)(5 * _s)), new Color(Shade, 0.7f * col.A));
        DrawString(font, pos, s, align, width, px, col);
    }

    private float Width(string s, float size, Font font = null) =>
        (font ?? GlassStyle.Number).GetStringSize(s, HorizontalAlignment.Left, -1, Mathf.Max(8, (int)(size * _s))).X;

    /// <summary>A caret on the ring pointing in at the reading.</summary>
    private void Caret(float deg)
    {
        Vector2 tip = P(_r - 4f * _s, deg), a = P(_r - 18f * _s, deg - 3f), b = P(_r - 18f * _s, deg + 3f);
        DrawColoredPolygon(new[] { tip, a, b }, GlassStyle.Cyan);
    }

    /// <summary>
    /// A scale that slides under a fixed caret: ticks every <paramref name="step"/>, longer every <paramref name="major"/>,
    /// <paramref name="degPer"/> degrees round the ring per unit, larger values toward <paramref name="upSign"/>.
    /// </summary>
    private void Scale(float from, float to, float value, float step, float major, float degPer, float upSign)
    {
        Arc(_r, from, to, new Color(Ink, 0.5f), 1.6f * _s);
        float mid = (from + to) * 0.5f, span = (to - from) * 0.5f / degPer;
        float first = Mathf.Floor((value - span) / step) * step;
        for (float t = first; t <= value + span + 0.01f; t += step)
        {
            float a = mid + (t - value) * degPer * upSign;
            if (a < from - 0.01f || a > to + 0.01f) continue;
            bool big = Mathf.Abs(t / major - Mathf.Round(t / major)) < 1e-3f;
            Line(P(_r, a), P(_r + (big ? 12f : 6f) * _s, a), new Color(Ink, big ? 0.95f : 0.55f), 1.6f * _s);
        }

        Caret(mid);
    }

    // ------------------------------------------------------------------ the ring

    public override void _Draw()
    {
        VehicleBody v = _f.Vehicle;
        if (v == null || !IsInstanceValid(v) || _f.Camera == null) return;
        bool air = v.IsAir, plane = air && v.FlightMode == MobilityMode.Plane, naval = v.IsNaval, post = v.IsBuilding;

        if (!post)
        {
            // Left: speed.
            float speed = air ? v.AirspeedKmh : Mathf.Abs(v.SpeedKmh);
            Scale(SpeedFrom, SpeedTo, speed, 10f, 50f, 0.4f, 1f);
            Text(new Vector2(_c.X - _r - 24f * _s, _c.Y + 14f * _s), speed.ToString("0"), 40, Ink, HorizontalAlignment.Right);
            string unit = plane ? $"KM/H · M {v.AirspeedKmh / 1225f:0.00}" : "KM/H";
            Text(new Vector2(_c.X - _r - 26f * _s, _c.Y + 34f * _s), unit, 12, GlassStyle.CyanDim, HorizontalAlignment.Right, -1, GlassStyle.Label);

            // Inside it: throttle, telegraph or engine power, amber in the afterburner's range.
            float first = plane ? v.AirThrottle
                : naval ? v.Telegraph / (float)(VehicleBody.TelegraphNames.Length - 1)
                : v.PowerKw > 0 ? v.UsablePowerKw / v.PowerKw : 0f;
            float ri = _r - 26f * _s, span = SpeedTo - SpeedFrom - 4f;
            Arc(ri, SpeedFrom + 2f, SpeedTo - 2f, new Color(GlassStyle.Cyan, 0.18f), 4f * _s);
            float split = plane && v.HasJets ? 0.85f : 1f;
            Arc(ri, SpeedFrom + 2f, SpeedFrom + 2f + span * Mathf.Min(first, split), GlassStyle.Cyan, 4f * _s);
            if (first > split) Arc(ri, SpeedFrom + 2f + span * split, SpeedFrom + 2f + span * first, GlassStyle.Amber, 4f * _s);

            // Right: height, depth, the telegraph, or the engine.
            DrawRight(v, air, naval);

            // Inside it: fuel, from the bottom up.
            float fuel = v.FuelCapacityL > 0 ? v.FuelL / v.FuelCapacityL : 0f;
            Color fc = fuel < 0.1f ? GlassStyle.Red : fuel < 0.25f ? GlassStyle.Amber : GlassStyle.Cyan;
            Arc(ri, RightFrom + 2f, RightTo - 2f, new Color(GlassStyle.Cyan, 0.18f), 4f * _s);
            Arc(ri, RightTo - 2f - (RightTo - RightFrom - 4f) * Mathf.Clamp(fuel, 0, 1), RightTo - 2f, fc, 4f * _s);
            if (fuel < 0.25f && v.FuelCapacityL > 0)
            {
                float minutes = Instruments.FuelMinutes(v.FuelL, _burnLps);
                string left = float.IsInfinity(minutes) || minutes > 999 ? "" : $" · {minutes:0} MIN";
                Text(new Vector2(_c.X + _r + 26f * _s, _c.Y + 54f * _s), "FUEL" + left, 12, fc, HorizontalAlignment.Left, -1, GlassStyle.Label);
            }
        }

        DrawHeading(v, air);
        DrawThreatsAndContacts();
        DrawWeapons(v, post);
        DrawDamage(v);
        DrawLock();
        DrawAlerts();
    }

    private void DrawRight(VehicleBody v, bool air, bool naval)
    {
        Vector2 at = new(_c.X + _r + 24f * _s, _c.Y + 14f * _s), sub = new(_c.X + _r + 26f * _s, _c.Y + 34f * _s);
        if (air)
        {
            Scale(RightFrom, RightTo, v.AltitudeM, 50f, 250f, 0.08f, -1f);
            Text(at, Group(v.AltitudeM), 40, Ink);
            string climb = v.ClimbRate >= 0 ? $"↑ {v.ClimbRate:0}" : $"↓ {-v.ClimbRate:0}";
            Text(sub, $"M · {climb} · AGL {v.HeightAboveGroundM:0}", 12, GlassStyle.CyanDim, HorizontalAlignment.Left, -1, GlassStyle.Label);
        }
        else if (v.IsSubmarine)
        {
            // Deeper is further down the arc.
            Scale(RightFrom, RightTo, -v.DepthM, 5f, 25f, 0.8f, -1f);
            Text(at, Mathf.Max(0, v.DepthM).ToString("0"), 40, Ink);
            Text(sub, $"M DEEP · ORDERED {v.TargetDepth:0} · {v.TelegraphName}", 12, GlassStyle.CyanDim, HorizontalAlignment.Left, -1, GlassStyle.Label);
        }
        else if (naval)
        {
            // The telegraph's six positions as notches, the one ordered lit.
            Arc(_r, RightFrom, RightTo, new Color(Ink, 0.5f), 1.6f * _s);
            int n = VehicleBody.TelegraphNames.Length;
            for (int i = 0; i < n; i++)
            {
                float a = RightTo - i * (RightTo - RightFrom) / (n - 1);
                bool on = i == v.Telegraph;
                Line(P(_r, a), P(_r + (on ? 14f : 7f) * _s, a), on ? GlassStyle.Cyan : new Color(Ink, 0.55f), (on ? 3f : 1.6f) * _s);
            }

            Caret(RightTo - v.Telegraph * (RightTo - RightFrom) / (n - 1));
            Text(at, v.TelegraphName, 24, Ink);
            Text(sub, "TELEGRAPH · W / S", 12, GlassStyle.CyanDim, HorizontalAlignment.Left, -1, GlassStyle.Label);
        }
        else
        {
            // The engine: how much of its power it's giving.
            float load = v.PowerKw > 0 ? v.UsablePowerKw / v.PowerKw : 0f;
            Arc(_r, RightFrom, RightTo, new Color(Ink, 0.5f), 1.6f * _s);
            for (int i = 0; i <= 10; i++)
            {
                float a = RightTo - i * (RightTo - RightFrom) / 10f;
                Line(P(_r, a), P(_r + (i % 5 == 0 ? 12f : 6f) * _s, a), new Color(Ink, i % 5 == 0 ? 0.95f : 0.55f), 1.6f * _s);
            }

            Caret(RightTo - load * (RightTo - RightFrom));
            Text(at, v.UsablePowerKw.ToString("0"), 40, v.PowerKw > 0 ? Ink : GlassStyle.Red);
            Text(sub, v.PowerKw > 0 ? $"KW · {load * 100f:0}%" : "ENGINES OUT", 12, v.PowerKw > 0 ? GlassStyle.CyanDim : GlassStyle.Red,
                HorizontalAlignment.Left, -1, GlassStyle.Label);
        }
    }

    private static string Group(float m)
    {
        int i = Mathf.RoundToInt(m);
        return Mathf.Abs(i) >= 1000 ? $"{i / 1000} {Mathf.Abs(i % 1000):000}" : i.ToString();
    }

    /// <summary>
    /// Heading across the top: where you're looking (an aircraft: where its nose points). On land and at sea a small
    /// hull mark shows which way the hull faces, which matters for armor.
    /// </summary>
    private void DrawHeading(VehicleBody v, bool air)
    {
        Vector3 hullFwd = -v.GlobalTransform.Basis.Z, look = -_f.Camera.GlobalTransform.Basis.Z;
        float hull = Instruments.Heading(new NVec(hullFwd.X, hullFwd.Y, hullFwd.Z));
        float hdg = air ? hull : Instruments.Heading(new NVec(look.X, look.Y, look.Z));
        const float degPer = HeadHalf / 30f;
        Arc(_r, -HeadHalf, HeadHalf, new Color(Ink, 0.5f), 1.6f * _s);
        for (int d = (int)Mathf.Floor((hdg - 30f) / 5f) * 5; d <= hdg + 30f; d += 5)
        {
            float a = Instruments.Turn(hdg, d) * degPer;
            if (Mathf.Abs(a) > HeadHalf) continue;
            int shown = ((d % 360) + 360) % 360;
            bool big = shown % 10 == 0;
            Line(P(_r, a), P(_r + (big ? 12f : 6f) * _s, a), new Color(Ink, big ? 0.95f : 0.55f), 1.6f * _s);
            if (shown % 30 == 0 && Mathf.Abs(a) > 6f)
            {
                string label = shown == 0 ? "N" : shown == 90 ? "E" : shown == 180 ? "S" : shown == 270 ? "W" : (shown / 10).ToString("00");
                Text(P(_r + 26f * _s, a) + new Vector2(0, 4f * _s), label, 12, InkDim, HorizontalAlignment.Center, -1, GlassStyle.Label);
            }
        }

        Caret(0f);
        Text(new Vector2(_c.X, _c.Y - _r - 30f * _s), hdg.ToString("000"), 20, Ink, HorizontalAlignment.Center);
        if (!air && !v.IsBuilding)
        {
            // The hull mark: on the arc if it's within it, pinned to the end (pointing on) if not.
            float off = Instruments.Turn(hdg, hull) * degPer;
            bool beyond = Mathf.Abs(off) > HeadHalf;
            float a = Mathf.Clamp(off, -HeadHalf, HeadHalf);
            Vector2 p = P(_r + 2f * _s, a), dir = (P(_r + 20f * _s, a) - p).Normalized(), side = new(-dir.Y, dir.X);
            Color hc = GlassStyle.Amber;
            DrawColoredPolygon(new[] { p + dir * 16f * _s, p + side * 6f * _s, p - side * 6f * _s }, hc);
            if (beyond) Text(P(_r + 40f * _s, a), off > 0 ? "HULL ›" : "‹ HULL", 11, hc, HorizontalAlignment.Center, -1, GlassStyle.Label);
        }
    }

    /// <summary>Missiles coming at you light up the ring on their side; contacts sit just outside it where they are.</summary>
    private void DrawThreatsAndContacts()
    {
        float pulse = 0.65f + 0.35f * Mathf.Sin(_t * 12f);
        foreach (float b in _threats)
        {
            Arc(_r + 22f * _s, b - 14f, b + 14f, new Color(GlassStyle.Red, pulse), 6f * _s);
            Text(P(_r + 48f * _s, b) + new Vector2(0, 5f * _s), "MSL", 13, new Color(GlassStyle.Red, pulse), HorizontalAlignment.Center, -1, GlassStyle.Label);
        }

        foreach ((float bearing, bool air, bool friend, bool locked) in _contacts)
        {
            Vector2 p = P(_r + 34f * _s, bearing);
            Color col = friend ? new Color(GlassStyle.Cyan, 0.6f) : GlassStyle.Red;
            float k = (friend ? 4f : 5.5f) * _s;
            Vector2 outward = (p - _c).Normalized(), side = new(-outward.Y, outward.X);
            if (air) DrawColoredPolygon(new[] { p + outward * k * 1.2f, p - outward * k * 0.8f + side * k, p - outward * k * 0.8f - side * k }, col);
            else DrawRect(new Rect2(p - new Vector2(k, k) * 0.8f, new Vector2(k, k) * 1.6f), col);
            if (locked) DrawRect(new Rect2(p - new Vector2(k, k) * 1.8f, new Vector2(k, k) * 3.6f), GlassStyle.Red, false, 1.6f * _s);
        }
    }

    /// <summary>Weapons in one line under the ring; the systems worth knowing about in a dimmer line under that.</summary>
    private void DrawWeapons(VehicleBody v, bool post)
    {
        var parts = new List<(string text, Color col)>();
        void Sep() { if (parts.Count > 0) parts.Add(("  ·  ", InkDim)); }
        float reload = 1f;
        bool firstGroup = true;
        foreach (IGrouping<string, VehicleBody.GunRt> group in v.Guns.GroupBy(g => g.Label).Take(2))
        {
            var alive = group.Where(g => g.Alive).ToList();
            bool first = firstGroup;
            firstGroup = false;
            Sep();
            if (alive.Count == 0)
            {
                parts.Add((group.Key.ToUpperInvariant() + " OUT", GlassStyle.Red));
                continue;
            }

            VehicleBody.GunRt g0 = alive[0];
            bool beam = g0.Info.BreechDef.Breech.Beam;
            if (first && !beam) reload = Mathf.Clamp(1f - alive.Min(g => g.ReloadLeft) / Mathf.Max(g0.Stats.ReloadS, 0.01f), 0, 1);
            parts.Add(((alive.Count > 1 ? $"{alive.Count}× " : "") + group.Key.ToUpperInvariant() + "  ", Ink));
            parts.Add((beam ? (v.HasElectric ? $"{v.Electric.Fraction * 100f:0}%" : "—") : v.Rounds(g0, v.SelectedAmmo).ToString(), GlassStyle.Cyan));
        }

        if (v.Guns.Count > 0)
        {
            Sep();
            parts.Add(((v.SelectedAmmo switch { AmmoType.HE => "HE", AmmoType.Flak => "FLAK", _ => "AP" }) + " [Q]", GlassStyle.Amber));
        }

        if (v.Analysis.Missiles.Count > 0)
        {
            Sep();
            string dots = _missiles <= 6 ? new string('●', _missiles) + new string('○', Mathf.Clamp(v.Analysis.Missiles.Count - _missiles, 0, 6 - _missiles)) : _missiles.ToString();
            parts.Add(((v.IsAir ? "MSL " : "MSL [X] ") + dots, _missiles > 0 ? Ink : GlassStyle.Off));
        }

        if (v.Analysis.BombRacks.Count > 0)
        {
            Sep();
            parts.Add(($"BMB [B] {_bombs}", _bombs > 0 ? Ink : GlassStyle.Off));
        }

        if (_decoyTotal > 0)
        {
            Sep();
            parts.Add(($"FLR {_decoyReady}", _decoyReady > 0 ? Ink : GlassStyle.Off));
        }

        if (_smokeTotal > 0)
        {
            Sep();
            parts.Add(($"SMK [Z] {_smokeReady}", _smokeReady > 0 ? Ink : GlassStyle.Off));
        }

        float y = _c.Y + _r + 44f * _s;
        if (parts.Count > 0)
        {
            const float size = 15f;
            float total = parts.Sum(p => Width(p.text, size, GlassStyle.Label));
            float x = _c.X - total * 0.5f;
            float gunEnd = x;
            for (int i = 0; i < parts.Count; i++)
            {
                Text(new Vector2(x, y), parts[i].text, size, parts[i].col, HorizontalAlignment.Left, -1, GlassStyle.Label);
                x += Width(parts[i].text, size, GlassStyle.Label);
                if (i == 1) gunEnd = x;
            }

            // The first gun's reload as a thin line under its name.
            if (v.Guns.Count > 0 && reload < 0.999f)
            {
                float x0 = _c.X - total * 0.5f, w = gunEnd - x0;
                DrawRect(new Rect2(x0, y + 5f * _s, w, 2.5f * _s), new Color(GlassStyle.Cyan, 0.2f));
                DrawRect(new Rect2(x0, y + 5f * _s, w * reload, 2.5f * _s), GlassStyle.Amber);
            }
        }

        // Systems: only the ones that aren't in their usual state.
        var sys = new List<string>();
        if (v.HasRetractingGear && v.GearDown && !v.OnGround) sys.Add("GEAR DOWN");
        if (v.HasBayDoors && v.BayOpen) sys.Add("BAY OPEN [X]");
        if (v.HasLights && v.LightsOn) sys.Add("LIGHTS [H]");
        if (v.HasBlade && v.BladeDown) sys.Add("BLADE DOWN [K]");
        if (v.HasVectoring && v.IsAir) sys.Add("VECTORING");
        if (v.Seats.Any(s => s.Role == SeatRole.Gunner))
        {
            if (v.Roe == Engagement.HoldFire) sys.Add("HOLD FIRE [O]");
            else if (v.Roe == Engagement.ReturnFire) sys.Add("RETURN FIRE [O]");
            else if (!v.GunnerFollowsPlayer) sys.Add("GUNNERS FREE");
        }

        if (sys.Count > 0) Text(new Vector2(_c.X, y + 24f * _s), string.Join("  ·  ", sys), 11, GlassStyle.CyanDim, HorizontalAlignment.Center, -1, GlassStyle.Label);
    }

    /// <summary>The vehicle from above beside the ring, for a few seconds after a hit (and while anything's burning).</summary>
    private void DrawDamage(VehicleBody v)
    {
        if (_map.Count == 0) return;
        float alpha = v.FiresBurning > 0 ? 1f : Mathf.Clamp(_hurtShow, 0f, 1f);
        if (alpha <= 0.01f) return;
        var box = new Rect2(_c.X - _r - 170f * _s, _c.Y + _r * 0.42f, 120f * _s, 74f * _s);
        float w = _mapMaxX - _mapMinX + 1, h = _mapMaxZ - _mapMinZ + 1;
        float sc = Mathf.Min(box.Size.X / w, box.Size.Y / h);
        Vector2 origin = box.Position + (box.Size - new Vector2(w, h) * sc) * 0.5f;
        float pad = sc > 3f ? 0.6f : 0f;
        int hit = 0, gone = 0;
        foreach (Instruments.DamageColumn col in _map)
        {
            Color fill = col.Health >= 0.999f ? new Color(GlassStyle.Cyan, 0.4f) : col.Health > 0f ? GlassStyle.Amber : GlassStyle.Red;
            if (col.Health < 0.999f && col.Health > 0f) hit++;
            if (col.Health <= 0f) gone++;
            DrawRect(new Rect2(origin + new Vector2(col.X - _mapMinX, col.Z - _mapMinZ) * sc, new Vector2(sc - pad, sc - pad)), new Color(fill, fill.A * alpha));
        }

        int crew = v.Seats.Count(s2 => v.SeatAlive(s2) && s2.Occupant != Occupant.Empty), seats = v.Seats.Count;
        string line = gone > 0 ? $"{gone} GONE" : hit > 0 ? $"{hit} HIT" : "INTACT";
        if (crew < seats) line += $" · CREW {crew}/{seats}";
        if (v.FiresBurning > 0) line += $" · FIRE {v.FiresBurning}";
        Color lc = v.FiresBurning > 0 || gone > 0 ? GlassStyle.Red : GlassStyle.Amber;
        Text(new Vector2(box.Position.X + box.Size.X * 0.5f, box.End.Y + 18f * _s), line, 11, new Color(lc, alpha), HorizontalAlignment.Center, -1, GlassStyle.Label);
    }

    private void DrawLock()
    {
        if (!_lockOn || _f.Lock == null || !IsInstanceValid(_f.Lock)) return;
        float b = 26f * _s, l = 10f * _s;
        Color r = GlassStyle.Red;
        foreach ((int sx, int sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Vector2 corner = _lockPos + new Vector2(sx * b, sy * b);
            Line(corner - new Vector2(sx * l, 0), corner, r, 2.2f * _s);
            Line(corner, corner - new Vector2(0, sy * l), r, 2.2f * _s);
        }

        string text = $"{_f.Lock.NameForYou.ToUpperInvariant()} · {_lockRange / 1000f:0.0} KM · {_closing:+0;-0}";
        Text(_lockPos + new Vector2(b + 12f * _s, 5f * _s), text, 12, r, HorizontalAlignment.Left, -1, GlassStyle.Label);
    }

    /// <summary>The most urgent warnings in a line or two above the heading, no box.</summary>
    private void DrawAlerts()
    {
        if (_f.Alerts == null || _f.Alerts.Count == 0) return;
        float y = _c.Y - _r - 62f * _s;
        foreach ((string text, Color color) in _f.Alerts.Take(2))
        {
            bool urgent = color == GlassStyle.Red;
            Color c = urgent ? new Color(color, 0.75f + 0.25f * Mathf.Sin(_t * 9f)) : color;
            Text(new Vector2(_c.X, y), text, urgent ? 16 : 14, c, HorizontalAlignment.Center, -1, GlassStyle.Label);
            y -= 24f * _s;
        }
    }
}
