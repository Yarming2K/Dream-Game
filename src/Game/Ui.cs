using System;
using Godot;

namespace DreamGame.Game;

/// <summary>Shared look for menus and HUDs, plus small helpers for building UI in code.</summary>
public static class Ui
{
    public static readonly Color Text = new(0.92f, 0.93f, 0.9f);
    public static readonly Color Dim = new(0.65f, 0.68f, 0.66f);
    public static readonly Color Accent = new(1f, 0.78f, 0.3f);
    public static readonly Color Good = new(0.45f, 0.95f, 0.5f);
    public static readonly Color Bad = new(1f, 0.42f, 0.38f);
    public static readonly Color Warn = new(1f, 0.8f, 0.4f);

    private static Theme _theme;

    public static Theme Theme => _theme ??= BuildTheme();

    public static StyleBoxFlat PanelStyle(float alpha = 0.82f)
    {
        var sb = new StyleBoxFlat { BgColor = new Color(0.07f, 0.08f, 0.085f, alpha) };
        sb.SetCornerRadiusAll(6);
        sb.SetContentMarginAll(10);
        sb.BorderColor = new Color(1f, 1f, 1f, 0.06f);
        sb.SetBorderWidthAll(1);
        return sb;
    }

    private static Theme BuildTheme()
    {
        var t = new Theme { DefaultFontSize = 15 };
        t.SetStylebox("panel", "PanelContainer", PanelStyle());
        t.SetColor("font_color", "Label", Text);
        t.SetColor("default_color", "RichTextLabel", Text);
        return t;
    }

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

    public static Label Label(string text, int size = 15, Color? color = null)
    {
        var l = new Label { Text = text };
        if (size != 15) l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    public static Button Button(string text, Action pressed, string tooltip = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "", FocusMode = Control.FocusModeEnum.None };
        if (pressed != null) b.Pressed += pressed;
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
}
