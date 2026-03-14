using Godot;

public partial class AgentHooks : Node
{
    private Main? CurrentMain => GetTree().CurrentScene as Main;

    public Godot.Collections.Dictionary<string, Variant> ResetMenu()
    {
        return CurrentMain?.ResetMenu() ?? new Godot.Collections.Dictionary<string, Variant>();
    }

    public Godot.Collections.Dictionary<string, Variant> GetMenuState()
    {
        return CurrentMain?.GetMenuState() ?? new Godot.Collections.Dictionary<string, Variant>();
    }
}
