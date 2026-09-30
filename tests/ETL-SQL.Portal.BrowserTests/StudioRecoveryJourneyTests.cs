using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Unsaved Studio work surviving the three ways a session ends early: the browser dies, the access
/// token expires, or someone changes the report before the author comes back. Drafts are kept on the
/// Portal, so each test ends by reading what the author sees, not what the table holds.
/// </summary>
[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioRecoveryJourneyTests(StudioAuthoringFixture fixture)
{
    private const string Edit = "-- unsaved edit that must survive";

    [Fact]
    public async Task AnEditLostToACrashIsOfferedBackWhenTheReportIsReopened()
    {
        int reportId;
        await using (var crashed = await fixture.NewSessionAsync())
        {
            reportId = await OpenStudioAsync(crashed.Page);
            await TypeAndWaitForDraftAsync(crashed.Page, reportId);
            // Disposing the context closes the page without beforeunload: nothing gets a last chance.
        }

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await OpenReportAsync(page, reportId);

        var offer = page.Locator(".etlsql-feedback-toast", new() { HasText = "from a previous session were kept" });
        await Expect(offer).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(offer).Not.ToContainTextAsync("changed since");
        await offer.Locator(".etlsql-feedback-action", new() { HasText = "Restore" }).ClickAsync();
        await page.WaitForFunctionAsync("edit => window.__STUDIO__.state.editorInstance.getValue().includes(edit)", Edit);
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AnExpiredAccessTokenIsRefreshedWithoutLeavingStudio()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var reportId = await OpenStudioAsync(page);

        // An access token the Portal no longer accepts; the refresh token is still good.
        await page.EvaluateAsync("() => sessionStorage.setItem('etlsql_token', 'expired.access.token')");
        await TypeAndWaitForDraftAsync(page, reportId);

        Assert.Contains("/studio.html", page.Url, StringComparison.Ordinal);
        Assert.NotEqual("expired.access.token", await page.EvaluateAsync<string>("() => sessionStorage.getItem('etlsql_token')"));
        Assert.Equal(Edit, await DraftContentAsync(reportId));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ADraftOverAReportThatChangedSaysSoBeforeRestoring()
    {
        int reportId;
        await using (var crashed = await fixture.NewSessionAsync())
        {
            reportId = await OpenStudioAsync(crashed.Page);
            await TypeAndWaitForDraftAsync(crashed.Page, reportId);
        }

        // Someone saves a newer version while the author is away.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(item => item.Id == reportId);
            report.Version += 1;
            await db.SaveChangesAsync();
        }

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await OpenReportAsync(page, reportId);

        var offer = page.Locator(".etlsql-feedback-toast", new() { HasText = "from a previous session were kept" });
        await Expect(offer).ToContainTextAsync("changed since", new() { Timeout = 15_000 });
        Assert.Empty(session.PageErrors);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<int> OpenStudioAsync(IPage page)
    {
        await fixture.SignInAsync(page);
        var reportId = await CreateReportAsync(page);
        await OpenReportAsync(page, reportId);
        return reportId;
    }

    private static async Task OpenReportAsync(IPage page, int reportId)
    {
        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await page.WaitForFunctionAsync(
            "id => (window.__STUDIO__?.state?.documents || []).some(d => String(d.reportId) === String(id)) && window.__STUDIO__.state.editorInstance",
            reportId, new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    /// <summary>Types the edit and waits for the Portal to accept it as a draft.</summary>
    private static async Task TypeAndWaitForDraftAsync(IPage page, int reportId)
    {
        var kept = page.WaitForResponseAsync(response =>
            response.Url.EndsWith($"/api/studio/drafts/{reportId}", StringComparison.Ordinal)
            && response.Request.Method == "PUT" && response.Status == 204,
            new() { Timeout = 20_000 });
        await page.EvaluateAsync(
            """
            edit => {
                const editor = window.__STUDIO__.state.editorInstance;
                editor.replaceAll(editor.getValue() + '\n' + edit + '\n');
            }
            """, Edit);
        await kept;
    }

    private async Task<string?> DraftContentAsync(int reportId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var draft = await db.StudioRecoveryDrafts.SingleOrDefaultAsync(item => item.ReportId == reportId);
        return draft?.Content.Trim().Split('\n').Last().Trim();
    }

    private async Task<int> CreateReportAsync(IPage page)
    {
        var folderId = await CreateWritableFolderAsync();
        var report = await page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const { studioApi } = await import('/js/api.js');
                return studioApi.createReport(request);
            }
            """,
            new
            {
                folderId,
                name = $"Recovery Journey {Guid.NewGuid():N}",
                // A page makes the report a dashboard, so opening it does not stop to ask which kind.
                scriptText = """
                    CREATE TABLE #t (A INT);
                    CREATE VISUAL T AS TABLE (SOURCE = #t);
                    CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = T));

                    """
            });
        return report.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateWritableFolderAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var adminId = await db.Users.Where(user => user.UserName == PortalBrowserFixture.AdminUsername)
            .Select(user => user.Id)
            .SingleAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder { Name = $"Recovery {suffix}", Path = $"/Recovery-{suffix}", OwnerId = adminId };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }
}
