# Godot Agent Runtime Addon

This addon is the Godot-side runtime bridge for the `godot-agent` CLI.

## Install into a project

1. Copy `addons/godot_agent_runtime` into your Godot project.
2. Enable the plugin in `Project > Project Settings > Plugins`.
3. The plugin installs an autoload named `GodotAgentRuntime`.
4. Optionally add your own `AgentHooks` autoload for deterministic setup helpers.

## Host arguments

The CLI daemon passes the following user args when it launches Godot:

- `--agent-host=127.0.0.1`
- `--agent-port=<port>`
- `--agent-token=<token>`
- `--agent-session-id=<id>`

## RPC surface

- `inspect.scene`
- `inspect.node`
- `inspect.focus`
- `inspect.hover`
- `inspect.monitors`
- `input.action`
- `input.key`
- `input.mouse`
- `capture.screenshot`
- `hook.invoke`
- `runtime.quit`
