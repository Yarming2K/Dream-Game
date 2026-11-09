using Godot;

namespace DreamGame.Game;

/// <summary>Keyboard and mouse bindings, registered in code so they're easy to read and change.</summary>
public static class GameInput
{
    public static void Register()
    {
        AddKeys("move_forward", Key.W, Key.Up);
        AddKeys("move_back", Key.S, Key.Down);
        AddKeys("steer_left", Key.A, Key.Left);
        AddKeys("steer_right", Key.D, Key.Right);
        AddKeys("brake", Key.Space);
        AddMouse("fire", MouseButton.Left);
        AddMouse("zoom", MouseButton.Right);
        AddKeys("toggle_ammo", Key.Q);
        AddKeys("recover", Key.R);
        AddKeys("respawn", Key.Backspace);
        AddKeys("reset_targets", Key.T);
        AddKeys("to_builder", Key.Tab);
        AddKeys("help", Key.F1);
    }

    private static void AddKeys(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (Key k in keys) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
    }

    private static void AddMouse(string action, MouseButton button)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}
