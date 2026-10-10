using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(LineageCatalogCollection.Name)]
public sealed class LineageCatalogPageTests(LineageCatalogFixture fixture)
{
    [Fact]
    public async Task Lineage_RealHistoryProtectedStewardshipImpactAndExportUseServerContracts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var table = $"lineage_{Guid.NewGuid():N}";
        var entry = new LineageEntry(table, "SELECT")
        {
            TargetColumn = "customer_email",
            SourceTables = ["source.customers"],
            SourceColumns = ["email"],
            SourceFile = "loads/lineage-browser.etlsql",
            Line = 3,
        };
        entry.Metadata["pii"] = "true";
        entry.Metadata["classification"] = "restricted";
        entry.Metadata["steward"] = "Native steward";
        entry.Metadata["domain"] = "Browser evidence";
        var catalog = fixture.Factory.Services.GetRequiredService<ILineageCatalogStore>();
        await catalog.SaveLineageAsync([entry], $"job-{table}", "loads/lineage-browser.etlsql",
            new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc).AddTicks(5_000_000));

        await page.GotoAsync("/index.html#governance/lineage");
        await Expect(page.Locator("#lineageSearchForm")).ToBeVisibleAsync();
        await page.Locator("#lineageQuery").FillAsync(table);
        await page.Locator("#lineageFrom").FillAsync("2026-07-31");
        await page.Locator("#lineageTo").FillAsync("2026-07-31");
        var history = await page.RunAndWaitForResponseAsync(() => page.Locator("#lineageSearchForm button[type='submit']").ClickAsync(),
            response => response.Url.Contains("/api/catalog/lineage/table?", StringComparison.Ordinal));
        Assert.Equal(200, history.Status);
        Assert.Contains("23%3A59%3A59.9999999Z", history.Url, StringComparison.Ordinal);
        await Expect(page.Locator("#lineageResults .lineage-result-row")).ToHaveCountAsync(1);
        await Expect(page.Locator("#lineageResults")).ToContainTextAsync(table);
        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("#lineageExportBtn").ClickAsync());
        var csv = await File.ReadAllTextAsync((await download.PathAsync())!);
        Assert.Contains(table, csv, StringComparison.Ordinal);
        Assert.Contains("customer_email", csv, StringComparison.Ordinal);
        await page.Locator("#lineageViewToggle").ClickAsync();
        await Expect(page.Locator("#lineageDagContainer svg")).ToBeVisibleAsync();

        await page.Locator("[data-lineage-mode='protected']").ClickAsync();
        await Expect(page.Locator("#lineageResults .stewardship-metric").Filter(new() { HasText = "Protected observations" }).Locator("strong")).ToHaveTextAsync("1");
        await Expect(page.Locator("#lineageResults .stewardship-metric").Filter(new() { HasText = "Restricted" }).Locator("strong")).ToHaveTextAsync("1");
        await Expect(page.Locator("#lineageResults .stewardship-metric").Filter(new() { HasText = "Sensitive" }).Locator("strong")).ToHaveTextAsync("1");
        await page.Locator("#protectedSteward").SelectOptionAsync("Native steward");
        await page.Locator("#protectedSearchForm button[type='submit']").ClickAsync();
        await Expect(page.Locator("#lineageResults .steward-audit-row").First).ToContainTextAsync(table);

        await page.Locator("[data-lineage-mode='stewardship']").ClickAsync();
        await Expect(page.Locator("#stewardshipSteward option[value='Native steward']")).ToHaveCountAsync(1);
        await page.Locator("#stewardshipSteward").SelectOptionAsync("Native steward");
        await page.Locator("#stewardshipSearchForm button[type='submit']").ClickAsync();
        await Expect(page.Locator("#lineageResults .stewardship-row")).ToHaveCountAsync(1);
        await Expect(page.Locator("#lineageResults .stewardship-row")).ToContainTextAsync(table);

        await page.Locator("[data-lineage-mode='impact']").ClickAsync();
        await page.Locator("#impactName").FillAsync(table);
        await page.Locator("#impactSearchForm button[type='submit']").ClickAsync();
        await Expect(page.Locator("#lineageResults .impact-row").First).ToContainTextAsync(table);
        await page.Locator("[data-lineage-mode='audit']").ClickAsync();
        await Expect(page.Locator("#lineageResults .steward-audit-grid")).ToBeVisibleAsync();
        await Expect(page.Locator("#lineageResults .steward-audit-errors")).ToHaveCountAsync(0);
        await Expect(page.Locator("#lineageResults")).ToContainTextAsync(table);
        Assert.Empty(session.PageErrors);
    }
}

public sealed class LineageCatalogFixture() : PortalBrowserFixture(new PortalBrowserFactory());

[CollectionDefinition(Name)]
public sealed class LineageCatalogCollection : ICollectionFixture<LineageCatalogFixture>
{
    public const string Name = "Portal lineage catalog";
}
