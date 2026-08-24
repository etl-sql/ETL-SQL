using System.Diagnostics;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Interaction latency for detail surfaces, measured on a named fixture.
///
/// <para><b>Fixture.</b> The kitchen-sink tooltip example from
/// <c>samples/10_Kitchen_Sinks/01_BAR.rptsql</c>, reproduced by
/// <c>tools/ui-sandbox/detail-surface.html</c>: <c>#sales</c> is 8 rows × 4 columns, the
/// trigger visual (<c>BarWithTooltip</c>) groups it to 4 marks, and the detail visual
/// (<c>MonthDetail</c>) renders the 2 regions for the activated month through the
/// <c>TooltipBox</c> container. That is a small payload on purpose — it is the shape the
/// sample actually ships, so a regression here is a regression in real authored reports.</para>
///
/// <para><b>Thresholds.</b> Deliberately coarse absolute ceilings, not tuned targets. These
/// are regression tripwires for work that is O(marks) or worse — a reflow storm, an unfenced
/// refresh, a listener leak — and they must not encode an unmeasured sub-millisecond claim.
/// A headless CI agent is an order of magnitude noisier than a developer machine, so the
/// tolerance is set well above observed values rather than at them. Measured values are
/// written to test output on every run, which is the record to compare against when
/// re-baselining.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(DetailSurfaceCollection.Name)]
public sealed class DetailSurfacePerformanceTests(DetailSurfaceHarnessFixture fixture, ITestOutputHelper output)
{
    /// <summary>Transient text has no network step; opening it is pure DOM work.</summary>
    private const int TransientOpenBudgetMs = 400;

    /// <summary>Popover open through refresh completion, including the parameter round trip.</summary>
    private const int RefreshCompleteBudgetMs = 2_500;

    /// <summary>Repositioning after a scroll or resize must not rebuild content.</summary>
    private const int RepositionBudgetMs = 300;

    /// <summary>Dismissal tears down the surface, its listeners, and its observers.</summary>
    private const int DismissBudgetMs = 300;

    private const string Mark = "[data-row-index='1']";
    private const string Surface = ".report-chart-tooltip";
    private const string Loading = ".report-chart-tooltip-loading";

    private async Task<IPage> OpenAsync(BrowserSession session, string surface)
    {
        var page = session.Page;
        await page.GotoAsync($"{fixture.BaseUrl}/tools/ui-sandbox/detail-surface.html?surface={surface}&spacer=3000");
        await page.WaitForSelectorAsync(Mark, new PageWaitForSelectorOptions { Timeout = 30_000 });
        return page;
    }

    private void Record(string measure, double ms, int budget)
    {
        output.WriteLine($"detail-surface {measure}: {ms:0.0} ms (budget {budget} ms) " +
                         "fixture=kitchen-sink 01_BAR #sales 8x4, 4 marks, 2 detail rows");
    }

    [Fact]
    public async Task TransientOpenLatency_StaysWithinBudget()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "tooltip");

        // Warm the path once so the measurement is not dominated by first-render costs.
        await page.Locator(Mark).HoverAsync();
        await page.WaitForSelectorAsync(Surface);
        await page.Mouse.MoveAsync(5, 5);
        await page.Locator(Surface).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        var timer = Stopwatch.StartNew();
        await page.Locator(Mark).HoverAsync();
        await page.WaitForSelectorAsync(Surface);
        timer.Stop();

        Record("transient open", timer.Elapsed.TotalMilliseconds, TransientOpenBudgetMs);
        Assert.True(timer.Elapsed.TotalMilliseconds < TransientOpenBudgetMs,
            $"transient open took {timer.Elapsed.TotalMilliseconds:0.0} ms, budget {TransientOpenBudgetMs} ms");
    }

    [Fact]
    public async Task DetailRefreshCompletion_StaysWithinBudget()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "popover");

        var timer = Stopwatch.StartNew();
        await page.Locator(Mark).ClickAsync();
        await page.Locator($"{Surface}.report-chart-detail-pinned").WaitForAsync();
        await page.Locator(Loading).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 15_000
        });
        timer.Stop();

        Record("refresh complete", timer.Elapsed.TotalMilliseconds, RefreshCompleteBudgetMs);
        Assert.True(timer.Elapsed.TotalMilliseconds < RefreshCompleteBudgetMs,
            $"refresh took {timer.Elapsed.TotalMilliseconds:0.0} ms, budget {RefreshCompleteBudgetMs} ms");
    }

    [Fact]
    public async Task RepositionWork_StaysWithinBudget()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "popover");

        await page.Locator(Mark).ClickAsync();
        await page.Locator(Loading).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 15_000
        });

        // Measured inside the page: a scroll that triggers repositioning must settle without
        // rebuilding the surface's content.
        var elapsed = await page.EvaluateAsync<double>("""
            async () => {
              const started = performance.now();
              window.scrollBy(0, 300);
              await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
              return performance.now() - started;
            }
            """);

        Record("reposition", elapsed, RepositionBudgetMs);
        Assert.True(elapsed < RepositionBudgetMs,
            $"reposition took {elapsed:0.0} ms, budget {RepositionBudgetMs} ms");

        // The surface must still be the one that was already open, not a rebuilt one.
        Assert.Equal(1, await page.Locator(Surface).CountAsync());
    }

    [Fact]
    public async Task DismissalAndCleanup_StayWithinBudget()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "popover");

        await page.Locator(Mark).ClickAsync();
        await page.Locator(Loading).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 15_000
        });

        var timer = Stopwatch.StartNew();
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(Surface).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        timer.Stop();

        Record("dismiss + cleanup", timer.Elapsed.TotalMilliseconds, DismissBudgetMs);
        Assert.True(timer.Elapsed.TotalMilliseconds < DismissBudgetMs,
            $"dismissal took {timer.Elapsed.TotalMilliseconds:0.0} ms, budget {DismissBudgetMs} ms");
    }

    [Fact]
    public async Task RepeatedOpenAndDismiss_LeaksNoSurfacesOrRegions()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = await OpenAsync(session, "popover");

        // A leak here is what turns a fast interaction into a slow report over a session:
        // orphaned surfaces accumulate scroll/resize listeners and ResizeObservers.
        for (var i = 0; i < 12; i++)
        {
            await page.Locator($"[data-row-index='{i % 3}']").ClickAsync();
            await page.Keyboard.PressAsync("Escape");
        }

        Assert.Equal(0, await page.Locator(Surface).CountAsync());
        Assert.True(await page.Locator("#report-detail-live").CountAsync() <= 1,
            "the polite region must be shared, not created per surface");
    }
}
