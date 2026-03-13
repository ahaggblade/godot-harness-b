using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GodotAgent.Core;

public sealed class ArtifactRef
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Path { get; init; }
    public required string RelativePath { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class CommandResult
{
    public required bool Success { get; init; }
    public required string Command { get; init; }
    public string? Message { get; init; }
    public string? SessionId { get; init; }
    public int ExitCode { get; init; }
    public JsonNode? Data { get; init; }
    public IReadOnlyList<ArtifactRef> Artifacts { get; init; } = Array.Empty<ArtifactRef>();
}

public sealed class SessionManifest
{
    public required string SessionId { get; init; }
    public required string ProjectPath { get; init; }
    public required string State { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; set; }
    public int DaemonPid { get; set; }
    public int? GodotPid { get; set; }
    public int? Port { get; set; }
    public string? Token { get; set; }
    public string? GodotPath { get; set; }
    public string? Scene { get; set; }
    public bool RuntimeConnected { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? CurrentScene { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, string> ArtifactIds { get; init; } = new(StringComparer.Ordinal);
    public JsonObject Metadata { get; init; } = new();
}

public sealed class BridgeEnvelope
{
    public required string Type { get; init; }
    public string? Role { get; init; }
    public string? SessionId { get; init; }
    public string? Token { get; init; }
    public string? Id { get; init; }
    public string? Method { get; init; }
    public JsonObject? Params { get; init; }
    public JsonNode? Result { get; init; }
    public BridgeError? Error { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class BridgeError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

public sealed class ScenarioDocument
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required List<ScenarioStep> Steps { get; init; }
}

public sealed class ScenarioStep
{
    public required string Type { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Arguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public T? Get<T>(string key, T? fallback = default)
    {
        if (!Arguments.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Deserialize<T>(JsonDefaults.Options) ?? fallback;
    }
}

public sealed class ValidationReport
{
    public required string ScenarioName { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; set; }
    public bool Passed { get; set; }
    public List<ValidationStepResult> Steps { get; init; } = new();
}

public sealed class ValidationStepResult
{
    public required int Index { get; init; }
    public required string Type { get; init; }
    public required bool Success { get; set; }
    public string? Message { get; set; }
    public JsonNode? Payload { get; set; }
}

