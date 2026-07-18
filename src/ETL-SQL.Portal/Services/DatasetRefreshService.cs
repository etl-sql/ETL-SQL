using System.Security.Claims;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Services;

public sealed class DatasetRefreshService(
    IDatasetRegistry registry,
    ExecutionJobService jobService,
    AuditService audit)
{
    public async Task<DatasetRefreshResult> RefreshAsync(
        Dataset dataset,
        ClaimsPrincipal user,
        int currentUserId,
        bool isAdmin,
        string correlationId)
    {
        if (dataset.OwningReportId.HasValue)
        {
            var existingJobId = await jobService.GetActiveRefreshJobIdAsync(dataset.OwningReportId.Value);
            if (existingJobId is not null)
                return DatasetRefreshResult.Conflict(existingJobId);
        }

        await registry.SetStale(dataset.Name);

        if (dataset.OwningReport is not null)
        {
            var jobId = await jobService.EnqueueRefreshAsync(
                dataset.OwningReport.Id,
                currentUserId,
                dataset.OwningReport.ScriptPath,
                isAdministrator: isAdmin,
                actorType: user.FindFirstValue(TokenService.IdentityTypeClaim) == TokenService.ServiceIdentityType
                    ? "ServiceAccount" : "User",
                actorId: user.FindFirstValue(TokenService.ServiceAccountIdClaim) ?? currentUserId.ToString(),
                effectiveScopes: string.Join(' ', user.FindAll(TokenService.ScopeClaim)
                    .Select(value => value.Value).OrderBy(value => value)),
                correlationId: correlationId);
            await audit.LogAsync(currentUserId, "REFRESH_DATASET", "Dataset", dataset.Id.ToString(), dataset.Name);
            return DatasetRefreshResult.Queued(jobId);
        }

        await audit.LogAsync(
            currentUserId,
            "REFRESH_DATASET",
            "Dataset",
            dataset.Id.ToString(),
            dataset.Name + " (stale-only)");
        return DatasetRefreshResult.StaleOnly(
            $"Dataset '{dataset.Name}' has no linked report. Run the script that produced it to refresh the data.");
    }

    public async Task<DatasetRefreshStatusDto> GetStatusAsync(Dataset dataset)
    {
        if (dataset.OwningReportId.HasValue)
        {
            var jobId = await jobService.GetActiveRefreshJobIdAsync(dataset.OwningReportId.Value);
            if (jobId is not null)
            {
                var job = await jobService.GetAsync(jobId);
                return new DatasetRefreshStatusDto(
                    "InProgress", jobId,
                    job?.StartedAt, null, null,
                    dataset.LastRefresh, IsStale(dataset));
            }
        }

        return new DatasetRefreshStatusDto(
            "Idle", null, null, null, null,
            dataset.LastRefresh, IsStale(dataset));
    }

    private static bool IsStale(Dataset d)
    {
        if (!d.LastRefresh.HasValue) return true;
        if (string.IsNullOrWhiteSpace(d.Ttl)) return false;
        var ttl = ParseDuration(d.Ttl);
        return ttl.HasValue && d.LastRefresh.Value + ttl.Value < DateTime.UtcNow;
    }

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            value.Trim(),
            @"^(\d+)([smhd])$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        int amount = int.Parse(match.Groups[1].Value);
        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "S" => TimeSpan.FromSeconds(amount),
            "M" => TimeSpan.FromMinutes(amount),
            "H" => TimeSpan.FromHours(amount),
            "D" => TimeSpan.FromDays(amount),
            _ => null
        };
    }
}

public enum DatasetRefreshResultKind
{
    Conflict,
    Queued,
    StaleOnly
}

public sealed record DatasetRefreshResult(
    DatasetRefreshResultKind Kind,
    string? JobId,
    string? Message)
{
    public static DatasetRefreshResult Conflict(string jobId) =>
        new(DatasetRefreshResultKind.Conflict, jobId, "Refresh already in progress.");

    public static DatasetRefreshResult Queued(string jobId) =>
        new(DatasetRefreshResultKind.Queued, jobId, null);

    public static DatasetRefreshResult StaleOnly(string message) =>
        new(DatasetRefreshResultKind.StaleOnly, null, message);
}
