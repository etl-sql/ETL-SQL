using System.Text.Json;
using ETL_SQL.Core.Reporting;
using ETL_SQL.Reporting;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Opens a generated <c>.etlsnap</c> viewer as a file on disk, with every network origin fenced
/// off, and asserts the two things the documentation has been promising: an author bookmark
/// replays, and a detail popover opens with its content.
///
/// <para>Both behaviours were implemented and unit-tested in the shared runtime long before this
/// test existed, and both were unreachable: nothing shipped a host that set
/// <c>window.__ETLSNAP__</c>. Driving the real exported file is what distinguishes "the offline
/// branch is covered" from "a reader can open a snapshot", and it is what caught the detail
/// popover still refreshing through the parameter API — a call that has no server behind it
/// offline, so every popover in a snapshot rendered "could not be loaded".</para>
///
/// <para>The fence is deliberately doubled. The page carries a <c>default-src 'none'</c> CSP, and
/// the test also aborts any http(s) request and fails if one was even attempted: a viewer that
/// works only because the machine that opened it happened to be online is the failure this is
/// guarding against, and it would pass a test that merely looked at rendered output.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class OfflineSnapshotViewerTests(DetailSurfaceHarnessFixture fixture) : IDisposable
{
    private readonly string workDir = Directory.CreateTempSubdirectory("etlsnap-viewer-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The manifest under test, in the shape a snapshot actually carries.
    ///
    /// <para>Written as JSON and deserialized rather than assembled property by property: this is
    /// the same shape <c>tools/ui-sandbox/detail-surface.html</c> drives the runtime with, so the
    /// offline path and the online detail-surface tests are demonstrably exercising one report and
    /// not two subtly different ones.</para>
    /// </summary>
    private const string ManifestJson = """
        {
          "title": "Regional revenue (snapshot)",
          "parameters": { "@region": "North" },
          "pages": [
            { "name": "Overview", "structure": "A", "mode": "DASHBOARD", "slotMap": { "A": "RevenueByMonth" } },
            { "name": "Detail",   "structure": "A", "mode": "DASHBOARD", "slotMap": { "A": "RegionalDetail" } }
          ],
          "containers": [
            { "name": "TooltipBox", "containerType": "BOX", "structure": "A", "slotMap": { "A": "RegionalDetail" } }
          ],
          "visuals": [
            {
              "name": "RevenueByMonth",
              "visualType": "BAR",
              "columns": ["Month", "Revenue"],
              "rows": [["January", "320"], ["February", "290"], ["March", "410"]],
              "options": { "mapping:x": "Month", "mapping:y": "Revenue" },
              "nativeSvg": "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"360\" height=\"180\" viewBox=\"0 0 360 180\"><rect data-row-index=\"0\" x=\"20\" y=\"60\" width=\"80\" height=\"100\" fill=\"#2563eb\"></rect><rect data-row-index=\"1\" x=\"140\" y=\"30\" width=\"80\" height=\"130\" fill=\"#2563eb\"></rect><rect data-row-index=\"2\" x=\"260\" y=\"90\" width=\"80\" height=\"70\" fill=\"#2563eb\"></rect></svg>",
              "tooltip": {
                "type": "container",
                "mode": "popover",
                "containerRef": "TooltipBox",
                "resolvedVisuals": ["RegionalDetail"]
              }
            },
            {
              "name": "RegionalDetail",
              "visualType": "TABLE",
              "columns": ["Region", "Revenue"],
              "rows": [["North", "120"], ["South", "200"]]
            }
          ]
        }
        """;

    /// <summary>
    /// Builds the viewer for <see cref="ManifestJson"/> with one author bookmark that moves the
    /// reader to another page and selects another region, and returns its <c>file://</c> URL.
    /// </summary>
    private string WriteViewer(string fileName = "snapshot.offline.html")
    {
        var manifest = JsonSerializer.Deserialize<ReportManifest>(ManifestJson)
            ?? throw new InvalidOperationException("The test manifest did not deserialize.");

        manifest.Bookmarks =
        [
            new BookmarkManifest
            {
                Name = "SouthDetail",
                Title = "South · detail",
                State = new ResolvedReportState
                {
                    ActivePage = "Detail",
                    Parameters = { ["@region"] = ReportStateValue.FromString("South") },
                },
            },
        ];

        var path = Path.Combine(workDir, fileName);
        File.WriteAllText(path, OfflineSnapshotViewer.Build(manifest, DateTimeOffset.UnixEpoch));
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// The name of the page the reader is currently on. The runtime toggles pages with inline
    /// <c>display</c>, so this mirrors its own <c>getActivePage</c>.
    ///
    /// <para>The hash the runtime would normally write (<c>#bookmark=…</c>) is deliberately not
    /// asserted: a file opened from disk has an opaque origin, where <c>replaceState</c> throws.
    /// The state still applies, which is the property that matters.</para>
    /// </summary>
    private const string ActivePageName =
        "[...document.querySelectorAll('.page')]"
        + ".filter(el => el.style.display !== 'none').map(el => el.dataset.pageName)[0]";

    /// <summary>
    /// Aborts every http(s) request and records what was attempted, so "no network" is proved by
    /// what the page tried to do rather than by whether it happened to succeed.
    /// </summary>
    private static async Task<List<string>> FenceOffTheNetworkAsync(IPage page)
    {
        var attempted = new List<string>();
        await page.RouteAsync("http://**", async route =>
        {
            lock (attempted) attempted.Add(route.Request.Url);
            await route.AbortAsync();
        });
        await page.RouteAsync("https://**", async route =>
        {
            lock (attempted) attempted.Add(route.Request.Url);
            await route.AbortAsync();
        });
        return attempted;
    }

    [Fact]
    public async Task ExportedViewer_RendersTheReportWithoutReachingTheNetwork()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var attempted = await FenceOffTheNetworkAsync(page);

        await page.GotoAsync(WriteViewer());
        await page.WaitForSelectorAsync(".visual-card", new PageWaitForSelectorOptions { Timeout = 30_000 });

        Assert.Contains("RevenueByMonth", await page.Locator("#root").InnerTextAsync());

        // Every asset is inlined, so the document is the only thing the browser ever fetched.
        var external = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('script[src], link[href], img[src]')]"
            + ".map(el => el.getAttribute('src') || el.getAttribute('href'))");
        Assert.Empty(external);

        lock (attempted) Assert.Empty(attempted);
        Assert.Empty(session.PageErrors);
        Assert.Empty(session.ConsoleErrors);
    }

    [Fact]
    public async Task ExportedViewer_ReplaysAnAuthorBookmark()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var attempted = await FenceOffTheNetworkAsync(page);

        await page.GotoAsync(WriteViewer());
        await page.WaitForSelectorAsync(".visual-card", new PageWaitForSelectorOptions { Timeout = 30_000 });

        // The Views menu is the only way a reader reaches a bookmark in a snapshot: there is no
        // Portal saved-view API behind it, so if the menu is missing the feature is unreachable.
        await page.ClickAsync("#etlsql-views-menu-button");
        await page.ClickAsync(".bookmark-menu-item:has-text('South · detail')");

        // Applied atomically: the page moves and the parameter moves, or neither does.
        await page.WaitForFunctionAsync($"() => {ActivePageName} === 'Detail'");
        Assert.Equal("South", await page.EvaluateAsync<string>(
            "() => (window.__MANIFEST__.parameters || {})['@region']"));

        // The reader is told once that the figures are frozen. Applying bookmarked filters over
        // numbers that cannot move is the misleading outcome this notice exists to prevent.
        var toast = page.Locator(".etlsql-feedback-toast");
        await toast.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        Assert.Contains("snapshot", (await toast.First.InnerTextAsync()), StringComparison.OrdinalIgnoreCase);

        lock (attempted) Assert.Empty(attempted);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ExportedViewer_OpensADetailPopoverFromTheSnapshot()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var attempted = await FenceOffTheNetworkAsync(page);

        await page.GotoAsync(WriteViewer());
        await page.WaitForSelectorAsync("[data-row-index='1']", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await page.Locator("[data-row-index='1']").FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var surface = page.Locator(".report-chart-tooltip.report-chart-detail-pinned");
        await surface.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        // The refresh a popover performs on open has no server behind it here. It must resolve from
        // the manifest the page already carries, not degrade to the unavailable state.
        await page.Locator(".report-chart-tooltip-loading").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 10_000
        });

        var text = await surface.InnerTextAsync();
        Assert.Contains("RegionalDetail", text);
        Assert.Contains("South", text);
        Assert.DoesNotContain("could not be loaded", text, StringComparison.OrdinalIgnoreCase);

        lock (attempted) Assert.Empty(attempted);
        Assert.Empty(session.PageErrors);
    }
}
