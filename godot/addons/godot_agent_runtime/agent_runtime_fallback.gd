extends Node

const ROLE := "runtime"
const DIAGNOSTIC_RUNTIME := "gdscript-diagnostic"

var _host: String = "127.0.0.1"
var _port: int = 0
var _token: String = ""
var _session_id: String = ""
var _tcp := StreamPeerTCP.new()
var _connected := false
var _handshake_sent := false
var _current_scene_path := ""
var _receive_buffer := ""
var _managed_bridge_error := "Managed bridge was unavailable."

func configure(host: String, port: int, token: String, session_id: String) -> void:
    _host = host
    _port = port
    _token = token
    _session_id = session_id

func set_diagnostic_state(managed_bridge_error: String) -> void:
    if not managed_bridge_error.is_empty():
        _managed_bridge_error = managed_bridge_error

func _ready() -> void:
    if _port <= 0 or _token.is_empty():
        push_warning("Diagnostic runtime bridge is missing launch configuration.")
        return

    var error := _tcp.connect_to_host(_host, _port)
    if error != OK:
        push_warning("GodotAgentRuntime diagnostic bridge failed to connect to %s:%d" % [_host, _port])
        return

    set_process(true)

func _process(_delta: float) -> void:
    _tcp.poll()

    if not _connected and _tcp.get_status() == StreamPeerTCP.STATUS_CONNECTED:
        _connected = true

    if _connected and not _handshake_sent:
        _handshake_sent = true
        _send({
            "type": "hello",
            "role": ROLE,
            "sessionId": _session_id,
            "token": _token
        })
        _send_event("runtime.ready", {
            "engineVersion": Engine.get_version_info().get("string", ""),
            "mainLoop": Engine.get_main_loop().get_class(),
            "runtime": DIAGNOSTIC_RUNTIME,
            "degraded": true,
            "managedBridgeAvailable": false,
            "managedBridgeError": _managed_bridge_error
        })
        _send_event("runtime.error", {
            "code": "managed_bridge_unavailable",
            "message": _managed_bridge_error
        })
    if not _connected:
        return

    _emit_scene_change_if_needed()

    while _tcp.get_available_bytes() > 0:
        _receive_buffer += _tcp.get_utf8_string(_tcp.get_available_bytes())

    var messages := Array(_receive_buffer.split("\n", false))
    if not _receive_buffer.ends_with("\n") and messages.size() > 0:
        _receive_buffer = str(messages[messages.size() - 1])
        messages.remove_at(messages.size() - 1)
    else:
        _receive_buffer = ""

    for message in messages:
        if message.strip_edges().is_empty():
            continue
        _handle_message(message)

func _notification(what: int) -> void:
    if what == NOTIFICATION_PREDELETE and _connected:
        _send_event("runtime.disconnected", {})

func _handle_message(raw_message: String) -> void:
    var payload = JSON.parse_string(raw_message)
    if typeof(payload) != TYPE_DICTIONARY:
        return

    var envelope: Dictionary = payload
    if envelope.get("type", "") == "response":
        return
    if envelope.get("type", "") != "request":
        return

    var method: String = envelope.get("method", "")
    var request_id: String = envelope.get("id", "")

    match method:
        "inspect.scene":
            _reply(request_id, _inspect_scene())
        "inspect.monitors":
            _reply(request_id, _inspect_monitors())
        "runtime.quit":
            _reply(request_id, {"ok": true, "degraded": true})
            get_tree().quit()
        _:
            _error(request_id, "managed_bridge_unavailable", _managed_bridge_error)

func _inspect_scene() -> Dictionary:
    var scene := get_tree().current_scene
    return {
        "currentScene": scene.scene_file_path if scene else "",
        "tree": get_tree().root.get_tree_string_pretty(),
        "bridgeMode": DIAGNOSTIC_RUNTIME,
        "degraded": true,
        "managedBridgeError": _managed_bridge_error
    }

func _inspect_monitors() -> Dictionary:
    return {
        "fps": Engine.get_frames_per_second(),
        "processFrames": Engine.get_process_frames(),
        "physicsFrames": Engine.get_physics_frames(),
        "bridgeMode": DIAGNOSTIC_RUNTIME,
        "degraded": true
    }

func _emit_scene_change_if_needed() -> void:
    var current_path := ""
    if get_tree().current_scene != null:
        current_path = get_tree().current_scene.scene_file_path

    if current_path == _current_scene_path:
        return

    _current_scene_path = current_path
    _send_event("scene.changed", {
        "currentScene": _current_scene_path
    })

func _send_event(method: String, payload: Dictionary) -> void:
    _send({
        "type": "event",
        "method": method,
        "result": payload
    })

func _reply(request_id: String, payload: Dictionary) -> void:
    _send({
        "type": "response",
        "id": request_id,
        "result": payload
    })

func _error(request_id: String, code: String, message: String) -> void:
    _send({
        "type": "response",
        "id": request_id,
        "error": {
            "code": code,
            "message": message
        }
    })

func _send(payload: Dictionary) -> void:
    if not _connected:
        return

    var line := JSON.stringify(payload) + "\n"
    _tcp.put_data(line.to_utf8_buffer())
