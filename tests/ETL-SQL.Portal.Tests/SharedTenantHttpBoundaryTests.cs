using System.Net;
using System.Net.Http.Headers;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedTenantHttpBoundaryTests
{
    [Fact]
    public async Task SignedTenantClaimScopesStoreDespiteCallerTenantSelectors()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string token;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>()
                .FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.MustChangePassword = false;
            db.PortalSecrets.AddRange(
                new PortalSecret { TenantId = "tenant-alpha", Name = "alpha-visible", EncryptedValue = "unused" },
                new PortalSecret { TenantId = "tenant-beta", Name = "beta-hidden", EncryptedValue = "unused" });
            await db.SaveChangesAsync();

            token = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
                admin,
                await scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>().GetRolesAsync(admin),
                tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/admin/secrets?tenant=tenant-beta&issuer=https%3A%2F%2Fevil.test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Tenant-Id", "tenant-beta");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("alpha-visible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("beta-hidden", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedPortalTokenWithoutTenantClaimIsRejectedBeforeControllerActivation()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string token;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var admin = await scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>()
                .FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin was not found.");
            var sharedConfig = scope.ServiceProvider.GetRequiredService<PortalConfig>();
            var legacyConfig = new PortalConfig
            {
                Jwt = sharedConfig.Jwt,
                SharedTenancy = new SharedTenancyConfig { Enabled = false }
            };
            token = new TokenService(legacyConfig).GenerateJwt(
                admin,
                await scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>().GetRolesAsync(admin));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/secrets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_tenant_credential", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
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
