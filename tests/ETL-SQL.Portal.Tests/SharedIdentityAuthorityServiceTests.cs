using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedIdentityAuthorityServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "shared_identity_" + Guid.NewGuid().ToString("N"));

    public SharedIdentityAuthorityServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task TenantAdministrationAndHostDiscoveryRemainPartitioned()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        var alpha = new SharedIdentityAuthorityService(
            db, config, TenantContext.FromVerifiedCredential("tenant-alpha"));
        var beta = new SharedIdentityAuthorityService(
            db, config, TenantContext.FromVerifiedCredential("tenant-beta"));

        await alpha.SetAsync("primary", new(
            "alpha.portal.test", "alpha.example", "https://idp.test/alpha",
            "shared-client", "SECRET:alpha-oidc"));
        await beta.SetAsync("secondary", new(
            "beta.portal.test", "beta.example", "https://idp.test/beta",
            "shared-client", "SECRET:beta-oidc"));

        Assert.Equal("tenant-alpha", Assert.Single(await alpha.ListAsync()).TenantId);
        Assert.Equal("tenant-beta", Assert.Single(await beta.ListAsync()).TenantId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            beta.SetAsync("primary", new(
                "other.portal.test", "other.example", "https://idp.test/other",
                "other-client", null)));

        var request = new DefaultHttpContext().Request;
        request.Host = new HostString("ALPHA.PORTAL.TEST", 443);
        request.QueryString = new QueryString(
            "?tenant=tenant-beta&issuer=https%3A%2F%2Fidp.test%2Fbeta&domain=beta.example");
        var binding = await new SharedIdentityAuthorityResolver(db, config)
            .ResolveForRequestAsync(request);

        Assert.NotNull(binding);
        Assert.Equal("tenant-alpha", binding.TenantId);
        Assert.Equal("https://idp.test/alpha", binding.Issuer);
    }

    [Fact]
    public async Task ValidatedIssuerMustMatchServerRoutedAuthorityBeforeTenantContextExists()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        var service = new SharedIdentityAuthorityService(
            db, config, TenantContext.FromVerifiedCredential("tenant-alpha"));
        await service.SetAsync("primary", new(
            "alpha.portal.test", "alpha.example", "https://idp.test/alpha/",
            "alpha-client", null));
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString("alpha.portal.test");
        var resolver = new SharedIdentityAuthorityResolver(db, config);
        var binding = Assert.IsType<SharedIdentityAuthorityBinding>(
            await resolver.ResolveForRequestAsync(request));

        Assert.Throws<UnauthorizedAccessException>(() =>
            resolver.BindValidatedIssuer(binding, "https://idp.test/beta"));

        var context = resolver.BindValidatedIssuer(binding, "https://idp.test/alpha/");
        Assert.Equal("tenant-alpha", context.Tenant.Value);
        Assert.Equal(TenantContextOrigin.VerifiedCredential, context.Origin);
    }

    [Fact]
    public async Task DisabledOrUnknownHostDoesNotDiscoverAnAuthority()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        var service = new SharedIdentityAuthorityService(
            db, config, TenantContext.FromVerifiedCredential("tenant-alpha"));
        await service.SetAsync("primary", new(
            "alpha.portal.test", "alpha.example", "https://idp.test/alpha",
            "alpha-client", null));
        await service.DisableAsync("primary");
        var resolver = new SharedIdentityAuthorityResolver(db, config);
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString("alpha.portal.test");
        Assert.Null(await resolver.ResolveForRequestAsync(request));

        request.Host = new HostString("alpha.portal.test.evil.example");
        Assert.Null(await resolver.ResolveForRequestAsync(request));
    }

    [Fact]
    public async Task RawClientSecretAndCrossTenantHostCollisionAreRejected()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        var alpha = new SharedIdentityAuthorityService(
            db, config, TenantContext.FromVerifiedCredential("tenant-alpha"));
        var beta = new SharedIdentityAuthorityService(
            db, config, TenantContext.FromVerifiedCredential("tenant-beta"));

        await Assert.ThrowsAsync<ArgumentException>(() => alpha.SetAsync("raw-secret", new(
            "raw.portal.test", "raw.example", "https://idp.test/raw",
            "raw-client", "not-a-secret-reference")));

        await alpha.SetAsync("alpha", new(
            "shared.portal.test", "alpha.example", "https://idp.test/alpha",
            "alpha-client", null));
        await Assert.ThrowsAsync<DbUpdateException>(() => beta.SetAsync("beta", new(
            "SHARED.PORTAL.TEST", "beta.example", "https://idp.test/beta",
            "beta-client", null)));
    }

    [Fact]
    public async Task PlatformPrincipalCannotAdministerTenantAuthorityAsTenantUser()
    {
        await using var db = await CreateDbAsync();
        var now = DateTimeOffset.UtcNow;
        var grant = PlatformAccessGrant.Issue(
            "tenant-alpha", "operator@example.test", "approval-1", "support",
            now.AddMinutes(5), now);

        Assert.Throws<UnauthorizedAccessException>(() => new SharedIdentityAuthorityService(
            db, SharedConfig(), TenantContext.FromPlatformGrant(grant, now)));
    }

    private async Task<PortalDbContext> CreateDbAsync()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db")}")
            .Options;
        var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static PortalConfig SharedConfig() => new()
    {
        SharedTenancy = new SharedTenancyConfig { Enabled = true }
    };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
