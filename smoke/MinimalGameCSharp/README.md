# MinimalGameCSharp

This is a small managed smoke-project skeleton for the hybrid bridge.

What it is for:

- validating that `AgentRuntimeBridge.cs` can load inside a Godot C# project
- checking that the bootstrap chooses the managed bridge instead of the fallback
- providing a tiny local project to exercise `session run`, `inspect`, `capture`, and `session stop`

To use it, open the project in Godot or run:

```text
Godot --path smoke/MinimalGameCSharp --editor --build-solutions --quit
```

This sample now includes a minimal `.csproj`, `.sln`, and `NuGet.Config` pointing at Godot's bundled NuGet packages so the project can be built directly with `dotnet build`.

After the C# solution has been built, the `GodotAgentRuntime` autoload should prefer the managed bridge.
