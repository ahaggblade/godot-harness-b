extends Node

const MANAGED_BRIDGE_PATH := "res://addons/godot_agent_runtime/AgentRuntimeBridge.cs"
const FALLBACK_BRIDGE_PATH := "res://addons/godot_agent_runtime/agent_runtime_fallback.gd"
const ENABLE_ENV := "GODOT_AGENT_ENABLE"
const HOST_ENV := "GODOT_AGENT_HOST"
const PORT_ENV := "GODOT_AGENT_PORT"
const TOKEN_ENV := "GODOT_AGENT_TOKEN"
const SESSION_ENV := "GODOT_AGENT_SESSION_ID"

func _ready() -> void:
	var activation := _resolve_activation()
	if not activation.enabled:
		if activation.partial:
			push_warning("GodotAgentRuntime found incomplete activation data for source: %s" % activation.source)
		return

	var config: Dictionary = activation.config
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

func _resolve_activation() -> Dictionary:
	var activation := _empty_activation()

	_consume_agent_args(activation, OS.get_cmdline_user_args(), "user_args")
	if activation.enabled or activation.partial:
		return activation

	_consume_agent_args(activation, OS.get_cmdline_args(), "cmdline_args")
	if activation.enabled or activation.partial:
		return activation

	_consume_agent_env(activation)
	return activation

func _empty_activation() -> Dictionary:
	return {
		"enabled": false,
		"partial": false,
		"source": "",
		"config": {
			"host": "127.0.0.1",
			"port": 0,
			"token": "",
			"session_id": ""
		}
	}

func _consume_agent_args(activation: Dictionary, args: PackedStringArray, source: String) -> void:
	var config: Dictionary = activation.config
	var saw_agent_args := false
	for arg in args:
		if arg.begins_with("--agent-host="):
			config.host = arg.trim_prefix("--agent-host=")
			saw_agent_args = true
		elif arg.begins_with("--agent-port="):
			config.port = int(arg.trim_prefix("--agent-port="))
			saw_agent_args = true
		elif arg.begins_with("--agent-token="):
			config.token = arg.trim_prefix("--agent-token=")
			saw_agent_args = true
		elif arg.begins_with("--agent-session-id="):
			config.session_id = arg.trim_prefix("--agent-session-id=")
			saw_agent_args = true

	activation.config = config
	activation.source = source if saw_agent_args else activation.source
	activation.partial = saw_agent_args
	activation.enabled = saw_agent_args and _is_complete_config(config)

func _consume_agent_env(activation: Dictionary) -> void:
	var enabled_flag := OS.get_environment(ENABLE_ENV).to_lower()
	if enabled_flag not in ["1", "true", "yes", "on"]:
		return

	var config: Dictionary = activation.config
	config.host = OS.get_environment(HOST_ENV)
	config.port = int(OS.get_environment(PORT_ENV))
	config.token = OS.get_environment(TOKEN_ENV)
	config.session_id = OS.get_environment(SESSION_ENV)

	activation.config = config
	activation.source = "environment"
	activation.partial = true
	activation.enabled = _is_complete_config(config)

func _is_complete_config(config: Dictionary) -> bool:
	return int(config.port) > 0 \
		and not String(config.token).is_empty() \
		and not String(config.session_id).is_empty()
