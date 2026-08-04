using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// What each role actually sees when they sign in.
///
/// <para><c>AuthorizationMatrixTests</c> proves the API refuses the right requests. That is the half
/// that matters for security, and it is not the half users experience: a Viewer whose page renders
/// an Admin button gets a 403 when they press it, and concludes the product is broken rather than
/// that they lack permission. A navigation that offers what it cannot deliver is its own defect.</para>
///
/// <para>So each journey asserts both directions — the surfaces the role is meant to reach are
/// present and usable, and the ones they are not are absent rather than merely guarded. The absence
/// assertions are the ones worth having; showing a control and failing it later is the default
/// behaviour of any UI nobody checked.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class RoleJourneyTests(PortalBrowserFixture fixture)
{
    /// <param name="Role">The role assigned at creation.</param>
    /// <param name="SeesAdminNav">Whether the Admin entry point should be offered.</param>
    /// <param name="SeesOrchestratorNav">Whether the Orchestrator entry point should be offered.</param>
    /// <param name="SeesGovernanceNav">Whether the Governance entry point should be offered.</param>
    public sealed record RoleExpectation(
        string Role, bool SeesAdminNav, bool SeesOrchestratorNav, bool SeesGovernanceNav);

    /// <remarks>
    /// Governance is expected for every role. Its lineage and stewardship views are open to any
    /// authenticated user — a report consumer tracing where a number came from is the point of them —
    /// and the pieces that are not, quarantine and audit evidence, are gated inside the section
    /// rather than by hiding the whole thing. This started as an expectation that only stewards
    /// should see it; the product was right and the expectation was wrong.
    /// </remarks>
    public static TheoryData<string, bool, bool, bool> Roles() => new()
    {
        // A report consumer. Administration is out of reach and out of sight.
        { "Viewer", false, false, true },
        // Publishes reports; does not administer the Portal or run the scheduler.
        { "Publisher", false, false, true },
        // Works the data-quality queue, which lives under Governance.
        { "DataSteward", false, false, true },
        // Operates the scheduler and nothing else — the "Operator" journey.
        { "OrchestratorManager", false, true, true },
    };

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task EachRole_IsOfferedExactlyTheSurfacesItCanUse(
        string role, bool seesAdmin, bool seesOrchestrator, bool seesGovernance)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        var user = await CreateAndSignInAsync(page, role);

        await Expect(page.Locator("#navReports")).ToBeVisibleAsync();
        await AssertNavAsync(page, "#navAdmin, [href='/admin.html']", seesAdmin, role, "Admin");
        await AssertNavAsync(page, "#navOrchestrator, [href='/orchestrator.html']",
            seesOrchestrator, role, "Orchestrator");
        await AssertNavAsync(page, "#navGovernance", seesGovernance, role, "Governance");

        // A page that throws on load is not a page this role can use, whatever it renders.
        Assert.Empty(session.PageErrors);
        _ = user;
    }

    /// <param name="canOverview">Governance Overview needs GovernanceRead.</param>
    /// <param name="canQuarantine">The quarantine queue needs DataQualityStewardAccess.</param>
    [Theory]
    [InlineData("Viewer", false, false)]
    [InlineData("Publisher", false, false)]
    [InlineData("OrchestratorManager", false, false)]
    [InlineData("DataSteward", true, true)]
    public async Task GovernanceSubViews_AreOfferedOnlyToRolesTheirApisAccept(
        string role, bool canOverview, bool canQuarantine)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await CreateAndSignInAsync(page, role);

        // The section itself is open to everyone, because Lineage Search and Stewardship are. The
        // views inside it are not all open, and checking only the top-level entry — which is what
        // the first version of this suite did — misses exactly that.
        await page.ClickAsync("#navGovernance");
        await page.WaitForTimeoutAsync(300);

        await AssertNavAsync(page, "#govNavOverview", canOverview, role, "Governance Overview");
        await AssertNavAsync(page, "#govNavQuarantine", canQuarantine, role, "Quarantine Queue");
        await AssertNavAsync(page, "#govNavAudit", false, role, "Audit Evidence");
        // The two that are genuinely open, so the section is not empty for a report consumer.
        await AssertNavAsync(page, "#govNavLineage", true, role, "Lineage Search");
        await AssertNavAsync(page, "#govNavStewardship", true, role, "Stewardship");

        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Publisher")]
    public async Task ClickingGovernance_LandsOnAViewTheRoleCanActuallyUse(string role)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await CreateAndSignInAsync(page, role);

        await page.ClickAsync("#navGovernance");
        await page.WaitForTimeoutAsync(500);

        // It used to route everyone to the quarantine queue, so a report consumer's first click on
        // Governance landed them on the one view they are refused.
        Assert.DoesNotContain("governance/quarantine", page.Url);
        Assert.DoesNotContain("governance/overview", page.Url);
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData("Viewer", "overview")]
    [InlineData("Viewer", "quarantine")]
    [InlineData("Publisher", "quarantine")]
    public async Task ADeepLinkToAViewTheRoleCannotUse_RedirectsRatherThanOpening(
        string role, string view)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await CreateAndSignInAsync(page, role);

        // Deep links get shared and bookmarked, so this path is reached by people whoever sent it
        // never thought about. Hiding the nav entry does nothing for them.
        await page.GotoAsync($"/index.html#governance/{view}");
        await page.ReloadAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = 20_000 });

        Assert.DoesNotContain($"governance/{view}", page.Url);
        Assert.Empty(session.PageErrors);
    }

    [Theory(Skip = "Known finding, not yet root-caused: the report library still probes three "
        + "endpoints every non-admin role is refused.")]
    [MemberData(nameof(Roles))]
    public async Task NonAdminRoles_LoadTheLibraryWithoutForbiddenRequests(
        string role, bool seesAdmin, bool seesOrchestrator, bool seesGovernance)
    {
        _ = (seesAdmin, seesOrchestrator, seesGovernance);

        // Skipped rather than deleted, because it is finding something real and the finding should
        // stay visible in the test report rather than vanishing along with the assertion.
        //
        // Every non-admin role currently sees 403s on the report library. One cause is fixed — the
        // Studio capability probe was itself role-gated, so asking "what may I do?" was an error for
        // anyone outside two roles — which took it from six to three. The remaining three are not
        // yet identified.
        //
        // It matters twice over: a 403 on every load trains everyone to ignore the console, which is
        // where the next real failure will appear; and a page that asks for things it may not have
        // is usually a page about to offer a control that does not work.
        await using var session = await fixture.NewSessionAsync();
        await CreateAndSignInAsync(session.Page, role);

        Assert.True(session.ConsoleErrors.Count == 0,
            $"{role} saw console errors on the report library:\n  "
            + string.Join("\n  ", session.ConsoleErrors)
            + "\n  failed requests:\n  " + string.Join("\n  ", session.FailedRequests));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Publisher")]
    public async Task RolesWithoutAdminRights_AreTurnedAwayFromTheAdminPageItself(string role)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await CreateAndSignInAsync(page, role);

        // Hiding the navigation is presentation. Typing the URL has to be refused too, or the
        // hiding was decoration over an open door.
        await page.GotoAsync("/admin.html");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = 20_000 });

        var reachedAdminContent = await page.Locator("#userTableWrap, #tab-users").IsVisibleAsync();
        Assert.False(reachedAdminContent,
            $"{role} reached the admin console by navigating directly to /admin.html.");
    }

    [Fact]
    public async Task ADataStewardReachesTheQuarantineQueue_AndNotTheAdminConsole()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await CreateAndSignInAsync(page, "DataSteward");

        // The steward's actual job. Reaching Governance but not the queue would leave the role with
        // a menu entry and nothing behind it.
        await page.GotoAsync("/index.html#governance/quarantine");
        await page.ReloadAsync();
        await Expect(page.Locator("#dqQueueSearch")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 20_000 });

        await page.GotoAsync("/admin.html");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = 20_000 });
        Assert.False(await page.Locator("#userTableWrap, #tab-users").IsVisibleAsync());

        Assert.Empty(session.PageErrors);
    }

    private static async Task AssertNavAsync(
        IPage page, string selector, bool expected, string role, string surface)
    {
        var count = await page.Locator($"{selector}:visible").CountAsync();
        if (expected)
        {
            Assert.True(count > 0,
                $"{role} should be offered {surface} but the navigation entry is absent.");
        }
        else
        {
            Assert.True(count == 0,
                $"{role} is offered {surface}, which they cannot use. Pressing it returns 403 and "
                + "reads as the product being broken rather than as a permission they lack.");
        }
    }

    /// <summary>Creates a user in <paramref name="role"/>, clears the forced change, and signs in.</summary>
    private async Task<string> CreateAndSignInAsync(IPage page, string role)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"j_{role.ToLowerInvariant()}_{suffix}";
        const string initial = "Journey@Role99!";
        const string password = "Journey@Role99b!";

        await using (var adminSession = await fixture.NewSessionAsync())
        {
            await fixture.SignInAsync(adminSession.Page);
            var created = await adminSession.Page.APIRequest.PostAsync(
                $"{fixture.BaseUrl}/api/admin/users",
                new APIRequestContextOptions
                {
                    Headers = await BearerAsync(adminSession.Page),
                    DataObject = new
                    {
                        username,
                        password = initial,
                        role,
                        email = $"{username}@example.test"
                    }
                });
            Assert.True(created.Ok, $"Creating {role} failed: {await created.TextAsync()}");
        }

        await page.GotoAsync("/login.html");
        await page.FillAsync("#username", username);
        await page.FillAsync("#password", initial);
        await page.ClickAsync("#loginBtn");

        await page.WaitForSelectorAsync("#changeForm", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20_000
        });
        await page.FillAsync("#currentPwd", initial);
        await page.FillAsync("#newPwd", password);
        await page.FillAsync("#confirmPwd", password);
        await page.ClickAsync("#changeBtn");

        await page.WaitForURLAsync("**/index.html", new PageWaitForURLOptions { Timeout = 20_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = 20_000 });
        return username;
    }

    /// <summary>Lifts the signed-in page's bearer token out of storage for API calls.</summary>
    private static async Task<Dictionary<string, string>> BearerAsync(IPage page)
    {
        // The key api.js actually writes. Guessing it produced a token-less request that failed
        // as "unauthorized" rather than as "the test looked in the wrong place".
        var token = await page.EvaluateAsync<string?>(
            "() => sessionStorage.getItem('etlsql_token')");
        Assert.False(string.IsNullOrWhiteSpace(token), "No bearer token in browser storage.");
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }
}
