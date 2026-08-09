using System.Security.Claims;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Middleware;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Service accounts were categorically barred from every <c>/api/admin</c> route. Opening identity
/// administration to automation carves a hole in a deliberate deny, so the negative cases are the
/// deliverable: the positive ones only prove the hole exists, not that it is the right size.
/// </summary>
public sealed class AdminIdentityScopeTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "etlsql_adminscope_" + Guid.NewGuid().ToString("N")[..8]);

    public AdminIdentityScopeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ── Route allowlist ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("GET", "/api/admin/users/42")]
    [InlineData("PUT", "/api/admin/users/42")]
    [InlineData("DELETE", "/api/admin/users/42")]
    [InlineData("POST", "/api/admin/users/bulk-status")]
    [InlineData("POST", "/api/admin/users/42/reset-password")]
    [InlineData("POST", "/api/admin/users/42/revoke-tokens")]
    [InlineData("POST", "/api/admin/users/42/disconnect")]
    [InlineData("GET", "/api/admin/sessions")]
    [InlineData("GET", "/api/admin/groups")]
    [InlineData("POST", "/api/admin/groups")]
    [InlineData("DELETE", "/api/admin/groups/7")]
    [InlineData("GET", "/api/admin/groups/7/members")]
    [InlineData("POST", "/api/admin/groups/7/members/bulk-add")]
    [InlineData("DELETE", "/api/admin/groups/7/members/42")]
    [InlineData("PUT", "/api/admin/groups/7/studio-capabilities")]
    [InlineData("GET", "/api/admin/permissions/effective/user/42")]
    [InlineData("GET", "/api/admin/access-simulator/user/42")]
    public void IdentityRoutesAreOnTheAllowlist(string method, string path) =>
        Assert.True(AdminIdentityRoutes.IsIdentityRoute(path, method));

    /// <summary>
    /// The capabilities the narrow scope deliberately withholds. If any of these ever became
    /// reachable, "identity administration only" would stop being true.
    /// </summary>
    [Theory]
    [InlineData("POST", "/api/admin/datasets/rotate-at-rest-key")]
    [InlineData("GET", "/api/admin/support-bundle")]
    [InlineData("GET", "/api/admin/configuration/export")]
    [InlineData("GET", "/api/admin/audit")]
    [InlineData("GET", "/api/admin/audit/export/csv")]
    [InlineData("POST", "/api/admin/service/restart")]
    [InlineData("POST", "/api/admin/service/shutdown")]
    [InlineData("PUT", "/api/admin/settings/orchestrator")]
    [InlineData("PUT", "/api/admin/settings/branding")]
    [InlineData("GET", "/api/admin/environments/plan")]
    [InlineData("POST", "/api/admin/environments/validate")]
    [InlineData("GET", "/api/admin/metrics/usage")]
    [InlineData("GET", "/api/admin/reports")]
    [InlineData("GET", "/api/admin/identity/diagnostics")]
    [InlineData("GET", "/api/admin/credentials/posture")]
    public void NonIdentityAdminRoutesStayOffTheAllowlist(string method, string path) =>
        Assert.False(AdminIdentityRoutes.IsIdentityRoute(path, method));

    /// <summary>
    /// Default-deny must survive the next person to add an admin endpoint. A prefix rule would have
    /// admitted these; an enumerated allowlist does not.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/admin/users/42/favorites")]
    [InlineData("POST", "/api/admin/users/42/favorites/9")]
    [InlineData("GET", "/api/admin/groups/7/some-future-endpoint")]
    [InlineData("GET", "/api/admin/users/42/something-added-later")]
    [InlineData("GET", "/api/admin/permissions/effective/folder/3")]
    [InlineData("GET", "/api/admin/permissions/effective/report/3")]
    public void UnenumeratedRoutesUnderAnAllowedPrefixAreStillDenied(string method, string path) =>
        Assert.False(AdminIdentityRoutes.IsIdentityRoute(path, method));

    /// <summary>The method is part of the grant: a readable route is not therefore writable.</summary>
    [Theory]
    [InlineData("DELETE", "/api/admin/sessions")]
    [InlineData("POST", "/api/admin/permissions/effective/user/42")]
    [InlineData("PUT", "/api/admin/users")]
    public void MethodIsPartOfTheGrant(string method, string path) =>
        Assert.False(AdminIdentityRoutes.IsIdentityRoute(path, method));

    // ── Middleware behaviour ─────────────────────────────────────────────────────

    [Fact]
    public async Task IdentityScopeReachesAnIdentityRouteWhenTheOwnerIsStillAnAdmin()
    {
        await using var db = await NewDbAsync(ownerIsAdmin: true);
        var context = Context("GET", "/api/admin/users", db,
            scopes: [ServiceAccountScopes.AdminIdentity], roles: ["Admin"]);

        var called = false;
        await new ServiceAccountScopeMiddleware(_ => { called = true; return Task.CompletedTask; })
            .InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task TokenWithoutTheScopeIsDeniedOnAnIdentityRoute()
    {
        await using var db = await NewDbAsync(ownerIsAdmin: true);
        var context = Context("GET", "/api/admin/users", db,
            scopes: [ServiceAccountScopes.PortalRead], roles: ["Admin"]);

        await new ServiceAccountScopeMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task IdentityScopeDoesNotReachANonIdentityAdminRoute()
    {
        await using var db = await NewDbAsync(ownerIsAdmin: true);
        var context = Context("POST", "/api/admin/service/shutdown", db,
            scopes: [ServiceAccountScopes.AdminIdentity], roles: ["Admin"]);

        await new ServiceAccountScopeMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    /// <summary>A scope must never substitute for the role.</summary>
    [Fact]
    public async Task ScopeWithoutTheAdminRoleIsDenied()
    {
        await using var db = await NewDbAsync(ownerIsAdmin: false);
        var context = Context("GET", "/api/admin/users", db,
            scopes: [ServiceAccountScopes.AdminIdentity], roles: ["Viewer"]);

        await new ServiceAccountScopeMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    /// <summary>And holding the role must not imply the scope.</summary>
    [Fact]
    public async Task AdminRoleWithoutTheScopeIsDenied()
    {
        await using var db = await NewDbAsync(ownerIsAdmin: true);
        var context = Context("GET", "/api/admin/users", db, scopes: [], roles: ["Admin"]);

        await new ServiceAccountScopeMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    /// <summary>
    /// The role claim is stamped at issue and a service token lives up to 15 minutes. Without a
    /// live check, demoting an administrator would leave their service token able to create users
    /// for the rest of that window — on the one route family that can grant access.
    /// </summary>
    [Fact]
    public async Task OwnerDemotedAfterIssueLosesAccessImmediately()
    {
        await using var db = await NewDbAsync(ownerIsAdmin: false);
        // The token still carries Admin from before the demotion.
        var context = Context("GET", "/api/admin/users", db,
            scopes: [ServiceAccountScopes.AdminIdentity], roles: ["Admin"]);

        await new ServiceAccountScopeMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    /// <summary>Interactive administrators are unaffected by any of this.</summary>
    [Fact]
    public async Task HumanAdminIsNotSubjectToScopeChecks()
    {
        await using var db = await NewDbAsync(ownerIsAdmin: true);
        var context = new DefaultHttpContext { RequestServices = Services(db) };
        context.Request.Method = "POST";
        context.Request.Path = "/api/admin/service/shutdown";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.NameIdentifier, "1")], "test"));

        var called = false;
        await new ServiceAccountScopeMiddleware(_ => { called = true; return Task.CompletedTask; })
            .InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public void AdminIdentityIsGrantableButThereIsNoBlanketAdminScope()
    {
        Assert.Contains(ServiceAccountScopes.AdminIdentity, ServiceAccountScopes.Allowed);
        Assert.DoesNotContain(ServiceAccountScopes.Allowed, scope =>
            scope.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("admin.*", StringComparison.OrdinalIgnoreCase));
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    private async Task<PortalDbContext> NewDbAsync(bool ownerIsAdmin)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, Guid.NewGuid().ToString("N")[..8] + ".db")}")
            .Options;
        var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Roles.Add(new PortalRole { Id = 1, Name = "Admin", NormalizedName = "ADMIN" });
        db.Roles.Add(new PortalRole { Id = 2, Name = "Viewer", NormalizedName = "VIEWER" });
        db.Users.Add(new PortalUser
        {
            Id = 1,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            IsActive = true
        });
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<int>
        {
            UserId = 1,
            RoleId = ownerIsAdmin ? 1 : 2
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static IServiceProvider Services(PortalDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext Context(
        string method, string path, PortalDbContext db, string[] scopes, string[] roles)
    {
        var claims = new List<Claim>
        {
            new(TokenService.IdentityTypeClaim, TokenService.ServiceIdentityType),
            new(ClaimTypes.NameIdentifier, "1")
        };
        claims.AddRange(scopes.Select(scope => new Claim(TokenService.ScopeClaim, scope)));
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var context = new DefaultHttpContext { RequestServices = Services(db) };
        context.Request.Method = method;
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        context.Response.Body = new MemoryStream();
        return context;
    }
}
