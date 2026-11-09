using System.Collections.Generic;
using Godot;

namespace DreamGame.Game;

/// <summary>Draws the crosshair and where each gun will actually hit.</summary>
public partial class ReticleOverlay : Control
{
    public readonly List<(Vector2 pos, bool ready)> Marks = new();

    /// <summary>1 right after one of your shells gets through, fading to 0: draws the hit marker.</summary>
    public float HitFlash;

    public override void _Draw()
    {
        Vector2 c = Size / 2f;
        var col = new Color(1, 1, 1, 0.85f);
        const float gap = 6f, len = 10f;
        DrawLine(c + new Vector2(-gap - len, 0), c + new Vector2(-gap, 0), col, 2f);
        DrawLine(c + new Vector2(gap, 0), c + new Vector2(gap + len, 0), col, 2f);
        DrawLine(c + new Vector2(0, -gap - len), c + new Vector2(0, -gap), col, 2f);
        DrawLine(c + new Vector2(0, gap), c + new Vector2(0, gap + len), col, 2f);
        if (HitFlash > 0)
        {
            var hc = new Color(1f, 0.35f, 0.25f, HitFlash);
            float a = 9f + 6f * (1f - HitFlash), b = a + 8f;
            foreach (Vector2 d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                DrawLine(c + d.Normalized() * a, c + d.Normalized() * b, hc, 3f);
        }

        foreach ((Vector2 pos, bool ready) in Marks)
        {
            Color m = ready ? new Color(0.45f, 1f, 0.5f, 0.95f) : new Color(1f, 0.6f, 0.3f, 0.9f);
            DrawArc(pos, 9f, 0, Mathf.Tau, 28, m, 2f);
            DrawCircle(pos, 1.6f, m);
        }
    }
}
