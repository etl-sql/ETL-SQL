namespace ETL_SQL.Core.Governance;

/// <summary>
/// Structured decision emitted when policy analysis accepts or rejects script behavior.
/// </summary>
public sealed record GovernancePolicyDecision(
    string PolicyKey,
    GovernancePolicyClassification Classification,
    GovernancePolicyScope Scope,
    string Action,
    bool IsViolation,
    string Reason)
{
    public static GovernancePolicyDecision Violation(
        GovernancePolicyDefinition policy,
        string action,
        string reason) =>
        new(policy.Key, policy.Classification, policy.Scope, action, true, reason);

    public static GovernancePolicyDecision Allowed(
        GovernancePolicyDefinition policy,
        string action,
        string reason) =>
        new(policy.Key, policy.Classification, policy.Scope, action, false, reason);
}
