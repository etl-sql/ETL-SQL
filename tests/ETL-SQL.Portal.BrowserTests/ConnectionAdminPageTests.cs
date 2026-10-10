using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(PortalAdminCollection.Name)]
public sealed class ConnectionAdminPageTests(PortalAdminFixture fixture)
{
    [Fact]
    public async Task ConnectionAdmin_RealCatalogLifecycleDiagnosticsAndImportExportPreserveDefinitions()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/admin.html");
        await page.Locator("[data-tab='connections']").ClickAsync();
        await Expect(page.Locator("#panel-connections")).ToHaveAttributeAsync("aria-busy", "false");
        var alias = $"browser_connection_{Guid.NewGuid():N}";
        await page.Locator("#conn-alias").FillAsync(alias);
        await page.Locator("#conn-type").SelectOptionAsync("MOCKDB");
        await page.Locator("#conn-scope").FillAsync("Browser");
        await page.Locator("#conn-saveBtn").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync($"Shared connection '{alias}' saved.");
        var row = page.Locator($"#conn-tableWrap tr[data-alias='{alias}']");
        await row.GetByRole(AriaRole.Button, new() { Name = "Detail", Exact = true }).ClickAsync();
        await Expect(page.Locator("#conn-detailBody")).ToContainTextAsync("MOCKDB");
        await Expect(page.Locator("#conn-type")).ToHaveValueAsync("MOCKDB");
        await row.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
        await Expect(row.Locator(".conn-row-status")).ToContainTextAsync("Verified (0 secret ref(s))");
        await row.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true }).ClickAsync();
        await Expect(page.Locator("#conn-detailBody")).ToContainTextAsync("All attempted checks passed.");
        await Expect(page.Locator("#conn-detailBody")).ToContainTextAsync("SKIPPED");
        await row.GetByRole(AriaRole.Button, new() { Name = "Disable", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Disable connection", Exact = true }).ClickAsync();
        await Expect(row.Locator(".conn-row-status")).ToHaveTextAsync("Disabled");
        await row.GetByRole(AriaRole.Button, new() { Name = "Test", Exact = true }).ClickAsync();
        await Expect(page.Locator("#conn-detailBody")).ToHaveTextAsync("This connection is disabled — enable it before testing.");
        await row.GetByRole(AriaRole.Button, new() { Name = "Enable", Exact = true }).ClickAsync();
        await Expect(row.Locator(".conn-row-status")).ToHaveTextAsync("Active");
        var importedAlias = $"{alias}_imported";
        await page.Locator("#conn-importBtn").ClickAsync();
        await page.Locator("#conn-importJson").FillAsync(JsonSerializer.Serialize(new[] { new {
            alias = importedAlias, connectorType = "MOCKDB", target = (string?)null,
            options = new Dictionary<string, string>(), environmentScope = "Imported", disabled = true,
            sensitiveFields = Array.Empty<string>(), gateway = (object?)null
        } }));
        await page.Locator("#conn-importRunBtn").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync("Imported: 1 created, 0 updated.");
        await Expect(page.Locator($"tr[data-alias='{importedAlias}'] .conn-row-status")).ToHaveTextAsync("Disabled");
        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("#conn-exportBtn").ClickAsync());
        var exportPath = Path.Combine(RepoRoot(), "artifacts", "section1", "connections", "native-export.json");
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        await download.SaveAsAsync(exportPath);
        using var exported = JsonDocument.Parse(await File.ReadAllTextAsync(exportPath));
        var entry = exported.RootElement.EnumerateArray().Single(entry => entry.GetProperty("alias").GetString() == importedAlias);
        Assert.True(entry.GetProperty("disabled").GetBoolean());
        Assert.Equal("Imported", entry.GetProperty("environmentScope").GetString());
        Assert.Empty(entry.GetProperty("options").EnumerateObject());
        await row.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete connection", Exact = true }).ClickAsync();
        await Expect(row).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionAdmin_RealWizardParsingSavingAndUnknownProviderEditsUseCurrentContracts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/admin.html");
        await page.Locator("[data-tab='connections']").ClickAsync();
        await Expect(page.Locator("#panel-connections")).ToHaveAttributeAsync("aria-busy", "false");
        var webhook = $"browser_webhook_{Guid.NewGuid():N}";
        await page.EvaluateAsync("""
            async alias => {
              const { connectionsApi } = await import('/js/api.js');
              await connectionsApi.set(alias, { connectorType: 'WEBHOOK', target: null,
                options: { URL: 'SECRET:browser_missing_webhook' }, environmentScope: 'Original', sensitiveFields: [] });
            }
            """, webhook);
        await page.Locator("#conn-refreshBtn").ClickAsync();
        var row = page.Locator($"tr[data-alias='{webhook}']");
        await row.GetByRole(AriaRole.Button, new() { Name = "Detail", Exact = true }).ClickAsync();
        await Expect(page.Locator("#conn-type")).ToHaveValueAsync("WEBHOOK");
        await page.Locator("#conn-scope").FillAsync("Updated");
        await page.Locator("#conn-saveBtn").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync($"Shared connection '{webhook}' saved.");
        await Expect(row).ToContainTextAsync("WEBHOOK");
        await Expect(row).ToContainTextAsync("Updated");
        await row.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
        await Expect(page.Locator("#conn-error")).ToContainTextAsync("Connection verification failed");
        await Expect(row.Locator(".conn-row-status")).Not.ToContainTextAsync("Verified");
        await page.Locator("#conn-wizardBtn").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToBeVisibleAsync();
        await page.Locator("#etlsql-cw-paste-btn").ClickAsync();
        await page.Locator("#etlsql-cw-paste-input").FillAsync("Server=sql-fixture;Initial Catalog=BrowserFixture");
        await page.Locator("#etlsql-cw-paste-apply").ClickAsync();
        await Expect(page.Locator("#etlsql-cw-opt-server")).ToHaveValueAsync("sql-fixture");
        await Expect(page.Locator("#etlsql-cw-opt-database")).ToHaveValueAsync("BrowserFixture");
        await page.Locator("button[data-cat='testdata']").ClickAsync();
        await page.Locator("button[data-type='MOCKDB']").ClickAsync();
        var wizardAlias = $"browser_wizard_{Guid.NewGuid():N}";
        await page.Locator("#etlsql-cw-alias-input").FillAsync(wizardAlias);
        await page.Locator("#etlsql-cw-submit-btn").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Connection Wizard" })).ToHaveCountAsync(0);
        await Expect(page.Locator($"tr[data-alias='{wizardAlias}']")).ToContainTextAsync("MOCKDB");
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync($"Shared connection '{wizardAlias}' saved.");
        var unconfirmedAlias = $"browser_unconfirmed_{Guid.NewGuid():N}";
        var routePattern = $"**/api/admin/connections/{unconfirmedAlias}";
        await page.RouteAsync(routePattern, route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" }));
        await page.Locator("#conn-alias").FillAsync(unconfirmedAlias);
        await page.Locator("#conn-saveBtn").ClickAsync();
        await Expect(page.Locator("#conn-status")).ToHaveTextAsync("Connection outcome unconfirmed.");
        await Expect(page.Locator("#conn-saveBtn")).ToBeDisabledAsync();
        await page.UnrouteAsync(routePattern);
        await page.Locator("#conn-refreshBtn").ClickAsync();
        await Expect(page.Locator("#conn-saveBtn")).ToBeEnabledAsync();
        await Expect(page.Locator($"tr[data-alias='{unconfirmedAlias}']")).ToHaveCountAsync(0);
        Assert.Empty(session.PageErrors);
    }

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
