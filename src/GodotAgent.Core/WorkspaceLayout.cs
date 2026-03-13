using System.Text;
using System.Text.Json.Nodes;

namespace GodotAgent.Core;

public sealed class WorkspaceLayout
{
    public WorkspaceLayout(string projectPath)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        RootPath = Path.Combine(ProjectPath, ".godot-agent");
        SessionsPath = Path.Combine(RootPath, "sessions");
        ArtifactsPath = Path.Combine(RootPath, "artifacts");
        ReportsPath = Path.Combine(RootPath, "reports");
        ActiveSessionPath = Path.Combine(RootPath, "active-session.txt");
    }

    public string ProjectPath { get; }
    public string RootPath { get; }
    public string SessionsPath { get; }
    public string ArtifactsPath { get; }
    public string ReportsPath { get; }
    public string ActiveSessionPath { get; }

    public void Ensure()
    {
        Directory.CreateDirectory(ProjectPath);
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(SessionsPath);
        Directory.CreateDirectory(ArtifactsPath);
        Directory.CreateDirectory(ReportsPath);
    }

    public string GetSessionPath(string sessionId) => Path.Combine(SessionsPath, $"{sessionId}.json");

    public string ResolveScenarioPath(string scenarioPath)
    {
        if (Path.IsPathRooted(scenarioPath))
        {
            return scenarioPath;
        }

        return Path.Combine(ProjectPath, scenarioPath);
    }

    public void SetActiveSession(string sessionId)
    {
        Ensure();
        File.WriteAllText(ActiveSessionPath, sessionId + Environment.NewLine, Encoding.UTF8);
    }

    public string? GetActiveSession()
    {
        if (!File.Exists(ActiveSessionPath))
        {
            return null;
        }

        var value = File.ReadAllText(ActiveSessionPath, Encoding.UTF8).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public SessionManifest? LoadManifest(string? sessionId = null)
    {
        sessionId ??= GetActiveSession();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var path = GetSessionPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonDefaults.Deserialize<SessionManifest>(json);
    }

    public void SaveManifest(SessionManifest manifest)
    {
        manifest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(GetSessionPath(manifest.SessionId), JsonDefaults.Serialize(manifest), Encoding.UTF8);
    }

    public ArtifactRef WriteTextArtifact(string kind, string extension, string content)
    {
        Ensure();
        var id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var relativePath = Path.Combine("artifacts", $"{kind}-{id}.{extension.TrimStart('.')}");
        var fullPath = Path.Combine(RootPath, relativePath);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return new ArtifactRef
        {
            Id = id,
            Kind = kind,
            Path = fullPath,
            RelativePath = relativePath,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public ArtifactRef WriteBytesArtifact(string kind, string extension, byte[] bytes)
    {
        Ensure();
        var id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var relativePath = Path.Combine("artifacts", $"{kind}-{id}.{extension.TrimStart('.')}");
        var fullPath = Path.Combine(RootPath, relativePath);
        File.WriteAllBytes(fullPath, bytes);
        return new ArtifactRef
        {
            Id = id,
            Kind = kind,
            Path = fullPath,
            RelativePath = relativePath,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public ArtifactRef WriteJsonArtifact(string kind, JsonNode node) =>
        WriteTextArtifact(kind, "json", node.ToJsonString(JsonDefaults.Options));
}
