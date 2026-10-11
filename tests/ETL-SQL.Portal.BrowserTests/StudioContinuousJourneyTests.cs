using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.WorkstationEditor;
using Microsoft.AspNetCore.Identity;
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
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioContinuousJourneyTests(StudioAuthoringFixture fixture)
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
        var alias = $"studio_journey_{Guid.NewGuid():N}";
        await using (var setup = await fixture.NewSessionAsync())
        {
            await fixture.SignInAsync(setup.Page);
            await CreateSharedConnectionAsync(setup.Page, alias);
        }
        var authorId = await SignInAsPublisherAsync(page);
        var reportId = await CreateReportAsync(page, alias, authorId);

        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(session);

        var expected = await DriveJourneyAsync(session, alias, "-- continuous Portal journey");

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
        Assert.Equal(NormalizeEndings(expected), NormalizeEndings(reloaded));
        await page.ReloadAsync();
        await WaitForStudioAsync(session);
        Assert.Equal(NormalizeEndings(expected), NormalizeEndings(await ScriptAsync(page)));
        StudioCertification.Certify(
            new CertifiedArtifact("continuous authoring", StudioHost.Portal, $"report-{reportId}.rptsql", expected),
            reloaded);
        await page.GotoAsync($"/index.html#report-{reportId}");
        var execute = page.Locator("#execBtn");
        var refresh = page.Locator("#refreshBtn");
        await Microsoft.Playwright.Assertions.Expect(execute.Or(refresh).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await execute.IsVisibleAsync()) await execute.ClickAsync();
        else await refresh.ClickAsync();
        var report = page.FrameLocator("#reportFrame iframe");
        await Microsoft.Playwright.Assertions.Expect(report.Locator("[data-visual-name='UsersTable'] tbody tr").First)
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
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

        var expected = await DriveJourneyAsync(session, "sample_data", "-- continuous desktop journey");

        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");

        // On this host the file on disk is what the host wrote, so it is what the claim is made against.
        var reloaded = await File.ReadAllTextAsync(file);
        Assert.Equal(NormalizeEndings(expected), NormalizeEndings(reloaded));
        await page.ReloadAsync();
        await WaitForStudioAsync(session);
        Assert.Equal(NormalizeEndings(expected), NormalizeEndings(await ScriptAsync(page)));
        StudioCertification.Certify(
            new CertifiedArtifact("continuous authoring", StudioHost.Desktop, "journey.rptsql", expected),
            reloaded);
        var run = page.WaitForResponseAsync(response =>
            response.Url.EndsWith("/api/designer/run", StringComparison.Ordinal)
            && response.Request.Method == "POST");
        await page.Locator("[data-action='run']").ClickAsync();
        Assert.True((await run).Ok);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].studioContext.runActive === false");
        await AssertLearningResultAsync(page);
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// The same gestures on both hosts. Returns an independent pre-save capture for persistence checks.
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
        // Both roles, not just "a MAPPINGS clause exists". Each click patches the script on its own,
        // so after the first one `MAPPINGS (X = …)` already satisfies a presence-only regex — the
        // journey then saved a BAR with no Y and the linter rejected it, which is exactly what CI
        // caught while a faster machine landed both clicks before the check ran. The condition that
        // matters is the one the engine enforces: a bar chart needs X and Y.
        await page.WaitForFunctionAsync(
            """
            () => {
                const last = window.__STUDIO__.state.editorInstance.getValue()
                    .split('CREATE VISUAL').slice(-1)[0];
                const mappings = last.match(/MAPPINGS\s*\(([^)]*)\)/);
                return !!mappings && /\bX\s*=/.test(mappings[1]) && /\bY\s*=/.test(mappings[1]);
            }
            """,
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

        // Inspect the helper's SQL and explanation before making a keyboard edit.
        await AssertExplainedHelperCancelsWithoutEditingAsync(page);
        var editor = page.Locator("[data-code-stage] .cm-content");
        await editor.ClickAsync();
        await editor.PressAsync("ControlOrMeta+End");
        await page.Keyboard.InsertTextAsync($"\n{marker}\nSELECT 42 AS LearningCheck;");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === true");
        var expected = await ScriptAsync(page);
        Assert.Contains("SELECT 42 AS LearningCheck;", expected, StringComparison.Ordinal);

        // Returning to the helper must not discard what the author typed.
        await AssertExplainedHelperCancelsWithoutEditingAsync(page);
        Assert.Equal(expected, await ScriptAsync(page));

        await editor.ClickAsync();
        await editor.PressAsync("ControlOrMeta+End");
        await editor.PressAsync("Shift+Home");
        Assert.Equal("SELECT 42 AS LearningCheck;", await page.EvaluateAsync<string>(
            "() => window.__STUDIO__.state.editorInstance.getSelection()"));
        var response = page.WaitForResponseAsync(response =>
            response.Url.EndsWith("/api/designer/run", StringComparison.Ordinal)
            && response.Request.Method == "POST");
        await page.Locator("[data-action='run-selected']").ClickAsync();
        Assert.True((await response).Ok);
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.runActive === false",
            null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        await AssertLearningResultAsync(page);
        return expected;
    }

    private static async Task AssertLearningResultAsync(IPage page)
    {
        var result = await page.EvaluateAsync<JsonElement>(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace.find(item => item.type === 'results')");
        Assert.Equal("LearningCheck", Assert.Single(result.GetProperty("columns").EnumerateArray()).GetString());
        Assert.Equal(42m, Assert.Single(result.GetProperty("rows").EnumerateArray()).GetProperty("LearningCheck").GetDecimal());
    }

    private static async Task AssertExplainedHelperCancelsWithoutEditingAsync(IPage page)
    {
        var before = await ScriptAsync(page);
        await page.Locator("[data-workflow-step='palette']").ClickAsync();
        await page.Locator("[data-dialog-action='build']").ClickAsync();
        var preview = page.Locator(".etlsql-studio-sql-preview").First;
        await preview.WaitForAsync();
        Assert.Contains("CREATE VISUAL", await preview.Locator("pre").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(await preview.Locator(".etlsql-studio-sql-explains").TextContentAsync()));
        await page.Locator("[data-dialog-action='cancel']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => { const backdrop = document.querySelector('[data-modal-backdrop]'); return !backdrop || backdrop.hidden; }");
        Assert.Equal(before, await ScriptAsync(page));
    }

    private static Task<string> ScriptAsync(IPage page) => page.EvaluateAsync<string>(
        "() => window.__STUDIO__.state.editorInstance.getValue()");

    private static string NormalizeEndings(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private async Task<int> SignInAsPublisherAsync(IPage page)
    {
        var name = $"studio_author_{Guid.NewGuid():N}"[..28];
        var password = $"Studio!9{Guid.NewGuid():N}";
        int userId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var user = new PortalUser
            {
                UserName = name,
                Email = $"{name}@example.invalid",
                IsActive = true,
                MustChangePassword = false
            };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Publisher")).Succeeded);
            Assert.Equal("Publisher", Assert.Single(await users.GetRolesAsync(user)));
            userId = user.Id;
        }
        await page.GotoAsync("/login.html");
        await page.FillAsync("#username", name);
        await page.FillAsync("#password", password);
        await page.ClickAsync("#loginBtn");
        await page.WaitForURLAsync("**/index.html");
        return userId;
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

    private async Task<int> CreateReportAsync(IPage page, string alias, int authorId)
    {
        var folderId = await CreateWritableFolderAsync(authorId);
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
                    CREATE CONNECTION {alias} AS MOCKDB('SHARED:{alias}');
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

    private async Task<int> CreateWritableFolderAsync(int authorId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder
        {
            Name = $"Journey {suffix}",
            Path = $"/Journey-{suffix}",
            OwnerId = authorId
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
