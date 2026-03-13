extends Node

const ROLE := "runtime"
var _host: String = "127.0.0.1"
var _port: int = 0
var _token: String = ""
var _session_id: String = ""
var _tcp := StreamPeerTCP.new()
var _connected := false
var _handshake_sent := false
var _current_scene_path := ""
var _recent_events: Array[Dictionary] = []
var _receive_buffer := ""

func configure(host: String, port: int, token: String, session_id: String) -> void:
    _host = host
    _port = port
    _token = token
    _session_id = session_id

func _ready() -> void:
    if _port <= 0 or _token.is_empty():
        push_warning("Fallback runtime bridge is missing launch configuration.")
        return

    var error := _tcp.connect_to_host(_host, _port)
    if error != OK:
        push_warning("GodotAgentRuntime fallback failed to connect to %s:%d" % [_host, _port])
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
            "engineVersion": Engine.get_version_info(),
            "mainLoop": Engine.get_main_loop().get_class(),
            "runtime": "gdscript-fallback"
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
    var params: Dictionary = envelope.get("params", {})

    match method:
        "inspect.scene":
            _reply(request_id, _inspect_scene())
        "inspect.node":
            _reply(request_id, _inspect_node(params.get("path", "")))
        "inspect.focus":
            _reply(request_id, _inspect_focus())
        "inspect.hover":
            _reply(request_id, _inspect_hover())
        "inspect.monitors":
            _reply(request_id, _inspect_monitors())
        "input.action":
            _reply(request_id, _input_action(params))
        "input.key":
            _reply(request_id, _input_key(params))
        "input.mouse":
            _reply(request_id, _input_mouse(params))
        "capture.screenshot":
            _capture_and_reply(request_id, params)
        "hook.invoke":
            _reply(request_id, _invoke_hook(params))
        "runtime.quit":
            _reply(request_id, {"ok": true})
            get_tree().quit()
        _:
            _error(request_id, "unknown_method", "Unsupported method: %s" % method)

func _capture_and_reply(request_id: String, params: Dictionary) -> void:
    var label := str(params.get("label", "capture"))
    _capture_and_reply_async(request_id, label)

func _capture_and_reply_async(request_id: String, label: String) -> void:
    await get_tree().process_frame
    var texture := get_viewport().get_texture()
    if texture == null:
        _error(request_id, "capture_failed", "Viewport texture was null.")
        return

    var image := texture.get_image()
    if image == null:
        _error(request_id, "capture_failed", "Viewport image was null.")
        return

    var bytes: PackedByteArray = image.save_png_to_buffer()
    _reply(request_id, {
        "label": label,
        "width": image.get_width(),
        "height": image.get_height(),
        "pngBase64": Marshalls.raw_to_base64(bytes)
    })

func _inspect_scene() -> Dictionary:
    var scene := get_tree().current_scene
    return {
        "currentScene": scene.scene_file_path if scene else "",
        "tree": get_tree().root.get_tree_string_pretty(),
        "focusOwner": _node_summary(get_viewport().gui_get_focus_owner())
    }

func _inspect_node(node_path: String) -> Dictionary:
    var node := get_node_or_null(node_path)
    if node == null:
        return {"found": false, "path": node_path}

    return {
        "found": true,
        "path": str(node.get_path()),
        "name": node.name,
        "class": node.get_class(),
        "visible": node.visible if node is CanvasItem else true,
        "position": node.global_position if node is Node2D else null
    }

func _inspect_focus() -> Dictionary:
    return {
        "focused": _node_summary(get_viewport().gui_get_focus_owner())
    }

func _inspect_hover() -> Dictionary:
    return {
        "hovered": _node_summary(get_viewport().gui_get_hovered_control())
    }

func _inspect_monitors() -> Dictionary:
    return {
        "fps": Engine.get_frames_per_second(),
        "processFrames": Engine.get_process_frames(),
        "physicsFrames": Engine.get_physics_frames(),
        "staticMemory": Performance.get_monitor(Performance.MEMORY_STATIC)
    }

func _input_action(params: Dictionary) -> Dictionary:
    var action_name := str(params.get("action", ""))
    var pressed := bool(params.get("pressed", true))
    if action_name.is_empty():
        return {"ok": false, "message": "action is required"}

    if pressed:
        Input.action_press(action_name)
    else:
        Input.action_release(action_name)

    return {"ok": true, "action": action_name, "pressed": pressed}

func _input_key(params: Dictionary) -> Dictionary:
    var event := InputEventKey.new()
    event.keycode = int(params.get("keycode", KEY_ENTER))
    event.pressed = bool(params.get("pressed", true))
    Input.parse_input_event(event)
    return {"ok": true}

func _input_mouse(params: Dictionary) -> Dictionary:
    var event := InputEventMouseButton.new()
    event.button_index = int(params.get("buttonIndex", MOUSE_BUTTON_LEFT))
    event.pressed = bool(params.get("pressed", true))
    if params.has("x") and params.has("y"):
        var position := Vector2(float(params["x"]), float(params["y"]))
        event.position = position
        event.global_position = position

    Input.parse_input_event(event)
    return {"ok": true}

func _invoke_hook(params: Dictionary) -> Dictionary:
    var hook_method := str(params.get("method", ""))
    var hook_args := params.get("arguments", [])
    var hooks := get_node_or_null("/root/AgentHooks")
    if hooks == null:
        return {"ok": false, "message": "AgentHooks autoload not found"}

    if not hooks.has_method(hook_method):
        return {"ok": false, "message": "AgentHooks missing method %s" % hook_method}

    var result = hooks.callv(hook_method, hook_args)
    return {"ok": true, "result": result}

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

func _node_summary(node) -> Dictionary:
    if node == null:
        return {}

    return {
        "name": node.name,
        "path": str(node.get_path()),
        "class": node.get_class()
    }

func _send_event(method: String, payload: Dictionary) -> void:
    _append_recent_event(method, payload)
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
    _append_recent_event("runtime.error", {"code": code, "message": message})
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

func _append_recent_event(method: String, payload: Dictionary) -> void:
    _recent_events.append({
        "timestampUtc": Time.get_datetime_string_from_system(true),
        "method": method,
        "payload": payload
    })
    if _recent_events.size() > 100:
        _recent_events.pop_front()

