using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(AdminUsersCollection.Name)]
public sealed class AdminReportsPageTests(AdminUsersFixture fixture)
{
    [Fact]
    public async Task Reports_PublishEditConflictAndCascadeDeletePersistThroughTheNativePage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportName = "Admin report " + suffix;
        var folderName = "Admin reports " + suffix;
        var scriptName = "admin_report_" + suffix + ".rptsql";
        await File.WriteAllTextAsync(Path.Combine(fixture.Factory.TempDir, "scripts", scriptName), """
            SET REPORT TITLE = 'Admin report acceptance';
            SELECT 'North' AS Region, 1042 AS Sales INTO #data;
            CREATE VISUAL SalesTable AS TABLE (SOURCE = #data, MAPPINGS (Region, Sales));
            CREATE PAGE Page1 AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesTable)));
            """);
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/admin.html");
        await Expect(page.Locator("#userTableWrap")).ToHaveAttributeAsync("data-users-state", "ready");
        await page.Locator(".admin-tab[data-tab='reports']").ClickAsync();
        await Expect(page.Locator("#reportsTableWrap")).ToHaveAttributeAsync("data-reports-state", "ready");
        await page.Locator("#openPublishBtn").ClickAsync();
        await Expect(page.Locator("#pr-newFolderToggle")).ToBeEnabledAsync();
        await page.Locator("#pr-name").FillAsync(reportName);
        await page.Locator("#pr-desc").FillAsync("Published description");
        await page.Locator("#pr-browseBtn").ClickAsync();
        await page.Locator("#sb-filter").FillAsync(scriptName);
        await Expect(page.Locator("#sb-list [data-path]")).ToHaveCountAsync(1);
        await page.Locator("#sb-list [data-path]").FocusAsync();
        await page.Locator("#sb-list [data-path]").PressAsync("Space");
        await Expect(page.Locator("#pr-path")).ToHaveValueAsync(scriptName);
        await Expect(page.Locator("#pr-path")).ToBeFocusedAsync();
        await page.Locator("#pr-newFolderToggle").ClickAsync();
        await page.Locator("#pr-newFolderName").FillAsync(folderName);
        await page.Locator("#pr-newFolderParent").SelectOptionAsync("");
        var folderCreate = await page.RunAndWaitForResponseAsync(() => page.Locator("#pr-newFolderCreate").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/folders", StringComparison.Ordinal));
        Assert.Equal(201, folderCreate.Status);
        await Expect(page.Locator("#pr-folder option:checked")).ToHaveTextAsync("/" + folderName);
        await Expect(page.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        var validation = await page.RunAndWaitForResponseAsync(() => page.Locator("#pr-validateBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/reports/validate", StringComparison.Ordinal));
        Assert.Equal(200, validation.Status);
        await Expect(page.Locator("#pr-validation")).ToContainTextAsync("sha256:");
        var publication = await page.RunAndWaitForResponseAsync(() => page.Locator("#pr-saveBtn").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/reports", StringComparison.Ordinal));
        Assert.Equal(201, publication.Status);
        await Expect(page.Locator("#publishReportForm")).ToBeHiddenAsync();
        await Expect(page.Locator("#reportsTableWrap")).ToContainTextAsync(reportName);
        int reportId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports
                .Include(item => item.Folder).SingleAsync(item => item.Name == reportName);
            reportId = report.Id;
            Assert.Equal("Published description", report.Description);
            Assert.Equal(folderName, report.Folder.Name);
            Assert.Equal(scriptName, Path.GetFileName(report.ScriptPath));
            Assert.Equal(1, report.Version);
        }
        await page.Locator($"[data-action='edit-report'][data-id='{reportId}']").ClickAsync();
        await Expect(page.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await page.Locator("#pr-desc").FillAsync("Updated description");
        var update = await page.RunAndWaitForResponseAsync(() => page.Locator("#pr-saveBtn").ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.EndsWith($"/api/reports/{reportId}", StringComparison.Ordinal));
        Assert.Equal(200, update.Status);
        await Expect(page.Locator("#publishReportForm")).ToBeHiddenAsync();
        await Expect(page.Locator("#reportsTableWrap")).ToHaveAttributeAsync("data-reports-state", "ready");
        await page.Locator($"[data-action='edit-report'][data-id='{reportId}']").ClickAsync();
        await Expect(page.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        await page.Locator("#pr-desc").FillAsync("Private conflict draft");
        await page.EvaluateAsync("""
            async id => {
              const { apiFetch } = await import('/js/api.js');
              const response = await apiFetch('/api/reports/' + id, {
                method: 'PUT', headers: { 'If-Match': '"2"' }, body: { description: 'External description' }
              });
              if (response.status !== 200) throw new Error('External report update failed: ' + response.status);
            }
            """, reportId);
        var refreshed = await page.RunAndWaitForResponseAsync(() => page.Locator("#reportsRefreshBtn").ClickAsync(),
            response => response.Request.Method == "GET" && response.Url.EndsWith("/api/admin/reports", StringComparison.Ordinal));
        Assert.Equal(200, refreshed.Status);
        await Expect(page.Locator("#reportsTableWrap")).ToHaveAttributeAsync("data-reports-state", "ready");
        await Expect(page.Locator("#pr-saveBtn")).ToBeEnabledAsync();
        var conflict = await page.RunAndWaitForResponseAsync(() => page.Locator("#pr-saveBtn").ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.EndsWith($"/api/reports/{reportId}", StringComparison.Ordinal));
        Assert.Equal(409, conflict.Status);
        await Expect(page.Locator("#publishReportForm")).ToBeVisibleAsync();
        await Expect(page.Locator("#pr-desc")).ToHaveValueAsync("Private conflict draft");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(item => item.Id == reportId);
            Assert.Equal("External description", report.Description);
            Assert.Equal(3, report.Version);
            var adminId = await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
            db.Subscriptions.Add(new Subscription
            {
                ReportId = reportId,
                UserId = adminId,
                Name = "Deletion acceptance",
                Recipients = "admin@example.test",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
        await page.Locator("#pr-cancelBtn").ClickAsync();
        var deletion = await page.RunAndWaitForResponseAsync(async () =>
        {
            await page.Locator($"[data-action='delete-report'][data-id='{reportId}']").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Delete report", Exact = true }).ClickAsync();
        }, response => response.Request.Method == "DELETE" && response.Url.EndsWith($"/api/reports/{reportId}?cascade=true", StringComparison.Ordinal));
        Assert.Equal(204, deletion.Status);
        await Expect(page.Locator("#reportsTableWrap")).Not.ToContainTextAsync(reportName);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.Reports.Where(item => item.Id == reportId).Select(item => item.IsDeleted).SingleAsync());
            Assert.False(await db.Subscriptions.Where(item => item.ReportId == reportId).Select(item => item.IsActive).SingleAsync());
        }
        Assert.Empty(session.PageErrors);
    }
}
