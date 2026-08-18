using System.Collections.Concurrent;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Gateway;

/// <summary>Summary metadata for an active connected Gateway session.</summary>
public sealed record GatewaySessionInfo(
    string TenantId,
    string GatewayId,
    string WorkloadPublicKeyThumbprint,
    DateTimeOffset ConnectedUtc);

/// <summary>Represents a live server-side handle to an authenticated on-premises Gateway.</summary>
public interface IGatewaySession : IAsyncDisposable
{
    string TenantId { get; }
    string GatewayId { get; }
    string WorkloadPublicKeyThumbprint { get; }
    DateTimeOffset ConnectedUtc { get; }
    bool IsActive { get; }

    Task<GatewayExecutionResult> ExecuteAsync(
        GatewayOperation operation,
        string? request,
        IReadOnlyList<string>? parameters,
        CancellationToken cancellationToken);
}

/// <summary>
/// Thread-safe registry for active multi-tenant Gateway sessions.
///
/// <para>Strictly partitions sessions by (TenantId, GatewayId). Sessions belonging to tenant A cannot
/// be looked up or manipulated under tenant B's scope.</para>
/// </summary>
public sealed class GatewaySessionRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string GatewayId), IGatewaySession> _sessions = new();

    public bool TryRegister(IGatewaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = (session.TenantId, session.GatewayId);
        return _sessions.TryAdd(key, session);
    }

    public bool TryGet(string tenantId, string gatewayId, out IGatewaySession? session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        return _sessions.TryGetValue((tenantId, gatewayId), out session);
    }

    public bool Unregister(string tenantId, string gatewayId, IGatewaySession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        ArgumentNullException.ThrowIfNull(session);

        var key = (tenantId, gatewayId);
        if (_sessions.TryGetValue(key, out var current) && ReferenceEquals(current, session))
        {
            return _sessions.TryRemove(key, out _);
        }
        return false;
    }

    public IReadOnlyList<GatewaySessionInfo> ListActive(string? tenantId = null)
    {
        var query = _sessions.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(s => string.Equals(s.TenantId, tenantId, StringComparison.Ordinal));
        }

        return query
            .Select(s => new GatewaySessionInfo(s.TenantId, s.GatewayId, s.WorkloadPublicKeyThumbprint, s.ConnectedUtc))
            .ToList();
    }
}
