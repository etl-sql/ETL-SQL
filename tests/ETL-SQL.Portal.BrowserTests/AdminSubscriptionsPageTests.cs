using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(AdminUsersCollection.Name)]
public sealed class AdminSubscriptionsPageTests(AdminUsersFixture fixture)
{
    [Fact]
    public async Task Subscriptions_ParametersPagingStatusAndDeletePersistThroughTheNativePage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reportName = "Native subscription " + suffix;
        var scriptName = "subscription_" + suffix + ".rptsql";
        const string scriptText = "DECLARE @Region STRING INPUT = 'All';";
        await File.WriteAllTextAsync(Path.Combine(fixture.Factory.TempDir, "scripts", scriptName), scriptText);
        int[] ids;
        string parameterName;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = await db.Users.Where(user => user.UserName == "admin").Select(user => user.Id).SingleAsync();
            parameterName = scope.ServiceProvider.GetRequiredService<ReportScriptInspectionService>()
                .ExtractInputParameters(scriptText).Single().Name;
            var folder = new Folder { Name = reportName, Path = "/subscription-" + suffix };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            var report = new Report { Name = reportName, ScriptPath = scriptName, FolderId = folder.Id, CreatedBy = adminId };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            var rows = Enumerable.Range(1, 26).Select(index => new Subscription
            {
                ReportId = report.Id,
                UserId = adminId,
                Name = "Native delivery " + index.ToString("D2"),
                Schedule = "Daily",
                SmtpAlias = "mail",
                Recipients = $"recipient{index:D2}@example.test",
                ParametersJson = JsonSerializer.Serialize(new Dictionary<string, string> { [parameterName] = "North" })
            }).ToArray();
            db.Subscriptions.AddRange(rows);
            await db.SaveChangesAsync();
            ids = rows.Select(row => row.Id).ToArray();
        }
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/admin.html");
        await page.Locator(".admin-tab[data-tab='subscriptions']").ClickAsync();
        await page.Locator("#subsFilter").FillAsync(reportName);
        await Expect(page.Locator("#subsTableWrap")).ToHaveAttributeAsync("data-subscriptions-state", "ready");
        await Expect(page.Locator("#subsTableWrap tbody tr")).ToHaveCountAsync(25);
        await Expect(page.Locator("#subsPager")).ToContainTextAsync("1-25 of 26");
        await page.Locator("#subsPager").GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
        await Expect(page.Locator("#subsTableWrap tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("#subsPager")).ToContainTextAsync("26-26 of 26");
        await page.Locator("#subsPager").GetByRole(AriaRole.Button, new() { Name = "Previous", Exact = true }).ClickAsync();
        await page.Locator($"[data-action='edit-params'][data-id='{ids[0]}']").ClickAsync();
        await Expect(page.Locator("#editParamsFields input")).ToHaveValueAsync("North");
        await page.Locator("#editParamsFields input").FillAsync("East");
        var saved = await page.RunAndWaitForResponseAsync(() => page.Locator("#editParamsSaveBtn").ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.EndsWith($"/api/subscriptions/{ids[0]}", StringComparison.Ordinal));
        Assert.Equal(200, saved.Status);
        await Expect(page.Locator("#editParamsModal")).ToBeHiddenAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var stored = await db.Subscriptions.SingleAsync(row => row.Id == ids[0]);
            Assert.Equal("East", JsonSerializer.Deserialize<Dictionary<string, string>>(stored.ParametersJson!)![parameterName]);
        }
        await page.Locator($"[data-action='edit-params'][data-id='{ids[0]}']").ClickAsync();
        await Expect(page.Locator("#editParamsFields input")).ToHaveValueAsync("East");
        await page.Locator("#editParamsFields input").FillAsync("");
        await page.Locator("#editParamsSaveBtn").ClickAsync();
        await Expect(page.Locator("#editParamsModal")).ToBeHiddenAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Null(await db.Subscriptions.Where(row => row.Id == ids[0]).Select(row => row.ParametersJson).SingleAsync());
        }
        await page.Locator($"#subsTableWrap [data-action='toggle'][data-id='{ids[0]}']").ClickAsync();
        await Expect(page.Locator($"#subsTableWrap [data-action='toggle'][data-id='{ids[0]}']")).ToHaveTextAsync("Resume");
        await page.Locator($"[data-action='history'][data-id='{ids[0]}']").ClickAsync();
        await Expect(page.Locator("#subscriptionHistoryTitle")).ToContainTextAsync("Scheduler Trigger History");
        await Expect(page.Locator("#subscriptionHistoryBody")).ToContainTextAsync("No scheduler triggers");
        await page.Locator("#subscriptionHistoryCloseBtn").ClickAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var run = await store.LogAdHocRunStartAsync(SubscriptionOrchestration.JobName(ids[0], reportName), "portal-host");
            await store.LogJobEndAsync(run, "SUCCESS", rowsProcessed: 4712);
        }
        await page.Locator($"[data-action='history'][data-id='{ids[0]}']").ClickAsync();
        await Expect(page.Locator("#subscriptionHistoryBody tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator("#subscriptionHistoryBody tbody tr")).ToContainTextAsync("SUCCESS");
        await Expect(page.Locator("#subscriptionHistoryBody tbody tr")).ToContainTextAsync("4712");
        await page.Locator("#subscriptionHistoryCloseBtn").ClickAsync();
        await page.Locator($"#subsTableWrap [data-action='delete'][data-id='{ids[0]}']").ClickAsync();
        await page.Locator(".etlsql-feedback-dialog").GetByRole(AriaRole.Button, new() { Name = "Delete subscription", Exact = true }).ClickAsync();
        await Expect(page.Locator($"#subsTableWrap [data-action='delete'][data-id='{ids[0]}']")).ToHaveCountAsync(0);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.Subscriptions.AnyAsync(row => row.Id == ids[0]));
            Assert.Equal(25, await db.Subscriptions.CountAsync(row => ids.Contains(row.Id)));
            Assert.True(await db.AuditLogs.AnyAsync(row => row.Action == "UPDATE_SUBSCRIPTION" && row.ResourceId == ids[0].ToString()));
            Assert.True(await db.AuditLogs.AnyAsync(row => row.Action == "DELETE_SUBSCRIPTION" && row.ResourceId == ids[0].ToString()));
        }
        Assert.Empty(session.PageErrors);
    }
}
