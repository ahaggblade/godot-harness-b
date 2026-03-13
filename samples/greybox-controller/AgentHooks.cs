using Godot;

public partial class AgentHooks : Node
{
    private Main? CurrentMain => GetTree().CurrentScene as Main;

    public Godot.Collections.Dictionary<string, Variant> ResetWorld()
    {
        CurrentMain?.ResetWorld();
        return CurrentMain?.GetSemanticState() ?? new Godot.Collections.Dictionary<string, Variant>();
    }

    public Godot.Collections.Dictionary<string, Variant> GetSemanticState()
    {
        return CurrentMain?.GetSemanticState() ?? new Godot.Collections.Dictionary<string, Variant>();
    }
}
