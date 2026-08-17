using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Hostile-egress coverage for <see cref="InfrastructureEgressFence"/> — the non-bypassable outbound
/// fence that holds in every topology, including a standalone or unenrolled host with no allowlist.
///
/// <para>The property under test is the one the SaaS isolation architecture requires and that the
/// host allowlist alone did not deliver: a tenant workload cannot reach the cloud metadata service,
/// the node's container runtime, or cluster service discovery <b>whatever the connector policy says</b>
/// — the default <c>AllowedHosts: ["*"]</c> included. Loopback and RFC 1918 private ranges are
/// deliberately still reachable, because on-premises databases live there.</para>
/// </summary>
public sealed class InfrastructureEgressFenceTests : IDisposable
{
    public void Dispose()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        InfrastructureEgressFence.SetLocalExemptions(null);
    }

    // ---------------------------------------------------------------- classification

    [Theory]
    // Canonical metadata endpoints across providers.
    [InlineData("169.254.169.254")]
    [InlineData("169.254.170.2")]
    [InlineData("168.63.129.16")]
    [InlineData("100.100.100.200")]
    [InlineData("192.0.0.192")]
    [InlineData("fd00:ec2::254")]
    [InlineData("[fd00:ec2::254]")]
    // Alternate address forms for 169.254.169.254 — the fence normalizes before classifying.
    [InlineData("2852039166")]                 // 32-bit decimal
    [InlineData("0xa9fea9fe")]                 // 32-bit hex
    [InlineData("0251.0376.0251.0376")]        // dotted octal
    [InlineData("0xa9.0xfe.0xa9.0xfe")]        // dotted hex
    [InlineData("::ffff:169.254.169.254")]     // IPv4-mapped IPv6
    // Metadata DNS names, including a fully-qualified trailing dot.
    [InlineData("metadata.google.internal")]
    [InlineData("metadata.google.internal.")]
    [InlineData("metadata.goog")]
    [InlineData("metadata")]
    [InlineData("instance-data")]
    public void Classify_FlagsCloudMetadataInEveryAddressForm(string host) =>
        Assert.Equal(InfrastructureDestinationClass.CloudMetadata, InfrastructureEgressFence.Classify(host));

    [Theory]
    [InlineData("169.254.1.1")]                // kubelet / node-local agents
    [InlineData("169.254.0.0")]
    [InlineData("169.254.255.255")]
    [InlineData("fe80::1")]
    public void Classify_FlagsLinkLocalNodeServices(string host) =>
        Assert.Equal(InfrastructureDestinationClass.LinkLocalNodeService, InfrastructureEgressFence.Classify(host));

    [Theory]
    [InlineData("host.docker.internal")]
    [InlineData("gateway.docker.internal")]
    [InlineData("host.containers.internal")]
    [InlineData("kubernetes.default.svc")]
    [InlineData("kubernetes.default.svc.cluster.local")]
    public void Classify_FlagsContainerRuntimeBridge(string host) =>
        Assert.Equal(InfrastructureDestinationClass.ContainerRuntime, InfrastructureEgressFence.Classify(host));

    [Theory]
    [InlineData("payments.other-tenant.svc.cluster.local")]
    [InlineData("pod-1.pod.cluster.local")]
    [InlineData("etcd.svc")]
    public void Classify_FlagsClusterServiceDiscovery(string host) =>
        Assert.Equal(InfrastructureDestinationClass.ClusterServiceDiscovery, InfrastructureEgressFence.Classify(host));

    [Theory]
    // On-premises reality: private ranges, loopback, and corporate .internal zones must stay
    // reachable. Fencing these would break every on-premises install and add no boundary the host
    // allowlist does not already provide.
    [InlineData("db.corp.internal")]
    [InlineData("sales.internal")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.9")]
    [InlineData("192.168.1.10")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("api.example.com")]
    [InlineData("169.253.169.254")]            // adjacent to link-local, not in it
    [InlineData("169.255.0.1")]
    public void Classify_LeavesOrdinaryAndPrivateDestinationsToPolicy(string host) =>
        Assert.Equal(InfrastructureDestinationClass.None, InfrastructureEgressFence.Classify(host));

    // ---------------------------------------------------------------- topology independence

    [Fact]
    public void EnforceHost_DeniesMetadata_WhenStandaloneAndUnenrolled()
    {
        // The gap this fence closes: with no organization policy at all, nothing previously stopped a
        // script reaching the instance credential endpoint.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        var denied = Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("169.254.169.254"));
        Assert.Contains("hosting infrastructure", denied.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnforceHost_IsNotRelaxedByWildcardAllowlist()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["*"]));

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("metadata.google.internal"));
    }

    [Fact]
    public void EnforceHost_IsNotRelaxedByExplicitHostAllowlistEntry()
    {
        // Listing the metadata address in Security:AllowedHosts is enough to satisfy the connector
        // allowlist, but the fence is a separate control with its own exemption surface — otherwise
        // the "explicitly listed internal address" escape hatch would reopen IMDS.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["169.254.169.254"]));

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("169.254.169.254"));
    }

    [Fact]
    public void EnforceHost_PolicyChangeDuringRunCannotWidenTheFence()
    {
        // A run starts under a narrow policy; policy is then replaced mid-run with the broadest
        // possible allowlist. The fence reads current policy for exemptions only, so the widened
        // allowlist changes nothing.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["db.corp.internal"]));
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("169.254.169.254"));

        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["*"], allowedPorts: [80, 443, 8080]));
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("169.254.169.254"));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(443)]
    [InlineData(1)]
    [InlineData(10250)]   // kubelet
    [InlineData(2375)]    // docker daemon
    [InlineData(65535)]
    public void EnforceEnterpriseUrl_FencedHostIsDeniedAtEveryPort(int port)
    {
        // Port scanning a fenced host: the fence is port-independent, so probing for an open service
        // on the node is denied uniformly rather than at a subset of ports.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        var context = StandaloneContext();

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(
                context, new Uri($"http://169.254.169.254:{port}/latest/meta-data/")));
    }

    [Fact]
    public void EnforceEnterpriseUrl_DeniesRedirectToMetadata_WhenUnenrolled()
    {
        // The dynamic REST path (initial request, redirects, pagination, template targets) is fenced
        // too, so a 302 to IMDS is refused on a host with no organization policy.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        var context = StandaloneContext();

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(
                context, new Uri("http://169.254.169.254/latest/meta-data/iam/security-credentials/")));

        // An ordinary public destination is untouched by the fence.
        Assert.Null(Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(context, new Uri("https://api.example.com/v1"))));
    }

    [Fact]
    public void EnforceResolvedAddress_DeniesRebindToMetadata_WhenUnenrolled()
    {
        // DNS rebinding without an allowlist: the enterprise rebinding check no-ops when no host
        // allowlist is configured, so before the fence a permitted name could resolve onto IMDS.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress(
                "api.example.com", System.Net.IPAddress.Parse("169.254.169.254")));
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress(
                "api.example.com", System.Net.IPAddress.Parse("169.254.1.1")));
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress(
                "api.example.com", System.Net.IPAddress.Parse("::ffff:169.254.169.254")));

        // Loopback and private ranges stay resolvable when unenrolled — unchanged behavior.
        Assert.Null(Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress("localhost", System.Net.IPAddress.Loopback)));
        Assert.Null(Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress("db.corp.internal", System.Net.IPAddress.Parse("10.0.0.5"))));
    }

    // ---------------------------------------------------------------- exemptions

    [Fact]
    public void Exemption_ExactLiteralPermitsTheOperatorsOwnService()
    {
        InfrastructureEgressFence.SetLocalExemptions(["169.254.1.50"]);

        Assert.Null(Record.Exception(() => InfrastructureEgressFence.EnforceHost("169.254.1.50")));
        // Exempting one address does not exempt the class.
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("169.254.169.254"));
    }

    [Fact]
    public void Exemption_MatchesAcrossAlternateAddressForms()
    {
        // An operator exemption written in canonical form still applies when a script names the same
        // address obfuscated — and vice versa, so the exemption set cannot be probed for a gap.
        InfrastructureEgressFence.SetLocalExemptions(["169.254.169.254"]);
        Assert.Null(Record.Exception(() => InfrastructureEgressFence.EnforceHost("2852039166")));

        InfrastructureEgressFence.SetLocalExemptions(["0xa9fea9fe"]);
        Assert.Null(Record.Exception(() => InfrastructureEgressFence.EnforceHost("169.254.169.254")));
    }

    [Fact]
    public void Exemption_WildcardEntriesAreIgnored()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        InfrastructureEgressFence.SetLocalExemptions(["*", "169.254.*", "*.docker.internal"]);

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("169.254.169.254"));
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("host.docker.internal"));
        Assert.Empty(InfrastructureEgressFence.Exemptions);
    }

    [Fact]
    public void Exemption_FromOrganizationPolicyIsHonoured()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(fenceExemptions: ["host.docker.internal"]));

        Assert.Null(Record.Exception(() => InfrastructureEgressFence.EnforceHost("host.docker.internal")));
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("gateway.docker.internal"));
    }

    [Fact]
    public void Exemption_FromOrganizationPolicyIsIgnoredWhenUnenrolled()
    {
        // A policy document that is not the authoritative live policy cannot open the fence.
        InfrastructureEgressFence.SetLocalExemptions(null);
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("host.docker.internal"));
    }

    // ---------------------------------------------------------------- evidence

    [Fact]
    public void Denial_EmitsOneSecurityEventThatDoesNotEchoTheAddress()
    {
        var sink = new RecordingSink();
        using var scope = SecurityEventRuntime.UseSinkForScope(sink);
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("169.254.169.254"));

        var securityEvent = Assert.Single(sink.Events);
        Assert.Equal(SecurityEventType.OperationDenied, securityEvent.Type);
        Assert.Equal(SecurityEventDecision.Denied, securityEvent.Decision);
        // The denial must not confirm which infrastructure address answers on this node.
        Assert.DoesNotContain("169.254.169.254", securityEvent.SanitizedTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("169.254.169.254", securityEvent.Reason, StringComparison.Ordinal);
        Assert.Contains("cloud instance metadata service", securityEvent.SanitizedTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void Denial_ForANameRecordsTheNameTheScriptAsked()
    {
        var sink = new RecordingSink();
        using var scope = SecurityEventRuntime.UseSinkForScope(sink);
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            InfrastructureEgressFence.EnforceHost("metadata.google.internal"));

        var securityEvent = Assert.Single(sink.Events);
        Assert.Equal("metadata.google.internal", securityEvent.SanitizedTarget);
    }

    // ---------------------------------------------------------------- policy document + registry

    [Fact]
    public void PolicyDocument_RejectsWildcardAndNonFencedExemptions()
    {
        var document = new OrganizationPolicyDocument
        {
            Network = new NetworkPolicySection
            {
                EgressFenceExemptions = ["169.254.*", "db.corp.internal", " ", "169.254.1.5", "169.254.1.5"]
            }
        };

        var result = OrganizationPolicySchema.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("wildcards are not permitted", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("does not name a fenced infrastructure destination", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("blank entries", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PolicyDocument_AcceptsAnExactFencedExemption()
    {
        var document = new OrganizationPolicyDocument
        {
            Network = new NetworkPolicySection { EgressFenceExemptions = ["169.254.1.5", "host.docker.internal"] }
        };

        Assert.DoesNotContain(OrganizationPolicySchema.Validate(document).Errors,
            error => error.Contains("exemption", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GovernanceRegistry_PublishesTheExemptionSurface()
    {
        var registry = GovernancePolicyRegistry.CreateDefault();

        Assert.True(registry.TryGet("Security:EgressFenceExemptions", out var definition));
        Assert.Equal(GovernancePolicyScope.Network, definition.Scope);
        Assert.Equal(GovernancePolicyValueKind.StringList, definition.ValueKind);
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public async Task CreateConnection_ToMetadataService_IsDeniedWithNoOrganizationPolicy()
    {
        // The whole point, exercised through the language: default configuration, no enrollment, no
        // allowlist — and the statement still cannot name the instance credential endpoint.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            "CREATE CONNECTION imds AS REST('http://169.254.169.254/latest/meta-data/');"));
        Assert.Contains("hosting infrastructure", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateConnection_ToObfuscatedMetadataAddress_IsDenied()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            "CREATE CONNECTION pg AS POSTGRES(HOST = '2852039166', DATABASE = 'd');"));
        Assert.Contains("hosting infrastructure", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("172.20.3.4")]
    [InlineData("db.corp.internal")]
    public void PrivateAndOnPremisesDestinations_PassTheFence(string host)
    {
        // Regression guard for on-premises deployments: the fence must not have swept up RFC 1918 or
        // corporate .internal zones. Asserted at the authorizer boundary rather than end to end,
        // because an end-to-end CREATE CONNECTION would attempt a real socket connect to a
        // non-routable address and block on the connect timeout.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        Assert.Null(Record.Exception(() => InfrastructureEgressFence.EnforceHost(host)));
        Assert.Null(Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseHost(StandaloneContext(), host)));
    }

    [Fact]
    public async Task CreateConnection_ToLoopbackDatabase_IsUnaffectedByTheFence()
    {
        // End-to-end companion to the theory above, on the one internal address that fails fast
        // instead of hanging: creation may fail on the connection itself, never on the fence.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        var error = await Record.ExceptionAsync(() => ExecuteAsync(
            "CREATE CONNECTION pg AS POSTGRES(HOST = '127.0.0.1', DATABASE = 'd');"));

        Assert.DoesNotContain("hosting infrastructure", error?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PolicyBoundHttpClient_CannotConnectToMetadata_WhenUnenrolled()
    {
        // The real socket path: PolicyBoundHttp resolves the host itself and validates every resolved
        // address before connecting, so the fence stops the request at the connect callback with no
        // packet leaving the process — and with no organization policy in play.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        using var client = PolicyBoundHttp.CreateClient(timeout: TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.GetAsync(new Uri("http://169.254.169.254/latest/meta-data/")));

        Assert.Contains("hosting infrastructure", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- helpers

    private static IExecutionContext StandaloneContext()
    {
        var context = new Moq.Mock<IExecutionContext>();
        context.SetupGet(c => c.ExecutionPolicy).Returns(ExecutionPolicySnapshot.Capture(
            EnterprisePolicyRuntime.Current, "operator", ScriptExecutionMode.Batch, "hash"));
        context.SetupGet(c => c.SecurityService).Returns(
            new ETL_SQL.Services.SecurityService(ETL_SQL.Common.NullLogger.Instance));
        return context.Object;
    }

    private static async Task ExecuteAsync(string sql)
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }

    private static EffectiveEnterprisePolicy EnrolledPolicy(
        string[]? allowedHosts = null,
        int[]? allowedPorts = null,
        string[]? fenceExemptions = null)
    {
        var document = new OrganizationPolicyDocument
        {
            Network = new NetworkPolicySection
            {
                AllowedPorts = allowedPorts ?? [],
                EgressFenceExemptions = fenceExemptions ?? []
            },
            RemoteExecution = new RemoteExecutionPolicySection
            {
                Mode = allowedHosts is { Length: > 0 }
                    ? RemoteExecutionMode.AllowedHosts
                    : RemoteExecutionMode.Disabled,
                AllowedHosts = allowedHosts ?? []
            }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }

    private sealed class RecordingSink : ISecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];
        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }
}
