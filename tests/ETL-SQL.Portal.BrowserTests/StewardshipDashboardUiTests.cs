using System.Net;
using System.Text.Json.Nodes;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>
    /// The never-scanned state, asserted on a response this test owns.
    ///
    /// <para><b>Why it is not read from the shared Portal.</b> "Never scanned" is the one estate
    /// state a neighbour destroys just by existing: the lane shares one Portal, and any test that
    /// runs a scan — this class does, and so does the stewardship journey — moves the estate out of
    /// it permanently. Asserted against live state, this test passes or fails on execution order,
    /// which is not a property of the code under test. So the page's real dashboard response is
    /// fetched and its <c>lastScan</c> is replaced with null: everything else on the page is the
    /// server's own data, and the one field the state turns on is owned here.</para>
    ///
    /// <para>That the server reports null for an unscanned estate is a server claim, and it is
    /// asserted as one, on an isolated database, by
    /// <c>GovernanceDashboardTests.NeverScanned_IsReportedAsUnscanned_NotAsAnEstateWithNoFindings</c>.
    /// Between the two, the claim is covered end to end without either half racing the lane.</para>
    /// </summary>
    [Fact]
    public async Task NeverScannedEstate_SaysSo_RatherThanShowingACleanBillOfHealth()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await RouteDashboardAsNeverScannedAsync(page);
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

    /// <summary>
    /// Every point an asset loses is shown with the rule that took it.
    ///
    /// <para><b>Asserted on an asset this test put there.</b> It used to assert its deductions only
    /// <c>if</c> the estate had any assets, and the estate on this lane was always empty, so the
    /// test named for scores had never asserted one — the branch it actually took was the "estate is
    /// empty" branch, every run. Seeding one ungoverned asset before the scan removes the branch:
    /// there is an asset because this test made one, so there is a deduction to read.</para>
    /// </summary>
    [Fact]
    public async Task LiveData_RendersScoresWithTheRuleThatTookEachPoint()
    {
        // Unique per run: the lane's Portal is shared and holds whatever every other test has run,
        // so "the first row" is a different asset depending on order. This one is ours.
        var table = $"gov_scored_{Guid.NewGuid():N}"[..24];
        await SeedUngovernedAssetAsync(table);

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

        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasTextString = table }).First;
        await row.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // The claim is not "a score was rendered" but "the score is explained": a deduction, the
        // points it cost, and the rule that took them.
        var deductions = row.Locator(".gov-deductions");
        await Expect(deductions).ToBeVisibleAsync();
        await Expect(deductions).ToContainTextAsync("−");
        await Expect(deductions).ToContainTextAsync("missing-metadata");
        await Expect(deductions).ToContainTextAsync("owner");

        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Writes one lineage row for a table carrying none of the required stewardship tags, which is
    /// what gives the scan something to deduct for.
    /// </summary>
    private async Task SeedUngovernedAssetAsync(string table)
    {
        var catalog = fixture.Factory.Services.GetRequiredService<ILineageCatalogStore>();
        await catalog.SaveLineageAsync(
            [new LineageEntry(table, "SELECT")],
            $"job-{table}",
            $"loads/{table}.etlsql",
            DateTime.UtcNow);
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
        await Expect(page.Locator("[data-gov-state='unauthorized']")).ToContainTextAsync("GovernanceViewer");
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

    /// <summary>
    /// Serves the real dashboard response with <c>lastScan</c> nulled out.
    ///
    /// <para>Fetched rather than fabricated, so a change to the payload's shape reaches this test
    /// instead of being frozen into a literal that renders a page the product no longer serves.</para>
    /// </summary>
    private static async Task RouteDashboardAsNeverScannedAsync(IPage page) =>
        await page.RouteAsync("**/api/governance/dashboard*", async route =>
        {
            var response = await route.FetchAsync();
            var body = await response.TextAsync();
            var payload = JsonNode.Parse(body)!.AsObject();
            payload["lastScan"] = null;

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = (int)HttpStatusCode.OK,
                ContentType = "application/json",
                Body = payload.ToJsonString()
            });
        });

    private static async Task OpenGovernanceOverviewAsync(IPage page)
    {
        await page.GotoAsync("/index.html#governance/overview");
        await page.ReloadAsync();
        await Expect(page.Locator(".gov-container")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 20_000 });
    }

}
