using DreamGame.Core;
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
        _builder = new Builder();
        _builder.TestDriveRequested += bp => Callable.From(() => StartTestDrive(bp)).CallDeferred();
        AddChild(_builder);
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
