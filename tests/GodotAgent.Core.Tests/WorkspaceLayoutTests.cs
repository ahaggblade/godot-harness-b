using System.Text.Json.Nodes;
using GodotAgent.Core;

namespace GodotAgent.Core.Tests;

public sealed class WorkspaceLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "godot-agent-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveManifest_RoundTrips()
    {
        var layout = new WorkspaceLayout(_root);
        layout.Ensure();

        var manifest = new SessionManifest
        {
            SessionId = "session-123",
            ProjectPath = layout.ProjectPath,
            State = "running",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            DaemonPid = 42,
            Port = 9999,
            Token = "token",
        };

        layout.SaveManifest(manifest);
        layout.SetActiveSession(manifest.SessionId);

        var loaded = layout.LoadManifest();

        Assert.NotNull(loaded);
        Assert.Equal("session-123", loaded!.SessionId);
        Assert.Equal(9999, loaded.Port);
    }

    [Fact]
    public void WriteJsonArtifact_CreatesArtifactFile()
    {
        var layout = new WorkspaceLayout(_root);
        var artifact = layout.WriteJsonArtifact("report", new JsonObject { ["ok"] = true });

        Assert.True(File.Exists(artifact.Path));
        Assert.EndsWith(".json", artifact.Path, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

