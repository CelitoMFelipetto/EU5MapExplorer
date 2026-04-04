using System.Text.Json;

namespace EU5MapExplorer.Api.Services;

public class ProjectConfig
{
    public string DataPath { get; } = null!;
    public string GameVersion { get; } = null!;
    public string ConnectionString { get; } = null!;

    public static ProjectConfig Instance
    {
        get { return _instance ??= new ProjectConfig(); }
    }

    private static ProjectConfig? _instance;

    private ProjectConfig()
    {
        // ── Find project dir by walking up from CWD ──────────────────────────────────

        var projectDir = Directory.GetCurrentDirectory();
        while (
            projectDir != null
            && !File.Exists(Path.Combine(projectDir, "EU5MapExplorer.Api.csproj"))
        )
            projectDir = Directory.GetParent(projectDir)?.FullName;

        if (projectDir == null)
        {
            Console.Error.WriteLine(
                "Error: Could not locate EU5MapExplorer.Api.csproj. Run from inside the project directory."
            );
            return;
        }
        foreach (var filename in new[] { "appsettings.Development.json", "appsettings.json" })
        {
            var fullPath = Path.Combine(projectDir, filename);
            if (!File.Exists(fullPath))
                continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));

            if (
                doc.RootElement.TryGetProperty("EU5", out var eu5)
                && eu5.TryGetProperty("DataPath", out var dp)
                && !string.IsNullOrWhiteSpace(dp.GetString())
            )
            {
                DataPath = dp.GetString() ?? "";
            }

            if (
                GameVersion == null
                && doc.RootElement.TryGetProperty("EU5", out var eu5v)
                && eu5v.TryGetProperty("Version", out var ver)
                && !string.IsNullOrWhiteSpace(ver.GetString())
            )
            {
                GameVersion = ver.GetString() ?? "";
            }

            if (
                ConnectionString == null
                && doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)
                && cs.TryGetProperty("Default", out var csDefault)
                && !string.IsNullOrWhiteSpace(csDefault.GetString())
            )
            {
                ConnectionString = csDefault.GetString() ?? "";
            }
        }

        if (DataPath == null)
        {
            throw new InvalidOperationException(
                "Error: EU5:DataPath is not configured in appsettings.Development.json."
            );
        }

        GameVersion ??= "1.1.10";
        Console.WriteLine($"Game version: {GameVersion}");

        if (ConnectionString == null)
        {
            throw new InvalidOperationException(
                "Error: ConnectionStrings:Default is not configured."
            );
        }
    }
}
