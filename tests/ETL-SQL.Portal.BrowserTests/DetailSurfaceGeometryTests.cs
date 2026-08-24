using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Deterministic geometry fixtures for anchor-based detail placement.
///
/// <para>The old implementation followed the cursor and clamped to the viewport, which cannot
/// be asserted on: the result depended on where the mouse happened to be. Placement is now a
/// pure function of the anchor rect, the surface size, and the viewport, so every edge, flip,
/// shift, and RTL case is a table of numbers rather than a screenshot someone eyeballs.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class DetailSurfaceGeometryTests(DetailSurfaceHarnessFixture fixture)
{

    /// <summary>
    /// Result of the runtime placement function. Playwright materialises evaluate results by
    /// reflection, so this needs settable properties and a parameterless constructor.
    /// </summary>
    private sealed class Placement
    {
        public string Side { get; set; } = "";
        public double Left { get; set; }
        public double Top { get; set; }
        public bool Flipped { get; set; }
        public bool Shifted { get; set; }
    }

    /// <summary>Evaluates the runtime's placement function with synthetic geometry.</summary>
    private async Task<Placement> PlaceAsync(
        IPage page,
        (double Left, double Top, double Right, double Bottom) anchor,
        (double Width, double Height) size,
        (double Width, double Height) viewport,
        bool rtl = false)
    {
        var placement = await page.EvaluateAsync<Placement>(
            "a => window.__ETLSQL_DETAIL__.computeDetailPlacement(a.anchor, a.size, a.viewport, a.options)",
            new
            {
                anchor = new { left = anchor.Left, top = anchor.Top, right = anchor.Right, bottom = anchor.Bottom },
                size = new { width = size.Width, height = size.Height },
                viewport = new { width = viewport.Width, height = viewport.Height },
                options = new { rtl }
            });

        return placement ?? throw new InvalidOperationException("placement returned null");
    }

    private async Task<IPage> HarnessAsync(BrowserSession session)
    {
        var page = session.Page;
        await page.GotoAsync($"{fixture.BaseUrl}/tools/ui-sandbox/detail-surface.html");
        await page.WaitForFunctionAsync("() => !!window.__ETLSQL_DETAIL__", null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        return page;
    }

    // ── Preferred side and flipping ────────────────────────────────────────

    [Fact]
    public async Task WithRoomAbove_UsesThePreferredSide()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        var placement = await PlaceAsync(page, (100, 300, 200, 340), (200, 100), (1000, 800));

        Assert.Equal("top", placement.Side);
        Assert.False(placement.Flipped);
        Assert.Equal(190, placement.Top);   // 300 - 100 - gap(10)
        Assert.Equal(100, placement.Left);  // aligned to the anchor's leading edge
    }

    [Fact]
    public async Task WithoutRoomAbove_FlipsBelow()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        // Anchor hugs the top edge: 20 - 100 - 10 would land off-screen.
        var placement = await PlaceAsync(page, (100, 20, 200, 60), (200, 100), (1000, 800));

        Assert.Equal("bottom", placement.Side);
        Assert.True(placement.Flipped);
        Assert.Equal(70, placement.Top);    // 60 + gap(10)
    }

    [Fact]
    public async Task WithRoomOnNeitherVerticalSide_FallsBackToAHorizontalSide()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        // A short viewport with a tall surface: neither top nor bottom fits.
        var placement = await PlaceAsync(page, (100, 80, 200, 120), (200, 300), (1000, 200));

        Assert.Equal("right", placement.Side);
        Assert.Equal(210, placement.Left);  // 200 + gap(10)
    }

    // ── Shifting within viewport margins ───────────────────────────────────

    [Fact]
    public async Task NearTheRightEdge_ShiftsBackInsideTheMargin()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        var placement = await PlaceAsync(page, (900, 300, 980, 340), (200, 100), (1000, 800));

        Assert.True(placement.Shifted);
        // margin(8): 1000 - 200 - 8
        Assert.Equal(792, placement.Left);
    }

    [Fact]
    public async Task NearTheLeftEdge_ShiftsBackInsideTheMargin()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        var placement = await PlaceAsync(page, (2, 300, 60, 340), (200, 100), (1000, 800));

        Assert.Equal(8, placement.Left);
    }

    [Fact]
    public async Task OversizedContent_StaysClampedInsideTheViewport()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        // Surface larger than the viewport on both axes: it must still be positioned
        // deterministically at the margin rather than escaping off-screen.
        var placement = await PlaceAsync(page, (100, 300, 200, 340), (2000, 2000), (1000, 800));

        Assert.Equal(8, placement.Left);
        Assert.Equal(8, placement.Top);
    }

    [Fact]
    public async Task NarrowViewport_KeepsTheSurfaceOnScreen()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        var placement = await PlaceAsync(page, (300, 400, 340, 440), (280, 120), (360, 640));

        Assert.True(placement.Left >= 8);
        Assert.True(placement.Left + 280 <= 360 - 8 + 0.001);
    }

    // ── Right-to-left ──────────────────────────────────────────────────────

    [Fact]
    public async Task RightToLeft_AlignsToTheAnchorsTrailingEdge()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        var ltr = await PlaceAsync(page, (400, 300, 500, 340), (200, 100), (1000, 800));
        var rtl = await PlaceAsync(page, (400, 300, 500, 340), (200, 100), (1000, 800), rtl: true);

        Assert.Equal(400, ltr.Left);        // leading edge is the left in LTR
        Assert.Equal(300, rtl.Left);        // right(500) - width(200) in RTL
        Assert.NotEqual(ltr.Left, rtl.Left);
    }

    // ── Determinism ────────────────────────────────────────────────────────

    [Fact]
    public async Task PlacementIsPure_SameInputsGiveSameOutput()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await HarnessAsync(session);

        var first = await PlaceAsync(page, (100, 300, 200, 340), (200, 100), (1000, 800));
        var second = await PlaceAsync(page, (100, 300, 200, 340), (200, 100), (1000, 800));

        Assert.Equal(first.Side, second.Side);
        Assert.Equal(first.Left, second.Left);
        Assert.Equal(first.Top, second.Top);
        Assert.Equal(first.Flipped, second.Flipped);
        Assert.Equal(first.Shifted, second.Shifted);
    }

}
