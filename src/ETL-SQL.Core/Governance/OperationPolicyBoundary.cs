using System.Globalization;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Shared operation-boundary policy logic: snapshot freshness refresh and governed numeric
/// ceilings. Security revocation and expired policy fail promptly; an ordinary version/hash
/// change recaptures the snapshot so limit changes apply at the next operation boundary.
/// </summary>
public static class OperationPolicyBoundary
{
    public static ExecutionPolicySnapshot Refresh(IExecutionContext context, string operationLabel)
    {
        var snapshot = context.ExecutionPolicy ?? ExecutionPolicySnapshot.Capture(
            EnterprisePolicyRuntime.Current, Environment.UserName,
            context.InteractiveMode ? ScriptExecutionMode.Interactive : ScriptExecutionMode.Batch,
            "unknown");
        var current = EnterprisePolicyRuntime.Current;
        var freshness = snapshot.GetFreshness(current);
        if (!freshness.CanContinue)
        {
            var denied = OperationPolicyDecision.Deny(snapshot, "EnterprisePolicy:Freshness",
                operationLabel, "available, unexpired enterprise policy",
                freshness.Reason ?? "Enterprise policy is not valid.");
            throw new FileSystemPolicyDeniedException(denied);
        }
        if (freshness.CurrentPolicyChanged)
        {
            snapshot = ExecutionPolicySnapshot.Capture(current, snapshot.Actor, snapshot.ExecutionMode,
                snapshot.ScriptHash, snapshot.JobId, snapshot.CorrelationId);
            context.ExecutionPolicy = snapshot;
        }
        return snapshot;
    }

    /// <summary>
    /// Denies when an enrolled policy sets <paramref name="policyKey"/> and
    /// <paramref name="observedValue"/> exceeds it. Local override flags never weaken an
    /// enterprise ceiling; users may still configure stricter local limits, which run first.
    /// </summary>
    public static void EnforceCeiling(
        IExecutionContext context,
        string policyKey,
        long observedValue,
        string targetLabel)
    {
        var snapshot = Refresh(context, targetLabel);
        if (!snapshot.IsEnrolled) return;
        if (!TryGetGovernedLong(snapshot, policyKey, out var ceiling)) return;
        if (observedValue <= ceiling) return;

        throw new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot, policyKey,
            targetLabel, $"<= {ceiling}",
            $"Enterprise policy limits {policyKey} to {ceiling}; the script reached {observedValue}."));
    }

    public static bool TryGetGovernedLong(ExecutionPolicySnapshot snapshot, string key, out long value)
    {
        value = 0;
        return snapshot.GovernedValues.TryGetValue(key, out var raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
