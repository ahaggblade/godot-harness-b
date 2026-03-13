using Godot;

public partial class Main : Control
{
    public override void _Ready()
    {
        GD.Print("Managed smoke scene ready.");
        GD.Print($"CLI_ARGS={string.Join(",", OS.GetCmdlineArgs())}");
        GD.Print($"CLI_USER_ARGS={string.Join(",", OS.GetCmdlineUserArgs())}");
    }
}

