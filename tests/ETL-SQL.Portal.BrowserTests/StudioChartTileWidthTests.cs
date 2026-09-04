using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Pins that a server-rendered chart uses the width of the design-canvas tile it is placed in.
///
/// <para>A native chart is drawn on one of three bounded server canvases and delivered as SVG
/// carrying that canvas's pixel width and height. The design canvas injected it as raw markup and
/// nothing sized it, so it laid out at its intrinsic size inside a card body that clips: the author
/// saw the top-left corner of a chart — a wide left axis margin, then whichever bars fell inside
/// the card — and dragging the tile wider only uncovered more of the same fixed drawing.</para>
///
/// <para>The assertion is the mechanism, not the picture. A card that merely contains an
/// <c>svg</c> element passed throughout the defect, so these measure: the drawing fills the tile's
/// width, the last mark is inside the tile rather than clipped away, and widening the tile moves
/// the marks apart. The last one is what the author actually reported, and it is the one that
/// cannot be satisfied by an SVG pinned to its own pixel size.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioChartTileWidthTests(PortalBrowserFixture fixture) : IAsyncLifetime
{
    private IHost? host;
    private string baseUrl = "";

    /// <summary>
    /// A chart shaped exactly as <c>PlotPlanSvgRenderer</c> emits one: an explicit pixel width and
    /// height for the canvas the layout tier chose, plus the viewBox that makes it scalable. The
    /// width attribute is the whole point of the fixture — an SVG without one would size itself to
    /// its container and the defect could not reproduce.
    /// </summary>
    private const string ServerChartSvg =
        "<svg xmlns='http://www.w3.org/2000/svg' width='600' height='350' viewBox='0 0 600 350' role='img'>"
        + "<rect width='600' height='350' fill='white'/>"
        + "<rect id='bar-0' x='76' y='92' width='96' height='198' fill='#5470c6'/>"
        + "<rect id='bar-1' x='206' y='75' width='96' height='215' fill='#5470c6'/>"
        + "<rect id='bar-2' x='336' y='40' width='96' height='250' fill='#5470c6'/>"
        + "<rect id='bar-3' x='466' y='85' width='96' height='205' fill='#5470c6'/>"
        + "</svg>";

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

    /// <summary>Geometry read back out of the browser after the canvas has laid the tile out.</summary>
    private sealed class TileGeometry
    {
        public double BodyLeft { get; set; }
        public double BodyWidth { get; set; }
        public double BodyHeight { get; set; }
        public double SvgLeft { get; set; }
        public double SvgWidth { get; set; }
        public double FirstBarLeft { get; set; }
        public double LastBarRight { get; set; }
    }

    private static async Task<TileGeometry> MeasureAsync(IPage page, int hostWidth)
    {
        var geometry = await page.EvaluateAsync<TileGeometry>(
            """
            async width => {
              document.querySelectorAll('#chart-tile-host').forEach(node => node.remove());
              const host = document.createElement('div');
              host.id = 'chart-tile-host';
              host.style.cssText =
                'position:fixed;left:0;top:0;height:700px;background:#fff;z-index:9999;width:' + width + 'px';
              document.body.appendChild(host);

              const mod = await import(
                '/src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js');

              const pkg = {
                format: 'etl-sql.snapshot',
                version: 2,
                reportName: 'Tile width probe',
                sampleRows: {
                  RevenueByRegion: [['North', '120'], ['South', '90'], ['East', '150'], ['West', '70']]
                },
                columnsByVisual: { RevenueByRegion: ['Region', 'Revenue'] },
                visualSvgs: { RevenueByRegion: window.__SERVER_CHART_SVG__ },
                metadata: { isSampled: true },
              };

              mod.createDesigner(host, {
                designState: {
                  pages: [{
                    id: 'p1', name: 'Page 1', mode: 'Dashboard',
                    visuals: [{
                      id: 'RevenueByRegion', name: 'RevenueByRegion', type: 'BAR',
                      title: 'Revenue by region',
                      gridCol: 1, gridColSpan: 12, gridRow: 1, gridRowSpan: 8,
                      dataset: 'sales', mappings: { X: 'Region', Y: 'Revenue' },
                    }],
                  }],
                  datasets: [{ name: 'sales', query: 'SELECT Region, Revenue FROM #t' }],
                },
                reportName: 'Tile width probe',
                snapshotMode: true,
                snapshotPackage: pkg,
              });

              // Two frames: one for the designer's own mount, one for the grid to settle.
              await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

              const body = host.querySelector('.etlsql-dsgn-vcard-body');
              if (!body) throw new Error('the design canvas rendered no visual card body');
              const svg = body.querySelector('svg');
              if (!svg) throw new Error('the visual card body holds no chart');

              const bodyRect = body.getBoundingClientRect();
              const svgRect = svg.getBoundingClientRect();
              const first = svg.querySelector('#bar-0').getBoundingClientRect();
              const last = svg.querySelector('#bar-3').getBoundingClientRect();

              return {
                bodyLeft: bodyRect.left, bodyWidth: bodyRect.width, bodyHeight: bodyRect.height,
                svgLeft: svgRect.left, svgWidth: svgRect.width,
                firstBarLeft: first.left, lastBarRight: last.right,
              };
            }
            """,
            hostWidth);

        return geometry ?? throw new InvalidOperationException("the tile probe returned nothing");
    }

    [Fact]
    public async Task DesignCanvasChartTile_FillsItsWidth_AndRedistributesWhenWidened()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await page.GotoAsync($"{baseUrl}/tools/ui-sandbox/index.html");
        await page.WaitForSelectorAsync(".story-link", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });
        await page.EvaluateAsync("svg => { window.__SERVER_CHART_SVG__ = svg; }", ServerChartSvg);

        var narrow = await MeasureAsync(page, 900);

        // The chart is laid out against the tile, not against the pixel size the server drew it at.
        // Before the fix the SVG measured its own 600px however wide the tile was.
        Assert.True(narrow.BodyWidth > 40,
            $"the card body collapsed to {narrow.BodyWidth}px, so nothing is being measured.");
        Assert.Equal(narrow.BodyWidth, narrow.SvgWidth, 1);
        Assert.Equal(narrow.BodyLeft, narrow.SvgLeft, 1);

        // Every bar is inside the tile. The reported symptom was the opposite: the drawing ran past
        // the card's clipping edge, so the bars on the right were simply not there.
        Assert.InRange(narrow.LastBarRight, narrow.BodyLeft, narrow.BodyLeft + narrow.BodyWidth + 1);

        // The one the author actually reported: widen the tile and the bars move apart. An SVG
        // pinned to its own pixel width draws the identical picture at every tile size.
        var wide = await MeasureAsync(page, 1500);

        Assert.True(wide.BodyWidth > narrow.BodyWidth + 200,
            $"the probe did not widen the tile: {narrow.BodyWidth}px then {wide.BodyWidth}px.");
        Assert.Equal(wide.BodyWidth, wide.SvgWidth, 1);

        var narrowSpan = narrow.LastBarRight - narrow.FirstBarLeft;
        var wideSpan = wide.LastBarRight - wide.FirstBarLeft;
        Assert.True(wideSpan > narrowSpan + 100,
            $"widening the tile from {narrow.BodyWidth}px to {wide.BodyWidth}px left the bars spanning "
            + $"{narrowSpan}px and then {wideSpan}px, so the chart is not using the width it is given.");
        Assert.InRange(wide.LastBarRight, wide.BodyLeft, wide.BodyLeft + wide.BodyWidth + 1);
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
