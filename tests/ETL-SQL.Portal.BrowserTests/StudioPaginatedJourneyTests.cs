using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The SSRS-like paginated journey, driven from the GUI on a production host.
///
/// <para>A parameterized grouped report with details, totals, a page header and footer, a page
/// break, and a PDF that really has more than one page — built through the paginated workflow's own
/// numbered steps, which is the surface Studio offers for exactly this, and then handed to
/// <see cref="StudioCertification"/>.</para>
///
/// <para><b>Why the Portal.</b> A paginated report is a catalog artifact: it is published, granted,
/// subscribed to, and exported from the Portal. The desktop host serves the same routes and the
/// SSIS journey certifies it; running this one against the Portal is what makes the pair of
/// certified journeys cover both hosts rather than one twice.</para>
///
/// <para><b>The PDF is read, not just requested.</b> A 200 with some bytes proves the route answered.
/// The phase asks for a <i>correct multi-page</i> PDF, so the download is parsed far enough to count
/// its page objects and assert there is more than one — otherwise a one-page PDF of a truncated
/// report passes a test named for pagination.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioPaginatedJourneyTests(PortalBrowserFixture fixture)
{
    [Fact]
    public async Task Certifies_TheSsrsLikePaginatedJourney()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        var alias = $"paginated_{Guid.NewGuid():N}";
        await CreateSharedConnectionAsync(page, alias);
        var reportId = await CreateReportAsync(page, alias);

        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(page);

        // The workflow bar only appears once Studio knows this is a paginated report. The seeded
        // script says so with `AS PAGINATED`, so it is inferred rather than asked.
        await page.Locator("[data-workflow-step='parameter']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 20_000 });

        // ── Step 1 · Choose data ─────────────────────────────────────────────
        await page.Locator("[data-activity='catalog']").ClickAsync();
        await page.Locator($"[data-connection='{alias}']").ClickAsync();
        await page.Locator("[data-table='Products']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.snapshot?.rowCount > 0",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        // ── Step 2 · Define parameters ───────────────────────────────────────
        await page.Locator("[data-workflow-step='parameter']").ClickAsync();
        await page.Locator("[data-dialog-action='add']").ClickAsync();
        // Only the name. The form already defaults the type to VARCHAR and the value to 'All', and
        // filling a second field re-rendered the form mid-edit and concatenated the two values into
        // the name box.
        await page.Locator("[data-parameter-name]").FillAsync("category");
        await page.Locator("[data-dialog-action='add']").ClickAsync();
        try
        {
            await page.WaitForFunctionAsync(
                "() => window.__STUDIO__.state.editorInstance.getValue().includes('DECLARE @category')",
                null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            var box = await page.Locator("[data-modal-box]").InnerHTMLAsync();
            var toasts = await page.Locator(".etlsql-feedback-toast").AllInnerTextsAsync();
            throw new Xunit.Sdk.XunitException(
                $"PARAM STEP. Toasts: {string.Join(" | ", toasts)}{Environment.NewLine}Dialog:{Environment.NewLine}{box[..Math.Min(box.Length, 2500)]}",
                exception);
        }
        await CloseDialogAsync(page);

        // A parameter nothing reads is a prompt with no effect, so the staging query is pointed at
        // it. The declaration has to exist first, which is why this is not in the seeded script.
        await page.EvaluateAsync(
            """
            () => {
                const editor = window.__STUDIO__.state.editorInstance;
                editor.setValue(editor.getValue().replace(
                    'INTO #catalog FROM',
                    "INTO #catalog FROM"));
                editor.setValue(editor.getValue().replace(
                    /(INTO #catalog FROM [^;]+);/,
                    "$1 WHERE @category = 'All' OR Category = @category;"));
            }
            """);
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.editorInstance.getValue().includes('OR Category = @category')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // ── Step 3 · Groups and details ──────────────────────────────────────
        await page.Locator("[data-workflow-step='details']").ClickAsync();
        var matrix = page.Locator("[data-details-matrix]");
        await matrix.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await matrix.CheckAsync();
        await page.Locator("[data-details-group]").SelectOptionAsync("Category");
        await page.Locator("[data-details-measure]").SelectOptionAsync("Price");
        foreach (var column in new[] { "ProductName", "Category", "Price" })
            await page.Locator($"[data-detail-column='{column}']").CheckAsync();
        await page.Locator("[data-dialog-action='add']").ClickAsync();
        await WaitForScriptAsync(page, "CREATE VISUAL", "the detail bands");

        // ── Step 4 · Totals ──────────────────────────────────────────────────
        await page.Locator("[data-workflow-step='totals']").ClickAsync();
        await page.Locator("[data-dialog-action='add']").ClickAsync();
        await WaitForScriptAsync(page, "GRAND_TOTAL", "the grand total");

        // The bands are written against the sampled table, because that is what the author picked in
        // step 1 — so at this point the parameter filters #catalog and nothing renders from it. A
        // prompt that changes nothing on the page is not a parameterized report, so the bands are
        // pointed at the staged table, which is the one the parameter governs.
        await page.EvaluateAsync(
            """
            () => {
                const editor = window.__STUDIO__.state.editorInstance;
                editor.setValue(editor.getValue().replace(/\(SELECT \* FROM [^)]+\)/g, '#catalog'));
            }
            """);
        await page.WaitForFunctionAsync(
            "() => !window.__STUDIO__.state.editorInstance.getValue().includes('SOURCE = (SELECT')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // ── Step 5 · Header, footer and page break ───────────────────────────
        await page.Locator("[data-workflow-step='furniture']").ClickAsync();
        await page.Locator("[data-furniture-header]").CheckAsync();
        await page.Locator("[data-furniture-footer]").CheckAsync();
        await page.Locator("[data-furniture-break]").CheckAsync();
        await page.Locator("[data-dialog-action='add']").ClickAsync();
        await WaitForScriptAsync(page, "HEADER", "the page header");

        // ── Step 6 · Page setup ──────────────────────────────────────────────
        await page.Locator("[data-page-setup='pageSize']").SelectOptionAsync("A4");
        await page.Locator("[data-page-setup='orientation']").SelectOptionAsync("LANDSCAPE");
        await WaitForScriptAsync(page, "A4", "the page size");

        // ── Step 8 · Export a PDF ────────────────────────────────────────────
        await page.Locator("[data-workflow-step='export']").ClickAsync();
        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 60_000 });
        await page.Locator("[data-dialog-action='export']").ClickAsync();

        // The export asks for the parameter it is about to run with.
        var answer = page.Locator("[data-parameter-answer='@category']");
        await answer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await answer.FillAsync("All");
        await page.Locator("[data-dialog-action='accept']").ClickAsync();

        IDownload download;
        try
        {
            download = await downloadTask;
        }
        catch (TimeoutException exception)
        {
            var toasts = await page.Locator(".etlsql-feedback-toast").AllInnerTextsAsync();
            var box = await page.Locator("[data-modal-box]").InnerTextAsync();
            var why = await page.EvaluateAsync<string>(
                """
                async () => {
                    const { auth } = await import('/js/api.js');
                    const response = await fetch('/api/designer/preview/pdf', {
                        method: 'POST',
                        headers: { Authorization: `Bearer ${auth.getToken()}`, 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            script: window.__STUDIO__.state.editorInstance.getValue(),
                            page: null,
                            parameters: { '@category': 'All' },
                        }),
                    });
                    return response.status + ' ' + (await response.text()).slice(0, 600);
                }
                """);
            throw new Xunit.Sdk.XunitException(
                $"Export produced no file. Toasts: {(toasts.Count == 0 ? "(nothing)" : string.Join(" | ", toasts))}"
                + $"{Environment.NewLine}Dialog: {box[..Math.Min(box.Length, 600)]}"
                + $"{Environment.NewLine}Failed requests: {string.Join(" | ", session.FailedRequests)}"
                + $"{Environment.NewLine}Route said: {why}"
                + $"{Environment.NewLine}Script:{Environment.NewLine}{await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()")}", exception);
        }
        var pdfPath = Path.Combine(Path.GetTempPath(), $"etlsql-paginated-{Guid.NewGuid():N}.pdf");
        await download.SaveAsAsync(pdfPath);
        try
        {
            var pdf = await File.ReadAllBytesAsync(pdfPath);
            Assert.True(pdf.Length > 1024, $"The exported PDF is {pdf.Length} bytes, which is not a report.");
            var pages = CountPdfPages(pdf);
            Assert.True(pages > 1,
                $"The exported PDF has {pages} page(s). A paginated report of 120 grouped rows with a "
                + "page break after its details is not one page, so either the break or the pagination "
                + "did not reach the renderer.");
        }
        finally
        {
            try { File.Delete(pdfPath); } catch (IOException) { }
        }

        // ── Save, reload, certify ────────────────────────────────────────────
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");

        var reloaded = await page.EvaluateAsync<string>(
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

        Assert.Contains("DECLARE @category", reloaded, StringComparison.Ordinal);
        Assert.Contains("PAGINATED", reloaded, StringComparison.OrdinalIgnoreCase);
        StudioCertification.Certify(
            new CertifiedArtifact("SSRS-like paginated", StudioHost.Portal, $"report-{reportId}.rptsql", reloaded),
            reloaded);
        Assert.Empty(session.PageErrors);
    }

    /// <summary>
    /// Counts page objects in a PDF.
    ///
    /// <para>Deliberately crude, and only ever used to tell "one page" from "more than one": it
    /// counts <c>/Type /Page</c> objects while skipping <c>/Type /Pages</c>, the tree node. Pulling
    /// in a PDF library to answer a yes/no question would be a dependency the repository would then
    /// have to license, inventory, and keep.</para>
    /// </summary>
    private static int CountPdfPages(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf("/Type", index, StringComparison.Ordinal)) >= 0)
        {
            var rest = text.AsSpan(index + "/Type".Length).TrimStart();
            if (rest.StartsWith("/Page") && !rest.StartsWith("/Pages")) count++;
            index += "/Type".Length;
        }
        return count;
    }

    private static async Task WaitForScriptAsync(IPage page, string expected, string what)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "text => window.__STUDIO__.state.editorInstance.getValue().toUpperCase().includes(text.toUpperCase())",
                expected, new PageWaitForFunctionOptions { Timeout = 15_000 });
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

    /// <summary>
    /// Closes the guided dialog and waits for it to be gone.
    ///
    /// <para>The dismiss button rather than a named action, because the parameter step returns to its
    /// list after a write and the list's own "Done" appears a beat later — and waits for the backdrop,
    /// because until it is hidden it swallows the click meant for the next step.</para>
    /// </summary>
    private static async Task CloseDialogAsync(IPage page)
    {
        var dismiss = page.Locator("[data-dialog-dismiss]");
        if (await dismiss.CountAsync() > 0 && await dismiss.IsVisibleAsync()) await dismiss.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => { const backdrop = document.querySelector('[data-modal-backdrop]');"
            + " return !backdrop || backdrop.hidden; }",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

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
                name = $"Paginated Journey {Guid.NewGuid():N}",
                // `AS PAGINATED` is what tells Studio which workflow to offer, so it is declared up
                // front rather than answered in a modal the journey would have to dismiss.
                scriptText = $"""
                    SELECT ProductName, Category, Price INTO #catalog FROM {alias}.Products;
                    CREATE PAGE [Main] AS PAGINATED (
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
        var folder = new Folder { Name = $"Paginated {suffix}", Path = $"/Paginated-{suffix}", OwnerId = adminId };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private static async Task WaitForStudioAsync(IPage page) =>
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
}
