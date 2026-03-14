using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotAgent.Core;

public sealed class GodotSessionDaemon
{
    private readonly WorkspaceLayout _layout;
    private readonly SessionManifest _manifest;
    private readonly string _token;
    private readonly List<JsonObject> _recentEvents = new();
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeEnvelope>> _pendingResponses = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();

    private TcpListener? _listener;
    private RuntimeConnection? _runtimeConnection;
    private Process? _godotProcess;
    private StreamWriter? _hostLogWriter;

    public GodotSessionDaemon(DaemonLaunchArguments arguments)
    {
        _layout = new WorkspaceLayout(arguments.ProjectPath);
        _layout.Ensure();
        _token = arguments.Token;
        _manifest = new SessionManifest
        {
            SessionId = arguments.SessionId,
            ProjectPath = _layout.ProjectPath,
            State = "starting",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            DaemonPid = Environment.ProcessId,
            GodotPath = arguments.GodotPath,
            Scene = arguments.Scene,
        };
    }

    public async Task<int> RunAsync(DaemonLaunchArguments arguments, CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var hostLog = _layout.WriteTextArtifact("host-log", "log", string.Empty);
        var runtimeEvents = _layout.WriteTextArtifact("runtime-events", "log", string.Empty);
        _manifest.ArtifactIds["hostLog"] = hostLog.Id;
        _manifest.ArtifactIds["runtimeEvents"] = runtimeEvents.Id;
        _layout.SetActiveSession(_manifest.SessionId);
        _layout.SaveManifest(_manifest);

        await using var hostFile = new FileStream(hostLog.Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _hostLogWriter = new StreamWriter(hostFile, new UTF8Encoding(false)) { AutoFlush = true };
        await LogAsync($"[{DateTimeOffset.UtcNow:O}] daemon starting");

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _manifest.Port = port;
        _manifest.Token = _token;
        _layout.SaveManifest(_manifest);

        var acceptLoop = AcceptLoopAsync(runtimeEvents.Path, linked.Token);

        try
        {
            await LaunchGodotAsync(arguments, linked.Token);
            _manifest.State = _godotProcess is null ? "waiting_for_runtime" : "running";
            _layout.SaveManifest(_manifest);
            await LogAsync($"[{DateTimeOffset.UtcNow:O}] daemon listening on 127.0.0.1:{port}");
            await WaitForSessionShutdownAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            await LogAsync($"[{DateTimeOffset.UtcNow:O}] daemon stopping");
        }
        catch (Exception ex)
        {
            _manifest.State = "error";
            _manifest.LastError = ex.ToString();
            _layout.SaveManifest(_manifest);
            await LogAsync($"[{DateTimeOffset.UtcNow:O}] daemon error {ex}");
            return 1;
        }
        finally
        {
            _manifest.State = "stopped";
            _manifest.RuntimeConnected = false;
            _manifest.GodotPid = null;
            _layout.SaveManifest(_manifest);
            _listener.Stop();
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or SocketException)
            {
                await LogAsync($"[{DateTimeOffset.UtcNow:O}] accept loop ended during shutdown: {ex.Message}");
            }
        }

        return 0;
    }

    public static async Task<int> RunFromCliAsync(DaemonLaunchArguments arguments, CancellationToken cancellationToken)
    {
        var daemon = new GodotSessionDaemon(arguments);
        return await daemon.RunAsync(arguments, cancellationToken);
    }

    private async Task LaunchGodotAsync(DaemonLaunchArguments arguments, CancellationToken cancellationToken)
    {
        var godotPath = ResolveGodotPath(arguments.GodotPath);
        if (string.IsNullOrWhiteSpace(godotPath))
        {
            await LogAsync($"[{DateTimeOffset.UtcNow:O}] no Godot binary configured; waiting for a manually started runtime");
            return;
        }

        var runtimeLog = _layout.WriteTextArtifact("godot-log", "log", string.Empty);
        _manifest.ArtifactIds["godotLog"] = runtimeLog.Id;
        _manifest.GodotPath = godotPath;
        _layout.SaveManifest(_manifest);

        var psi = new ProcessStartInfo
        {
            FileName = godotPath,
            WorkingDirectory = _layout.ProjectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(_layout.ProjectPath);
        if (arguments.Headless)
        {
            psi.ArgumentList.Add("--headless");
        }

        if (!string.IsNullOrWhiteSpace(arguments.Scene))
        {
            psi.ArgumentList.Add("--scene");
            psi.ArgumentList.Add(arguments.Scene);
        }

        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--agent-host=127.0.0.1");
        psi.ArgumentList.Add($"--agent-port={_manifest.Port}");
        psi.ArgumentList.Add($"--agent-token={_token}");
        psi.ArgumentList.Add($"--agent-session-id={_manifest.SessionId}");

        _godotProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _godotProcess.Start();
        _manifest.GodotPid = _godotProcess.Id;
        _layout.SaveManifest(_manifest);

        _ = PipeProcessStreamAsync(_godotProcess.StandardOutput, runtimeLog.Path, cancellationToken);
        _ = PipeProcessStreamAsync(_godotProcess.StandardError, runtimeLog.Path, cancellationToken);
    }

    private async Task WaitForSessionShutdownAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var processExited = _godotProcess is null || _godotProcess.HasExited;
            var runtimeConnected = _runtimeConnection is not null;

            if (processExited && !runtimeConnected)
            {
                _manifest.State = "stopped";
                _manifest.GodotPid = null;
                _layout.SaveManifest(_manifest);
                return;
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private async Task PipeProcessStreamAsync(StreamReader source, string targetPath, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await source.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            await File.AppendAllTextAsync(targetPath, line + Environment.NewLine, cancellationToken);
        }
    }

    private async Task AcceptLoopAsync(string runtimeEventsPath, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleConnectionAsync(client, runtimeEventsPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                break;
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, string runtimeEventsPath, CancellationToken cancellationToken)
    {
        using var _client = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        var hello = await ReadEnvelopeAsync(reader, cancellationToken);
        if (hello.Type != "hello" || hello.SessionId != _manifest.SessionId || hello.Token != _token)
        {
            await WriteEnvelopeAsync(writer, new BridgeEnvelope
            {
                Type = "response",
                Error = new BridgeError { Code = "unauthorized", Message = "Session token or id mismatch." },
            });
            return;
        }

        await WriteEnvelopeAsync(writer, new BridgeEnvelope
        {
            Type = "response",
            Result = new JsonObject { ["ok"] = true },
        });

        if (string.Equals(hello.Role, "runtime", StringComparison.Ordinal))
        {
            var runtime = new RuntimeConnection(client, reader, writer);
            _runtimeConnection = runtime;
            _manifest.RuntimeConnected = true;
            _layout.SaveManifest(_manifest);
            try
            {
                await AppendEventAsync(runtimeEventsPath, "runtime.connected", new JsonObject());
                await RuntimeReadLoopAsync(runtime, runtimeEventsPath, cancellationToken);
            }
            finally
            {
                _manifest.RuntimeConnected = false;
                _runtimeConnection = null;
                _layout.SaveManifest(_manifest);
            }

            return;
        }

        var request = await ReadEnvelopeAsync(reader, cancellationToken);
        var response = await ProcessClientRequestAsync(request, runtimeEventsPath, cancellationToken);
        await WriteEnvelopeAsync(writer, response);
    }

    private async Task RuntimeReadLoopAsync(RuntimeConnection runtime, string runtimeEventsPath, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var envelope = await ReadEnvelopeAsync(runtime.Reader, cancellationToken);
                if (envelope.Type == "response" && envelope.Id is not null && _pendingResponses.TryRemove(envelope.Id, out var pending))
                {
                    pending.TrySetResult(envelope);
                    continue;
                }

                if (envelope.Type == "event")
                {
                    var payload = envelope.Result as JsonObject ?? new JsonObject();
                    if (string.Equals(envelope.Method, "runtime.ready", StringComparison.Ordinal))
                    {
                        _manifest.RuntimeVersion = payload["engineVersion"]?.ToString();
                        _manifest.Metadata["runtime"] = payload["runtime"]?.DeepClone();
                        _manifest.Metadata["degraded"] = payload["degraded"]?.DeepClone() ?? false;
                        _manifest.Metadata["managedBridgeAvailable"] = payload["managedBridgeAvailable"]?.DeepClone();
                        _manifest.Metadata["managedBridgeError"] = payload["managedBridgeError"]?.DeepClone();
                        if (payload["managedBridgeError"] is not null)
                        {
                            _manifest.LastError = payload["managedBridgeError"]?.GetValue<string>();
                        }

                        _layout.SaveManifest(_manifest);
                    }

                    if (string.Equals(envelope.Method, "scene.changed", StringComparison.Ordinal))
                    {
                        _manifest.CurrentScene = payload["currentScene"]?.GetValue<string>();
                        _layout.SaveManifest(_manifest);
                    }

                    if (string.Equals(envelope.Method, "runtime.error", StringComparison.Ordinal))
                    {
                        _manifest.LastError = payload["message"]?.GetValue<string>();
                        _layout.SaveManifest(_manifest);
                    }

                    await AppendEventAsync(runtimeEventsPath, envelope.Method ?? "event", payload);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            await AppendEventAsync(runtimeEventsPath, "runtime.disconnected", new JsonObject { ["message"] = ex.Message });
        }
        catch (Exception ex)
        {
            _manifest.LastError = ex.Message;
            _layout.SaveManifest(_manifest);
            await AppendEventAsync(runtimeEventsPath, "runtime.error", new JsonObject
            {
                ["code"] = "runtime_read_failed",
                ["message"] = ex.Message,
            });
            await AppendEventAsync(runtimeEventsPath, "runtime.disconnected", new JsonObject { ["message"] = ex.Message });
        }
    }

    private async Task<BridgeEnvelope> ProcessClientRequestAsync(BridgeEnvelope request, string runtimeEventsPath, CancellationToken cancellationToken)
    {
        try
        {
            return request.Method switch
            {
                "session.status" => SuccessResponse(request, GetStatusNode()),
                "session.logs" => SuccessResponse(request, BuildLogSummaryNode()),
                "session.stop" => SuccessResponse(request, await StopAsync(runtimeEventsPath, cancellationToken)),
                "capture.screenshot" => await ForwardToRuntimeAsync(request, cancellationToken),
                "inspect.scene" => await ForwardToRuntimeAsync(request, cancellationToken),
                "inspect.node" => await ForwardToRuntimeAsync(request, cancellationToken),
                "inspect.focus" => await ForwardToRuntimeAsync(request, cancellationToken),
                "inspect.hover" => await ForwardToRuntimeAsync(request, cancellationToken),
                "inspect.monitors" => await ForwardToRuntimeAsync(request, cancellationToken),
                "inspect.errors" => SuccessResponse(request, BuildErrorsNode()),
                "input.action" => await ForwardToRuntimeAsync(request, cancellationToken),
                "input.key" => await ForwardToRuntimeAsync(request, cancellationToken),
                "input.mouse" => await ForwardToRuntimeAsync(request, cancellationToken),
                "hook.invoke" => await ForwardToRuntimeAsync(request, cancellationToken),
                "runtime.quit" => await ForwardToRuntimeAsync(request, cancellationToken),
                _ => ErrorResponse(request, "unknown_method", $"Unsupported method '{request.Method}'."),
            };
        }
        catch (Exception ex)
        {
            return ErrorResponse(request, "daemon_error", ex.Message);
        }
    }

    private async Task<BridgeEnvelope> ForwardToRuntimeAsync(BridgeEnvelope request, CancellationToken cancellationToken)
    {
        if (_runtimeConnection is null)
        {
            return ErrorResponse(request, "runtime_unavailable", "No live Godot runtime is connected to the daemon.");
        }

        var requestId = request.Id ?? Guid.NewGuid().ToString("N");
        var envelope = new BridgeEnvelope
        {
            Type = "request",
            Id = requestId,
            Method = request.Method,
            Params = request.Params ?? new JsonObject(),
            SessionId = _manifest.SessionId,
            Token = _token,
        };

        var pending = new TaskCompletionSource<BridgeEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[requestId] = pending;
        await WriteEnvelopeAsync(_runtimeConnection.Writer, envelope);

        var response = await pending.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        if (response.Error is not null)
        {
            return new BridgeEnvelope
            {
                Type = "response",
                Id = request.Id,
                Error = response.Error,
            };
        }

        if (response.Result is JsonObject resultObject && request.Method == "capture.screenshot")
        {
            var bytesBase64 = resultObject["pngBase64"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(bytesBase64))
            {
                var bytes = Convert.FromBase64String(bytesBase64);
                var artifact = _layout.WriteBytesArtifact("screenshot", "png", bytes);
                resultObject["artifactKind"] = artifact.Kind;
                resultObject["artifactPath"] = artifact.Path;
                resultObject["artifactRelativePath"] = artifact.RelativePath;
                resultObject.Remove("pngBase64");
            }
        }

        return new BridgeEnvelope
        {
            Type = "response",
            Id = request.Id,
            Result = response.Result,
        };
    }

    private async Task<JsonObject> StopAsync(string runtimeEventsPath, CancellationToken cancellationToken)
    {
        if (_runtimeConnection is not null)
        {
            await ForwardToRuntimeAsync(new BridgeEnvelope
            {
                Type = "request",
                Id = Guid.NewGuid().ToString("N"),
                Method = "runtime.quit",
                Params = new JsonObject(),
            }, cancellationToken);
        }

        if (_godotProcess is not null && !_godotProcess.HasExited)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            try
            {
                await _godotProcess.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _godotProcess.Kill(entireProcessTree: true);
                await _godotProcess.WaitForExitAsync(CancellationToken.None);
            }
        }

        await AppendEventAsync(runtimeEventsPath, "session.stopped", new JsonObject());
        _shutdown.Cancel();
        _manifest.State = "stopped";
        _layout.SaveManifest(_manifest);
        return new JsonObject
        {
            ["sessionId"] = _manifest.SessionId,
            ["state"] = _manifest.State,
        };
    }

    private JsonObject GetStatusNode() =>
        new()
        {
            ["sessionId"] = _manifest.SessionId,
            ["state"] = _manifest.State,
            ["projectPath"] = _manifest.ProjectPath,
            ["godotPid"] = _manifest.GodotPid,
            ["daemonPid"] = _manifest.DaemonPid,
            ["runtimeConnected"] = _manifest.RuntimeConnected,
            ["runtimeVersion"] = _manifest.RuntimeVersion,
            ["currentScene"] = _manifest.CurrentScene,
            ["port"] = _manifest.Port,
            ["lastError"] = _manifest.LastError,
            ["metadata"] = _manifest.Metadata.DeepClone(),
        };

    private JsonObject BuildLogSummaryNode()
    {
        var node = new JsonObject
        {
            ["sessionId"] = _manifest.SessionId,
            ["artifacts"] = new JsonArray(),
        };

        foreach (var entry in _manifest.ArtifactIds)
        {
            var path = Directory.EnumerateFiles(_layout.ArtifactsPath, $"*-{entry.Value}.*").FirstOrDefault();
            if (path is null)
            {
                continue;
            }

            ((JsonArray)node["artifacts"]!).Add(new JsonObject
            {
                ["name"] = entry.Key,
                ["path"] = path,
            });
        }

        return node;
    }

    private JsonObject BuildErrorsNode()
    {
        var errors = new JsonArray();
        lock (_gate)
        {
            foreach (var item in _recentEvents.Where(x => string.Equals(x["method"]?.GetValue<string>(), "runtime.error", StringComparison.Ordinal)))
            {
                errors.Add(item.DeepClone());
            }
        }

        return new JsonObject
        {
            ["sessionId"] = _manifest.SessionId,
            ["lastError"] = _manifest.LastError,
            ["metadata"] = _manifest.Metadata.DeepClone(),
            ["errors"] = errors,
        };
    }

    private async Task AppendEventAsync(string runtimeEventsPath, string method, JsonObject payload)
    {
        var node = new JsonObject
        {
            ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["method"] = method,
            ["payload"] = payload,
        };

        lock (_gate)
        {
            _recentEvents.Add(node);
            if (_recentEvents.Count > 100)
            {
                _recentEvents.RemoveAt(0);
            }
        }

        await File.AppendAllTextAsync(runtimeEventsPath, node.ToJsonString(JsonDefaults.Options) + Environment.NewLine, Encoding.UTF8);
    }

    private async Task LogAsync(string line)
    {
        if (_hostLogWriter is null)
        {
            return;
        }

        await _hostLogWriter.WriteLineAsync(line);
    }

    private static string? ResolveGodotPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        return Environment.GetEnvironmentVariable("GODOT_BIN");
    }

    private static BridgeEnvelope SuccessResponse(BridgeEnvelope request, JsonNode node) =>
        new()
        {
            Type = "response",
            Id = request.Id,
            Result = node,
        };

    private static BridgeEnvelope ErrorResponse(BridgeEnvelope request, string code, string message) =>
        new()
        {
            Type = "response",
            Id = request.Id,
            Error = new BridgeError { Code = code, Message = message },
        };

    private static async Task WriteEnvelopeAsync(StreamWriter writer, BridgeEnvelope envelope)
    {
        await writer.WriteLineAsync(JsonDefaults.SerializeCompact(envelope));
    }

    private static async Task<BridgeEnvelope> ReadEnvelopeAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new IOException("Bridge connection closed.");
        }

        return JsonDefaults.Deserialize<BridgeEnvelope>(line) ?? throw new IOException("Invalid bridge payload.");
    }

    public static (string executable, string arguments) BuildRespawnCommand(DaemonLaunchArguments arguments)
    {
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        var processPath = Environment.ProcessPath ?? entryAssembly ?? throw new InvalidOperationException("Unable to determine current process path.");
        var argList = new List<string>
        {
            "internal",
            "daemon",
            "--project", Quote(arguments.ProjectPath),
            "--session-id", Quote(arguments.SessionId),
            "--token", Quote(arguments.Token),
        };

        if (!string.IsNullOrWhiteSpace(arguments.GodotPath))
        {
            argList.Add("--godot");
            argList.Add(Quote(arguments.GodotPath));
        }

        if (!string.IsNullOrWhiteSpace(arguments.Scene))
        {
            argList.Add("--scene");
            argList.Add(Quote(arguments.Scene));
        }

        if (arguments.Headless)
        {
            argList.Add("--headless");
        }

        if (processPath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) || processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entryAssembly))
            {
                throw new InvalidOperationException("Failed to resolve the CLI assembly path.");
            }

            return (processPath, $"{Quote(entryAssembly)} {string.Join(' ', argList)}");
        }

        return (processPath, string.Join(' ', argList));
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private sealed record RuntimeConnection(TcpClient Client, StreamReader Reader, StreamWriter Writer);
}
