using Godot;

public partial class Main : Node2D
{
    [Export]
    public Player? Player { get; set; }

    public override void _Ready()
    {
        Player ??= GetNodeOrNull<Player>("Player");
        EnsureInputAction("move_left", Key.A, Key.Left);
        EnsureInputAction("move_right", Key.D, Key.Right);
        EnsureInputAction("jump", Key.Space, Key.W, Key.Up);
    }

    public void ResetWorld()
    {
        Player?.ResetToSpawn();
        GetViewport().GetCamera2D()?.ResetSmoothing();
    }

    public Godot.Collections.Dictionary<string, Variant> GetSemanticState()
    {
        return Player?.GetSemanticState() ?? new Godot.Collections.Dictionary<string, Variant>();
    }

    private static void EnsureInputAction(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        foreach (var key in keys)
        {
            var inputEvent = new InputEventKey
            {
                Keycode = key,
            };

            if (!InputMap.ActionHasEvent(action, inputEvent))
            {
                InputMap.ActionAddEvent(action, inputEvent);
            }
        }
    }
}
