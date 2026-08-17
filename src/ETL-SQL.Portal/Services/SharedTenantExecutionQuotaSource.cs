using System.Collections.Concurrent;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Reporting;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Reads a Shared tenant's provisioned <c>MaxReportSessions</c> back out of the control plane, so the
/// interactive-session quota an operator agreed to at provisioning time is the one enforced at run
/// time. A Dedicated deployment materializes the same number into its own node configuration; in
/// Shared there is one node for every tenant, so it has to be resolved per execution.
///
/// <para>Results are cached briefly: this sits in the execution admission path, and a tenant's
/// quota changes only through a lifecycle operation.</para>
/// </summary>
public sealed class SharedTenantExecutionQuotaSource(
    PortalConfig config,
    ISharedTenantLifecycleStore store,
    TimeSpan? cacheDuration = null) : ITenantExecutionQuotaSource
{
    private readonly TimeSpan _cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, (int? Limit, DateTimeOffset ExpiresUtc)> _cache =
        new(StringComparer.Ordinal);

    public async ValueTask<int?> GetMaxConcurrentExecutionsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (!config.SharedTenancy.Enabled || string.IsNullOrWhiteSpace(tenantId))
            return null;

        var now = DateTimeOffset.UtcNow;
        if (_cache.TryGetValue(tenantId, out var cached) && cached.ExpiresUtc > now)
            return cached.Limit;

        var state = await store.GetSharedTenantStateAsync(
            TenantContext.FromVerifiedCredential(tenantId), cancellationToken);

        // A tenant with no provisioned record is not a Shared tenant under quota; a deleted one is
        // fenced by the lifecycle path rather than throttled here. Neither case invents a ceiling.
        var limit = state is null || state.State == "Deleted" || state.MaxReportSessions <= 0
            ? (int?)null
            : state.MaxReportSessions;
        _cache[tenantId] = (limit, now.Add(_cacheDuration));
        return limit;
    }

    /// <summary>Drops a cached quota so a lifecycle change takes effect without waiting out the TTL.</summary>
    public void Invalidate(string tenantId) => _cache.TryRemove(tenantId, out _);
}
