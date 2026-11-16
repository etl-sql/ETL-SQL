using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(DataQualityQueueCollection.Name)]
public sealed class DataQualityQueuePageTests(DataQualityQueueFixture fixture)
{
    [Fact]
    public async Task QualityQueue_RealCatalogPreviewTrendAndAuditedDispositionUseServerContracts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var job = $"browser_quality_{Guid.NewGuid():N}";
        var alias = $"dq_{Guid.NewGuid():N}";
        var target = $"{alias}.q_rows";
        var path = Path.Combine(fixture.Factory.TempDir, $"{alias}.csv");
        await File.WriteAllTextAsync(path, "Id,Email,__dq_row_id,__dq_status,__dq_rule\n1,bad-address,row-a,quarantined,email_format\n2,ok@example.com,row-b,replaying,email_format\n");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.PortalSharedConnections.Add(new PortalSharedConnection
            {
                Alias = alias,
                ConnectorType = "FLATFILE",
                Target = path.Replace('\\', '/'),
                OptionsJson = """{"HEADER":"ON"}"""
            });
            await db.SaveChangesAsync();
        }
        var store = fixture.Factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SaveJobAsync(new JobDefinition(job, "missing_quality_script.etlsql", 1, "DAY", null, null, null));
        var savedJob = await store.GetJobAsync(null, job);
        Assert.NotNull(savedJob);
        await store.SetJobStateAsync(savedJob!.Id, $"dq:quarantine-manifest:{target}", JsonSerializer.Serialize(
            new QuarantineReplayManifest(job, "missing_quality_script.etlsql", "Users", "#source", target,
                true, null, ["Id", "Email"], "browser-quality", DateTimeOffset.UtcNow,
                TargetConnectionAlias: alias, TargetConnectorType: "FLATFILE", TargetIsCatalogBacked: true)));
        var historyId = await store.LogJobStartAsync(savedJob.Id);
        await store.LogJobEndAsync(historyId, "SUCCESS", rowsProcessed: 100, rowsQuarantined: 5, rowsWarned: 2);
        await store.SaveJobDataQualityFailuresAsync(historyId, [
            new DataQualityRuleFailureMetric(target, "Email", "NOT NULL", "QUARANTINE", 5, "DataSteward")
        ]);
        await page.GotoWithDiagnosticsAsync($"/index.html?target={Uri.EscapeDataString(target)}&jobName={job}#governance/quarantine");
        await Expect(page.Locator("#dqRowsModalTitle")).ToHaveTextAsync(target);
        await Expect(page.Locator("[data-edit-row='row-a'][data-edit-column='Email']")).ToHaveValueAsync("bad-address");
        await Expect(page.Locator("[data-edit-row='row-b']")).ToHaveCountAsync(0);
        await page.Locator("#dqCloseRows").ClickAsync();
        await page.Locator($"[data-trend-job='{job}']").ClickAsync();
        await Expect(page.Locator("#dqTrendModalTitle")).ToHaveTextAsync(job);
        await Expect(page.Locator(".dq-page [role='dialog']")).ToContainTextAsync("Average over 1 run(s)");
        await Expect(page.Locator(".dq-page [role='dialog']")).ToContainTextAsync("5.00%");
        await Expect(page.Locator(".dq-page [role='dialog']")).ToContainTextAsync("DataSteward");
        await page.Locator("#dqTrendClose").ClickAsync();
        await page.Locator($"[data-review-target='{target}']").ClickAsync();
        await Expect(page.Locator("[data-note-row='row-a']")).ToBeVisibleAsync();
        await page.Locator("[data-note-row='row-a']").FillAsync("Confirmed duplicate source event");
        var response = await page.RunAndWaitForResponseAsync(() => page.Locator("[data-discard-row='row-a']").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/data-quality/quarantine/disposition", StringComparison.Ordinal));
        Assert.Equal(202, response.Status);
        using var submitted = JsonDocument.Parse(await response.TextAsync());
        var jobId = submitted.RootElement.GetProperty("jobId").GetString()!;
        Assert.Contains("AND __dq_status = 'quarantined'", submitted.RootElement.GetProperty("dispositionStatement").GetString());
        await Expect(page.Locator(".dq-job-row")).ToContainTextAsync(jobId);
        await page.WaitForFunctionAsync("() => ['Completed', 'Failed', 'Cancelled', 'Unknown'].includes(document.querySelector('.dq-job-row .dq-status')?.textContent)");
        var observed = await page.Locator(".dq-job-row .dq-status").TextContentAsync();
        var serverStatus = await page.EvaluateAsync<string>("async id => { const { dataQualityApi } = await import('/js/api.js'); return (await dataQualityApi.jobStatus(id)).status; }", jobId);
        Assert.Equal(serverStatus, observed);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(entry => entry.Action == "READ_QUARANTINE_ROWS" && entry.ResourceId == target));
            var audit = await db.AuditLogs.SingleAsync(entry => entry.Action == "DATA_QUALITY_DISCARD" && entry.ResourceId == target);
            Assert.NotNull(audit.UserId);
            Assert.Contains("Confirmed duplicate source event", audit.Detail);
            Assert.Contains("row-a", audit.Detail);
        }
        await page.Locator("#dqRowsStatus").SelectOptionAsync("replaying");
        await Expect(page.Locator("[data-discard-row='row-b']")).ToHaveTextAsync("Mark replayed");
        await Expect(page.Locator("[data-edit-row='row-b'][data-edit-column='Email']")).ToBeDisabledAsync();
        var recovery = await page.RunAndWaitForResponseAsync(() => page.Locator("[data-discard-row='row-b']").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/api/data-quality/quarantine/disposition", StringComparison.Ordinal));
        Assert.Equal(202, recovery.Status);
        using var recovered = JsonDocument.Parse(await recovery.TextAsync());
        Assert.Contains("AND __dq_status = 'replaying'", recovered.RootElement.GetProperty("dispositionStatement").GetString());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task QualityQueue_UnknownAcknowledgementAndUnavailableTrendRequireExplicitRecovery()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var job = $"browser_quality_missing_{Guid.NewGuid():N}";
        var target = $"q_missing_{Guid.NewGuid():N}";
        var store = fixture.Factory.Services.GetRequiredService<IJobHistoryStore>();
        await store.SaveJobAsync(new JobDefinition(job, "missing_quality_script.etlsql", 1, "DAY", null, null, null));
        var savedJob = await store.GetJobAsync(null, job);
        Assert.NotNull(savedJob);
        await store.SetJobStateAsync(savedJob!.Id, $"dq:quarantine-manifest:{target}", JsonSerializer.Serialize(
            new QuarantineReplayManifest(job, null, "Missing script", "#source", target, true, null, ["Id"], "missing", DateTimeOffset.UtcNow)));
        await page.GotoWithDiagnosticsAsync($"/index.html?q={target}#governance/quarantine");
        await Expect(page.Locator($"[data-replay-target='{target}']")).ToBeVisibleAsync();
        await Expect(page.Locator($"[data-review-target='{target}']")).ToHaveCountAsync(0);
        await page.RouteAsync("**/api/data-quality/quarantine/replay", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"jobId":"unconfirmed","replayStatement":"REPLAY QUARANTINE q_missing;"}"""
        }));
        await page.Locator($"[data-replay-target='{target}']").ClickAsync();
        await Expect(page.Locator(".dq-page > .error-msg")).ToContainTextAsync("Replay outcome is unconfirmed.");
        await Expect(page.Locator($"[data-replay-target='{target}']")).ToBeDisabledAsync();
        await Expect(page.Locator(".dq-job-row")).ToHaveCountAsync(0);
        await page.UnrouteAsync("**/api/data-quality/quarantine/replay");
        await page.Locator("#dqRefreshBtn").ClickAsync();
        await Expect(page.Locator($"[data-replay-target='{target}']")).ToBeEnabledAsync();
        await page.RouteAsync("**/api/data-quality/trend?*", route => route.FulfillAsync(new()
        {
            Status = 503,
            ContentType = "application/json",
            Body = """{"error":"Trend store unavailable."}"""
        }));
        await page.Locator($"[data-trend-job='{job}']").ClickAsync();
        await Expect(page.Locator(".dq-page [role='dialog']")).ToContainTextAsync("Quality trend unavailable");
        await Expect(page.Locator(".dq-page [role='dialog']")).Not.ToContainTextAsync("No recorded runs");
        await page.UnrouteAsync("**/api/data-quality/trend?*");
        await page.Locator("#dqTrendClose").ClickAsync();
        await page.Locator($"[data-trend-job='{job}']").ClickAsync();
        await Expect(page.Locator(".dq-page [role='dialog']")).ToContainTextAsync("No recorded runs");
        Assert.Empty(session.PageErrors);
    }
}

public sealed class DataQualityQueueFixture : PortalBrowserFixture
{
    public DataQualityQueueFixture() : base(new DataQualityQueueFactory()) { }
}

internal sealed class DataQualityQueueFactory : PortalBrowserFactory
{
    protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
    {
        base.CustomizeConfiguration(settings);
        settings["Portal:DataQuality:AllowConnectionPreview"] = "true";
    }
    protected override void CustomizePortalConfig(ETL_SQL.Portal.PortalConfig config)
        => config.DataQuality.AllowConnectionPreview = true;
}

[CollectionDefinition(Name)]
public sealed class DataQualityQueueCollection : ICollectionFixture<DataQualityQueueFixture>
{
    public const string Name = "Portal data quality";
}
