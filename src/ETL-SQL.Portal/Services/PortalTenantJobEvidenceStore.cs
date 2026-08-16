using ETL_SQL.Core.Data;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Selects the tenant-qualified job-evidence contract on Shared hosts while preserving the legacy
/// deployment-wide contract for Solo/Team and one-store-per-tenant Dedicated installations.
/// </summary>
public sealed class PortalTenantJobEvidenceStore(
    IJobHistoryStore store,
    DatasetTenantScope scope,
    PortalConfig config)
{
    public Task<IEnumerable<JobDefinition>> GetAllJobsAsync() => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetAllJobsAsync(scope.Context)
        : store.GetAllJobsAsync();

    public async Task<JobDefinition?> GetJobAsync(string name) => config.SharedTenancy.Enabled
        ? await RequireTenantStore().GetJobAsync(scope.Context, name)
        : await store.GetJobAsync(scope.TenantId, name) ?? await store.GetJobAsync(null, name);

    // History stays name-addressed: it outlives the job it belongs to, so reading it by identity
    // would report a dropped job's runs as never having happened. The tenant predicate is what keeps
    // a name shared between tenants from reaching the wrong runs.
    public Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(
        string? jobName = null, int limit = 100) => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetHistoryAsync(scope.Context, jobName, limit)
        : string.IsNullOrWhiteSpace(jobName)
            ? store.GetHistoryAsync(limit: limit)
            : LocalHistoryForNameAsync(jobName, limit);

    public Task<JobHistoryEntry?> GetHistoryEntryAsync(long entryId) => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetHistoryEntryAsync(scope.Context, entryId)
        : store.GetHistoryEntryAsync(entryId);

    public Task<IReadOnlyList<ETL_SQL.Core.Profiling.StatementMetricsPayload>>
        GetJobStatementMetricsAsync(long entryId) => config.SharedTenancy.Enabled
            ? RequireTenantStore().GetJobStatementMetricsAsync(scope.Context, entryId)
            : store.GetJobStatementMetricsAsync(entryId);

    public Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForJobAsync(
        string jobName, int limit = 1000) => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetDataQualityFailuresForJobAsync(scope.Context, jobName, limit)
        : store.GetDataQualityFailuresForJobAsync(jobName, limit);

    public Task<IReadOnlyList<JobDataQualityFailure>> GetDataQualityFailuresForRunAsync(
        long entryId, int limit = 1000) => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetDataQualityFailuresForRunAsync(scope.Context, entryId, limit)
        : store.GetDataQualityFailuresForRunAsync(entryId, limit);

    // Job state, unlike history, is keyed by identity and is deleted with the job, so these resolve
    // the name in the caller's tenant first. A name that resolves to nothing has no state, which is
    // reported as absent rather than as every job's state.
    public async Task<string?> GetJobStateAsync(string jobName, string key)
    {
        if (config.SharedTenancy.Enabled)
            return await RequireTenantStore().GetJobStateAsync(scope.Context, jobName, key);
        var jobId = await ResolveAsync(jobName);
        return jobId.IsAssigned ? await store.GetJobStateAsync(jobId, key) : null;
    }

    public async Task SetJobStateAsync(string jobName, string key, string? value)
    {
        if (config.SharedTenancy.Enabled)
        {
            await RequireTenantStore().SetJobStateAsync(scope.Context, jobName, key, value);
            return;
        }
        var jobId = await ResolveAsync(jobName);
        if (!jobId.IsAssigned)
            throw new InvalidOperationException(
                $"Job '{jobName}' does not exist, so there is nothing to record this state against.");
        await store.SetJobStateAsync(jobId, key, value);
    }

    public async Task<IReadOnlyList<JobStateEntry>> GetJobStatesAsync(
        string? jobName = null, int limit = 1000)
    {
        if (config.SharedTenancy.Enabled)
            return await RequireTenantStore().GetJobStatesAsync(scope.Context, jobName, limit);
        if (string.IsNullOrWhiteSpace(jobName))
            return await store.GetJobStatesAsync(limit: limit);
        var jobId = await ResolveAsync(jobName);
        return jobId.IsAssigned ? await store.GetJobStatesAsync(jobId, limit) : [];
    }

    /// <summary>
    /// Resolves a job name for a <b>non-shared</b> Portal, which spans two tenant keys rather than one.
    ///
    /// <para>The Portal binds its own objects to the configured tenant (<c>portal-host</c> by default),
    /// but the same orchestrator database is written by the scheduler and the CLI, which have no signed
    /// tenant and bind to the unbound scope. On a single-tenant deployment both are this deployment, so
    /// filtering on the Portal's key alone would hide the engine's own jobs — and job state, quarantine
    /// manifests and watermarks are mostly written by exactly those runs. There is no isolation lost:
    /// where isolation is the point, <c>SharedTenancy.Enabled</c> is on and the tenant-qualified store
    /// answers instead, on a verified <see cref="ETL_SQL.Core.Multitenancy.TenantContext"/>.</para>
    /// </summary>
    private async Task<JobId> ResolveAsync(string jobName) =>
        (await store.GetJobAsync(scope.TenantId, jobName)
            ?? await store.GetJobAsync(null, jobName))?.Id ?? JobId.None;

    /// <summary>Name-addressed history across the same two scopes. See <see cref="ResolveAsync"/>.</summary>
    private async Task<IEnumerable<JobHistoryEntry>> LocalHistoryForNameAsync(string jobName, int limit)
    {
        var scoped = await store.GetHistoryForNameAsync(scope.TenantId, jobName, limit);
        var unbound = string.IsNullOrWhiteSpace(scope.TenantId)
            ? []
            : await store.GetHistoryForNameAsync(null, jobName, limit);
        return scoped.Concat(unbound)
            .OrderByDescending(entry => entry.StartTime)
            .ThenByDescending(entry => entry.Id)
            .Take(limit);
    }

    private ITenantJobEvidenceStore RequireTenantStore() => store as ITenantJobEvidenceStore
        ?? throw new InvalidOperationException(
            "Shared job evidence requires a tenant-qualified provider store.");
}
