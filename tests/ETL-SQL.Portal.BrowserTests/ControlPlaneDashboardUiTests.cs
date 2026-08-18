using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class ControlPlaneDashboardUiTests(PortalBrowserFixture fixture)
{
    [Fact]
    public async Task ControlPlaneDashboard_LoadsAndRendersZeroTrustSecurityBoundary()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{fixture.BaseUrl}/control-plane.html");

        // Assert platform header and security boundary banner
        await Expect(page.Locator(".cp-title")).ToContainTextAsync("SaaS Control Plane");
        await Expect(page.Locator(".cp-security-banner")).ToContainTextAsync("Zero-Trust Platform Boundary");
        await Expect(page.Locator(".cp-security-banner")).ToContainTextAsync("Platform Identity Isolation");

        // Assert KPI strip
        await Expect(page.Locator("#kpiStrip")).ToBeVisibleAsync();

        // Assert tabs exist and clicking switches panels
        await Expect(page.Locator(".cp-tab[data-tab='audit']")).ToBeVisibleAsync();
        await page.ClickAsync(".cp-tab[data-tab='audit']");
        await Expect(page.Locator("#panel-audit")).ToBeVisibleAsync();
        await Expect(page.Locator("#panel-tenants")).Not.ToBeVisibleAsync();

        await page.ClickAsync(".cp-tab[data-tab='fleet']");
        await Expect(page.Locator("#panel-fleet")).ToBeVisibleAsync();
        await Expect(page.Locator("#panel-fleet")).ToContainTextAsync("Hardened OCI Sandbox");

        Assert.Empty(session.PageErrors);
    }
}
