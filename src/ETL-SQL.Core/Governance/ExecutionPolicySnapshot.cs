using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Immutable policy and correlation state captured when a top-level script execution begins.
/// The same instance must flow to child and parallel execution contexts.
/// </summary>
public sealed record ExecutionPolicySnapshot
{
    public required bool IsEnrolled { get; init; }
    public required bool IsPolicyAvailable { get; init; }
    public required string PolicyStatus { get; init; }
    public string? PolicyVersion { get; init; }
    public string? PolicyHash { get; init; }
    public DateTimeOffset? PolicyIssuedAtUtc { get; init; }
    public DateTimeOffset? PolicyExpiresAtUtc { get; init; }
    public required string Actor { get; init; }
    public required ScriptExecutionMode ExecutionMode { get; init; }
    public required string ScriptHash { get; init; }
    public string? JobId { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public required IReadOnlyDictionary<string, string?> GovernedValues { get; init; }

    public static ExecutionPolicySnapshot Capture(
        EffectiveEnterprisePolicy policy,
        string actor,
        ScriptExecutionMode executionMode,
        string scriptHash,
        string? jobId = null,
        string? correlationId = null,
        DateTimeOffset? capturedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptHash);

        return new ExecutionPolicySnapshot
        {
            IsEnrolled = policy.IsEnrolled,
            IsPolicyAvailable = policy.IsAvailable,
            PolicyStatus = policy.Status,
            PolicyVersion = policy.PolicyVersion,
            PolicyHash = ComputePolicyHash(policy.Document),
            PolicyIssuedAtUtc = policy.IssuedAtUtc,
            PolicyExpiresAtUtc = policy.ExpiresAtUtc,
            Actor = actor,
            ExecutionMode = executionMode,
            ScriptHash = scriptHash,
            JobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId,
            CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow,
            GovernedValues = new ReadOnlyDictionary<string, string?>(
                new Dictionary<string, string?>(policy.ConfigurationValues,
                    StringComparer.OrdinalIgnoreCase))
        };
    }

    /// <summary>
    /// Checks security-critical freshness at an operation boundary. Ordinary policy changes are
    /// represented by <see cref="CurrentPolicyChanged"/> so the boundary authorizer can recapture.
    /// </summary>
    public ExecutionPolicyFreshness GetFreshness(
        EffectiveEnterprisePolicy current,
        DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        if (IsEnrolled && (!current.IsEnrolled || !current.IsAvailable))
            return new(false, true, "Enterprise policy is no longer available.");
        if (PolicyExpiresAtUtc is { } expiry && expiry <= now)
            return new(false, false, $"Enterprise policy expired at {expiry:O}.");
        if (IsEnrolled && current.ExpiresAtUtc is { } currentExpiry && currentExpiry <= now)
            return new(false, true, $"Current enterprise policy expired at {currentExpiry:O}.");

        var changed = !string.Equals(PolicyVersion, current.PolicyVersion, StringComparison.Ordinal)
            || !string.Equals(PolicyHash, ComputePolicyHash(current.Document), StringComparison.Ordinal);
        return new(true, changed, null);
    }

    private static string? ComputePolicyHash(OrganizationPolicyDocument? document)
    {
        if (document is null) return null;
        var json = JsonSerializer.Serialize(document);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}

public sealed record ExecutionPolicyFreshness(
    bool CanContinue,
    bool CurrentPolicyChanged,
    string? Reason);

/// <summary>Structured result returned by an operation-boundary policy authorizer.</summary>
public sealed record OperationPolicyDecision(
    bool IsAllowed,
    string PolicyKey,
    string RequestedTarget,
    string EffectiveConstraint,
    string Reason,
    string CorrelationId,
    string? JobId,
    string? PolicyVersion,
    string? PolicyHash)
{
    public static OperationPolicyDecision Allow(
        ExecutionPolicySnapshot snapshot,
        string policyKey,
        string requestedTarget,
        string effectiveConstraint,
        string reason) =>
        Create(true, snapshot, policyKey, requestedTarget, effectiveConstraint, reason);

    public static OperationPolicyDecision Deny(
        ExecutionPolicySnapshot snapshot,
        string policyKey,
        string requestedTarget,
        string effectiveConstraint,
        string reason) =>
        Create(false, snapshot, policyKey, requestedTarget, effectiveConstraint, reason);

    private static OperationPolicyDecision Create(
        bool allowed,
        ExecutionPolicySnapshot snapshot,
        string policyKey,
        string requestedTarget,
        string effectiveConstraint,
        string reason) =>
        new(allowed, policyKey, requestedTarget, effectiveConstraint, reason,
            snapshot.CorrelationId, snapshot.JobId, snapshot.PolicyVersion, snapshot.PolicyHash);
}
