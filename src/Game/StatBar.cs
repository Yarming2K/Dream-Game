using Godot;

namespace DreamGame.Game;

/// <summary>A slim horizontal meter, with an optional tick mark (e.g. the weakest armor spot).</summary>
public partial class StatBar : Control
{
    private float _value;
    private float _max = 1f;
    private float _marker = -1f;
    private Color _fill = Ui.Accent;

    public StatBar()
    {
        CustomMinimumSize = new Vector2(40, 8);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(float value, float max, Color fill, float marker = -1f)
    {
        _value = value;
        _max = Mathf.Max(max, 0.0001f);
        _fill = fill;
        _marker = marker;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var full = new Rect2(Vector2.Zero, Size);
        DrawRect(full, new Color(1, 1, 1, 0.07f));
        float k = Mathf.Clamp(_value / _max, 0, 1);
        if (k > 0) DrawRect(new Rect2(0, 0, Size.X * k, Size.Y), _fill);
        if (_marker >= 0)
        {
            float x = Size.X * Mathf.Clamp(_marker / _max, 0, 1);
            DrawRect(new Rect2(x - 1, -2, 2, Size.Y + 4), new Color(1, 1, 1, 0.85f));
        }
    }
}
