# Architecture

## Components

- `godot-agent` CLI: the primary interface for agents and scripts.
- background daemon: a hidden CLI mode that owns the active session, TCP listener, and Godot child process.
- Godot addon/autoload: a C#-first bridge with a tiny GDScript bootstrap and a diagnostic-only GDScript fallback.
- MCP adapter: optional stdio server that exposes a narrow tool surface over the same core contract.

## Design constraints

- CLI first, MCP second.
- Artifacts over inline payloads.
- Godot `4.6.1` baseline.
- macOS and Linux desktop workflows first.
- C# deterministic tests integrate through `dotnet test` and `gdUnit4Net`, not a bespoke runner.

## Session flow

1. `session run` creates a session id, token, and manifest.
2. The CLI respawns itself in hidden daemon mode.
3. The daemon opens a loopback TCP listener and launches Godot with agent user args.
4. The GDScript bootstrap instantiates the managed C# bridge when possible, otherwise it falls back to a local diagnostic bridge that reports why the managed path failed.
5. The active runtime bridge connects back with the session id and token.
6. Runtime commands are forwarded over the bridge, while status and artifact metadata remain filesystem-backed.

## Failure model

- The managed C# bridge is the only full-featured runtime path.
- The GDScript fallback is intentionally thin: it connects, reports degraded state, exposes minimal diagnostics, and makes managed bridge failures visible through session metadata and runtime errors.
- This keeps the development environment easy to debug without maintaining two separate full runtime implementations.

## Artifact strategy

All high-volume outputs live under `.godot-agent/`:

- `sessions/`: manifest snapshots
- `artifacts/`: logs, screenshots, and serialized reports
- `reports/`: reserved for higher-level summaries and future exports

This keeps model context small by returning file paths and concise summaries instead of streaming large bodies into the agent.
