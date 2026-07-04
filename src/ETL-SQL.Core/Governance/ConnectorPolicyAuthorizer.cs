using System.Text.RegularExpressions;
using ETL_SQL.Services;

namespace ETL_SQL.Core.Governance;

public sealed class ConnectorPolicyDeniedException(
    OperationPolicyDecision decision,
    Exception? innerException = null)
    : SecurityException(decision.Reason)
{
    public OperationPolicyDecision Decision { get; } = decision;
    public Exception? AuthorizationFailure { get; } = innerException;
}

/// <summary>
/// Canonical operation boundary for script-selected connector destinations. Enforces
/// connector-type and destination-host organization policy before a connection is created,
/// in addition to the local <see cref="SecurityService"/> egress guardrails.
/// </summary>
public sealed partial class ConnectorPolicyAuthorizer(SecurityService securityService)
{
    /// <summary>
    /// Authorizes a connector destination. Call before creating the data source (i.e. before DNS
    /// resolution / connection creation). Throws <see cref="ConnectorPolicyDeniedException"/> on denial.
    /// </summary>
    public OperationPolicyDecision Authorize(
        IExecutionContext context,
        string connectorType,
        string? host,
        string? target)
    {
        var snapshot = OperationPolicyBoundary.Refresh(context, "<connector-operation>");
        try
        {
            RejectEmbeddedCredentials(target);
            EnforceAllowedTypes(snapshot, connectorType);
            if (!string.IsNullOrWhiteSpace(host))
            {
                securityService.ValidateHost(host);
                EnforceEnterpriseHosts(snapshot, host);
            }
        }
        catch (SecurityException ex) when (ex is not ConnectorPolicyDeniedException)
        {
            var denied = OperationPolicyDecision.Deny(snapshot, "Connectors:Destination",
                Sanitize(connectorType, host), EffectiveConstraint(snapshot), ex.Message);
            throw new ConnectorPolicyDeniedException(denied, ex);
        }

        return OperationPolicyDecision.Allow(snapshot, "Connectors:Destination",
            Sanitize(connectorType, host), EffectiveConstraint(snapshot), "Connector destination allowed.");
    }

    private static void EnforceAllowedTypes(ExecutionPolicySnapshot snapshot, string connectorType)
    {
        if (!snapshot.IsEnrolled) return;
        var allowed = GovernedList(snapshot, "Connectors:AllowedTypes:");
        if (allowed.Length == 0) return;
        if (allowed.Any(value => string.Equals(value, connectorType, StringComparison.OrdinalIgnoreCase)))
            return;

        throw new SecurityException(
            $"Enterprise policy permits connector types [{string.Join(", ", allowed)}]; '{connectorType}' is not allowed.");
    }

    private static void EnforceEnterpriseHosts(ExecutionPolicySnapshot snapshot, string host)
    {
        if (!snapshot.IsEnrolled) return;
        var allowed = GovernedList(snapshot, "Security:AllowedHosts:");
        if (allowed.Length == 0) return;
        if (allowed.Any(pattern => HostMatches(pattern, host))) return;

        throw new SecurityException(
            $"Enterprise policy denied connection to host '{host}' (not in the authorized host list).");
    }

    private static bool HostMatches(string pattern, string host)
    {
        if (pattern == "*") return true;
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
            return host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectEmbeddedCredentials(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        // scheme://user:pass@host — a password in a URL authority is an exfiltration/rebinding
        // vector and is rejected regardless of policy. The colon requirement avoids false
        // positives on non-credential authorities such as bundle URIs (orch://name@version).
        // Plain ADO-style connection strings (no scheme://) never match this anchor.
        if (EmbeddedCredentialRegex().IsMatch(target))
            throw new SecurityException(
                "Credentials embedded in a connection URL authority are not permitted; use PASSWORD/secret references.");
    }

    private static string[] GovernedList(ExecutionPolicySnapshot snapshot, string keyPrefix) =>
        snapshot.GovernedValues
            .Where(pair => pair.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value!.Trim())
            .ToArray();

    private static string EffectiveConstraint(ExecutionPolicySnapshot snapshot) =>
        snapshot.IsEnrolled ? "enterprise connector + host allowlists" : "local egress guardrails";

    private static string Sanitize(string connectorType, string? host) =>
        string.IsNullOrWhiteSpace(host) ? connectorType : $"{connectorType}://{host}";

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/@\s]*:[^/@\s]*@", RegexOptions.None)]
    private static partial Regex EmbeddedCredentialRegex();
}
