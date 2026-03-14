# Godot Agent Runtime Addon

This addon is the Godot-side runtime bridge for the `godot-agent` CLI.

## Hybrid structure

- `agent_runtime.gd`: tiny bootstrap autoload that parses launch args and chooses the runtime implementation
- `AgentRuntimeBridge.cs`: primary managed runtime bridge for C#-capable projects
- `agent_runtime_fallback.gd`: diagnostic-only fallback that reports why the managed bridge could not load

This keeps the real bridge logic in C# while preserving a small bootstrap and a clear failure-reporting path.

## Install into a project

1. Copy `addons/godot_agent_runtime` into your Godot project.
2. Enable the plugin in `Project > Project Settings > Plugins`.
3. The plugin installs an autoload named `GodotAgentRuntime`.
4. Optionally add your own `AgentHooks` autoload for deterministic setup helpers.
5. For C# projects, run `Godot --editor --build-solutions` or open the project in the editor so `AgentRuntimeBridge.cs` is compiled into the project assembly.

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
