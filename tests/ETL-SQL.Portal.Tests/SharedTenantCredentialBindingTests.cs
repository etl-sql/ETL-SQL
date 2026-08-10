using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Middleware;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedTenantCredentialBindingTests
{
    private static readonly PortalUser User = new()
    {
        Id = 7,
        UserName = "shared-user",
        SecurityStamp = "stamp"
    };

    [Fact]
    public void SharedTokenStampsOnlyServerVerifiedTenantContext()
    {
        var config = SharedConfig();
        var tokens = new TokenService(config);

        Assert.Throws<UnauthorizedAccessException>(() => tokens.GenerateJwt(User, ["Viewer"]));

        var token = tokens.GenerateJwt(
            User,
            ["Viewer"],
            tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("tenant-alpha", jwt.Claims.Single(c => c.Type == TokenService.TenantClaim).Value);
    }

    [Fact]
    public void PlatformGrantCannotMintTenantUserOrServiceSession()
    {
        var now = DateTimeOffset.UtcNow;
        var grant = PlatformAccessGrant.Issue(
            "tenant-alpha", "operator@example.test", "approval-17", "support",
            now.AddMinutes(5), now);
        var platform = TenantContext.FromPlatformGrant(grant, now);
        var tokens = new TokenService(SharedConfig());

        Assert.Throws<UnauthorizedAccessException>(() =>
            tokens.GenerateJwt(User, ["Admin"], tenantContext: platform));
        Assert.Throws<UnauthorizedAccessException>(() =>
            tokens.GenerateServiceJwt(
                new ServiceAccount { Id = "svc-1", OwnerUserId = User.Id, Name = "svc" },
                ["Admin"], ["admin.identity"], tenantContext: platform));
    }

    [Fact]
    public void SharedCredentialRequiresExactlyOneCanonicalTenantClaim()
    {
        var config = SharedConfig();

        Assert.False(TenantCredentialBinding.TryResolve(
            Principal(), config, out _, out var missing));
        Assert.Contains("require a tenant claim", missing, StringComparison.OrdinalIgnoreCase);

        Assert.False(TenantCredentialBinding.TryResolve(
            Principal(new Claim(TokenService.TenantClaim, "tenant-alpha"),
                new Claim(TokenService.TenantClaim, "tenant-beta")),
            config, out _, out var duplicate));
        Assert.Contains("exactly one", duplicate, StringComparison.OrdinalIgnoreCase);

        Assert.False(TenantCredentialBinding.TryResolve(
            Principal(new Claim(TokenService.TenantClaim, "../tenant-alpha")),
            config, out _, out var malformed));
        Assert.Contains("malformed", malformed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MiddlewareIgnoresCallerTenantSelectorsAndUsesValidatedClaim()
    {
        var config = SharedConfig();
        var accessor = new RequestTenantContextAccessor(config);
        var http = new DefaultHttpContext();
        http.User = Principal(new Claim(TokenService.TenantClaim, "tenant-alpha"));
        http.Request.Headers["X-Tenant-Id"] = "tenant-beta";
        http.Request.QueryString = new QueryString("?tenant=tenant-beta&issuer=https://evil.test");
        var called = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http, config, accessor);

        Assert.True(called);
        Assert.Equal("tenant-alpha", accessor.RequireCurrent().Tenant.Value);
        Assert.Equal(TenantContextOrigin.VerifiedCredential, accessor.RequireCurrent().Origin);
    }

    [Fact]
    public async Task MiddlewareRejectsAuthenticatedSharedCredentialWithoutTenant()
    {
        var config = SharedConfig();
        var accessor = new RequestTenantContextAccessor(config);
        var http = new DefaultHttpContext { User = Principal() };
        var called = false;
        var middleware = new TenantContextMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http, config, accessor);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public void DedicatedHostRejectsMismatchedSignedTenantClaim()
    {
        var config = new PortalConfig { TenantId = "tenant-alpha" };

        Assert.False(TenantCredentialBinding.TryResolve(
            Principal(new Claim(TokenService.TenantClaim, "tenant-beta")),
            config, out _, out var error));
        Assert.Contains("does not match", error, StringComparison.OrdinalIgnoreCase);
    }

    private static PortalConfig SharedConfig() => new()
    {
        SharedTenancy = new SharedTenancyConfig { Enabled = true },
        Jwt = new JwtConfig
        {
            Secret = "shared-tenant-test-secret-that-is-at-least-32-bytes"
        }
    };

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "validated-test-jwt"));
}
