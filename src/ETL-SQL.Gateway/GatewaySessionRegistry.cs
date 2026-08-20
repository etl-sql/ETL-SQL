using System.Collections.Concurrent;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Gateway;

/// <summary>Summary metadata for an active connected Gateway session node.</summary>
public sealed record GatewaySessionInfo(
    string TenantId,
    string GatewayId,
    string NodeId,
    string WorkloadPublicKeyThumbprint,
    DateTimeOffset ConnectedUtc,
    bool IsActive = true);

/// <summary>Cluster-level overview showing active node capacity for a Gateway cluster.</summary>
public sealed record GatewayClusterInfo(
    string TenantId,
    string GatewayId,
    int TotalNodes,
    int ActiveNodes,
    IReadOnlyList<GatewaySessionInfo> Nodes);

/// <summary>Represents a live server-side handle to an authenticated on-premises Gateway.</summary>
public interface IGatewaySession : IAsyncDisposable
{
    string TenantId { get; }
    string GatewayId { get; }
    string NodeId { get; }
    string WorkloadPublicKeyThumbprint { get; }
    DateTimeOffset ConnectedUtc { get; }
    bool IsActive { get; }
    IReadOnlyList<GatewayPublishedResource> PublishedResources => [];

    Task<GatewayExecutionResult> ExecuteAsync(
        GatewayOperation operation,
        string? request,
        IReadOnlyList<string>? parameters,
        CancellationToken cancellationToken);
}

/// <summary>
/// Active-Active multi-node cluster pool for a single (TenantId, GatewayId) pair.
/// Manages round-robin load balancing and node failover.
/// </summary>
public sealed class GatewayClusterPool
{
    private readonly ConcurrentDictionary<string, IGatewaySession> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private int _roundRobinIndex;

    public string TenantId { get; }
    public string GatewayId { get; }

    public GatewayClusterPool(string tenantId, string gatewayId)
    {
        TenantId = tenantId;
        GatewayId = gatewayId;
    }

    public bool TryAddNode(IGatewaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _nodes[session.NodeId] = session;
        return true;
    }

    public bool TryRemoveNode(string nodeId, IGatewaySession session)
    {
        if (_nodes.TryGetValue(nodeId, out var existing) && ReferenceEquals(existing, session))
        {
            return _nodes.TryRemove(nodeId, out _);
        }
        return false;
    }

    public bool IsEmpty => _nodes.IsEmpty;

    public bool TryGetSession(out IGatewaySession? session)
    {
        var active = _nodes.Values.Where(s => s.IsActive).ToList();
        if (active.Count == 0)
        {
            session = null;
            return false;
        }

        var idx = (uint)Interlocked.Increment(ref _roundRobinIndex);
        session = active[(int)(idx % active.Count)];
        return true;
    }

    public IReadOnlyList<GatewaySessionInfo> ListNodes()
    {
        return _nodes.Values
            .Select(s => new GatewaySessionInfo(
                s.TenantId, s.GatewayId, s.NodeId, s.WorkloadPublicKeyThumbprint, s.ConnectedUtc, s.IsActive))
            .ToList();
    }
}

/// <summary>
/// Thread-safe registry for active multi-tenant Gateway cluster pools.
///
/// <para>Strictly partitions sessions by (TenantId, GatewayId). Supports multi-node active-active
/// clustering with automatic load balancing across connected nodes.</para>
/// </summary>
public sealed class GatewaySessionRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string GatewayId), GatewayClusterPool> _clusters = new();

    public bool TryRegister(IGatewaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = (session.TenantId, session.GatewayId);
        var pool = _clusters.GetOrAdd(key, k => new GatewayClusterPool(k.TenantId, k.GatewayId));
        return pool.TryAddNode(session);
    }

    public bool TryGet(string tenantId, string gatewayId, out IGatewaySession? session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);

        if (_clusters.TryGetValue((tenantId, gatewayId), out var pool))
        {
            return pool.TryGetSession(out session);
        }

        session = null;
        return false;
    }

    public bool Unregister(string tenantId, string gatewayId, IGatewaySession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        ArgumentNullException.ThrowIfNull(session);

        var key = (tenantId, gatewayId);
        if (_clusters.TryGetValue(key, out var pool))
        {
            var removed = pool.TryRemoveNode(session.NodeId, session);
            if (pool.IsEmpty)
            {
                _clusters.TryRemove(key, out _);
            }
            return removed;
        }
        return false;
    }

    public IReadOnlyList<GatewaySessionInfo> ListActive(string? tenantId = null)
    {
        var query = _clusters.Values.SelectMany(p => p.ListNodes()).Where(n => n.IsActive);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(s => string.Equals(s.TenantId, tenantId, StringComparison.Ordinal));
        }

        return query.ToList();
    }

    public IReadOnlyList<GatewayClusterInfo> ListActiveClusters(string? tenantId = null)
    {
        var pools = _clusters.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            pools = pools.Where(p => string.Equals(p.TenantId, tenantId, StringComparison.Ordinal));
        }

        return pools.Select(p =>
        {
            var nodes = p.ListNodes();
            return new GatewayClusterInfo(
                p.TenantId,
                p.GatewayId,
                TotalNodes: nodes.Count,
                ActiveNodes: nodes.Count(n => n.IsActive),
                Nodes: nodes);
        }).ToList();
    }
}
