# Greybox Controller Sample

This is now a real Godot C# benchmark project, not just a prompt stub.

What it includes:

- one controllable `CharacterBody2D`
- runtime-created `move_left`, `move_right`, and `jump` input actions
- a visible floor, a follow camera, and simple greybox visuals
- managed `AgentHooks` autoload methods for `reset_world` and `get_semantic_state`
- a local copy of the Godot Agent Runtime addon
- `NuGet.Config`, `.csproj`, and `.sln` files so the sample can be built directly

Success looks like:

- the agent can build the project and start it through `godot-agent`
- `reset_world` restores the player to a deterministic spawn
- injected input moves the player right and leaves the floor at least once
- `get_semantic_state` reports `movedRightEnough = true` and `jumped = true`
- a screenshot artifact is produced during validation
