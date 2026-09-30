using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// A pipeline authored in Portal Studio with no desktop tool: created from Home, written, saved to
/// the catalog, and reopened from it (docs/architecture/decisions/portal-etl-documents.md).
/// </summary>
[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioPortalPipelineJourneyTests(StudioAuthoringFixture fixture)
{
    private const string Body = "CREATE TABLE #staged (Id INT);\nINSERT INTO #staged VALUES (1);\nASSERT (SELECT COUNT(*) FROM #staged) = 1;\n";

    [Fact]
    public async Task APipelineIsCreatedSavedAndReopenedInThePortal()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var folder = await CreateWritableFolderAsync();
        await page.GotoAsync("/studio.html");

        var card = page.Locator("[data-create-from-home='etl']:not([data-seed-sample])");
        await Expect(card).Not.ToHaveAttributeAsync("aria-disabled", "true", new() { Timeout = 20_000 });
        await card.ClickAsync();
        var name = $"Load orders {Guid.NewGuid():N}"[..20];
        await page.Locator("[data-catalog-report-name]").FillAsync(name);
        await page.Locator("[data-catalog-report-folder]").SelectOptionAsync(folder.Id.ToString());
        await page.Locator("[data-catalog-create-confirm]").ClickAsync();

        await page.WaitForFunctionAsync(
            "name => (window.__STUDIO__?.state?.documents || []).some(d => d.name === name + '.etlsql' && d.reportId) && window.__STUDIO__.state.editorInstance",
            name, new PageWaitForFunctionOptions { Timeout = 20_000 });
        var reportId = await page.EvaluateAsync<int>("name => window.__STUDIO__.state.documents.find(d => d.name === name + '.etlsql').reportId", name);

        await page.EvaluateAsync("body => window.__STUDIO__.state.editorInstance.setValue(body)", Body);
        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync(
            "id => window.__STUDIO__.state.documents.find(d => d.reportId === id)?.isDirty === false", reportId,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports.SingleAsync(r => r.Id == reportId);
            Assert.Equal(CatalogDocumentKind.Pipeline, stored.Kind);
        }

        // A fresh page lists it among catalog documents and opens it as ETL-SQL with the saved text.
        await page.GotoAsync("/studio.html");
        var open = page.Locator($"[data-open-report='{reportId}'][data-open-proj='code']");
        await Expect(open).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await open.ClickAsync();
        await page.WaitForFunctionAsync(
            "([id, body]) => { const s = window.__STUDIO__.state; const d = s.documents.find(x => x.reportId === id);"
            + " return d && d.path.endsWith('.etlsql') && s.editorInstance && s.editorInstance.getValue().replace(/\\r\\n/g, '\\n') === body; }",
            new object[] { reportId, Body }, new PageWaitForFunctionOptions { Timeout = 20_000 });
        Assert.Empty(session.PageErrors);
    }

    private async Task<Folder> CreateWritableFolderAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var adminId = await db.Users.Where(user => user.UserName == PortalBrowserFixture.AdminUsername)
            .Select(user => user.Id)
            .SingleAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder { Name = $"Pipelines {suffix}", Path = $"/Pipelines-{suffix}", OwnerId = adminId };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }
}
