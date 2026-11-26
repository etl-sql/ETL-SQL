using ETL_SQL.App;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Real-Chromium verification of <c>PAGE OPTIONS (FIT = 'SCREEN', MIN_HEIGHT, ROW_WEIGHTS)</c>
/// and card-embedded dark-theme SVGs using the real kitchen-sink report.
/// </summary>
[Trait("Category", "Browser")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class FitPageSizingTests(DetailSurfaceHarnessFixture fixture) : IDisposable
{
    private readonly string workDir = Directory.CreateTempSubdirectory("fit-page-sizing-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FitGeometry
    {
        public double ViewportHeight { get; set; }
        public double PageHeight { get; set; }
        public double GridHeight { get; set; }
        public double GridScrollHeight { get; set; }
        public string GridRows { get; set; } = string.Empty;
        public double ChartWrapperHeight { get; set; }
        public double SvgHeight { get; set; }
        public double TableCardHeight { get; set; }
        public bool HasWhiteBgRect { get; set; }
        public double H3MarginTop { get; set; }
    }

    [Fact]
    public async Task KitchenSinkOverview_FitsViewportWithRowWeights_RespectsMinHeight_AndRendersDarkCardSvg()
    {
        var scriptPath = FindRepoFile(Path.Combine("samples", "10_Kitchen_Sinks", "report_kitchen_sink.rptsql"));
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var service = new DashboardService(scriptPath, provider.GetRequiredService<IServiceScopeFactory>());
        var manifest = await service.GetManifestAsync();
        Assert.NotEmpty(manifest.Pages);
        Assert.NotEmpty(manifest.Visuals);

        var htmlPath = Path.Combine(workDir, "kitchen-sink-fit.html");
        File.WriteAllText(htmlPath, OfflineSnapshotViewer.Build(manifest, DateTimeOffset.UnixEpoch));
        var url = new Uri(htmlPath).AbsoluteUri;

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.SetViewportSizeAsync(1440, 900);

        await page.GotoWithDiagnosticsAsync(url);
        await page.WaitForTimeoutAsync(1000);

        Assert.True(session.PageErrors.Count == 0, "PageErrors: " + string.Join(" | ", session.PageErrors));
        Assert.True(session.ConsoleErrors.Count == 0, "ConsoleErrors: " + string.Join(" | ", session.ConsoleErrors));
        var rootText = await page.EvaluateAsync<string>("() => document.body.innerText.slice(0, 500)");
        Assert.True(await page.Locator(".visual-card").CountAsync() > 0, $"No .visual-card rendered. Body: {rootText}");
        var debugState = await page.EvaluateAsync<string>(
            "() => JSON.stringify([...document.querySelectorAll('.page')].map(p => ({ id: p.id, cls: p.className, fit: p.dataset.fit, minH: p.dataset.fitMinHeight, rw: p.dataset.fitRowWeights, svgs: p.querySelectorAll('.native-chart-wrapper svg').length })))");
        Assert.True(
            await page.Locator(".page[data-fit='SCREEN'].active .native-chart-wrapper svg").CountAsync() > 0,
            $"No active fit-screen SVG found. Pages: {debugState}");

        var desktop = await MeasureAsync(page);
        Assert.Empty(session.PageErrors);
        Assert.InRange(desktop.PageHeight, 700, desktop.ViewportHeight);
        Assert.True(desktop.GridScrollHeight <= desktop.PageHeight + 2,
            $"Single-page FIT='SCREEN' overflowed: gridScrollHeight={desktop.GridScrollHeight} > pageHeight={desktop.PageHeight}");
        Assert.Equal("auto minmax(0px, 2fr) minmax(0px, 2fr) minmax(0px, 1.5fr)", desktop.GridRows);
        Assert.True(desktop.ChartWrapperHeight >= 100, $"Expected chart wrapper >= 100px, got {desktop.ChartWrapperHeight}");
        Assert.True(desktop.SvgHeight >= desktop.ChartWrapperHeight * 0.85,
            $"Expected SVG height ({desktop.SvgHeight}) to fill chart wrapper ({desktop.ChartWrapperHeight})");
        Assert.False(desktop.HasWhiteBgRect, "Embedded dark card SVG should not paint an opaque white background rect.");
        Assert.Equal(0, desktop.H3MarginTop);

        // Collapsing the first chart row switches its grid track to auto and expands the table row.
        var beforeTableHeight = desktop.TableCardHeight;
        await page.Locator(".page[data-fit='SCREEN'].active .visual-card:has(.chart-wrapper) .visual-collapse-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(450);
        var collapsed = await MeasureAsync(page);
        Assert.Equal("auto auto minmax(0px, 2fr) minmax(0px, 1.5fr)", collapsed.GridRows);
        Assert.True(collapsed.TableCardHeight > beforeTableHeight + 30,
            $"Expected table card to expand after collapsing a chart row ({beforeTableHeight} -> {collapsed.TableCardHeight})");

        // Expand it back and confirm the automatic row-budget floor holds on a short viewport.
        await page.Locator(".page[data-fit='SCREEN'].active .visual-card.visual-collapsed .visual-collapse-btn").First.ClickAsync();
        await page.SetViewportSizeAsync(1440, 600);
        await page.WaitForTimeoutAsync(450);

        var shortViewport = await MeasureAsync(page);
        Assert.True(shortViewport.PageHeight >= 710, $"Expected automatic row-budget floor >= 716px, got {shortViewport.PageHeight}");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ScreenFit_ReleasesResizeListenersOnRefresh_AndShowsTableSubtitleOnMobile()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.SetViewportSizeAsync(1400, 900);
        await page.SetContentAsync("<main id=\"root\"></main>");
        await page.EvaluateAsync(
            """
            () => {
              const resizeListeners = new Set();
              const add = window.addEventListener.bind(window);
              const remove = window.removeEventListener.bind(window);
              window.addEventListener = (type, listener, options) => {
                if (type === 'resize') resizeListeners.add(listener);
                add(type, listener, options);
              };
              window.removeEventListener = (type, listener, options) => {
                if (type === 'resize') resizeListeners.delete(listener);
                remove(type, listener, options);
              };
              window.getResizeListenerCount = () => resizeListeners.size;
              window.acquireVsCodeApi = () => ({ postMessage() {} });
              window.__MANIFEST__ = {
                type: 'reportManifest',
                pages: [{ name: 'Overview', structure: 'tile', slotMap: { tile: 'Status' }, options: { FIT: 'SCREEN' } }],
                visuals: [{
                  name: 'Status', visualType: 'TABLE', columns: ['Status'], rows: [['Ready']],
                  options: { SUBTITLE: 'Current status' },
                }],
              };
            }
            """);
        var resources = Path.Combine("src", "ETL-SQL.ReportRuntime", "Resources", "Shared");
        await page.AddStyleTagAsync(new() { Path = FindRepoFile(Path.Combine(resources, "report-runtime.css")) });
        await page.AddScriptTagAsync(new() { Path = FindRepoFile(Path.Combine(resources, "report-runtime.bundle.js")) });

        var subtitle = page.Locator(".page[data-fit='SCREEN'] .visual-subtitle");
        await subtitle.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        Assert.False(await subtitle.IsVisibleAsync());
        var initialListeners = await page.EvaluateAsync<int>("() => window.getResizeListenerCount()");
        Assert.True(initialListeners > 0);

        await page.EvaluateAsync(
            """
            async () => {
              const root = document.getElementById('root');
              for (let refresh = 0; refresh < 5; refresh++) {
                await new Promise(resolve => {
                  const observer = new MutationObserver(() => {
                    observer.disconnect();
                    resolve();
                  });
                  observer.observe(root, { childList: true });
                  window.postMessage(window.__MANIFEST__, '*');
                });
              }
            }
            """);
        Assert.Equal(initialListeners, await page.EvaluateAsync<int>("() => window.getResizeListenerCount()"));

        await page.SetViewportSizeAsync(630, 760);
        await subtitle.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Equal("Current status", await subtitle.TextContentAsync());
        Assert.Empty(session.PageErrors);
    }

    private static async Task<FitGeometry> MeasureAsync(IPage page) =>
        await page.EvaluateAsync<FitGeometry>(
            """
            () => {
              const p = document.querySelector('.page[data-fit="SCREEN"].active');
              const grid = p.querySelector('.page-grid');
              const wrapper = p.querySelector('.native-chart-wrapper');
              const svg = wrapper.querySelector('svg');
              const h3 = p.querySelector('.visual-card > h3');
              const tableCard = p.querySelector('.visual-card:has(.table-wrapper)');
              const pRect = p.getBoundingClientRect();
              const gRect = grid.getBoundingClientRect();
              const wRect = wrapper.getBoundingClientRect();
              const sRect = svg.getBoundingClientRect();
              const tRect = tableCard ? tableCard.getBoundingClientRect() : { height: 0 };
              const hasWhiteBgRect = Array.from(svg.querySelectorAll('rect'))
                .some(r => (r.getAttribute('fill') || '').toLowerCase() === 'white');
              return {
                viewportHeight: window.innerHeight,
                pageHeight: pRect.height,
                gridHeight: gRect.height,
                gridScrollHeight: grid.scrollHeight,
                gridRows: grid.style.gridTemplateRows || '',
                chartWrapperHeight: wRect.height,
                svgHeight: sRect.height,
                tableCardHeight: tRect.height,
                hasWhiteBgRect,
                h3MarginTop: parseFloat(getComputedStyle(h3).marginTop) || 0,
              };
            }
            """);

    private static string FindRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ETL-SQL.slnx")))
                return Path.Combine(current.FullName, relativePath);
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
