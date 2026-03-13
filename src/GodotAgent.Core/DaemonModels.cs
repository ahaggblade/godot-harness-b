using System.Text.Json.Nodes;

namespace GodotAgent.Core;

public sealed class SessionRunOptions
{
    public required string ProjectPath { get; init; }
    public string? GodotPath { get; init; }
    public string? Scene { get; init; }
    public bool Headless { get; init; }
    public bool Json { get; init; }
}

public sealed class SessionControlOptions
{
    public required string ProjectPath { get; init; }
    public string? SessionId { get; init; }
    public bool Json { get; init; }
}

public sealed class InputRequest
{
    public required string Kind { get; init; }
    public JsonObject Payload { get; init; } = new();
}

public sealed class InspectRequest
{
    public required string Kind { get; init; }
    public JsonObject Payload { get; init; } = new();
}

public sealed class DaemonLaunchArguments
{
    public required string ProjectPath { get; init; }
    public required string SessionId { get; init; }
    public required string Token { get; init; }
    public string? GodotPath { get; init; }
    public string? Scene { get; init; }
    public bool Headless { get; init; }
}

