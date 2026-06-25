using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Data;
/// <summary>
/// A node's liveness record in the shared cluster registry. <see cref="ExpiresAtUtc"/> is
/// <see cref="LastHeartbeatUtc"/> plus the heartbeat TTL; a node is "live" only while now &lt;
/// <see cref="ExpiresAtUtc"/>, so a crashed node ages out without any explicit deregistration.
/// </summary>
public record NodeHeartbeat(
    string NodeId,
    string Role,
    DateTime FirstSeenUtc,
    DateTime LastHeartbeatUtc,
    DateTime ExpiresAtUtc,
    string? Metadata
);

/// <summary>
/// Database-backed node heartbeats (Practical HA P1.7). Generalizes the per-job execution lease into
/// a cluster-wide view of which Portal/Orchestrator nodes are currently alive, over the same shared
/// state. It is the substrate for fencing (P1.8), leader election (P1.9), and node-capacity-aware
/// claims (P2.1): a TTL heartbeat that any node can write and every node can read.
/// </summary>
public interface INodeRegistryStore
{
    /// <summary>
    /// Registers the node if new, or renews its lease if known: sets the last-heartbeat to now and
    /// the expiry to now + <paramref name="ttl"/>. The original first-seen time is preserved across
    /// renewals. <paramref name="metadata"/> is opaque (e.g. host/version/capacity JSON).
    /// </summary>
    Task RegisterOrRenewNodeAsync(string nodeId, string role, TimeSpan ttl, string? metadata = null);

    /// <summary>Nodes whose lease has not expired (now &lt; ExpiresAt).</summary>
    Task<IReadOnlyList<NodeHeartbeat>> GetLiveNodesAsync();

    /// <summary>All registered nodes, including expired ones not yet pruned (for diagnostics).</summary>
    Task<IReadOnlyList<NodeHeartbeat>> GetAllNodesAsync();

    /// <summary>Removes a node immediately (graceful shutdown). Safe if the node is unknown.</summary>
    Task DeregisterNodeAsync(string nodeId);

    /// <summary>Deletes all expired node rows; returns the number removed.</summary>
    Task<int> PruneExpiredNodesAsync();
}
