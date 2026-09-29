using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Cross-filtering, drill-down and drill-through, authored from Studio's inspector on the Portal
/// and then used the way a reader uses them: by clicking the running report.
///
/// <para>The visuals are seeded in the script, because building visuals is certified elsewhere.
/// Every interaction is set only through the inspector, and each test ends by clicking the rendered
/// report and reading the rows the reader sees. A clause that is in the script and does nothing
/// when clicked is the failure worth catching.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioInteractionJourneyTests(StudioAuthoringFixture fixture)
{
    private const int ExecutionTimeoutMs = 60_000;

    [Fact]
    public async Task ASelectionReachesOnlyTheVisualsItIsSentTo()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var reportId = await OpenStudioAsync(page);

        // The chart sends; linking it is what makes a click a selection.
        await SelectVisualAsync(page, "ByRegion");
        await page.SelectOptionAsync("#pp-interaction-on-select", "HIGHLIGHT");
        await WaitForScriptAsync(page, "CREATE VISUAL ByRegion", "INTERACTIONS (ON_SELECT = HIGHLIGHT)");

        // Both tables are linked to filter. A selection arrives as @Region, which their queries do
        // not read yet, so the inspector says so and writes the filter when asked.
        foreach (var table in new[] { "Detail", "Other" })
        {
            await SelectVisualAsync(page, table);
            await page.SelectOptionAsync("#pp-interaction-on-select", "FILTER");
            await WaitForScriptAsync(page, $"CREATE VISUAL {table}", "INTERACTIONS (ON_SELECT = FILTER)");
            var filterOn = page.Locator("[data-filter-on='Region']");
            await Expect(page.Locator("[data-unread-key='Region']")).ToContainTextAsync("arrives as @Region");
            await filterOn.ClickAsync();
            await WaitForScriptAsync(page, $"CREATE VISUAL {table}", "WHERE @Region = 'All' OR Region = @Region");
            await Expect(filterOn).ToHaveCountAsync(0);
        }
        await WaitForScriptAsync(page, "DECLARE @Region", "'All'");

        // Only one of them is on the chart's list.
        await SelectVisualAsync(page, "ByRegion");
        await page.Locator("[data-emit-target='Detail']").CheckAsync();
        await WaitForScriptAsync(page, "CREATE VISUAL ByRegion", "EMIT_FILTER (TARGETS = (Detail))");
        await Expect(page.Locator("[data-emit-note]")).ToContainTextAsync("reaches only Detail");

        var script = await SaveAsync(page, reportId);
        Assert.Contains("EMIT_FILTER (TARGETS = (Detail))", script, StringComparison.Ordinal);

        var report = await RunReportAsync(page, reportId);
        Assert.True((await RegionsShownAsync(report, "Detail")).Count > 1, "Detail should start unfiltered.");
        var region = await ClickChartMarkAsync(report, "ByRegion");

        await WaitForRegionsAsync(report, "Detail", regions => regions.Count == 1 && regions.Contains(region),
            $"Detail should show only {region} after the click");
        // Other is linked too, and would have filtered without EMIT_FILTER.
        Assert.True((await RegionsShownAsync(report, "Other")).Count > 1,
            "Other is not on the chart's list, so the selection must not reach it.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ADrillDownShowsTheClickedRegionInItsTarget()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var reportId = await OpenStudioAsync(page);

        await SelectVisualAsync(page, "RegionDrill");
        await page.SelectOptionAsync("#pp-click-kind", "DRILL_DOWN");
        await page.SelectOptionAsync("#pp-click-target", "Orders");
        await page.Locator("#pp-click-keys").FillAsync("Region");
        await page.Locator("#pp-click-keys").BlurAsync();
        await WaitForScriptAsync(page, "CREATE VISUAL RegionDrill", "ON_CLICK = DRILL_DOWN(Target = Orders, Key = (Region))");
        // The note tells the author what the target has to do for the click to mean anything, and
        // the button does it.
        await Expect(page.Locator("[data-click-note]")).ToContainTextAsync("Orders must read @Region");
        await page.Locator("[data-drill-read='Region']").ClickAsync();
        await WaitForScriptAsync(page, "CREATE VISUAL Orders", "WHERE @Region = 'All' OR Region = @Region");
        await WaitForScriptAsync(page, "DECLARE @Region", "'All'");

        await SaveAsync(page, reportId);

        var report = await RunReportAsync(page, reportId);
        Assert.True((await RegionsShownAsync(report, "Orders")).Count > 1, "Orders should start unfiltered.");
        var region = await ClickChartMarkAsync(report, "RegionDrill");

        await WaitForRegionsAsync(report, "Orders", regions => regions.Count == 1 && regions.Contains(region),
            $"Orders should show only {region} after the drill-down");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ARowDetailExpandsToTheRowsThatMatchIt()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var reportId = await OpenStudioAsync(page);

        await SelectVisualAsync(page, "Totals", "Row detail");
        await page.SelectOptionAsync("#pp-row-detail-target", "Detail");
        await WaitForScriptAsync(page, "CREATE VISUAL Totals", "ROW_DETAIL (TARGET = Detail)");
        await page.Locator("#pp-row-detail-add").ClickAsync();
        await page.Locator("[data-row-detail-child='0']").FillAsync("Region");
        await page.Locator("[data-row-detail-child='0']").BlurAsync();
        await page.Locator("[data-row-detail-parent='0']").FillAsync("Region");
        await page.Locator("[data-row-detail-parent='0']").BlurAsync();
        await page.Locator("#pp-row-detail-limit").FillAsync("5");
        await page.Locator("#pp-row-detail-limit").BlurAsync();
        await WaitForScriptAsync(page, "CREATE VISUAL Totals", "ROW_DETAIL (TARGET = Detail, BINDINGS (@Region = Region), LIMIT = 5)");

        await SaveAsync(page, reportId);

        var report = await RunReportAsync(page, reportId);
        var totals = report.Locator("[data-visual-name='Totals']");
        var firstRow = totals.Locator("tbody tr").Filter(new() { Has = report.Locator(".expand-btn") }).First;
        var region = await CellTextAsync(firstRow, await ColumnIndexAsync(totals, "Region"));
        await firstRow.Locator(".expand-btn").ClickAsync();

        var detail = totals.Locator(".detail-row .detail-container");
        await Expect(detail.Locator("tbody tr").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var detailRegions = await detail.EvaluateAsync<string[]>(
            """
            container => {
                const headers = [...container.querySelectorAll('thead th')].map(th => th.textContent.trim().toLowerCase());
                const index = headers.findIndex(text => text.startsWith('region'));
                return [...container.querySelectorAll('tbody tr')]
                    .filter(tr => tr.querySelector(':scope > td') && !tr.classList.contains('summary-row'))
                    .map(tr => tr.children[index]?.textContent.trim() ?? '');
            }
            """);
        Assert.NotEmpty(detailRegions);
        Assert.All(detailRegions, value => Assert.Equal(region, value));
        Assert.True(detailRegions.Length <= 5, $"LIMIT = 5 showed {detailRegions.Length} rows.");
        Assert.Empty(session.PageErrors);
    }

    // ── Studio ───────────────────────────────────────────────────────────────

    private async Task<int> OpenStudioAsync(IPage page)
    {
        await fixture.SignInAsync(page);
        var reportId = await CreateReportAsync(page);
        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await page.WaitForFunctionAsync(
            "() => (window.__STUDIO__?.state?.designerInstance?.getState()?.pages || []).some(p => (p.visuals || []).length > 0)",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        return reportId;
    }

    private static async Task SelectVisualAsync(IPage page, string name, string group = "Actions & Interactions")
    {
        await page.EvaluateAsync(
            """
            name => {
                const designer = window.__STUDIO__.state.designerInstance;
                const visual = designer.getState().pages.flatMap(p => p.visuals || []).find(v => v.name === name);
                if (!visual) throw new Error(`No visual named ${name}`);
                designer.selectVisual(visual.id);
            }
            """, name);
        await page.WaitForFunctionAsync(
            """
            group => [...document.querySelectorAll('details.etlsql-format-group')]
                .some(item => item.querySelector('summary')?.textContent.trim().startsWith(group))
            """, group, new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.EvaluateAsync(
            """
            group => {
                const details = [...document.querySelectorAll('details.etlsql-format-group')]
                    .find(item => item.querySelector('summary')?.textContent.trim().startsWith(group));
                details.open = true;
                details.scrollIntoView();
            }
            """, group);
    }

    /// <summary>Waits for <paramref name="clause"/> inside the statement that starts with <paramref name="statement"/>.</summary>
    private static async Task WaitForScriptAsync(IPage page, string statement, string clause)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                ([statement, clause]) => {
                    const text = window.__STUDIO__.state.editorInstance.getValue();
                    const start = text.indexOf(statement);
                    if (start < 0) return false;
                    const end = text.indexOf(');', start);
                    return text.slice(start, end < 0 ? undefined : end).includes(clause);
                }
                """, new[] { statement, clause }, new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
            throw new Xunit.Sdk.XunitException(
                $"'{statement}' never gained '{clause}'.{Environment.NewLine}Script:{Environment.NewLine}{script}", exception);
        }
    }

    private static async Task<string> SaveAsync(IPage page, int reportId)
    {
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");
        return await page.EvaluateAsync<string>(
            """
            async id => {
                const { auth } = await import('/js/api.js');
                const response = await fetch(`/api/reports/${id}/script-content`, {
                    headers: { Authorization: `Bearer ${auth.getToken()}` }
                });
                if (!response.ok) throw new Error(`script-content returned ${response.status}`);
                const payload = await response.json();
                return payload.scriptText ?? payload.script ?? payload.content ?? '';
            }
            """, reportId);
    }

    // ── The running report ───────────────────────────────────────────────────

    private static async Task<IFrameLocator> RunReportAsync(IPage page, int reportId)
    {
        await page.GotoAsync($"/index.html#report-{reportId}");
        var execute = page.Locator("#execBtn");
        var refresh = page.Locator("#refreshBtn");
        await Expect(execute.Or(refresh).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await execute.IsVisibleAsync()) await execute.ClickAsync();
        else await refresh.ClickAsync();

        var report = page.FrameLocator("#reportFrame iframe");
        await Expect(report.Locator("[data-visual-name='ByRegion'] [data-row-index]").First)
            .ToBeVisibleAsync(new() { Timeout = ExecutionTimeoutMs });
        return report;
    }

    /// <summary>Clicks the chart's first mark and returns the region it stands for.</summary>
    private static async Task<string> ClickChartMarkAsync(IFrameLocator report, string visual)
    {
        var mark = report.Locator($"[data-visual-name='{visual}'] [data-row-index]").First;
        var region = await mark.EvaluateAsync<string>(
            """
            mark => {
                const card = mark.closest('[data-visual-name]');
                const data = card._visualData || card.closest('.visual-card')?._visualData;
                const index = (data.columns || []).findIndex(c => c.toLowerCase() === 'region');
                return String(data.rows[Number(mark.dataset.rowIndex)][index]);
            }
            """);
        await mark.ClickAsync(new() { Force = true });
        return region;
    }

    private static async Task<int> ColumnIndexAsync(ILocator table, string column) =>
        await table.EvaluateAsync<int>(
            """
            (card, column) => [...card.querySelector('table').querySelectorAll('thead th')]
                .map(th => th.textContent.trim().toLowerCase())
                .findIndex(text => text.startsWith(column.toLowerCase()))
            """, column);

    private static async Task<string> CellTextAsync(ILocator row, int index) =>
        (await row.Locator("td").Nth(index).TextContentAsync() ?? string.Empty).Trim();

    /// <summary>The distinct regions a table's rendered rows show.</summary>
    private static async Task<HashSet<string>> RegionsShownAsync(IFrameLocator report, string visual)
    {
        var table = report.Locator($"[data-visual-name='{visual}']");
        await Expect(table.Locator("tbody tr").First).ToBeVisibleAsync(new() { Timeout = ExecutionTimeoutMs });
        var values = await table.EvaluateAsync<string[]>(
            """
            card => {
                const table = card.querySelector('table');
                const headers = [...table.querySelectorAll('thead th')].map(th => th.textContent.trim().toLowerCase());
                const index = headers.findIndex(text => text.startsWith('region'));
                return [...table.querySelectorAll('tbody tr')]
                    .filter(tr => tr.querySelector(':scope > td') && !tr.classList.contains('summary-row') && !tr.classList.contains('detail-row'))
                    .map(tr => tr.children[index]?.textContent.trim() ?? '')
                    .filter(Boolean);
            }
            """);
        return new HashSet<string>(values, StringComparer.Ordinal);
    }

    private static async Task WaitForRegionsAsync(IFrameLocator report, string visual, Func<HashSet<string>, bool> done, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        HashSet<string> regions = [];
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                regions = await RegionsShownAsync(report, visual);
                if (done(regions)) return;
            }
            catch (PlaywrightException)
            {
                // The table is re-rendered by the refresh; read it again.
            }
            await Task.Delay(250);
        }
        throw new Xunit.Sdk.XunitException($"{because}; it shows {string.Join(", ", regions)}.");
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    private async Task<int> CreateReportAsync(IPage page)
    {
        var folderId = await CreateWritableFolderAsync();
        var report = await page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const { studioApi } = await import('/js/api.js');
                return studioApi.createReport(request);
            }
            """,
            new
            {
                folderId,
                name = $"Interaction Journey {Guid.NewGuid():N}",
                // Nothing reads a parameter yet: each test has the inspector write what its
                // interaction needs, so the script it starts from is the one an author would have.
                scriptText = $"""
                    CREATE TABLE #sales (SaleID INT, Region VARCHAR(20), Total DECIMAL(10,2));
                    INSERT INTO #sales VALUES (1, 'North', 100), (2, 'North', 150), (3, 'South', 80),
                      (4, 'South', 60), (5, 'West', 200), (6, 'West', 40);
                    SELECT Region, SUM(Total) AS Revenue INTO #by_region FROM #sales GROUP BY Region;
                    CREATE VISUAL ByRegion AS BAR (
                      SOURCE = #by_region,
                      MAPPINGS (X = Region, Y = Revenue)
                    );
                    CREATE VISUAL RegionDrill AS BAR (
                      SOURCE = #by_region,
                      MAPPINGS (X = Region, Y = Revenue)
                    );
                    CREATE VISUAL Detail AS TABLE (
                      SOURCE = #sales
                    );
                    CREATE VISUAL Other AS TABLE (
                      SOURCE = #sales
                    );
                    CREATE VISUAL Orders AS TABLE (
                      SOURCE = #sales
                    );
                    CREATE VISUAL Totals AS TABLE (
                      SOURCE = #by_region
                    );
                    CREATE PAGE [Main] AS DASHBOARD (
                      LAYOUT (
                        STRUCTURE = 'A B
                                     C D
                                     E F',
                        MAP ('A' = ByRegion, 'B' = RegionDrill, 'C' = Detail, 'D' = Other, 'E' = Orders, 'F' = Totals)
                      )
                    );
                    """
            });
        return report.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateWritableFolderAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var adminId = await db.Users.Where(user => user.UserName == PortalBrowserFixture.AdminUsername)
            .Select(user => user.Id)
            .SingleAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder { Name = $"Interactions {suffix}", Path = $"/Interactions-{suffix}", OwnerId = adminId };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }
}
