using ETL_SQL.WorkstationEditor;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Studio's governance rail, driven against the real desktop host.
///
/// <para>Against the real host on purpose. The projection and the write are exercised by unit tests
/// already; what those cannot show is that the rail button reaches the panel, that the panel reaches
/// the route, and that the route's answer reaches the editor buffer. Every one of those seams has
/// broken silently in this workbench before — a panel written, wired, and unreachable; a client that
/// swallows a 404 and renders a success — so the assertions here end at the author's own text.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioGovernanceTests(PortalBrowserFixture fixture)
{
    private const string InitialScript = """
        CREATE CONNECTION sample_data AS MOCKDB();

        INSERT TAG FOR TABLE sample_data.Users COLUMN UserName (pii = 'true');

        SELECT UserID, UserName INTO #users FROM sample_data.Users;
        """;

    [Fact]
    public async Task GovernanceRail_ShowsInheritedTags_AndWritesAnAuthoredOneIntoTheBuffer()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Root, "users.etlsql");
        await File.WriteAllTextAsync(file, InitialScript);

        await using var host = WorkstationEditorApp.Create([], Options(workspace.Root, file, "gov-token"));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync(StudioUrl(host, "gov-token"));
        await WaitForStudioAsync(session);

        await page.Locator("[data-activity='governance']").ClickAsync();
        var rows = page.Locator("[data-gov-scope]");
        await rows.First.WaitForAsync(new() { Timeout = 15_000 });

        // The inherited tag is the claim worth checking: #users.UserName carries @pii because the
        // source column does, and the panel has to say where it came from rather than showing it as
        // though somebody wrote it here.
        await page.Locator("[data-gov-scope='column:#users.UserName']").ClickAsync();
        var derived = page.Locator("[data-gov-detail] .etlsql-studio-gov-tag.is-derived");
        await derived.First.WaitForAsync(new() { Timeout = 10_000 });
        var derivedText = await derived.First.InnerTextAsync();
        Assert.Contains("pii", derivedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sample_data.Users.UserName", derivedText, StringComparison.OrdinalIgnoreCase);

        // A projected column is authored inline, and the panel says so before it writes.
        Assert.Contains("comment on the column",
            await page.Locator("[data-gov-detail] .etlsql-studio-subhead").InnerTextAsync(),
            StringComparison.OrdinalIgnoreCase);

        await page.Locator("[data-gov-name]").SelectOptionAsync("owner");
        await page.Locator("[data-gov-value]").FillAsync("analytics");
        await page.Locator("[data-gov-apply]").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('@owner: analytics')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.Contains("@owner: analytics", script, StringComparison.Ordinal);
        // Written on the column, not as a statement — the two forms are not interchangeable.
        Assert.DoesNotContain("INSERT TAG FOR TABLE #users", script, StringComparison.Ordinal);
        Assert.Contains("INSERT TAG FOR TABLE sample_data.Users COLUMN UserName", script, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task GovernanceRail_SaysWhyARefusedTagChangedNothing()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Root, "users.etlsql");
        await File.WriteAllTextAsync(file, InitialScript);

        await using var host = WorkstationEditorApp.Create([], Options(workspace.Root, file, "gov-refuse-token"));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync(StudioUrl(host, "gov-refuse-token"));
        await WaitForStudioAsync(session);

        await page.Locator("[data-activity='governance']").ClickAsync();
        await page.Locator("[data-gov-scope='table:#users']").WaitForAsync(new() { Timeout = 15_000 });
        await page.Locator("[data-gov-scope='table:#users']").ClickAsync();

        // freshness takes a duration. A value the catalog refuses must leave the script alone and
        // say so — a panel that redraws unchanged is indistinguishable from one that applied it.
        await page.Locator("[data-gov-name]").SelectOptionAsync("freshness");
        await page.Locator("[data-gov-value]").FillAsync("soonish");
        await page.Locator("[data-gov-apply]").ClickAsync();

        var toast = page.Locator(".etlsql-feedback-toast").Filter(new() { HasTextString = "duration" });
        await toast.First.WaitForAsync(new() { Timeout = 10_000 });

        var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.DoesNotContain("freshness", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GovernanceRail_AuthorsAnExpectRule_AndRoutesTheRowsItRejects()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Root, "users.etlsql");
        await File.WriteAllTextAsync(file, InitialScript);

        await using var host = WorkstationEditorApp.Create([], Options(workspace.Root, file, "gov-rule-token"));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync(StudioUrl(host, "gov-rule-token"));
        await WaitForStudioAsync(session);

        await page.Locator("[data-activity='governance']").ClickAsync();
        await page.Locator("[data-gov-scope='column:#users.UserName']").WaitForAsync(new() { Timeout = 15_000 });
        await page.Locator("[data-gov-scope='column:#users.UserName']").ClickAsync();

        await page.Locator("[data-gov-rule-text]").WaitForAsync(new() { Timeout = 10_000 });
        await page.Locator("[data-gov-rule-text]").FillAsync("NOT BLANK");
        await page.Locator("[data-gov-rule-action]").SelectOptionAsync("QUARANTINE");
        await page.Locator("[data-gov-rule-apply]").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('EXPECT NOT BLANK ON FAILURE QUARANTINE')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        // The rule elects QUARANTINE and the statement routes nowhere, so those rows have nowhere to
        // go. Nothing in the language refuses that; the panel has to be what says so.
        await page.Locator("[data-gov-scope='table:#users']").ClickAsync();
        var warning = page.Locator("[data-gov-routing] .etlsql-studio-gov-finding");
        await warning.First.WaitForAsync(new() { Timeout = 10_000 });
        Assert.Contains("nowhere to go", await warning.First.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await page.Locator("[data-gov-routing-action]").SelectOptionAsync("QUARANTINE");
        await page.Locator("[data-gov-routing-target]").FillAsync("#rejected_users");
        await page.Locator("[data-gov-routing-retention]").FillAsync("30 DAYS");
        await page.Locator("[data-gov-routing-apply]").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('ON FAILURE QUARANTINE TO #rejected_users')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.Contains("RETENTION = '30 DAYS'", script, StringComparison.Ordinal);
        Assert.Contains("HANDLING = STEWARD", script, StringComparison.Ordinal);

        // With the rows routed, the warning is gone and the panel points at where they are inspected.
        await page.Locator("[data-gov-scope='table:#users']").ClickAsync();
        await page.Locator("[data-gov-routing]").WaitForAsync(new() { Timeout = 10_000 });
        var routing = await page.Locator("[data-gov-routing]").InnerTextAsync();
        Assert.DoesNotContain("nowhere to go", routing, StringComparison.OrdinalIgnoreCase);
        // No steward queue on the desktop host, so it says where the queue lives rather than
        // offering a link that goes nowhere.
        Assert.Contains("steward queue", routing, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await page.Locator("[data-gov-steward-link]").AllAsync());
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// The preview identity, proved by what a run returns rather than by the control being present.
    ///
    /// <para>A workstation run carries no identity at all, so <c>HAS_GROUP</c> is false and an
    /// RLS-guarded query returns nothing — which is exactly what a broken preview also looks like.
    /// The test therefore asserts both directions: no rows before, the row after, from the same
    /// script.</para>
    /// </summary>
    [Fact]
    public async Task PreviewAs_ChangesWhatTheAuthorsPredicatesSee()
    {
        const string guarded = """
            CREATE CONNECTION sample_data AS MOCKDB();
            SELECT 'north-only' AS Label WHERE HAS_GROUP('Region:North');
            """;

        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Root, "guarded.etlsql");
        await File.WriteAllTextAsync(file, guarded);

        await using var host = WorkstationEditorApp.Create([], Options(workspace.Root, file, "gov-rls-token"));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync(StudioUrl(host, "gov-rls-token"));
        await WaitForStudioAsync(session);

        Assert.Equal(0, await RunRowCountAsync(page));

        await page.Locator("[data-activity='governance']").ClickAsync();
        await page.Locator("[data-gov-preview-groups]").WaitForAsync(new() { Timeout = 15_000 });
        await page.Locator("[data-gov-preview-label]").FillAsync("a northern rep");
        await page.Locator("[data-gov-preview-groups]").FillAsync("Region:North");
        await page.Locator("[data-gov-preview-apply]").ClickAsync();

        // Previewed rows look exactly like real ones, so the banner is the only thing telling the
        // author whose result they are reading.
        var banner = page.Locator("[data-studio-preview-banner]");
        await banner.WaitForAsync(new() { Timeout = 10_000, State = WaitForSelectorState.Visible });
        Assert.Contains("a northern rep", await banner.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, await RunRowCountAsync(page));

        await banner.Locator("[data-preview-banner-clear]").ClickAsync();
        await banner.WaitForAsync(new() { Timeout = 10_000, State = WaitForSelectorState.Hidden });
        Assert.Equal(0, await RunRowCountAsync(page));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetLifecycle_ChangesAccessAndWritesAStepWithoutTouchingTheOtherClauses()
    {
        const string dataset = """
            CREATE CONNECTION sample_data AS MOCKDB();

            SELECT UserID, UserName INTO #users FROM sample_data.Users;

            CREATE DATASET &people COMPRESS = ON ENCRYPT = MACHINE TTL = '1h' AS (
              SELECT UserID, UserName FROM #users
            );

            -- A .rptsql with no page has no declared workflow, so opening one asks the author
            -- "dashboard or paginated?" before the workbench finishes booting. A fixture without a
            -- page therefore waits on a modal nobody answers, which reads as a hang.
            CREATE PAGE Main AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
            """;

        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Root, "people.rptsql");
        await File.WriteAllTextAsync(file, dataset);

        await using var host = WorkstationEditorApp.Create([], Options(workspace.Root, file, "gov-dataset-token"));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync(StudioUrl(host, "gov-dataset-token"));
        await WaitForStudioAsync(session);

        await page.Locator("[data-activity='governance']").ClickAsync();
        var card = page.Locator("[data-gov-dataset='&people']");
        await card.WaitForAsync(new() { Timeout = 15_000 });

        await card.Locator("[data-gov-dataset-access]").SelectOptionAsync("PUBLIC");
        await card.Locator("[data-gov-dataset-ttl]").FillAsync("24h");
        await card.Locator("[data-gov-dataset-save]").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('ACCESS PUBLIC')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        await page.Locator("[data-gov-dataset='&people'] [data-gov-dataset-step]").SelectOptionAsync("export");
        await page.Locator("[data-gov-dataset='&people'] [data-gov-dataset-path]").FillAsync("people.parquet");
        await page.Locator("[data-gov-dataset='&people'] [data-gov-dataset-secret]").FillAsync("hunter2");
        await page.Locator("[data-gov-dataset='&people'] [data-gov-dataset-step-add]").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('EXPORT DATASET &people')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.Contains("TTL = '24h'", script, StringComparison.Ordinal);
        // The clauses no authoring model represents are still exactly where the author put them.
        Assert.Contains("COMPRESS = ON", script, StringComparison.Ordinal);
        Assert.Contains("ENCRYPT = MACHINE", script, StringComparison.Ordinal);
        Assert.Contains("EXPORT DATASET &people TO 'people.parquet' ENCRYPT = PASSWORD PASSWORD = 'hunter2';", script, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DatasetLifecycle_RefusesAnExportWithNoTransportCredential()
    {
        const string dataset = """
            CREATE CONNECTION sample_data AS MOCKDB();
            SELECT UserID INTO #users FROM sample_data.Users;
            CREATE DATASET &people AS ( SELECT UserID FROM #users );
            """;

        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Root, "people.etlsql");
        await File.WriteAllTextAsync(file, dataset);

        await using var host = WorkstationEditorApp.Create([], Options(workspace.Root, file, "gov-dataset-refuse"));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync(StudioUrl(host, "gov-dataset-refuse"));
        await WaitForStudioAsync(session);

        await page.Locator("[data-activity='governance']").ClickAsync();
        var card = page.Locator("[data-gov-dataset='&people']");
        await card.WaitForAsync(new() { Timeout = 15_000 });

        await card.Locator("[data-gov-dataset-step]").SelectOptionAsync("export");
        await card.Locator("[data-gov-dataset-path]").FillAsync("people.parquet");
        await card.Locator("[data-gov-dataset-step-add]").ClickAsync();

        var toast = page.Locator(".etlsql-feedback-toast").Filter(new() { HasTextString = "transport credential" });
        await toast.First.WaitForAsync(new() { Timeout = 10_000 });

        var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.DoesNotContain("EXPORT DATASET", script, StringComparison.Ordinal);
    }

    /// <summary>Runs the open script through Studio's own run path and counts the rows it returns.</summary>
    private static async Task<int> RunRowCountAsync(IPage page) =>
        await page.EvaluateAsync<int>("""
            async () => {
                const studio = window.__STUDIO__;
                const body = {
                    script: studio.state.editorInstance.getValue(),
                    ...(studio.state.previewAs ? { previewAs: studio.state.previewAs } : {}),
                };
                // The host gates /api on the session token the page was opened with; it is in the
                // page URL, which is how Studio's own fetches carry it too.
                const token = new URLSearchParams(location.search).get('token') || '';
                const url = new URL('/api/designer/run', location.href);
                url.searchParams.set('token', token);
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body),
                });
                if (!response.ok) return -1;
                const result = await response.json();
                return (result.rows || []).length;
            }
            """);

    private static WorkstationEditorOptions Options(string root, string file, string token) =>
        new(root, file, 0, false, token, StudioMode: true, InstanceId: Guid.NewGuid().ToString("D"));

    private static string StudioUrl(Microsoft.AspNetCore.Builder.WebApplication app, string token) =>
        $"{WorkstationEditorApp.GetListeningUrl(app)}/studio?token={Uri.EscapeDataString(token)}";

    /// <summary>
    /// Waits for the workbench to finish booting, and says why it did not when it does not.
    ///
    /// <para>Boot renders the shell before it finishes, so a failure leaves a page that looks like
    /// Studio and is not: every control is drawn and none of them works. The page's own errors are
    /// the only thing that distinguishes that from a slow start, so they are reported here.</para>
    /// </summary>
    private static async Task WaitForStudioAsync(BrowserSession session)
    {
        try
        {
            await session.Page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
                new PageWaitForFunctionOptions { Timeout = 20_000 });
        }
        catch (TimeoutException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"Studio did not boot at {session.Page.Url}. "
                + $"Page errors: {string.Join(" | ", session.PageErrors)}. "
                + $"Console errors: {string.Join(" | ", session.ConsoleErrors)}.", ex);
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "etlsql-studio-governance", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup must never mask a failed assertion.
            }
        }
    }
}
