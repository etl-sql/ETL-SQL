namespace ETL_SQL.Portal.Services;

/// <summary>
/// Whether a report's rows depend on who is looking. Such a report is never served from a shared
/// snapshot. A script that cannot be read counts as sensitive, so a read failure cannot turn a
/// per-viewer report into a shared one.
/// </summary>
public static class ReportIdentitySensitivity
{
    public static async Task<bool> IsSensitiveAsync(string scriptPath, CancellationToken ct)
    {
        try
        {
            return ETL_SQL.Core.Governance.RowLevelSecurityScan.ReferencesIdentity(
                await File.ReadAllTextAsync(scriptPath, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return true;
        }
    }
}
