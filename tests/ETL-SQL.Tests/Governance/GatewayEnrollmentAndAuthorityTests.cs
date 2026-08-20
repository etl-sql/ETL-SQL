using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Governance;

/// <summary>
/// Slices D2, D3, D5, D6, and D7 of the Secure Outbound Data Gateway: enrollment lifecycle, the
/// Gateway-local resource registry, authority agreement, revocation, and the operator boundary.
///
/// <para>The authority tests deliberately drive one clause at a time from a request that is
/// otherwise fully valid. The recurring failure in this codebase has been that each door is only
/// ever tested on its own while nothing asserts the whole set agrees, so every clause here is
/// falsified against a request that would otherwise be allowed.</para>
/// </summary>
public sealed class GatewayEnrollmentAndAuthorityTests
{
    private const string Tenant = "tenant-acme";
    private const string OtherTenant = "tenant-globex";
    private const string GatewayId = "hq-gateway";
    private const string ResourceId = "corp-sql-sales";
    private const string Actor = "svc-etl";
    private const string PolicyVersion = "v42";
    private const string Token = "0123456789abcdef0123456789abcdef";

    // ------------------------------------------------------------------ D2: enrollment lifecycle

    [Fact]
    public async Task Enrollment_IsConsumableExactlyOnce()
    {
        var store = new InMemoryGatewayEnrollmentStore();
        await store.IssueAsync(Tenant, GatewayId, Token, DateTimeOffset.UtcNow.AddHours(1));

        var consumed = await store.ConsumeAsync(Tenant, Token, "thumb-abc");
        Assert.Equal(GatewayEnrollmentState.Consumed, consumed.State);
        Assert.Equal("thumb-abc", consumed.WorkloadPublicKeyThumbprint);

        // The second installer to present the same token gets nothing.
        await Assert.ThrowsAsync<GatewayEnrollmentException>(
            () => store.ConsumeAsync(Tenant, Token, "thumb-xyz"));
    }

    [Fact]
    public async Task Enrollment_StoresOnlyAHashSoTheRecordCannotEnrolAGateway()
    {
        var store = new InMemoryGatewayEnrollmentStore();
        var issued = await store.IssueAsync(Tenant, GatewayId, Token, DateTimeOffset.UtcNow.AddHours(1));

        Assert.DoesNotContain(Token, issued.TokenHash, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GatewayEnrollmentToken.Hash(Token), issued.TokenHash);
    }

    [Fact]
    public async Task Enrollment_RefusesExpiredRevokedAndCrossTenantPresentation()
    {
        var expired = new InMemoryGatewayEnrollmentStore();
        await expired.IssueAsync(Tenant, GatewayId, Token, DateTimeOffset.UtcNow.AddSeconds(-1));
        await Assert.ThrowsAsync<GatewayEnrollmentException>(
            () => expired.ConsumeAsync(Tenant, Token, "thumb"));

        var revoked = new InMemoryGatewayEnrollmentStore();
        await revoked.IssueAsync(Tenant, GatewayId, Token, DateTimeOffset.UtcNow.AddHours(1));
        await revoked.RevokeAsync(Tenant, GatewayId);
        await Assert.ThrowsAsync<GatewayEnrollmentException>(
            () => revoked.ConsumeAsync(Tenant, Token, "thumb"));

        // A valid token presented under another tenant must look exactly like an unknown token.
        var crossTenant = new InMemoryGatewayEnrollmentStore();
        await crossTenant.IssueAsync(Tenant, GatewayId, Token, DateTimeOffset.UtcNow.AddHours(1));
        var wrongTenant = await Assert.ThrowsAsync<GatewayEnrollmentException>(
            () => crossTenant.ConsumeAsync(OtherTenant, Token, "thumb"));
        var unknownToken = await Assert.ThrowsAsync<GatewayEnrollmentException>(
            () => crossTenant.ConsumeAsync(Tenant, new string('f', 32), "thumb"));
        Assert.Equal(unknownToken.Message, wrongTenant.Message);
    }

    [Fact]
    public async Task Enrollment_RefusesAWeakOneTimeToken()
    {
        var store = new InMemoryGatewayEnrollmentStore();
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.IssueAsync(Tenant, GatewayId, "short", DateTimeOffset.UtcNow.AddHours(1)));
    }

    // ------------------------------------------------------- D3: Gateway-local resource registry

    [Fact]
    public async Task Discovery_ProposesButCannotApprove()
    {
        var registry = new GatewayResourceRegistry();
        var proposed = await registry.ProposeAsync(SampleResource());

        Assert.Equal(GatewayResourceState.Proposed, proposed.State);
        // A proposed resource is inert: it neither executes nor reaches the tenant catalog.
        await Assert.ThrowsAsync<GatewayResourceException>(
            () => registry.ResolveForExecutionAsync(ResourceId, GatewayOperationClass.Read));
        Assert.Empty(await registry.PublishAsync());

        await registry.ApproveAsync(ResourceId);
        Assert.Single(await registry.PublishAsync());
    }

    [Fact]
    public async Task Discovery_CannotRedefineAnApprovedResource()
    {
        // Otherwise a rogue discovery pass could repoint an approved alias at a target of its choice.
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(SampleResource());
        await registry.ApproveAsync(ResourceId);

        await Assert.ThrowsAsync<GatewayResourceException>(
            () => registry.ProposeAsync(SampleResource() with { LocalTarget = "attacker.internal:1433" }));

        var stillOriginal = await registry.ResolveForExecutionAsync(ResourceId, GatewayOperationClass.Read);
        Assert.Equal("sqlserver://myserver:1433/Sales", stillOriginal.LocalTarget);
    }

    [Fact]
    public async Task PublishedMetadata_CarriesNoTargetAndNoCredential()
    {
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(SampleResource());
        await registry.ApproveAsync(ResourceId);

        var published = Assert.Single(await registry.PublishAsync());
        var serialized = System.Text.Json.JsonSerializer.Serialize(published);

        Assert.DoesNotContain("myserver", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1433", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sales-etl-credential", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ResourceId, published.ResourceId);
    }

    [Fact]
    public async Task Resource_RefusesAnOperationClassItDoesNotPermit()
    {
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(SampleResource() with { AllowedOperations = GatewayOperationClass.Read });
        await registry.ApproveAsync(ResourceId);

        await Assert.ThrowsAsync<GatewayResourceException>(
            () => registry.ResolveForExecutionAsync(ResourceId, GatewayOperationClass.Write));
        Assert.NotNull(await registry.ResolveForExecutionAsync(ResourceId, GatewayOperationClass.Read));
    }

    [Fact]
    public async Task Resource_RequiresPositiveBoundsAndAtLeastOneOperation()
    {
        var registry = new GatewayResourceRegistry();

        await Assert.ThrowsAsync<GatewayResourceException>(
            () => registry.ProposeAsync(SampleResource() with { AllowedOperations = GatewayOperationClass.None }));
        await Assert.ThrowsAsync<GatewayResourceException>(
            () => registry.ProposeAsync(SampleResource() with { Limits = new GatewayResourceLimits(MaxRows: 0) }));
        await Assert.ThrowsAsync<GatewayResourceException>(
            () => registry.ProposeAsync(SampleResource() with { ResourceId = "../other-tenant" }));
    }

    [Fact]
    public async Task ProtectedRegistry_SurvivesRestartWithoutPlaintextTargetOrCredential()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gateway-registry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "resources.protected");
        try
        {
            var first = new GatewayResourceRegistry(path);
            await first.ProposeAsync(SampleResource());
            await first.ApproveAsync(ResourceId);

            var bytes = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("myserver", bytes, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sales-etl-credential", bytes, StringComparison.OrdinalIgnoreCase);

            var restarted = new GatewayResourceRegistry(path);
            var restored = await restarted.ResolveForExecutionAsync(ResourceId, GatewayOperationClass.Read);
            Assert.Equal("sqlserver://myserver:1433/Sales", restored.LocalTarget);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    // --------------------------------------------------------------- D5: all seven clauses agree

    [Fact]
    public void Authority_AllowsOnlyWhenEverySourceAgrees()
    {
        Assert.True(Evaluate().Allowed);
    }

    [Theory]
    [InlineData(GatewayAuthorityDenial.ExecutionTenantMissing)]
    [InlineData(GatewayAuthorityDenial.CapabilityTenantMismatch)]
    [InlineData(GatewayAuthorityDenial.GatewayIdentityTenantMismatch)]
    [InlineData(GatewayAuthorityDenial.GatewayNotEnrolled)]
    [InlineData(GatewayAuthorityDenial.GatewayRevoked)]
    [InlineData(GatewayAuthorityDenial.BindingMissing)]
    [InlineData(GatewayAuthorityDenial.BindingGatewayMismatch)]
    [InlineData(GatewayAuthorityDenial.ResourceNotOwnedByGateway)]
    [InlineData(GatewayAuthorityDenial.ResourceNotApproved)]
    [InlineData(GatewayAuthorityDenial.OperationNotPermitted)]
    [InlineData(GatewayAuthorityDenial.ActorGrantMissing)]
    [InlineData(GatewayAuthorityDenial.PolicyVersionMismatch)]
    public void Authority_FalsifiesEachClauseIndependently(GatewayAuthorityDenial expected)
    {
        // Each case breaks exactly one clause of an otherwise-valid request.
        var request = ValidRequest();
        var enrollment = ConsumedEnrollment();
        var binding = new GatewayResourceBinding(GatewayId, ResourceId);
        var resource = ApprovedResource();
        var grants = new[] { ValidGrant() };

        switch (expected)
        {
            case GatewayAuthorityDenial.ExecutionTenantMissing:
                request = request with { ExecutionTenantId = null }; break;
            case GatewayAuthorityDenial.CapabilityTenantMismatch:
                request = request with { CapabilityTenantId = OtherTenant }; break;
            case GatewayAuthorityDenial.GatewayIdentityTenantMismatch:
                request = request with { GatewayIdentityTenantId = OtherTenant }; break;
            case GatewayAuthorityDenial.GatewayNotEnrolled:
                enrollment = null; break;
            case GatewayAuthorityDenial.GatewayRevoked:
                enrollment = enrollment with { State = GatewayEnrollmentState.Revoked }; break;
            case GatewayAuthorityDenial.BindingMissing:
                binding = null; break;
            case GatewayAuthorityDenial.BindingGatewayMismatch:
                binding = new GatewayResourceBinding("other-gateway", ResourceId); break;
            case GatewayAuthorityDenial.ResourceNotOwnedByGateway:
                resource = null; break;
            case GatewayAuthorityDenial.ResourceNotApproved:
                resource = resource with { State = GatewayResourceState.Proposed }; break;
            case GatewayAuthorityDenial.OperationNotPermitted:
                resource = resource with { AllowedOperations = GatewayOperationClass.Write }; break;
            case GatewayAuthorityDenial.ActorGrantMissing:
                grants = []; break;
            case GatewayAuthorityDenial.PolicyVersionMismatch:
                request = request with { PolicyVersion = "v41" }; break;
        }

        var decision = GatewayAuthority.Evaluate(request, enrollment, binding, resource, grants, PolicyVersion);

        Assert.False(decision.Allowed);
        Assert.Equal(expected, decision.Denial);
    }

    [Fact]
    public void Authority_AGrantForAnotherTenantOrPrincipalDoesNotCount()
    {
        // A grant that exists but belongs to someone else must not satisfy the clause.
        Assert.False(Evaluate(grants: [ValidGrant() with { TenantId = OtherTenant }]).Allowed);
        Assert.False(Evaluate(grants: [ValidGrant() with { PrincipalName = "someone-else" }]).Allowed);
        Assert.False(Evaluate(grants: [ValidGrant() with { ResourceId = "other-resource" }]).Allowed);
        // A grant for a narrower operation class does not widen to the requested one.
        Assert.False(Evaluate(grants: [ValidGrant() with { Operations = GatewayOperationClass.Write }]).Allowed);
    }

    [Fact]
    public void Authority_KnowingAnotherTenantsIdentifiersIsNotEnough()
    {
        // The certification gate: cross-tenant requests fail even when the caller knows the alias,
        // gateway, resource, and operation of the other tenant.
        var request = ValidRequest() with { ExecutionTenantId = OtherTenant, CapabilityTenantId = OtherTenant, GatewayIdentityTenantId = OtherTenant };

        var decision = GatewayAuthority.Evaluate(
            request, ConsumedEnrollment(), new GatewayResourceBinding(GatewayId, ResourceId),
            ApprovedResource(), [ValidGrant()], PolicyVersion);

        Assert.False(decision.Allowed);
        Assert.Equal(GatewayAuthorityDenial.GatewayNotEnrolled, decision.Denial);
    }

    // ------------------------------------------------------------------------ D6: revocation

    [Fact]
    public async Task Revocation_StopsNewWorkImmediately()
    {
        // Revoking the Gateway invalidates authority on the very next evaluation; there is no
        // grace window in which a cached decision keeps working.
        var store = new InMemoryGatewayEnrollmentStore();
        await store.IssueAsync(Tenant, GatewayId, Token, DateTimeOffset.UtcNow.AddHours(1));
        await store.ConsumeAsync(Tenant, Token, "thumb");

        var live = await store.FindByGatewayAsync(Tenant, GatewayId);
        Assert.True(Evaluate(enrollment: live).Allowed);

        await store.RevokeAsync(Tenant, GatewayId);
        var afterRevoke = await store.FindByGatewayAsync(Tenant, GatewayId);

        var decision = Evaluate(enrollment: afterRevoke);
        Assert.False(decision.Allowed);
        Assert.Equal(GatewayAuthorityDenial.GatewayRevoked, decision.Denial);
    }

    [Fact]
    public async Task Revocation_OfAResourceStopsExecutionAndUnpublishesIt()
    {
        var registry = new GatewayResourceRegistry();
        await registry.ProposeAsync(SampleResource());
        await registry.ApproveAsync(ResourceId);
        Assert.Single(await registry.PublishAsync());

        await registry.DisableAsync(ResourceId);

        Assert.Empty(await registry.PublishAsync());
        await Assert.ThrowsAsync<GatewayResourceException>(
            () => registry.ResolveForExecutionAsync(ResourceId, GatewayOperationClass.Read));
        // A disabled resource cannot be quietly brought back by approving it again.
        await Assert.ThrowsAsync<GatewayResourceException>(() => registry.ApproveAsync(ResourceId));
    }

    // ------------------------------------------------------------------- D7: operator boundary

    [Fact]
    public void OperatorBoundary_PlatformOperatorGetsNoImplicitAuthority()
    {
        // A platform operator is not a tenant principal. With no grant naming it, every clause that
        // could have let it through is absent — the decision is denial, not "operator override".
        var decision = GatewayAuthority.Evaluate(
            ValidRequest() with { ActorPrincipal = "platform-operator" },
            ConsumedEnrollment(), new GatewayResourceBinding(GatewayId, ResourceId),
            ApprovedResource(), [ValidGrant()], PolicyVersion);

        Assert.False(decision.Allowed);
        Assert.Equal(GatewayAuthorityDenial.ActorGrantMissing, decision.Denial);
    }

    [Fact]
    public void OperatorBoundary_DenialReasonsNeverLeakTargetsOrCredentials()
    {
        // Refusals are read by people who may not be entitled to the resource's details, so every
        // distinct denial path — not one request evaluated repeatedly — must stay free of the
        // target and the credential reference.
        var decisions = new List<GatewayAuthorityDecision>
        {
            GatewayAuthority.Evaluate(ValidRequest() with { ExecutionTenantId = null },
                ConsumedEnrollment(), Binding(), ApprovedResource(), [ValidGrant()], PolicyVersion),
            GatewayAuthority.Evaluate(ValidRequest() with { CapabilityTenantId = OtherTenant },
                ConsumedEnrollment(), Binding(), ApprovedResource(), [ValidGrant()], PolicyVersion),
            GatewayAuthority.Evaluate(ValidRequest(),
                null, Binding(), ApprovedResource(), [ValidGrant()], PolicyVersion),
            GatewayAuthority.Evaluate(ValidRequest(),
                ConsumedEnrollment() with { State = GatewayEnrollmentState.Revoked },
                Binding(), ApprovedResource(), [ValidGrant()], PolicyVersion),
            GatewayAuthority.Evaluate(ValidRequest(),
                ConsumedEnrollment(), null, ApprovedResource(), [ValidGrant()], PolicyVersion),
            GatewayAuthority.Evaluate(ValidRequest(),
                ConsumedEnrollment(), Binding(), null, [ValidGrant()], PolicyVersion),
            GatewayAuthority.Evaluate(ValidRequest(),
                ConsumedEnrollment(), Binding(),
                ApprovedResource() with { State = GatewayResourceState.Proposed },
                [ValidGrant()], PolicyVersion),
            GatewayAuthority.Evaluate(ValidRequest(),
                ConsumedEnrollment(), Binding(), ApprovedResource(), [], PolicyVersion),
            GatewayAuthority.Evaluate(ValidRequest() with { PolicyVersion = "v41" },
                ConsumedEnrollment(), Binding(), ApprovedResource(), [ValidGrant()], PolicyVersion)
        };

        // The set must actually cover distinct denials, or this test proves nothing.
        Assert.True(decisions.Select(decision => decision.Denial).Distinct().Count() >= 8);
        Assert.All(decisions, decision =>
        {
            Assert.False(decision.Allowed);
            Assert.DoesNotContain("myserver", decision.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("1433", decision.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sales-etl-credential", decision.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    // ------------------------------------------------------------------------------- helpers

    private static GatewayAuthorityDecision Evaluate(
        GatewayEnrollment? enrollment = null,
        GatewayResourceGrant[]? grants = null) =>
        GatewayAuthority.Evaluate(
            ValidRequest(),
            enrollment ?? ConsumedEnrollment(),
            new GatewayResourceBinding(GatewayId, ResourceId),
            ApprovedResource(),
            grants ?? [ValidGrant()],
            PolicyVersion);

    private static GatewayResourceBinding Binding() => new(GatewayId, ResourceId);

    private static GatewayRoutingRequest ValidRequest() => new(
        Tenant, Tenant, Tenant, GatewayId, ResourceId, GatewayOperationClass.Read, Actor, PolicyVersion);

    private static GatewayEnrollment ConsumedEnrollment() => new(
        "enrol-1", Tenant, GatewayId, GatewayEnrollmentToken.Hash(Token),
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1),
        GatewayEnrollmentState.Consumed, DateTimeOffset.UtcNow.AddMinutes(-30), "thumb");

    private static GatewayPublishedResource ApprovedResource() => new(
        ResourceId, "SQLSERVER", GatewayOperationClass.Read | GatewayOperationClass.Write,
        new GatewayResourceLimits(), GatewayResourceState.Approved, "Sales");

    private static GatewayResourceGrant ValidGrant() => new(
        Tenant, GatewayId, ResourceId, Actor, GatewayOperationClass.Read | GatewayOperationClass.Write);

    private static GatewayResource SampleResource() => new(
        ResourceId, "SQLSERVER", "sqlserver://myserver:1433/Sales", "SECRET:sales-etl-credential",
        GatewayOperationClass.Read | GatewayOperationClass.Write, new GatewayResourceLimits());
}
