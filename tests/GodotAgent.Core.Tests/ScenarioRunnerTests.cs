using System.Text.Json.Nodes;
using GodotAgent.Core;

namespace GodotAgent.Core.Tests;

public sealed class ScenarioRunnerTests
{
    [Fact]
    public void ResolvePath_FindsNestedProperties()
    {
        var payload = JsonNode.Parse("""
        {
          "focused": {
            "path": "/root/Main/Menu/StartButton"
          }
        }
        """);

        var node = ScenarioRunner.ResolvePath(payload!, "focused.path");

        Assert.Equal("/root/Main/Menu/StartButton", node!.GetValue<string>());
    }

    [Fact]
    public void ScenarioStep_Get_ReturnsFallbackWhenMissing()
    {
        var step = new ScenarioStep
        {
            Type = "wait",
        };

        Assert.Equal(250, step.Get("milliseconds", 250));
    }
}

