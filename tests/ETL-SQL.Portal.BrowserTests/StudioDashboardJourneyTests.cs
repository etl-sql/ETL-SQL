using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The Power BI-like dashboard journey, driven from the GUI on a production host.
///
/// <para>A KPI, a trend, a category breakdown and a detail table, a slicer the reader drives, the
/// cross-filter that slicer performs, and formatting that is still there after a save and a reload —
/// built through the chart builder and the filter pane, then handed to
/// <see cref="StudioCertification"/>.</para>
///
/// <para><b>Cross-filtering is asserted as the mechanism, not as a word.</b> Promoting a filter to a
/// viewer control writes three things that only mean something together: a parameter, a control whose
/// <c>ON_CHANGE</c> sets it, and a dataset filtered by it. Checking only that a <c>SLICER</c> exists
/// would pass for a control wired to nothing, which is the failure worth catching.</para>
///
/// <para><b>Formatting is asserted after the reload</b>, from the bytes the host returned. Formatting
/// that survives until the tab is closed is not persistent formatting.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioDashboardJourneyTests(PortalBrowserFixture fixture)
{
    private const string KpiTitle = "Revenue to date";
    private const string TrendTitle = "Revenue over time";
    private const string CategoryTitle = "Revenue by region";
    private const string DetailTitle = "Sales detail";

    [Fact]
    public async Task Certifies_ThePowerBiLikeDashboardJourney()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        var alias = $"dashboard_{Guid.NewGuid():N}";
        await CreateSharedConnectionAsync(page, alias);
        var reportId = await CreateReportAsync(page, alias);

        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(page);
        await page.Locator("[data-workflow-step='palette']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 20_000 });

        // ── Step 1 · Data, as one named query ────────────────────────────────
        // A dataset rather than a table, because that is what makes the next two steps mean
        // anything: every visual reads the same named query, so narrowing it narrows the page. Built
        // on the connection table each visual would carry its own inlined source, and a slicer could
        // only ever reach the one visual that happened to be selected when the filter was made.
        await page.Locator("[data-activity='catalog']").ClickAsync();
        await page.Locator("[data-new-dataset]").ClickAsync();
        await page.Locator("[data-start-path='create']").ClickAsync();
        await page.Locator($"[data-pick-connection='{alias}']").ClickAsync();
        await page.Locator("[data-pick-table='Sales']").ClickAsync();
        await page.Locator("[data-dialog-action='next']").ClickAsync();
        var datasetName = page.Locator("[data-dataset-name]");
        await datasetName.WaitForAsync(new LocatorWaitForOptions { Timeout = 20_000 });
        await datasetName.FillAsync("sales");
        await page.Locator("[data-dialog-action='create']").ClickAsync();
        await WaitForScriptAsync(page, "CREATE DATASET", "the dataset");
        await WaitForDialogClosedAsync(page);
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.snapshot?.rowCount > 0",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });

        // ── Step 2 · The four visuals a dashboard is made of ─────────────────
        await AddVisualAsync(page, "CARD", KpiTitle, new() { ["VALUE"] = "Total" });
        await AddVisualAsync(page, "LINE", TrendTitle, new() { ["X"] = "OrderDate", ["Y"] = "Total" });
        await AddVisualAsync(page, "BAR", CategoryTitle, new() { ["X"] = "Region", ["Y"] = "Total" });
        await AddVisualAsync(page, "TABLE", DetailTitle, new() { ["COLUMN1"] = "Region", ["COLUMN2"] = "Total" });

        // ── Step 3 · A slicer, and the cross-filter it performs ──────────────
        // Filtering a field and then promoting it is the documented one-click path: the promotion is
        // what turns a design-time filter into a control the reader drives.
        await page.Locator("[data-activity='catalog']").ClickAsync();
        await page.Locator("[data-field='Region']").ClickAsync();
        // Scoped to the dataset before it is applied. A visual-scoped filter edits one visual's own
        // source, which narrows that visual and nothing else; the dataset is the query every visual
        // on this page reads, so scoping it there is what makes one control filter the page.
        var dialogScope = page.Locator("[data-filter-dialog-scope]");
        await dialogScope.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await dialogScope.SelectOptionAsync("dataset");
        await page.Locator("[data-filter-dialog-apply]").ClickAsync();
        await page.Locator("[data-filter-value='Region']").First.CheckAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('ETL-SQL-STUDIO-FILTER')",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        await page.Locator("[data-promote-slicer='Region']").ClickAsync();
        await WaitForScriptAsync(page, "SET_PARAMETER", "the slicer's cross-filter action");


        // ── Save, reload, certify ────────────────────────────────────────────
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");

        var reloaded = await ReadHostScriptAsync(page, reportId);

        // The four visuals, by the type each one has to be.
        Assert.Contains("AS CARD", reloaded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS LINE", reloaded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS BAR", reloaded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS TABLE", reloaded, StringComparison.OrdinalIgnoreCase);

        // Persistent formatting: the titles were typed into the builder, and they are read back from
        // the bytes the host returned rather than from the editor that typed them.
        foreach (var title in new[] { KpiTitle, TrendTitle, CategoryTitle, DetailTitle })
            Assert.Contains(title, reloaded, StringComparison.Ordinal);

        // Cross-filtering, as the three parts that make it work rather than as the word "slicer".
        Assert.Contains("@selected_region", reloaded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET_PARAMETER", reloaded, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            reloaded.Contains("AS SLICER", StringComparison.OrdinalIgnoreCase)
            || reloaded.Contains("AS MULTISELECT", StringComparison.OrdinalIgnoreCase),
            $"The promoted filter produced no viewer control.{Environment.NewLine}{reloaded}");

        // The claim that separates a slicer from cross-filtering: the parameter narrows the dataset
        // every visual reads, not one visual's own source. Without this a control can exist, be
        // wired to a parameter, and still leave three of the four visuals showing everything.
        var dataset = reloaded[reloaded.IndexOf("CREATE DATASET", StringComparison.OrdinalIgnoreCase)..];
        dataset = dataset[..dataset.IndexOf(");", StringComparison.Ordinal)];
        Assert.Contains("@selected_region", dataset, StringComparison.OrdinalIgnoreCase);
        foreach (var visual in new[] { "revenue_to_date", "revenue_over_time", "revenue_by_region" })
            Assert.Contains($"CREATE VISUAL {visual}", reloaded, StringComparison.OrdinalIgnoreCase);

        StudioCertification.Certify(
            new CertifiedArtifact("Power BI-like dashboard", StudioHost.Portal, $"report-{reportId}.rptsql", reloaded),
            reloaded);
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Adds one visual through the chart builder: pick the type, bind its roles, title it, add it.
    /// </summary>
    private static async Task AddVisualAsync(IPage page, string type, string title, Dictionary<string, string> roles)
    {
        // Away and back rather than straight to the palette. Adding a visual selects it, which
        // replaces the panel with the format inspector while the rail still reads "palette" - so a
        // single click on an already-active rail button collapses the panel instead of showing the
        // list, and the chip stays present but hidden.
        await page.Locator("[data-activity='catalog']").ClickAsync();
        await page.Locator("[data-activity='palette']").ClickAsync();
        var chip = page.Locator($"[data-add-visual='{type}']");
        await chip.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await chip.ClickAsync();

        var typeButton = page.Locator($"[data-builder-type='{type}']");
        await typeButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await typeButton.ClickAsync();

        foreach (var (role, column) in roles)
        {
            // A repeatable role only offers the next slot once the previous one is bound, so the
            // slot is added when it is not already on screen.
            var select = page.Locator($"[data-role-select='{role}']");
            if (await select.CountAsync() == 0)
            {
                var add = page.Locator("[data-role-add]");
                if (await add.CountAsync() > 0) await add.First.ClickAsync();
                await select.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
            }
            await select.SelectOptionAsync(column);
        }

        await page.Locator("[data-builder-title]").FillAsync(title);
        await page.Locator("[data-dialog-action='add']").ClickAsync();
        await WaitForScriptAsync(page, title, $"the {type} visual");
        await WaitForDialogClosedAsync(page);
    }

    private static async Task WaitForScriptAsync(IPage page, string expected, string what)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "text => window.__STUDIO__.state.editorInstance.getValue().toUpperCase().includes(text.toUpperCase())",
                expected, new PageWaitForFunctionOptions { Timeout = 20_000 });
        }
        catch (TimeoutException exception)
        {
            var toasts = await page.Locator(".etlsql-feedback-toast").AllInnerTextsAsync();
            var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
            throw new Xunit.Sdk.XunitException(
                $"The step that writes {what} put no '{expected}' in the script. "
                + $"Feedback said: {(toasts.Count == 0 ? "(nothing)" : string.Join(" | ", toasts))}"
                + $"{Environment.NewLine}Script:{Environment.NewLine}{script}", exception);
        }
    }

    private static async Task WaitForDialogClosedAsync(IPage page) =>
        await page.WaitForFunctionAsync(
            "() => { const backdrop = document.querySelector('[data-modal-backdrop]');"
            + " return !backdrop || backdrop.hidden; }",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static async Task<string> ReadHostScriptAsync(IPage page, int reportId) =>
        await page.EvaluateAsync<string>(
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

    private static async Task CreateSharedConnectionAsync(IPage page, string alias)
    {
        var status = await page.EvaluateAsync<int>(
            """
            async alias => {
                const { auth } = await import('/js/api.js');
                const response = await fetch(`/api/admin/connections/${encodeURIComponent(alias)}`, {
                    method: 'PUT',
                    headers: {
                        Authorization: `Bearer ${auth.getToken()}`,
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({ connectorType: 'MOCKDB', options: {} })
                });
                return response.status;
            }
            """, alias);
        Assert.Equal(204, status);
    }

    private async Task<int> CreateReportAsync(IPage page, string alias)
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
                name = $"Dashboard Journey {Guid.NewGuid():N}",
                // `AS DASHBOARD` is what tells Studio which workflow to offer.
                scriptText = $"""
                    SELECT OrderDate, Region, Total INTO #sales FROM {alias}.Sales;
                    CREATE PAGE [Main] AS DASHBOARD (
                      LAYOUT (STRUCTURE = '.')
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
        var folder = new Folder { Name = $"Dashboard {suffix}", Path = $"/Dashboard-{suffix}", OwnerId = adminId };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private static async Task WaitForStudioAsync(IPage page) =>
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
}
