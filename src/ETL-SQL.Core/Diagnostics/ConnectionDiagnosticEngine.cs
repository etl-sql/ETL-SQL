using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Services;

namespace ETL_SQL.Core.Diagnostics;

/// <summary>Outcome of a single diagnostic layer.</summary>
public enum DiagnosticStatus
{
    /// <summary>The layer completed successfully.</summary>
    Ok,
    /// <summary>The layer was attempted and failed — see <see cref="DiagnosticStep.Remedy"/>.</summary>
    Failed,
    /// <summary>The layer was not applicable / not attempted (e.g. file connector, auth deferred).</summary>
    Skipped,
    /// <summary>Governance denied the operation before any probe ran.</summary>
    Denied
}

/// <summary>A single layer of the layered connection diagnostic (DNS, TCP, TLS, AUTH …).</summary>
/// <remarks>Details and remedies are constructed to never contain secret values.</remarks>
public sealed record DiagnosticStep(string Layer, DiagnosticStatus Status, string Detail, string? Remedy = null);

/// <summary>The full plain-English diagnostic report for a connection.</summary>
public sealed record DiagnosticReport(string Connection, string ConnectorType, IReadOnlyList<DiagnosticStep> Steps)
{
    /// <summary>True when every attempted layer succeeded (no failures or denials).</summary>
    public bool Succeeded => Steps.All(s => s.Status is DiagnosticStatus.Ok or DiagnosticStatus.Skipped);
}

/// <summary>Connector-specific context for credential/host-key diagnostics.</summary>
public sealed record ConnectionDiagnosticAuthContext(
    string Alias,
    string ConnectorType,
    string Target,
    IReadOnlyDictionary<string, string>? Options,
    int ProbeTimeoutSeconds);

/// <summary>
/// Optional connector capability used by <see cref="ConnectionDiagnosticEngine"/> after DNS/TCP/TLS
/// succeeds. Implementations may open a real provider session, validate an SSH host key, or otherwise
/// verify authentication without returning secret values in report details.
/// </summary>
public interface IConnectionDiagnosticAuthProbe
{
    Task<IReadOnlyList<DiagnosticStep>> DiagnoseAuthenticationAsync(
        ConnectionDiagnosticAuthContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Governed, layered connection diagnostic used by the <c>TEST CONNECTION</c> statement (and, later,
/// the Portal "Test connection" button — this is the one shared core). Actively probes a catalog
/// connection through DNS → TCP → TLS and returns a plain-English troubleshooting report.
///
/// <para><b>Security.</b> Active probing is an SSRF / port-scan primitive if ungoverned. Every probe
/// therefore routes through the same egress boundary a real connection does:
/// <see cref="ConnectorPolicyAuthorizer.Authorize"/> (connector-type + host allowlist, internal-range
/// denial) runs <i>before any network I/O</i>, and each resolved address is re-validated at connect
/// time via <see cref="ConnectorPolicyAuthorizer.EnforceResolvedAddress"/> to defeat DNS rebinding —
/// the same pattern <see cref="PolicyBoundHttp"/> applies to HTTP. Reports never echo secret values.</para>
/// </summary>
public sealed class ConnectionDiagnosticEngine(IConnectorRegistry connectorRegistry)
{
    private readonly IConnectorRegistry _connectorRegistry = connectorRegistry;

    // Well-known default ports used when a connector supplies neither a probe endpoint nor a PORT
    // option. Intentionally conservative — an unknown port yields a Skipped TCP step, not a guess.
    private static readonly IReadOnlyDictionary<string, int> DefaultPorts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSSQL"] = 1433,
            ["SQLSERVER"] = 1433,
            ["POSTGRES"] = 5432,
            ["POSTGRESQL"] = 5432,
            ["MYSQL"] = 3306,
            ["MARIADB"] = 3306,
            ["ORACLE"] = 1521,
            ["MONGODB"] = 27017,
            ["REDIS"] = 6379,
            ["NEO4J"] = 7687,
            ["SFTP"] = 22,
            ["FTP"] = 21,
            ["FTP_CONN"] = 21,
        };

    /// <summary>
    /// Runs the layered diagnostic for the connection registered under <paramref name="alias"/>.
    /// Throws <see cref="ExecutionException"/> if the alias is not a registered connection.
    /// </summary>
    public async Task<DiagnosticReport> DiagnoseAsync(
        IExecutionContext context,
        string alias,
        int probeTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Connections.TryGetValue(alias, out var dataSource) || dataSource is null)
            throw new ExecutionException($"Connection '{alias}' not found.");

        ExecutionPolicySnapshot snapshot;
        try
        {
            snapshot = OperationPolicyBoundary.Refresh(context, "<connection-diagnostic>");
        }
        catch (SecurityException ex)
        {
            // Stale/expired enterprise policy — report as a governance denial rather than throwing.
            return new DiagnosticReport(alias, dataSource.ConnectorType, new[]
            {
                new DiagnosticStep("POLICY", DiagnosticStatus.Denied, Sanitize(ex.Message),
                    "This connection's destination is not permitted by the active security policy. Ask an administrator to authorize the host/connector, or correct the connection target.")
            });
        }

        return await DiagnoseAsync(alias, dataSource.ConnectorType, dataSource.Path, dataSource.Options,
            context.SecurityService, snapshot, probeTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the layered diagnostic for an explicit target and options specification without requiring
    /// the connection to be registered in the execution context's active connection catalog.
    /// </summary>
    public async Task<DiagnosticReport> DiagnoseTargetAsync(
        IExecutionContext context,
        string alias,
        string connectorType,
        string? target,
        IReadOnlyDictionary<string, string>? options,
        int probeTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutionPolicySnapshot snapshot;
        try
        {
            snapshot = OperationPolicyBoundary.Refresh(context, "<connection-diagnostic>");
        }
        catch (SecurityException ex)
        {
            return new DiagnosticReport(alias, connectorType, new[]
            {
                new DiagnosticStep("POLICY", DiagnosticStatus.Denied, Sanitize(ex.Message),
                    "Security policy validation failed before running diagnostic probes.")
            });
        }

        return await DiagnoseAsync(
            alias,
            connectorType,
            target,
            options,
            context.SecurityService,
            snapshot,
            probeTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Context-free diagnostic core shared by the <c>TEST CONNECTION</c> statement and the Portal
    /// "Test connection" surface. The caller supplies the connection definition (connector type,
    /// target, options), a <see cref="SecurityService"/> for host validation, and a policy snapshot
    /// (e.g. <c>ExecutionPolicySnapshot.Capture(EnterprisePolicyRuntime.Current, …)</c>). Governance
    /// runs before any network I/O; the report never contains secret values.
    /// </summary>
    public async Task<DiagnosticReport> DiagnoseAsync(
        string alias,
        string connectorType,
        string? target,
        IReadOnlyDictionary<string, string>? options,
        SecurityService security,
        ExecutionPolicySnapshot snapshot,
        int probeTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(snapshot);

        var opts = options is null
            ? null
            : new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
        var effectiveTarget = target ?? string.Empty;
        var connector = _connectorRegistry.GetConnector(connectorType);
        var host = connector?.GetHost(effectiveTarget, opts);

        var steps = new List<DiagnosticStep>();

        // 1. Governance gate — before any DNS resolution or socket connect. Authorizes the connector
        //    type + destination host against the supplied policy snapshot.
        try
        {
            new ConnectorPolicyAuthorizer(security)
                .Authorize(snapshot, connector?.Name ?? connectorType, host, target);
        }
        catch (SecurityException ex)
        {
            steps.Add(new DiagnosticStep("POLICY", DiagnosticStatus.Denied, Sanitize(ex.Message),
                "This connection's destination is not permitted by the active security policy. Ask an administrator to authorize the host/connector, or correct the connection target."));
            return new DiagnosticReport(alias, connectorType, steps);
        }

        steps.Add(new DiagnosticStep("POLICY", DiagnosticStatus.Ok, "Destination permitted by active security policy."));

        // 2. Non-network connectors have nothing to probe.
        var endpoint = ResolveEndpoint(connector, effectiveTarget, opts, host);
        if (connector is { IsFileBased: true } || string.IsNullOrWhiteSpace(host))
        {
            steps.Add(new DiagnosticStep("NETWORK", DiagnosticStatus.Skipped,
                $"'{connectorType}' is a local/file connector — no network diagnostics apply."));
            return new DiagnosticReport(alias, connectorType, steps);
        }

        var timeout = TimeSpan.FromSeconds(probeTimeoutSeconds > 0 ? probeTimeoutSeconds : 5);

        // 3. DNS resolution (+ DNS-rebind re-validation of every resolved address).
        IPAddress[] addresses;
        try
        {
            using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            dnsCts.CancelAfter(timeout);
            addresses = await Dns.GetHostAddressesAsync(host!, dnsCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            steps.Add(new DiagnosticStep("DNS", DiagnosticStatus.Failed, $"Could not resolve host '{host}'.",
                "Verify the hostname is spelled correctly and is resolvable from this machine (DNS / hosts file / VPN)."));
            AddSkippedTail(steps, dns: false);
            return new DiagnosticReport(alias, connectorType, steps);
        }

        if (addresses.Length == 0)
        {
            steps.Add(new DiagnosticStep("DNS", DiagnosticStatus.Failed, $"Host '{host}' resolved to no addresses.",
                "Verify the hostname resolves from this machine (DNS / hosts file / VPN)."));
            AddSkippedTail(steps, dns: false);
            return new DiagnosticReport(alias, connectorType, steps);
        }

        try
        {
            foreach (var address in addresses)
                ConnectorPolicyAuthorizer.EnforceResolvedAddress(host!, address);
        }
        catch (SecurityException ex)
        {
            steps.Add(new DiagnosticStep("DNS", DiagnosticStatus.Denied, Sanitize(ex.Message),
                "The host resolved to an address the active security policy blocks (e.g. an internal range). List the address explicitly if this is intended."));
            AddSkippedTail(steps, dns: false);
            return new DiagnosticReport(alias, connectorType, steps);
        }

        steps.Add(new DiagnosticStep("DNS", DiagnosticStatus.Ok,
            $"'{host}' resolved to {string.Join(", ", addresses.Select(a => a.ToString()))}."));

        // 4. TCP handshake (requires a known port).
        if (endpoint is not { } ep)
        {
            steps.Add(new DiagnosticStep("TCP", DiagnosticStatus.Skipped,
                "Port could not be determined for this connector.",
                "Add a PORT option to the connection so reachability can be tested."));
            AddSkippedTail(steps, dns: true, tcp: false);
            return new DiagnosticReport(alias, connectorType, steps);
        }

        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            using var tcpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            tcpCts.CancelAfter(timeout);
            await socket.ConnectAsync(addresses, ep.Port, tcpCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            steps.Add(new DiagnosticStep("TCP", DiagnosticStatus.Failed,
                $"Could not open a TCP connection to {host}:{ep.Port}.",
                "Check that the service is running and listening on that port and that no firewall blocks it."));
            AddSkippedTail(steps, dns: true, tcp: false);
            return new DiagnosticReport(alias, connectorType, steps);
        }

        steps.Add(new DiagnosticStep("TCP", DiagnosticStatus.Ok, $"Port {ep.Port} on {host} is reachable."));

        // 5. TLS handshake (only when the connector expects transport encryption).
        if (ep.ExpectTls)
        {
            try
            {
                await using var network = new NetworkStream(socket, ownsSocket: false);
                await using var tls = new SslStream(network, leaveInnerStreamOpen: true);
                using var tlsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                tlsCts.CancelAfter(timeout);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, tlsCts.Token)
                    .ConfigureAwait(false);
                steps.Add(new DiagnosticStep("TLS", DiagnosticStatus.Ok, DescribeTls(tls)));
            }
            catch (Exception ex) when (ex is System.Security.Authentication.AuthenticationException
                                          or SocketException or OperationCanceledException or System.IO.IOException)
            {
                steps.Add(new DiagnosticStep("TLS", DiagnosticStatus.Failed,
                    $"TLS handshake with {host}:{ep.Port} failed.",
                    "Check the server certificate, its expiry and trust chain, and that the client and server share a supported TLS version."));
                AddSkippedTail(steps, dns: true, tcp: true, tls: false);
                return new DiagnosticReport(alias, connectorType, steps);
            }
        }
        else
        {
            steps.Add(new DiagnosticStep("TLS", DiagnosticStatus.Skipped, "Connector does not expect transport TLS on this port."));
        }

        // 6. Connector-specific authentication / host-key probe.
        if (connector is IConnectionDiagnosticAuthProbe authProbe)
        {
            IReadOnlyList<DiagnosticStep> authSteps;
            try
            {
                authSteps = await authProbe.DiagnoseAuthenticationAsync(
                    new ConnectionDiagnosticAuthContext(
                        alias,
                        connector.Name,
                        effectiveTarget,
                        opts,
                        probeTimeoutSeconds),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                authSteps =
                [
                    new DiagnosticStep("AUTH", DiagnosticStatus.Failed,
                        $"Credential authentication failed for '{connector.Name}'.",
                        "Verify the username, password/key material, account status, and authentication mode.")
                ];
            }

            steps.AddRange(authSteps.Select(s => s with { Detail = Sanitize(s.Detail), Remedy = s.Remedy is null ? null : Sanitize(s.Remedy) }));
        }
        else
        {
            steps.Add(new DiagnosticStep("AUTH", DiagnosticStatus.Skipped,
                "Credential authentication is not supported by this connector diagnostic.",
                "Run a query or connector-specific operation to confirm the credentials are accepted."));
        }

        return new DiagnosticReport(alias, connectorType, steps);
    }

    /// <summary>Resolves the probe endpoint from the connector override, then PORT option, then defaults.</summary>
    private static (string Host, int Port, bool ExpectTls)? ResolveEndpoint(
        IConnector? connector, string target, Dictionary<string, string>? options, string? host)
    {
        var supplied = connector?.GetProbeEndpoint(target, options);
        if (supplied is { } s && s.Port > 0 && !string.IsNullOrWhiteSpace(s.Host))
            return s;

        if (string.IsNullOrWhiteSpace(host))
            return null;

        int? port = null;
        if (options != null && options.TryGetValue("PORT", out var portText)
            && int.TryParse(portText, out var parsed) && parsed is > 0 and <= 65535)
            port = parsed;

        if (port is null && connector != null && DefaultPorts.TryGetValue(connector.Name, out var def))
            port = def;

        if (port is null)
            return null;

        return (host!, port.Value, ExpectsTls(options));
    }

    /// <summary>Heuristic: does the connection request transport encryption via a common option flag?</summary>
    private static bool ExpectsTls(Dictionary<string, string>? options)
    {
        if (options is null) return false;
        foreach (var key in new[] { "ENCRYPT", "SSL", "TLS", "USESSL", "USETLS" })
            if (options.TryGetValue(key, out var v) && IsTruthy(v))
                return true;
        if (options.TryGetValue("SSLMODE", out var mode)
            && !mode.Equals("disable", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(mode))
            return true;
        return false;
    }

    private static bool IsTruthy(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("1", StringComparison.Ordinal)
        || value.Equals("required", StringComparison.OrdinalIgnoreCase)
        || value.Equals("strict", StringComparison.OrdinalIgnoreCase);

    private static string DescribeTls(SslStream tls)
    {
        var subject = tls.RemoteCertificate is System.Security.Cryptography.X509Certificates.X509Certificate2 cert
            ? $"; certificate '{cert.Subject}' valid until {cert.NotAfter:yyyy-MM-dd}"
            : string.Empty;
        return $"TLS handshake succeeded ({tls.SslProtocol}){subject}.";
    }

    private static void AddSkippedTail(List<DiagnosticStep> steps, bool dns, bool tcp = false, bool tls = false)
    {
        if (!dns) steps.Add(new DiagnosticStep("TCP", DiagnosticStatus.Skipped, "Not attempted — DNS resolution did not succeed."));
        if (!dns || !tcp) steps.Add(new DiagnosticStep("TLS", DiagnosticStatus.Skipped, "Not attempted — an earlier layer did not succeed."));
        steps.Add(new DiagnosticStep("AUTH", DiagnosticStatus.Skipped, "Not attempted — an earlier layer did not succeed."));
    }

    /// <summary>Defence in depth: strip anything secret-shaped from a message before it reaches a report.</summary>
    private static string Sanitize(string message) => SecretRedactor.Redact(message) ?? message;
}
