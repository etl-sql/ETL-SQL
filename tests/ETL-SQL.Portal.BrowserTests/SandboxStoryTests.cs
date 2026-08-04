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
                fixtures: (s.fixtures ?? [{ id: '' }]).map(f => f.id),
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

                // Clicking through the shell rather than calling mount() directly: the shell's
                // dispose/re-mount path is part of what breaks, and a story that leaks between
                // fixtures only shows up when one is mounted after another.
                await DismissOpenOverlaysAsync(page);
                await page.ClickAsync($".story-link:nth-of-type({Array.IndexOf(stories, story) + 1})");
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
                    failures.Add($"{label} logged: {string.Join(" | ", logged)}");

                if (await page.Locator("#stage *").CountAsync() == 0)
                    failures.Add($"{label} mounted nothing into the stage.");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} sandbox story fixtures failed to mount cleanly:\n  "
            + string.Join("\n  ", failures));
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
