using ETL_SQL.Orchestrator.Storage;

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

        var candidates = new[]
        {
            // Global AppData path used by all ETL-SQL instances (preferred).
            SQLiteJobHistoryStore.DefaultDbPath(),
            // Legacy: relative paths near the portal DB (fallback for old deployments).
            portalDir is null ? null : Path.Combine(portalDir, "etlsql.db"),
            portalDir is null ? null : Path.Combine(portalDir, "..", "etlsql.db"),
        };

        _cachedPath = candidates.FirstOrDefault(p => p is not null && File.Exists(p));
        return _cachedPath;
    }
}
