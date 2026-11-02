using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Regressions for the 2026-11-01 Portal bug and security review, run against a Shared Tenancy
/// host: every request tenant must come from the verified credential, never from a selector.
/// </summary>
[Trait("Category", "Portal")]
public sealed class SharedPortalAuditRegressionTests
{
    private static readonly string AlphaMachine = Guid.NewGuid().ToString("N");
    private static readonly string BetaMachine = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task PolicyAuthority_SharedAdminCannotReadOrRevokeAnotherTenant()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        var token = await SeedAlphaAdminAsync(factory, async db =>
        {
            db.PolicyMachines.AddRange(
                new PolicyMachineEntity
                {
                    MachineId = AlphaMachine,
                    EnrollmentId = Guid.NewGuid().ToString("N"),
                    Tenant = "tenant-alpha",
                    Environment = "prod"
                },
                new PolicyMachineEntity
                {
                    MachineId = BetaMachine,
                    EnrollmentId = Guid.NewGuid().ToString("N"),
                    Tenant = "tenant-beta",
                    Environment = "prod"
                });
            await db.SaveChangesAsync();
        });

        using var active = await SendAsync(client, token, HttpMethod.Get,
            "/api/admin/policy-authority/active?tenant=tenant-beta&environment=prod");
        Assert.Equal(HttpStatusCode.Forbidden, active.StatusCode);

        using var machines = await SendAsync(client, token, HttpMethod.Get,
            "/api/admin/policy-authority/machines");
        var inventory = await machines.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, machines.StatusCode);
        Assert.Contains(AlphaMachine, inventory, StringComparison.Ordinal);
        Assert.DoesNotContain(BetaMachine, inventory, StringComparison.Ordinal);

        using var selected = await SendAsync(client, token, HttpMethod.Get,
            "/api/admin/policy-authority/machines?tenant=tenant-beta");
        Assert.Equal(HttpStatusCode.Forbidden, selected.StatusCode);

        using var revoke = await SendAsync(client, token, HttpMethod.Post,
            $"/api/admin/policy-authority/machines/{BetaMachine}/revoke", new { reason = "cross-tenant" });
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().PolicyMachines
            .AsNoTracking().SingleAsync(machine => machine.MachineId == BetaMachine);
        Assert.False(stored.Revoked);
    }

    private static async Task<string> SeedAlphaAdminAsync(
        PortalWebFactory factory, Func<PortalDbContext, Task>? seed = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var admin = await users.FindByNameAsync("admin")
            ?? throw new InvalidOperationException("Seeded admin was not found.");
        admin.TenantId = "tenant-alpha";
        admin.MustChangePassword = false;
        await db.SaveChangesAsync();
        if (seed is not null) await seed(db);
        return scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
            admin, await users.GetRolesAsync(admin),
            tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string? token, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed class SharedPortalFactory : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings) =>
            settings["Portal:SharedTenancy:Enabled"] = "true";

        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.SharedTenancy = new SharedTenancyConfig { Enabled = true };

        protected override void CustomizeServices(IServiceCollection services) =>
            services.AddScoped<TenantContext>(sp =>
                sp.GetRequiredService<RequestTenantContextAccessor>().RequireCurrent());
    }
}
