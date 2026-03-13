# Greybox Controller Sample

Build a minimal 2D scene with:

- one controllable character
- `move_right` and `jump` input actions
- a visible floor and spawn point
- a camera that keeps the character in frame
- optional `AgentHooks.reset_world()` support

Success looks like:

- the agent can reset the scene
- move and jump through injected input
- inspect the runtime scene state
- capture a frame that shows the expected jump posture and framing

