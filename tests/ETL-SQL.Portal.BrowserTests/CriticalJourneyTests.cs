using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The critical Portal journey, driven through a real browser against a real HTTP server:
/// first-run sign-in (including the forced password change) → create a user → create a folder →
/// publish a report into it → run that report and see rendered rows.
///
/// Every other Portal test drives controllers through <c>HttpClient</c>. That leaves the part users
/// actually touch — the pages, their JavaScript, and the wiring between them — unproven. This test
/// clicks the same buttons a person clicks and asserts on what the page shows, so a broken selector,
/// a stale cached folder list, or a page that throws mid-flow fails here rather than in production.
/// </summary>
[Trait("Category", "Browser")]
public sealed class CriticalJourneyTests(PortalBrowserFixture fixture) : IClassFixture<PortalBrowserFixture>
{
    private const string FirstRunUsername = "admin";
    private const string FirstRunPassword = "Admin@12345!";
    private const string ChangedPassword = "Admin@Journey99!";

    /// <summary>Self-contained report: no connections, no parameters, one visual with one row.</summary>
    private const string ReportScript = """
        SET REPORT TITLE = 'Browser Journey';
        SELECT 'North' AS Region, 1042 AS Sales INTO #data;
        CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Region = Region, Sales = Sales));
        CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = SalesTable));
        """;

    /// <summary>Report execution is a real engine run, so it gets a longer budget than a click.</summary>
    private const float ExecutionTimeoutMs = 120_000;

    [Fact]
    public async Task LoginUsersFoldersPublishRun_CompletesInABrowser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var newUsername = $"journey_{suffix}";
        var folderName = $"Journey Folder {suffix}";
        var reportName = $"Journey Report {suffix}";
        var scriptFileName = $"journey_{suffix}.rptsql";

        await File.WriteAllTextAsync(
            Path.Combine(fixture.Factory.TempDir, "scripts", scriptFileName), ReportScript);

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;

        await SignInThroughForcedPasswordChangeAsync(page);
        await CreateUserAsync(page, newUsername);
        await CreateFolderAsync(page, folderName);
        await PublishReportAsync(page, reportName, scriptFileName, folderName);
        await RunReportAsync(page, folderName, reportName);

        Assert.Empty(session.PageErrors);
    }

    // ── Step 1: sign in ──────────────────────────────────────────────────────
    // The seeded first-run admin has MustChangePassword set, so the real first sign-in is two
    // forms, not one. Anything that breaks the hand-off between them locks every new deployment out.
    private static async Task SignInThroughForcedPasswordChangeAsync(IPage page)
    {
        await page.GotoAsync("/login.html");
        await page.FillAsync("#username", FirstRunUsername);
        await page.FillAsync("#password", FirstRunPassword);
        await page.ClickAsync("#loginBtn");

        await Expect(page.Locator("#changeForm")).ToBeVisibleAsync();
        await Expect(page.Locator("#mustChangeBanner")).ToBeVisibleAsync();

        await page.FillAsync("#currentPwd", FirstRunPassword);
        await page.FillAsync("#newPwd", ChangedPassword);
        await page.FillAsync("#confirmPwd", ChangedPassword);
        await page.ClickAsync("#changeBtn");

        await page.WaitForURLAsync("**/index.html");
    }

    // ── Step 2: create a user ────────────────────────────────────────────────
    private static async Task CreateUserAsync(IPage page, string username)
    {
        await GoToAdminTabAsync(page, "users");
        await page.ClickAsync("#newUserBtn");
        await page.FillAsync("#nu-username", username);
        await page.FillAsync("#nu-email", $"{username}@example.test");
        await page.FillAsync("#nu-password", "Journey@Test1!");
        await page.SelectOptionAsync("#nu-role", "Publisher");
        await page.ClickAsync("#nu-saveBtn");

        await Expect(page.Locator("#nu-error")).ToBeEmptyAsync();
        await Expect(page.Locator("#userTableWrap")).ToContainTextAsync(username);
    }

    // ── Step 3: create a folder ──────────────────────────────────────────────
    private static async Task CreateFolderAsync(IPage page, string folderName)
    {
        await GoToAdminTabAsync(page, "folders");
        await page.ClickAsync("#newFolderBtn");
        await page.FillAsync("#nf-name", folderName);
        await page.ClickAsync("#nf-saveBtn");

        await Expect(page.Locator("#nf-error")).ToBeEmptyAsync();
        await Expect(page.Locator("#folderTableWrap")).ToContainTextAsync(folderName);
    }

    // ── Step 4: publish into that folder ─────────────────────────────────────
    // The destination dropdown must already offer the folder created moments earlier on the
    // previous tab — the regression publish-folders.js was extracted to prevent.
    private static async Task PublishReportAsync(
        IPage page, string reportName, string scriptFileName, string folderName)
    {
        await GoToAdminTabAsync(page, "reports");
        await page.ClickAsync("#openPublishBtn");
        await page.FillAsync("#pr-name", reportName);
        await page.FillAsync("#pr-path", scriptFileName);

        var folderOptionValue = await page.Locator("#pr-folder").EvaluateAsync<string?>(
            "(select, name) => Array.from(select.options).find(o => o.textContent.includes(name))?.value",
            folderName);
        Assert.False(string.IsNullOrEmpty(folderOptionValue),
            $"The publish form's destination folders did not include '{folderName}'.");

        await page.SelectOptionAsync("#pr-folder", folderOptionValue!);
        await page.ClickAsync("#pr-saveBtn");

        await Expect(page.Locator("#pr-error")).ToBeEmptyAsync();
        await Expect(page.Locator("#reportsTableWrap")).ToContainTextAsync(reportName);
    }

    // ── Step 5: run it and see rows ──────────────────────────────────────────
    private static async Task RunReportAsync(IPage page, string folderName, string reportName)
    {
        await page.GotoAsync("/index.html");
        await page.ClickAsync($"#folderTree .folder-item:has-text(\"{folderName}\")");
        await page.ClickAsync($"a.report-card-link:has-text(\"{reportName}\")");

        await page.ClickAsync("#execBtn");

        // #refreshBtn only exists in the post-snapshot viewer, so waiting for it waits for a
        // completed run rather than for the job to be accepted.
        await Expect(page.Locator("#refreshBtn"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = ExecutionTimeoutMs });

        // The report itself renders inside a srcdoc iframe driven by report-runtime.js.
        var reportFrame = page.FrameLocator("#reportFrame iframe");
        await Expect(reportFrame.Locator("#root"))
            .ToContainTextAsync("North", new LocatorAssertionsToContainTextOptions { Timeout = ExecutionTimeoutMs });
        await Expect(reportFrame.Locator("#root")).ToContainTextAsync("1042");
    }

    private static async Task GoToAdminTabAsync(IPage page, string tab)
    {
        if (!page.Url.Contains("/admin.html", StringComparison.Ordinal))
        {
            await page.GotoAsync("/admin.html");
        }

        await page.ClickAsync($"#tab-{tab}");
        await Expect(page.Locator($"#panel-{tab}")).ToBeVisibleAsync();
    }
}
