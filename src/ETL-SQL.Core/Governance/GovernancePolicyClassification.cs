namespace ETL_SQL.Core.Governance;

/// <summary>
/// Describes how a centrally governed policy entry constrains behavior.
/// </summary>
public enum GovernancePolicyClassification
{
    /// <summary>The behavior or setting is explicitly denied.</summary>
    Forbidden,

    /// <summary>The behavior is explicitly permitted when it matches the policy value.</summary>
    Allowed,

    /// <summary>The behavior is permitted only inside a typed range, set, or pattern list.</summary>
    Constrained,

    /// <summary>The value is fixed by a higher authority and cannot be overridden by scripts or lower-level config.</summary>
    Locked
}

