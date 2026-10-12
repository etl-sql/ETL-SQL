using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// A report read by someone who did not write it. The author is a Publisher who owns one folder;
/// each reader is a Viewer who holds Execute on that folder through a group and nothing else. No
/// administrator signs in.
///
/// <para>The reader's half is what an author's own run cannot show: the report opened from the
/// library, driven through its own controls, a saved view kept and reopened, and the author's
/// controls absent rather than refused.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalJourneyCollection.Name)]
public sealed class PortalReaderJourneyTests(PortalJourneyFixture fixture)
{
    private const string Password = "Reader@Journey99!";
    private const int ExecutionTimeoutMs = 60_000;

    private const string SalesReport = """
        CREATE TABLE #sales (SaleID INT, Region VARCHAR(20), Total DECIMAL(10,2));
        INSERT INTO #sales VALUES (1, 'North', 100), (2, 'North', 150), (3, 'South', 80),
          (4, 'South', 60), (5, 'West', 200), (6, 'West', 40);
        DECLARE @Region VARCHAR(20) = 'All';
        SELECT DISTINCT Region INTO #regions FROM #sales;
        SELECT Region, SUM(Total) AS Revenue INTO #by_region FROM #sales GROUP BY Region;
        CREATE VISUAL RegionPicker AS SLICER (
          SOURCE = #regions,
          MAPPINGS (VALUE = Region),
          OPTIONS (MODE = SINGLE, LAYOUT = 'DROPDOWN', TITLE = 'Region'),
          ACTIONS (ON_CHANGE = SET_PARAMETER(@Region, value))
        );
        CREATE VISUAL ByRegion AS BAR (
          SOURCE = #by_region,
          MAPPINGS (X = Region, Y = Revenue),
          ACTIONS (ON_CLICK = SET_PARAMETER(@Region, Region))
        );
        CREATE VISUAL Detail AS TABLE (
          SOURCE = (SELECT SaleID, Region, Total FROM #sales WHERE @Region = 'All' OR Region = @Region)
        );
        CREATE PAGE [Main] AS DASHBOARD (
          LAYOUT (
            STRUCTURE = 'A B
                         C C',
            MAP ('A' = RegionPicker, 'B' = ByRegion, 'C' = Detail)
          )
        );
        """;

    [Fact]
    public async Task AReaderOpensAReportUsesItAndReopensASavedViewWithoutAuthorControls()
    {
        var author = await CreateUserAsync("Publisher");
        var folderId = await CreateFolderOwnedByAsync(author);
        var reader = await CreateUserAsync("Viewer");
        await GrantExecuteAsync(folderId, reader);

        int reportId;
        string reportName = $"Regional sales {Guid.NewGuid():N}"[..28];
        await using (var authoring = await fixture.NewSessionAsync())
        {
            await SignInAsync(authoring.Page, author);
            reportId = await PublishAsync(authoring.Page, folderId, reportName, SalesReport);
        }

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await SignInAsync(page, reader);
        var afterSignIn = session.FailedRequests.Count;

        // Found in the library, not reached by a link the author sent.
        var card = page.Locator($".report-card-link[data-id='{reportId}']").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(card).ToHaveTextAsync(reportName);
        await card.ClickAsync();

        // The reader runs it under their own Execute grant.
        var report = await RunAsync(page);
        Assert.Equal(new[] { "North", "South", "West" }, await RegionsShownAsync(report));

        // The author's entry points are not offered, here or in the navigation.
        await Expect(page.Locator("#exportBtn")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(page.Locator("#designBtn")).ToHaveCountAsync(0);
        await Expect(page.Locator("#studioNav:visible")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-viewer-action='delete'], [data-viewer-action='publish'], [data-viewer-action='edit']"))
            .ToHaveCountAsync(0);

        // A parameter, through the report's own control.
        await report.Locator("[data-visual-name='RegionPicker'] select").SelectOptionAsync("South");
        await WaitForRegionsAsync(report, ["South"]);
        Assert.Equal(new[] { 3, 4 }, await SaleIdsShownAsync(report));

        // Kept as a saved view.
        var viewName = "South only";
        await report.Locator("#etlsql-views-menu-button").ClickAsync();
        await report.Locator("#etlsql-views-menu [role='menuitem']", new() { HasText = "Save current view as" }).ClickAsync();
        var dialog = report.Locator(".etlsql-feedback-dialog");
        await dialog.Locator("input").FillAsync(viewName);
        await dialog.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        await Expect(report.Locator(".etlsql-feedback-toast", new() { HasText = $"Saved '{viewName}'." }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // An interaction: clicking a bar moves the same parameter somewhere else.
        var clicked = await ClickBarAsync(report);
        Assert.NotEqual("South", clicked);
        await WaitForRegionsAsync(report, [clicked]);

        // A fresh page opens the base report, and the saved view brings the reader's state back.
        await page.GotoAsync("/index.html");
        await page.Locator($".report-card-link[data-id='{reportId}']").First.ClickAsync();
        report = await FrameAsync(page);
        await WaitForRegionsAsync(report, ["North", "South", "West"]);
        await report.Locator("#etlsql-views-menu-button").ClickAsync();
        await report.Locator(".saved-view-menu-item", new() { HasText = viewName }).ClickAsync();
        await WaitForRegionsAsync(report, ["South"]);
        Assert.Equal(new[] { 3, 4 }, await SaleIdsShownAsync(report));
        await Expect(report.Locator("[data-visual-name='RegionPicker'] select")).ToHaveValueAsync("South");
        await Expect(page.Locator("#designBtn")).ToHaveCountAsync(0);

        // Asked for directly, the author's routes refuse the reader and the script is untouched.
        Assert.Equal(403, await StatusAsync(page, "GET", $"/api/reports/{reportId}/script-content"));
        Assert.Equal(403, await StatusAsync(page, "DELETE", $"/api/reports/{reportId}"));
        Assert.Equal(SalesReport.ReplaceLineEndings("\n"), (await StoredScriptAsync(reportId)).ReplaceLineEndings("\n"));

        // Nothing the reader was offered was refused or failed: the two refusals above are the only
        // ones. The 404 is the viewer asking for a snapshot before the report's first run.
        var failed = session.FailedRequests.Skip(afterSignIn)
            .Where(entry => !entry.StartsWith("404 GET", StringComparison.Ordinal) || !entry.EndsWith($"/api/reports/{reportId}/snapshot?includeManifest=false", StringComparison.Ordinal))
            .ToList();
        Assert.True(failed.Count == 2 && failed.All(entry => entry.StartsWith("403 ", StringComparison.Ordinal)),
            $"Unexpected failed requests:\n  {string.Join("\n  ", failed)}");
        Assert.Empty(session.PageErrors);
    }

    // ── The running report ───────────────────────────────────────────────────

    private static async Task<IFrameLocator> RunAsync(IPage page)
    {
        var execute = page.Locator("#execBtn");
        var refresh = page.Locator("#refreshBtn");
        await Expect(execute.Or(refresh).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await execute.IsVisibleAsync()) await execute.ClickAsync();
        else await refresh.ClickAsync();
        return await FrameAsync(page);
    }

    private static async Task<IFrameLocator> FrameAsync(IPage page)
    {
        var report = page.FrameLocator("#reportFrame iframe");
        await Expect(report.Locator("[data-visual-name='Detail'] tbody tr").First)
            .ToBeVisibleAsync(new() { Timeout = ExecutionTimeoutMs });
        return report;
    }

    /// <summary>Clicks the first bar that is not South and returns the region it stands for.</summary>
    private static async Task<string> ClickBarAsync(IFrameLocator report)
    {
        var marks = report.Locator("[data-visual-name='ByRegion'] [data-row-index]");
        await Expect(marks.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var regions = await marks.EvaluateAllAsync<string[]>(
            """
            marks => marks.map(mark => {
                const card = mark.closest('[data-visual-name]');
                const data = card._visualData || card.closest('.visual-card')?._visualData;
                const index = (data.columns || []).findIndex(c => c.toLowerCase() === 'region');
                return String(data.rows[Number(mark.dataset.rowIndex)][index]);
            })
            """);
        var target = Array.FindIndex(regions, region => region != "South");
        await marks.Nth(target).ClickAsync(new() { Force = true });
        return regions[target];
    }

    private static Task<string[]> RegionsShownAsync(IFrameLocator report) => ColumnAsync(report, "Detail", "region", distinct: true);

    private static async Task<int[]> SaleIdsShownAsync(IFrameLocator report) =>
        [.. (await ColumnAsync(report, "Detail", "saleid", distinct: false)).Select(int.Parse).Order()];

    /// <summary>The values one column of a rendered table shows, sorted.</summary>
    private static async Task<string[]> ColumnAsync(IFrameLocator report, string visual, string column, bool distinct)
    {
        var table = report.Locator($"[data-visual-name='{visual}']");
        await Expect(table.Locator("tbody tr").First).ToBeVisibleAsync(new() { Timeout = ExecutionTimeoutMs });
        var values = await table.EvaluateAsync<string[]>(
            """
            (card, column) => {
                const table = card.querySelector('table');
                const headers = [...table.querySelectorAll('thead th')].map(th => th.textContent.trim().toLowerCase());
                const index = headers.findIndex(text => text.startsWith(column));
                if (index < 0) throw new Error(`No ${column} column in: ${headers.join(', ')}`);
                return [...table.querySelectorAll('tbody tr')]
                    .filter(tr => tr.querySelector(':scope > td') && !tr.classList.contains('summary-row') && !tr.classList.contains('detail-row'))
                    .map(tr => tr.children[index]?.textContent.trim() ?? '')
                    .filter(Boolean);
            }
            """, column);
        return [.. (distinct ? values.Distinct() : values).Order(StringComparer.Ordinal)];
    }

    private static async Task WaitForRegionsAsync(IFrameLocator report, string[] expected)
    {
        await ETL_SQL.TestSupport.LoadAwareWait.UntilAsync<string[]?>(
            $"Detail should show exactly {string.Join(", ", expected)}",
            async cancellationToken =>
            {
                try
                {
                    return await RegionsShownAsync(report).WaitAsync(cancellationToken);
                }
                catch (PlaywrightException)
                {
                    // The table is re-rendered by the refresh; read it again.
                    return null;
                }
            },
            regions => regions is not null && regions.SequenceEqual(expected),
            TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(250),
            describe: regions => regions is null ? "Table is re-rendering" : $"Regions: {string.Join(", ", regions)}");
    }

    // ── The Portal ───────────────────────────────────────────────────────────

    private static async Task SignInAsync(IPage page, Person person)
    {
        await page.GotoAsync("/login.html");
        await page.FillAsync("#username", person.UserName);
        await page.FillAsync("#password", Password);
        await page.ClickAsync("#loginBtn");
        await page.WaitForURLAsync("**/index.html", new PageWaitForURLOptions { Timeout = 20_000 });
        await page.WaitForSelectorAsync("body[data-nav-applied='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });
    }

    private static async Task<int> PublishAsync(IPage page, int folderId, string name, string scriptText)
    {
        var report = await page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const { studioApi } = await import('/js/api.js');
                return studioApi.createReport(request);
            }
            """, new { folderId, name, scriptText });
        return report.GetProperty("id").GetInt32();
    }

    /// <summary>The status the signed-in person gets from a route, using the page's own token.</summary>
    private static Task<int> StatusAsync(IPage page, string method, string path) =>
        page.EvaluateAsync<int>(
            """
            async ([method, path]) => {
                const { auth } = await import('/js/api.js');
                return (await fetch(path, { method, headers: { Authorization: `Bearer ${auth.getToken()}` } })).status;
            }
            """, new[] { method, path });

    private async Task<string> StoredScriptAsync(int reportId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var path = await db.Reports.AsNoTracking().Where(report => report.Id == reportId)
            .Select(report => report.ScriptPath).SingleAsync();
        return await File.ReadAllTextAsync(path);
    }

    // ── The people involved ──────────────────────────────────────────────────

    private sealed record Person(int Id, string UserName);

    /// <summary>An account holding exactly one role and no folder of its own.</summary>
    private async Task<Person> CreateUserAsync(string role)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new PortalUser
        {
            UserName = $"{role.ToLowerInvariant()}_{suffix}",
            Email = $"{role.ToLowerInvariant()}_{suffix}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        Assert.Equal(role, Assert.Single(await users.GetRolesAsync(user)));
        return new Person(user.Id, user.UserName);
    }

    private async Task<int> CreateFolderOwnedByAsync(Person owner)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder { Name = $"Readers {suffix}", Path = $"/Readers-{suffix}", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    /// <summary>Puts the reader in a new group that holds Execute on the folder, and returns the group's name.</summary>
    private async Task<string> GrantExecuteAsync(int folderId, Person reader, string? groupName = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var group = new Group { Name = groupName ?? $"readers_{Guid.NewGuid():N}"[..20] };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = reader.Id, GroupId = group.Id });
        db.FolderAcls.Add(new FolderAcl { FolderId = folderId, GroupId = group.Id, Permission = FolderPermission.Execute });
        await db.SaveChangesAsync();
        return group.Name;
    }
}
