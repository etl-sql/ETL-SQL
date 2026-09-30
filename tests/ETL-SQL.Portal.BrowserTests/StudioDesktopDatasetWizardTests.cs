using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The dataset wizard on the desktop host, where a connection is whatever the script declares.
///
/// <para>The host learns a document's connections by analysing its script, and the editor asks it
/// to on a debounce. A connection written a moment before the wizard opened was therefore not known
/// to the host yet, and reading its tables failed with "not registered for this document". The
/// pipeline task dialog had the same race; both now ask the host to analyse before reading.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioDesktopDatasetWizardTests(StudioAuthoringFixture fixture)
{
    private const string Seed = """
        CREATE CONNECTION sample_data AS MOCKDB();
        CREATE PAGE [Main] AS DASHBOARD (
          LAYOUT (STRUCTURE = '.')
        );
        """;

    [Fact]
    public async Task AConnectionDeclaredAMomentAgo_ListsItsTables()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "sales.rptsql");
        await File.WriteAllTextAsync(file, Seed);

        await using var host = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
            workspace.Root, file, 0, false, "wizard-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=wizard-token");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__?.state?.editorInstance)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });

        // A slow host: every analysis takes two seconds. Opening the wizard does start one, but the
        // connection is listed from the script at once, so a pick could read its schema before the
        // host had registered it.
        await page.RouteAsync("**/api/designer/analyze", async route =>
        {
            await Task.Delay(2_000);
            await route.ContinueAsync();
        });

        // Written in the code pane and used straight away, inside the editor's analysis debounce.
        await page.EvaluateAsync(
            "() => { const editor = window.__STUDIO__.state.editorInstance;"
            + " editor.setValue('CREATE CONNECTION fresh_db AS MOCKDB();\\n' + editor.getValue()); }");

        await page.Locator("[data-activity='catalog']").ClickAsync();
        await page.Locator("[data-new-dataset]").ClickAsync();
        await page.Locator("[data-start-path='create']").ClickAsync();
        await page.Locator("[data-pick-connection='fresh_db']").ClickAsync();

        try
        {
            await page.Locator("[data-pick-table='Users']").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            var dialog = await page.Locator("[data-modal-backdrop]").InnerTextAsync();
            throw new Xunit.Sdk.XunitException(
                $"fresh_db's tables never listed. The dialog said:{Environment.NewLine}{dialog}", exception);
        }
        Assert.Empty(session.PageErrors);
    }
}
