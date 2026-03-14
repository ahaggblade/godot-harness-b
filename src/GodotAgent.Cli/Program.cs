using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAgent.Core;

var exitCode = await CliApp.RunAsync(args, CancellationToken.None);
return exitCode;

internal static class CliApp
{
    private static readonly HashSet<string> RuntimeDependentMethods = new(StringComparer.Ordinal)
    {
        "capture.screenshot",
        "inspect.scene",
        "inspect.node",
        "inspect.focus",
        "inspect.hover",
        "inspect.monitors",
        "input.action",
        "input.key",
        "input.mouse",
        "hook.invoke",
        "runtime.quit",
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage();
            return 0;
        }

        try
        {
            return await DispatchAsync(args, cancellationToken);
        }
        catch (Exception ex)
        {
            PrintResult(new CommandResult
            {
                Success = false,
                Command = string.Join(' ', args),
                ExitCode = 1,
                Message = ex.Message,
                Data = new JsonObject { ["exception"] = ex.ToString() },
            }, args.Contains("--json", StringComparer.Ordinal));
            return 1;
        }
    }

    private static async Task<int> DispatchAsync(string[] args, CancellationToken cancellationToken)
    {
        return args[0] switch
        {
            "session" => await HandleSessionAsync(args[1..], cancellationToken),
            "capture" => await HandleCaptureAsync(args[1..], cancellationToken),
            "input" => await HandleInputAsync(args[1..], cancellationToken),
            "inspect" => await HandleInspectAsync(args[1..], cancellationToken),
            "test" => await HandleTestAsync(args[1..], cancellationToken),
            "validate" => await HandleValidateAsync(args[1..], cancellationToken),
            "internal" => await HandleInternalAsync(args[1..], cancellationToken),
            _ => throw new InvalidOperationException($"Unknown command '{args[0]}'."),
        };
    }

    private static async Task<int> HandleSessionAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new InvalidOperationException("Missing session subcommand.");
        }

        var options = ParseOptions(args[1..]);
        var projectPath = options.GetValueOrDefault("project") ?? Environment.CurrentDirectory;
        var layout = new WorkspaceLayout(projectPath);
        var json = options.ContainsKey("json");

        switch (args[0])
        {
            case "run":
                return await RunSessionAsync(new SessionRunOptions
                {
                    ProjectPath = projectPath,
                    GodotPath = options.GetValueOrDefault("godot"),
                    Scene = options.GetValueOrDefault("scene"),
                    Headless = options.ContainsKey("headless"),
                    Json = json,
                }, cancellationToken);
            case "status":
                return PrintFileBackedResult("session status", layout.LoadManifest(options.GetValueOrDefault("session-id")), json);
            case "logs":
                return PrintLogs(layout.LoadManifest(options.GetValueOrDefault("session-id")), json);
            case "stop":
                return await RunDaemonCommandAsync(projectPath, options.GetValueOrDefault("session-id"), "session.stop", new JsonObject(), "session stop", json, cancellationToken);
            case "restart":
                var existing = layout.LoadManifest(options.GetValueOrDefault("session-id"));
                if (existing is not null && existing.State is not "stopped")
                {
                    await RunDaemonCommandAsync(projectPath, existing.SessionId, "session.stop", new JsonObject(), "session stop", true, cancellationToken);
                }

                return await RunSessionAsync(new SessionRunOptions
                {
                    ProjectPath = projectPath,
                    GodotPath = options.GetValueOrDefault("godot") ?? existing?.GodotPath,
                    Scene = options.GetValueOrDefault("scene") ?? existing?.Scene,
                    Headless = options.ContainsKey("headless"),
                    Json = json,
                }, cancellationToken);
            default:
                throw new InvalidOperationException($"Unknown session subcommand '{args[0]}'.");
        }
    }

    private static async Task<int> RunSessionAsync(SessionRunOptions options, CancellationToken cancellationToken)
    {
        var layout = new WorkspaceLayout(options.ProjectPath);
        layout.Ensure();
        var existing = layout.LoadManifest();
        if (existing is not null && existing.State is "running" or "waiting_for_runtime")
        {
            if (IsDaemonAlive(existing.DaemonPid))
            {
                PrintResult(new CommandResult
                {
                    Success = false,
                    Command = "session run",
                    ExitCode = 1,
                    Message = $"Session '{existing.SessionId}' is already active.",
                    SessionId = existing.SessionId,
                    Data = JsonSerializer.SerializeToNode(existing, JsonDefaults.Options),
                }, options.Json);
                return 1;
            }

            ForceStopStaleSession(layout, existing);
        }

        var launch = new DaemonLaunchArguments
        {
            ProjectPath = layout.ProjectPath,
            SessionId = $"session-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".ToLowerInvariant(),
            Token = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant(),
            GodotPath = options.GodotPath,
            Scene = options.Scene,
            Headless = options.Headless,
        };

        var (fileName, arguments) = GodotSessionDaemon.BuildRespawnCommand(launch);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = layout.ProjectPath,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the background daemon.");

        SessionManifest? manifest = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(250, cancellationToken);
            manifest = layout.LoadManifest(launch.SessionId);
            if (manifest is not null && manifest.Port is not null)
            {
                break;
            }
        }

        if (manifest is null)
        {
            PrintResult(new CommandResult
            {
                Success = false,
                Command = "session run",
                ExitCode = 1,
                Message = "The daemon did not create a session manifest in time.",
                Data = new JsonObject { ["artifactRoot"] = layout.RootPath },
            }, options.Json);
            return 1;
        }

        layout.SetActiveSession(manifest.SessionId);
        PrintResult(new CommandResult
        {
            Success = true,
            Command = "session run",
            ExitCode = 0,
            Message = "Session started.",
            SessionId = manifest.SessionId,
            Data = JsonSerializer.SerializeToNode(manifest, JsonDefaults.Options),
        }, options.Json);
        return 0;
    }

    private static async Task<int> HandleCaptureAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] != "screenshot")
        {
            throw new InvalidOperationException("Usage: capture screenshot [--project PATH] [--json]");
        }

        var options = ParseOptions(args[1..]);
        return await RunDaemonCommandAsync(
            options.GetValueOrDefault("project") ?? Environment.CurrentDirectory,
            options.GetValueOrDefault("session-id"),
            "capture.screenshot",
            new JsonObject { ["label"] = options.GetValueOrDefault("label") ?? "capture" },
            "capture screenshot",
            options.ContainsKey("json"),
            cancellationToken);
    }

    private static async Task<int> HandleInputAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new InvalidOperationException("Missing input subcommand.");
        }

        var options = ParseOptions(args[1..]);
        var payload = new JsonObject();
        foreach (var (key, value) in options)
        {
            if (key is "project" or "session-id" or "json")
            {
                continue;
            }

            payload[key] = value is null ? true : JsonValue.Create(value);
        }

        return await RunDaemonCommandAsync(
            options.GetValueOrDefault("project") ?? Environment.CurrentDirectory,
            options.GetValueOrDefault("session-id"),
            $"input.{args[0]}",
            payload,
            $"input {args[0]}",
            options.ContainsKey("json"),
            cancellationToken);
    }

    private static async Task<int> HandleInspectAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new InvalidOperationException("Missing inspect subcommand.");
        }

        var options = ParseOptions(args[1..]);
        var payload = new JsonObject();
        foreach (var (key, value) in options)
        {
            if (key is "project" or "session-id" or "json")
            {
                continue;
            }

            payload[key] = value is null ? true : JsonValue.Create(value);
        }

        return await RunDaemonCommandAsync(
            options.GetValueOrDefault("project") ?? Environment.CurrentDirectory,
            options.GetValueOrDefault("session-id"),
            $"inspect.{args[0]}",
            payload,
            $"inspect {args[0]}",
            options.ContainsKey("json"),
            cancellationToken);
    }

    private static async Task<int> HandleTestAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] != "gdunit")
        {
            throw new InvalidOperationException("Usage: test gdunit [--project PATH] [--csproj PATH] [--json]");
        }

        var options = ParseOptions(args[1..]);
        var json = options.ContainsKey("json");
        var runner = new TestRunner();
        var result = await runner.RunGdUnitAsync(
            options.GetValueOrDefault("project") ?? Environment.CurrentDirectory,
            options.GetValueOrDefault("csproj"),
            cancellationToken);
        PrintResult(result, json);
        return result.ExitCode;
    }

    private static async Task<int> HandleValidateAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 2 || args[0] != "run")
        {
            throw new InvalidOperationException("Usage: validate run <scenario> [--project PATH] [--json]");
        }

        var scenarioPath = args[1];
        var options = ParseOptions(args[2..]);
        var layout = new WorkspaceLayout(options.GetValueOrDefault("project") ?? Environment.CurrentDirectory);
        var manifest = layout.LoadManifest(options.GetValueOrDefault("session-id"))
            ?? throw new InvalidOperationException("No active session was found.");
        manifest = await WaitForRuntimeConnectionAsync(layout, manifest, cancellationToken);
        if (!manifest.RuntimeConnected)
        {
            throw new InvalidOperationException("Timed out waiting for a live Godot runtime to connect.");
        }

        var runner = new ScenarioRunner();
        var (report, artifact) = await runner.RunAsync(manifest, scenarioPath, cancellationToken);
        PrintResult(new CommandResult
        {
            Success = report.Passed,
            Command = "validate run",
            ExitCode = report.Passed ? 0 : 1,
            Message = report.Passed ? "Scenario passed." : "Scenario failed.",
            SessionId = manifest.SessionId,
            Data = JsonSerializer.SerializeToNode(report, JsonDefaults.Options),
            Artifacts = new[] { artifact },
        }, options.ContainsKey("json"));
        return report.Passed ? 0 : 1;
    }

    private static async Task<int> HandleInternalAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] != "daemon")
        {
            throw new InvalidOperationException("Unsupported internal command.");
        }

        var options = ParseOptions(args[1..]);
        var launch = new DaemonLaunchArguments
        {
            ProjectPath = options["project"]!,
            SessionId = options["session-id"]!,
            Token = options["token"]!,
            GodotPath = options.GetValueOrDefault("godot"),
            Scene = options.GetValueOrDefault("scene"),
            Headless = options.ContainsKey("headless"),
        };
        return await GodotSessionDaemon.RunFromCliAsync(launch, cancellationToken);
    }

    private static async Task<int> RunDaemonCommandAsync(
        string projectPath,
        string? sessionId,
        string method,
        JsonObject payload,
        string commandName,
        bool json,
        CancellationToken cancellationToken)
    {
        var layout = new WorkspaceLayout(projectPath);
        var manifest = layout.LoadManifest(sessionId) ?? throw new InvalidOperationException("No active session was found.");
        if (RuntimeDependentMethods.Contains(method))
        {
            manifest = await WaitForRuntimeConnectionAsync(layout, manifest, cancellationToken);
            if (!manifest.RuntimeConnected)
            {
                throw new InvalidOperationException("Timed out waiting for a live Godot runtime to connect.");
            }
        }

        if (method == "session.status")
        {
            return PrintFileBackedResult(commandName, manifest, json);
        }

        var client = new BridgeClient();
        JsonNode result;
        try
        {
            result = await client.SendAsync(manifest, method, payload, cancellationToken);
        }
        catch (SocketException) when (method == "session.stop")
        {
            result = ForceStopStaleSession(layout, manifest);
        }

        var artifacts = ExtractArtifacts(result);
        PrintResult(new CommandResult
        {
            Success = true,
            Command = commandName,
            ExitCode = 0,
            Message = "OK",
            SessionId = manifest.SessionId,
            Data = result,
            Artifacts = artifacts,
        }, json);
        return 0;
    }

    private static async Task<SessionManifest> WaitForRuntimeConnectionAsync(
        WorkspaceLayout layout,
        SessionManifest manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.RuntimeConnected)
        {
            return manifest;
        }

        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(250, cancellationToken);
            var refreshed = layout.LoadManifest(manifest.SessionId);
            if (refreshed is null)
            {
                break;
            }

            manifest = refreshed;
            if (manifest.RuntimeConnected || string.Equals(manifest.State, "stopped", StringComparison.Ordinal))
            {
                break;
            }
        }

        return manifest;
    }

    private static JsonNode ForceStopStaleSession(WorkspaceLayout layout, SessionManifest manifest)
    {
        if (manifest.GodotPid is int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch
            {
                // Best-effort cleanup for stale sessions.
            }
        }

        manifest.State = "stopped";
        manifest.GodotPid = null;
        manifest.RuntimeConnected = false;
        manifest.Port = null;
        manifest.Token = null;
        layout.SaveManifest(manifest);

        return new JsonObject
        {
            ["sessionId"] = manifest.SessionId,
            ["state"] = "stopped",
            ["recovered"] = true,
        };
    }

    private static bool IsDaemonAlive(int daemonPid)
    {
        try
        {
            _ = Process.GetProcessById(daemonPid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<ArtifactRef> ExtractArtifacts(JsonNode result)
    {
        if (result is not JsonObject obj)
        {
            return Array.Empty<ArtifactRef>();
        }

        var artifactPath = obj["artifactPath"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
        {
            return Array.Empty<ArtifactRef>();
        }

        var kind = obj["artifactKind"]?.GetValue<string>() ?? InferKindFromPath(artifactPath);
        var relativePath = obj["artifactRelativePath"]?.GetValue<string>() ?? Path.GetFileName(artifactPath);
        return new[]
        {
            new ArtifactRef
            {
                Id = Path.GetFileNameWithoutExtension(artifactPath),
                Kind = kind,
                Path = artifactPath,
                RelativePath = relativePath,
                CreatedAtUtc = File.GetCreationTimeUtc(artifactPath),
            },
        };
    }

    private static string InferKindFromPath(string artifactPath)
    {
        var fileName = Path.GetFileName(artifactPath);
        if (fileName.StartsWith("screenshot-", StringComparison.OrdinalIgnoreCase))
        {
            return "screenshot";
        }

        return "artifact";
    }

    private static int PrintLogs(SessionManifest? manifest, bool json)
    {
        if (manifest is null)
        {
            PrintResult(new CommandResult
            {
                Success = false,
                Command = "session logs",
                ExitCode = 1,
                Message = "No active session was found.",
            }, json);
            return 1;
        }

        var layout = new WorkspaceLayout(manifest.ProjectPath);
        var data = new JsonObject
        {
            ["sessionId"] = manifest.SessionId,
            ["artifactsPath"] = layout.ArtifactsPath,
        };
        foreach (var entry in manifest.ArtifactIds)
        {
            data[entry.Key] = Directory.EnumerateFiles(layout.ArtifactsPath, $"*-{entry.Value}.*").FirstOrDefault();
        }

        PrintResult(new CommandResult
        {
            Success = true,
            Command = "session logs",
            ExitCode = 0,
            Message = "Log artifacts listed.",
            SessionId = manifest.SessionId,
            Data = data,
        }, json);
        return 0;
    }

    private static int PrintFileBackedResult(string command, SessionManifest? manifest, bool json)
    {
        if (manifest is null)
        {
            PrintResult(new CommandResult
            {
                Success = false,
                Command = command,
                ExitCode = 1,
                Message = "No active session was found.",
            }, json);
            return 1;
        }

        PrintResult(new CommandResult
        {
            Success = true,
            Command = command,
            ExitCode = 0,
            Message = "OK",
            SessionId = manifest.SessionId,
            Data = JsonSerializer.SerializeToNode(manifest, JsonDefaults.Options),
        }, json);
        return 0;
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            if (key.Contains('=', StringComparison.Ordinal))
            {
                var split = key.Split('=', 2);
                result[split[0]] = split[1];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[key] = args[++i];
            }
            else
            {
                result[key] = null;
            }
        }

        return result;
    }

    private static void PrintResult(CommandResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonDefaults.Serialize(result));
            return;
        }

        Console.WriteLine($"{(result.Success ? "OK" : "ERR")} {result.Command}");
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            Console.WriteLine(result.Message);
        }

        if (!string.IsNullOrWhiteSpace(result.SessionId))
        {
            Console.WriteLine($"session: {result.SessionId}");
        }

        if (result.Data is not null)
        {
            Console.WriteLine(result.Data.ToJsonString(JsonDefaults.Options));
        }

        if (result.Artifacts.Count > 0)
        {
            foreach (var artifact in result.Artifacts)
            {
                Console.WriteLine($"artifact[{artifact.Kind}]: {artifact.Path}");
            }
        }
    }

    private static bool IsHelp(string arg) => arg is "help" or "--help" or "-h";

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            godot-agent session run|stop|restart|status|logs
            godot-agent capture screenshot
            godot-agent input action|key|mouse
            godot-agent inspect scene|node|focus|hover|monitors|errors
            godot-agent test gdunit
            godot-agent validate run <scenario>
            """);
    }
}
