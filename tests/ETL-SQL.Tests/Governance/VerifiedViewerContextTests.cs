using ETL_SQL.Core.Governance;
using ETL_SQL.Gateway;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Tests.Governance;

public sealed class VerifiedViewerContextTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly ViewerContextPolicy Policy = new(
        ["department", "region", "viewer_groups", "viewer_roles"], 60);

    [Fact]
    public void ValidEnvelope_BindsEveryAuthorityDimension()
    {
        var service = Service();
        var operation = Operation();
        var envelope = service.Sign(operation, "viewer@example.test", "admin@example.test", "svc-reporting-v3",
            new Dictionary<string, string> { ["department"] = "finance" }, Policy);

        var verified = service.Verify(envelope, operation, Resource());

        Assert.Equal(operation.TenantId, verified.TenantId);
        Assert.Equal(operation.ResourceId, verified.ResourceId);
        Assert.Equal(operation.OperationId, verified.OperationId);
        Assert.Equal("viewer@example.test", verified.ViewerId);
        Assert.Equal("admin@example.test", verified.RealViewerId);
        Assert.Equal("svc-reporting-v3", verified.ExecutingCredentialId);
    }

    [Fact]
    public void ForgedSignature_IsRejected()
    {
        var service = Service();
        var operation = Operation();
        var envelope = service.Sign(operation, "viewer", "viewer", "svc-reporting-v3",
            EmptyClaims(), Policy) with
        {
            ViewerId = "attacker"
        };

        Assert.Throws<GatewayProtocolException>(() => service.Verify(envelope, operation, Resource()));
    }

    [Fact]
    public void MissingSignatureOrClaimMap_FailsClosed()
    {
        var service = Service();
        var operation = Operation();
        var envelope = service.Sign(operation, "viewer", "viewer", "svc-reporting-v3", EmptyClaims(), Policy);

        Assert.Throws<GatewayProtocolException>(() =>
            service.Verify(envelope with { Signature = null! }, operation, Resource()));
        Assert.Throws<GatewayProtocolException>(() =>
            service.Verify(envelope with { Claims = null! }, operation, Resource()));
    }

    [Fact]
    public void ReplayedNonce_IsRejected()
    {
        var service = Service();
        var operation = Operation();
        var envelope = service.Sign(operation, "viewer", "viewer", "svc-reporting-v3", EmptyClaims(), Policy);
        service.Verify(envelope, operation, Resource());

        var error = Assert.Throws<GatewayProtocolException>(() => service.Verify(envelope, operation, Resource()));
        Assert.Contains("already been used", error.Message);
    }

    [Fact]
    public void ReplayedNonce_IsRejectedAfterGatewayRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"viewer-replay-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "nonces.json");
        try
        {
            var operation = Operation();
            var first = new HmacViewerContextEnvelopeService(
                "portal-gateway-v1", Key, new ViewerContextReplayStore(path));
            var envelope = first.Sign(operation, "viewer", "viewer", "svc-reporting-v3", EmptyClaims(), Policy);
            first.Verify(envelope, operation, Resource());

            var restarted = new HmacViewerContextEnvelopeService(
                "portal-gateway-v1", Key, new ViewerContextReplayStore(path));
            Assert.Throws<GatewayProtocolException>(() => restarted.Verify(envelope, operation, Resource()));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void EnvelopeForAnotherGatewayNode_IsRejected()
    {
        var service = Service();
        var operation = Operation();
        var envelope = service.Sign(operation, "viewer", "viewer", "svc-reporting-v3", EmptyClaims(), Policy);

        Assert.Throws<GatewayProtocolException>(() =>
            service.Verify(envelope, operation with { GatewayNodeId = "node-b" }, Resource()));
    }

    [Theory]
    [InlineData("tenant", "other-tenant")]
    [InlineData("gateway", "other-gateway")]
    [InlineData("resource", "other-resource")]
    [InlineData("operation", "other-operation")]
    [InlineData("credential", "other-credential")]
    public void CrossBoundaryEnvelope_IsRejected(string boundary, string replacement)
    {
        var service = Service();
        var operation = Operation();
        var envelope = service.Sign(operation, "viewer", "viewer", "svc-reporting-v3", EmptyClaims(), Policy);
        var changedOperation = boundary switch
        {
            "tenant" => operation with { TenantId = replacement },
            "gateway" => operation with { GatewayId = replacement },
            "resource" => operation with { ResourceId = replacement },
            "operation" => operation with { OperationId = replacement },
            _ => operation
        };
        var resource = boundary == "credential"
            ? Resource() with { ExecutingCredentialId = replacement }
            : Resource();

        Assert.Throws<GatewayProtocolException>(() => service.Verify(envelope, changedOperation, resource));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("roles")]
    [InlineData("groups")]
    [InlineData("operation")]
    public void ReservedClaims_AreRejected(string claim)
    {
        var service = Service();
        var policy = new ViewerContextPolicy([claim]);

        Assert.Throws<GatewayProtocolException>(() => service.Sign(
            Operation(), "viewer", "viewer", "svc-reporting-v3",
            new Dictionary<string, string> { [claim] = "attacker" }, policy));
    }

    [Fact]
    public void UnlistedClaim_IsRejected()
    {
        var service = Service();
        Assert.Throws<GatewayProtocolException>(() => service.Sign(
            Operation(), "viewer", "viewer", "svc-reporting-v3",
            new Dictionary<string, string> { ["clearance"] = "admin" }, Policy));
    }

    [Fact]
    public void InjectionCharacters_AreDataAndRemainCoveredBySignature()
    {
        var service = Service();
        var operation = Operation();
        const string hostile = "finance'; SET ROLE postgres; --";
        var envelope = service.Sign(operation, hostile, hostile, "svc-reporting-v3",
            new Dictionary<string, string> { ["department"] = hostile }, Policy);

        var verified = service.Verify(envelope, operation, Resource());

        Assert.Equal(hostile, verified.ViewerId);
        Assert.Equal(hostile, verified.Claims["department"]);
    }

    [Fact]
    public void Envelope_RoundTripsThroughTypedGatewayFrame()
    {
        var service = Service();
        var operation = Operation();
        var envelope = service.Sign(operation, "viewer", "viewer", "svc-reporting-v3",
            new Dictionary<string, string> { ["department"] = "finance" }, Policy);

        var decoded = GatewayFrame.Deserialize(new GatewayFrame
        {
            Kind = GatewayFrameKind.Operation,
            OperationId = operation.OperationId,
            ViewerContext = envelope
        }.Serialize());

        Assert.Equal(envelope.Signature, decoded.ViewerContext?.Signature);
        Assert.Equal(envelope.OperationId, decoded.ViewerContext?.OperationId);
        Assert.Equal("finance", decoded.ViewerContext?.Claims["department"]);
        Assert.Equal("node-a", decoded.ViewerContext?.GatewayNodeId);
    }

    [Fact]
    public void ExpiredEnvelope_IsRejected()
    {
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
        var service = Service(clock);
        var operation = Operation();
        var envelope = service.Sign(operation, "viewer", "viewer", "svc-reporting-v3", EmptyClaims(), Policy);
        clock.UtcNow = clock.UtcNow.AddSeconds(61);

        Assert.Throws<GatewayProtocolException>(() => service.Verify(envelope, operation, Resource()));
    }

    [Fact]
    public async Task Dispatcher_AuditsViewerAndExecutingCredential_AndPassesOnlyVerifiedContext()
    {
        var operation = Operation();
        var resource = Resource();
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(resource);
        await registry.ApproveAsync(resource.ResourceId);
        var service = Service();
        var envelope = service.Sign(operation, "viewer", "real-viewer", "svc-reporting-v3", EmptyClaims(), Policy);
        var executor = new CapturingExecutor();
        var sink = new RecordingSink();
        using var scope = SecurityEventRuntime.UseSinkForScope(sink);
        var dispatcher = new GatewayOperationDispatcher(registry, executor, new GatewayOutcomeLedger(), service);

        var frames = await dispatcher.DispatchAsync(new GatewayFrame
        {
            Kind = GatewayFrameKind.Operation,
            OperationId = operation.OperationId,
            TenantId = operation.TenantId,
            GatewayId = operation.GatewayId,
            ResourceId = operation.ResourceId,
            OperationClass = operation.Class,
            Effect = operation.Effect,
            Bounds = operation.Bounds,
            CorrelationId = operation.CorrelationId,
            ViewerContext = envelope,
            Request = "{}"
        }, operation.TenantId, operation.GatewayId, CancellationToken.None, operation.GatewayNodeId);

        Assert.Contains(frames, frame => frame.Kind == GatewayFrameKind.Complete);
        Assert.Equal("viewer", executor.Context?.ViewerId);
        var audit = Assert.Single(sink.Events);
        Assert.Equal(SecurityEventType.ViewerContextAccepted, audit.Type);
        Assert.Equal("viewer", audit.ActorIdentity);
        Assert.Equal("svc-reporting-v3", audit.EffectiveIdentity);
        Assert.Equal(operation.TenantId, audit.TenantId);
    }

    [Fact]
    public async Task ContextEnabledResource_FailsClosedWithoutEnvelopeOrVerifier()
    {
        var operation = Operation();
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(Resource());
        await registry.ApproveAsync(operation.ResourceId);
        var dispatcher = new GatewayOperationDispatcher(registry, new CapturingExecutor(), new GatewayOutcomeLedger());

        var frames = await dispatcher.DispatchAsync(new GatewayFrame
        {
            Kind = GatewayFrameKind.Operation,
            OperationId = operation.OperationId,
            ResourceId = operation.ResourceId,
            OperationClass = operation.Class,
            Effect = operation.Effect,
            Bounds = operation.Bounds
        }, operation.TenantId, operation.GatewayId, CancellationToken.None, operation.GatewayNodeId);

        var fault = Assert.Single(frames);
        Assert.Equal(GatewayFrameKind.Fault, fault.Kind);
        Assert.Contains("required", fault.Reason);
    }

    [Fact]
    public async Task PortalRouter_SignsServerDerivedViewerForSelectedGatewayNode()
    {
        const string token = "token-32-characters-minimum-for-test";
        var enrollments = new InMemoryGatewayEnrollmentStore();
        await enrollments.IssueAsync("tenant-a", "gateway-a", token, DateTimeOffset.UtcNow.AddMinutes(5));
        await enrollments.ConsumeAsync("tenant-a", token, "thumb-a");
        var session = new CapturingSession(Resource().ToPublishedMetadata());
        var sessions = new GatewaySessionRegistry();
        sessions.TryRegister(session);
        var signer = Service();
        var router = new PortalGatewayOperationRouter(enrollments, sessions, new TestGrantResolver(), signer);

        await router.ExecuteAsync(new ExecutionIdentity
        {
            EffectiveUser = "viewer@example.test",
            RealUser = "admin@example.test",
            TenantId = "tenant-a",
            IsAdmin = false,
            Groups = ["finance"],
            Roles = ["analyst"]
        }, new GatewayResourceBinding("gateway-a", "postgres-reports"),
            GatewayOperationClass.Read, GatewayOperationEffect.ReadOnly,
            GatewayOperationBounds.Default, "{}", null, CancellationToken.None);

        Assert.NotNull(session.Envelope);
        Assert.NotNull(session.Operation);
        var verified = signer.Verify(session.Envelope!, session.Operation!, Resource());
        Assert.Equal("viewer@example.test", verified.ViewerId);
        Assert.Equal("admin@example.test", verified.RealViewerId);
        Assert.Equal(session.NodeId, session.Envelope!.GatewayNodeId);
        Assert.Equal("[\"finance\"]", verified.Claims["viewer_groups"]);
        Assert.Equal("[\"analyst\"]", verified.Claims["viewer_roles"]);
    }

    private static HmacViewerContextEnvelopeService Service(TimeProvider? timeProvider = null) =>
        new("portal-gateway-v1", Key, new ViewerContextReplayStore(), timeProvider);

    private static IReadOnlyDictionary<string, string> EmptyClaims() =>
        new Dictionary<string, string>();

    private static GatewayOperation Operation() => new(
        "op-1", "tenant-a", "gateway-a", "postgres-reports", GatewayOperationClass.Read,
        GatewayOperationEffect.ReadOnly, GatewayOperationBounds.Default, "corr-1", "node-a");

    private static GatewayResource Resource() => new(
        "postgres-reports", "POSTGRES", "Host=internal", "ENV:POSTGRES_PASSWORD",
        GatewayOperationClass.Read, new GatewayResourceLimits(), GatewayResourceState.Approved,
        "Reports", "svc-reporting-v3", Policy);

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class CapturingExecutor : IGatewayResourceExecutor
    {
        public VerifiedViewerContext? Context { get; private set; }

        public Task<GatewayExecutionResult> ExecuteAsync(
            GatewayResource resource, GatewayOperationClass operationClass, string? request,
            IReadOnlyList<string>? parameters, GatewayOperationBounds bounds,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The unverified execution path must not be used.");

        public Task<GatewayExecutionResult> ExecuteAsync(
            GatewayResource resource, GatewayOperationClass operationClass, string? request,
            IReadOnlyList<string>? parameters, VerifiedViewerContext? viewerContext,
            GatewayOperationBounds bounds, CancellationToken cancellationToken)
        {
            Context = viewerContext;
            return Task.FromResult(new GatewayExecutionResult([], []));
        }
    }

    private sealed class RecordingSink : ISecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];
        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }

    private sealed class CapturingSession(GatewayPublishedResource resource) : IGatewaySession
    {
        public string TenantId => "tenant-a";
        public string GatewayId => "gateway-a";
        public string NodeId => "node-a";
        public string WorkloadPublicKeyThumbprint => "thumb-a";
        public DateTimeOffset ConnectedUtc => DateTimeOffset.UtcNow;
        public bool IsActive => true;
        public IReadOnlyList<GatewayPublishedResource> PublishedResources => [resource];
        public GatewayOperation? Operation { get; private set; }
        public ViewerContextEnvelope? Envelope { get; private set; }

        public Task<GatewayExecutionResult> ExecuteAsync(
            GatewayOperation operation, string? request, IReadOnlyList<string>? parameters,
            CancellationToken cancellationToken) =>
            ExecuteAsync(operation, request, parameters, null, cancellationToken);

        public Task<GatewayExecutionResult> ExecuteAsync(
            GatewayOperation operation, string? request, IReadOnlyList<string>? parameters,
            ViewerContextEnvelope? viewerContext, CancellationToken cancellationToken)
        {
            Operation = operation;
            Envelope = viewerContext;
            return Task.FromResult(new GatewayExecutionResult([], []));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestGrantResolver : IPortalGatewayGrantResolver
    {
        public Task<IReadOnlyList<GatewayResourceGrant>> ResolveAsync(
            ExecutionIdentity identity,
            GatewayResourceBinding binding,
            GatewayOperationClass operationClass,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GatewayResourceGrant>>(
            [
                new GatewayResourceGrant(
                    identity.TenantId!, binding.GatewayId, binding.ResourceId,
                    identity.EffectiveUser, operationClass)
            ]);
    }
}
