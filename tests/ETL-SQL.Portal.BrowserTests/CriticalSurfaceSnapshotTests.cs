using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Snapshots of the Portal's critical surfaces, taken as <b>accessibility trees</b> rather than
/// pixels.
///
/// <para>An aria snapshot records what the page <em>is</em> — its headings, landmarks, controls and
/// their accessible names — rather than what it looks like. That is deliberate on three counts. It
/// does not churn on fonts, GPU, or platform anti-aliasing, so it can run on any machine without a
/// tolerance nobody can justify. It is a text diff, so a change is reviewable in the pull request
/// that causes it instead of requiring someone to open two images. And it fails for the changes that
/// matter — a heading that stopped being a heading, a button that lost its name, a landmark that
/// disappeared — which is precisely the class of regression a pixel diff reports as a handful of
/// grey pixels nobody investigates.</para>
///
/// <para>Baselines live beside this file as <c>.snapshot.txt</c>. Regenerate with
/// <c>ETLSQL_UPDATE_SNAPSHOTS=1</c> and <b>read the diff</b> — an updated baseline is a claim that
/// the new structure is correct, which is a review decision rather than a mechanical one.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class CriticalSurfaceSnapshotTests(PortalBrowserFixture fixture)
{
    private static bool Updating =>
        Environment.GetEnvironmentVariable("ETLSQL_UPDATE_SNAPSHOTS") == "1";

    [Fact]
    public async Task SignInPage_KeepsItsStructure()
    {
        await using var session = await fixture.NewSessionAsync();
        await session.Page.GotoAsync("/login.html");
        await session.Page.WaitForSelectorAsync("#loginBtn");

        // The one page every user meets, and the only one they cannot navigate away from if it
        // breaks. It carries no dynamic content, so any diff here is a real structural change.
        await AssertSnapshotAsync(session.Page.Locator("body"), "login");
    }

    [Fact]
    public async Task GovernanceOverview_NeverScannedState_KeepsItsStructure()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        // The payload is fixed rather than taken from the shared Portal. Reading real data made this
        // depend on whether another test in the lane had run a scan first — an ordering dependency,
        // and the wrong thing to assert anyway: a snapshot is about how a state renders, not about
        // which state the database happens to be in.
        await page.RouteAsync("**/api/governance/dashboard*", route => route.FulfillAsync(
            new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                {
                  "summary": {
                    "totalAssets": 0, "governedAssets": 0, "belowThreshold": 0,
                    "openFindings": 0, "ignoredFindings": 0, "acceptedRisks": 0, "targetScore": 80
                  },
                  "assets": [],
                  "lastScan": null
                }
                """
            }));

        await page.GotoAsync("/index.html#governance/overview");
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("[data-gov-state='never-scanned']",
            new PageWaitForSelectorOptions { Timeout = 20_000 });

        // The state that most needs to keep saying what it says: zero findings because nothing has
        // been measured, not because the estate is clean. Losing that wording turns the tiles below
        // it into a false compliance claim, and nothing else on the page would fail.
        await AssertSnapshotAsync(page.Locator(".gov-body"), "governance-never-scanned");
    }

    [Fact]
    public async Task GovernanceOverview_UnauthorizedState_KeepsItsStructure()
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
        await page.GotoAsync("/index.html#governance/overview");
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("[data-gov-state='unauthorized']",
            new PageWaitForSelectorOptions { Timeout = 20_000 });

        // A denial has to keep naming the roles that would grant access. Without them it is a dead
        // end; with them it is a request someone can make.
        await AssertSnapshotAsync(page.Locator(".gov-body"), "governance-unauthorized");
    }

    [Fact]
    public async Task GovernanceSidebar_KeepsTheViewsAnAdminIsOffered()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        await page.GotoAsync("/index.html#governance/overview");
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("#governanceSidebarSection",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        // Which views appear here is role-dependent and easy to change by accident — two of them
        // were shown to everyone until recently. Pinning the admin's set makes any change to the
        // gating visible as a diff rather than as a silently wider audience.
        await AssertSnapshotAsync(page.Locator("#governanceSidebarSection"), "governance-sidebar-admin");
    }

    /// <summary>
    /// Compares the locator's accessibility tree with the stored baseline, or writes it when
    /// <c>ETLSQL_UPDATE_SNAPSHOTS=1</c>.
    /// </summary>
    private static async Task AssertSnapshotAsync(ILocator locator, string name)
    {
        var actual = Normalize(await locator.AriaSnapshotAsync());
        var path = Path.Combine(SnapshotDirectory(), $"{name}.snapshot.txt");

        if (Updating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual);
            return;
        }

        Assert.True(File.Exists(path),
            $"No baseline for '{name}'. Create it with ETLSQL_UPDATE_SNAPSHOTS=1 and review the "
            + $"generated file before committing it.\n\nCaptured:\n{actual}");

        var expected = Normalize(await File.ReadAllTextAsync(path));
        Assert.True(expected == actual,
            $"The accessibility structure of '{name}' changed.\n\n"
            + "If the new structure is correct, regenerate with ETLSQL_UPDATE_SNAPSHOTS=1 — but read "
            + "the diff first, because an updated baseline is a claim that the change is an "
            + $"improvement.\n\n--- expected ---\n{expected}\n\n--- actual ---\n{actual}");
    }

    /// <summary>
    /// Removes the things that differ between runs without meaning anything: generated ids, the
    /// loopback port, timestamps, and trailing whitespace.
    /// </summary>
    private static string Normalize(string snapshot)
    {
        var text = snapshot.ReplaceLineEndings("\n").TrimEnd();
        text = Regex.Replace(text, @"\b[0-9a-f]{8,32}\b", "<id>", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"http://127\.0\.0\.1:\d+", "<origin>");
        text = Regex.Replace(text, @"\d{1,2}/\d{1,2}/\d{4}[^""\n]*", "<timestamp>");
        return string.Join('\n', text.Split('\n').Select(line => line.TrimEnd()));
    }

    private static string SnapshotDirectory() =>
        Path.Combine(RepoRoot(), "tests", "ETL-SQL.Portal.BrowserTests", "Snapshots");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
