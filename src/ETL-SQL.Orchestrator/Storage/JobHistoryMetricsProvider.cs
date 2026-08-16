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
        string? tenantId, string jobName, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Read by name, not identity: a HISTORICAL baseline is about what has run under this name in
        // this tenant, and history outlives the job it belongs to. Over-fetch a little — the store
        // returns runs in start order regardless of outcome, and a failed or in-flight run is not a
        // baseline.
        var history = await store.GetHistoryForNameAsync(
            tenantId, jobName, Math.Clamp(limit * 4, limit, 1000));

        return history
            .Where(IsCompletedSuccessfully)
            .OrderByDescending(h => h.EndTime ?? h.StartTime)
            .Take(limit)
            .Select(h => new JobRunMetrics(h.RowsProcessed, h.RowsQuarantined, h.RowsWarned))
            .ToList();
    }

    public async Task<IReadOnlyList<ColumnRunMetrics>> GetRecentColumnMetricsAsync(
        string? tenantId,
        string jobName,
        string? targetTable,
        string columnName,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobId = await ResolveAsync(tenantId, jobName);
        return jobId.IsAssigned
            ? await store.GetRecentColumnMetricsAsync(jobId, targetTable, columnName, limit)
            : Array.Empty<ColumnRunMetrics>();
    }

    public async Task<AssertJobAlertState?> GetAssertJobAlertStateAsync(
        string? tenantId,
        string jobName,
        string assertionKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobId = await ResolveAsync(tenantId, jobName);
        if (!jobId.IsAssigned) return null;

        var json = await store.GetJobStateAsync(jobId, AlertStatePrefix + assertionKey);
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
        string? tenantId,
        string jobName,
        string assertionKey,
        AssertJobAlertState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Best-effort, unlike the quarantine manifest. `ASSERT JOB 'import'` names a job the script
        // author typed, and in an ad-hoc run that name may have no orchestrator row at all — there is
        // simply nowhere to keep the memo. Alert state only suppresses a repeat notification, so
        // losing it means the next failure alerts again: noisier, never wrong. Refusing here would
        // instead fail a run over its own alert bookkeeping.
        var jobId = await ResolveAsync(tenantId, jobName);
        if (!jobId.IsAssigned) return;
        await store.SetJobStateAsync(
            jobId,
            AlertStatePrefix + assertionKey,
            JsonSerializer.Serialize(state));
    }

    public async Task<QuarantineReplayManifest?> GetQuarantineReplayManifestAsync(
        string? tenantId,
        string jobName,
        string quarantineTarget,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobId = await ResolveAsync(tenantId, jobName);
        if (!jobId.IsAssigned) return null;

        var json = await store.GetJobStateAsync(
            jobId, QuarantineManifestPrefix + NormalizeStateKeyPart(quarantineTarget));
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
        string? tenantId,
        QuarantineReplayManifest manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobId = await RequireAsync(tenantId, manifest.JobName);
        await store.SetJobStateAsync(
            jobId,
            QuarantineManifestPrefix + NormalizeStateKeyPart(manifest.QuarantineTarget),
            JsonSerializer.Serialize(manifest));
    }

    public async Task<bool> TryAcquireQuarantineReplayLeaseAsync(
        string? tenantId,
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
        var jobId = await RequireAsync(tenantId, jobName);
        return await locks.TryAcquireLockAsync(QuarantineReplayLockName(jobId, quarantineTarget), owner, ttl);
    }

    public async Task ReleaseQuarantineReplayLeaseAsync(
        string? tenantId,
        string jobName,
        string quarantineTarget,
        string owner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (locks == null) return;
        var jobId = await ResolveAsync(tenantId, jobName);
        if (jobId.IsAssigned)
            await locks.ReleaseLockAsync(QuarantineReplayLockName(jobId, quarantineTarget), owner);
    }

    /// <summary>
    /// The single point where a name from a script becomes an identity. Unresolved is not an error
    /// here: a read about a job that does not exist in this tenant has no answer, which the readers
    /// above report as "no baseline" rather than as a failure.
    /// </summary>
    private async Task<JobId> ResolveAsync(string? tenantId, string jobName) =>
        (await store.GetJobAsync(tenantId, jobName))?.Id ?? JobId.None;

    /// <summary>
    /// As <see cref="ResolveAsync"/>, but for the writers. A write has nowhere to go without an
    /// identity, and silently discarding alert state or a quarantine manifest is how a replay ends
    /// up double-inserting, so this says so instead.
    /// </summary>
    private async Task<JobId> RequireAsync(string? tenantId, string jobName)
    {
        var jobId = await ResolveAsync(tenantId, jobName);
        if (!jobId.IsAssigned)
            throw new InvalidOperationException(
                $"Job '{jobName}' does not exist" +
                (string.IsNullOrWhiteSpace(tenantId) ? "" : $" in tenant '{tenantId}'") +
                ", so there is nothing to record this state against.");
        return jobId;
    }

    private static bool IsCompletedSuccessfully(JobHistoryEntry entry) =>
        entry.EndTime.HasValue
        && (entry.Status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
            || entry.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeStateKeyPart(string value) =>
        value.Trim().TrimStart('#').ToLowerInvariant();

    // Keyed by identity, so two tenants replaying their own job of the same name do not contend for
    // one lock — and so neither can block the other indefinitely.
    private static string QuarantineReplayLockName(JobId jobId, string quarantineTarget) =>
        QuarantineReplayLockPrefix + jobId.Value + ":" + NormalizeStateKeyPart(quarantineTarget);
}
