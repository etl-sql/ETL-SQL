using System.Text.RegularExpressions;
using ETL_SQL.Services;

namespace ETL_SQL.Core.Governance;

public sealed class ConnectorPolicyDeniedException(
    OperationPolicyDecision decision,
    Exception? innerException = null)
    : SecurityException(decision.Reason), ISecurityEventEmittedException
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
        => Authorize(OperationPolicyBoundary.Refresh(context, "<connector-operation>"), connectorType, host, target);

    /// <summary>
    /// Context-free destination authorization for callers that have no <see cref="IExecutionContext"/>
    /// (e.g. the Portal connection-test surface). The caller supplies a policy snapshot — typically
    /// <c>ExecutionPolicySnapshot.Capture(EnterprisePolicyRuntime.Current, …)</c> — and host validation
    /// runs against this authorizer's <see cref="SecurityService"/>. Enforces the same connector-type,
    /// host allowlist, internal-range, scheme, and port rules as the context overload.
    /// </summary>
    public OperationPolicyDecision Authorize(
        ExecutionPolicySnapshot snapshot,
        string connectorType,
        string? host,
        string? target)
    {
        // The infrastructure fence runs first, unconditionally, and outside the wrapping try. It is
        // not an organization-policy decision — enrollment state and a wildcard allowlist cannot
        // relax it — so a fenced destination is reported as the plain non-policy denial it is, with
        // one security event, rather than as a ConnectorPolicyDeniedException.
        InfrastructureEgressFence.EnforceHost(host);
        if (Uri.TryCreate(target, UriKind.Absolute, out var fenceUri))
            InfrastructureEgressFence.EnforceHost(fenceUri.Host);

        try
        {
            RejectEmbeddedCredentials(target);
            EnforceAllowedTypes(snapshot, connectorType);
            if (!string.IsNullOrWhiteSpace(host))
            {
                securityService.ValidateHost(host);
                EnforceEnterpriseHosts(snapshot, host);
            }
            // Scheme/port rules apply only to URL-shaped targets (REST, and other connectors whose
            // connection string is an absolute URI); ADO connection strings name no scheme and are
            // governed by the host allowlist alone.
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
                EnforceSchemeAndPort(snapshot, uri);
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

    /// <summary>
    /// Applies the enterprise destination-host allowlist and internal-range denial to a single host,
    /// for connectors that resolve request URLs dynamically after connection creation (e.g. REST
    /// redirect/pagination/template targets). No-op when standalone or unenrolled.
    /// </summary>
    public static void EnforceEnterpriseHost(IExecutionContext context, string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        var snapshot = OperationPolicyBoundary.Refresh(context, "<connector-probe>");
        // The local egress guardrail and the infrastructure fence throw plain SecurityExceptions;
        // keep them outside the wrapping try so a local denial is reported as-is and not
        // misclassified as an enterprise ConnectorPolicyDeniedException. Only organization-policy
        // denials are wrapped.
        InfrastructureEgressFence.EnforceHost(host);
        context.SecurityService.ValidateHost(host);
        try
        {
            EnforceEnterpriseHosts(snapshot, host);
        }
        catch (SecurityException ex) when (ex is not ConnectorPolicyDeniedException)
        {
            var denied = OperationPolicyDecision.Deny(snapshot, "Connectors:Destination",
                host, EffectiveConstraint(snapshot), ex.Message);
            throw new ConnectorPolicyDeniedException(denied, ex);
        }
    }

    /// <summary>
    /// Applies the enterprise host, scheme, and port allowlists to a fully-formed request URL. Use
    /// on the dynamic REST path (initial request, redirects, pagination, template targets) where the
    /// scheme and port are known and a redirect could otherwise reach a denied port/scheme on an
    /// allowed host. No-op when standalone or unenrolled.
    /// </summary>
    public static void EnforceEnterpriseUrl(IExecutionContext context, Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var snapshot = OperationPolicyBoundary.Refresh(context, "<connector-probe>");
        // Fence and local egress guardrail throw plain SecurityExceptions, unwrapped (see
        // EnforceEnterpriseHost). The fence also covers the redirect/pagination path, so a 302 to the
        // metadata service is denied on an unenrolled host with no allowlist.
        InfrastructureEgressFence.EnforceHost(url.Host);
        context.SecurityService.ValidateHost(url.Host);
        try
        {
            EnforceEnterpriseHosts(snapshot, url.Host);
            EnforceSchemeAndPort(snapshot, url);
        }
        catch (SecurityException ex) when (ex is not ConnectorPolicyDeniedException)
        {
            var denied = OperationPolicyDecision.Deny(snapshot, "Connectors:Destination",
                $"{url.Scheme}://{url.Host}:{url.Port}", EffectiveConstraint(snapshot), ex.Message);
            throw new ConnectorPolicyDeniedException(denied, ex);
        }
    }

    /// <summary>
    /// Connect-time DNS-rebinding defense. Validates an address the target host actually resolved to,
    /// immediately before the socket connects, against the process-wide enterprise policy. Under a
    /// host allowlist a resolved loopback/link-local/private/CGNAT/ULA/metadata address is denied
    /// unless that exact address is explicitly listed — so a name that passed the earlier name-based
    /// allowlist cannot rebind to an internal IP between check and connect. No-op when standalone /
    /// unenrolled or when no host allowlist is configured (parity with the name-based check). Reads
    /// <see cref="EnterprisePolicyRuntime.Current"/> because the connect callback runs below the
    /// request context; a policy change between request and connect fails closed to the current policy.
    /// </summary>
    public static void EnforceResolvedAddress(string host, System.Net.IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        // Infrastructure classes are fenced with no enrollment or allowlist precondition — an
        // unenrolled worker is still running on someone else's node.
        InfrastructureEgressFence.EnforceResolvedAddress(host, address);

        var policy = EnterprisePolicyRuntime.Current;
        if (!policy.IsEnrolled) return;

        var allowed = policy.ConfigurationValues
            .Where(pair => pair.Key.StartsWith("Security:AllowedHosts:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value!.Trim())
            .ToArray();
        if (allowed.Length == 0) return;

        var literal = address.ToString();
        var explicitMatch = allowed.Any(pattern =>
            !pattern.Contains('*') && HostMatches(pattern, literal));
        if (NetworkDestinationRules.IsRestrictedRange(literal) && !explicitMatch)
        {
            SecurityEventRuntime.EmitNetworkDenial(policy, host,
                "DNS resolution reached a policy-restricted network address.");
            throw new SecurityException(
                $"Enterprise policy denied connection to '{host}': it resolved to internal address '{literal}' " +
                "(DNS rebinding to a loopback/link-local/private range is blocked; list the address explicitly to allow it).");
        }
    }

    private static void EnforceSchemeAndPort(ExecutionPolicySnapshot snapshot, Uri url)
    {
        if (!snapshot.IsEnrolled) return;

        var allowedSchemes = GovernedList(snapshot, "Security:AllowedSchemes:");
        if (allowedSchemes.Length > 0
            && !allowedSchemes.Any(scheme => string.Equals(scheme, url.Scheme, StringComparison.OrdinalIgnoreCase)))
            throw new SecurityException(
                $"Enterprise policy permits schemes [{string.Join(", ", allowedSchemes)}]; '{url.Scheme}' is not allowed.");

        var allowedPorts = GovernedList(snapshot, "Security:AllowedPorts:");
        if (allowedPorts.Length == 0) return;

        // Uri.Port is the explicit port or the scheme's default (-1 only for schemes without one).
        var port = url.Port;
        if (port < 0) return;
        if (!allowedPorts.Any(value => int.TryParse(value, out var allowed) && allowed == port))
            throw new SecurityException(
                $"Enterprise policy permits ports [{string.Join(", ", allowedPorts)}]; port {port} is not allowed.");
    }

    private static void EnforceEnterpriseHosts(ExecutionPolicySnapshot snapshot, string host)
    {
        if (!snapshot.IsEnrolled) return;
        var allowed = GovernedList(snapshot, "Security:AllowedHosts:");
        if (allowed.Length == 0) return;

        // Normalize obfuscated IP literals so allowlist matching and range checks see one form.
        var normalized = NetworkDestinationRules.Normalize(host);

        // A loopback/link-local/private/metadata address is reachable only when an operator lists
        // it explicitly — a wildcard entry (e.g. "*") must never grant access to internal ranges.
        var explicitMatch = allowed.Any(pattern =>
            !pattern.Contains('*') && HostMatches(pattern, normalized));
        if (NetworkDestinationRules.IsRestrictedRange(normalized) && !explicitMatch)
            throw new SecurityException(
                $"Enterprise policy denied connection to internal address '{normalized}' (loopback/link-local/private range not explicitly permitted).");

        if (allowed.Any(pattern => HostMatches(pattern, normalized))) return;

        throw new SecurityException(
            $"Enterprise policy denied connection to host '{normalized}' (not in the authorized host list).");
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
