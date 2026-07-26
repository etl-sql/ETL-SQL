using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage;

/// <summary>
/// Orchestrator-side implementation of the <see cref="IJobMetricsProvider"/> seam, reading
/// previous runs' recorded metrics from the job-history store for
/// <c>ASSERT JOB … WITHIN … OF HISTORICAL</c> baselines.
/// </summary>
public sealed class JobHistoryMetricsProvider(IJobHistoryStore store, IClusterLockStore? locks = null) : IJobMetricsProvider
{
    private const string AlertStatePrefix = "dq:assert-alert:";
    private const string QuarantineManifestPrefix = "dq:quarantine-manifest:";
    private const string QuarantineReplayLockPrefix = "dq:quarantine-replay:";

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

    public async Task<AssertJobAlertState?> GetAssertJobAlertStateAsync(
        string jobName,
        string assertionKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await store.GetJobStateAsync(jobName, AlertStatePrefix + assertionKey);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<AssertJobAlertState>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAssertJobAlertStateAsync(
        string jobName,
        string assertionKey,
        AssertJobAlertState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await store.SetJobStateAsync(
            jobName,
            AlertStatePrefix + assertionKey,
            JsonSerializer.Serialize(state));
    }

    public async Task<QuarantineReplayManifest?> GetQuarantineReplayManifestAsync(
        string jobName,
        string quarantineTarget,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await store.GetJobStateAsync(jobName, QuarantineManifestPrefix + NormalizeStateKeyPart(quarantineTarget));
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<QuarantineReplayManifest>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveQuarantineReplayManifestAsync(
        QuarantineReplayManifest manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await store.SetJobStateAsync(
            manifest.JobName,
            QuarantineManifestPrefix + NormalizeStateKeyPart(manifest.QuarantineTarget),
            JsonSerializer.Serialize(manifest));
    }

    public async Task<bool> TryAcquireQuarantineReplayLeaseAsync(
        string jobName,
        string quarantineTarget,
        string owner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Fail closed. This lease is the only thing stopping two concurrent replays from consuming
        // the same released set and double-inserting into the production target, so a host that
        // did not supply a lock store must not silently get an unguarded replay.
        if (locks == null)
            throw new InvalidOperationException(
                "Quarantine replay requires a cluster lock store; this host was constructed without one, " +
                "and running unguarded could double-insert released rows.");
        return await locks.TryAcquireLockAsync(QuarantineReplayLockName(jobName, quarantineTarget), owner, ttl);
    }

    public async Task ReleaseQuarantineReplayLeaseAsync(
        string jobName,
        string quarantineTarget,
        string owner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (locks != null)
            await locks.ReleaseLockAsync(QuarantineReplayLockName(jobName, quarantineTarget), owner);
    }

    private static bool IsCompletedSuccessfully(JobHistoryEntry entry) =>
        entry.EndTime.HasValue
        && (entry.Status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
            || entry.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeStateKeyPart(string value) =>
        value.Trim().TrimStart('#').ToLowerInvariant();

    private static string QuarantineReplayLockName(string jobName, string quarantineTarget) =>
        QuarantineReplayLockPrefix + NormalizeStateKeyPart(jobName) + ":" + NormalizeStateKeyPart(quarantineTarget);
}
