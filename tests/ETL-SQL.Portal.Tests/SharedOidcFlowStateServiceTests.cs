using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedOidcFlowStateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "shared_oidc_flow_" + Guid.NewGuid().ToString("N"));

    public SharedOidcFlowStateServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ProtectedFlowPinsRoutedAuthorityVersionAndRedirectUri()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        await RegisterAsync(db, config);
        var clock = new TestClock(new DateTimeOffset(2026, 8, 10, 14, 0, 0, TimeSpan.Zero));
        var service = NewService(db, config, clock);
        var request = Request("ALPHA.PORTAL.TEST");
        request.QueryString = new QueryString(
            "?tenant=tenant-beta&issuer=https%3A%2F%2Fidp.test%2Fbeta");

        var started = await service.BeginAsync(
            request,
            Authorization(),
            "https://alpha.portal.test/api/auth/oidc/callback");

        Assert.Equal("tenant-alpha", started.Authority.TenantId);
        Assert.DoesNotContain("tenant-alpha", started.ProtectedState, StringComparison.Ordinal);
        Assert.DoesNotContain("state-123", started.ProtectedState, StringComparison.Ordinal);

        // Resume has no HttpRequest parameter: a callback Host/query cannot reselect authority.
        var resumed = await service.ResumeAsync(started.ProtectedState, "state-123");
        Assert.Equal("tenant-alpha", resumed.Authority.TenantId);
        Assert.Equal("https://idp.test/alpha", resumed.Authority.Issuer);
        Assert.Equal("nonce-123", resumed.Nonce);
        Assert.Equal("verifier-123", resumed.CodeVerifier);
        Assert.Equal("https://alpha.portal.test/api/auth/oidc/callback", resumed.RedirectUri);
    }

    [Fact]
    public async Task TamperedEnvelopeAndMismatchedStateAreRejected()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        await RegisterAsync(db, config);
        var service = NewService(db, config, new TestClock(DateTimeOffset.UtcNow));
        var started = await service.BeginAsync(
            Request("alpha.portal.test"), Authorization(),
            "https://alpha.portal.test/api/auth/oidc/callback");

        await Assert.ThrowsAsync<OidcAuthenticationException>(() =>
            service.ResumeAsync(started.ProtectedState + "tampered", "state-123"));
        await Assert.ThrowsAsync<OidcAuthenticationException>(() =>
            service.ResumeAsync(started.ProtectedState, "state-from-another-flow"));
    }

    [Fact]
    public async Task AuthorityChangeOrDisableInvalidatesOutstandingFlow()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        var admin = await RegisterAsync(db, config);
        var service = NewService(db, config, new TestClock(DateTimeOffset.UtcNow));
        var changed = await service.BeginAsync(
            Request("alpha.portal.test"), Authorization(),
            "https://alpha.portal.test/api/auth/oidc/callback");

        await admin.SetAsync("primary", Definition(clientId: "rotated-client"));
        await Assert.ThrowsAsync<OidcAuthenticationException>(() =>
            service.ResumeAsync(changed.ProtectedState, "state-123"));

        var disabled = await service.BeginAsync(
            Request("alpha.portal.test"), Authorization(),
            "https://alpha.portal.test/api/auth/oidc/callback");
        await admin.DisableAsync("primary");
        await Assert.ThrowsAsync<OidcAuthenticationException>(() =>
            service.ResumeAsync(disabled.ProtectedState, "state-123"));
    }

    [Fact]
    public async Task ExpiredFlowAndRedirectHostMismatchAreRejected()
    {
        await using var db = await CreateDbAsync();
        var config = SharedConfig();
        await RegisterAsync(db, config);
        var clock = new TestClock(new DateTimeOffset(2026, 8, 10, 14, 0, 0, TimeSpan.Zero));
        var service = NewService(db, config, clock);

        await Assert.ThrowsAsync<OidcAuthenticationException>(() => service.BeginAsync(
            Request("alpha.portal.test"), Authorization(),
            "https://beta.portal.test/api/auth/oidc/callback"));

        var started = await service.BeginAsync(
            Request("alpha.portal.test"), Authorization(),
            "https://alpha.portal.test/api/auth/oidc/callback");
        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        await Assert.ThrowsAsync<OidcAuthenticationException>(() =>
            service.ResumeAsync(started.ProtectedState, "state-123"));
    }

    private SharedOidcFlowStateService NewService(
        PortalDbContext db, PortalConfig config, TimeProvider clock)
    {
        var protector = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_root, "keys")),
            options => options.SetApplicationName("shared-oidc-flow-tests"));
        return new SharedOidcFlowStateService(
            new SharedIdentityAuthorityResolver(db, config), protector, clock);
    }

    private static async Task<SharedIdentityAuthorityService> RegisterAsync(
        PortalDbContext db, PortalConfig config)
    {
        var admin = new SharedIdentityAuthorityService(
            db, config, TenantContext.FromVerifiedCredential("tenant-alpha"));
        await admin.SetAsync("primary", Definition());
        return admin;
    }

    private static SharedIdentityAuthorityDefinition Definition(string clientId = "alpha-client") => new(
        "alpha.portal.test", "alpha.example", "https://idp.test/alpha",
        clientId, "SECRET:alpha-oidc");

    private static OidcAuthorizationRequest Authorization() => new(
        "https://idp.test/alpha/authorize", "state-123", "nonce-123", "verifier-123");

    private static HttpRequest Request(string host)
    {
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString(host);
        return request;
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

    private sealed class TestClock(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
