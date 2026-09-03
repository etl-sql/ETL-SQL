using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.WorkstationEditor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The one continuous authoring journey, driven end to end without leaving the browser:
/// connect → pick a table → drag a visual card onto the canvas → filter it → open Split view →
/// edit the code → run.
///
/// <para>The individual steps already have tests. This class exists because passing them
/// individually is not the same claim: each of those tests sets up the state its step needs, so a
/// step that only works from a state the previous step never leaves behind still passes. The drag
/// is the clearest example — the palette card is only draggable once a data sample exists, and the
/// canvas it lands on is only mounted once a report document is open, so nothing short of one
/// continuous run exercises the affordance the way an author reaches it.</para>
///
/// <para>Both production hosts run the same journey against the same asserted outcomes. Studio
/// serves one <c>studio.js</c> to both, but the hosts differ in how a document is opened, where a
/// connection comes from, and what "reload" means, so a single-host proof would leave the other
/// host's wiring unproven. What each host does with the finished script goes through
/// <see cref="StudioCertification"/>, so the verdict is the same contract the certified journeys
/// are held to.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioContinuousJourneyTests(PortalBrowserFixture fixture)
{
    /// <summary>The visual the journey drags in. BAR because it is not the type already on the page.</summary>
    private const string DraggedVisualType = "BAR";

    private const string DesktopScript = """
        CREATE CONNECTION sample_data AS MOCKDB();
        SELECT UserID, UserName INTO #users FROM sample_data.Users;
        CREATE VISUAL UsersTable AS TABLE (
          SOURCE = #users,
          MAPPINGS (USER_ID = UserID, USER_NAME = UserName)
        );
        CREATE PAGE Main AS DASHBOARD (
          LAYOUT (STRUCTURE = 'A', MAP ('A' = UsersTable))
        );
        """;

    [Fact]
    public async Task Portal_DrivesTheContinuousJourneyFromConnectToRun()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        var alias = $"studio_journey_{Guid.NewGuid():N}";
        await CreateSharedConnectionAsync(page, alias);
        var reportId = await CreateReportAsync(page, alias);

        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(session);

        var marker = await DriveJourneyAsync(session, alias, "-- continuous Portal journey");

        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");

        // The bytes the host hands back, not the ones the editor believes it saved.
        var reloaded = await page.EvaluateAsync<string>(
            """
            async id => {
                const { auth } = await import('/js/api.js');
                const response = await fetch(`/api/reports/${id}/script-content`, {
                    headers: { Authorization: `Bearer ${auth.getToken()}` }
                });
                if (!response.ok) throw new Error(`script-content returned ${response.status}`);
                const payload = await response.json();
                return payload.scriptText ?? payload.script ?? payload.content ?? '';
            }
            """, reportId);
        Assert.Contains(marker, reloaded, StringComparison.Ordinal);

        StudioCertification.Certify(
            new CertifiedArtifact("continuous authoring", StudioHost.Portal, $"report-{reportId}.rptsql", reloaded),
            reloaded);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Desktop_DrivesTheContinuousJourneyFromConnectToRun()
    {
        using var workspace = new StudioTempWorkspace();
        var file = Path.Combine(workspace.Root, "journey.rptsql");
        await File.WriteAllTextAsync(file, DesktopScript);

        await using var host = WorkstationEditorApp.Create(
            [],
            new WorkstationEditorOptions(workspace.Root, file, 0, false, "journey-token",
                StudioMode: true, InstanceId: Guid.NewGuid().ToString("D")));
        await host.StartAsync();

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync($"{WorkstationEditorApp.GetListeningUrl(host)}/studio?token=journey-token");
        await WaitForStudioAsync(session);

        var marker = await DriveJourneyAsync(session, "sample_data", "-- continuous desktop journey");

        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");

        // On this host the file on disk is what the host wrote, so it is what the claim is made against.
        var reloaded = await File.ReadAllTextAsync(file);
        Assert.Contains(marker, reloaded, StringComparison.Ordinal);

        StudioCertification.Certify(
            new CertifiedArtifact("continuous authoring", StudioHost.Desktop, "journey.rptsql", reloaded),
            reloaded);
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// The journey itself, identical on both hosts. Returns the marker the code-edit step wrote, so
    /// the caller can assert it survived that host's own idea of a reload.
    /// </summary>
    private static async Task<string> DriveJourneyAsync(BrowserSession session, string alias, string marker)
    {
        var page = session.Page;

        // ── Connect ──────────────────────────────────────────────────────────
        await page.Locator("[data-activity='catalog']").ClickAsync();
        var connection = page.Locator($"[data-connection='{alias}']");
        await connection.WaitForAsync();
        await connection.ClickAsync();

        // ── Pick a table ─────────────────────────────────────────────────────
        await page.Locator("[data-table='Users']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.snapshot?.rowCount > 0",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        // ── Drag a visual card onto the canvas ───────────────────────────────
        // On the canvas rather than in Split, because that is where an author does it, and because
        // the drop target only exists while the designer is mounted.
        await page.Locator("[data-projection='canvas']").ClickAsync();
        await page.Locator("[data-activity='palette']").ClickAsync();
        var card = page.Locator($"[data-add-visual='{DraggedVisualType}']");
        await card.WaitForAsync();
        Assert.False(await card.IsDisabledAsync(),
            "The visual palette is still disabled after a table was sampled, so an author could not drag a card.");

        var grid = page.Locator(".etlsql-dsgn-grid");
        await grid.WaitForAsync(new LocatorWaitForOptions { Timeout = 20_000 });
        var visualsBefore = await CountVisualsAsync(page);
        await card.DragToAsync(grid);

        try
        {
            await page.WaitForFunctionAsync(
                "expected => (window.__STUDIO__.state.editorInstance.getValue().match(/CREATE\\s+VISUAL/gi) || []).length === expected",
                visualsBefore + 1,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"Dragging a {DraggedVisualType} card onto the canvas wrote nothing into the script. The palette "
                + "card is draggable and sets its own drag payload, so the affordance looks live to an author "
                + $"while doing nothing. Script:{Environment.NewLine}"
                + await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"),
                exception);
        }

        // ── Configure what was dropped ───────────────────────────────────────
        // A card arrives with a source but no roles, and the engine refuses a chart that has no X
        // and no Y — correctly, since it cannot be drawn. Selecting it opens the properties panel,
        // where clicking a field fills the next empty role, which is how an author finishes the drop.
        var droppedCard = page.Locator(".etlsql-dsgn-visual-card:not([data-vid='v_UsersTable_0'])").First;
        await droppedCard.ClickAsync();
        var fields = page.Locator("[data-property-field]");
        await fields.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await fields.Nth(0).ClickAsync();
        await fields.Nth(1).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => /MAPPINGS\\s*\\([^)]*\\)/.test(window.__STUDIO__.state.editorInstance.getValue().split('CREATE VISUAL').slice(-1)[0])",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // ── Filter ───────────────────────────────────────────────────────────
        await page.Locator("[data-activity='catalog']").ClickAsync();
        await page.EvaluateAsync("() => { window.__STUDIO__.state.selectedVisualId = 'UsersTable'; }");
        await page.Locator("[data-field='UserName']").ClickAsync();
        await page.Locator("[data-filter-dialog-apply]").ClickAsync();
        await page.Locator("[data-filter-value='UserName']").First.CheckAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('ETL-SQL-STUDIO-FILTER')");

        // ── Open Split view ──────────────────────────────────────────────────
        await page.Locator("[data-projection='split']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].projection === 'split'");
        // Split means both panes, so both are asserted visible rather than just the projection flag.
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-visual-stage]")).ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-code-stage]")).ToBeVisibleAsync();

        // ── Edit the code ────────────────────────────────────────────────────
        await page.EvaluateAsync(
            "text => window.__STUDIO__.state.editorInstance.setValue(window.__STUDIO__.state.editorInstance.getValue() + `\n${text}\n`)",
            marker);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === true");

        // ── Run ──────────────────────────────────────────────────────────────
        // Run the statement the author is looking at, not the whole document. The Portal's
        // interactive-run policy refuses presentation statements — a report script is executed by
        // rendering the report, not by an ad-hoc run — so "run all" is a desktop-only affordance and
        // running a selection is the step both hosts actually offer an author mid-edit.
        await page.EvaluateAsync("() => window.__STUDIO__.state.editorInstance.gotoLine(1, 1)");
        await page.Locator("[data-action='run-selected']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.runActive === false",
            null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        var trace = await page.EvaluateAsync<JsonElement>(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace");
        Assert.True(
            trace.EnumerateArray().Any(item => item.GetProperty("type").GetString() == "results"),
            $"The run produced no result set. Trace: {trace}");

        return marker;
    }

    private static async Task<int> CountVisualsAsync(IPage page) => await page.EvaluateAsync<int>(
        "() => (window.__STUDIO__.state.editorInstance.getValue().match(/CREATE\\s+VISUAL/gi) || []).length");

    private static async Task CreateSharedConnectionAsync(IPage page, string alias)
    {
        var status = await page.EvaluateAsync<int>(
            """
            async alias => {
                const { auth } = await import('/js/api.js');
                const response = await fetch(`/api/admin/connections/${encodeURIComponent(alias)}`, {
                    method: 'PUT',
                    headers: {
                        Authorization: `Bearer ${auth.getToken()}`,
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({ connectorType: 'MOCKDB', options: {} })
                });
                return response.status;
            }
            """, alias);
        Assert.Equal(204, status);
    }

    private async Task<int> CreateReportAsync(IPage page, string alias)
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
                name = $"Continuous Journey {Guid.NewGuid():N}",
                scriptText = $"""
                    SELECT UserID, UserName INTO #users FROM {alias}.Users;
                    CREATE VISUAL UsersTable AS TABLE (
                      SOURCE = #users,
                      MAPPINGS (USER_ID = UserID, USER_NAME = UserName)
                    );
                    CREATE PAGE Main AS DASHBOARD (
                      LAYOUT (STRUCTURE = 'A', MAP ('A' = UsersTable))
                    );
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
        var folder = new Folder
        {
            Name = $"Journey {suffix}",
            Path = $"/Journey-{suffix}",
            OwnerId = adminId
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private static async Task WaitForStudioAsync(BrowserSession session)
    {
        try
        {
            await session.Page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
                new PageWaitForFunctionOptions { Timeout = 20_000 });
        }
        catch (TimeoutException exception)
        {
            var body = await session.Page.Locator("body").InnerTextAsync();
            throw new Xunit.Sdk.XunitException(
                $"Studio did not boot at {session.Page.Url}. Body: {body[..Math.Min(body.Length, 500)]}. "
                + $"Page errors: {string.Join(" | ", session.PageErrors)}. "
                + $"Console errors: {string.Join(" | ", session.ConsoleErrors)}. "
                + $"Failed requests: {string.Join(" | ", session.FailedRequests)}.", exception);
        }
    }
}
