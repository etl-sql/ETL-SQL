using System.Net;
using System.Net.Sockets;
using ETL_SQL.Services;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// The class of hosting-infrastructure destination a host or address names. Used to report which
/// fence rule denied an egress attempt without echoing the address back to the caller.
/// </summary>
public enum InfrastructureDestinationClass
{
    /// <summary>Not an infrastructure destination — ordinary policy rules decide.</summary>
    None = 0,

    /// <summary>Cloud instance metadata / instance identity service (IMDS and equivalents).</summary>
    CloudMetadata,

    /// <summary>Link-local range used by node-local agents, kubelet, and instance services.</summary>
    LinkLocalNodeService,

    /// <summary>Container runtime host bridge (Docker/Podman host gateway names).</summary>
    ContainerRuntime,

    /// <summary>Cluster-internal service discovery namespace (Kubernetes service/pod DNS).</summary>
    ClusterServiceDiscovery
}

/// <summary>
/// Non-bypassable outbound fence for hosting-infrastructure destinations.
///
/// <para>Unlike the organization host allowlist in <see cref="ConnectorPolicyAuthorizer"/>, this fence
/// applies to <b>every</b> deployment topology — standalone, unenrolled, Managed Dedicated, and Shared
/// SaaS — and it is not relaxed by a wildcard allowlist or by the absence of one. A tenant workload
/// running on infrastructure the tenant does not own must not be able to read the cloud metadata
/// service, talk to the node's container runtime, or enumerate cluster service discovery, whatever
/// the connector policy happens to say. That is the property the SaaS isolation architecture calls
/// default-deny egress
/// (<c>docs/architecture/SaaSTenantIsolation.md</c> §12), and a dedicated tenant's own worker is held
/// to it too.</para>
///
/// <para>The fence deliberately covers only the <i>infrastructure</i> classes. Loopback and RFC 1918
/// private ranges are <b>not</b> fenced here: on-premises databases and local development
/// legitimately live there, and they remain governed by the host allowlist and
/// <see cref="NetworkDestinationRules.IsRestrictedRange"/> instead. Fencing them unconditionally
/// would break every on-premises install without adding a boundary the allowlist does not already
/// provide.</para>
///
/// <para>Exemptions exist for the operator who genuinely runs a service on a fenced address, and are
/// server-owned: they come from authoritative organization policy
/// (<c>Security:EgressFenceExemptions</c>) or from the host's own configuration, never from a script.
/// An exemption must name the destination as an exact literal — wildcards are ignored by design, so
/// a broad allowlist can never widen the fence.</para>
/// </summary>
public static class InfrastructureEgressFence
{
    /// <summary>Organization policy key prefix carrying operator exemptions.</summary>
    public const string ExemptionPolicyKeyPrefix = "Security:EgressFenceExemptions:";

    /// <summary>
    /// Cloud instance metadata and instance identity endpoints. Every major provider serves
    /// credentials from a fixed address, which is exactly why it is the first thing a hostile script
    /// reaches for.
    /// </summary>
    private static readonly string[] MetadataAddresses =
    [
        "169.254.169.254",  // AWS / GCP / Azure / OpenStack / DigitalOcean IMDS
        "169.254.169.253",  // AWS VPC DNS (link-local resolver)
        "169.254.169.123",  // AWS time sync
        "169.254.170.2",    // ECS task metadata / credential endpoint
        "169.254.170.23",   // ECS agent introspection (IPv4)
        "169.254.171.2",    // EKS Pod Identity agent
        "168.63.129.16",    // Azure WireServer / host plugin (not a private range)
        "100.100.100.200",  // Alibaba Cloud metadata
        "192.0.0.192",      // Oracle Cloud legacy metadata (not a private range)
        "fd00:ec2::254",    // AWS IMDS over IPv6
        "fd00:ec2::253"     // AWS VPC DNS over IPv6
    ];

    /// <summary>Metadata service DNS names, matched exactly so a corporate <c>*.internal</c> zone is unaffected.</summary>
    private static readonly string[] MetadataNames =
    [
        "metadata.google.internal",
        "metadata.goog",
        "metadata",
        "instance-data",
        "instance-data.ec2.internal"
    ];

    /// <summary>Container runtime host-bridge names — reaching the node's own runtime is an escape, not a data source.</summary>
    private static readonly string[] ContainerRuntimeNames =
    [
        "host.docker.internal",
        "gateway.docker.internal",
        "vm.docker.internal",
        "host.containers.internal",
        "host.lima.internal",
        "host.minikube.internal",
        "docker.for.win.localhost",
        "docker.for.mac.localhost",
        "docker.for.win.host.internal",
        "docker.for.mac.host.internal",
        "kubernetes",
        "kubernetes.default",
        "kubernetes.default.svc",
        "kubernetes.default.svc.cluster.local"
    ];

    /// <summary>Cluster service-discovery suffixes. None of these are delegable public zones.</summary>
    private static readonly string[] ClusterDiscoverySuffixes =
    [
        ".svc.cluster.local",
        ".pod.cluster.local",
        ".cluster.local",
        ".svc"
    ];

    private static readonly HashSet<string> ConfiguredExemptions =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly AsyncLocal<HashSet<string>?> ScopedExemptions = new();

    private static readonly object ExemptionLock = new();

    /// <summary>
    /// Replaces the host's own exemption list, read from server-owned configuration
    /// (<c>Security:EgressFenceExemptions</c>). Wildcard entries are dropped: the fence is not
    /// something a broad allowlist may switch off.
    /// </summary>
    public static void SetLocalExemptions(IEnumerable<string>? exemptions)
    {
        lock (ExemptionLock)
        {
            ConfiguredExemptions.Clear();
            if (exemptions is null) return;
            foreach (var entry in exemptions)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var trimmed = entry.Trim();
                if (trimmed.Contains('*', StringComparison.Ordinal)) continue;
                ConfiguredExemptions.Add(NetworkDestinationRules.Normalize(trimmed));
            }
        }
    }

    /// <summary>
    /// Adds exemptions for the calling thread only, for tests and for tightly-scoped host operations.
    /// Disposing restores the previous scope.
    /// </summary>
    public static IDisposable UseExemptionsForScope(params string[] exemptions)
    {
        var previous = ScopedExemptions.Value;
        var scope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in exemptions ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var trimmed = entry.Trim();
            if (trimmed.Contains('*', StringComparison.Ordinal)) continue;
            scope.Add(NetworkDestinationRules.Normalize(trimmed));
        }

        ScopedExemptions.Value = scope;
        return new ExemptionScope(previous);
    }

    /// <summary>
    /// Classifies a host literal or DNS name. Obfuscated IP literals (32-bit decimal, hex, octal,
    /// IPv4-mapped IPv6, bracketed IPv6) are normalized first, so an alternate address form cannot
    /// slip past the fence.
    /// </summary>
    public static InfrastructureDestinationClass Classify(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return InfrastructureDestinationClass.None;

        var normalized = NetworkDestinationRules.Normalize(host);
        if (NetworkDestinationRules.TryParseAddress(normalized, out var address))
            return ClassifyAddress(address);

        var name = normalized.TrimEnd('.');
        if (MetadataNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return InfrastructureDestinationClass.CloudMetadata;
        if (ContainerRuntimeNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return InfrastructureDestinationClass.ContainerRuntime;
        if (ClusterDiscoverySuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            return InfrastructureDestinationClass.ClusterServiceDiscovery;

        return InfrastructureDestinationClass.None;
    }

    /// <summary>Classifies a resolved IP address. Used by the connect-time rebinding defense.</summary>
    public static InfrastructureDestinationClass ClassifyAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var literal = address.ToString();
        if (MetadataAddresses.Contains(literal, StringComparer.OrdinalIgnoreCase))
            return InfrastructureDestinationClass.CloudMetadata;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();
            if (octets[0] == 169 && octets[1] == 254)
                return InfrastructureDestinationClass.LinkLocalNodeService;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal)
        {
            return InfrastructureDestinationClass.LinkLocalNodeService;
        }

        return InfrastructureDestinationClass.None;
    }

    /// <summary>
    /// Denies an egress attempt to a fenced infrastructure destination. Call before DNS resolution or
    /// connection creation, alongside — not instead of — the organization allowlist. No-op for
    /// ordinary destinations and for exempted literals.
    /// </summary>
    public static void EnforceHost(string? host)
    {
        var classification = Classify(host);
        if (classification == InfrastructureDestinationClass.None) return;
        if (IsExempt(host!)) return;

        Deny(host!, classification,
            $"Outbound access to hosting infrastructure ({Describe(classification)}) is denied for all " +
            "tenants and deployment topologies. This fence is not relaxed by a host allowlist; an " +
            "operator must add an exact-literal Security:EgressFenceExemptions entry to permit it.");
    }

    /// <summary>
    /// Connect-time rebinding defense for the fenced classes. A name that passed
    /// <see cref="EnforceHost"/> must not be able to resolve to the metadata service or a node-local
    /// address between check and connect. Unlike
    /// <see cref="ConnectorPolicyAuthorizer.EnforceResolvedAddress"/> this runs with no enrollment or
    /// allowlist precondition, because an unenrolled worker is still running on someone's node.
    /// </summary>
    public static void EnforceResolvedAddress(string host, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var classification = ClassifyAddress(address);
        if (classification == InfrastructureDestinationClass.None) return;
        if (IsExempt(address.ToString())) return;

        Deny(host, classification,
            $"DNS resolution reached hosting infrastructure ({Describe(classification)}); " +
            "rebinding a permitted name onto an infrastructure address is denied for all tenants.");
    }

    /// <summary>Server-owned exemptions currently in effect, normalized. Exposed for diagnostics and tests.</summary>
    public static IReadOnlyCollection<string> Exemptions
    {
        get
        {
            var effective = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (ExemptionLock)
            {
                effective.UnionWith(ConfiguredExemptions);
            }
            effective.UnionWith(PolicyExemptions());
            if (ScopedExemptions.Value is { } scoped) effective.UnionWith(scoped);
            return effective;
        }
    }

    private static bool IsExempt(string destination)
    {
        var normalized = NetworkDestinationRules.Normalize(destination).TrimEnd('.');
        return Exemptions.Contains(normalized);
    }

    private static IEnumerable<string> PolicyExemptions()
    {
        var policy = EnterprisePolicyRuntime.Current;
        if (!policy.IsEnrolled) return [];

        return policy.ConfigurationValues
            .Where(pair => pair.Key.StartsWith(ExemptionPolicyKeyPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value!.Trim())
            .Where(value => !value.Contains('*', StringComparison.Ordinal))
            .Select(value => NetworkDestinationRules.Normalize(value))
            .ToArray();
    }

    private static void Deny(string host, InfrastructureDestinationClass classification, string reason)
    {
        // The event records the host the script named, never the resolved address — the address is
        // the thing the caller was not supposed to learn.
        SecurityEventRuntime.EmitNetworkDenial(EnterprisePolicyRuntime.Current, Sanitize(host, classification), reason);
        throw new SecurityException(reason);
    }

    // A fenced literal is itself infrastructure detail; report the class for a raw address so a
    // denial message cannot confirm which metadata endpoint answers on this node.
    private static string Sanitize(string host, InfrastructureDestinationClass classification) =>
        NetworkDestinationRules.TryParseAddress(NetworkDestinationRules.Normalize(host), out _)
            ? $"<{Describe(classification)}>"
            : host;

    private static string Describe(InfrastructureDestinationClass classification) => classification switch
    {
        InfrastructureDestinationClass.CloudMetadata => "cloud instance metadata service",
        InfrastructureDestinationClass.LinkLocalNodeService => "link-local node service range",
        InfrastructureDestinationClass.ContainerRuntime => "container runtime host bridge",
        InfrastructureDestinationClass.ClusterServiceDiscovery => "cluster service discovery",
        _ => "hosting infrastructure"
    };

    private sealed class ExemptionScope(HashSet<string>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            ScopedExemptions.Value = previous;
            _disposed = true;
        }
    }
}
