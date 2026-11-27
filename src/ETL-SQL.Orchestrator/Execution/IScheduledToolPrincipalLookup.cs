using ETL_SQL.Core.Governance;

namespace ETL_SQL.Orchestrator.Execution;

/// <summary>Returns current tool authority for a saved job owner, or null when it cannot be verified.</summary>
public interface IScheduledToolPrincipalLookup
{
    Task<ToolExecutionPrincipal?> ResolveAsync(string tenantId, string ownerKey, CancellationToken cancellationToken);
}
