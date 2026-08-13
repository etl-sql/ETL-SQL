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

    public Task<JobDefinition?> GetJobAsync(string name) => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetJobAsync(scope.Context, name)
        : store.GetJobAsync(name);

    public Task<IEnumerable<JobHistoryEntry>> GetHistoryAsync(
        string? jobName = null, int limit = 100) => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetHistoryAsync(scope.Context, jobName, limit)
        : store.GetHistoryAsync(jobName, limit);

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

    public Task<string?> GetJobStateAsync(string jobName, string key) => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetJobStateAsync(scope.Context, jobName, key)
        : store.GetJobStateAsync(jobName, key);

    public Task SetJobStateAsync(string jobName, string key, string? value) => config.SharedTenancy.Enabled
        ? RequireTenantStore().SetJobStateAsync(scope.Context, jobName, key, value)
        : store.SetJobStateAsync(jobName, key, value);

    public Task<IReadOnlyList<JobStateEntry>> GetJobStatesAsync(
        string? jobName = null, int limit = 1000) => config.SharedTenancy.Enabled
        ? RequireTenantStore().GetJobStatesAsync(scope.Context, jobName, limit)
        : store.GetJobStatesAsync(jobName, limit);

    private ITenantJobEvidenceStore RequireTenantStore() => store as ITenantJobEvidenceStore
        ?? throw new InvalidOperationException(
            "Shared job evidence requires a tenant-qualified provider store.");
}
