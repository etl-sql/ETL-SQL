using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Trait("CompatBreak", "0.21.0")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioFailedRecoveryJourneyTests(StudioAuthoringFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedEditorModuleKeepsTwoBuffersAndRecoversWithoutReload(bool desktop)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.RouteAsync("**/codemirror-bundle.min.js*", route => route.AbortAsync());
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(page, workspace, desktop);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.isEditorDegraded === true");
        var fallback = page.GetByRole(AriaRole.Textbox, new() { Name = "Script editor (basic mode)" });
        const string first = "-- first kept buffer\nSELECT 11 AS First;";
        const string second = "-- second kept buffer\nSELECT 22 AS Second;";
        var firstId = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.activeDocId");
        await fallback.FillAsync(first);
        await Expect(page.Locator("[data-action='code-format']")).ToBeDisabledAsync();
        await page.EvaluateAsync("() => window.__STUDIO__.surgicalPatchVisualOption('T', 'title', 'Must not write')");
        await Expect(page.Locator(".etlsql-feedback-toast", new() { HasText = "Visual and helper edits are unavailable" })).ToBeVisibleAsync();
        await Expect(fallback).ToHaveValueAsync(first);
        await page.Locator("[data-activity='engine']").ClickAsync();
        await Expect(page.Locator("[data-sidebar-content]")).ToContainTextAsync("Cursor planning is unavailable");
        Assert.Equal(0, await page.Locator("[data-explain-statement]").CountAsync());
        await page.Locator("[data-studio-new-tab]").ClickAsync();
        await page.Locator("[data-new-type='dashboard']").ClickAsync();
        if (!desktop)
        {
            await page.Locator("[data-catalog-report-name]").FillAsync("Recovery second");
            await page.Locator("[data-catalog-create-confirm]").ClickAsync();
        }
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents.length === 2 && window.__STUDIO__.state.activeDocId === window.__STUDIO__.state.documents[1].id");
        await page.Locator("[data-projection='split']").ClickAsync();
        await fallback.FillAsync(second);
        var secondId = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.activeDocId");
        await page.EvaluateAsync("id => window.__STUDIO__.switchDoc(id)", firstId);
        await Expect(fallback).ToHaveValueAsync(first);
        // A second failed attempt must leave the existing textarea and its text usable.
        await page.Locator("[data-action='retry-cm']").ClickAsync();
        await Expect(page.Locator("[data-action='retry-cm']")).ToBeEnabledAsync();
        await Expect(fallback).ToHaveValueAsync(first);
        await page.UnrouteAsync("**/codemirror-bundle.min.js*");
        var loading = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/codemirror-bundle.min.js*", route => { loading.TrySetResult(route); return Task.CompletedTask; });
        await page.Locator("[data-action='retry-cm']").ClickAsync();
        var pendingModule = await loading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var firstLatest = first + "\n-- typed while retry was loading";
        var secondLatest = second + "\n-- switched and typed while retry was loading";
        await fallback.FillAsync(firstLatest);
        await page.EvaluateAsync("id => window.__STUDIO__.switchDoc(id)", secondId);
        await fallback.FillAsync(secondLatest);
        await pendingModule.ContinueAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.isEditorDegraded === false");
        Assert.Equal(secondLatest, await TextAsync(page));
        await Expect(page.Locator("[data-action='code-format']")).ToBeEnabledAsync();
        await page.EvaluateAsync("id => window.__STUDIO__.switchDoc(id)", firstId);
        Assert.Equal(firstLatest, await TextAsync(page));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents.every(d => d.isDirty)"));
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("http500")]
    [InlineData("disconnect")]
    public async Task PortalStartupFailureIsBoundedAndRetryUsesRealServerCapabilities(string failure)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.RouteAsync("**/api/studio/session", async route =>
        {
            if (failure == "http500") await route.FulfillAsync(new() { Status = 500, ContentType = "application/json", Body = "{\"error\":\"Controlled session outage\"}" });
            else if (failure == "disconnect") await route.AbortAsync();
            // Timeout: leave the actual browser request unanswered until its AbortSignal expires.
        });
        await page.GotoAsync("/studio.html");
        await Expect(page.Locator("#retrySessionBtn")).ToBeVisibleAsync(new() { Timeout = 22_000 });
        if (failure == "timeout") await Expect(page.Locator(".etlsql-studio-error-stage")).ToContainTextAsync("15 seconds");
        Assert.Null(await page.EvaluateAsync<object?>("() => window.__STUDIO__ || null"));
        Assert.Equal(0, await page.Locator("[data-action='run']").CountAsync());
        await page.UnrouteAsync("**/api/studio/session");
        await page.Locator("#retrySessionBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)");
        var expected = await page.EvaluateAsync<string[]>("async () => { const {studioApi} = await import('/js/api.js'); return (await studioApi.session()).capabilities.sort(); }");
        var actual = await page.EvaluateAsync<string[]>("() => [...window.__STUDIO__.state.capabilities].sort()");
        Assert.Equal(expected, actual);
        Assert.Empty(session.PageErrors);
    }

    private static Task<string> TextAsync(IPage page) => page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupAccessExpiryAndFailedRefreshRecoverTheAuthenticatedUsersKeptDraft(bool failRefresh)
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(page, workspace, false);
        var reportId = await page.EvaluateAsync<int>("() => window.__STUDIO__.state.documents[0].reportId");
        const string edit = "-- kept across startup authentication failure\nSELECT 41 AS Retained;";
        var kept = page.WaitForResponseAsync(r => r.Url.EndsWith($"/api/studio/drafts/{reportId}", StringComparison.Ordinal) && r.Request.Method == "PUT" && r.Status == 204);
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, edit);
        await kept;
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var user = await db.Users.SingleAsync(u => u.UserName == PortalBrowserFixture.AdminUsername);
        var config = scope.ServiceProvider.GetRequiredService<ETL_SQL.Portal.PortalConfig>();
        var expired = new TokenService(new ETL_SQL.Portal.PortalConfig
        {
            Jwt = new ETL_SQL.Portal.JwtConfig { Secret = config.Jwt.Secret, ExpiryMinutes = -60 }
        }).GenerateJwt(user, ["Admin"]);
        // Expire only when the new document starts. The old editor may still have authenticated
        // background reads, and rotating its refresh token during navigation is a different case.
        await page.EvaluateAsync("token => sessionStorage.setItem('section8_startup_token', token)", expired);
        await page.AddInitScriptAsync("""
            const expiredAtStartup = sessionStorage.getItem('section8_startup_token');
            if (expiredAtStartup && location.pathname === '/studio.html') {
                sessionStorage.setItem('etlsql_token', expiredAtStartup);
                sessionStorage.removeItem('section8_startup_token');
            }
            """);
        if (failRefresh)
            await page.RouteAsync("**/api/auth/refresh", route => route.FulfillAsync(new() { Status = 401, ContentType = "application/json", Body = "{\"error\":\"Expired refresh session\"}" }));
        await page.GotoAsync($"/studio.html?reportId={reportId}");
        if (failRefresh)
        {
            await page.WaitForURLAsync("**/login.html");
            Assert.Null(await page.EvaluateAsync<object?>("() => sessionStorage.getItem('etlsql_token')"));
            await page.UnrouteAsync("**/api/auth/refresh");
            await fixture.SignInAsync(page);
            // The existing startup retry action must succeed after re-authentication, too.
            await page.RouteAsync("**/api/studio/session", route => route.FulfillAsync(new() { Status = 500, ContentType = "application/json", Body = "{\"error\":\"Retry after sign-in\"}" }));
            await page.GotoAsync($"/studio.html?reportId={reportId}");
            await Expect(page.Locator("#retrySessionBtn")).ToBeVisibleAsync();
            await page.UnrouteAsync("**/api/studio/session");
            await page.Locator("#retrySessionBtn").ClickAsync();
        }
        try { await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__?.state.documents[0]?.reportId)", null, new() { Timeout = 15_000 }); }
        catch (TimeoutException error)
        {
            var body = await page.Locator("body").InnerTextAsync();
            throw new TimeoutException($"Startup did not reopen the report. URL: {page.Url}. Page: {body[..Math.Min(800, body.Length)]}", error);
        }
        await Expect(page.Locator("#topbarUser")).ToHaveTextAsync(PortalBrowserFixture.AdminUsername);
        var expected = await page.EvaluateAsync<string[]>("async () => { const {studioApi} = await import('/js/api.js'); return (await studioApi.session()).capabilities.sort(); }");
        Assert.Equal(expected, await page.EvaluateAsync<string[]>("() => [...window.__STUDIO__.state.capabilities].sort()"));
        var offer = page.Locator(".etlsql-feedback-toast", new() { HasText = "from a previous session were kept" });
        await Expect(offer).ToBeVisibleAsync();
        await offer.Locator(".etlsql-feedback-action", new() { HasText = "Restore" }).ClickAsync();
        Assert.Equal(edit, (await TextAsync(page)).TrimEnd());
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));
        Assert.Empty(session.PageErrors);
    }
}
