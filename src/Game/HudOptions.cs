using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>The crew orders the options panel offers: the vehicle's rules of engagement, plus whether its gunners follow your aim.</summary>
public enum CrewOrder
{
    FireAtWill,
    FollowMyAim,
    ReturnFire,
    HoldFire,
}

/// <summary>What the options panel can ask the range to do.</summary>
public sealed class OptionActions
{
    public Action Lights, Smoke, Blade, LiveFire, Ammo;
    public Action<OverlayStyle> Overlay;
    public Func<bool> LiveFireOn;
}

/// <summary>
/// Vehicle options (O): smoked-glass panels attached beside the vehicle, with the crew's orders, the systems you can
/// switch, the loaded ammunition and the overlay style. The mouse is free while it's open; O or Esc closes it.
/// </summary>
public partial class HudOptions : Control
{
    private const float W = 520f;

    private readonly OptionActions _act;
    private readonly VBoxContainer _stack;
    private readonly List<(Button button, CrewOrder order)> _orders = new();
    private readonly List<(Button button, OverlayStyle style)> _styles = new();
    private Button _lights, _smoke, _blade, _live, _ammo;
    private GlassPanel _ordersPanel, _systemsPanel, _ammoPanel, _stylePanel;
    private VehicleBody _v;
    private Vector2 _pos;
    private bool _placed;

    public HudOptions(OptionActions act)
    {
        _act = act;
        MouseFilter = MouseFilterEnum.Ignore;
        _stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 14);
        AddChild(_stack);
        BuildOrders();
        BuildSystems();
        BuildAmmo();
        BuildStyles();
    }

    public static CrewOrder OrderOf(VehicleBody v) => v.Roe switch
    {
        Engagement.HoldFire => CrewOrder.HoldFire,
        Engagement.ReturnFire => CrewOrder.ReturnFire,
        _ => v.GunnerFollowsPlayer ? CrewOrder.FollowMyAim : CrewOrder.FireAtWill,
    };

    public static void Apply(VehicleBody v, CrewOrder o)
    {
        v.Roe = o switch { CrewOrder.HoldFire => Engagement.HoldFire, CrewOrder.ReturnFire => Engagement.ReturnFire, _ => Engagement.FireAtWill };
        v.GunnerFollowsPlayer = o == CrewOrder.FollowMyAim;
    }

    // ------------------------------------------------------------------ building

    private GlassPanel Section(float height, string caption, out VBoxContainer body)
    {
        var p = new GlassPanel(new Vector2(W, height)) { Painter = c => GlassStyle.Caption(c, new Vector2(18, 26), caption) };
        _stack.AddChild(p);
        body = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(12, 40), Size = new Vector2(W - 24, height - 50) };
        body.AddThemeConstantOverride("separation", 6);
        p.AddChild(body);
        return p;
    }

    /// <summary>A flat glass button: a faint fill and border, brighter when hovered, cyan-edged when selected.</summary>
    private static Button GlassButton(string text, float height, Action pressed, bool toggle = false)
    {
        var b = new Button
        {
            Text = text, ToggleMode = toggle, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, height), MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        StyleBoxFlat Box(float fill, float edge, int width = 1) =>
            Ui.Box(new Color(GlassStyle.Cyan, fill), 0, 12, new Color(GlassStyle.Cyan, edge), width);
        b.AddThemeStyleboxOverride("normal", Box(0.05f, 0.25f));
        b.AddThemeStyleboxOverride("hover", Box(0.12f, 0.45f));
        b.AddThemeStyleboxOverride("pressed", Box(0.16f, 1f, 2));
        b.AddThemeStyleboxOverride("hover_pressed", Box(0.2f, 1f, 2));
        b.AddThemeStyleboxOverride("disabled", Box(0.02f, 0.12f));
        b.AddThemeFontOverride("font", GlassStyle.Label);
        b.AddThemeFontSizeOverride("font_size", 14);
        b.AddThemeColorOverride("font_color", GlassStyle.Text);
        b.AddThemeColorOverride("font_hover_color", GlassStyle.Text);
        b.AddThemeColorOverride("font_pressed_color", GlassStyle.Cyan);
        b.AddThemeColorOverride("font_hover_pressed_color", GlassStyle.Cyan);
        b.AddThemeColorOverride("font_disabled_color", GlassStyle.Off);
        b.Pressed += () =>
        {
            pressed?.Invoke();
            Sfx.Play2D(b, SoundId.Tick, -8f);
        };
        return b;
    }

    private void BuildOrders()
    {
        _ordersPanel = Section(286, "CREW ORDERS", out VBoxContainer body);
        foreach ((CrewOrder o, string title, string sub) in new[]
                 {
                     (CrewOrder.FireAtWill, "FIRE AT WILL", "Gunners pick their own targets"),
                     (CrewOrder.FollowMyAim, "FOLLOW MY AIM", "Gunners shoot where you point"),
                     (CrewOrder.ReturnFire, "RETURN FIRE ONLY", "Only at whoever shoots first"),
                     (CrewOrder.HoldFire, "HOLD FIRE", "Nobody shoots"),
                 })
        {
            Button b = GlassButton($"  {title}   ·   {sub}", 52, () =>
            {
                if (_v == null) return;
                Apply(_v, o);
                Sync();
            }, toggle: true);
            body.AddChild(b);
            _orders.Add((b, o));
        }
    }

    private void BuildSystems()
    {
        _systemsPanel = Section(112, "SYSTEMS", out VBoxContainer body);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        body.AddChild(row);
        _lights = GlassButton("LIGHTS  [H]", 46, () => { _act.Lights?.Invoke(); Sync(); }, toggle: true);
        _smoke = GlassButton("SMOKE  [Z]", 46, () => { _act.Smoke?.Invoke(); Sync(); });
        _blade = GlassButton("BLADE  [K]", 46, () => { _act.Blade?.Invoke(); Sync(); }, toggle: true);
        _live = GlassButton("LIVE FIRE  [L]", 46, () => { _act.LiveFire?.Invoke(); Sync(); }, toggle: true);
        foreach (Button b in new[] { _lights, _smoke, _blade, _live })
        {
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.Alignment = HorizontalAlignment.Center;
            row.AddChild(b);
        }
    }

    private void BuildAmmo()
    {
        _ammoPanel = Section(112, "LOADED", out VBoxContainer body);
        _ammo = GlassButton("", 46, () => { _act.Ammo?.Invoke(); Sync(); });
        _ammo.Alignment = HorizontalAlignment.Center;
        body.AddChild(_ammo);
    }

    private void BuildStyles()
    {
        _stylePanel = Section(170, "OVERLAY STYLE · COMES WITH THE COCKPIT", out VBoxContainer body);
        var grid = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        body.AddChild(grid);
        foreach (OverlayStyle s in Overlays.Choices)
        {
            bool built = s == OverlayStyle.Auto || Overlays.IsBuilt(s);
            Button b = GlassButton(Overlays.Name(s).ToUpperInvariant() + (built ? "" : "  · SOON"), 46, () =>
            {
                _act.Overlay?.Invoke(s);
                Sync();
            }, toggle: true);
            b.Alignment = HorizontalAlignment.Center;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.CustomMinimumSize = new Vector2(156, 46);
            b.Disabled = !built;
            b.TooltipText = Overlays.Describe(s);
            grid.AddChild(b);
            _styles.Add((b, s));
        }
    }

    // ------------------------------------------------------------------ each frame

    /// <summary>Shows the panel for <paramref name="v"/> and keeps it beside the vehicle on screen.</summary>
    public void Refresh(VehicleBody v, Camera3D cam, float dt)
    {
        bool changed = v != _v;
        _v = v;
        if (v == null || !IsInstanceValid(v) || cam == null) return;
        Vector2 view = GetViewportRect().Size;
        float scale = Mathf.Clamp(view.Y / 1080f, 0.6f, 1.6f) * 0.9f;
        _stack.Scale = new Vector2(scale, scale);
        Vector3 com = v.GlobalTransform * v.CenterOfMass;
        Vector2 anchor = cam.IsPositionBehind(com) ? view * 0.5f : cam.UnprojectPosition(com);
        Vector2 size = _stack.GetCombinedMinimumSize() * scale;
        // On the side away from the Glass panels, which hang left of the vehicle unless it's well left of center.
        bool right = anchor.X > view.X * 0.42f;
        var want = new Vector2(right ? anchor.X + 150f * scale : anchor.X - 150f * scale - size.X, anchor.Y - size.Y * 0.5f);
        want.X = Mathf.Clamp(want.X, 16f, view.X - size.X - 16f);
        want.Y = Mathf.Clamp(want.Y, 16f, Mathf.Max(16f, view.Y - size.Y - 16f));
        _pos = _placed && !changed ? _pos.Lerp(want, Mathf.Min(1f, dt * 6f)) : want;
        _placed = true;
        _stack.Position = _pos;
        Sync();
    }

    /// <summary>Lights up what's on and hides what this vehicle doesn't have.</summary>
    private void Sync()
    {
        VehicleBody v = _v;
        if (v == null || !IsInstanceValid(v)) return;
        bool guns = v.Seats.Any(s => s.Role == SeatRole.Gunner) || v.Guns.Count > 0 || v.Analysis.Missiles.Count > 0;
        _ordersPanel.Visible = guns;
        CrewOrder now = OrderOf(v);
        foreach ((Button b, CrewOrder o) in _orders) b.SetPressedNoSignal(o == now);

        _lights.Visible = v.HasLights;
        _lights.SetPressedNoSignal(v.LightsOn);
        int smoke = v.Model.Instances.Count(b => b.Def.Id == "smoke_launcher");
        _smoke.Visible = smoke > 0;
        _smoke.Text = $"SMOKE {v.SmokeReady}  [Z]";
        _smoke.Disabled = v.SmokeReady == 0;
        _blade.Visible = v.HasBlade;
        _blade.SetPressedNoSignal(v.BladeDown);
        _live.SetPressedNoSignal(_act.LiveFireOn?.Invoke() ?? false);

        _ammoPanel.Visible = v.Guns.Count > 0;
        string ammo = v.SelectedAmmo switch { AmmoType.HE => "HE", AmmoType.Flak => "FLAK", _ => "AP" };
        VehicleBody.GunRt g = v.Guns.FirstOrDefault(x => x.Alive);
        _ammo.Text = g == null ? "NO GUNS LEFT" : $"◀    {ammo} · {v.Rounds(g, v.SelectedAmmo)} ROUNDS    ▶      [Q]";

        OverlayStyle pick = v.Overlay;
        foreach ((Button b, OverlayStyle s) in _styles) b.SetPressedNoSignal(s == pick);
    }
}
