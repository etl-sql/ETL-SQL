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

    /// <summary>
    /// Studio is offered to exactly the roles granted the <c>StudioAccess</c> capability.
    ///
    /// <para>The shipped configuration grants the Studio capabilities to <c>Admin</c> and
    /// <c>Publisher</c> and to nobody else, so those two see the entry point and the other three
    /// must not.</para>
    ///
    /// <para>Worth asserting because the obvious client-side rule is wrong in a way that looks
    /// right. The Studio session probe was deliberately opened to every authenticated user, so that
    /// asking "what may I do in Studio?" would stop being an error for the roles that may do
    /// nothing. Pages that revealed the entry when the probe merely <em>succeeded</em> therefore
    /// revealed it to everybody: the probe answering is not the same as the answer being yes.</para>
    /// </summary>
    [Theory]
    [InlineData("Viewer", false)]
    [InlineData("Publisher", true)]
    [InlineData("DataSteward", false)]
    [InlineData("OrchestratorManager", false)]
    public async Task StudioIsOffered_OnlyToRolesHoldingStudioAccess(string role, bool seesStudio)
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await CreateAndSignInAsync(page, role);

        // Waits for the navigation answer to be applied rather than for a fixed delay. An absence
        // assertion made too early passes because nothing has been revealed yet, which is a green
        // test that proves nothing — and under a loaded full-lane run the positive case loses the
        // same race and fails instead.
        await page.WaitForSelectorAsync("body[data-nav-applied='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });
        await AssertNavAsync(page, "#studioNav", seesStudio, role, "Studio");

        Assert.Empty(session.PageErrors);
    }

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
        // Every gated entry is revealed by one applied answer, so wait for that rather than
        // sampling the DOM while it is still the markup default.
        await page.WaitForSelectorAsync("body[data-nav-applied='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });
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
        // Stewardship and Audit Evidence were removed from the sidebar when the three redundant
        // entries that all opened Lineage Search with a different top selector were collapsed into
        // it. This asserted Stewardship was present and had been failing since; a nav item that no
        // longer exists is not a coverage gap, so the assertion goes rather than the entry coming
        // back.
        await AssertNavAsync(page, "#govNavStewardship", false, role, "Stewardship");
        await AssertNavAsync(page, "#govNavAudit", false, role, "Audit Evidence");
        // The one that is genuinely open, so the section is not empty for a report consumer.
        await AssertNavAsync(page, "#govNavLineage", true, role, "Lineage Search");

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

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task NonAdminRoles_LoadTheLibraryWithoutForbiddenRequests(
        string role, bool seesAdmin, bool seesOrchestrator, bool seesGovernance)
    {
        _ = (seesAdmin, seesOrchestrator, seesGovernance);

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await CreateAndSignInAsync(page, role);

        // Measured from *after* sign-in completes, and this is the whole point of the test rather
        // than a detail of it. A newly created user carries MustChangePassword, and the middleware
        // answers 403 to every API call until it is cleared -- so every request the login page makes
        // during the forced change is a 403 by design. Counting from session start attributed all of
        // those to the report library and reported a defect that was not there.
        var before = session.FailedRequests.Count;

        await page.GotoAsync("/index.html");
        await page.ReloadAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(500);

        var afterSignIn = session.FailedRequests.Skip(before).ToList();

        Assert.True(afterSignIn.Count == 0,
            $"{role} saw failed requests loading the report library:\n  "
            + string.Join("\n  ", afterSignIn));
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
