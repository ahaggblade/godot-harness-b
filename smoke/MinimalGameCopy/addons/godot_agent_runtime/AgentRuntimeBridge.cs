using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using VariantArray = Godot.Collections.Array;
using VariantDictionary = Godot.Collections.Dictionary;
using StringObjectMap = System.Collections.Generic.Dictionary<string, object?>;

public partial class AgentRuntimeBridge : Node
{
    private const string Role = "runtime";

    private readonly StreamPeerTcp _tcp = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string _host = "127.0.0.1";
    private int _port;
    private string _token = string.Empty;
    private string _sessionId = string.Empty;
    private bool _connected;
    private bool _handshakeSent;
    private string _currentScenePath = string.Empty;
    private string _receiveBuffer = string.Empty;

    public void Configure(string host, int port, string token, string sessionId)
    {
        _host = host;
        _port = port;
        _token = token;
        _sessionId = sessionId;
    }

    public override void _Ready()
    {
        if (_port <= 0 || string.IsNullOrWhiteSpace(_token))
        {
            GD.PushWarning("Managed runtime bridge is missing launch configuration.");
            return;
        }

        var error = _tcp.ConnectToHost(_host, _port);
        if (error != Godot.Error.Ok)
        {
            GD.PushWarning($"Managed runtime bridge failed to connect to {_host}:{_port} ({error}).");
            return;
        }

        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _tcp.Poll();

        if (!_connected && _tcp.GetStatus() == StreamPeerTcp.Status.Connected)
        {
            _connected = true;
        }

        if (_connected && !_handshakeSent)
        {
            _handshakeSent = true;
            SendEnvelope(new Envelope
            {
                Type = "hello",
                Role = Role,
                SessionId = _sessionId,
                Token = _token,
            });
            SendEvent("runtime.ready", new StringObjectMap
            {
                ["engineVersion"] = Engine.GetVersionInfo()["string"].AsString(),
                ["mainLoop"] = Engine.GetMainLoop().GetClass(),
                ["runtime"] = "csharp",
            });
        }

        if (!_connected)
        {
            return;
        }

        EmitSceneChangeIfNeeded();

        while (_tcp.GetAvailableBytes() > 0)
        {
            _receiveBuffer += _tcp.GetUtf8String((int)_tcp.GetAvailableBytes());
        }

        var messages = _receiveBuffer.Split('\n');
        if (!_receiveBuffer.EndsWith('\n') && messages.Length > 0)
        {
            _receiveBuffer = messages[^1];
            Array.Resize(ref messages, messages.Length - 1);
        }
        else
        {
            _receiveBuffer = string.Empty;
        }

        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            HandleMessage(message);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && _connected)
        {
            SendEvent("runtime.disconnected", new Dictionary<string, object?>());
        }
    }

    private void HandleMessage(string rawMessage)
    {
        IncomingEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IncomingEnvelope>(rawMessage, _jsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (envelope is null || envelope.Type != "request" || string.IsNullOrWhiteSpace(envelope.Method))
        {
            return;
        }

        var parameters = envelope.Params;
        switch (envelope.Method)
        {
            case "inspect.scene":
                Reply(envelope.Id, InspectScene());
                break;
            case "inspect.node":
                Reply(envelope.Id, InspectNode(GetString(parameters, "path")));
                break;
            case "inspect.focus":
                Reply(envelope.Id, InspectFocus());
                break;
            case "inspect.hover":
                Reply(envelope.Id, InspectHover());
                break;
            case "inspect.monitors":
                Reply(envelope.Id, InspectMonitors());
                break;
            case "input.action":
                Reply(envelope.Id, InputAction(parameters));
                break;
            case "input.key":
                Reply(envelope.Id, InputKey(parameters));
                break;
            case "input.mouse":
                Reply(envelope.Id, InputMouse(parameters));
                break;
            case "capture.screenshot":
                _ = CaptureAndReplyAsync(envelope.Id, GetString(parameters, "label", "capture"));
                break;
            case "hook.invoke":
                Reply(envelope.Id, InvokeHook(parameters));
                break;
            case "runtime.quit":
                Reply(envelope.Id, new StringObjectMap { ["ok"] = true });
                GetTree().Quit();
                break;
            default:
                SendError(envelope.Id, "unknown_method", $"Unsupported method: {envelope.Method}");
                break;
        }
    }

    private async System.Threading.Tasks.Task CaptureAndReplyAsync(string? requestId, string label)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var texture = GetViewport().GetTexture();
        if (texture is null)
        {
            SendError(requestId, "capture_failed", "Viewport texture was null.");
            return;
        }

        var image = texture.GetImage();
        if (image is null)
        {
            SendError(requestId, "capture_failed", "Viewport image was null.");
            return;
        }

        var bytes = image.SavePngToBuffer();
        Reply(requestId, new Dictionary<string, object?>
        {
            ["label"] = label,
            ["width"] = image.GetWidth(),
            ["height"] = image.GetHeight(),
            ["pngBase64"] = Convert.ToBase64String(bytes),
        });
    }

    private StringObjectMap InspectScene()
    {
        var scene = GetTree().CurrentScene;
        return new StringObjectMap
        {
            ["currentScene"] = scene?.SceneFilePath ?? string.Empty,
            ["tree"] = GetTree().Root.GetTreeStringPretty(),
            ["focusOwner"] = NodeSummary(GetViewport().GuiGetFocusOwner()),
        };
    }

    private StringObjectMap InspectNode(string nodePath)
    {
        var node = GetNodeOrNull(nodePath);
        if (node is null)
        {
            return new StringObjectMap
            {
                ["found"] = false,
                ["path"] = nodePath,
            };
        }

        return new StringObjectMap
        {
            ["found"] = true,
            ["path"] = node.GetPath().ToString(),
            ["name"] = node.Name.ToString(),
            ["class"] = node.GetClass(),
            ["visible"] = node is CanvasItem canvasItem ? canvasItem.Visible : true,
            ["position"] = node is Node2D node2D ? new StringObjectMap
            {
                ["x"] = node2D.GlobalPosition.X,
                ["y"] = node2D.GlobalPosition.Y,
            } : null,
        };
    }

    private StringObjectMap InspectFocus() =>
        new()
        {
            ["focused"] = NodeSummary(GetViewport().GuiGetFocusOwner()),
        };

    private StringObjectMap InspectHover() =>
        new()
        {
            ["hovered"] = NodeSummary(GetViewport().GuiGetHoveredControl()),
        };

    private StringObjectMap InspectMonitors() =>
        new()
        {
            ["fps"] = Engine.GetFramesPerSecond(),
            ["processFrames"] = Engine.GetProcessFrames(),
            ["physicsFrames"] = Engine.GetPhysicsFrames(),
            ["staticMemory"] = Performance.GetMonitor(Performance.Monitor.MemoryStatic),
        };

    private StringObjectMap InputAction(JsonElement parameters)
    {
        var action = GetString(parameters, "action");
        var pressed = GetBool(parameters, "pressed", true);
        if (string.IsNullOrWhiteSpace(action))
        {
            return new StringObjectMap
            {
                ["ok"] = false,
                ["message"] = "action is required",
            };
        }

        if (pressed)
        {
            Input.ActionPress(action);
        }
        else
        {
            Input.ActionRelease(action);
        }

        return new StringObjectMap
        {
            ["ok"] = true,
            ["action"] = action,
            ["pressed"] = pressed,
        };
    }

    private static StringObjectMap InputKey(JsonElement parameters)
    {
        var inputEvent = new InputEventKey
        {
            Keycode = (Key)GetInt(parameters, "keycode", (int)Key.Enter),
            Pressed = GetBool(parameters, "pressed", true),
        };
        Input.ParseInputEvent(inputEvent);
        return new StringObjectMap { ["ok"] = true };
    }

    private static StringObjectMap InputMouse(JsonElement parameters)
    {
        var inputEvent = new InputEventMouseButton
        {
            ButtonIndex = (MouseButton)GetInt(parameters, "buttonIndex", (int)MouseButton.Left),
            Pressed = GetBool(parameters, "pressed", true),
        };

        if (TryGetDouble(parameters, "x", out var x) && TryGetDouble(parameters, "y", out var y))
        {
            var position = new Vector2((float)x, (float)y);
            inputEvent.Position = position;
            inputEvent.GlobalPosition = position;
        }

        Input.ParseInputEvent(inputEvent);
        return new StringObjectMap { ["ok"] = true };
    }

    private StringObjectMap InvokeHook(JsonElement parameters)
    {
        var hookMethod = GetString(parameters, "method");
        var hooks = GetNodeOrNull("/root/AgentHooks");
        if (hooks is null)
        {
            return new StringObjectMap
            {
                ["ok"] = false,
                ["message"] = "AgentHooks autoload not found",
            };
        }

        if (!hooks.HasMethod(hookMethod))
        {
            return new StringObjectMap
            {
                ["ok"] = false,
                ["message"] = $"AgentHooks missing method {hookMethod}",
            };
        }

        var args = new VariantArray();
        if (parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("arguments", out var argumentsElement) &&
            argumentsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in argumentsElement.EnumerateArray())
            {
                args.Add(ConvertToVariant(item));
            }
        }

        var result = hooks.Callv(hookMethod, args);
        return new StringObjectMap
        {
            ["ok"] = true,
            ["result"] = result.Obj,
        };
    }

    private void EmitSceneChangeIfNeeded()
    {
        var currentPath = GetTree().CurrentScene?.SceneFilePath ?? string.Empty;
        if (currentPath == _currentScenePath)
        {
            return;
        }

        _currentScenePath = currentPath;
        SendEvent("scene.changed", new StringObjectMap
        {
            ["currentScene"] = _currentScenePath,
        });
    }

    private static StringObjectMap NodeSummary(Node? node)
    {
        if (node is null)
        {
            return new StringObjectMap();
        }

        return new StringObjectMap
        {
            ["name"] = node.Name.ToString(),
            ["path"] = node.GetPath().ToString(),
            ["class"] = node.GetClass(),
        };
    }

    private void SendEvent(string method, StringObjectMap payload)
    {
        SendEnvelope(new Envelope
        {
            Type = "event",
            Method = method,
            Result = payload,
        });
    }

    private void Reply(string? requestId, StringObjectMap payload)
    {
        SendEnvelope(new Envelope
        {
            Type = "response",
            Id = requestId,
            Result = payload,
        });
    }

    private void SendError(string? requestId, string code, string message)
    {
        SendEnvelope(new Envelope
        {
            Type = "response",
            Id = requestId,
            Error = new EnvelopeError
            {
                Code = code,
                Message = message,
            },
        });
    }

    private void SendEnvelope(Envelope envelope)
    {
        if (!_connected && envelope.Type != "hello")
        {
            return;
        }

        var line = JsonSerializer.Serialize(envelope, _jsonOptions) + "\n";
        _tcp.PutData(Encoding.UTF8.GetBytes(line));
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? fallback;
        }

        return fallback;
    }

    private static bool GetBool(JsonElement element, string propertyName, bool fallback)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
        {
            return property.GetBoolean();
        }

        return fallback;
    }

    private static int GetInt(JsonElement element, string propertyName, int fallback)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt32(out var value))
        {
            return value;
        }

        return fallback;
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.TryGetDouble(out value);
    }

    private static Variant ConvertToVariant(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => default(Variant),
            JsonValueKind.Array => ConvertArray(element),
            JsonValueKind.Object => ConvertDictionary(element),
            _ => default(Variant),
        };

    private static VariantArray ConvertArray(JsonElement element)
    {
        var array = new VariantArray();
        foreach (var child in element.EnumerateArray())
        {
            array.Add(ConvertToVariant(child));
        }

        return array;
    }

    private static VariantDictionary ConvertDictionary(JsonElement element)
    {
        var dictionary = new VariantDictionary();
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertToVariant(property.Value);
        }

        return dictionary;
    }

    private sealed class IncomingEnvelope
    {
        public string? Type { get; set; }
        public string? Id { get; set; }
        public string? Method { get; set; }
        public JsonElement Params { get; set; }
    }

    private sealed class Envelope
    {
        public string? Type { get; set; }
        public string? Role { get; set; }
        public string? SessionId { get; set; }
        public string? Token { get; set; }
        public string? Id { get; set; }
        public string? Method { get; set; }
        public object? Result { get; set; }
        public EnvelopeError? Error { get; set; }
    }

    private sealed class EnvelopeError
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}
