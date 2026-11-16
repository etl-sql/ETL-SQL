using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(PortalAdminCollection.Name)]
public sealed class OperationsAdminPageTests(PortalAdminFixture fixture)
{
    [Fact]
    public async Task Operations_RealResponsesCreateRotateAndRevokeMachineIdentity()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoWithDiagnosticsAsync("/admin.html");
        await page.Locator("[data-tab='operations']").ClickAsync();
        await Expect(page.Locator("#panel-operations")).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(page.Locator("#ops-signals")).ToContainTextAsync("Nothing pending");
        await Expect(page.Locator("#ops-error")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bshow\\b"));
        var name = $"browser-machine-{Guid.NewGuid():N}"[..30];
        await page.GetByRole(AriaRole.Button, new() { Name = "New service account", Exact = true }).ClickAsync();
        await page.Locator("#ops-account-name").FillAsync(name);
        await page.Locator("#ops-modal-submit").ClickAsync();
        await Expect(page.Locator("#ops-secret-value")).ToBeVisibleAsync();
        Assert.True((await page.Locator("#ops-secret-value").InnerTextAsync()).Length > 20,
            "The created credential was not delivered.");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#ops-secret-value")).ToHaveCountAsync(0);
        var row = page.Locator("#ops-accounts tbody tr").Filter(new() { HasText = name });
        await Expect(row).ToHaveCountAsync(1);
        await row.GetByRole(AriaRole.Button, new() { Name = "Rotate", Exact = true }).ClickAsync();
        await page.Locator("#ops-modal-submit").ClickAsync();
        await Expect(page.Locator("#ops-secret-value")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#ops-secret-value")).ToHaveCountAsync(0);
        await row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true }).ClickAsync();
        await page.Locator("#ops-modal-submit").ClickAsync();
        await Expect(row).ToContainTextAsync("Revoked");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Rotate", Exact = true })).ToHaveCountAsync(0);
        await row.GetByRole(AriaRole.Button, new() { Name = "Audit", Exact = true }).ClickAsync();
        await Expect(page.Locator("#ops-modal-body")).ToContainTextAsync("ROTATE_SERVICE_ACCOUNT");
        await Expect(page.Locator("#ops-modal-body")).ToContainTextAsync("REVOKE_SERVICE_ACCOUNT");
        Assert.Empty(session.PageErrors);
    }
}
