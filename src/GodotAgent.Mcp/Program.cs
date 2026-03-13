using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAgent.Core;

var server = new McpServer();
await server.RunAsync(CancellationToken.None);

internal sealed class McpServer
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string? line;
        while ((line = await Console.In.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var request = JsonNode.Parse(line)?.AsObject() ?? new JsonObject();
            var response = await HandleAsync(request, cancellationToken);
            Console.Out.WriteLine(response.ToJsonString(JsonDefaults.Options));
            await Console.Out.FlushAsync();
        }
    }

    private async Task<JsonObject> HandleAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var method = request["method"]?.GetValue<string>();
        var id = request["id"]?.DeepClone();
        return method switch
        {
            "initialize" => Rpc(id, new JsonObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["serverInfo"] = new JsonObject { ["name"] = "godot-agent-mcp", ["version"] = "0.1.0" },
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            }),
            "tools/list" => Rpc(id, BuildToolList()),
            "tools/call" => Rpc(id, await CallToolAsync(request["params"]?.AsObject() ?? new JsonObject(), cancellationToken)),
            _ => RpcError(id, -32601, $"Unknown method '{method}'."),
        };
    }

    private async Task<JsonObject> CallToolAsync(JsonObject parameters, CancellationToken cancellationToken)
    {
        var name = parameters["name"]?.GetValue<string>() ?? throw new InvalidOperationException("Tool call missing name.");
        var arguments = parameters["arguments"]?.AsObject() ?? new JsonObject();
        var projectPath = arguments["projectPath"]?.GetValue<string>() ?? Environment.CurrentDirectory;
        var layout = new WorkspaceLayout(projectPath);
        var manifest = layout.LoadManifest(arguments["sessionId"]?.GetValue<string>()) ?? layout.LoadManifest();
        var client = new BridgeClient();

        JsonNode payload = name switch
        {
            "godot_session_status" => JsonSerializer.SerializeToNode(manifest, JsonDefaults.Options) ?? new JsonObject(),
            "godot_capture_screenshot" => await client.SendAsync(manifest ?? throw new InvalidOperationException("No active session."), "capture.screenshot", new JsonObject
            {
                ["label"] = arguments["label"]?.GetValue<string>() ?? "mcp",
            }, cancellationToken),
            "godot_inspect_scene" => await client.SendAsync(manifest ?? throw new InvalidOperationException("No active session."), "inspect.scene", new JsonObject(), cancellationToken),
            "godot_inspect_focus" => await client.SendAsync(manifest ?? throw new InvalidOperationException("No active session."), "inspect.focus", new JsonObject(), cancellationToken),
            "godot_input_action" => await client.SendAsync(manifest ?? throw new InvalidOperationException("No active session."), "input.action", new JsonObject
            {
                ["action"] = arguments["action"]?.GetValue<string>(),
                ["pressed"] = arguments["pressed"]?.GetValue<bool>() ?? true,
            }, cancellationToken),
            "godot_validate_run" => await RunValidationAsync(arguments, manifest ?? throw new InvalidOperationException("No active session."), cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported MCP tool '{name}'."),
        };

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = payload.ToJsonString(JsonDefaults.Options),
                },
            },
        };
    }

    private static async Task<JsonNode> RunValidationAsync(JsonObject arguments, SessionManifest manifest, CancellationToken cancellationToken)
    {
        var runner = new ScenarioRunner();
        var (report, artifact) = await runner.RunAsync(manifest, arguments["scenarioPath"]?.GetValue<string>() ?? throw new InvalidOperationException("scenarioPath is required."), cancellationToken);
        return new JsonObject
        {
            ["passed"] = report.Passed,
            ["reportPath"] = artifact.Path,
            ["steps"] = JsonSerializer.SerializeToNode(report.Steps, JsonDefaults.Options),
        };
    }

    private static JsonObject BuildToolList() =>
        new()
        {
            ["tools"] = new JsonArray
            {
                Tool("godot_session_status", "Return the current session manifest for a Godot project."),
                Tool("godot_capture_screenshot", "Capture a screenshot artifact from the running Godot game."),
                Tool("godot_inspect_scene", "Inspect the current scene and scene tree."),
                Tool("godot_inspect_focus", "Inspect the focused control and UI state."),
                Tool("godot_input_action", "Simulate a named Godot input action."),
                Tool("godot_validate_run", "Run a declarative validation scenario and return the report path."),
            },
        };

    private static JsonObject Tool(string name, string description) =>
        new()
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["projectPath"] = new JsonObject { ["type"] = "string" },
                    ["sessionId"] = new JsonObject { ["type"] = "string" },
                },
            },
        };

    private static JsonObject Rpc(JsonNode? id, JsonObject result) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        };

    private static JsonObject RpcError(JsonNode? id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
}
