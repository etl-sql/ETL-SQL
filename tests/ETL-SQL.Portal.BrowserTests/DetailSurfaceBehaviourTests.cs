using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Behavioural tests for the shared detail-surface controller, driven through a real browser
/// against the <b>canonical</b> <c>report-runtime.js</c>.
///
/// <para>These deliberately replace string-presence assertions against the runtime source. A
/// test that greps for <c>function attachDetailSurface</c> passes just as happily when the
/// controller never opens, never returns focus, or leaves a popover orphaned after a refresh —
/// which is exactly the class of defect this feature had. Everything here asserts on what a
/// user or a screen reader would actually observe.</para>
///
/// <para>The harness at <c>tools/ui-sandbox/detail-surface.html</c> supplies a hand-written SVG
/// with fixed <c>data-row-index</c> marks, so geometry and focus order are deterministic and no
/// Portal, database, login, or report execution is involved.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class DetailSurfaceBehaviourTests(DetailSurfaceHarnessFixture fixture)
{

    private const string Mark = "[data-row-index='1']";
    private const string Surface = ".report-chart-tooltip";

    private const string Loading = ".report-chart-tooltip-loading";

    private async Task<IPage> OpenAsync(BrowserSession session, string surface = "popover", string query = "")
    {
        var page = session.Page;
        await page.GotoAsync($"{fixture.BaseUrl}/tools/ui-sandbox/detail-surface.html?surface={surface}{query}");
        await page.WaitForSelectorAsync(Mark, new PageWaitForSelectorOptions { Timeout = 30_000 });
        return page;
    }

    /// <summary>
    /// Waits for a popover to finish its refresh. Opening one shows a loading state first, so
    /// asserting on content immediately after the surface appears races the response.
    /// </summary>
    private static async Task AwaitDetailAsync(IPage page)
    {
        await page.Locator($"{Surface}.report-chart-detail-pinned").WaitForAsync();
        await page.Locator(Loading).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 15_000
        });
    }

    /// <summary>
    /// Serves the parameter-refresh endpoint the popover calls, echoing the page's own manifest
    /// with an optional delay so overlapping refreshes can be ordered deterministically.
    /// </summary>
    private static async Task InterceptRefreshAsync(IPage page, Func<int, int> delayForCall)
    {
        var calls = 0;
        await page.RouteAsync("**/api/parameters", async route =>
        {
            var index = Interlocked.Increment(ref calls) - 1;
            var delay = delayForCall(index);
            if (delay > 0) await Task.Delay(delay);
            var manifest = await page.EvaluateAsync<string>("() => JSON.stringify(window.__MANIFEST__)");
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = manifest
            });
        });
    }

    // ── Discoverability ────────────────────────────────────────────────────

    [Fact]
    public async Task MarksExposingDetail_AreFocusableAndNamed()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);
        var mark = page.Locator(Mark);

        // Hover-only detail is not acceptable: the mark must be reachable and named.
        Assert.Equal("0", await mark.GetAttributeAsync("tabindex"));
        Assert.Equal("button", await mark.GetAttributeAsync("role"));
        Assert.Equal("dialog", await mark.GetAttributeAsync("aria-haspopup"));

        var label = await mark.GetAttributeAsync("aria-label");
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.Contains("February", label);
    }

    // ── Hover preview (fine pointer) ───────────────────────────────────────

    [Fact]
    public async Task Hovering_ShowsATransientPreview_AndLeavingDismissesIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "tooltip");

        await page.Locator(Mark).HoverAsync();
        await page.WaitForSelectorAsync(Surface);
        Assert.Equal("Revenue for the month", (await page.Locator($"{Surface} .report-chart-tooltip-text").InnerTextAsync()).Trim());

        // Moving off the mark always dismisses an unpinned surface: it never bridges to
        // the pointer, so there is no ambiguous in-between state.
        await page.Mouse.MoveAsync(5, 5);
        await page.Locator(Surface).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    // ── Keyboard ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public async Task ActivatingAMarkFromTheKeyboard_PinsThePopover(string key)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).FocusAsync();
        await page.Keyboard.PressAsync(key);

        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");
        Assert.Equal("true", await page.Locator(Mark).GetAttributeAsync("aria-expanded"));
    }

    [Fact]
    public async Task EscapeClosesThePopover_AndReturnsFocusToTheTrigger()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(Surface).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // Focus must come back to where the user was, not to the body.
        var focusedRow = await page.EvaluateAsync<string?>("() => document.activeElement?.getAttribute('data-row-index')");
        Assert.Equal("1", focusedRow);
    }

    [Fact]
    public async Task SpaceActivation_DoesNotScrollThePage()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "popover", "&spacer=3000");

        await page.Locator(Mark).FocusAsync();
        await page.Keyboard.PressAsync(" ");
        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");

        Assert.Equal(0, await page.EvaluateAsync<int>("() => Math.round(window.scrollY)"));
    }

    // ── Pointer pinning and dismissal ──────────────────────────────────────

    [Fact]
    public async Task ClickingAMark_PinsThePopover_AndSurvivesPointerLeave()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).ClickAsync();
        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");

        // The whole point of pinning: interactive detail must not vanish when the pointer
        // leaves the mark it was opened from.
        await page.Mouse.MoveAsync(5, 5);
        await page.WaitForTimeoutAsync(250);
        Assert.True(await page.Locator(Surface).IsVisibleAsync());
    }

    [Fact]
    public async Task ReactivatingTheTrigger_TogglesThePopoverClosed()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).ClickAsync();
        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");

        await page.Locator(Mark).ClickAsync();
        await page.Locator(Surface).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    [Fact]
    public async Task ClickingOutside_ClosesThePinnedPopover()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).ClickAsync();
        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");

        await page.Mouse.ClickAsync(5, 5);
        await page.Locator(Surface).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    [Fact]
    public async Task OpeningASecondDetailSurface_ClosesTheFirst()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator("[data-row-index='0']").ClickAsync();
        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");

        await page.Locator("[data-row-index='2']").ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // Exactly one detail surface exists in the document at any time.
        Assert.Equal(1, await page.Locator(Surface).CountAsync());
        Assert.Equal("false", await page.Locator("[data-row-index='0']").GetAttributeAsync("aria-expanded"));
        Assert.Equal("true", await page.Locator("[data-row-index='2']").GetAttributeAsync("aria-expanded"));
    }

    // ── Touch ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tapping_OpensThePinnedPopoverDirectly()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        // A coarse pointer has no hover state, so there is no preview step to get stuck in:
        // the first tap must produce the pinned surface.
        await page.Locator(Mark).DispatchEventAsync("pointerover", new { pointerType = "touch" });
        await page.WaitForTimeoutAsync(250);
        Assert.Equal(0, await page.Locator(Surface).CountAsync());

        await page.Locator(Mark).ClickAsync();
        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");
    }

    // ── Repositioning ──────────────────────────────────────────────────────

    [Fact]
    public async Task ScrollingThePage_KeepsTheSurfaceAnchoredToItsMark()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "popover", "&spacer=3000");

        await page.Locator(Mark).ClickAsync();
        await AwaitDetailAsync(page);

        await AssertAnchoredAsync(page);

        await page.Mouse.WheelAsync(0, 400);
        await page.WaitForTimeoutAsync(300);

        // Scrolling the mark towards the top edge legitimately flips the surface below it.
        // What must hold either way is the anchoring itself: the gap to the mark is preserved
        // on whichever side was chosen, and the surface stays inside the viewport.
        await AssertAnchoredAsync(page);
    }

    /// <summary>
    /// Asserts the surface sits one gap away from its mark on the side it reports, and is
    /// fully inside the viewport. Uses client rects so the check is scroll-independent.
    /// </summary>
    private static async Task AssertAnchoredAsync(IPage page)
    {
        var geometry = await page.EvaluateAsync<Anchoring>("""
            () => {
              const mark = document.querySelector("[data-row-index='1']").getBoundingClientRect();
              const el = document.querySelector('.report-chart-tooltip');
              const s = el.getBoundingClientRect();
              return {
                side: el.dataset.side,
                gap: el.dataset.side === 'top' ? mark.top - s.bottom
                   : el.dataset.side === 'bottom' ? s.top - mark.bottom
                   : el.dataset.side === 'right' ? s.left - mark.right
                   : mark.left - s.right,
                left: s.left, top: s.top, right: s.right, bottom: s.bottom,
                viewportWidth: window.innerWidth, viewportHeight: window.innerHeight
              };
            }
            """) ?? throw new InvalidOperationException("no geometry");

        Assert.False(string.IsNullOrEmpty(geometry.Side));
        Assert.True(Math.Abs(geometry.Gap - 10) < 2, $"gap on '{geometry.Side}' was {geometry.Gap}, expected ~10");
        Assert.True(geometry.Left >= -1 && geometry.Top >= -1, $"surface escaped: {geometry.Left},{geometry.Top}");
        Assert.True(geometry.Right <= geometry.ViewportWidth + 1, "surface overflows the right edge");
        Assert.True(geometry.Bottom <= geometry.ViewportHeight + 1, "surface overflows the bottom edge");
    }

    private sealed class Anchoring
    {
        public string Side { get; set; } = "";
        public double Gap { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
    }

    [Fact]
    public async Task ResizingTheViewport_RepositionsTheSurface()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).ClickAsync();
        await AwaitDetailAsync(page);
        var before = await page.Locator(Surface).BoundingBoxAsync();

        await page.SetViewportSizeAsync(700, 500);
        await page.WaitForTimeoutAsync(300);
        var after = await page.Locator(Surface).BoundingBoxAsync();

        Assert.NotNull(before);
        Assert.NotNull(after);
        // Still fully inside the narrowed viewport.
        Assert.True(after!.X >= 0 && after.X + after.Width <= 700 + 1, $"x={after.X} w={after.Width}");
    }

    // ── Accessibility contracts ────────────────────────────────────────────

    [Fact]
    public async Task TransientText_UsesTheTooltipContract()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "tooltip");

        await page.Locator(Mark).HoverAsync();
        await page.WaitForSelectorAsync(Surface);

        var surface = page.Locator(Surface);
        Assert.Equal("tooltip", await surface.GetAttributeAsync("role"));

        // The trigger points at it, and it holds nothing focusable.
        var describedBy = await page.Locator(Mark).GetAttributeAsync("aria-describedby");
        Assert.Equal(await surface.GetAttributeAsync("id"), describedBy);
        Assert.Equal(0, await page.Locator($"{Surface} a, {Surface} button, {Surface} input, {Surface} [tabindex]").CountAsync());
    }

    [Fact]
    public async Task PinnedDetail_UsesTheLabelledDialogContract()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).ClickAsync();
        var surface = page.Locator($"{Surface}.report-chart-detail-pinned");
        await surface.WaitForAsync();

        // Interactive detail is never role="tooltip": a tooltip may not own focusable content.
        Assert.Equal("dialog", await surface.GetAttributeAsync("role"));
        var label = await surface.GetAttributeAsync("aria-label");
        Assert.False(string.IsNullOrWhiteSpace(label));

        // A pinned dialog must not also be announced inline as the trigger's description.
        Assert.Null(await page.Locator(Mark).GetAttributeAsync("aria-describedby"));
    }

    [Fact]
    public async Task LiveRegion_IsPolite_AndDoesNotFloodOnRepeatedHover()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).ClickAsync();
        await page.WaitForSelectorAsync("#report-detail-live");

        var region = page.Locator("#report-detail-live");
        Assert.Equal("polite", await region.GetAttributeAsync("aria-live"));
        Assert.Equal("status", await region.GetAttributeAsync("role"));

        // One region for the whole report, not one per surface.
        Assert.Equal(1, await page.Locator("#report-detail-live").CountAsync());
    }

    // ── Inline VISUALS actually render ─────────────────────────────────────

    [Fact]
    public async Task InlineVisualsForm_RendersItsMarkdownAndItsVisuals()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "inline");

        await page.Locator(Mark).ClickAsync();
        await AwaitDetailAsync(page);

        // This form previously parsed and serialized, then rendered nothing at all.
        var text = await page.Locator(Surface).InnerTextAsync();
        Assert.Contains("Regional breakdown", text);
        Assert.Contains("North", text);
    }

    // ── Referenced container renders a nested chart ────────────────────────

    [Fact]
    public async Task ReferencedContainer_RendersTheNestedVisual()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).ClickAsync();
        await AwaitDetailAsync(page);

        var text = await page.Locator(Surface).InnerTextAsync();
        Assert.Contains("February", text);   // the row context
        Assert.Contains("North", text);      // the nested visual's content
    }

    // ── Stale refresh fencing ──────────────────────────────────────────────

    [Fact]
    public async Task ARefreshForASupersededRow_NeverReplacesCurrentDetail()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        // First request is slow, second is immediate: the stale response therefore lands
        // last and would overwrite the newer detail if generations were not fenced.
        await InterceptRefreshAsync(page, call => call == 0 ? 1500 : 0);

        await page.Locator("[data-row-index='0']").ClickAsync();
        await page.Locator("[data-row-index='2']").ClickAsync();

        await AwaitDetailAsync(page);
        await page.WaitForTimeoutAsync(2000); // outlive the slow first response

        // The surface must still belong to the row the user actually activated last.
        Assert.Equal("true", await page.Locator("[data-row-index='2']").GetAttributeAsync("aria-expanded"));
        Assert.Contains("March", await page.Locator(Surface).InnerTextAsync());
        Assert.DoesNotContain("January", await page.Locator(Surface).InnerTextAsync());
    }

    [Fact]
    public async Task ARefreshLandingAfterDismissal_DoesNotReopenTheSurface()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);
        await InterceptRefreshAsync(page, _ => 1200);

        await page.Locator(Mark).ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(2000);

        Assert.Equal(0, await page.Locator(Surface).CountAsync());
    }

    // ── Refresh and unmount cleanup ────────────────────────────────────────

    [Fact]
    public async Task ReRenderingTheReport_ClosesAnOpenDetailSurface()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.Locator(Mark).ClickAsync();
        await page.WaitForSelectorAsync($"{Surface}.report-chart-detail-pinned");

        // The surface is appended to document.body, so clearing the report root would
        // otherwise leave it orphaned and anchored to marks that no longer exist. This is
        // the same teardown the runtime runs before it clears the root on a re-render.
        await page.EvaluateAsync("() => window.__ETLSQL_DETAIL__.destroyIn(document)");

        await page.Locator(Surface).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.Equal(0, await page.Locator(Surface).CountAsync());
    }

    [Fact]
    public async Task TearingDownTheVisual_DetachesItsListeners()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session);

        await page.EvaluateAsync("() => window.__ETLSQL_DETAIL__.destroyIn(document)");

        // After teardown the marks are inert: activating one must not resurrect a surface.
        await page.Locator(Mark).ClickAsync();
        await page.WaitForTimeoutAsync(250);
        Assert.Equal(0, await page.Locator(Surface).CountAsync());
    }

}
