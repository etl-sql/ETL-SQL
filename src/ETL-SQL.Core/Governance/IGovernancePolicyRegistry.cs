namespace ETL_SQL.Core.Governance;

/// <summary>
/// Central catalog of policy keys known to ETL-SQL hosts.
/// </summary>
public interface IGovernancePolicyRegistry
{
    IReadOnlyCollection<GovernancePolicyDefinition> Definitions { get; }

    bool TryGet(string key, out GovernancePolicyDefinition definition);

    GovernancePolicyDefinition GetRequired(string key);

    void Register(GovernancePolicyDefinition definition);

    void RegisterRange(IEnumerable<GovernancePolicyDefinition> definitions);
}

