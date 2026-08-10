using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Tests.Multitenancy;

/// <summary>
/// SaaS isolation domain 2: platform and tenant identity are separate, and platform administration
/// cannot implicitly impersonate a tenant user (SaaSTenantIsolation.md §4, §7).
/// </summary>
public sealed class PlatformAccessGrantTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00Z");

    private static PlatformAccessGrant Grant(
        string tenant = "acme", DateTimeOffset? expires = null, DateTimeOffset? now = null) =>
        PlatformAccessGrant.Issue(tenant, "operator@platform.test", "support-ticket-42",
            "Investigating a failed nightly load at the tenant's request",
            expires ?? Now.AddHours(2), now ?? Now);

    [Fact]
    public void AGrantCarriesWhoWhyAndUnderWhatAuthorization()
    {
        var grant = Grant();

        Assert.Equal("acme", grant.Tenant.Value);
        Assert.Equal("operator@platform.test", grant.OperatorPrincipal);
        Assert.Equal("support-ticket-42", grant.AuthorizationReference);
        Assert.Contains("nightly load", grant.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "support-ticket-42", "reason")]
    [InlineData("operator@platform.test", "", "reason")]
    [InlineData("operator@platform.test", "support-ticket-42", "")]
    public void EveryAttributionFieldIsRequired(string principal, string authorization, string reason)
    {
        Assert.Throws<ArgumentException>(() => PlatformAccessGrant.Issue(
            "acme", principal, authorization, reason, Now.AddHours(1), Now));
    }

    [Fact]
    public void AGrantCannotBeOpenEnded()
    {
        var ex = Assert.Throws<ArgumentException>(() => PlatformAccessGrant.Issue(
            "acme", "operator@platform.test", "ticket", "reason", Now, Now));

        Assert.Contains("second tenant administrator", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiryIsCheckedWhenTheGrantIsUsedNotOnlyWhenItWasIssued()
    {
        // Valid when written, stale by the time someone acts on it.
        var grant = Grant(expires: Now.AddMinutes(30));

        Assert.NotNull(TenantContext.FromPlatformGrant(grant, Now));

        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => TenantContext.FromPlatformGrant(grant, Now.AddHours(1)));
        Assert.Contains("Expired authorization is not authorization", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGrantForOneTenantProducesScopeOverThatTenantOnly()
    {
        var context = TenantContext.FromPlatformGrant(Grant("acme"), Now);

        Assert.Equal("acme", context.Tenant.Value);
        Assert.Equal(TenantContextOrigin.PlatformAuthorization, context.Origin);
        // The operator holds a grant for acme; naming globex's row still fails.
        Assert.Throws<UnauthorizedAccessException>(() => context.RequireOwned("globex/run-1", "run id"));
    }

    [Fact]
    public void PlatformScopeIsDistinguishableFromATenantsOwnUsers()
    {
        var platform = TenantContext.FromPlatformGrant(Grant("acme"), Now);
        var tenantUser = TenantContext.FromVerifiedCredential("acme");

        // Same tenant, different authority. An audit record can tell a support operator apart from
        // the customer's own user, which is the whole point of separating them.
        Assert.Equal(platform.Tenant, tenantUser.Tenant);
        Assert.NotEqual(platform.Origin, tenantUser.Origin);
        Assert.NotNull(platform.Grant);
        Assert.Null(tenantUser.Grant);
    }

    [Fact]
    public void ThereIsNoWayForPlatformScopeToBecomeATenantUser()
    {
        // The type exposes no factory that takes a platform principal and yields a tenant-user
        // origin. Impersonation is unrepresentable rather than merely discouraged, and this pins it:
        // every public factory is accounted for.
        var factories = typeof(TenantContext)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(TenantContext))
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["FromHostConfiguration", "FromPlatformGrant", "FromVerifiedCredential"],
            factories);
    }

    [Fact]
    public void APlatformGrantDoesNotWidenToOtherTenantsHeldByTheSameOperator()
    {
        var acme = TenantContext.FromPlatformGrant(Grant("acme"), Now);
        var globex = TenantContext.FromPlatformGrant(Grant("globex"), Now);

        // Two grants, two scopes. Holding both must not produce a key that satisfies either.
        Assert.NotEqual(acme.ScopeKey("job/1"), globex.ScopeKey("job/1"));
        Assert.Throws<UnauthorizedAccessException>(() => acme.RequireOwned(globex.ScopeKey("job/1"), "job"));
    }
}
