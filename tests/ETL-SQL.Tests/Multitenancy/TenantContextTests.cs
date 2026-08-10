using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Tests.Multitenancy;

/// <summary>
/// SaaS isolation domain 1: tenant context is server-derived, and a caller-supplied identifier cannot
/// widen scope (SaaSTenantIsolation.md §6). Every other domain assumes this one holds.
/// </summary>
public sealed class TenantContextTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("a1b")]
    public void AValidTenantIdIsAcceptedInCanonicalForm(string value) =>
        Assert.Equal(value, TenantId.FromTrustedSource(value).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]                 // too short
    [InlineData("-acme")]              // must start alphanumeric
    [InlineData("acme-")]              // must end alphanumeric
    [InlineData("ACME")]               // canonical form is lowercase
    [InlineData("acme corp")]          // no spaces
    [InlineData("acme/../other")]      // no traversal
    [InlineData("acme/other")]         // no separators
    public void AMalformedTenantIdIsRefusedRatherThanBecomingAScopeThatMatchesNothing(string? value)
    {
        Assert.False(TenantId.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => TenantId.FromTrustedSource(value));
    }

    [Fact]
    public void EveryConstructionPathNamesAServerOwnedOrigin()
    {
        Assert.Equal(TenantContextOrigin.HostFixed,
            TenantContext.FromHostConfiguration("acme").Origin);
        Assert.Equal(TenantContextOrigin.VerifiedCredential,
            TenantContext.FromVerifiedCredential("acme").Origin);
        Assert.Equal(TenantContextOrigin.PlatformAuthorization,
            TenantContext.FromPlatformAuthorization("acme", "support-ticket-42").Origin);

        // There is no path that takes an unverified caller value: the type has no public constructor
        // and no parse-from-request factory. This asserts that stays true.
        Assert.Empty(typeof(TenantContext).GetConstructors());
    }

    [Fact]
    public void PlatformAccessToATenantMustNameItsAuthorization()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => TenantContext.FromPlatformAuthorization("acme", "  "));

        Assert.Contains("impersonation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACallerSuppliedIdentifierBelongingToAnotherTenantIsRefused()
    {
        var acme = TenantContext.FromHostConfiguration("acme");

        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => acme.RequireOwned("globex/run-1", "run id"));

        Assert.Contains("cannot widen scope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallerMayStillNameAResourceItOwns()
    {
        var acme = TenantContext.FromHostConfiguration("acme");

        Assert.Equal("acme/run-1", acme.RequireOwned("acme/run-1", "run id"));
    }

    [Fact]
    public void APrefixThatMerelyLooksLikeTheTenantDoesNotPass()
    {
        var acme = TenantContext.FromHostConfiguration("acme");

        // "acme-evil/..." starts with "acme" but is a different tenant. Scoping on the raw name
        // rather than the delimited prefix is the classic version of this bug.
        Assert.Throws<UnauthorizedAccessException>(() => acme.RequireOwned("acme-evil/run-1", "run id"));
    }

    [Fact]
    public void EqualLogicalIdsInDifferentTenantsDoNotCollide()
    {
        var acme = TenantContext.FromHostConfiguration("acme");
        var globex = TenantContext.FromHostConfiguration("globex");

        // The collision case the shared-store domains depend on: same name, same numeric id.
        Assert.NotEqual(acme.ScopeKey("job/1"), globex.ScopeKey("job/1"));
        Assert.NotEqual(acme.ScopeKey("nightly-load"), globex.ScopeKey("nightly-load"));
        Assert.Equal("acme/job/1", acme.ScopeKey("job/1"));
    }

    [Fact]
    public void ScopePrefixIsDelimitedSoOneTenantIsNotAPrefixOfAnother()
    {
        var acme = TenantContext.FromHostConfiguration("acme");
        var acmeEvil = TenantContext.FromHostConfiguration("acme-evil");

        Assert.Equal("acme/", acme.ScopePrefix);
        // The reason this is a property rather than ScopeKey(""): a range scan on the bare name
        // "acme" would also match every "acme-evil/..." key.
        Assert.False(acmeEvil.ScopePrefix.StartsWith(acme.ScopePrefix, StringComparison.Ordinal));
        Assert.StartsWith(acme.ScopePrefix, acme.ScopeKey("job/1"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyLogicalIdIsStillACallerBug()
    {
        var acme = TenantContext.FromHostConfiguration("acme");

        Assert.Throws<ArgumentException>(() => acme.ScopeKey(""));
        Assert.Throws<ArgumentException>(() => acme.ScopeKey("   "));
    }

    [Fact]
    public void AScopedKeyIsAcceptedBackByTheTenantThatMintedItAndNoOther()
    {
        var acme = TenantContext.FromHostConfiguration("acme");
        var globex = TenantContext.FromHostConfiguration("globex");
        var key = acme.ScopeKey("dataset/7");

        Assert.Equal(key, acme.RequireOwned(key, "dataset key"));
        Assert.Throws<UnauthorizedAccessException>(() => globex.RequireOwned(key, "dataset key"));
    }

    [Fact]
    public void TwoContextsForTheSameTenantCompareEqualRegardlessOfHowTheyWereDerived()
    {
        // Value equality on the id, so a context is safe to use as a dictionary key when partitioning
        // shared state. Origin is part of the record, so evidence can still distinguish them.
        Assert.Equal(
            TenantContext.FromHostConfiguration("acme").Tenant,
            TenantContext.FromVerifiedCredential("acme").Tenant);
        Assert.NotEqual(
            TenantContext.FromHostConfiguration("acme"),
            TenantContext.FromVerifiedCredential("acme"));
    }
}
