using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Drives every UI-sandbox story and fixture through a real browser.
///
/// <para>The sandbox already imports the <b>canonical</b> component sources directly, which is what
/// makes it worth automating: mounting a story exercises the same file the Portal ships, without a
/// Portal, a database, or a login. It has only ever been run by a person clicking through it, so a
/// story that throws on mount stays broken until someone happens to open that fixture — and the
/// fixtures people open least are the failure states, which is precisely where a rendering bug is
/// least likely to be noticed and most likely to matter.</para>
///
/// <para>The assertions are deliberately shallow — mounts, does not throw, does not overflow. A
/// sandbox test that asserted on rendered content would duplicate the component's own tests and
/// break every time the fixtures changed, which would end with it being deleted.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class SandboxStoryTests(PortalBrowserFixture fixture) : IAsyncLifetime
{
    private IHost? host;
    private string baseUrl = "";

    /// <summary>
    /// Serves the repository root, because the sandbox's ES module imports reach back into
    /// <c>src/</c> with absolute paths — the same arrangement <c>tools/ui-sandbox/serve.ps1</c> uses.
    /// </summary>
    public async Task InitializeAsync()
    {
        var root = RepoRoot();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var provider = new PhysicalFileProvider(root);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            ServeUnknownFileTypes = true,
        });

        var mapsPath = Path.Combine(root, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "maps");
        if (Directory.Exists(mapsPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(mapsPath),
                RequestPath = "/maps",
                ServeUnknownFileTypes = true,
            });
        }

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

    [Fact]
    public async Task EveryStoryAndFixture_MountsWithoutThrowing()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.WaitForSelectorAsync(".story-link", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

        var stories = await page.EvaluateAsync<StoryInfo[]>("""
            async () => {
              const { stories } = await import('/tools/ui-sandbox/stories/index.js');
              return stories.map(s => ({
                id: s.id,
                title: s.title,
                fixtures: (Array.isArray(s.fixtures)
                  ? s.fixtures
                  : (s.fixtures && typeof s.fixtures === 'object' ? Object.entries(s.fixtures).map(([k, v]) => typeof v === 'object' ? { id: v?.id ?? k } : { id: k }) : [{ id: '' }])
                ).map(f => (typeof f === 'object' ? (f?.id ?? '') : String(f))),
              }));
            }
            """);

        Assert.NotEmpty(stories);
        var failures = new List<string>();

        foreach (var story in stories)
        {
            for (var i = 0; i < story.Fixtures.Length; i++)
            {
                var before = session.PageErrors.Count;
                var consoleBefore = session.ConsoleErrors.Count;
                var failedReqsBefore = session.FailedRequests.Count;

                // Clicking through the shell rather than calling mount() directly: the shell's
                // dispose/re-mount path is part of what breaks, and a story that leaks between
                // fixtures only shows up when one is mounted after another.
                await DismissOpenOverlaysAsync(page);
                await page.ClickAsync($"button.story-link[data-story-id='{story.Id}']");
                await page.WaitForTimeoutAsync(150);

                if (story.Fixtures.Length > 1 && story.Fixtures[i].Length > 0)
                {
                    await page.SelectOptionAsync("#fixtureSel", story.Fixtures[i]);
                    await page.WaitForTimeoutAsync(250);
                }

                var label = $"{story.Id}/{(story.Fixtures[i].Length == 0 ? "(default)" : story.Fixtures[i])}";

                var thrown = session.PageErrors.Skip(before).ToList();
                if (thrown.Count > 0)
                    failures.Add($"{label} threw: {string.Join(" | ", thrown)}");

                var logged = session.ConsoleErrors.Skip(consoleBefore).ToList();
                if (logged.Count > 0)
                {
                    var failedReqs = session.FailedRequests.Skip(failedReqsBefore).ToList();
                    failures.Add($"{label} logged: {string.Join(" | ", logged)}" + (failedReqs.Count > 0 ? $" (Failed requests: {string.Join(", ", failedReqs)})" : ""));
                }

                if (await page.Locator("#stage *").CountAsync() == 0)
                    failures.Add($"{label} mounted nothing into the stage.");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} sandbox story fixtures failed to mount cleanly:\n  "
            + string.Join("\n  ", failures));
    }

    [Fact]
    public async Task Sidebar_CollapseAndExpand_TogglesPanelVisibilityAndResizesStage()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.WaitForSelectorAsync(".story-link", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

        var sidebar = page.Locator("#sidebar");
        var stage = page.Locator("#stage");
        var toggleBtn = page.Locator("#sidebarToggleBtn");
        var collapseBtn = page.Locator("#sidebarCollapseBtn");
        var searchInput = page.Locator("#searchInput");

        // 1. Initially expanded
        Assert.True(await sidebar.IsVisibleAsync());
        Assert.Equal("true", await toggleBtn.GetAttributeAsync("aria-expanded"));
        var initialStageBox = await stage.BoundingBoxAsync();
        Assert.NotNull(initialStageBox);

        // 2. Collapse via sidebar button
        await collapseBtn.ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.False(await sidebar.IsVisibleAsync());
        Assert.Equal("false", await toggleBtn.GetAttributeAsync("aria-expanded"));
        var collapsedStageBox = await stage.BoundingBoxAsync();
        Assert.NotNull(collapsedStageBox);
        Assert.True(collapsedStageBox.Width > initialStageBox.Width + 200,
            $"Stage width ({collapsedStageBox.Width}) did not expand when sidebar collapsed (was {initialStageBox.Width})");

        // 3. Expand via header toggle button
        await toggleBtn.ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.True(await sidebar.IsVisibleAsync());
        Assert.Equal("true", await toggleBtn.GetAttributeAsync("aria-expanded"));

        // 4. Toggle with Ctrl+B shortcut
        await page.Keyboard.PressAsync("Control+b");
        await page.WaitForTimeoutAsync(100);
        Assert.False(await sidebar.IsVisibleAsync());

        // 5. Toggle with [ shortcut
        await page.Keyboard.PressAsync("[");
        await page.WaitForTimeoutAsync(100);
        Assert.True(await sidebar.IsVisibleAsync());

        // 6. Search hotkey '/' auto-expands sidebar when collapsed
        await page.Keyboard.PressAsync("Control+b");
        await page.WaitForTimeoutAsync(100);
        Assert.False(await sidebar.IsVisibleAsync());
        await page.Keyboard.PressAsync("/");
        await page.WaitForTimeoutAsync(100);
        Assert.True(await sidebar.IsVisibleAsync());
        Assert.True(await searchInput.EvaluateAsync<bool>("el => document.activeElement === el"));

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    /// <summary>
    /// The Report Builder's bookmark panel, driven in a real browser against the canonical
    /// <c>designer.js</c>.
    ///
    /// <para>Bookmarks reached the builder as data before they reached it as a surface: the designer
    /// state round-tripped them through parse/patch while offering no way to see or change one. The
    /// property asserted here is that the panel lists what the script declares and that the
    /// at-most-one-default rule the parser enforces is enforced in the editor too — authoring a second
    /// default in the builder would produce a script that no longer parses.</para>
    /// </summary>
    [Fact]
    public async Task DesignerBookmarkPanel_ListsDeclaredBookmarksAndKeepsOneDefault()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='designer']");
        await page.WaitForSelectorAsync("#dsgn-bookmark-list", new PageWaitForSelectorOptions { Timeout = 30_000 });

        var rows = page.Locator("#dsgn-bookmark-list .etlsql-dsgn-ds-block");
        Assert.Equal(2, await rows.CountAsync());
        Assert.Contains("West, Q4", await rows.Nth(0).InnerTextAsync());

        // The report default is marked, and exactly one of them carries it.
        Assert.Equal(1, await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_0']").CountAsync());
        Assert.Equal("★", (await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_0']").InnerTextAsync()).Trim());
        Assert.Equal("☆", (await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_1']").InnerTextAsync()).Trim());

        // Promoting the second must demote the first rather than leaving two defaults behind.
        await page.ClickAsync("#dsgn-bookmark-list [data-bmdefault='bm_1']");
        await page.WaitForTimeoutAsync(150);
        Assert.Equal("☆", (await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_0']").InnerTextAsync()).Trim());
        Assert.Equal("★", (await page.Locator("#dsgn-bookmark-list [data-bmdefault='bm_1']").InnerTextAsync()).Trim());

        // Deleting one leaves the other; the panel is the only place a bookmark can be removed
        // without hand-editing the script.
        await page.ClickAsync("#dsgn-bookmark-list [data-bmid='bm_0']");
        await page.WaitForTimeoutAsync(150);
        Assert.Equal(1, await rows.CountAsync());
        Assert.DoesNotContain("West, Q4", await page.Locator("#dsgn-bookmark-list").InnerTextAsync());

        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Proves that transient syntax errors in split-screen script editing retain the last valid
    /// canvas state instead of clearing or corrupting the designer canvas, while displaying
    /// the diagnostic badge. Restoring valid script clears the badge and updates state.
    /// </summary>
    [Fact]
    public async Task Designer_TransientSyntaxError_RetainsCanvasCardsAndDisplaysDiagnosticBadge()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='designer']");
        await page.WaitForSelectorAsync("#fixtureSel", new PageWaitForSelectorOptions { Timeout = 30_000 });

        // 1. Mount the transient syntax resilience fixture
        await page.SelectOptionAsync("#fixtureSel", "syntax-resilience");
        await page.WaitForTimeoutAsync(300);

        // 2. Diagnostic badge must be displayed with syntax warning
        var badge = page.Locator("#dsgn-diagnostic-badge");
        Assert.Equal(1, await badge.CountAsync());
        Assert.True(await badge.IsVisibleAsync());
        Assert.Contains("Script syntax warning", await badge.InnerTextAsync());

        // 3. Canvas visual cards must remain intact (3 cards from sample state)
        var cards = page.Locator(".etlsql-dsgn-visual-card");
        Assert.Equal(3, await cards.CountAsync());

        // 4. Recover by applying a valid script
        await page.EvaluateAsync("""
            async () => {
                const stage = document.querySelector('#stage');
                if (stage && stage.__designerInstance) {
                    await stage.__designerInstance.applyScriptText(`
                        SELECT 'A' AS Date, 'V' AS Vendor, 100 AS total INTO #sales;
                        CREATE VISUAL salesBar AS BAR ( TITLE = 'Recovered Bar', SOURCE = #sales, MAPPINGS (X = Date, Y = total) );
                        CREATE PAGE Overview AS DASHBOARD ( LAYOUT ( STRUCTURE = 'A', MAP ('A' = salesBar) ) );
                    `);
                }
            }
        """);
        await page.WaitForTimeoutAsync(200);

        // 5. Diagnostic badge must be hidden after recovery
        Assert.False(await badge.IsVisibleAsync());
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Proves that CUSTOM visual cards render dedicated Grammar-of-Graphics controls
    /// in the properties panel and live SVG GoG layered mark preview on the canvas.
    /// </summary>
    [Fact]
    public async Task Designer_CustomChartVisual_RendersGrammarOfGraphicsControlsAndPreview()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='designer']");
        await page.WaitForSelectorAsync("#fixtureSel", new PageWaitForSelectorOptions { Timeout = 30_000 });

        // 1. Mount the custom-chart fixture
        await page.SelectOptionAsync("#fixtureSel", "custom-chart");
        await page.WaitForTimeoutAsync(300);

        // 2. Visual card on canvas must be rendered with SVG preview
        var card = page.Locator(".etlsql-dsgn-visual-card");
        Assert.Equal(1, await card.CountAsync());
        var svg = card.Locator("svg");
        Assert.True(await svg.CountAsync() >= 1);
        Assert.Contains("CUSTOM CHART", await card.InnerTextAsync());

        // 3. Click the visual to select and open Properties panel
        await card.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // 4. Grammar of Graphics (CHART) controls must be visible in properties panel
        var chartSection = page.Locator(".etlsql-dsgn-chart-editor-section");
        Assert.Equal(1, await chartSection.CountAsync());
        Assert.True(await chartSection.IsVisibleAsync());

        var coordSelect = page.Locator("#pp-chart-coord");
        Assert.True(await coordSelect.IsVisibleAsync());
        Assert.Equal("CARTESIAN", await coordSelect.InputValueAsync());

        var chartCode = page.Locator("#pp-chart-code");
        Assert.True(await chartCode.IsVisibleAsync());
        var codeVal = await chartCode.InputValueAsync();
        Assert.Contains("COORDINATE (TYPE = CARTESIAN)", codeVal);
        Assert.Contains("bars = RECT", codeVal);

        // 5. Modify coordinate to POLAR
        await coordSelect.SelectOptionAsync("POLAR");
        await page.WaitForTimeoutAsync(200);
        var updatedCode = await chartCode.InputValueAsync();
        Assert.Contains("COORDINATE (TYPE = POLAR)", updatedCode);

        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Proves that HTML visual cards render dedicated Constrained HTML Component controls
    /// in the properties panel and live sanitized preview on the canvas.
    /// </summary>
    [Fact]
    public async Task Designer_ConstrainedHtmlVisual_RendersHtmlComponentControlsAndPreview()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='designer']");
        await page.WaitForSelectorAsync("#fixtureSel", new PageWaitForSelectorOptions { Timeout = 30_000 });

        // 1. Mount the custom-html fixture
        await page.SelectOptionAsync("#fixtureSel", "custom-html");
        await page.WaitForTimeoutAsync(300);

        // 2. Visual card on canvas must be rendered with HTML preview
        var card = page.Locator(".etlsql-dsgn-visual-card");
        Assert.Equal(1, await card.CountAsync());
        var htmlPreview = card.Locator(".etlsql-html-visual-preview");
        Assert.Equal(1, await htmlPreview.CountAsync());
        Assert.Contains("CPU: CpuPercent", await htmlPreview.EvaluateAsync<string>(
            "element => element.shadowRoot?.textContent || ''"));

        // 3. Click the visual to select and open Properties panel
        await card.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // 4. Constrained HTML Component controls must be visible in properties panel
        var htmlSection = page.Locator(".etlsql-dsgn-html-editor-section");
        Assert.Equal(1, await htmlSection.CountAsync());
        Assert.True(await htmlSection.IsVisibleAsync());

        var modeSelect = page.Locator("#pp-html-mode");
        Assert.True(await modeSelect.IsVisibleAsync());
        Assert.Equal("REPEATER", await modeSelect.InputValueAsync());

        var templateInput = page.Locator("#pp-html-template");
        Assert.True(await templateInput.IsVisibleAsync());
        var templateVal = await templateInput.InputValueAsync();
        Assert.Contains("<article class=\"node-card\">", templateVal);
        Assert.Contains("{{HostName}}", templateVal);

        var styleInput = page.Locator("#pp-html-style");
        Assert.True(await styleInput.IsVisibleAsync());
        Assert.Contains(".node-card", await styleInput.InputValueAsync());

        var fallbackInput = page.Locator("#pp-html-fallback");
        Assert.True(await fallbackInput.IsVisibleAsync());
        Assert.Contains("Node: {{HostName}}", await fallbackInput.InputValueAsync());

        // 5. Modify mode to SINGLE
        await modeSelect.SelectOptionAsync("SINGLE");
        await page.WaitForTimeoutAsync(200);
        Assert.Equal("SINGLE", await modeSelect.InputValueAsync());

        // 6. Hostile authored markup fails closed in the live preview and cannot execute.
        await page.EvaluateAsync("() => window.__designerHtmlScriptExecuted = false");
        await templateInput.FillAsync(
            "<article>safe</article><img src='x' onerror='window.__designerHtmlScriptExecuted=true'>");
        await templateInput.PressAsync("Tab");
        await page.Locator(".etlsql-html-preview-error").WaitForAsync();
        Assert.False(await page.EvaluateAsync<bool>("() => window.__designerHtmlScriptExecuted"));
        Assert.Equal(0, await htmlPreview.Locator("img").CountAsync());

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionWizard_CanonicalComponent_CoversSqlFilesDiagnosticsSecurityAndThemes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
              document.body.innerHTML = '<div id="canonical-connection-wizard"></div>';
              const { createConnectionWizard } = await import('/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/connection-wizard.js');
              createConnectionWizard({
                host: document.getElementById('canonical-connection-wizard'),
                schemas: [
                  {
                    connectorType: 'MSSQL', description: 'SQL Server', isFileBased: false,
                    options: [
                      { name: 'SERVER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'sql.test' },
                      { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'warehouse' },
                      { name: 'PASSWORD', type: 3, category: 'Auth', mutuallyExclusiveGroup: 'Credentials' }
                    ]
                  },
                  {
                    connectorType: 'FLATFILE', description: 'Delimited file', isFileBased: true,
                    options: [
                      { name: 'PATH', type: 4, isMandatory: true, category: 'Basic', defaultValue: 'uploads/default.csv' },
                      { name: 'HEADER', type: 2, category: 'Basic', defaultValue: 'ON' }
                    ]
                  },
                  {
                    connectorType: 'MOCKDB', description: 'Generated test data', isFileBased: false,
                    options: []
                  }
                ],
                secrets: ['WAREHOUSE_PASSWORD'],
                gateways: [{ id: 'gateway-a', name: 'gateway-a', status: 'Active', region: 'On-Premises' }],
                stagedFiles: [{ name: 'sales.csv', path: 'uploads/sales.csv' }],
                existingNames: ['existing_connection'],
                onTest: async req => req.target.startsWith('fail')
                  ? { succeeded: false, error: 'Provider rejected the endpoint.', steps: [{ layer: 'TCP', status: 'failed', detail: 'Connection refused.' }] }
                  : { succeeded: true, steps: [
                      { layer: 'POLICY', status: 'ok', detail: 'Allowed.' },
                      { layer: 'DNS', status: 'ok', detail: 'Resolved.' },
                      { layer: 'TCP', status: 'ok', detail: 'Connected.' },
                      { layer: 'AUTH', status: 'ok', detail: 'Authenticated.' }
                    ] }
              });
            }
            """);

        await page.Locator("#etlsql-cw-alias-input").FillAsync("warehouse_reader");
        await page.Locator("#etlsql-cw-secret-key").FillAsync("WAREHOUSE_PASSWORD");
        await page.Locator("#etlsql-cw-gateway-select").SelectOptionAsync("gateway-a");

        var sql = await page.Locator(".etlsql-cw-sql-box").InnerTextAsync();
        Assert.Contains("CREATE CONNECTION warehouse_reader AS MSSQL", sql);
        Assert.Contains("PASSWORD = SECRET:WAREHOUSE_PASSWORD", sql);
        Assert.Contains("GATEWAY = 'gateway-a'", sql);

        await page.Locator("#etlsql-cw-test-btn").ClickAsync();
        await page.Locator(".etlsql-cw-diag-badge.badge-ok").WaitForAsync();
        Assert.Equal(4, await page.Locator(".etlsql-cw-step-item").CountAsync());

        await page.Locator("#etlsql-cw-opt-server").FillAsync("fail.internal");
        await page.Locator("#etlsql-cw-test-btn").ClickAsync();
        await page.Locator(".etlsql-cw-diag-badge.badge-fail").WaitForAsync();
        Assert.Contains("Provider rejected", await page.Locator(".etlsql-cw-diag-result").InnerTextAsync());

        await page.Locator("button[data-cat='testdata']").ClickAsync();
        Assert.Equal(1, await page.Locator("button[data-type='MOCKDB']").CountAsync());
        Assert.Equal(0, await page.Locator("button[data-type='MSSQL']").CountAsync());
        Assert.Contains("MOCKDB", await page.Locator(".etlsql-cw-sql-box").InnerTextAsync());

        await page.Locator("button[data-cat='files']").ClickAsync();
        await page.Locator("button[data-type='FLATFILE']").ClickAsync();
        await page.Locator("button[data-filepath='uploads/sales.csv']").ClickAsync();
        sql = await page.Locator(".etlsql-cw-sql-box").InnerTextAsync();
        Assert.Contains("FLATFILE", sql);
        Assert.Contains("PATH = 'uploads/sales.csv'", sql);

        await page.Locator("#etlsql-cw-opt-path").FillAsync("../secrets.sql");
        await page.Locator(".etlsql-cw-security-alert").WaitForAsync();
        Assert.True(await page.Locator("#etlsql-cw-submit-btn").IsDisabledAsync());

        foreach (var theme in new[] { "light", "dark" })
        {
            await page.EvaluateAsync("theme => document.documentElement.dataset.theme = theme", theme);
            var colors = await page.Locator(".etlsql-cw-modal").EvaluateAsync<string[]>(
                "el => [getComputedStyle(el).color, getComputedStyle(el).backgroundColor]");
            Assert.All(colors, color => Assert.DoesNotContain("rgba(0, 0, 0, 0)", color));
        }

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ConnectionWizard_DiscoversAndBindsApprovedGatewayResources()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
              document.body.innerHTML = '<div id="canonical-connection-wizard"></div>';
              const { createConnectionWizard } = await import('/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/connection-wizard.js');
              window.__savedEntry = null;
              window.__gatewayResourceFetches = 0;
              createConnectionWizard({
                host: document.getElementById('canonical-connection-wizard'),
                mode: 'admin',
                schemas: [
                  {
                    connectorType: 'MSSQL', description: 'SQL Server', isFileBased: false,
                    options: [
                      { name: 'SERVER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'sql.test' },
                      { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'warehouse' }
                    ]
                  },
                  {
                    connectorType: 'POSTGRES', description: 'PostgreSQL', isFileBased: false,
                    options: [
                      { name: 'HOST', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'pg.test' }
                    ]
                  }
                ],
                gateways: [{ id: 'corp-gw', name: 'corp-gw', status: 'Online', region: 'On-Premises' }],
                fetchGatewayResources: async (gwId) => {
                  window.__gatewayResourceFetches++;
                  return [
                  {
                    resourceId: 'finance-dw',
                    connectorType: 'MSSQL',
                    allowedOperations: 'Read, Write',
                    state: 'Approved',
                    isOnline: true,
                    lastSeenUtc: '2026-08-27T12:00:00Z'
                  },
                  {
                    resourceId: 'audit-pg',
                    connectorType: 'POSTGRES',
                    allowedOperations: 'Read',
                    state: 'Approved',
                    isOnline: true,
                    lastSeenUtc: '2026-08-27T12:05:00Z'
                  }
                  ];
                },
                onSave: async (entry) => {
                  window.__savedEntry = entry;
                }
              });
            }
            """);

        await page.Locator("#etlsql-cw-alias-input").FillAsync("corp_finance_conn");
        await page.Locator("#etlsql-cw-gateway-select").SelectOptionAsync("corp-gw");

        // Verify resource picker appeared with approved resources
        var resourceCard = page.Locator(".etlsql-cw-resource-card[data-resource-id='finance-dw']");
        await resourceCard.WaitForAsync();
        Assert.Contains("finance-dw", await resourceCard.InnerTextAsync());
        Assert.Contains("Approved", await resourceCard.InnerTextAsync());
        Assert.Contains("Read, Write", await resourceCard.InnerTextAsync());

        // Click to bind resource
        await resourceCard.ClickAsync();
        await page.Locator(".etlsql-cw-gateway-bound-banner").WaitForAsync();
        Assert.Contains("Gateway Resource Bound", await page.Locator(".etlsql-cw-gateway-bound-banner").InnerTextAsync());

        // Verify preview SQL
        var preview = await page.Locator(".etlsql-cw-sql-box").InnerTextAsync();
        Assert.Contains("Gateway: corp-gw", preview);
        Assert.Contains("Resource: finance-dw", preview);

        // Clear only the resource binding; keep the selected Gateway available for another choice.
        await page.Locator("[data-unbind-gateway-resource]").ClickAsync();
        Assert.Equal("corp-gw", await page.Locator("#etlsql-cw-gateway-select").InputValueAsync());
        Assert.Equal(0, await page.Locator(".etlsql-cw-gateway-bound-banner").CountAsync());
        Assert.Contains("SERVER", await page.Locator(".etlsql-cw-content").InnerTextAsync());

        // Manual refresh re-runs discovery without changing the selected Gateway.
        await page.Locator("[data-refresh-gateway-resources]").ClickAsync();
        await page.Locator(".etlsql-cw-resource-card[data-resource-id='finance-dw']").WaitForAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("() => window.__gatewayResourceFetches"));
        await page.Locator(".etlsql-cw-resource-card[data-resource-id='finance-dw']").ClickAsync();

        // Submit and verify payload contains gateway binding without physical target
        await page.Locator("#etlsql-cw-submit-btn").ClickAsync();
        var savedEntry = await page.EvaluateAsync<System.Text.Json.JsonElement>("() => window.__savedEntry");
        Assert.Equal("corp_finance_conn", savedEntry.GetProperty("alias").GetString());
        Assert.Equal("MSSQL", savedEntry.GetProperty("connectorType").GetString());
        var gatewayObj = savedEntry.GetProperty("gateway");
        Assert.Equal("corp-gw", gatewayObj.GetProperty("gatewayId").GetString());
        Assert.Equal("finance-dw", gatewayObj.GetProperty("resourceId").GetString());

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ConnectionWizard_KeepsModalOpenWhenCatalogSaveFails()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
              document.body.innerHTML = '<div id="canonical-connection-wizard"></div>';
              const { createConnectionWizard } = await import('/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/connection-wizard.js');
              window.__saveAttempts = 0;
              createConnectionWizard({
                host: document.getElementById('canonical-connection-wizard'),
                mode: 'admin',
                schemas: [{ connectorType: 'MSSQL', description: 'SQL Server', options: [] }],
                gateways: [{ id: 'corp-gw', name: 'corp-gw', status: 'Online' }],
                fetchGatewayResources: async () => [{
                  resourceId: 'finance-dw', connectorType: 'MSSQL',
                  allowedOperations: 'Read', state: 'Approved', isOnline: true
                }],
                onSave: async () => {
                  window.__saveAttempts++;
                  throw new Error('sensitive upstream detail');
                }
              });
            }
            """);

        await page.Locator("#etlsql-cw-alias-input").FillAsync("corp_finance_conn");
        await page.Locator("#etlsql-cw-gateway-select").SelectOptionAsync("corp-gw");
        await page.Locator(".etlsql-cw-resource-card[data-resource-id='finance-dw']").ClickAsync();
        await page.Locator("#etlsql-cw-submit-btn").ClickAsync();

        var error = page.Locator(".etlsql-cw-save-error");
        await error.WaitForAsync();
        Assert.Contains("could not be saved", await error.InnerTextAsync());
        Assert.DoesNotContain("sensitive upstream detail", await error.InnerTextAsync());
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__saveAttempts"));
        Assert.Equal(1, await page.Locator(".etlsql-cw-modal").CountAsync());
        Assert.True(await page.Locator("#etlsql-cw-submit-btn").IsEnabledAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task OperationsAdmin_ReconcilesAmbiguousGatewayWriteWithImmutableEvidence()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
              document.body.innerHTML = '<div id="operations-admin"></div>';
              const { createOperationsAdmin } = await import('/src/ETL-SQL.Portal/wwwroot/js/operations-admin.js');
              let item = {
                id: 7, operationId: 'op-write-7f3a', tenantId: 'tenant-a', gatewayId: 'hq-gateway',
                resourceId: 'corp-sql-sales', correlationId: 'corr-98d1',
                executedAtUtc: '2026-08-27T14:42:00Z', state: 'Acknowledged', priority: 'High',
                owner: 'database-operations', resolution: null, version: 2,
                events: [
                  { eventType: 'Detected', actor: 'Gateway', note: 'Ambiguous outcome.', createdAtUtc: '2026-08-27T14:42:00Z' },
                  { eventType: 'Acknowledged', actor: 'admin', note: 'Investigating.', createdAtUtc: '2026-08-27T14:45:00Z' }
                ]
              };
              const empty = async () => [];
              const admin = createOperationsAdmin({
                host: document.getElementById('operations-admin'),
                adminApi: {
                  operationalMetrics: async () => ({ queuedExecutions: 0, activeExecutions: 1, executionCap: 8, auditOutboxPending: 0, auditOutboxFailed: 0, auditOutboxOldestPendingAgeSeconds: 0, windowHours: 24, recentExecutionFailures: 0, recentExecutions: 1, staleDatasets: 0, datasetStorageBytes: 0, securityEventPending: 0, securityEventFailed: 0 }),
                  fleetStatus: async () => ({ status: 'Healthy', environment: 'Test', storage: 'Ready', inventory: { nodeId: 'test-node', installedVersion: 'test', upgradeReadiness: { ready: true, findings: [] } } }),
                  gatewayAmbiguousWrites: async () => [item],
                  pendingAccessRequests: empty, listServiceAccounts: empty, anonymousReportAccess: empty,
                  listAdminServices: empty, listUsers: empty,
                  resolveGatewayAmbiguousWrite: async (id, body) => {
                    window.__resolutionRequest = { id, body };
                    item = { ...item, state: 'Resolved', resolution: body.resolution, version: item.version + 1,
                      events: [...item.events, { eventType: 'Resolved', actor: 'admin', note: body.note,
                        evidenceReference: body.evidenceReference, resolution: body.resolution,
                        createdAtUtc: '2026-08-27T15:00:00Z' }] };
                    return item;
                  }
                }
              });
              await admin.load();
            }
            """);

        Assert.Contains("Retry blocked", await page.Locator("#ops-signals").InnerTextAsync());
        Assert.Contains("op-write-7f3a", await page.Locator("#ops-ambiguous-writes").InnerTextAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Review case" }).ClickAsync();
        Assert.Contains("Immutable event history", await page.Locator("#ops-modal-body").InnerTextAsync());
        Assert.Contains("Detected", await page.Locator("#ops-modal-body").InnerTextAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Record verified outcome" }).ClickAsync();
        await page.Locator("#ops-case-resolution").SelectOptionAsync("confirmed committed");
        await page.Locator("#ops-case-evidence").FillAsync("INC-2042/query-17");
        await page.Locator("#ops-case-note").FillAsync("Target row and transaction log verified externally.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Record outcome" }).ClickAsync();

        await page.Locator("#ops-ambiguous-writes").GetByText("Resolved", new() { Exact = true }).WaitForAsync();
        var request = await page.EvaluateAsync<System.Text.Json.JsonElement>("() => window.__resolutionRequest");
        Assert.Equal(7, request.GetProperty("id").GetInt32());
        Assert.Equal("confirmed committed", request.GetProperty("body").GetProperty("resolution").GetString());
        Assert.Equal("INC-2042/query-17", request.GetProperty("body").GetProperty("evidenceReference").GetString());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_ScriptPane_KeepsHandAuthoredTextAndInsertedConnections()
    {
        // Typing into the script pane used to erase itself. The canvas regenerates its script from
        // the design state alone, and updating the canvas *from* the editor let that regeneration
        // run and overwrite the author's text ~800ms later -- so anything the design state does not
        // model, most visibly a CREATE CONNECTION, vanished as it was typed. That made the
        // Connection Wizard look broken too: its inserted statement disappeared moments later.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        var survived = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            async () => {
                const studio = window.__STUDIO_INSTANCE__;
                const editor = studio.state.editorInstance;
                const marker = 'CREATE CONNECTION demo AS MOCKDB();';
                editor.setValue(marker + String.fromCharCode(10) + editor.getValue());

                // Long enough for both debounces to fire: editor->canvas (400ms) and the
                // canvas->script regeneration (400ms) that used to clobber the buffer.
                await new Promise(resolve => setTimeout(resolve, 2000));
                return { keptConnection: editor.getValue().includes(marker) };
            }
            """);

        Assert.True(survived.GetProperty("keptConnection").GetBoolean(),
            "The script pane discarded a hand-authored CREATE CONNECTION after the canvas re-rendered.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_CanvasEdits_StillWriteBackToTheScript()
    {
        // The guard above must stay surgical: suppressing the write-back only while ingesting
        // script text, never for a genuine canvas edit. Without this, fixing the clobber would
        // silently sever canvas-to-code sync.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        var result = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            async () => {
                const studio = window.__STUDIO_INSTANCE__;
                const editor = studio.state.editorInstance;
                const before = editor.getValue();
                studio.state.designerInstance.addVisual('BAR');
                await new Promise(resolve => setTimeout(resolve, 2000));
                return { grew: editor.getValue().length > before.length };
            }
            """);

        Assert.True(result.GetProperty("grew").GetBoolean(),
            "A canvas edit no longer writes back to the script.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_DatasetEdits_RefreshTheRightSampleAndIgnoreStaleOrInvalidResults()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        var result = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            async () => {
                const studio = window.__STUDIO_INSTANCE__;
                const editor = studio.state.editorInstance;
                window.__STUDIO_API_DELAY__ = ({ url, body }) =>
                    url.endsWith('/api/designer/data-sample') && body?.script?.includes('stale_metric') ? 1400 : 0;

                editor.setValue(editor.getValue().replace(
                    'SELECT order_date, total_amount, region',
                    'SELECT order_date, total_amount AS stale_metric, region'));
                await new Promise(resolve => setTimeout(resolve, 650));
                editor.setValue(editor.getValue().replace('stale_metric', 'fresh_metric'));
                await new Promise(resolve => setTimeout(resolve, 2200));

                const document = studio.state.documents.find(item => item.id === 'doc-report');
                const sampleRequestsBeforeInvalid = window.__STUDIO_API_REQUESTS__.filter(request =>
                    request.url.endsWith('/api/designer/data-sample') && request.body?.sourceKind === 'dataset').length;
                const validSnapshot = JSON.stringify(document.studioContext.snapshot);
                const visualCount = document.querySelectorAll?.('.designer-card')?.length
                    ?? window.document.querySelectorAll('.designer-card').length;

                editor.setValue(editor.getValue() + String.fromCharCode(10) + '>>> INVALID <<<');
                await new Promise(resolve => setTimeout(resolve, 1100));

                const sampleRequestsAfterInvalid = window.__STUDIO_API_REQUESTS__.filter(request =>
                    request.url.endsWith('/api/designer/data-sample') && request.body?.sourceKind === 'dataset').length;
                return {
                    sampleRequestsBeforeInvalid,
                    sampleRequestsAfterInvalid,
                    columns: document.studioContext.snapshot.columns,
                    snapshotPreserved: JSON.stringify(document.studioContext.snapshot) === validSnapshot,
                    visualCount,
                    visualCountAfterInvalid: window.document.querySelectorAll('.designer-card').length,
                };
            }
            """);

        Assert.Equal(2, result.GetProperty("sampleRequestsBeforeInvalid").GetInt32());
        Assert.Equal(2, result.GetProperty("sampleRequestsAfterInvalid").GetInt32());
        var columns = result.GetProperty("columns").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("fresh_metric", columns);
        Assert.DoesNotContain("stale_metric", columns);
        Assert.True(result.GetProperty("snapshotPreserved").GetBoolean());
        Assert.Equal(result.GetProperty("visualCount").GetInt32(), result.GetProperty("visualCountAfterInvalid").GetInt32());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_SandboxStory_DoesNotOfferAHostExitAction()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        Assert.Equal(0, await page.Locator("[data-action='exit']").CountAsync());
        Assert.Null(await page.EvaluateAsync<object?>("() => window.__STUDIO_EXIT_REQUESTS__"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_ConnectionWizard_OffersMockDbUnderTestData()
    {
        // MOCKDB is the only connector a new author can use with no database, and it backs Studio
        // Home's "Start with sample data". The wizard falls back to a built-in connector list when
        // discovery fails, and that list had no MOCKDB -- so the zero-dependency on-ramp went
        // missing exactly when the environment could not reach a real server.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        await page.ClickAsync(".etlsql-studio-rail-btn[data-activity='catalog']");
        await page.ClickAsync("[data-action='wizard']");
        await page.Locator(".etlsql-cw-overlay").WaitForAsync();
        await page.ClickAsync(".etlsql-cw-overlay [data-cat='testdata']");

        await page.Locator(".etlsql-cw-overlay [data-type='MOCKDB']").WaitForAsync();
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_ConnectionWizard_PrefillsAUsableAliasAndBlocksInvalidOnes()
    {
        // The alias was marked required but nothing enforced it, and generateSql substitutes the
        // literal `<alias>` placeholder when it is blank -- so confirming the dialog wrote
        // `CREATE CONNECTION <alias> AS ...` into the script, which does not parse. It is also the
        // one field a newcomer has no basis to fill in before knowing what the connection is for,
        // so it is prefilled with a free name rather than left as a wall.
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        await page.ClickAsync(".etlsql-studio-rail-btn[data-activity='catalog']");
        await page.ClickAsync("[data-action='wizard']");
        await page.Locator(".etlsql-cw-overlay").WaitForAsync();

        var alias = page.Locator("#etlsql-cw-alias-input");
        var submit = page.Locator("#etlsql-cw-submit-btn");

        // Prefilled and immediately usable.
        Assert.False(string.IsNullOrWhiteSpace(await alias.InputValueAsync()));
        Assert.True(await submit.IsEnabledAsync());

        // The generated SQL shows a real alias, never the placeholder.
        Assert.DoesNotContain("<alias>", await page.Locator(".etlsql-cw-overlay").InnerTextAsync(), StringComparison.Ordinal);

        // Cleared: blocked.
        await alias.FillAsync("");
        Assert.True(await submit.IsDisabledAsync());

        // Not an identifier: blocked, and the hint says why rather than just "required".
        await alias.FillAsync("9 bad-name");
        Assert.True(await submit.IsDisabledAsync());
        Assert.Contains("must start with a letter",
            await page.Locator(".etlsql-cw-missing-hint").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        // Valid again: re-enabled.
        await alias.FillAsync("good_name");
        Assert.True(await submit.IsEnabledAsync());

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Studio_PipelineCanvasUsesEngineDagAndPreservesScriptBytes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");

        var status = page.Locator("[data-dag-status]");
        await status.WaitForAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        var sourceBefore = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content");
        var dagNodes = page.Locator("[data-dag-node]");
        // Six projected stages plus the one labelled task the sample script declares.
        Assert.Equal(7, await dagNodes.CountAsync());
        Assert.Contains("IF", await page.Locator("[data-dag-node='quality_branch']").InnerTextAsync());
        Assert.Contains("ASSERT", await page.Locator("[data-dag-node='quality_gate']").InnerTextAsync());

        var canvasBox = await page.Locator(".etlsql-dag-canvas").BoundingBoxAsync();
        Assert.NotNull(canvasBox);
        for (var i = 0; i < await dagNodes.CountAsync(); i++)
        {
            var nodeBox = await dagNodes.Nth(i).BoundingBoxAsync();
            Assert.NotNull(nodeBox);
            Assert.True(nodeBox!.X >= canvasBox!.X - 1 && nodeBox.Y >= canvasBox.Y - 1);
            Assert.True(nodeBox.X + nodeBox.Width <= canvasBox.X + canvasBox.Width + 1);
            Assert.True(nodeBox.Y + nodeBox.Height <= canvasBox.Y + canvasBox.Height + 1);
        }

        var trueEdge = page.Locator("[data-dag-source='quality_branch'][data-dag-target='#ready_sales'][data-dag-label='TRUE']");
        var elseEdge = page.Locator("[data-dag-source='quality_branch'][data-dag-target='#quarantine_sales'][data-dag-label='ELSE']");
        await trueEdge.WaitForAsync();
        await elseEdge.WaitForAsync();

        await page.Locator("[data-dag-node='quality_branch']").ClickAsync();
        var sourceAfterNavigation = await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content");
        Assert.Equal(sourceBefore, sourceAfterNavigation);

        var requestScript = await page.EvaluateAsync<string>("""
            () => [...window.__STUDIO_API_REQUESTS__]
              .reverse()
              .find(request => request.url.endsWith('/api/designer/dag'))
              ?.body?.script
            """);
        Assert.Equal(sourceBefore, requestScript);

        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.setValue('>>> INVALID <<<')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Last valid flow')");
        Assert.Equal(7, await page.Locator("[data-dag-node]").CountAsync());
        Assert.Contains("Unexpected token", await status.InnerTextAsync());

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_PipelineTasks_AreEditableOnTheCanvasAndLeaveTheRestOfTheScriptAlone()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        string Script() => page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content").Result;

        var before = Script();

        // Only the labelled statement is editable. Everything else on the map is projection-only.
        var task = page.Locator("[data-task-key='load_orders']");
        await task.WaitForAsync();
        Assert.Equal(1, await page.Locator("[data-task-key]").CountAsync());
        Assert.True(await task.EvaluateAsync<bool>("element => element.draggable"));
        Assert.False(await page.Locator("[data-dag-node='quality_gate']").EvaluateAsync<bool>("element => element.draggable"));

        // The palette offers every kind that passed its emission gate, and none of them is a dead
        // control — a chip that cannot write its statement would be worse than no chip.
        Assert.Equal(7, await page.Locator("[data-task-kind]").CountAsync());
        Assert.Equal(0, await page.Locator("[data-task-kind][disabled]").CountAsync());

        // Renaming goes through the task editor, and writes only the label.
        await task.ClickAsync();
        await page.ClickAsync("[data-task-edit]");
        var labelField = page.Locator("[data-task-id]");
        await labelField.WaitForAsync();

        // The execution editor carries the shared query workbench, not a bare textarea: that is what
        // gives the SQL a task runs the same completions, diagnostics, run, and results as the script.
        await page.Locator("[data-workbench-run]").WaitForAsync();

        await labelField.FillAsync("load_orders_v2");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync(
            """() => !!document.querySelector("[data-task-key='load_orders_v2']")""");

        var renamed = Script();
        Assert.Equal(before.Replace("load_orders:", "load_orders_v2:", StringComparison.Ordinal), renamed);

        // A validation task is authored as fields, and writes one ASSERT under its label.
        await page.ClickAsync("[data-task-kind='validation']");
        await page.Locator("[data-task-field='condition']").WaitForAsync();
        await page.Locator("[data-task-id]").FillAsync("orders_arrived");
        await page.Locator("[data-task-field='condition']").FillAsync("(SELECT COUNT(*) FROM #ready_sales) > 0");
        await page.Locator("[data-task-field='message']").FillAsync("No clean sales rows were staged.");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-task-key]').length === 2");

        var added = Script();
        Assert.Contains("orders_arrived:", added, StringComparison.Ordinal);
        Assert.Contains("ASSERT (SELECT COUNT(*) FROM #ready_sales) > 0", added, StringComparison.Ordinal);
        Assert.StartsWith(renamed.TrimEnd(), added.TrimEnd()[..renamed.TrimEnd().Length], StringComparison.Ordinal);

        // A half-filled task is refused before anything is written, with a sentence about the field
        // that is missing rather than a parse error about syntax the author never typed.
        await page.ClickAsync("[data-task-kind='fileoperation']");
        await page.Locator("[data-task-field='source']").WaitForAsync();
        await page.Locator("[data-task-id]").FillAsync("half_filled");
        await page.ClickAsync("[data-dialog-action='save']");
        Assert.Contains("needed before this task can be written",
            await page.Locator("[data-dialog-body]").InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(added, Script());
        await page.ClickAsync("[data-dialog-action='cancel']");

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_PipelineDependencies_AreDeclaredInTheScriptAndNeverImplyConcurrency()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        string Script() => page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content").Result;

        // Two more tasks to depend on each other.
        foreach (var (id, condition) in new[] { ("fetch_rates", "1 = 1"), ("merge_all", "2 = 2") })
        {
            await page.ClickAsync("[data-task-kind='validation']");
            await page.Locator("[data-task-field='condition']").WaitForAsync();
            await page.Locator("[data-task-id]").FillAsync(id);
            await page.Locator("[data-task-field='condition']").FillAsync(condition);
            await page.Locator("[data-task-field='message']").FillAsync($"{id} failed.");
            await page.ClickAsync("[data-dialog-action='save']");
            await page.WaitForFunctionAsync(
                $$"""() => !!document.querySelector("[data-task-key='{{id}}']")""");
        }

        // Every editable card carries a connector handle; dragging it declares a dependency.
        Assert.Equal(3, await page.Locator("[data-task-connector]").CountAsync());

        await page.EvaluateAsync("""
            () => {
              const drag = (fromKey, toKey) => {
                const handle = document.querySelector(`[data-task-connector='${fromKey}']`);
                const target = document.querySelector(`[data-task-key='${toKey}']`);
                const dt = new DataTransfer();
                handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
                target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
              };
              drag('load_orders', 'merge_all');
            }
            """);
        await page.WaitForFunctionAsync(
            """() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content.includes('@after: load_orders')""");

        await page.EvaluateAsync("""
            () => {
              const handle = document.querySelector("[data-task-connector='fetch_rates']");
              const target = document.querySelector("[data-task-key='merge_all']");
              const dt = new DataTransfer();
              handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
              target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """);
        await page.WaitForFunctionAsync(
            """() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content.includes('@after: load_orders, fetch_rates')""");

        var joined = Script();

        // The join is one declaration line, and it says nothing about running anything concurrently:
        // concurrency in ETL-SQL is only ever a PARALLEL block, and the canvas wrote none.
        Assert.Equal(1, joined.Split("-- @after:", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("PARALLEL", joined, StringComparison.OrdinalIgnoreCase);

        // Both dependencies run before the task that waits for them.
        var mergeAt = joined.IndexOf("merge_all:", StringComparison.Ordinal);
        Assert.True(joined.IndexOf("load_orders:", StringComparison.Ordinal) < mergeAt);
        Assert.True(joined.IndexOf("fetch_rates:", StringComparison.Ordinal) < mergeAt);

        // The inspector lists both prerequisites, and one can be removed without touching the other.
        await page.Locator("[data-task-key='merge_all']").ClickAsync();
        await page.Locator("[data-task-disconnect]").First.WaitForAsync();
        Assert.Equal(2, await page.Locator("[data-task-disconnect]").CountAsync());
        Assert.Contains("Waits for all 2", await page.Locator("[data-task-inspector]").InnerTextAsync(), StringComparison.Ordinal);

        await page.ClickAsync("[data-task-disconnect='load_orders']");
        await page.WaitForFunctionAsync(
            """() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content.includes('@after: fetch_rates')""");

        var trimmed = Script();
        Assert.DoesNotContain("@after: load_orders", trimmed, StringComparison.Ordinal);
        Assert.Contains("load_orders:", trimmed, StringComparison.Ordinal);

        // A cycle is refused with a visible reason. A linear script can never execute one, so a
        // canvas that drew it would be claiming something the engine cannot do.
        await page.EvaluateAsync("""
            () => {
              const handle = document.querySelector("[data-task-connector='merge_all']");
              const target = document.querySelector("[data-task-key='fetch_rates']");
              const dt = new DataTransfer();
              handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
              target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """);
        await page.Locator(".etlsql-feedback-toast", new() { HasText = "cycle" }).First.WaitForAsync();
        Assert.Equal(trimmed, Script());

        // An edge carries a condition, and choosing one rewrites the declaration in place rather
        // than adding a second prerequisite naming the same task.
        await page.SelectOptionAsync("[data-task-edge='fetch_rates']", "onfailure");
        await page.WaitForFunctionAsync(
            """() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content.includes('@after: fetch_rates on failure')""");

        var conditional = Script();
        Assert.Equal(1, conditional.Split("-- @after:", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("PARALLEL", conditional, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    /// <summary>
    /// Control-flow containers: a task dropped into one goes inside it, and the block the canvas
    /// writes is the only thing in the script that means concurrency.
    /// </summary>
    [Fact]
    public async Task Studio_PipelineContainers_HoldTasksAndOnlyParallelMeansConcurrency()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('doc-etl')");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-dag-status]')?.textContent?.includes('Engine projection')");

        string Script() => page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content").Result;

        var before = Script();
        Assert.DoesNotContain("PARALLEL", before, StringComparison.OrdinalIgnoreCase);

        // A container is added empty and named, then filled by dragging into it.
        await page.ClickAsync("[data-task-kind='parallel']");
        await page.Locator("[data-task-id]").WaitForAsync();
        await page.Locator("[data-task-id]").FillAsync("load_all");
        await page.ClickAsync("[data-dialog-action='save']");
        await page.WaitForFunctionAsync("""() => !!document.querySelector("[data-task-key='load_all']")""");

        Assert.Contains("PARALLEL BEGIN", Script(), StringComparison.Ordinal);
        Assert.True(await page.Locator("[data-task-key='load_all']")
            .EvaluateAsync<bool>("element => element.classList.contains('is-container-task')"));

        // Dropping a task onto the container puts it inside — the gesture matches the picture.
        await page.EvaluateAsync("""
            () => {
              const source = document.querySelector("[data-task-key='load_orders']");
              const target = document.querySelector("[data-task-key='load_all']");
              const dt = new DataTransfer();
              source.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: dt }));
              target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """);
        await page.WaitForFunctionAsync(
            """() => /PARALLEL BEGIN[\s\S]*load_orders:/.test(window.__STUDIO_INSTANCE__.state.documents.find(d => d.id === 'doc-etl').content)""");

        var nested = Script();

        // The canvas wrote exactly one PARALLEL block, and only because the author asked for one.
        Assert.Equal(1, nested.Split("PARALLEL", StringSplitOptions.None).Length - 1);

        // Selected through the DOM: a nested card can sit under the map's own toolbar, and this test
        // is about what the canvas writes, not about where the layout happens to put a box.
        await page.EvaluateAsync(
            """() => document.querySelector("[data-task-key='load_all']").dispatchEvent(new MouseEvent('click', { bubbles: true }))""");
        await page.Locator("[data-task-inspector]").WaitForAsync();
        var inspector = await page.Locator("[data-task-inspector]").InnerTextAsync();
        Assert.Contains("starts at the same time", inspector, StringComparison.Ordinal);

        // And a nested task offers its way back out.
        await page.EvaluateAsync(
            """() => document.querySelector("[data-task-key='load_orders']").dispatchEvent(new MouseEvent('click', { bubbles: true }))""");
        await page.Locator("[data-task-unnest]").WaitForAsync();
        await page.ClickAsync("[data-task-unnest]");
        await page.WaitForFunctionAsync("""() => !document.querySelector('[data-task-unnest]')""");

        // Out of the block, and still in the script: moving a task out is a relocation, never a delete.
        var freed = Script();
        Assert.Contains("load_orders:", freed, StringComparison.Ordinal);
        Assert.Matches(@"PARALLEL BEGIN\s*END;", freed);

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_TabName_DoubleClickRenamesAndEscapeCancels()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        var activeTitle = page.Locator(".etlsql-studio-tab.active .etlsql-tab-title");
        await activeTitle.DispatchEventAsync("dblclick");
        var input = page.Locator(".etlsql-tab-rename-input");
        await input.FillAsync("discarded-name");
        await input.PressAsync("Escape");
        Assert.Contains("sales_overview.rptsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());

        await page.Locator(".etlsql-studio-tab.active .etlsql-tab-title").DispatchEventAsync("dblclick");
        input = page.Locator(".etlsql-tab-rename-input");
        await input.FillAsync("quarterly_sales");
        await input.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.documents[0].name === 'quarterly_sales.rptsql'");
        Assert.Contains("quarterly_sales.rptsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Explorer_ManagesFoldersAndMovesFiles()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        await page.Locator("[data-explorer-new-folder='']").ClickAsync();
        await page.Locator(".etlsql-feedback-field input").FillAsync("archive-new");
        await page.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        var archive = page.Locator("[data-explorer-folder='archive-new']");
        await archive.WaitForAsync();

        await page.Locator("[data-explorer-file='reports/sales_overview.rptsql']").DragToAsync(archive);
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.workspaceFiles.some(file => file.path === 'archive-new/sales_overview.rptsql')");
        await page.Locator("[data-explorer-file='archive-new/sales_overview.rptsql']")
            .DragToAsync(page.Locator("[data-explorer-root-drop]"));
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.workspaceFiles.some(file => file.path === 'sales_overview.rptsql')");

        var etlFile = page.Locator("[data-explorer-file='etl/ingest_orders.etlsql']");
        await etlFile.HoverAsync();
        await etlFile.Locator("[data-explorer-rename]").ClickAsync();
        var prompt = page.Locator(".etlsql-feedback-field input");
        await prompt.FillAsync("import_orders");
        await page.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO_INSTANCE__.state.workspaceFiles.some(file => file.path === 'etl/import_orders.etlsql')");

        etlFile = page.Locator("[data-explorer-file='etl/import_orders.etlsql']");
        await etlFile.HoverAsync();
        await etlFile.Locator("[data-explorer-delete]").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await page.WaitForFunctionAsync("() => !window.__STUDIO_INSTANCE__.state.workspaceFiles.some(file => file.path === 'etl/import_orders.etlsql')");

        archive = page.Locator("[data-explorer-folder='archive-new']");
        await archive.HoverAsync();
        await archive.Locator("[data-explorer-rename]").ClickAsync();
        await page.Locator(".etlsql-feedback-field input").FillAsync("archive-final");
        await page.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        var renamedArchive = page.Locator("[data-explorer-folder='archive-final']");
        await renamedArchive.WaitForAsync();
        await renamedArchive.HoverAsync();
        await renamedArchive.Locator("[data-explorer-delete]").ClickAsync();
        await page.Locator(".etlsql-feedback-btn-danger").ClickAsync();
        await page.WaitForFunctionAsync("() => !window.__STUDIO_INSTANCE__.state.workspaceFolders.some(folder => folder.path === 'archive-final')");

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Mounts_SwitchesProjections_AndScansSecrets()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");

        // Wait for studio shell to mount
        var studioShell = page.Locator(".etlsql-studio-shell");
        await studioShell.WaitForAsync();
        await page.Locator(".etlsql-studio-tab").First.WaitForAsync();

        // 1. Verify tabs rendered (Home + 3 documents)
        Assert.Equal(4, await page.Locator(".etlsql-studio-tab").CountAsync());
        Assert.Contains("sales_overview.rptsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());
        var overflowBtn = page.Locator("[data-studio-overflow-btn]");
        Assert.True(await overflowBtn.IsVisibleAsync());

        // 2. Test projection switching
        await page.Locator("button[data-projection='canvas']").ClickAsync();
        Assert.True(await page.Locator("[data-visual-stage]").IsVisibleAsync());
        Assert.False(await page.Locator("[data-code-stage]").IsVisibleAsync());

        await page.Locator("button[data-projection='code']").ClickAsync();
        Assert.False(await page.Locator("[data-visual-stage]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-code-stage]").IsVisibleAsync());

        await page.Locator("button[data-projection='split']").ClickAsync();
        Assert.True(await page.Locator("[data-visual-stage]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-code-stage]").IsVisibleAsync());

        // 3. Test Activity Rail switching and Filter Pane
        await page.Locator("button.etlsql-studio-rail-btn[data-activity='catalog']").ClickAsync();
        Assert.Contains("New connection", await page.Locator("[data-sidebar-content]").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Connections", await page.Locator("[data-sidebar-content]").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await page.Locator("button.etlsql-studio-rail-btn[data-activity='filters']").ClickAsync();
        Assert.Equal("Data", await page.Locator("[data-sidebar-title]").InnerTextAsync(), ignoreCase: true);
        Assert.True(await page.Locator("[data-studio-sidebar]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-filter-sidebar]").IsVisibleAsync());
        await page.Locator("[data-studio-sidebar] [data-field='region']").ClickAsync();
        var filterDialog = page.Locator(".etlsql-studio-filter-dialog");
        await filterDialog.WaitForAsync();
        Assert.Equal("region", await filterDialog.Locator("[data-filter-dialog-field]").InputValueAsync());
        await filterDialog.Locator("[data-filter-dialog-field]").PressAsync("Escape");
        Assert.True(await page.Locator("[data-modal-backdrop]").IsHiddenAsync());

        // 4. Assert Phase 2 Live In-Memory Visual Canvas calculations
        var kpiCard = page.Locator(".etlsql-studio-canvas-card[data-visual-id='rev_kpi']");
        await kpiCard.WaitForAsync();
        Assert.Contains("Total Revenue", await kpiCard.InnerTextAsync());
        Assert.Contains("$402,000", await kpiCard.InnerTextAsync());

        var barCard = page.Locator(".etlsql-studio-canvas-card[data-visual-id='order_bar']");
        await barCard.WaitForAsync();
        Assert.True(await page.Locator(".etlsql-chart-bar-group").CountAsync() >= 3);

        // 5. Test Phase 3 "Promote to Slicer" 1-click workflow
        await page.Locator("button[data-promote-slicer='region']").ClickAsync();
        var slicerCard = page.Locator(".etlsql-studio-canvas-card[data-visual-id='region_slicer']");
        await slicerCard.WaitForAsync();
        Assert.True(await slicerCard.IsVisibleAsync());

        // Click "North" slicer pill and verify instant math recalculation ($45k + $62k + $54k = $161,000)
        await page.Locator("button.etlsql-slicer-pill[data-slicer-value='North']").ClickAsync();
        Assert.Contains("$161,000", await kpiCard.InnerTextAsync());

        // Click "All" slicer pill and verify restoration to $402,000
        await page.Locator("button.etlsql-slicer-pill[data-slicer-value='ALL']").ClickAsync();
        Assert.Contains("$402,000", await kpiCard.InnerTextAsync());

        // 6. Test Phase 4 Visual Card Click-to-Code and Surgical AST Patching
        await kpiCard.ClickAsync();
        Assert.True(await page.Locator(".etlsql-studio-canvas-card[data-visual-id='rev_kpi']").EvaluateAsync<bool>("el => el.classList.contains('selected')"));

        // Trigger surgical option update on rev_kpi
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__?.surgicalPatchVisualOption('rev_kpi', 'TITLE', 'Executive Net Revenue')");
        Assert.Contains("Executive Net Revenue", await kpiCard.InnerTextAsync());

        // 7. Switch to ETL tab via overflow dropdown and assert Pipeline DAG rendering
        await overflowBtn.ClickAsync();
        var tabDropdown = page.Locator("[data-studio-tab-dropdown]");
        Assert.False(await tabDropdown.IsHiddenAsync());
        Assert.Contains("ingest_orders.etlsql", await tabDropdown.InnerTextAsync());
        await tabDropdown.Locator(".etlsql-studio-tab-dropdown-item", new() { HasText = "ingest_orders.etlsql" }).ClickAsync();
        Assert.True(await tabDropdown.IsHiddenAsync());
        Assert.Contains("ingest_orders.etlsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());
        var dagView = page.Locator("[data-dag-view]");
        await dagView.WaitForAsync();
        Assert.True(await page.Locator("[data-dag-node]").CountAsync() >= 2);
        Assert.Contains("staging_db", await page.Locator("[data-dag-node='staging_db']").InnerTextAsync());
        Assert.Contains("#raw_sales", await page.Locator("[data-dag-node='#raw_sales']").InnerTextAsync());

        // 8. Switch to secret-containing tab and test save secret detection modal
        await page.Locator(".etlsql-studio-tab", new() { HasText = "direct_connect_test.sql" }).ClickAsync();
        await page.Locator("button[data-action='save']").ClickAsync();

        // Modal should appear with secret warning
        var modalBackdrop = page.Locator("[data-modal-backdrop]");
        await modalBackdrop.WaitForAsync();
        Assert.False(await modalBackdrop.IsHiddenAsync());
        Assert.Contains("Plaintext Secret Detected", await page.Locator("[data-modal-box]").InnerTextAsync());
        Assert.Contains("(value hidden)", await page.Locator("[data-modal-box]").InnerTextAsync());
        Assert.DoesNotContain("SuperSecretPassword123!", await page.Locator("[data-modal-box]").InnerTextAsync());
        Assert.False(await page.Locator("[data-modal-box]").EvaluateAsync<bool>(
            "element => element.innerHTML.includes('SuperSecretPassword123!')"));

        // Cancel modal
        await page.Locator("[data-modal-box] button[data-modal-close]").First.ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.True(await modalBackdrop.IsHiddenAsync());

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_VisualFormattingInspector_PatchesReportSqlAndKeepsAuthoredValues()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        var chart = page.Locator(".etlsql-studio-canvas-card[data-visual-id='v_SalesByRegion_1']");
        await chart.WaitForAsync();
        await chart.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var inspector = page.Locator(".etlsql-format-inspector");
        await inspector.WaitForAsync();
        Assert.Contains("BAR · Auto", await inspector.InnerTextAsync());
        Assert.Equal("Segoe UI", await page.Locator("#pp-title-font").InputValueAsync());
        Assert.False(await page.Locator("#pp-format-legend").IsCheckedAsync());
        Assert.Equal("Revenue", await page.Locator("[data-axis='y'][data-axis-key='LABEL']").InputValueAsync());
        Assert.Equal("$#,##0", await page.Locator("[data-axis='y'][data-axis-key='FORMAT']").InputValueAsync());
        Assert.True(await page.Locator("[data-axis='y'][data-axis-key='INCLUDE_ZERO']").IsCheckedAsync());
        Assert.Equal("6", await page.Locator("[data-axis='y'][data-axis-key='MAJOR_TICK_COUNT']").InputValueAsync());
        Assert.True(await page.Locator("[data-axis='y'][data-axis-key='MINOR_TICKS']").IsCheckedAsync());
        Assert.Equal(3, await page.Locator("[data-palette-text]").CountAsync());
        Assert.DoesNotContain("Optional", await inspector.InnerTextAsync(), StringComparison.Ordinal);

        await page.Locator("#pp-format-subtitle").FillAsync("FY26 booked revenue");
        await page.Locator("#pp-format-subtitle").DispatchEventAsync("change");
        await page.WaitForFunctionAsync("() => document.querySelector('.cm-content')?.textContent.includes(\"SUBTITLE = 'FY26 booked revenue'\")");
        await page.WaitForTimeoutAsync(300);
        await inspector.Locator("summary", new() { HasText = "Axes & legend" }).ClickAsync();
        await page.Locator("#pp-format-legend").CheckAsync();
        await page.Locator("#pp-format-legend-position").SelectOptionAsync("RIGHT");
        await page.Locator("#pp-format-grid-lines").UncheckAsync();
        await page.Locator("#pp-format-grid-color").FillAsync("#123456");
        await page.Locator("#pp-format-grid-color").DispatchEventAsync("input");
        await page.Locator("#pp-format-grid-dash").SelectOptionAsync("DASHED");
        await page.Locator("#pp-format-grid-width").FillAsync("2");
        await page.Locator("#pp-format-grid-width").DispatchEventAsync("change");
        await page.Locator("#pp-format-minor-grid-lines").CheckAsync();
        await page.Locator("#pp-format-zero-line").CheckAsync();
        await page.Locator("#pp-format-zero-line-color").FillAsync("#654321");
        await page.Locator("#pp-format-zero-line-color").DispatchEventAsync("input");
        await page.Locator("#pp-format-zero-line-dash").SelectOptionAsync("DOTTED");
        await page.Locator("#pp-format-zero-line-width").FillAsync("2.5");
        await page.Locator("#pp-format-zero-line-width").DispatchEventAsync("change");
        await page.Locator("#pp-format-zoom-slider").CheckAsync();
        await page.Locator("[data-axis='y'][data-axis-key='MAX']").FillAsync("500000");
        await page.Locator("[data-axis='y'][data-axis-key='MAX']").DispatchEventAsync("change");
        await page.Locator("[data-axis='y'][data-axis-key='REVERSE']").CheckAsync();
        await page.Locator("[data-axis='x'][data-axis-key='LABEL_ROTATION']").SelectOptionAsync("45");
        await page.Locator("[data-axis='x'][data-axis-key='LABEL_SKIP']").FillAsync("1");
        await page.Locator("[data-axis='x'][data-axis-key='LABEL_SKIP']").DispatchEventAsync("change");
        await page.Locator("[data-axis='x'][data-axis-key='AXIS_LINE']").UncheckAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.cm-content')?.textContent.includes('LEGEND = ON')");
        await page.WaitForFunctionAsync("() => document.querySelector('.cm-content')?.textContent.includes('MAX = 500000')");
        await page.WaitForTimeoutAsync(300);
        await inspector.Locator("summary", new() { HasText = "Series palette" }).ClickAsync();
        await page.Locator("[data-palette-add]").ClickAsync();
        await inspector.Locator("summary", new() { HasText = "Series palette" }).ClickAsync();
        await page.Locator("[data-named-color-add]").ClickAsync();
        await inspector.Locator("summary", new() { HasText = "Marks & labels" }).ClickAsync();
        await page.Locator("#pp-format-data-labels").CheckAsync();
        await page.Locator("#pp-format-data-label-position").SelectOptionAsync("INSIDE MIDDLE");
        await page.Locator("#pp-format-stacked").SelectOptionAsync("100PCT");
        await page.Locator("#pp-format-band-size").FillAsync("0.5");
        await page.Locator("#pp-format-band-size").DispatchEventAsync("input");
        await page.Locator("#pp-format-series-gap").FillAsync("0.25");
        await page.Locator("#pp-format-series-gap").DispatchEventAsync("input");
        await page.Locator("#pp-format-outer-padding").FillAsync("0.4");
        await page.Locator("#pp-format-outer-padding").DispatchEventAsync("input");
        await page.Locator("#pp-format-overlays").FillAsync("OVERLAYS (GOAL(100000) AS DASHED LABEL 'Target')");
        await page.Locator("#pp-format-overlays").DispatchEventAsync("change");
        await inspector.Locator("summary", new() { HasText = "Conditional formatting" }).ClickAsync();
        await page.Locator("[data-rule-add]").ClickAsync();

        await page.WaitForTimeoutAsync(1500);

        var activeScript = await page.EvaluateAsync<string>(
            """
            () => window.__STUDIO_INSTANCE__.state.documents.find(
                item => item.id === window.__STUDIO_INSTANCE__.state.activeDocId).content
            """);
        Assert.Contains("TITLE (TEXT = 'Sales by Region'", activeScript, StringComparison.Ordinal);
        Assert.Contains("SUBTITLE = 'FY26 booked revenue'", activeScript, StringComparison.Ordinal);
        Assert.Contains("LEGEND = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("LEGEND_POSITION = 'RIGHT'", activeScript, StringComparison.Ordinal);
        Assert.Contains("GRID_LINES = OFF", activeScript, StringComparison.Ordinal);
        Assert.Contains("GRID_LINE_COLOR = '#123456'", activeScript, StringComparison.Ordinal);
        Assert.Contains("GRID_LINE_DASH = 'DASHED'", activeScript, StringComparison.Ordinal);
        Assert.Contains("GRID_LINE_WIDTH = 2", activeScript, StringComparison.Ordinal);
        Assert.Contains("MINOR_GRID_LINES = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZERO_LINE = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZERO_LINE_COLOR = '#654321'", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZERO_LINE_DASH = 'DOTTED'", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZERO_LINE_WIDTH = 2.5", activeScript, StringComparison.Ordinal);
        Assert.Contains("ZOOM_SLIDER = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("DATA_LABELS = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("DATA_LABELS:POSITION = 'INSIDE_MIDDLE'", activeScript, StringComparison.Ordinal);
        Assert.Contains("BAND_SIZE = 0.5", activeScript, StringComparison.Ordinal);
        Assert.Contains("STACKED = '100PCT'", activeScript, StringComparison.Ordinal);
        Assert.Contains("SERIES_GAP = 0.25", activeScript, StringComparison.Ordinal);
        Assert.Contains("OUTER_PADDING = 0.4", activeScript, StringComparison.Ordinal);
        Assert.Contains("X_AXIS (LABEL = 'Region'", activeScript, StringComparison.Ordinal);
        Assert.Contains("LABEL_ROTATION = 45", activeScript, StringComparison.Ordinal);
        Assert.Contains("LABEL_SKIP = 1", activeScript, StringComparison.Ordinal);
        Assert.Contains("AXIS_LINE = OFF", activeScript, StringComparison.Ordinal);
        Assert.Contains("Y_AXIS (LABEL = 'Revenue'", activeScript, StringComparison.Ordinal);
        Assert.Contains("INCLUDE_ZERO = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("MAJOR_TICK_COUNT = 6", activeScript, StringComparison.Ordinal);
        Assert.Contains("MINOR_TICKS = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("REVERSE = ON", activeScript, StringComparison.Ordinal);
        Assert.Contains("MAX = 500000", activeScript, StringComparison.Ordinal);
        Assert.Contains("COLOR:Series1 = '#2563eb'", activeScript, StringComparison.Ordinal);
        Assert.Contains("STYLE (PALETTE = ('#58a6ff', '#2ea043', '#d29922', '#dc2626'))", activeScript, StringComparison.Ordinal);
        Assert.Contains("OVERLAYS (GOAL(100000) AS DASHED LABEL", activeScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FORMATTING (WHEN", activeScript, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_SourceControl_ShowsLocalHistoryAndSideBySideDiff()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.Locator("button[data-activity='git']").ClickAsync();

        var headComparison = page.Locator("[data-git-revision='HEAD']");
        await headComparison.WaitForAsync();
        Assert.Contains("Includes unsaved editor changes", await headComparison.InnerTextAsync());
        Assert.Equal(2, await page.Locator(".etlsql-studio-git-history [data-git-revision]").CountAsync());

        await headComparison.ClickAsync();
        var diff = page.Locator(".etlsql-studio-git-diff-modal");
        await diff.WaitForAsync();
        Assert.Contains("HEAD a12bc34d", await diff.InnerTextAsync());
        Assert.Contains("Working tree", await diff.InnerTextAsync());
        Assert.Equal(1, await diff.Locator(".etlsql-studio-git-diff-row.is-change").CountAsync());
        Assert.Contains(await diff.Locator(".etlsql-studio-git-diff-cell.is-left").AllInnerTextsAsync(),
            text => text.Contains("TITLE = 'Revenue'", StringComparison.Ordinal));
        Assert.Contains(await diff.Locator(".etlsql-studio-git-diff-cell.is-right").AllInnerTextsAsync(),
            text => text.Contains("TITLE = 'Total Revenue'", StringComparison.Ordinal));

        await diff.Locator("[data-git-diff-close]").ClickAsync();
        Assert.True(await diff.IsHiddenAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_FilterPane_RendersCategoricalNumericAndDateControls()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.Locator("button.etlsql-studio-rail-btn[data-activity='catalog']").ClickAsync();
        await page.Locator("button.etlsql-studio-rail-btn[data-activity='filters']").ClickAsync();

        var dataSidebar = page.Locator("[data-studio-sidebar]");
        var filterSidebar = page.Locator("[data-filter-sidebar]");
        Assert.True(await dataSidebar.IsVisibleAsync());
        Assert.True(await filterSidebar.IsVisibleAsync());
        Assert.DoesNotContain("Active filters", await dataSidebar.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Connections", await filterSidebar.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await dataSidebar.Locator("[data-field='region']").DragToAsync(filterSidebar.Locator("[data-filter-drop]"));
        var setup = page.Locator(".etlsql-studio-filter-dialog");
        await setup.WaitForAsync();
        Assert.Equal("region", await setup.Locator("[data-filter-dialog-field]").InputValueAsync());
        await setup.Locator("[data-filter-dialog-apply]").ClickAsync();

        await filterSidebar.Locator("[data-new-filter]").ClickAsync();
        await setup.Locator("[data-filter-dialog-field]").SelectOptionAsync("total_amount");
        await setup.Locator("[data-filter-dialog-apply]").ClickAsync();

        await filterSidebar.Locator("[data-new-filter]").ClickAsync();
        await setup.Locator("[data-filter-dialog-field]").SelectOptionAsync("order_date");
        await setup.Locator("[data-filter-dialog-apply]").ClickAsync();

        Assert.Equal(3, await filterSidebar.Locator(".etlsql-filter-card").CountAsync());
        Assert.True(await filterSidebar.Locator("[data-date-preset='order_date']").IsVisibleAsync());
        Assert.True(await filterSidebar.Locator("[data-filter-date-min='order_date']").IsVisibleAsync());
        Assert.True(await filterSidebar.Locator("[data-filter-date-max='order_date']").IsVisibleAsync());
        Assert.True(await filterSidebar.Locator("[data-filter-min='total_amount']").IsVisibleAsync());
        Assert.True(await filterSidebar.Locator("[data-filter-max='total_amount']").IsVisibleAsync());
        Assert.Equal(4, await filterSidebar.Locator("[data-filter-value='region']").CountAsync());
        Assert.Equal(3, await filterSidebar.Locator("[data-filter-scope]").CountAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_NewScript_CreatesAnEtlSqlDocument()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.Locator("[data-studio-new-tab]").ClickAsync();

        var newScript = page.Locator("[data-new-type='sql']");
        Assert.Contains("New Script (.etlsql)", await newScript.InnerTextAsync());
        await newScript.ClickAsync();

        var activeTab = page.Locator(".etlsql-studio-tab.active");
        Assert.Contains("untitled_query_", await activeTab.InnerTextAsync());
        Assert.Contains(".etlsql", await activeTab.InnerTextAsync());
        Assert.DoesNotContain(".sql", await activeTab.InnerTextAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Home_CreatesDistinctDashboardAndPaginatedReportWorkflows()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");

        var dashboardAction = page.Locator("[data-create-from-home='dashboard']:not([data-seed-sample])");
        var paginatedAction = page.Locator("[data-create-from-home='paginated']");
        Assert.Contains("New Dashboard", await dashboardAction.InnerTextAsync());
        Assert.Contains("New Paginated Report", await paginatedAction.InnerTextAsync());

        await paginatedAction.ClickAsync();
        await page.Locator(".etlsql-studio-visual-stage.is-paginated-workflow").WaitForAsync();
        Assert.Contains("Physical page authoring", await page.Locator("[data-workflow-bar]").InnerTextAsync());
        Assert.Equal(8, await page.Locator(".etlsql-paginated-steps > li").CountAsync());
        var paginated = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const doc = window.__STUDIO_INSTANCE__.state.documents.find(item => item.id === window.__STUDIO_INSTANCE__.state.activeDocId);
                return { name: doc.name, content: doc.content, workflow: doc.reportWorkflow };
            }
            """);
        Assert.EndsWith(".rptsql", paginated.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal("paginated", paginated.GetProperty("workflow").GetString());
        Assert.Contains("AS PAGINATED", paginated.GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Contains("PRINT_LAYOUT", paginated.GetProperty("content").GetString(), StringComparison.Ordinal);

        await page.SelectOptionAsync("[data-page-setup='orientation']", "LANDSCAPE");
        await page.WaitForFunctionAsync(
            """
            () => window.__STUDIO_API_REQUESTS__.some(request =>
                request.url.endsWith('/api/designer/patch') &&
                request.body?.designState?.pages?.[0]?.printLayout?.orientation === 'LANDSCAPE')
            """);

        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");
        await dashboardAction.ClickAsync();
        await page.Locator(".etlsql-studio-visual-stage.is-dashboard-workflow").WaitForAsync();
        Assert.Contains("Responsive visual canvas", await page.Locator("[data-workflow-bar]").InnerTextAsync());
        var dashboard = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const doc = window.__STUDIO_INSTANCE__.state.documents.find(item => item.id === window.__STUDIO_INSTANCE__.state.activeDocId);
                return { name: doc.name, content: doc.content, workflow: doc.reportWorkflow };
            }
            """);
        Assert.EndsWith(".rptsql", dashboard.GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal("dashboard", dashboard.GetProperty("workflow").GetString());
        Assert.Contains("AS DASHBOARD", dashboard.GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_ExistingReportWorkflowInference_PreservesAmbiguousAndInvalidScriptBytes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        Assert.Equal("dashboard", await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(doc => doc.id === 'doc-report').reportWorkflow"));
        var validCanvasCount = await page.Locator(".designer-card").CountAsync();
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.state.editorInstance.setValue('>>> INVALID <<<')");
        await page.WaitForTimeoutAsync(550);
        Assert.Equal("dashboard", await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.documents.find(doc => doc.id === 'doc-report').reportWorkflow"));
        Assert.Equal(validCanvasCount, await page.Locator(".designer-card").CountAsync());
        Assert.Equal(">>> INVALID <<<", await page.EvaluateAsync<string>(
            "() => window.__STUDIO_INSTANCE__.state.editorInstance.getValue()"));

        const string ambiguous = "-- hand-authored report\nCREATE VISUAL note AS TEXT (TITLE = 'Keep me', SOURCE = #rows);";
        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO_INSTANCE__;
                studio.state.documents.push({ id: 'ambiguous-report', path: 'ambiguous.rptsql', name: 'ambiguous.rptsql', content: script, isDirty: false, projection: 'split' });
                void studio.switchDoc('ambiguous-report');
            }
            """, ambiguous);
        await page.Locator("[data-choose-workflow='dashboard']").WaitForAsync();
        Assert.Contains("byte-for-byte unchanged", await page.Locator(".etlsql-workflow-choice").InnerTextAsync());
        await page.Locator("[data-choose-workflow='paginated']").ClickAsync();
        await page.Locator(".etlsql-studio-visual-stage.is-paginated-workflow").WaitForAsync();

        var selected = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const doc = window.__STUDIO_INSTANCE__.state.documents.find(item => item.id === 'ambiguous-report');
                return { content: doc.content, workflow: doc.reportWorkflow, dirty: doc.isDirty };
            }
            """);
        Assert.Equal(ambiguous, selected.GetProperty("content").GetString());
        Assert.Equal("paginated", selected.GetProperty("workflow").GetString());
        Assert.False(selected.GetProperty("dirty").GetBoolean());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Home_CanDismissAWorkspaceFileWithoutClosingItsDocument()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();
        await page.EvaluateAsync("() => window.__STUDIO_INSTANCE__.switchDoc('__home__')");

        var cards = page.Locator(".etlsql-studio-recent-card");
        await cards.First.WaitForAsync();
        Assert.Equal(3, await cards.CountAsync());
        await page.Locator("[data-dismiss-file='reports/sales_overview.rptsql']").ClickAsync();

        Assert.Equal(2, await cards.CountAsync());
        Assert.Equal(0, await page.Locator("[data-dismiss-file='reports/sales_overview.rptsql']").CountAsync());
        Assert.Equal(1, await page.Locator(".etlsql-studio-tab", new() { HasText = "sales_overview.rptsql" }).CountAsync());
        Assert.Contains("file was not deleted", await page.Locator("body").InnerTextAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_Header_OmitsConnectionAndFormatWhileContextualActionsRemain()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        var header = page.Locator(".etlsql-studio-header");
        Assert.Equal(0, await header.Locator("[data-action='wizard']").CountAsync());
        Assert.Equal(0, await header.Locator("[data-action='format']").CountAsync());
        Assert.True(await page.Locator("[data-action='code-format']").IsVisibleAsync());

        await page.Locator("[data-activity='catalog']").ClickAsync();
        Assert.True(await page.Locator(".etlsql-studio-sidebar [data-action='wizard']").IsVisibleAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_DecorativeWorkbenchControls_ReportUnavailableCapabilities()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");
        await page.Locator(".etlsql-studio-shell").WaitForAsync();

        await page.Locator("[data-activity='git']").ClickAsync();
        var gitState = page.Locator("[data-capability-state='git']");
        Assert.Contains("Source control is unavailable", await gitState.InnerTextAsync());
        Assert.DoesNotContain("Branch:", await gitState.InnerTextAsync());
        Assert.DoesNotContain("Working tree clean", await gitState.InnerTextAsync());

        await page.Locator("[data-activity='settings']").ClickAsync();
        var settingsState = page.Locator("[data-capability-state='settings']");
        Assert.Contains("Settings are unavailable", await settingsState.InnerTextAsync());
        Assert.Equal(0, await page.Locator("[data-sidebar-content] input").CountAsync());
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ScriptEditor_MouseSelectsSingleLineRangesAndWholeEtlSqlTokens()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.EvaluateAsync(
            """
            async () => {
                document.body.innerHTML = '<div id="selection-editor" style="width:700px;height:240px"></div>';
                const module = await import('/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js?selection-test');
                window.__selectionEditor = await module.createScriptEditor(
                    document.getElementById('selection-editor'),
                    { value: 'SELECT #temp, @declare, plain_word, commas;' });
            }
            """);
        await page.Locator(".cm-content").WaitForAsync();

        async Task<(float X, float Y)> CharacterPositionAsync(int offset)
        {
            var point = await page.EvaluateAsync<System.Text.Json.JsonElement>(
                """
                offset => {
                    const line = document.querySelector('.cm-line');
                    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
                    let remaining = offset;
                    let node;
                    while ((node = walker.nextNode())) {
                        if (remaining <= node.textContent.length) {
                            const range = document.createRange();
                            range.setStart(node, remaining);
                            range.setEnd(node, remaining);
                            const rect = range.getBoundingClientRect();
                            const lineRect = line.getBoundingClientRect();
                            return { x: rect.left, y: lineRect.top + lineRect.height / 2 };
                        }
                        remaining -= node.textContent.length;
                    }
                    throw new Error(`Offset ${offset} is outside the editor line.`);
                }
                """, offset);
            return ((float)point.GetProperty("x").GetDouble(), (float)point.GetProperty("y").GetDouble());
        }

        var dragStart = await CharacterPositionAsync(7);
        var dragEnd = await CharacterPositionAsync(12);
        await page.Mouse.MoveAsync(dragStart.X, dragStart.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(dragEnd.X, dragEnd.Y, new() { Steps = 5 });
        await page.Mouse.UpAsync();
        Assert.Equal("#temp", await page.EvaluateAsync<string>("() => window.__selectionEditor.getSelection()"));

        var variable = await CharacterPositionAsync(16);
        await page.Mouse.DblClickAsync(variable.X, variable.Y);
        Assert.Equal("@declare", await page.EvaluateAsync<string>("() => window.__selectionEditor.getSelection()"));
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task Studio_AuditsResponsivenessAndLayoutShiftAcrossResolutions()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");

        var studioShell = page.Locator(".etlsql-studio-shell");
        await studioShell.WaitForAsync();

        // Audit viewport geometries from 1024x768 up to 4K UHD
        (int width, int height)[] viewports = [
            (1024, 768),
            (1366, 768),
            (1920, 1080),
            (2560, 1440),
            (3840, 2160)
        ];

        foreach (var (w, h) in viewports)
        {
            await page.SetViewportSizeAsync(w, h);
            await page.WaitForTimeoutAsync(50);

            // 1. Verify no unwanted horizontal scroll overflow on the shell
            var isOverflowing = await studioShell.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 2");
            Assert.False(isOverflowing, $"Studio shell unexpectedly overflowed horizontally at {w}x{h}");

            // 2. Verify Activity Rail buttons maintain minimum accessible hitboxes (>= 24px)
            var railButtons = page.Locator("button.etlsql-studio-rail-btn");
            var buttonCount = await railButtons.CountAsync();
            for (var i = 0; i < buttonCount; i++)
            {
                var box = await railButtons.Nth(i).BoundingBoxAsync();
                Assert.NotNull(box);
                Assert.True(box.Width >= 24, $"Rail button {i} width {box.Width} < 24px at {w}x{h}");
                Assert.True(box.Height >= 24, $"Rail button {i} height {box.Height} < 24px at {w}x{h}");
            }

            // 3. Verify Canvas visual cards are rendered and accessible
            var canvasCards = page.Locator(".etlsql-studio-canvas-card");
            Assert.True(await canvasCards.CountAsync() >= 2);
            for (var j = 0; j < await canvasCards.CountAsync(); j++)
            {
                var cardBox = await canvasCards.Nth(j).BoundingBoxAsync();
                Assert.NotNull(cardBox);
                Assert.True(cardBox.Width >= 200, $"Canvas card {j} width {cardBox.Width} is too narrow at {w}x{h}");
            }
        }

        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ConstrainedHtmlRuntime_SanitizesEmbedsActsAndPrintsAccessibly()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='constrained-html-runtime']");
        var frame = page.FrameLocator("iframe[title='Constrained HTML runtime fixture']");
        await frame.Locator(".html-visual-content").WaitForAsync();

        Assert.Equal("Cluster status: 3 healthy nodes",
            await frame.Locator(".html-visual-content").GetAttributeAsync("aria-label"));
        Assert.Equal(0, await frame.Locator(".html-visual-content script").CountAsync());
        Assert.Equal(0, await frame.Locator(".html-visual-content img").CountAsync());
        Assert.Contains("42", await frame.Locator("[data-etl-embed-id]").InnerTextAsync());
        Assert.Contains("#etl-v-statuspanel", await frame.Locator(".html-visual-scoped-style").TextContentAsync());
        Assert.Equal(2, await frame.Locator(".html-inline-microchart svg").CountAsync());
        Assert.Equal("Service trend", await frame.Locator("[data-etl-microchart-id='StatusPanel-micro-0']")
            .GetAttributeAsync("aria-label"));
        Assert.Equal(0, await frame.Locator("[data-etl-microchart-id='forged'] svg").CountAsync());
        Assert.Contains("Error", await frame.Locator("[data-etl-microchart-id='forged']").InnerTextAsync());

        await frame.GetByRole(AriaRole.Button, new() { Name = "Undeclared refresh" }).ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.Equal(0, await frame.Locator("body").EvaluateAsync<int>(
            "() => window.__htmlVisualActionRequests.length"));

        await frame.GetByRole(AriaRole.Button, new() { Name = "Show West" }).ClickAsync();
        await page.WaitForTimeoutAsync(100);
        var requests = await frame.Locator("body").EvaluateAsync<int>(
            "() => window.__htmlVisualActionRequests.length");
        Assert.Equal(1, requests);

        await page.EmulateMediaAsync(new() { Media = Media.Print });
        Assert.True(await frame.Locator(".html-visual-content").IsVisibleAsync());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task DesignTokenRuntime_CascadesSafelyAcrossCustomLayerAndMaximizedScopes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='design-tokens']");
        var frame = page.FrameLocator("iframe[title='Design tokens runtime fixture']");

        var inheritedCard = frame.Locator("#etl-v-inheritedcard");
        var inheritedPanel = inheritedCard.Locator(".token-panel");
        await inheritedPanel.WaitForAsync();

        Assert.Equal("#38bdf8", await CssVariableAsync(inheritedCard, "--etl-accent"));
        Assert.Equal("#94a3b8", await CssVariableAsync(inheritedCard, "--etl-border"));
        Assert.Equal("12px", await CssVariableAsync(inheritedCard, "--etl-radius-md"));
        Assert.Equal("solid", await inheritedPanel.EvaluateAsync<string>("element => getComputedStyle(element).borderStyle"));
        Assert.Equal("rgb(148, 163, 184)", await inheritedPanel.EvaluateAsync<string>("element => getComputedStyle(element).borderColor"));

        var layer = frame.Locator(".container-layer[data-name='LayerContainer']");
        var layerCard = frame.Locator("#etl-v-layercard");
        Assert.Equal("#10b981", await CssVariableAsync(layer, "--etl-accent"));
        Assert.Equal("#059669", await CssVariableAsync(layerCard, "--etl-border"));
        Assert.Equal("rgb(5, 150, 105)", await layerCard.Locator(".token-panel")
            .EvaluateAsync<string>("element => getComputedStyle(element).borderColor"));

        var overrideCard = frame.Locator("#etl-v-overridecard");
        Assert.Equal("", await InlineCssVariableAsync(overrideCard, "--etl-unknown"));
        Assert.Equal("", await InlineCssVariableAsync(overrideCard, "--portal-private"));

        await inheritedCard.Locator(":scope > .visual-toolbar .visual-tool-btn").ClickAsync();
        Assert.Equal("12px", await CssVariableAsync(inheritedCard, "--etl-radius-md"));
        Assert.Equal("#94a3b8", await CssVariableAsync(inheritedCard, "--etl-border"));
        await inheritedCard.Locator(":scope > .visual-toolbar .visual-tool-btn").ClickAsync();
        Assert.Equal("12px", await CssVariableAsync(inheritedCard, "--etl-radius-md"));
        Assert.Equal("#94a3b8", await CssVariableAsync(inheritedCard, "--etl-border"));

        Assert.Empty(session.PageErrors);
    }

    private static Task<string> CssVariableAsync(ILocator locator, string name) =>
        locator.EvaluateAsync<string>("(element, token) => getComputedStyle(element).getPropertyValue(token).trim()", name);

    private static Task<string> InlineCssVariableAsync(ILocator locator, string name) =>
        locator.EvaluateAsync<string>("(element, token) => element.style.getPropertyValue(token).trim()", name);

    /// <summary>
    /// Drives the five <c>vscode-webviews</c> fixtures and asserts each one renders from files the
    /// repository actually tracks.
    ///
    /// <para>The results fixture used to fetch <c>src/etl-sql-vscode/ui/dist/index.html</c> — a Vite
    /// build output, gitignored — and fall back to a stub when the fetch 404'd. That made the story
    /// render one thing for whoever had built the UI and another thing for everyone else, and it is
    /// exactly the kind of difference nobody notices, because the fallback looks like a working
    /// panel. Asserting "mounts cleanly" would not have caught it; asserting that every byte the
    /// story loads is committed does.</para>
    ///
    /// <para>Each fixture is also checked for having rendered something recognisable inside its
    /// frame, because an iframe that loaded nothing still satisfies the shell-level mount check in
    /// <see cref="EveryStoryAndFixture_MountsWithoutThrowing"/>.</para>
    /// </summary>
    [Theory]
    [InlineData("results", "#tables table, #progress .node")]
    [InlineData("preview", ".visual-card")]
    [InlineData("preview-sink", ".visual-card")]
    [InlineData("designer", "#designerRoot *")]
    [InlineData("visual-flow", ".etlsql-dag-card")]
    public async Task VsCodeWebviewFixtures_RenderFromTrackedSourcesOnly(string fixtureId, string frameSelector)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        var requested = new List<string>();
        page.Request += (_, request) => { lock (requested) requested.Add(request.Url); };

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='vscode-webviews']");
        await page.SelectOptionAsync("#fixtureSel", fixtureId);

        // The frames stream (the results replay) or render asynchronously (the preview fetches a
        // sample snapshot), so wait on the content rather than on a fixed delay.
        var frame = page.FrameLocator("iframe.vscode-webview-frame");
        await frame.Locator(frameSelector).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

        Assert.Empty(session.PageErrors);

        // The browser asks for /favicon.ico on its own; the sandbox declares none, and that 404 says
        // nothing about whether the story's own dependencies are present.
        var failed = session.FailedRequests
            .Where(r => !r.Contains("/favicon.ico", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(failed.Count == 0,
            $"fixture '{fixtureId}' made requests that failed:\n  {string.Join("\n  ", failed)}");

        List<string> paths;
        lock (requested)
            paths = [.. requested.Where(u => u.StartsWith(baseUrl, StringComparison.Ordinal))
                                 .Select(ToRepoRelativePath)
                                 .Where(p => p is not null)
                                 .Select(p => p!)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)];

        Assert.NotEmpty(paths);

        var root = RepoRoot();
        var missing = paths.Where(p => !File.Exists(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar))))
                           .ToList();
        Assert.True(missing.Count == 0,
            $"fixture '{fixtureId}' requested files that do not exist:\n  {string.Join("\n  ", missing)}");

        var ignored = GitIgnoredPaths(root, paths);
        Assert.True(ignored.Count == 0,
            $"fixture '{fixtureId}' depends on generated files the repository does not track, so it "
            + $"renders differently on a clean checkout:\n  {string.Join("\n  ", ignored)}");
    }

    /// <summary>
    /// Maps a request URL back to a repository-relative path, or null for anything not served out of
    /// the tree (<c>/maps/*</c> is remapped onto the shared runtime directory, exactly as the host in
    /// <see cref="InitializeAsync"/> and <c>serve.ps1</c> do).
    /// </summary>
    private static string? ToRepoRelativePath(string url)
    {
        var rel = Uri.UnescapeDataString(new Uri(url).AbsolutePath).TrimStart('/');
        if (rel.Length == 0) return null;
        if (rel.Equals("favicon.ico", StringComparison.OrdinalIgnoreCase)) return null;
        if (rel.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            rel = "src/ETL-SQL.ReportRuntime/Resources/Shared/" + rel;
        return rel;
    }

    /// <summary>
    /// Returns the subset of <paramref name="paths"/> that <c>.gitignore</c> excludes.
    ///
    /// <para>Existence is not the question — the file exists on the machine that built it. The
    /// question is whether a fresh clone would have it, and <c>git check-ignore</c> is the only thing
    /// that answers that without re-implementing ignore-rule precedence.</para>
    /// </summary>
    private static List<string> GitIgnoredPaths(string root, IReadOnlyList<string> paths)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("check-ignore");
        psi.ArgumentList.Add("--stdin");

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException(
                "git is required to prove the sandbox runs from a clean checkout.");

        foreach (var path in paths) proc.StandardInput.WriteLine(path);
        proc.StandardInput.Close();

        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        // 0 = some paths are ignored, 1 = none are. Anything else means git itself failed, and a
        // silently empty result would turn this assertion into a no-op.
        if (proc.ExitCode > 1)
            throw new InvalidOperationException($"git check-ignore failed: {proc.StandardError.ReadToEnd()}");

        return [.. output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];
    }

    // A narrow-viewport assertion was tried here and removed. The sandbox stage is
    // `overflow: auto`, so a component wider than it scrolls inside its own container -- which is
    // the correct pattern, not a defect, and the check flagged six components for doing the right
    // thing. Page-level overflow is asserted where it actually reaches users, on the shipped pages
    // in PortalAccessibilityTests.

    /// <summary>
    /// Closes anything a previous fixture left covering the page.
    ///
    /// <para>A story that opens a modal as its fixture is doing its job -- the dataset viewer is
    /// one -- but a <c>position: fixed</c> overlay covers the sandbox's own navigation, so the next
    /// click lands on the modal instead of the story link. Escape first, because that is what a
    /// person would press and it exercises the shared dialog behaviour; hiding directly is the
    /// fallback for overlays that predate it.</para>
    /// </summary>
    private static async Task DismissOpenOverlaysAsync(IPage page)
    {
        const string selector = ".modal-overlay, [class$=modal-backdrop]";
        var open = await page.EvaluateAsync<int>(
            """
            (sel) => [...document.querySelectorAll(sel)]
                .filter(el => getComputedStyle(el).display !== 'none').length
            """, selector);
        if (open == 0) return;

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(100);

        await page.EvaluateAsync(
            """
            (sel) => [...document.querySelectorAll(sel)]
                .forEach(el => { el.style.display = 'none'; el.classList.remove('open'); })
            """, selector);
    }

    private sealed class StoryInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string[] Fixtures { get; set; } = [];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
