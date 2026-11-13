using System;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The game's look: fonts, colors and styles for every kind of control, plus small helpers for building
/// UI in code. Everything goes through one theme so screens stay consistent.
/// </summary>
public static class Ui
{
    // Tactical Glass: smoked dark glass, thin cyan lines, near-white text; amber is kept for warnings.
    public static readonly Color Text = new(0.91f, 0.96f, 0.98f);
    public static readonly Color Dim = new(0.6f, 0.76f, 0.82f);
    public static readonly Color Faint = new(0.4f, 0.5f, 0.55f);
    public static readonly Color Accent = new(0.5f, 0.89f, 1f);
    public static readonly Color AccentBright = new(0.78f, 0.96f, 1f);
    public static readonly Color AccentDark = new(0.16f, 0.4f, 0.48f);
    public static readonly Color Good = new(0.43f, 1f, 0.66f);
    public static readonly Color Bad = new(1f, 0.36f, 0.31f);
    public static readonly Color Warn = new(1f, 0.71f, 0.28f);
    public static readonly Color Info = new(0.5f, 0.89f, 1f);
    public static readonly Color Ink = new(0.02f, 0.07f, 0.1f);

    private static readonly Color PanelBg = new(0.03f, 0.055f, 0.08f, 0.72f);
    private static readonly Color CardBg = new(0.5f, 0.89f, 1f, 0.05f);
    private static readonly Color FieldBg = new(0.02f, 0.04f, 0.06f, 0.85f);
    private static readonly Color ButtonBg = new(0.5f, 0.89f, 1f, 0.06f);
    private static readonly Color ButtonHover = new(0.5f, 0.89f, 1f, 0.14f);
    private static readonly Color Line = new(0.5f, 0.89f, 1f, 0.26f);

    private static Theme _theme;
    private static Font _body, _bodyBold, _heading, _headingBold, _caption;

    public static Font BodyFont => _body ??= LoadFont("res://assets/fonts/barlow-latin-400-normal.woff2");
    public static Font BodyBoldFont => _bodyBold ??= LoadFont("res://assets/fonts/barlow-latin-600-normal.woff2");
    public static Font HeadingFont => _heading ??= LoadFont("res://assets/fonts/chakra-petch-latin-600-normal.woff2");
    public static Font HeadingBoldFont => _headingBold ??= LoadFont("res://assets/fonts/chakra-petch-latin-700-normal.woff2");

    /// <summary>Small caps-style labels with a little extra letter spacing.</summary>
    public static Font CaptionFont
    {
        get
        {
            if (_caption != null) return _caption;
            var v = new FontVariation { BaseFont = HeadingBoldFont };
            v.SetSpacing(TextServer.SpacingType.Glyph, 1);
            _caption = v;
            return _caption;
        }
    }

    private static Font LoadFont(string path)
    {
        Font f = ResourceLoader.Exists(path) ? GD.Load<Font>(path) : null;
        return f ?? ThemeDB.FallbackFont;
    }

    public static Theme Theme => _theme ??= BuildTheme();

    public static StyleBoxFlat Box(Color bg, int radius = 6, float margin = 10, Color? border = null, int borderWidth = 1,
        float marginY = -1)
    {
        var sb = new StyleBoxFlat { BgColor = bg, AntiAliasing = true };
        // Glass corners: the top-right and bottom-left cut off at 45°, the other two square.
        sb.CornerDetail = 1;
        sb.CornerRadiusTopLeft = sb.CornerRadiusBottomRight = 0;
        sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = radius > 10 ? radius : radius * 2;
        sb.ContentMarginLeft = sb.ContentMarginRight = margin;
        sb.ContentMarginTop = sb.ContentMarginBottom = marginY < 0 ? margin : marginY;
        if (border.HasValue)
        {
            sb.BorderColor = border.Value;
            sb.SetBorderWidthAll(borderWidth);
        }

        return sb;
    }

    private static void ButtonStyle(Theme t, string type, StyleBox normal, StyleBox hover, StyleBox pressed, Color text,
        Color hoverText, Color pressedText)
    {
        t.SetStylebox("normal", type, normal);
        t.SetStylebox("hover", type, hover);
        t.SetStylebox("pressed", type, pressed);
        t.SetStylebox("hover_pressed", type, pressed);
        t.SetStylebox("disabled", type, normal);
        t.SetStylebox("focus", type, new StyleBoxEmpty());
        t.SetColor("font_color", type, text);
        t.SetColor("font_hover_color", type, hoverText);
        t.SetColor("font_pressed_color", type, pressedText);
        t.SetColor("font_hover_pressed_color", type, pressedText);
        t.SetColor("font_focus_color", type, text);
        t.SetColor("font_disabled_color", type, Faint);
    }

    private static Theme BuildTheme()
    {
        var t = new Theme { DefaultFont = BodyFont, DefaultFontSize = 15 };

        t.SetStylebox("panel", "PanelContainer", Box(PanelBg, 7, 12, Line));
        Variation(t, "Card", "PanelContainer");
        t.SetStylebox("panel", "Card", Box(CardBg, 6, 10));
        Variation(t, "Chip", "PanelContainer");
        t.SetStylebox("panel", "Chip", Box(new Color(1, 1, 1, 0.06f), 4, 7, null, 0, 2));
        Variation(t, "TopBar", "PanelContainer");
        StyleBoxFlat top = Box(new Color(0.03f, 0.055f, 0.08f, 0.82f), 0, 12, null, 0, 8);
        top.BorderColor = new Color(Accent, 0.35f);
        top.BorderWidthBottom = 1;
        t.SetStylebox("panel", "TopBar", top);
        Variation(t, "Toast", "PanelContainer");
        t.SetStylebox("panel", "Toast", Box(new Color(0.08f, 0.085f, 0.09f, 0.96f), 18, 18, new Color(Accent, 0.6f), 1, 8));

        // Buttons
        ButtonStyle(t, "Button",
            Box(ButtonBg, 4, 12, Line, 1, 6), Box(ButtonHover, 4, 12, new Color(Accent, 0.6f), 1, 6),
            Box(new Color(Accent, 0.2f), 4, 12, Accent, 1, 6), Text, Colors.White, AccentBright);
        t.SetFont("font", "Button", HeadingFont);

        Variation(t, "PrimaryButton", "Button");
        ButtonStyle(t, "PrimaryButton", Box(Accent, 5, 18, null, 0, 7), Box(AccentBright, 5, 18, null, 0, 7),
            Box(AccentDark, 5, 18, null, 0, 7), Ink, Ink, Colors.White);
        t.SetFont("font", "PrimaryButton", HeadingBoldFont);
        t.SetFontSize("font_size", "PrimaryButton", 16);

        Variation(t, "Pill", "Button");
        ButtonStyle(t, "Pill", Box(new Color(Accent, 0.04f), 4, 10, Line, 1, 4), Box(new Color(Accent, 0.12f), 4, 10, new Color(Accent, 0.6f), 1, 4),
            Box(new Color(Accent, 0.2f), 4, 10, Accent, 1, 4), Dim, Text, AccentBright);
        t.SetFont("font", "Pill", HeadingFont);
        t.SetFontSize("font_size", "Pill", 13);

        Variation(t, "Tile", "Button");
        ButtonStyle(t, "Tile", Box(CardBg, 4, 4, Line), Box(new Color(Accent, 0.12f), 4, 4, new Color(Accent, 0.6f)),
            Box(new Color(Accent, 0.16f), 4, 4, Accent, 2), Text, Colors.White, AccentBright);

        Variation(t, "GhostButton", "Button");
        ButtonStyle(t, "GhostButton", Box(new Color(0, 0, 0, 0), 4, 10, null, 0, 6), Box(new Color(Accent, 0.1f), 4, 10, null, 0, 6),
            Box(new Color(Accent, 0.18f), 4, 10, null, 0, 6), Dim, Text, AccentBright);
        t.SetFont("font", "GhostButton", HeadingFont);

        // Fields, menus, tooltips, scrollbars
        t.SetStylebox("normal", "LineEdit", Box(FieldBg, 5, 10, Line, 1, 6));
        t.SetStylebox("focus", "LineEdit", Box(FieldBg, 5, 10, Accent, 1, 6));
        t.SetColor("font_color", "LineEdit", Text);
        t.SetColor("caret_color", "LineEdit", Accent);
        t.SetColor("selection_color", "LineEdit", new Color(Accent, 0.35f));
        t.SetColor("font_placeholder_color", "LineEdit", Faint);
        t.SetFont("font", "LineEdit", BodyBoldFont);

        t.SetStylebox("panel", "PopupMenu", Box(new Color(0.07f, 0.078f, 0.085f, 0.98f), 6, 6, Line));
        t.SetStylebox("hover", "PopupMenu", Box(new Color(Accent, 0.2f), 4, 8));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Colors.White);
        t.SetColor("font_separator_color", "PopupMenu", Accent);
        t.SetConstant("v_separation", "PopupMenu", 8);

        t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.05f, 0.055f, 0.06f, 0.97f), 5, 10, new Color(Accent, 0.4f), 1, 7));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetFontSize("font_size", "TooltipLabel", 14);

        StyleBoxFlat grab = Box(new Color(1, 1, 1, 0.16f), 3, 0);
        grab.ContentMarginLeft = grab.ContentMarginRight = 3;
        t.SetStylebox("scroll", "VScrollBar", Box(new Color(0, 0, 0, 0), 3, 0));
        t.SetStylebox("grabber", "VScrollBar", grab);
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(new Color(1, 1, 1, 0.3f), 3, 3));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(new Color(Accent, 0.6f), 3, 3));

        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Line, Thickness = 1 });

        // Text
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.85f));
        Variation(t, "Title", "Label");
        t.SetFont("font", "Title", HeadingBoldFont);
        t.SetFontSize("font_size", "Title", 22);
        Variation(t, "Heading", "Label");
        t.SetFont("font", "Heading", HeadingFont);
        t.SetFontSize("font_size", "Heading", 17);
        Variation(t, "Caption", "Label");
        t.SetFont("font", "Caption", CaptionFont);
        t.SetFontSize("font_size", "Caption", 12);
        t.SetColor("font_color", "Caption", Dim);
        Variation(t, "Value", "Label");
        t.SetFont("font", "Value", HeadingBoldFont);
        t.SetFontSize("font_size", "Value", 22);
        Variation(t, "Small", "Label");
        t.SetFontSize("font_size", "Small", 13);
        t.SetColor("font_color", "Small", Dim);

        t.SetFont("normal_font", "RichTextLabel", BodyFont);
        t.SetFont("bold_font", "RichTextLabel", BodyBoldFont);
        t.SetColor("default_color", "RichTextLabel", Text);
        return t;
    }

    private static void Variation(Theme t, string name, string baseType) => t.SetTypeVariation(name, baseType);

    /// <summary>A full-screen UI root on its own layer that lets mouse clicks through to the 3D view.</summary>
    public static Control Root(Node owner, int layer = 1)
    {
        var canvas = new CanvasLayer { Layer = layer };
        owner.AddChild(canvas);
        var root = new Control { Theme = Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        canvas.AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return root;
    }

    public static Label Label(string text, int size = 15, Color? color = null, string variant = null)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (variant != null) l.ThemeTypeVariation = variant;
        if (size != 15) l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    public static Label Title(string text) => Label(text, 15, null, "Title");
    public static Label Heading(string text, Color? color = null) => Label(text, 15, color, "Heading");
    public static Label Caption(string text, Color? color = null) => Label(text.ToUpperInvariant(), 15, color, "Caption");
    public static Label Small(string text, Color? color = null) => Label(text, 15, color, "Small");

    public static Label Value(string text, Color? color = null) => Label(text, 15, color, "Value");

    public static Label Wrapped(string text, int size = 14, Color? color = null)
    {
        Label l = Label(text, size, color);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(10, 0);
        return l;
    }

    public static Button Button(string text, Action pressed, string tooltip = null, string variant = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "", FocusMode = Control.FocusModeEnum.None };
        if (variant != null) b.ThemeTypeVariation = variant;
        if (pressed != null) b.Pressed += pressed;
        return b;
    }

    public static PanelContainer Card(string variant = "Card") =>
        new() { ThemeTypeVariation = variant, MouseFilter = Control.MouseFilterEnum.Pass };

    /// <summary>A little rounded tag, like "PEN 191 mm".</summary>
    public static PanelContainer Chip(string caption, string value, Color? color = null)
    {
        PanelContainer chip = Card("Chip");
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 5);
        chip.AddChild(row);
        if (!string.IsNullOrEmpty(caption)) row.AddChild(Caption(caption));
        Label v = Label(value, 14, color ?? Text);
        v.AddThemeFontOverride("font", BodyBoldFont);
        row.AddChild(v);
        return chip;
    }

    /// <summary>A section title with an accent tick on the left.</summary>
    public static Control Section(string text)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 7);
        row.AddChild(new ColorRect { Color = Accent, CustomMinimumSize = new Vector2(3, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        row.AddChild(Caption(text, Text));
        return row;
    }

    public static VBoxContainer Column(int separation = 6) => Box<VBoxContainer>(separation);
    public static HBoxContainer Row(int separation = 6) => Box<HBoxContainer>(separation);

    private static T Box<T>(int separation) where T : BoxContainer, new()
    {
        var b = new T { MouseFilter = Control.MouseFilterEnum.Ignore };
        b.AddThemeConstantOverride("separation", separation);
        return b;
    }

    public static RichTextLabel Rich(bool fit = true)
    {
        return new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = fit,
            ScrollActive = !fit,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
    }

    public static Control Spacer() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static string Hex(Color c) => "#" + c.ToHtml(false);

    /// <summary>Green, yellow or red depending on where a value sits against two thresholds.</summary>
    public static Color Grade(float value, float bad, float ok) => value < bad ? Bad : value < ok ? Warn : Good;
}
