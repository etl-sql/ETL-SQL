using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(PortalJourneyCollection.Name)]
public sealed class PortalDocumentationJourneyTests(PortalJourneyFixture fixture)
{
    [Fact]
    public async Task DocumentationShowsItsSearchSectionAndSignsOut()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoWithDiagnosticsAsync("/docs.html");
        await Expect(page.Locator("#document h1").First).ToBeVisibleAsync();

        var section = await page.Locator("#results .folder-item.active .docs-item-section").InnerTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(section));
        Assert.Equal(section, await page.Locator("#document > div").First.InnerTextAsync());

        await page.Locator("#logoutBtn").ClickAsync();
        await page.WaitForURLWithDiagnosticsAsync("**/login.html");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => sessionStorage.getItem('etlsql_token') === null
               && sessionStorage.getItem('etlsql_refresh') === null
            """));
        Assert.Empty(session.PageErrors);
    }
}
