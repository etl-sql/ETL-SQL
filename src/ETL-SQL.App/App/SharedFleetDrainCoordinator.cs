using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.App;

public sealed record SharedFleetNode(
    string NodeId,
    SandboxIsolationTier MaximumIsolationTier,
    string? DedicatedTenantId = null,
    bool Draining = false);

public sealed record SharedFleetPlacement(
    string WorkId,
    string TenantId,
    string NodeId,
    SandboxIsolationTier RequiredIsolationTier);

/// <summary>
/// Fleet-wide admission state used by rolling replacement: draining removes a node from new
/// placement without cancelling its in-flight work, and placement may never lower isolation.
/// </summary>
public sealed class SharedFleetDrainCoordinator(IEnumerable<SharedFleetNode> nodes)
{
    private readonly Dictionary<string, SharedFleetNode> _nodes = nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
    private readonly Dictionary<string, SharedFleetPlacement> _inFlight = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private int _nextNode;

    public SharedFleetPlacement Place(
        string workId, string tenantId, SandboxIsolationTier requiredIsolationTier)
    {
        lock (_gate)
        {
            if (_inFlight.ContainsKey(workId))
                throw new InvalidOperationException("A fleet work ID is already active.");
            var candidates = _nodes.Values
                .Where(node => !node.Draining && node.MaximumIsolationTier >= requiredIsolationTier)
                .Where(node => requiredIsolationTier == SandboxIsolationTier.Dedicated
                    ? string.Equals(node.DedicatedTenantId, tenantId, StringComparison.Ordinal)
                    : node.DedicatedTenantId is null)
                .OrderBy(node => node.NodeId, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
                throw new InvalidOperationException("No non-draining execution node satisfies the required isolation tier.");
            var node = candidates[(int)((uint)_nextNode++ % (uint)candidates.Length)];
            var placement = new SharedFleetPlacement(workId, tenantId, node.NodeId, requiredIsolationTier);
            _inFlight.Add(workId, placement);
            return placement;
        }
    }

    public IReadOnlyList<SharedFleetPlacement> BeginDrain(string nodeId)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(nodeId, out var node))
                throw new KeyNotFoundException("The execution node is not registered.");
            _nodes[nodeId] = node with { Draining = true };
            return _inFlight.Values.Where(work => work.NodeId == nodeId).ToList();
        }
    }

    public void Complete(string workId, string tenantId)
    {
        lock (_gate)
        {
            if (!_inFlight.TryGetValue(workId, out var work)
                || !string.Equals(work.TenantId, tenantId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The tenant does not own this in-flight work item.");
            _inFlight.Remove(workId);
        }
    }

    public bool CanReplace(string nodeId)
    {
        lock (_gate)
            return _nodes.TryGetValue(nodeId, out var node)
                && node.Draining
                && _inFlight.Values.All(work => work.NodeId != nodeId);
    }

    public IReadOnlyList<SharedFleetPlacement> InFlight
    {
        get { lock (_gate) return _inFlight.Values.ToList(); }
    }
}
