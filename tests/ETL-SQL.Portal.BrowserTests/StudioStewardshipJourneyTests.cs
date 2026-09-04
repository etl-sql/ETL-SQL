using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The data-stewardship journey: a finding is raised, an author fixes it where the metadata is
/// written, and a re-scan closes it.
///
/// <para>This one is not named after a competitor because it has no equivalent. SSIS, SSRS and Power
/// BI each certify a job someone else's tool also does; governance is a job this product does that
/// they do not, which is exactly why it needed a journey of its own rather than being assumed
/// covered by the surface tests around it.</para>
///
/// <para><b>What it proves that the surface tests do not.</b> The governance rail has tests for
/// authoring a tag, the dashboard has tests for its four empty states, and both pass today. Neither
/// says that a real asset, scored down for a real reason, can be fixed in the authoring surface and
/// come back clean — which is the whole job, and the only claim a steward actually cares about. It
/// spans two surfaces, the engine's lineage, and a scan in between.</para>
///
/// <para><b>Every asset it scores is its own.</b> The lane shares one Portal, so an assertion made
/// on "the estate" is an assertion on whatever every other test happened to leave behind. Both
/// halves publish and run a uniquely named report and then read the workqueue row for that asset,
/// which is why they can assert a deduction unconditionally rather than branching on whether the
/// estate has anything in it.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioStewardshipJourneyTests(PortalBrowserFixture fixture)
{
    /// <summary>Report execution is a real engine run, so it gets a longer budget than a click.</summary>
    private const float ExecutionTimeoutMs = 120_000;

    /// <summary>
    /// Deliberately carries none of the required stewardship tags — no owner, steward, contact,
    /// classification or quality — because a clean asset raises no finding and there would be
    /// nothing for the steward to do.
    /// </summary>
    private const string UngovernedScript = """
        SET REPORT TITLE = 'Stewardship Journey';
        SELECT 'North' AS Region, 1042 AS Sales INTO #steward_demo;
        CREATE VISUAL DemoTable AS TABLE (SOURCE = #steward_demo, MAPPINGS (Region = Region, Sales = Sales));
        CREATE PAGE Page1 AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = DemoTable));
        """;

    /// <summary>
    /// An ungoverned asset reaches the steward with its score explained.
    ///
    /// <para>This is the first test in the lane to put a real asset in the estate, which is why it
    /// was held: scanning is not a local act, and
    /// <c>GovernanceDashboardUiTests.NeverScannedEstate_SaysSo...</c> asserted a state any scan
    /// destroys. That state is now owned by the test that makes the claim rather than raced for on
    /// the shared Portal, so this one is free to scan.</para>
    /// </summary>
    [Fact]
    public async Task Certifies_TheStewardshipFinding()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderName = $"Steward Folder {suffix}";
        var reportName = $"Steward Report {suffix}";
        var scriptFileName = $"steward_{suffix}.rptsql";
        var scriptPath = Path.Combine(fixture.Factory.TempDir, "scripts", scriptFileName);
        await File.WriteAllTextAsync(scriptPath, UngovernedScript);

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        // ── An asset only exists once something has run ──────────────────────
        // The estate is projected from lineage, and lineage is written by a run. A report that has
        // never run is not yet anybody's responsibility.
        await CreateFolderAsync(page, folderName);
        await PublishReportAsync(page, reportName, scriptFileName, folderName);
        await RunReportAsync(page, folderName, reportName);

        // ── The steward's finding ────────────────────────────────────────────
        var deductions = await ScanAndReadDeductionsAsync(page, TaggedTable.TrimStart('#'));

        // Asserted unconditionally, on this journey's own asset: the estate has one because this
        // journey ran it, and every point it lost is explained by the rule that took it.
        Assert.Contains("−", deductions, StringComparison.Ordinal);
        Assert.Contains("metadata", deductions, StringComparison.OrdinalIgnoreCase);
        foreach (var tag in RequiredTags)
            Assert.Contains(tag, deductions, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(session.PageErrors);
    }


    /// <summary>
    /// The other half of the job: the author fixes the finding where the metadata is written, and a
    /// re-scan closes it.
    ///
    /// <para><b>The whole loop, not the halves.</b> The rail has tests that a tag is authored and the
    /// dashboard has tests that a finding is rendered. Neither says the second reflects the first,
    /// and it did not: five <c>INSERT TAG FOR TABLE</c> statements were written and saved, the report
    /// ran, and the steward saw one tag - the last statement to execute - because every governance
    /// surface read the newest lineage row instead of replaying the run. The author saw five tags in
    /// their script and the steward saw one, with nothing reported.</para>
    ///
    /// <para><b>Closed is asserted as closed</b>, not as "the row changed": the asset is still in the
    /// estate after the fix, and what must be gone is the missing-metadata deduction. A test that
    /// waited for the row to disappear would pass on an asset that simply stopped being scanned.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Certifies_TheStewardshipFixLoop()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderName = $"Steward Fix Folder {suffix}";
        var reportName = $"Steward Fix Report {suffix}";
        var scriptFileName = $"steward_fix_{suffix}.rptsql";
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Factory.TempDir, "scripts", scriptFileName), UngovernedScript);

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        await CreateFolderAsync(page, folderName);
        await PublishReportAsync(page, reportName, scriptFileName, folderName);
        await RunReportAsync(page, folderName, reportName);

        // ── The finding, before the fix ──────────────────────────────────────
        var before = await ScanAndReadDeductionsAsync(page, TaggedTable.TrimStart('#'));
        Assert.Contains("missing-metadata", before, StringComparison.OrdinalIgnoreCase);

        // ── The fix, in the surface the author actually uses ─────────────────
        var reportId = await ReportIdAsync(reportName);
        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });

        await page.Locator("[data-activity='governance']").ClickAsync();
        var scope = page.Locator($"[data-gov-scope='table:{TaggedTable}']");
        await scope.WaitForAsync(new LocatorWaitForOptions { Timeout = 20_000 });
        await scope.ClickAsync();

        foreach (var tag in RequiredTags) await ApplyTagAsync(page, tag);

        // Every tag the author entered is in the script they are about to save. Asserted before the
        // save, because a rail that silently dropped one would otherwise look like a scan defect.
        var edited = await page.EvaluateAsync<string>(
            "() => window.__STUDIO__.state.editorInstance.getValue()");
        foreach (var tag in RequiredTags)
            Assert.Contains($"{tag} =", edited, StringComparison.OrdinalIgnoreCase);

        await page.Keyboard.PressAsync("Control+s");
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].isDirty === false",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        // ── The finding, after ───────────────────────────────────────────────
        // Re-run first: tags reach the estate through lineage, and lineage is written by a run.
        await RunReportAsync(page, folderName, reportName);
        var after = await ScanAndReadDeductionsAsync(page, TaggedTable.TrimStart('#'));

        Assert.DoesNotContain("missing-metadata", after, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.PageErrors);
    }

    /// <summary>Gives one tag a value in the governance rail and applies it.</summary>
    private static async Task ApplyTagAsync(IPage page, string tag)
    {
        await page.Locator("[data-gov-name]").SelectOptionAsync(tag);
        await SetTagValueAsync(page, tag);
        await page.Locator("[data-gov-apply]").ClickAsync();
        await page.WaitForFunctionAsync(
            "name => window.__STUDIO__.state.editorInstance.getValue().toLowerCase().includes(name + ' =')",
            tag, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>The asset both halves act on: the table the script stages and the rail tags.</summary>
    private const string TaggedTable = "#steward_demo";

    /// <summary>The tags an asset has to carry before the estate stops deducting for their absence.</summary>
    private static readonly string[] RequiredTags =
        ["owner", "steward", "contact", "classification", "quality"];

    private static string TagValue(string tag) => tag switch
    {
        "contact" => "analytics@example.invalid",
        _ => "analytics",
    };

    /// <summary>
    /// Gives a tag a value, whichever control the rail offers for it.
    ///
    /// <para>A free-text tag gets an input and an enumerated one - <c>classification</c>,
    /// <c>quality</c> - gets a select, so the valid values are the ones the product offers rather
    /// than ones this test invented. Filling a select throws, which is how this was found.</para>
    /// </summary>
    private static async Task SetTagValueAsync(IPage page, string tag)
    {
        var control = page.Locator("[data-gov-value]");
        await control.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var isSelect = await control.EvaluateAsync<bool>("node => node.tagName === 'SELECT'");
        if (!isSelect)
        {
            await control.FillAsync(TagValue(tag));
            return;
        }

        var choice = await control.EvaluateAsync<string?>(
            "select => Array.from(select.options).map(o => o.value).find(v => v)");
        Assert.False(string.IsNullOrEmpty(choice),
            $"The rail offers a value list for @{tag} with nothing in it, so the tag cannot be set.");
        await control.SelectOptionAsync(choice!);
    }

    /// <summary>
    /// Runs a scan and returns the deductions listed against one asset.
    ///
    /// <para>Matched by asset key rather than taking the first row, because the estate holds whatever
    /// every other test in the lane has run and "the first row" is then a different asset depending
    /// on execution order.</para>
    /// </summary>
    private static async Task<string> ScanAndReadDeductionsAsync(IPage page, string assetKeyFragment)
    {
        await OpenGovernanceOverviewAsync(page);
        await page.ClickAsync("#btnRunScan");
        await Expect(page.Locator("[data-gov-state='scanned']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await page.ClickAsync("#govNavWorkqueue");
        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasTextString = assetKeyFragment }).First;
        await row.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        return await row.InnerTextAsync();
    }

    private async Task<int> ReportIdAsync(string reportName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ETL_SQL.Portal.Data.PortalDbContext>();
        return await db.Reports.Where(report => report.Name == reportName)
            .Select(report => report.Id)
            .SingleAsync();
    }
    // ── Portal plumbing, shared with the critical journey ────────────────────

    private static async Task OpenGovernanceOverviewAsync(IPage page)
    {
        await page.GotoAsync("/index.html#governance/overview");
        await Expect(page.Locator(".gov-body")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
    }

    private static async Task GoToAdminTabAsync(IPage page, string tab)
    {
        if (!page.Url.Contains("/admin.html", StringComparison.Ordinal)) await page.GotoAsync("/admin.html");
        await page.ClickAsync($"#tab-{tab}");
        await Expect(page.Locator($"#panel-{tab}")).ToBeVisibleAsync();
    }

    private static async Task CreateFolderAsync(IPage page, string folderName)
    {
        await GoToAdminTabAsync(page, "folders");
        await page.ClickAsync("#newFolderBtn");
        await page.FillAsync("#nf-name", folderName);
        await page.ClickAsync("#nf-saveBtn");
        await Expect(page.Locator("#nf-error")).ToBeEmptyAsync();
        await Expect(page.Locator("#folderTableWrap")).ToContainTextAsync(folderName);
    }

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
    /// <summary>
    /// Runs the report and waits for it to have rendered.
    ///
    /// <para>Whichever button the page offers: a report that has never run opens with Execute, and
    /// one that has opens in the snapshot viewer with Refresh. The completion signal is the report
    /// itself rendering, because Refresh leaves its own button on screen and waiting for that would
    /// return before the run had finished - and this journey depends on the lineage that run
    /// writes.</para>
    /// </summary>
    private static async Task RunReportAsync(IPage page, string folderName, string reportName)
    {
        await page.GotoAsync("/index.html");
        await page.ClickAsync($"#folderTree .folder-item:has-text(\"{folderName}\")");
        await page.ClickAsync($"a.report-card-link:has-text(\"{reportName}\")");

        var execute = page.Locator("#execBtn");
        var refresh = page.Locator("#refreshBtn");
        await Expect(execute.Or(refresh).First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        if (await execute.IsVisibleAsync()) await execute.ClickAsync();
        else await refresh.ClickAsync();

        var reportFrame = page.FrameLocator("#reportFrame iframe");
        await Expect(reportFrame.Locator("#root")).ToContainTextAsync("North",
            new LocatorAssertionsToContainTextOptions { Timeout = ExecutionTimeoutMs });
    }
}
