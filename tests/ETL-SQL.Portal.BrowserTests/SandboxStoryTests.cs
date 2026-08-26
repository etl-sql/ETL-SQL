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
        Assert.Contains("CPU: CpuPercent", await card.InnerTextAsync());

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

        Assert.Empty(session.PageErrors);
    }

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
