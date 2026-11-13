using System;
using Godot;

namespace DreamGame.Game;

/// <summary>Which edge of a <see cref="TiltedPanel"/> stays put while the opposite edge swings away from you.</summary>
public enum Hinge
{
    Left,
    Right,
    Bottom,
    Top,
}

/// <summary>
/// A panel of smoked glass leaning in toward you, like one side of a curved desk. Its controls are laid out flat in
/// <see cref="Content"/> (drawn off screen, at the screen's full resolution so text stays sharp), then drawn here in
/// perspective over a blurred copy of the view behind. Clicks, hovers and the mouse wheel are mapped back through the
/// same perspective to the control under the pointer.
/// </summary>
public partial class TiltedPanel : Control
{
    private const string Code = @"
shader_type canvas_item;
uniform vec3 r0;
uniform vec3 r1;
uniform vec3 r2;
uniform vec2 box_pos;
uniform vec2 box_size;
uniform vec2 panel_size;
uniform bool glass = true;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;

void fragment() {
	// Where this pixel falls on the flat panel: the inverse of the perspective it's drawn with.
	vec3 p = vec3(box_pos + UV * box_size, 1.0);
	vec3 q = vec3(dot(r0, p), dot(r1, p), dot(r2, p));
	vec2 uv = q.xy / q.z;
	float edge = min(min(uv.x, 1.0 - uv.x) * panel_size.x, min(uv.y, 1.0 - uv.y) * panel_size.y);
	if (q.z <= 0.0 || edge < -1.0) {
		discard;
	}
	float a = clamp(edge + 0.5, 0.0, 1.0);
	// (What the panel drew is premultiplied: it was drawn over a clear background.)
	vec4 c = texture(TEXTURE, clamp(uv, vec2(0.0), vec2(1.0)));
	if (glass) {
		vec3 behind = textureLod(screen_tex, SCREEN_UV, 2.6).rgb;
		COLOR = vec4(c.rgb + behind * (1.0 - c.a), a);
	} else {
		COLOR = vec4(c.rgb / max(c.a, 0.0001), c.a * a);
	}
}
";

    private static Shader _shader;

    /// <summary>Lay the panel's controls out in here, as if it were flat.</summary>
    public readonly Control Content;

    public readonly SubViewport Port;

    /// <summary>The edge that stays put.</summary>
    public Hinge Hinge = Hinge.Left;

    /// <summary>How far the far edge swings away, in degrees.</summary>
    public float Angle = 22f;

    /// <summary>How far your eye is from the screen, in pixels: smaller is a stronger perspective.</summary>
    public float Depth = 1300f;

    /// <summary>Blur the view behind it (smoked glass); off, whatever's behind shows through where the panel is clear.</summary>
    public bool Glass = true;

    private readonly TextureRect _surface;
    private readonly ShaderMaterial _mat;
    private readonly Vector2[] _quad = new Vector2[4];
    private Vector3 _r0, _r1, _r2;
    private Vector2 _lastSize, _lastPos, _lastView, _eye;
    private float _lastAngle = float.NaN, _lastScale;
    private bool _lastGlass;

    public TiltedPanel()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Port = new SubViewport
        {
            TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenParentVisible,
            GuiEmbedSubwindows = true, HandleInputLocally = true, Size = new Vector2I(8, 8),
        };
        AddChild(Port);
        Content = new Control { Theme = Ui.Theme, MouseFilter = MouseFilterEnum.Ignore };
        Port.AddChild(Content);
        _mat = new ShaderMaterial { Shader = _shader ??= new Shader { Code = Code } };
        _surface = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore, Material = _mat, TextureFilter = TextureFilterEnum.Linear,
        };
        AddChild(_surface);
        // Leaving it: tell whatever was under the pointer it's gone (unless a drag is still going).
        MouseExited += () =>
        {
            if (Input.GetMouseButtonMask() == 0) Push(new InputEventMouseMotion { Position = new Vector2(-10, -10) }, new Vector2(-10, -10));
        };
    }

    public override void _Ready() => _surface.Texture = Port.GetTexture();

    public override void _Process(double delta) => Fit();

    /// <summary>Resizes the off-screen panel to match, and works out the perspective when anything has moved.</summary>
    private void Fit()
    {
        Vector2 view = GetViewportRect().Size, pos = GlobalPosition, size = Size.Round();
        float k = Mathf.Max(1f, GetTree()?.Root?.ContentScaleFactor ?? 1f);
        if (size == _lastSize && pos == _lastPos && view == _lastView && Mathf.IsEqualApprox(Angle, _lastAngle) &&
            Mathf.IsEqualApprox(k, _lastScale) && Glass == _lastGlass) return;
        _lastSize = size;
        _lastPos = pos;
        _lastView = view;
        _lastAngle = Angle;
        _lastScale = k;
        _lastGlass = Glass;
        if (size.X < 4 || size.Y < 4) return;

        // Drawn at the screen's real resolution, laid out at the UI's.
        Port.Size = new Vector2I(Mathf.CeilToInt(size.X * k), Mathf.CeilToInt(size.Y * k));
        Port.Size2DOverride = new Vector2I((int)size.X, (int)size.Y);
        Port.Size2DOverrideStretch = true;
        Content.Position = Vector2.Zero;
        Content.Size = size;

        Vector2 o = view * 0.5f - pos;
        _eye = o;
        _quad[0] = Project(new Vector2(0, 0), size, o);
        _quad[1] = Project(new Vector2(size.X, 0), size, o);
        _quad[2] = Project(size, size, o);
        _quad[3] = Project(new Vector2(0, size.Y), size, o);
        InverseHomography();

        Vector2 min = _quad[0], max = _quad[0];
        foreach (Vector2 q in _quad)
        {
            min = new Vector2(Mathf.Min(min.X, q.X), Mathf.Min(min.Y, q.Y));
            max = new Vector2(Mathf.Max(max.X, q.X), Mathf.Max(max.Y, q.Y));
        }

        min = min.Floor() - Vector2.One;
        max = max.Ceil() + Vector2.One;
        _surface.Position = min;
        _surface.Size = max - min;
        _mat.SetShaderParameter("r0", _r0);
        _mat.SetShaderParameter("r1", _r1);
        _mat.SetShaderParameter("r2", _r2);
        _mat.SetShaderParameter("box_pos", min);
        _mat.SetShaderParameter("box_size", max - min);
        _mat.SetShaderParameter("panel_size", size);
        _mat.SetShaderParameter("glass", Glass);
    }

    /// <summary>Where a point on the flat panel (in <see cref="Content"/>'s coordinates) is drawn, in this control's coordinates.</summary>
    public Vector2 FlatToLocal(Vector2 flat)
    {
        Fit();
        return Project(flat, _lastSize, _eye);
    }

    /// <summary>How much bigger or smaller things at that point on the panel are drawn (1 at the hinge, less further away).</summary>
    public float ScaleAt(Vector2 flat) => FlatToLocal(flat + new Vector2(10, 0)).DistanceTo(FlatToLocal(flat)) / 10f;

    /// <summary>A point on the flat panel, swung round the hinge and seen in perspective from the middle of the screen.</summary>
    private Vector2 Project(Vector2 p, Vector2 size, Vector2 eye)
    {
        float th = Mathf.DegToRad(Angle), c = Mathf.Cos(th), s = Mathf.Sin(th);
        Vector2 q;
        float z;
        switch (Hinge)
        {
            case Hinge.Right:
            {
                float d = size.X - p.X;
                q = new Vector2(size.X - d * c, p.Y);
                z = d * s;
                break;
            }
            case Hinge.Bottom:
            {
                float d = size.Y - p.Y;
                q = new Vector2(p.X, size.Y - d * c);
                z = d * s;
                break;
            }
            case Hinge.Top:
                q = new Vector2(p.X, p.Y * c);
                z = p.Y * s;
                break;
            default:
                q = new Vector2(p.X * c, p.Y);
                z = p.X * s;
                break;
        }

        return eye + (q - eye) * (Depth / (Depth + z));
    }

    /// <summary>
    /// The perspective as a homography from the panel's square (0 to 1 each way) to the drawn corners, inverted so
    /// a point on screen gives back where it is on the panel.
    /// </summary>
    private void InverseHomography()
    {
        Vector2 p0 = _quad[0], p1 = _quad[1], p2 = _quad[2], p3 = _quad[3];
        float sx = p0.X - p1.X + p2.X - p3.X, sy = p0.Y - p1.Y + p2.Y - p3.Y;
        float dx1 = p1.X - p2.X, dx2 = p3.X - p2.X, dy1 = p1.Y - p2.Y, dy2 = p3.Y - p2.Y;
        float det = dx1 * dy2 - dx2 * dy1;
        float g = Mathf.Abs(det) < 1e-9f ? 0 : (sx * dy2 - dx2 * sy) / det;
        float h = Mathf.Abs(det) < 1e-9f ? 0 : (dx1 * sy - sx * dy1) / det;
        float a = p1.X - p0.X + g * p1.X, b = p3.X - p0.X + h * p3.X, cc = p0.X;
        float d = p1.Y - p0.Y + g * p1.Y, e = p3.Y - p0.Y + h * p3.Y, f = p0.Y;
        // Inverse of [[a b c] [d e f] [g h 1]] by its adjugate (the scale doesn't matter: it divides out).
        _r0 = new Vector3(e - f * h, cc * h - b, b * f - cc * e);
        _r1 = new Vector3(f * g - d, a - cc * g, cc * d - a * f);
        _r2 = new Vector3(d * h - e * g, b * g - a * h, a * e - b * d);
        // The adjugate is the inverse times the determinant: keep its sign positive so "in front" stays w > 0.
        float detH = a * (e - f * h) - b * (d - f * g) + cc * (d * h - e * g);
        if (detH < 0)
        {
            _r0 = -_r0;
            _r1 = -_r1;
            _r2 = -_r2;
        }
    }

    /// <summary>Where a point (in this control's coordinates) lands on the flat panel, 0 to 1 each way.</summary>
    private Vector2 ToPanel(Vector2 local, out bool inFront)
    {
        var p = new Vector3(local.X, local.Y, 1f);
        float x = _r0.Dot(p), y = _r1.Dot(p), w = _r2.Dot(p);
        inFront = w > 0;
        return Mathf.Abs(w) < 1e-9f ? new Vector2(-1, -1) : new Vector2(x / w, y / w);
    }

    public override bool _HasPoint(Vector2 point)
    {
        Vector2 uv = ToPanel(point, out bool front);
        return front && uv.X >= 0 && uv.Y >= 0 && uv.X <= 1 && uv.Y <= 1;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouse m) return;
        Vector2 uv = ToPanel(m.Position, out bool front);
        bool inside = front && uv.X >= 0 && uv.Y >= 0 && uv.X <= 1 && uv.Y <= 1;
        // (While a button is held, keep passing the drag on even off the edge, so a scrollbar can be dragged, and
        // the button's release with it.)
        bool release = m is InputEventMouseButton { Pressed: false };
        if (!inside && m.ButtonMask == 0 && !release) return;
        Push(m, uv * Size);
        AcceptEvent();
    }

    private void Push(InputEventMouse m, Vector2 at)
    {
        var ev = (InputEventMouse)m.Duplicate();
        ev.Position = at;
        ev.GlobalPosition = at;
        Port.PushInput(ev, true);
    }
}

/// <summary>
/// A curved band of glass holding a row of controls, bowed like the top or bottom edge of a visor: the top rail's
/// middle sits highest, the bottom rail's lowest, and each control leans with the curve.
/// </summary>
public partial class CurvedRail : Container
{
    /// <summary>How far the middle sits above the ends (a top rail) or below them (a bottom rail).</summary>
    public float Bow = 26f;

    public bool Bottom;
    public float Separation = 6f;
    public float Band = 50f;
    public float Pad = 44f;

    public CurvedRail() => MouseFilter = MouseFilterEnum.Pass;

    private float Total()
    {
        float w = 0;
        int n = 0;
        foreach (Node c in GetChildren())
        {
            if (c is not Control ctl || !ctl.Visible) continue;
            w += ctl.GetCombinedMinimumSize().X;
            n++;
        }

        return w + Math.Max(0, n - 1) * Separation;
    }

    public override Vector2 _GetMinimumSize() => new(Total() + Pad * 2f, Band + Bow);

    /// <summary>Height of the band's middle line at x.</summary>
    private float CenterY(float x)
    {
        float t = Size.X > 0 ? x / Size.X * 2f - 1f : 0f;
        return Bottom ? Band * 0.5f + Bow * (1f - t * t) : Band * 0.5f + Bow * t * t;
    }

    private float Slope(float x)
    {
        float t = Size.X > 0 ? x / Size.X * 2f - 1f : 0f;
        float d = Bow * 2f * t * (2f / Mathf.Max(Size.X, 1f));
        return Bottom ? -d : d;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationSortChildren) Arrange();
        else if (what == NotificationResized) QueueRedraw();
    }

    private void Arrange()
    {
        float x = (Size.X - Total()) * 0.5f;
        foreach (Node c in GetChildren())
        {
            if (c is not Control ctl || !ctl.Visible) continue;
            Vector2 min = ctl.GetCombinedMinimumSize();
            float cx = x + min.X * 0.5f;
            FitChildInRect(ctl, new Rect2(x, CenterY(cx) - min.Y * 0.5f, min.X, min.Y));
            ctl.PivotOffset = min * 0.5f;
            ctl.Rotation = Mathf.Atan(Slope(cx));
            x += min.X + Separation;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        const int n = 48;
        var top = new Vector2[n + 1];
        var bottom = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float x = Size.X * i / n, y = CenterY(x);
            top[i] = new Vector2(x, y - Band * 0.5f);
            bottom[i] = new Vector2(x, y + Band * 0.5f);
        }

        var poly = new Vector2[(n + 1) * 2];
        for (int i = 0; i <= n; i++)
        {
            poly[i] = top[i];
            poly[poly.Length - 1 - i] = bottom[i];
        }

        DrawColoredPolygon(poly, new Color(0.03f, 0.055f, 0.08f, 0.74f));
        Color line = new(GlassStyle.Cyan, 0.35f);
        DrawPolyline(top, line, 1f, true);
        DrawPolyline(bottom, line, 1f, true);
        DrawLine(top[0], bottom[0], line, 1f, true);
        DrawLine(top[n], bottom[n], line, 1f, true);
        // Cyan brackets on the two outer corners, like the glass panels.
        Color c = GlassStyle.Cyan;
        DrawPolyline(new[] { top[0] + new Vector2(0, 10), top[0], top[0] + new Vector2(10, 0) }, c, 2f);
        DrawPolyline(new[] { bottom[n] - new Vector2(0, 10), bottom[n], bottom[n] - new Vector2(10, 0) }, c, 2f);
    }
}
