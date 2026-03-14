# UI Menu Sample

This is a managed Godot benchmark project for menu validation rather than a stub.

What it includes:

- three focusable menu buttons with obvious focus and hover styling
- keyboard navigation through `ui_up`, `ui_down`, and `ui_accept`
- mouse hover and click validation against stable screen coordinates
- managed `AgentHooks` methods for `reset_menu` and `get_menu_state`
- a local copy of the Godot Agent Runtime addon
- `NuGet.Config`, `.csproj`, and `.sln` files so the sample can be built directly

Success looks like:

- the agent can build the project and start it through `godot-agent`
- `inspect.focus` reports `StartButton` immediately after reset
- injected keyboard input moves focus to `OptionsButton`
- `ui_accept` activates the focused button and updates visible status text
- mouse motion updates `inspect.hover` to `QuitButton`
- a screenshot artifact clearly shows the highlighted menu state during validation
