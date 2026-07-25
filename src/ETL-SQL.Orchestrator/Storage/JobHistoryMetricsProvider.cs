using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage;

/// <summary>
/// Orchestrator-side implementation of the <see cref="IJobMetricsProvider"/> seam, reading
/// previous runs' recorded metrics from the job-history store for
/// <c>ASSERT JOB … WITHIN … OF HISTORICAL</c> baselines.
/// </summary>
public sealed class JobHistoryMetricsProvider(IJobHistoryStore store) : IJobMetricsProvider
{
    public async Task<IReadOnlyList<JobRunMetrics>> GetRecentRunMetricsAsync(
        string jobName, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Over-fetch a little: the store returns runs in start order regardless of outcome, and a
        // failed or in-flight run is not a baseline.
        var history = await store.GetHistoryAsync(jobName, Math.Clamp(limit * 4, limit, 1000));

        return history
            .Where(IsCompletedSuccessfully)
            .OrderByDescending(h => h.EndTime ?? h.StartTime)
            .Take(limit)
            .Select(h => new JobRunMetrics(h.RowsProcessed, h.RowsQuarantined, h.RowsWarned))
            .ToList();
    }

    public async Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(
        string jobName,
        string? targetTable,
        string columnName,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await store.GetRecentColumnMetricsAsync(jobName, targetTable, columnName, limit);
    }

    private static bool IsCompletedSuccessfully(JobHistoryEntry entry) =>
        entry.EndTime.HasValue
        && (entry.Status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
            || entry.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase));
}
