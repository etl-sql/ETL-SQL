using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The behavioural half of the Studio authoring contract: rules 4 and 5, which cannot be checked by
/// inspecting the source.
///
/// <para><b>Rule 4 — preview before write.</b> A guided surface shows the exact Report-SQL it is about
/// to write, and writes only on an explicit confirm. The assertion here is deliberately literal: read
/// the SQL out of the dialog's preview block, click confirm, and require that same statement to appear
/// in the buffer. Anything weaker would pass while the preview drifted away from what the patcher
/// actually emits, which is the failure this lane exists to prevent.</para>
///
/// <para><b>Rule 5 — read state from the parse.</b> Reopening a wizard after the author has hand-edited
/// the script must see the edited script, not whatever the wizard wrote last time.</para>
///
/// <para>This lane exists because the guided steps shipped broken: the buttons were wired, the patcher
/// worked, and clicking a step still did nothing visible — and nothing in the suite would have noticed.
/// The UI sandbox actively concealed it by echoing the script back unchanged from
/// <c>/api/designer/patch</c>, so a test that only asserted "the click did not throw" would have stayed
/// green throughout.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioWizardContractTests(PortalBrowserFixture fixture) : IAsyncLifetime
{
    private IHost? host;
    private string baseUrl = "";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Serves the repository root, because the sandbox's ES modules import out of <c>src/</c>.</summary>
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var provider = new PhysicalFileProvider(RepoRoot());
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, ServeUnknownFileTypes = true });

        host = app;
        await app.StartAsync();
        baseUrl = app.Urls.First().TrimEnd('/');
    }

    public async Task DisposeAsync()
    {
        if (host is null) return;
        await host.StopAsync(TimeSpan.FromSeconds(10));
        host.Dispose();
    }

    private async Task<IPage> OpenStudioAsync(BrowserSession session)
    {
        var page = session.Page;
        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO_INSTANCE__?.state?.editorInstance)");
        return page;
    }

    /// <summary>
    /// Collapses runs of whitespace so a preview and the patcher's output compare on content rather
    /// than on indentation and line endings, which legitimately differ between the two.
    /// </summary>
    private static string Normalize(string sql) =>
        Regex.Replace(sql ?? string.Empty, @"\s+", " ").Trim();

    private static string ScriptTextScript =>
        "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()";

    private static async Task<string> PreviewedSqlAsync(IPage page) =>
        (await page.Locator(".etlsql-studio-sql-preview pre").First.TextContentAsync()) ?? "";

    // ── Rule 4: preview before write ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DatasetWizard_WritesExactlyTheCreateDatasetItPreviewed()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenStudioAsync(session);

        // sales_overview.rptsql declares corp_db, so the create path is reachable. A report with no
        // CREATE CONNECTION is deliberately blocked, which the blocker test below covers.
        await page.ClickAsync("[data-workflow-step='catalog']");
        await page.ClickAsync("[data-start-path='create']");
        await page.ClickAsync("[data-pick-connection='corp_db']");
        await page.Locator("[data-pick-table]").First.WaitForAsync();
        await page.Locator("[data-pick-table]").First.ClickAsync();
        await page.ClickAsync("[data-dialog-action='next']");
        await page.Locator(".etlsql-studio-sql-preview pre").WaitForAsync();

        var previewed = await PreviewedSqlAsync(page);
        Assert.Contains("CREATE DATASET", previewed, StringComparison.Ordinal);

        await page.ClickAsync("[data-dialog-action='create']");
        await page.Locator("[data-dialog-action='create']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        var script = await page.EvaluateAsync<string>(ScriptTextScript);
        Assert.Contains(Normalize(previewed), Normalize(script), StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ParameterStep_WritesExactlyTheDeclarationItPreviewed()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenStudioAsync(session);
        await NewPaginatedReportAsync(page);

        await page.ClickAsync("[data-workflow-step='parameter']");
        await page.Locator(".etlsql-studio-sql-preview pre").WaitForAsync();

        var previewed = await PreviewedSqlAsync(page);
        Assert.StartsWith("DECLARE", previewed.Trim(), StringComparison.Ordinal);

        await page.ClickAsync("[data-dialog-action='add']");
        await page.Locator("[data-dialog-action='add']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        var script = await page.EvaluateAsync<string>(ScriptTextScript);
        Assert.Contains(Normalize(previewed), Normalize(script), StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ChartBuilder_WritesExactlyTheVisualItPreviewed()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenStudioAsync(session);

        await page.ClickAsync("[data-workflow-step='palette']");
        await page.ClickAsync("[data-dialog-action='build']");
        await page.Locator(".etlsql-studio-sql-preview pre").WaitForAsync();

        var previewed = await PreviewedSqlAsync(page);
        Assert.Contains("CREATE VISUAL", previewed, StringComparison.Ordinal);

        await page.ClickAsync("[data-dialog-action='add']");
        await page.Locator("[data-dialog-action='add']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        var script = await page.EvaluateAsync<string>(ScriptTextScript);
        Assert.Contains(Normalize(previewed), Normalize(script), StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task AStepThatCannotRunYet_WritesNothingAndOffersTheControlThatFixesIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenStudioAsync(session);
        await NewPaginatedReportAsync(page);

        var before = await page.EvaluateAsync<string>(ScriptTextScript);

        // Totals need a detail table. The step must say so and offer step 3, not write a half-formed
        // statement and not fail into a toast the author cannot act on.
        await page.ClickAsync("[data-workflow-step='totals']");
        await page.Locator("[data-dialog-action='fix']").WaitForAsync();

        var body = await page.Locator(".etlsql-studio-guided-body").TextContentAsync() ?? "";
        Assert.Contains("detail table", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await page.Locator(".etlsql-studio-sql-preview").AllAsync());

        var after = await page.EvaluateAsync<string>(ScriptTextScript);
        Assert.Equal(before, after);
        Assert.Empty(session.PageErrors);
    }

    // ── Rule 5: read state from the parse ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReopeningTheDataWizard_SeesADatasetTheAuthorTypedByHand()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenStudioAsync(session);

        // A wizard that remembered its own last write would not see this, and would then offer to
        // create a dataset that already exists.
        await page.EvaluateAsync(
            """
            async () => {
                const editor = window.__STUDIO_INSTANCE__.state.editorInstance;
                editor.setValue(
                    'CREATE DATASET &hand_written AS (SELECT 1 AS a);' + String.fromCharCode(10)
                    + editor.getValue());
                await new Promise(resolve => setTimeout(resolve, 1200));
            }
            """);

        await page.ClickAsync("[data-workflow-step='catalog']");
        await page.ClickAsync("[data-start-path='existing']");
        await page.Locator("[data-use-dataset]").First.WaitForAsync();

        var offered = await page.Locator("[data-use-dataset]").AllTextContentsAsync();
        Assert.Contains(offered, entry => entry.Contains("hand_written", StringComparison.Ordinal));
        Assert.Empty(session.PageErrors);
    }

    /// <summary>Creates a blank paginated report, which is where the numbered report steps live.</summary>
    private static async Task NewPaginatedReportAsync(IPage page)
    {
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");
        await page.Locator("[data-create-from-home='paginated']").WaitForAsync();
        await page.ClickAsync("[data-create-from-home='paginated']");
        await page.Locator("[data-workflow-step='parameter']").WaitForAsync();
    }
}
