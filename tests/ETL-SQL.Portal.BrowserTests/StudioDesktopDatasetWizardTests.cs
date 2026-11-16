using System.Collections.Concurrent;
using System.Diagnostics;
using ETL_SQL.Core;
using ETL_SQL.Core.Services;
using ETL_SQL.WorkstationEditor;
using Microsoft.Extensions.DependencyInjection;
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
        var metadata = Assert.IsType<MetadataManager>(host.Services.GetRequiredService<IMetadataManager>());
        metadata.SchemaCacheDirectory = Path.Combine(workspace.Root, "schema-cache");
        Assert.False(Directory.Exists(metadata.SchemaCacheDirectory));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var timer = Stopwatch.StartNew();
        var requests = new ConcurrentQueue<string>();
        void RecordRequest(string kind, IRequest request, int? status = null)
        {
            if (requests.Count >= 100 || !Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
                || url.AbsolutePath is not ("/api/designer/analyze" or "/api/designer/schema")) return;
            requests.Enqueue($"{timer.ElapsedMilliseconds}ms {kind} {BrowserTransportFailures.SanitizeUrl(request.Url)} {status}");
        }
        page.Request += (_, request) => RecordRequest("start", request);
        page.Response += (_, response) => RecordRequest("response", response.Request, response.Status);
        page.RequestFailed += (_, request) => RecordRequest("failed", request);
        await page.GotoWithDiagnosticsAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=wizard-token");
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
            await BrowserNavigationDiagnostics.CaptureAsync(page, "dataset-picker-wait-failure",
                nameof(AConnectionDeclaredAMomentAgo_ListsItsTables), page.Url);
            var observation = "unavailable";
            try
            {
                observation = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                        tables: document.querySelectorAll('[data-pick-table]').length,
                        users: Array.from(document.querySelectorAll('[data-pick-table="Users"]')).map(button => {
                            const bounds = button.getBoundingClientRect();
                            return { hidden: button.hidden, width: bounds.width, height: bounds.height,
                                display: getComputedStyle(button).display, visibility: getComputedStyle(button).visibility };
                        })
                    })
                    """).WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception snapshotFailure) when (snapshotFailure is PlaywrightException or TimeoutException)
            {
                observation = snapshotFailure.GetType().Name;
            }
            var dialog = await page.Locator("[data-modal-backdrop]").InnerTextAsync();
            throw new Xunit.Sdk.XunitException(
                $"fresh_db's tables never listed. State: {observation}{Environment.NewLine}"
                + string.Join(Environment.NewLine, requests) + $"{Environment.NewLine}The dialog said:{Environment.NewLine}{dialog}", exception);
        }
        Assert.Empty(session.PageErrors);
    }
}
