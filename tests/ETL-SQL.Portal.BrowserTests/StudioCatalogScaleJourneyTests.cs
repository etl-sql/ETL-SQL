using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioCatalogScaleJourneyTests(StudioAuthoringFixture fixture)
{
    [Fact]
    public async Task HomePagesAndSearches2500DocumentsAndRefreshesWithoutChangingDirtyBuffers()
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(session.Page, workspace, false);
        var page = session.Page;
        const string dirtyText = "-- keep my unsaved work\nSELECT 42;";
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, dirtyText);
        var prefix = "Scale" + Guid.NewGuid().ToString("N")[..8];
        int folderId;
        int otherFolderId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(user => user.UserName == PortalBrowserFixture.AdminUsername);
            var folder = new Folder { Name = prefix, Path = "/" + prefix, OwnerId = admin.Id };
            var other = new Folder { Name = prefix + "Moved", Path = "/" + prefix + "Moved", OwnerId = admin.Id };
            db.Folders.AddRange(folder, other);
            await db.SaveChangesAsync();
            folderId = folder.Id;
            otherFolderId = other.Id;
            db.Reports.AddRange(Enumerable.Range(0, 2500).Select(index => new Report
            {
                Name = $"{prefix}-{index:D4}",
                FolderId = folderId,
                CreatedBy = admin.Id,
                ScriptPath = $"fixture/{prefix}-{index}.rptsql"
            }));
            await db.SaveChangesAsync();
        }
        await page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Home", Exact = true }).ClickAsync();
        await page.GetByRole(Microsoft.Playwright.AriaRole.Textbox, new() { Name = "Search catalog", Exact = true }).FillAsync(prefix);
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("prefix => window.__STUDIO__.state.catalogReports.length === 50 && window.__STUDIO__.state.catalogReports.every(r => r.name.startsWith(prefix))", prefix);
        var first = await page.EvaluateAsync<int>("() => window.__STUDIO__.state.catalogReports[0].id");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("first => window.__STUDIO__.state.catalogReports.length === 50 && window.__STUDIO__.state.catalogReports[0].id > first", first);
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Previous page", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("first => window.__STUDIO__.state.catalogReports[0].id === first", first);

        await page.GetByRole(Microsoft.Playwright.AriaRole.Textbox, new() { Name = "Search catalog", Exact = true }).FillAsync(prefix + "Probe");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.catalogReports.length === 0");
        var probe = await page.EvaluateAsync<JsonElement>("async data => { const {studioApi} = await import('/js/api.js'); return studioApi.createReport(data); }",
            new { folderId, name = prefix + "Probe", scriptText = "SELECT 1;" });
        var probeId = probe.GetProperty("id").GetInt32();
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("id => window.__STUDIO__.state.catalogReports.length === 1 && window.__STUDIO__.state.catalogReports[0].id === id", probeId);
        var directPage = await page.Context.NewPageAsync();
        try
        {
            await fixture.SignInAsync(directPage);
            await directPage.GotoWithDiagnosticsAsync($"/studio.html?reportId={probeId}");
            await directPage.Locator("[data-choose-workflow='dashboard']").ClickAsync();
            await directPage.WaitForFunctionAsync("id => window.__STUDIO__?.state.documents.some(document => document.reportId === id)", probeId);
            Assert.Equal("SELECT 1;", await directPage.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
        }
        finally { await directPage.CloseAsync(); }
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(report => report.Id == probeId);
            report.FolderId = otherFolderId;
            report.Name = prefix + "ProbeRenamed";
            await db.SaveChangesAsync();
        }
        await page.Locator($"[data-open-report='{probeId}']").Last.ClickAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("moved since the catalog was loaded");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("id => window.__STUDIO__.state.catalogReports[0]?.folderId === id", otherFolderId);
        Assert.Equal(prefix + "ProbeRenamed", await page.EvaluateAsync<string>("() => window.__STUDIO__.state.catalogReports[0].name"));
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            (await db.Reports.SingleAsync(report => report.Id == probeId)).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        await page.Locator($"[data-open-report='{probeId}']").Last.ClickAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("unavailable or outside your catalog permissions");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.catalogReports.length === 0");
        Assert.Equal(dirtyText, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.documents[0].content"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));
        Assert.Empty(session.PageErrors);
    }
}
