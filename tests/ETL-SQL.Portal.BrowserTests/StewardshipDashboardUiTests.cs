using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The governance dashboard's four "nothing to show" states, driven through a real browser.
///
/// <para>These states are worth a browser test precisely because they all look similar — a page
/// with no rows on it. A steward who cannot tell "you are not allowed to see this" from "the
/// service is down" from "nothing has been computed yet" from "the estate is genuinely clean" will
/// read every one of them as the last, which is the only reading that is safe to act on and the
/// most likely to be wrong. So each state is asserted by the marker it renders <em>and</em> by
/// wording a person could act on.</para>
///
/// <para>Driving it through the shipped page also proves the wiring: the module is imported, its
/// route resolves, its API client hits real endpoints, and nothing throws on the way. A unit test
/// of the module can pass while the page never loads it.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class GovernanceDashboardUiTests(PortalBrowserFixture fixture)
{
    [Fact]
    public async Task NeverScannedEstate_SaysSo_RatherThanShowingACleanBillOfHealth()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await OpenGovernanceOverviewAsync(page);

        // The whole point: zero findings on a never-scanned estate means "not measured", and the
        // page must say which one it is before anyone reads the tiles as compliance.
        await Expect(page.Locator("[data-gov-state='never-scanned']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-gov-state='never-scanned']"))
            .ToContainTextAsync("never been scanned");
        await Expect(page.Locator("[data-gov-state='never-scanned']"))
            .ToContainTextAsync("not because none exist");

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task LiveData_RendersScoresWithTheRuleThatTookEachPoint()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await OpenGovernanceOverviewAsync(page);

        // A scan is the only thing that turns lineage into findings, so run one through the UI.
        await page.ClickAsync("#btnRunScan");
        await Expect(page.Locator("[data-gov-state='scanned']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // Reached from the Governance sidebar. It used to be an in-page tab strip, until the two
        // competing menus were collapsed into one; this still clicked the tab, which no longer
        // renders, so it had been timing out rather than testing anything.
        await page.ClickAsync("#govNavWorkqueue");
        var body = page.Locator(".gov-body");

        // Either the estate has assets and every score is explained, or it is genuinely empty and
        // says so. Both are honest; a score with no explanation is not.
        var hasAssets = await page.Locator(".gov-asset-path").CountAsync() > 0;
        if (hasAssets)
        {
            await Expect(page.Locator(".gov-deductions").First).ToBeVisibleAsync();
            await Expect(page.Locator(".gov-deductions").First).ToContainTextAsync("−");
        }
        else
        {
            await Expect(body.Locator("[data-gov-state='empty']")).ToBeVisibleAsync();
        }

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ApiFailure_ShowsTheFailure_AndInventsNothingToFillIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        // Break the API at the network layer — the honest failure path is the one that runs when
        // the server cannot answer, which no amount of server-side testing reaches.
        await page.RouteAsync("**/api/governance/**", route => route.AbortAsync());
        await OpenGovernanceOverviewAsync(page);

        await Expect(page.Locator("[data-gov-state='failed']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-gov-state='failed']")).ToContainTextAsync("unavailable");

        // Nothing stands in for the real posture: no asset rows, no KPI tiles claiming a number.
        Assert.Equal(0, await page.Locator(".gov-asset-path").CountAsync());
        Assert.Equal(0, await page.Locator(".gov-kpi").CountAsync());
    }

    [Fact]
    public async Task Unauthorized_IsRenderedAsDenied_NotAsAnEmptyEstate()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        await page.RouteAsync("**/api/governance/**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 403,
            ContentType = "application/json",
            Body = "{\"error\":\"Forbidden\"}"
        }));
        await OpenGovernanceOverviewAsync(page);

        await Expect(page.Locator("[data-gov-state='unauthorized']")).ToBeVisibleAsync();
        // Naming the roles turns a dead end into a request someone can make.
        await Expect(page.Locator("[data-gov-state='unauthorized']")).ToContainTextAsync("StewardshipViewer");
        await Expect(page.Locator("[data-gov-state='unauthorized']"))
            .ToContainTextAsync("not an empty estate");

        Assert.Equal(0, await page.Locator(".gov-kpi").CountAsync());
    }

    [Fact]
    public async Task FailureIsRecoverable_WithoutReloadingThePage()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        var broken = true;
        await page.RouteAsync("**/api/governance/**", async route =>
        {
            if (broken) await route.AbortAsync();
            else await route.ContinueAsync();
        });

        await OpenGovernanceOverviewAsync(page);
        await Expect(page.Locator("[data-gov-state='failed']")).ToBeVisibleAsync();

        // A transient outage should not cost the steward their place. Retry is offered because the
        // alternative — telling someone to reload — loses whatever they were doing.
        broken = false;
        await page.ClickAsync("#btnGovRetry");
        await Expect(page.Locator("[data-gov-state='failed']")).Not.ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        Assert.Empty(session.PageErrors);
    }

    private static async Task OpenGovernanceOverviewAsync(IPage page)
    {
        await page.GotoAsync("/index.html#governance/overview");
        await page.ReloadAsync();
        await Expect(page.Locator(".gov-container")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 20_000 });
    }

}
