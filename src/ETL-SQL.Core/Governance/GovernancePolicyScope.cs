namespace ETL_SQL.Core.Governance;

/// <summary>
/// Product area governed by a policy entry.
/// </summary>
public enum GovernancePolicyScope
{
    Engine,
    Security,
    Connector,
    Filesystem,
    Network,
    Execution,
    Storage,
    Secret,
    Audit,
    Portal,
    Orchestrator
}

