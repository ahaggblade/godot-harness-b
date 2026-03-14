extends Node

const MANAGED_BRIDGE_PATH := "res://addons/godot_agent_runtime/AgentRuntimeBridge.cs"
const FALLBACK_BRIDGE_PATH := "res://addons/godot_agent_runtime/agent_runtime_fallback.gd"

func _ready() -> void:
    var config := _parse_launch_config()
    if not config.enabled:
        push_warning("GodotAgentRuntime did not find agent launch arguments.")
        return

    var managed_attempt := _instantiate_bridge(MANAGED_BRIDGE_PATH, "ManagedBridge")
    var bridge: Node = managed_attempt.node
    if bridge == null:
        var fallback_attempt := _instantiate_bridge(FALLBACK_BRIDGE_PATH, "DiagnosticBridge")
        bridge = fallback_attempt.node
        if bridge != null:
            var diagnostic_method := _resolve_diagnostic_method(bridge)
            if not diagnostic_method.is_empty():
                bridge.call(diagnostic_method, String(managed_attempt.error))

    if bridge == null:
        push_error("GodotAgentRuntime could not create a runtime bridge. Managed error: %s" % managed_attempt.error)
        return

    var configure_method := _resolve_configure_method(bridge)
    if not configure_method.is_empty():
        bridge.call(configure_method, config.host, config.port, config.token, config.session_id)

    add_child(bridge)

func _instantiate_bridge(script_path: String, node_name: String) -> Dictionary:
    if not ResourceLoader.exists(script_path):
        return {
            "node": null,
            "error": "Bridge script does not exist: %s" % script_path
        }

    var script := load(script_path) as Script
    if script == null:
        push_warning("GodotAgentRuntime failed to load bridge script: %s" % script_path)
        return {
            "node": null,
            "error": "Failed to load bridge script: %s" % script_path
        }
    if not script.can_instantiate():
        push_warning("GodotAgentRuntime bridge script cannot instantiate yet: %s" % script_path)
        return {
            "node": null,
            "error": "Bridge script cannot instantiate yet: %s" % script_path
        }

    var node = script.new()
    if not (node is Node):
        push_warning("GodotAgentRuntime bridge did not instantiate as a Node: %s" % script_path)
        return {
            "node": null,
            "error": "Bridge did not instantiate as a Node: %s" % script_path
        }

    node.name = node_name
    if _resolve_configure_method(node).is_empty():
        push_warning("GodotAgentRuntime bridge is missing configure/Configure(): %s" % script_path)
        node.free()
        return {
            "node": null,
            "error": "Bridge is missing configure/Configure(): %s" % script_path
        }

    return {
        "node": node,
        "error": ""
    }

func _resolve_configure_method(node: Node) -> String:
    if node.has_method("configure"):
        return "configure"
    if node.has_method("Configure"):
        return "Configure"
    return ""

func _resolve_diagnostic_method(node: Node) -> String:
    if node.has_method("set_diagnostic_state"):
        return "set_diagnostic_state"
    if node.has_method("SetDiagnosticState"):
        return "SetDiagnosticState"
    return ""

func _parse_launch_config() -> Dictionary:
    var config := {
        "enabled": false,
        "host": "127.0.0.1",
        "port": 0,
        "token": "",
        "session_id": ""
    }

    _consume_agent_args(config, OS.get_cmdline_user_args())
    if not config.enabled:
        _consume_agent_args(config, OS.get_cmdline_args())

    return config

func _consume_agent_args(config: Dictionary, args: PackedStringArray) -> void:
    for arg in args:
        if arg.begins_with("--agent-host="):
            config.host = arg.trim_prefix("--agent-host=")
            config.enabled = true
        elif arg.begins_with("--agent-port="):
            config.port = int(arg.trim_prefix("--agent-port="))
            config.enabled = true
        elif arg.begins_with("--agent-token="):
            config.token = arg.trim_prefix("--agent-token=")
            config.enabled = true
        elif arg.begins_with("--agent-session-id="):
            config.session_id = arg.trim_prefix("--agent-session-id=")
            config.enabled = true

    config.enabled = config.enabled and config.port > 0 and not String(config.token).is_empty()
