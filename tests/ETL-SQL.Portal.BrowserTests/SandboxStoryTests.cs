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
        Assert.Equal(1, await svg.CountAsync());
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
                fetchGatewayResources: async (gwId) => [
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
                ],
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
    public async Task Studio_Mounts_SwitchesProjections_AndScansSecrets()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.ClickAsync("button.story-link[data-story-id='studio']");

        // Wait for studio shell to mount
        var studioShell = page.Locator(".etlsql-studio-shell");
        await studioShell.WaitForAsync();

        // 1. Verify tabs rendered
        Assert.Equal(3, await page.Locator(".etlsql-studio-tab").CountAsync());
        Assert.Contains("sales_overview.rptsql", await page.Locator(".etlsql-studio-tab.active").InnerTextAsync());

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
        Assert.Contains("Published Connections", await page.Locator("[data-sidebar-content]").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await page.Locator("button.etlsql-studio-rail-btn[data-activity='filters']").ClickAsync();
        Assert.Contains("Filter Pane", await page.Locator("[data-sidebar-title]").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Region", await page.Locator(".etlsql-filter-card", new() { HasText = "Region" }).InnerTextAsync());

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

        // 7. Switch to ETL tab and assert Pipeline DAG rendering
        await page.Locator(".etlsql-studio-tab", new() { HasText = "ingest_orders.etlsql" }).ClickAsync();
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
        Assert.Contains("SuperSecretPassword123!", await page.Locator("[data-modal-box]").InnerTextAsync());

        // Cancel modal
        await page.Locator("[data-modal-box] button[data-modal-close]").First.ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.True(await modalBackdrop.IsHiddenAsync());

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
