namespace ETL_SQL.ReportPortal.Services;

/// <summary>Locates the Orchestrator's SQLite database at runtime.</summary>
public class OrchestratorDbLocator(PortalConfig config)
{
    private string? _cachedPath;

    public string? Resolve()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath))
            return _cachedPath;

        var portalDir = Path.GetDirectoryName(Path.GetFullPath(config.DatabasePath));
        if (portalDir is null) return null;

        var candidates = new[]
        {
            Path.Combine(portalDir, "etlsql.db"),
            Path.Combine(portalDir, "..", "etlsql.db"),
        };

        _cachedPath = candidates.FirstOrDefault(File.Exists);
        return _cachedPath;
    }
}
