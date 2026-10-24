using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(AdminUsersCollection.Name)]
public sealed class AdminAuditPageTests(AdminUsersFixture fixture)
{
    [Fact]
    public async Task Audit_PagingAndCsvRetainAllActorsThroughTokenRefresh()
    {
        var action = "AUDIT_BROWSER_" + Guid.NewGuid().ToString("N")[..8];
        int[] ids;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
            var timestamp = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
            var entries = Enumerable.Range(1, 61).Select(index => new AuditLog
            {
                UserId = index == 61 ? null : index == 60 ? int.MaxValue : adminId,
                Action = action,
                Detail = index == 61 ? "System, \"retained\" evidence" : "Native evidence " + index,
                Timestamp = timestamp,
                ResourceType = "NativeFixture",
                ResourceId = index.ToString()
            }).ToArray();
            db.AuditLogs.AddRange(entries);
            await db.SaveChangesAsync();
            ids = entries.Select(entry => entry.Id).OrderDescending().ToArray();
        }
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoWithDiagnosticsAsync("/admin.html");
        await Expect(page.Locator("#userTableWrap")).ToHaveAttributeAsync("data-users-state", "ready");
        await page.Locator(".admin-tab[data-tab='audit']").ClickAsync();
        await page.Locator("#auditFilter").FillAsync(action);
        await page.Locator("#auditFilter").PressAsync("Enter");
        await Expect(page.Locator("#auditTableWrap")).ToHaveAttributeAsync("data-audit-state", "ready");
        await Expect(page.Locator("#auditTableWrap tbody tr")).ToHaveCountAsync(50);
        await Expect(page.Locator("#auditTableWrap tbody tr").First).ToHaveAttributeAsync("data-audit-id", ids[0].ToString());
        await Expect(page.Locator("#auditTableWrap tbody tr").First).ToContainTextAsync("No account");
        await Expect(page.Locator("#auditTableWrap tbody tr").Nth(1)).ToContainTextAsync("Unavailable account");
        await Expect(page.Locator("#auditTableWrap tbody tr").Nth(2).Locator("td").Nth(1)).ToHaveTextAsync("admin");
        await Expect(page.Locator("#auditPager")).ToContainTextAsync("1-50 of 61");
        await page.Locator("#auditPager").GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
        await Expect(page.Locator("#auditPager")).ToContainTextAsync("51-61 of 61");
        await Expect(page.Locator("#auditTableWrap tbody tr")).ToHaveCountAsync(11);
        await Expect(page.Locator("#auditTableWrap tbody tr").Last).ToHaveAttributeAsync("data-audit-id", ids[^1].ToString());

        var exportRequests = 0;
        await page.RouteAsync("**/api/admin/audit/export/csv?*", async route =>
        {
            if (++exportRequests == 1)
                await route.FulfillAsync(new() { Status = 401, ContentType = "application/json", Body = "{}" });
            else await route.ContinueAsync();
        });
        var refresh = page.WaitForResponseAsync(response => response.Request.Method == "POST"
            && response.Url.EndsWith("/api/auth/refresh", StringComparison.Ordinal));
        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("#auditExportBtn").ClickAsync());
        Assert.Equal(200, (await refresh).Status);
        Assert.Equal(2, exportRequests);
        Assert.EndsWith(".csv", download.SuggestedFilename, StringComparison.Ordinal);
        Assert.Null(await download.FailureAsync());
        var downloadedPath = await download.PathAsync();
        Assert.NotNull(downloadedPath);
        var csv = await File.ReadAllTextAsync(downloadedPath);
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd().Split('\n');
        Assert.Equal(62, lines.Length);
        Assert.StartsWith("Id,Timestamp,UserId,Username,Action", lines[0], StringComparison.Ordinal);
        Assert.StartsWith(ids[0] + ",", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"System, \"\"retained\"\" evidence\"", lines[1], StringComparison.Ordinal);
        Assert.Contains(action, lines[1], StringComparison.Ordinal);
        Assert.Contains("Native evidence 1", lines[^1], StringComparison.Ordinal);
        await Expect(page.Locator("#auditExportStatus")).ToContainTextAsync("download started");
        await Expect(page.Locator("#auditExportBtn")).ToBeEnabledAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(entry => entry.Action == "EXPORT_AUDIT_LOG" && entry.Detail != null && entry.Detail.Contains(action)));
        }
        Assert.Empty(session.PageErrors);
    }
}
