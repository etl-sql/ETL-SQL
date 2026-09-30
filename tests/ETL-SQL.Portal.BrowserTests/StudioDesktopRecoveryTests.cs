using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The self-installed host keeps recovery drafts in its own draft folder, not in the browser. A
/// browser that dies with unsaved edits loses nothing: the next Studio page offers them back.
/// </summary>
[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioDesktopRecoveryTests(StudioAuthoringFixture fixture)
{
    private const string Seed = """
        CREATE TABLE #t (A INT);
        CREATE VISUAL T AS TABLE (SOURCE = #t);
        CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = T));

        """;

    private const string Edit = "-- unsaved desktop edit";

    [Fact]
    public async Task AnEditLostToACrashIsOfferedBackByTheSelfInstalledHost()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "sales.rptsql");
        await File.WriteAllTextAsync(file, Seed);
        var drafts = Path.Combine(workspace.Root, "..", Path.GetFileName(workspace.Root) + "-drafts");

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "recovery-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D"), DraftDirectory: drafts));
        await host.StartAsync();
        var studio = $"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=recovery-token";

        try
        {
            await using (var crashed = await fixture.NewSessionAsync())
            {
                var page = crashed.Page;
                await page.GotoAsync(studio);
                await WaitForEditorAsync(page);
                var kept = page.WaitForResponseAsync(response =>
                    response.Url.EndsWith("/api/drafts", StringComparison.Ordinal)
                    && response.Request.Method == "PUT" && response.Status == 204,
                    new() { Timeout = 20_000 });
                await page.EvaluateAsync(
                    "edit => { const e = window.__STUDIO__.state.editorInstance; e.replaceAll(e.getValue() + '\\n' + edit + '\\n'); }",
                    Edit);
                await kept;
            }

            // Nothing of the script reached browser storage, and the file itself is untouched.
            Assert.Equal(Seed, await File.ReadAllTextAsync(file));
            Assert.Single(Directory.GetFiles(drafts, "*.json"));

            await using var session = await fixture.NewSessionAsync();
            var reopened = session.Page;
            await reopened.GotoAsync(studio);
            await WaitForEditorAsync(reopened);
            Assert.Equal(0, await reopened.EvaluateAsync<int>(
                "() => Object.keys(localStorage).filter(key => key.startsWith('etlsql_studio_draft')).length"));

            var offer = reopened.Locator(".etlsql-feedback-toast", new() { HasText = "from a previous session were kept" });
            await Expect(offer).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await offer.Locator(".etlsql-feedback-action", new() { HasText = "Restore" }).ClickAsync();
            await reopened.WaitForFunctionAsync("edit => window.__STUDIO__.state.editorInstance.getValue().includes(edit)", Edit);
            Assert.Empty(session.PageErrors);
        }
        finally
        {
            if (Directory.Exists(drafts)) Directory.Delete(drafts, recursive: true);
        }
    }

    private static Task WaitForEditorAsync(IPage page) =>
        page.WaitForFunctionAsync(
            "() => (window.__STUDIO__?.state?.documents || []).some(d => d.path === 'sales.rptsql') && window.__STUDIO__.state.editorInstance",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
}
