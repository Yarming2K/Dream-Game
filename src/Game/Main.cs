using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>Starts the game in the builder and switches between building and the test range.</summary>
public partial class Main : Node
{
    private Builder _builder;
    private TestRange _range;

    public override void _Ready()
    {
        GameInput.Register();
        Sfx.SetupBuses();
        Bench.Parse(OS.GetCmdlineUserArgs());
        Display.FullRes = Bench.FullRes;
        Window w = GetWindow();
        Display.Fit(w);
        w.SizeChanged += () => Display.Fit(w);
        if (Bench.Active)
        {
            // A benchmark run goes straight to the range and quits when it's done.
            _range = new TestRange();
            _range.Setup(Bench.Design());
            _range.ExitRequested += () => GetTree().Quit();
            AddChild(_range);
            return;
        }

        _builder = new Builder();
        _builder.TestDriveRequested += bp => Callable.From(() => StartTestDrive(bp)).CallDeferred();
        AddChild(_builder);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("render_scale")) return;
        Display.FullRes = !Display.FullRes;
        Display.Fit(GetWindow());
        GetViewport().SetInputAsHandled();
    }

    private void StartTestDrive(Blueprint bp)
    {
        if (_range != null) return;
        RemoveChild(_builder);
        _range = new TestRange();
        _range.Setup(bp);
        _range.ExitRequested += () => Callable.From(BackToBuilder).CallDeferred();
        AddChild(_range);
    }

    private void BackToBuilder()
    {
        if (_range == null) return;
        RemoveChild(_range);
        _range.QueueFree();
        _range = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        AddChild(_builder);
    }
}
