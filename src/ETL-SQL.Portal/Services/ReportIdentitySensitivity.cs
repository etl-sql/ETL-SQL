using System.Security.Claims;

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

    /// <summary>
    /// Whether the report is one to run live for its reader: its script is there to run and reads
    /// the viewer's identity. A script that is gone cannot be run for anyone, and a report that was
    /// ever sensitive has no snapshot to fall back to, so a missing file is left to the snapshot path.
    /// </summary>
    public static async Task<bool> IsRunLiveAsync(string scriptPath, CancellationToken ct) =>
        File.Exists(scriptPath) && await IsSensitiveAsync(scriptPath, ct);

    /// <summary>
    /// Whether this caller may cause a live run by reading. A person may: Read on the folder is
    /// enough to open such a report. A service account may only with the scope that lets it run
    /// reports, because for a service account reading has never included running.
    /// </summary>
    public static bool CallerMayRunByReading(ClaimsPrincipal user) =>
        user.FindFirstValue(TokenService.IdentityTypeClaim) != TokenService.ServiceIdentityType
        || user.FindAll(TokenService.ScopeClaim).Any(scope =>
            string.Equals(scope.Value, ServiceAccountScopes.ReportsExecute, StringComparison.Ordinal));
}
