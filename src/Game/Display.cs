using Godot;

namespace DreamGame.Game;

/// <summary>
/// Fits the game to the screen. On a big screen (1440p, 4K) the menus and HUD are scaled up so they stay readable,
/// and the 3D world is drawn at about 1080p and sharpened up to the screen (AMD's FSR upscaler): a 4K window has
/// four times the pixels of a 1080p one to light, shade and fog, which costs far more frame rate than the extra
/// sharpness is worth. F4 switches the 3D between that and drawing every pixel.
/// </summary>
public static class Display
{
    /// <summary>Draw the 3D at the screen's full resolution (F4).</summary>
    public static bool FullRes;

    public static float UiScale { get; private set; } = 1f;
    public static float RenderScale { get; private set; } = 1f;

    /// <summary>The size the 3D is drawn at, in pixels.</summary>
    public static Vector2I RenderSize { get; private set; }

    public static void Fit(Window w)
    {
        Vector2I px = w.Size;
        if (px.Y <= 0) return;
        // The HUD was laid out for a 900-1080 line screen.
        float ui = Mathf.Clamp(Mathf.Snapped(px.Y / 1080f, 0.25f), 1f, 3f);
        if (!Mathf.IsEqualApprox(w.ContentScaleFactor, ui)) w.ContentScaleFactor = ui;
        UiScale = ui;

        float scale = FullRes ? 1f : Mathf.Clamp(1150f / px.Y, 0.5f, 1f);
        if (scale > 0.95f) scale = 1f;
        RenderScale = scale;
        w.Scaling3DMode = scale < 1f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        w.Scaling3DScale = scale;
        w.FsrSharpness = 0.25f;
        RenderSize = new Vector2I(Mathf.RoundToInt(px.X * scale), Mathf.RoundToInt(px.Y * scale));
    }

    /// <summary>One line for the F3 overlay.</summary>
    public static string Describe() => RenderScale < 1f
        ? $"3D drawn at {RenderSize.X}×{RenderSize.Y} ({RenderScale * 100:0}%) and sharpened up to the screen · F4 draws every pixel"
        : $"3D drawn at every pixel ({RenderSize.X}×{RenderSize.Y}){(FullRes ? " · F4 goes back to the faster setting" : "")}";
}
