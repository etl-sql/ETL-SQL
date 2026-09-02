using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class FilterControlBehaviourTests(DetailSurfaceHarnessFixture fixture)
{
    private async Task<IPage> OpenAsync(BrowserSession session)
    {
        var page = session.Page;
        await page.GotoAsync($"{fixture.BaseUrl}/tools/ui-sandbox/filter-controls.html");
        await page.Locator("input[type='search']").WaitForAsync();
        return page;
    }

    [Fact]
    public async Task SearchClearButton_IsAccessibleAndResetsTheInput()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);
        var input = page.Locator("input[type='search']");
        var clear = page.GetByRole(AriaRole.Button, new() { Name = "Clear Customer search" });

        Assert.Equal("Quarterly sales", await input.InputValueAsync());
        Assert.True(await clear.IsVisibleAsync());

        await clear.ClickAsync();

        Assert.Equal("", await input.InputValueAsync());
        Assert.False(await clear.IsVisibleAsync());
        Assert.True(await input.EvaluateAsync<bool>("element => element === document.activeElement"));
    }

    [Fact]
    public async Task TextboxMaxLength_PreventsAdditionalTypedCharacters()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);
        var input = page.Locator("input[type='text']");

        Assert.Equal("12", await input.GetAttributeAsync("maxlength"));
        await input.PressSequentiallyAsync("1234567890123456");
        Assert.Equal("123456789012", await input.InputValueAsync());
    }
}
