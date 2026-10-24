using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(PortalAdminCollection.Name)]
public sealed class GatewayAdminPageTests(PortalAdminFixture fixture)
{
    [Fact]
    public async Task GatewayAdmin_RealEnrollmentRefreshAndRevocationClearOneTimeCredentials()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoWithDiagnosticsAsync("/admin.html");
        await page.Locator("[data-tab='gateways']").ClickAsync();
        await Expect(page.Locator("#panel-gateways")).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(page.Locator("#gw-status")).Not.ToHaveTextAsync("Gateway fleet unavailable");
        var id = $"browser-gateway-{Guid.NewGuid():N}";
        await page.Locator("#gw-input-id").FillAsync(id);
        await page.Locator("#gw-enrollSubmitBtn").ClickAsync();
        await Expect(page.Locator("#gw-enrollResult")).ToBeVisibleAsync();
        Assert.True((await page.Locator("#gw-result-token").InputValueAsync()).Length >= 32,
            "The one-time gateway enrollment credential was not delivered.");
        await page.Locator("#gw-enrollDoneBtn").ClickAsync();
        await Expect(page.Locator("#gw-result-token")).ToHaveValueAsync("");
        await Expect(page.Locator("#gw-result-cmd")).ToHaveValueAsync("");
        var row = page.Locator("#gw-tableWrap tbody tr").Filter(new() { HasText = id });
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync("Enrollment Pending");
        await row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke gateway", Exact = true }).ClickAsync();
        await Expect(row).ToContainTextAsync("Revoked");
        await Expect(page.Locator("#gw-action-status")).ToContainTextAsync("revoked.");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true })).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }
}
