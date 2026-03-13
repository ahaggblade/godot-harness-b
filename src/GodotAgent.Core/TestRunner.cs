using System.Diagnostics;
using System.Text.Json.Nodes;

namespace GodotAgent.Core;

public sealed class TestRunner
{
    public async Task<CommandResult> RunGdUnitAsync(string projectPath, string? csprojPath, CancellationToken cancellationToken)
    {
        var layout = new WorkspaceLayout(projectPath);
        layout.Ensure();

        var resultsDir = Path.Combine(layout.RootPath, "test-results");
        Directory.CreateDirectory(resultsDir);

        if (!HasDotnet())
        {
            return new CommandResult
            {
                Success = false,
                Command = "test gdunit",
                ExitCode = 127,
                Message = "dotnet was not found on PATH.",
                Data = new JsonObject { ["resultsDir"] = resultsDir },
            };
        }

        var target = csprojPath ?? DiscoverProjectFile(projectPath) ?? throw new InvalidOperationException("No .csproj file was found for gdUnit4Net execution.");
        var stdoutPath = layout.WriteTextArtifact("gdunit-stdout", "log", string.Empty);
        var trxPath = Path.Combine(resultsDir, "gdunit.trx");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("test");
        psi.ArgumentList.Add(target);
        psi.ArgumentList.Add("--logger");
        psi.ArgumentList.Add($"trx;LogFileName={Path.GetFileName(trxPath)}");
        psi.ArgumentList.Add("--results-directory");
        psi.ArgumentList.Add(resultsDir);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        await File.AppendAllTextAsync(stdoutPath.Path, stdout + stderr, cancellationToken);
        var artifacts = new List<ArtifactRef> { stdoutPath };
        if (File.Exists(trxPath))
        {
            artifacts.Add(new ArtifactRef
            {
                Id = Path.GetFileNameWithoutExtension(trxPath),
                Kind = "gdunit-trx",
                Path = trxPath,
                RelativePath = Path.GetRelativePath(layout.RootPath, trxPath),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        return new CommandResult
        {
            Success = process.ExitCode == 0,
            Command = "test gdunit",
            ExitCode = process.ExitCode,
            Message = process.ExitCode == 0 ? "gdUnit completed successfully." : "gdUnit failed.",
            Data = new JsonObject
            {
                ["target"] = target,
                ["resultsDir"] = resultsDir,
            },
            Artifacts = artifacts,
        };
    }

    private static bool HasDotnet()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => Path.Combine(entry, "dotnet"))
            .Any(File.Exists);
    }

    private static string? DiscoverProjectFile(string projectPath) =>
        Directory.EnumerateFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
}
