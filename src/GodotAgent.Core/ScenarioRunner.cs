using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotAgent.Core;

public sealed class ScenarioRunner
{
    private readonly BridgeClient _bridgeClient = new();

    public async Task<(ValidationReport report, ArtifactRef artifact)> RunAsync(SessionManifest manifest, string scenarioPath, CancellationToken cancellationToken)
    {
        var layout = new WorkspaceLayout(manifest.ProjectPath);
        var resolvedScenarioPath = layout.ResolveScenarioPath(scenarioPath);
        var document = JsonDefaults.Deserialize<ScenarioDocument>(await File.ReadAllTextAsync(resolvedScenarioPath, cancellationToken))
            ?? throw new InvalidOperationException($"Scenario '{resolvedScenarioPath}' could not be parsed.");

        var report = new ValidationReport
        {
            ScenarioName = document.Name,
            StartedAtUtc = DateTimeOffset.UtcNow,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Passed = true,
        };

        JsonNode? lastPayload = null;

        for (var index = 0; index < document.Steps.Count; index++)
        {
            var step = document.Steps[index];
            var result = new ValidationStepResult
            {
                Index = index,
                Type = step.Type,
                Success = true,
            };

            try
            {
                switch (step.Type)
                {
                    case "wait":
                        await Task.Delay(step.Get("milliseconds", 1000), cancellationToken);
                        result.Message = "wait completed";
                        break;
                    case "input":
                        lastPayload = await RunInputStepAsync(manifest, step, cancellationToken);
                        result.Payload = lastPayload;
                        break;
                    case "capture":
                        lastPayload = await _bridgeClient.SendAsync(manifest, "capture.screenshot", new JsonObject
                        {
                            ["label"] = step.Get("label", "capture"),
                        }, cancellationToken);
                        result.Payload = lastPayload;
                        break;
                    case "inspect":
                        lastPayload = await _bridgeClient.SendAsync(manifest, $"inspect.{step.Get("target", "scene")}", BuildInspectPayload(step), cancellationToken);
                        result.Payload = lastPayload;
                        break;
                    case "hook":
                        lastPayload = await _bridgeClient.SendAsync(manifest, "hook.invoke", new JsonObject
                        {
                            ["method"] = step.Get<string>("method"),
                            ["arguments"] = JsonNode.Parse(step.Arguments.TryGetValue("arguments", out var value) ? value.GetRawText() : "[]"),
                        }, cancellationToken);
                        result.Payload = lastPayload;
                        break;
                    case "assert":
                        AssertStep(step, lastPayload);
                        result.Payload = lastPayload?.DeepClone();
                        result.Message = "assertion passed";
                        break;
                    case "quit":
                        lastPayload = await _bridgeClient.SendAsync(manifest, "runtime.quit", new JsonObject(), cancellationToken);
                        result.Payload = lastPayload;
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported scenario step '{step.Type}'.");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                report.Passed = false;
            }

            report.Steps.Add(result);
            if (!result.Success)
            {
                break;
            }
        }

        report.FinishedAtUtc = DateTimeOffset.UtcNow;
        var artifact = layout.WriteJsonArtifact("validation-report", JsonSerializer.SerializeToNode(report, JsonDefaults.Options) ?? new JsonObject());
        return (report, artifact);
    }

    private async Task<JsonNode> RunInputStepAsync(SessionManifest manifest, ScenarioStep step, CancellationToken cancellationToken)
    {
        var kind = step.Get("kind", "action");
        var method = $"input.{kind}";
        var payload = new JsonObject();
        foreach (var (key, value) in step.Arguments)
        {
            if (string.Equals(key, "type", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "kind", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            payload[key] = JsonNode.Parse(value.GetRawText());
        }

        return await _bridgeClient.SendAsync(manifest, method, payload, cancellationToken);
    }

    private static JsonObject BuildInspectPayload(ScenarioStep step)
    {
        var payload = new JsonObject();
        foreach (var (key, value) in step.Arguments)
        {
            if (string.Equals(key, "type", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "target", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            payload[key] = JsonNode.Parse(value.GetRawText());
        }

        return payload;
    }

    private static void AssertStep(ScenarioStep step, JsonNode? payload)
    {
        if (payload is null)
        {
            throw new InvalidOperationException("Assertion step requires a prior payload.");
        }

        var path = step.Get<string>("path") ?? throw new InvalidOperationException("Assertion requires a path.");
        var expected = step.Arguments.TryGetValue("equals", out var expectedValue)
            ? JsonNode.Parse(expectedValue.GetRawText())
            : throw new InvalidOperationException("Assertion requires an equals value.");

        var actual = ResolvePath(payload, path);
        if (!JsonNode.DeepEquals(actual, expected))
        {
            throw new InvalidOperationException($"Assertion failed for path '{path}'. Expected '{expected}', actual '{actual}'.");
        }
    }

    public static JsonNode? ResolvePath(JsonNode payload, string path)
    {
        var current = payload;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            current = current switch
            {
                JsonObject obj when obj.TryGetPropertyValue(part, out var next) => next,
                JsonArray arr when int.TryParse(part, out var index) && index >= 0 && index < arr.Count => arr[index],
                _ => null,
            };

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }
}

