@tool
extends EditorPlugin

const AUTOLOAD_NAME := "GodotAgentRuntime"
const AUTOLOAD_PATH := "res://addons/godot_agent_runtime/agent_runtime.gd"

func _enter_tree() -> void:
    if not ProjectSettings.has_setting("autoload/%s" % AUTOLOAD_NAME):
        add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_PATH)

func _exit_tree() -> void:
    if ProjectSettings.has_setting("autoload/%s" % AUTOLOAD_NAME):
        remove_autoload_singleton(AUTOLOAD_NAME)

