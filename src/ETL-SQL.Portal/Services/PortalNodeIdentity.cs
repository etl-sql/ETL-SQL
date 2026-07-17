namespace ETL_SQL.Portal.Services;

/// <summary>Stable identity for this Portal process, used for load-balancer affinity.</summary>
public sealed class PortalNodeIdentity
{
    public string NodeId { get; } =
        $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
}
