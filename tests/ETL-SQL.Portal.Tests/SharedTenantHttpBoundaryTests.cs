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
            admin.TenantId = "tenant-alpha";
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

    [Fact]
    public async Task DesignerLeaseKeyCannotCrossTheSignedTenantBoundary()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string alphaToken;
        int betaReportId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin")
                ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.TenantId = "tenant-alpha";
            admin.MustChangePassword = false;

            var beta = new PortalUser
            {
                UserName = $"beta-{Guid.NewGuid():N}",
                Email = $"beta-{Guid.NewGuid():N}@test.local",
                TenantId = "tenant-beta",
                MustChangePassword = false,
                IsActive = true
            };
            Assert.True((await users.CreateAsync(beta, "Beta@Test99!")).Succeeded);
            var folder = new Folder { Name = "Beta", Path = "/beta", OwnerId = beta.Id };
            var report = new Report
            {
                Folder = folder,
                Name = "Beta only",
                ScriptPath = "beta/only.rptsql",
                CreatedBy = beta.Id
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            betaReportId = report.Id;

            alphaToken = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
                admin,
                await users.GetRolesAsync(admin),
                tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/designer/lease")
        {
            Content = JsonContent.Create(new { reportId = betaReportId })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var leased = await verifyScope.ServiceProvider.GetRequiredService<PortalDbContext>()
            .Reports.AsNoTracking().SingleAsync(report => report.Id == betaReportId);
        Assert.Null(leased.EditSessionUserId);
    }

    [Fact]
    public async Task SharedResourceNamespacesIgnoreCallerTenantSelectorsAndForeignIds()
    {
        using var factory = new SharedPortalFactory();
        using var client = factory.CreateClient();
        string alphaToken;
        long betaId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin")
                ?? throw new InvalidOperationException("Seeded admin was not found.");
            admin.TenantId = "tenant-alpha";
            admin.MustChangePassword = false;
            db.SharedTenantResources.Add(new SharedTenantResource
            {
                TenantId = "tenant-beta",
                Kind = "gateway",
                LogicalId = "equal-id",
                ScopedId = "tenant-beta/gateway/equal-id"
            });
            await db.SaveChangesAsync();
            betaId = await db.SharedTenantResources
                .Where(value => value.TenantId == "tenant-beta")
                .Select(value => value.Id)
                .SingleAsync();
            alphaToken = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(
                admin, await users.GetRolesAsync(admin),
                tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));
        }

        using var create = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/shared/resources/gateway?tenant=tenant-beta")
        {
            Content = JsonContent.Create(new { logicalId = "equal-id" })
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        create.Headers.Add("X-Tenant-Id", "tenant-beta");
        var created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<SharedTenantResourceDto>();
        Assert.Equal("tenant-alpha/gateway/equal-id", createdBody!.ScopedId);

        using var foreignNumeric = new HttpRequestMessage(
            HttpMethod.Get, $"/api/shared/resources/gateway/{betaId}?tenant=tenant-beta");
        foreignNumeric.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        foreignNumeric.Headers.Add("X-Tenant-Id", "tenant-beta");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignNumeric)).StatusCode);

        using var foreignScope = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/shared/resources/gateway/by-scope?scopedId=tenant-beta%2Fgateway%2Fequal-id");
        foreignScope.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignScope)).StatusCode);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/shared/resources/gateway");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", alphaToken);
        var listed = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var values = await listed.Content.ReadFromJsonAsync<List<SharedTenantResourceDto>>();
        Assert.Single(values!);
        Assert.Equal("tenant-alpha/gateway/equal-id", values![0].ScopedId);
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
