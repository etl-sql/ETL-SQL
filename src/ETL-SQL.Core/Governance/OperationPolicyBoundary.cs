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

    /// <summary>
    /// Enforces <c>Security:MaxSpillBytesPerScript</c> against the cumulative engine-owned spill
    /// total. Reads the captured snapshot directly rather than refreshing, because this runs in the
    /// per-write spill hot path; security revocation/expiry is enforced at operation boundaries.
    /// </summary>
    public static void EnforceSpillCeiling(IExecutionContext context, long totalSpilledBytes)
    {
        var snapshot = context.ExecutionPolicy;
        if (snapshot is null || !snapshot.IsEnrolled) return;
        if (!TryGetGovernedLong(snapshot, "Security:MaxSpillBytesPerScript", out var ceiling)) return;
        if (totalSpilledBytes <= ceiling) return;

        throw new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot,
            "Security:MaxSpillBytesPerScript", "<engine-spill>", $"<= {ceiling} bytes",
            $"Enterprise policy Security:MaxSpillBytesPerScript limits script spill to {ceiling} bytes; the script has spilled {totalSpilledBytes}."));
    }

    /// <summary>
    /// Enforces <c>Security:AllowedExecutionModes</c> against the mode captured in the snapshot at
    /// execution start. No-op when standalone/unenrolled or when the policy lists no modes.
    /// </summary>
    public static void EnforceAllowedExecutionMode(ExecutionPolicySnapshot snapshot)
    {
        if (!snapshot.IsEnrolled) return;
        var allowed = snapshot.GovernedValues
            .Where(pair => pair.Key.StartsWith("Security:AllowedExecutionModes:",
                StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value!.Trim())
            .ToArray();
        if (allowed.Length == 0) return;

        var mode = snapshot.ExecutionMode.ToString();
        if (allowed.Any(value => string.Equals(value, mode, StringComparison.OrdinalIgnoreCase))) return;

        throw new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot,
            "Security:AllowedExecutionModes", mode, $"permitted modes: [{string.Join(", ", allowed)}]",
            $"Enterprise policy does not permit the '{mode}' execution mode."));
    }

    /// <summary>
    /// Enforces <c>Security:RemoteExecutionMode</c> at execution start: a run captured in
    /// <see cref="ScriptExecutionMode.Remote"/> is denied when the enrolled policy disables remote
    /// execution. The <c>TrustedOrchestrator</c>/<c>AllowedHosts</c> host-gating is applied by the
    /// remote-dispatch path that produces a Remote-mode snapshot. No-op for non-remote runs and when
    /// standalone/unenrolled.
    /// </summary>
    public static void EnforceRemoteExecutionMode(ExecutionPolicySnapshot snapshot)
    {
        if (!snapshot.IsEnrolled || snapshot.ExecutionMode != ScriptExecutionMode.Remote) return;
        if (!snapshot.GovernedValues.TryGetValue("Security:RemoteExecutionMode", out var mode)) return;
        if (!string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase)) return;

        throw new FileSystemPolicyDeniedException(OperationPolicyDecision.Deny(snapshot,
            "Security:RemoteExecutionMode", "Remote", "remote execution disabled",
            "Enterprise policy disables remote execution."));
    }

    public static bool TryGetGovernedLong(ExecutionPolicySnapshot snapshot, string key, out long value)
    {
        value = 0;
        return snapshot.GovernedValues.TryGetValue(key, out var raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
