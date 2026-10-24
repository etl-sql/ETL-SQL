using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

public sealed class ControlPlaneBrowserFactory : PortalBrowserFactory
{
    // Synthetic authority for this isolated fixture. Never supplied by the page or tenant storage.
    internal const string ManagementKey = "fixture-platform-authority-at-least-thirty-two-characters";

    protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
    {
        base.CustomizeConfiguration(settings);
        settings["Portal:SharedTenancy:Enabled"] = "true";
        settings["Portal:SharedTenancy:LifecycleManagementKey"] = ManagementKey;
    }

    protected override void CustomizePortalConfig(PortalConfig config) =>
        config.SharedTenancy = new SharedTenancyConfig { Enabled = true, LifecycleManagementKey = ManagementKey };
}

public sealed class ControlPlaneFixture() : PortalBrowserFixture(new ControlPlaneBrowserFactory());

[CollectionDefinition(ControlPlaneCollection.Name)]
public sealed class ControlPlaneCollection : ICollectionFixture<ControlPlaneFixture>
{
    public const string Name = "control-plane-journeys";
}

[Trait("Category", "Browser")]
[Collection(ControlPlaneCollection.Name)]
public sealed class ControlPlaneDashboardJourneyTests(ControlPlaneFixture fixture)
{
    [Fact]
    public async Task PlatformAuthority_ProvisionQuotasAndState_ProduceAttributedReceipts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["X-Portal-Platform-Key"] = ControlPlaneBrowserFactory.ManagementKey
        });
        await page.GotoWithDiagnosticsAsync("/control-plane.html");
        await Expect(page.Locator("#btnOpenProvision")).ToBeEnabledAsync();
        var tenantId = "browser-cp-" + Guid.NewGuid().ToString("N")[..10];
        await page.Locator("#btnOpenProvision").ClickAsync();
        await page.GetByLabel("Tenant ID *", new() { Exact = true }).FillAsync(tenantId);
        await page.Locator("#provMaxJobs").FillAsync("3");
        await page.Locator("#provStorageMb").FillAsync("2048");
        await page.Locator("#provOperator").FillAsync("operator@platform.test");
        await page.Locator("#provAuthRef").FillAsync("CHG-browser-provision");
        await page.Locator("#provReason").FillAsync("Browser journey provisioning");
        await page.GetByRole(AriaRole.Button, new() { Name = "Provision", Exact = true }).ClickAsync();
        await Expect(page.Locator("#receiptAlert")).ToContainTextAsync("Provision — Completed");
        await Expect(page.Locator("#receiptAlert")).ToContainTextAsync("CHG-browser-provision");
        var row = page.Locator($"tr[data-tenant='{tenantId}']");
        await Expect(row).ToContainTextAsync("3 max");
        await row.Locator(".btn-quota").ClickAsync();
        await page.GetByLabel("Concurrent Jobs", new() { Exact = true }).FillAsync("5");
        await page.Locator("#quotaStorageMb").FillAsync("4096");
        await page.Locator("#quotaOperator").FillAsync("operator@platform.test");
        await page.Locator("#quotaAuthRef").FillAsync("CHG-browser-quotas");
        await page.Locator("#quotaReason").FillAsync("Increase capacity");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Quotas" }).ClickAsync();
        await Expect(row).ToContainTextAsync("5 max");
        await Expect(row).ToContainTextAsync("4096 MB");
        await row.Locator(".btn-state").ClickAsync();
        await page.GetByLabel("Target State").SelectOptionAsync("Maintenance");
        await page.Locator("#stateOperator").FillAsync("operator@platform.test");
        await page.Locator("#stateAuthRef").FillAsync("CHG-browser-state");
        await page.Locator("#stateReason").FillAsync("Scheduled maintenance");
        await page.GetByRole(AriaRole.Button, new() { Name = "Update State" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Maintenance");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Platform Audit Log" }).ClickAsync();
        await Expect(page.Locator("#auditTableBody")).ToContainTextAsync("CHG-browser-state");
        await Expect(page.Locator("#auditTableBody")).ToContainTextAsync("CHG-browser-quotas");
        await Expect(page.Locator("#auditTableBody")).ToContainTextAsync("CHG-browser-provision");
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Portal-Platform-Key", ControlPlaneBrowserFactory.ManagementKey);
        var inventory = await client.GetFromJsonAsync<ControlPlaneTenantDto[]>("/api/platform/control-plane/tenants");
        var tenant = Assert.Single(inventory!, value => value.TenantId == tenantId);
        Assert.Equal("Maintenance", tenant.State);
        Assert.Equal(5, tenant.MaxConcurrentJobs);
        Assert.Equal(4096, tenant.MaxStorageMb);
        Assert.Equal(50, tenant.MaxReportSessions);
        var receipts = await client.GetFromJsonAsync<PlatformAuditReceiptDto[]>("/api/platform/control-plane/audit")
            ?? throw new InvalidOperationException("Platform audit response missing.");
        Assert.Equal(3, receipts.Count(receipt => receipt.TenantId == tenantId));
        Assert.All(receipts.Where(receipt => receipt.TenantId == tenantId), receipt =>
        {
            Assert.Equal("Completed", receipt.Status);
            Assert.Equal("operator@platform.test", receipt.PlatformOperator);
            Assert.False(string.IsNullOrWhiteSpace(receipt.ReceiptHash));
        });
        Assert.Equal(0, await page.EvaluateAsync<int>("sessionStorage.length + localStorage.length"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task TenantJwt_DoesNotAuthorizePlatformMetadataOrMutations()
    {
        string token;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var admin = await users.FindByNameAsync("admin") ?? throw new InvalidOperationException("Seeded admin missing.");
            admin.TenantId = "tenant-cp-jwt";
            admin.MustChangePassword = false;
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.SharedTenantLifecycles.Add(new SharedTenantLifecycle
            {
                TenantId = admin.TenantId,
                State = "Active",
                ActiveRelease = "v0.21.0",
                MaxConcurrentJobs = 2,
                MaxStorageMb = 1024,
                MaxReportSessions = 3,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            token = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateJwt(admin,
                await users.GetRolesAsync(admin), tenantContext: TenantContext.FromVerifiedCredential(admin.TenantId));
        }
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var identity = await client.GetAsync("/api/admin/users?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        foreach (var path in new[] { "overview", "tenants", "audit" })
        {
            using var response = await client.GetAsync("/api/platform/control-plane/" + path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("Platform management credentials required", await response.Content.ReadAsStringAsync());
        }
        using var mutation = await client.PostAsJsonAsync("/api/platform/control-plane/tenants/tenant-cp-jwt/state",
            new UpdateTenantStateAdminRequest("Maintenance", "tenant-admin", "CHG-denied", "Denied action"));
        Assert.Equal(HttpStatusCode.Unauthorized, mutation.StatusCode);
        await using var session = await fixture.NewSessionAsync();
        await session.Page.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["Authorization"] = "Bearer " + token });
        await session.Page.GotoWithDiagnosticsAsync("/control-plane.html");
        await Expect(session.Page.Locator("#controlPlaneStatus")).ToContainTextAsync("Platform management credentials required.");
        await Expect(session.Page.Locator("#btnOpenProvision")).ToBeDisabledAsync();
        await Expect(session.Page.Locator("#kpiAuditOutbox")).ToHaveTextAsync("Unavailable");
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Portal-Platform-Key", ControlPlaneBrowserFactory.ManagementKey);
        var inventory = await client.GetFromJsonAsync<ControlPlaneTenantDto[]>("/api/platform/control-plane/tenants");
        Assert.Equal("Active", Assert.Single(inventory!, value => value.TenantId == "tenant-cp-jwt").State);
        Assert.Empty(session.PageErrors);
    }
}
