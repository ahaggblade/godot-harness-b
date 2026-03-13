# Godot Agent Runtime

`Godot Agent Runtime` is a CLI-first development and validation harness for Godot `4.6.1` projects, including C# projects. It is designed for AI coding agents that can edit files and run shell commands, with optional MCP integration layered on top.

## What is in this repo

- `src/GodotAgent.Core`: session lifecycle, artifact storage, bridge protocol, scenario validation, and gdUnit wrapping.
- `src/GodotAgent.Cli`: user-facing CLI and background daemon entrypoint.
- `src/GodotAgent.Mcp`: optional MCP adapter that exposes a thin tool layer over the same core contract.
- `godot/addons/godot_agent_runtime`: the Godot-side dev addon/autoload bridge.
- `samples`: benchmark scenario definitions for acceptance testing.
- `docs`: architecture notes and benchmark success criteria.

## CLI surface

The intended CLI surface is:

```text
godot-agent session run|stop|restart|status|logs
godot-agent capture screenshot
godot-agent input action|key|mouse
godot-agent inspect scene|node|focus|hover|monitors|errors
godot-agent test gdunit
godot-agent validate run <scenario>
```

Every command supports `--json` and returns compact structured output with artifact paths rather than inlining large payloads.

## Runtime model

1. `session run` starts a background daemon for the target project.
2. The daemon launches Godot and passes loopback bridge details through command-line user arguments.
3. The project-side addon connects back to the daemon and exposes runtime inspection, capture, and input hooks.
4. Subsequent CLI commands talk to the daemon, which either serves filesystem-backed state or forwards bridge RPC requests to the running game.

## Deterministic validation

`test gdunit` wraps `dotnet test` so `gdUnit4Net` output becomes another artifact source inside `.godot-agent/`.

## Local verification note

This workspace does not currently have `dotnet` or `godot` installed, so the code has been implemented as a source-first scaffold and could not be compiled or run in-place yet.

