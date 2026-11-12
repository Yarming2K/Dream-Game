using System.Collections.Generic;
using Godot;

namespace DreamGame.Game;

/// <summary>Draws the crosshair and where each gun will actually hit.</summary>
public partial class ReticleOverlay : Control
{
    public readonly List<(Vector2 pos, bool ready)> Marks = new();

    /// <summary>1 right after one of your shells gets through, fading to 0: draws the hit marker.</summary>
    public float HitFlash;

    /// <summary>Bombsight: where a bomb dropped now would land. Lock: the aircraft a radar missile would chase.</summary>
    public Vector2? BombMark;
    public Vector2? LockMark;

    /// <summary>
    /// Lead markers: for each enemy aircraft in reach, where it is and where to aim so your rounds meet it (its speed,
    /// yours and the rounds' drop allowed for).
    /// </summary>
    public readonly List<(Vector2 target, Vector2 lead)> Leads = new();

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

        if (BombMark is Vector2 bomb)
        {
            var bc = new Color(1f, 0.82f, 0.35f, 0.95f);
            Vector2[] pts = { bomb + new Vector2(0, -11), bomb + new Vector2(11, 0), bomb + new Vector2(0, 11), bomb + new Vector2(-11, 0) };
            for (int i = 0; i < 4; i++) DrawLine(pts[i], pts[(i + 1) % 4], bc, 2f);
            DrawCircle(bomb, 2f, bc);
        }

        if (LockMark is Vector2 l)
        {
            var lc = new Color(1f, 0.35f, 0.3f, 0.95f);
            DrawRect(new Rect2(l - new Vector2(14, 14), new Vector2(28, 28)), lc, false, 2f);
        }

        foreach ((Vector2 target, Vector2 lead) in Leads)
        {
            // Green when the crosshair is on it: fire now.
            bool on = lead.DistanceTo(c) < 14f;
            var lc = on ? new Color(0.45f, 1f, 0.5f, 0.95f) : new Color(1f, 0.85f, 0.4f, 0.85f);
            Vector2 d = lead - target;
            if (d.Length() > 16f) DrawLine(target + d.Normalized() * 6f, lead - d.Normalized() * 11f, new Color(lc, 0.45f), 1.2f);
            DrawArc(lead, 11f, 0, Mathf.Tau, 32, lc, 2f);
            DrawCircle(lead, 2f, lc);
        }

        foreach ((Vector2 pos, bool ready) in Marks)
        {
            Color m = ready ? new Color(0.45f, 1f, 0.5f, 0.95f) : new Color(1f, 0.6f, 0.3f, 0.9f);
            DrawArc(pos, 9f, 0, Mathf.Tau, 28, m, 2f);
            DrawCircle(pos, 1.6f, m);
        }
    }
}
