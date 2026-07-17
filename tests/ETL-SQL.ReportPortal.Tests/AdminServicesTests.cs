using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public class AdminServicesTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FailureDigest_AlertOnly_SkipsWhenNoFailures_RecordsRunAndAudit()
    {
        using var factory = new AdminServicesFactory();
        var service = NewFailureDigest(factory);

        var run = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal("Skipped", run.Outcome);
        Assert.Empty(factory.Sender.Sent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Single(await db.AdminServiceRuns.Where(r => r.ServiceName == "failure-digest").ToListAsync());
        Assert.Contains(await db.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == "ADMIN_SERVICE_RUN");
    }

    [Fact]
    public async Task FailureDigest_SendsWhenFailuresExist()
    {
        using var factory = new AdminServicesFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.PortalExecutionJobs.Add(new PortalExecutionJob
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = "Report",
                Status = "Failed",
                CompletedAt = DateTime.UtcNow.AddMinutes(-5),
                Error = "boom: connection refused"
            });
            await db.SaveChangesAsync();
        }

        var run = await NewFailureDigest(factory).RunOnceAsync(CancellationToken.None);

        Assert.Equal("Sent", run.Outcome);
        var sent = Assert.Single(factory.Sender.Sent);
        Assert.Contains("1 failure(s)", sent.Subject);
        Assert.Contains("boom: connection refused", sent.Body);
        Assert.Equal("ops@example.com", sent.Recipients);
    }

    [Fact]
    public async Task FailureDigest_RetriesDeliveryAndRecordsFailure()
    {
        using var factory = new AdminServicesFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.PortalExecutionJobs.Add(new PortalExecutionJob
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = "Failed",
                CompletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Fails once, then succeeds → Sent with two attempts.
        factory.Sender.FailuresBeforeSuccess = 1;
        var run = await NewFailureDigest(factory).RunOnceAsync(CancellationToken.None);
        Assert.Equal("Sent", run.Outcome);
        Assert.Equal(2, run.Attempts);

        // Always fails → Failed after MaxAttempts.
        factory.Sender.FailuresBeforeSuccess = int.MaxValue;
        var failed = await NewFailureDigest(factory).RunOnceAsync(CancellationToken.None);
        Assert.Equal("Failed", failed.Outcome);
        Assert.Equal(2, failed.Attempts); // MaxAttempts = 2 in this fixture
        Assert.Contains("smtp down", failed.Detail);
    }

    [Fact]
    public async Task BackupReport_AlertsOnMissingFailedAndStale_SkipsWhenHealthy()
    {
        using var factory = new AdminServicesFactory();
        var jobHistory = factory.Services.GetRequiredService<IJobHistoryStore>();
        await jobHistory.InitializeAsync();
        var service = NewBackupReport(factory);

        // No outcome ever recorded → alert.
        var missing = await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal("Sent", missing.Outcome);
        Assert.Contains("ALERT", factory.Sender.Sent[^1].Subject);

        // Fresh successful backup → alert-only skips.
        await jobHistory.SetJobStateAsync("admin-backup", "last_backup_status", "success");
        await jobHistory.SetJobStateAsync("admin-backup", "last_backup_at", DateTime.UtcNow.ToString("o"));
        await jobHistory.SetJobStateAsync("admin-backup", "last_backup_exit_code", "0");
        var healthy = await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal("Skipped", healthy.Outcome);

        // Stale backup → alert.
        await jobHistory.SetJobStateAsync("admin-backup", "last_backup_at", DateTime.UtcNow.AddDays(-3).ToString("o"));
        var stale = await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal("Sent", stale.Outcome);
        Assert.Contains("STALE", factory.Sender.Sent[^1].Body);

        // Failed backup → alert.
        await jobHistory.SetJobStateAsync("admin-backup", "last_backup_status", "failed");
        await jobHistory.SetJobStateAsync("admin-backup", "last_backup_at", DateTime.UtcNow.ToString("o"));
        var failedBackup = await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal("Sent", failedBackup.Outcome);
        Assert.Contains("FAILED", factory.Sender.Sent[^1].Body);
    }

    [Fact]
    public async Task CapacityReport_AlwaysSends_AndPrunesOldHistory()
    {
        using var factory = new AdminServicesFactory();
        var jobHistory = factory.Services.GetRequiredService<IJobHistoryStore>();
        var hostMetrics = factory.Services.GetRequiredService<IHostMetricsStore>();
        await jobHistory.InitializeAsync();
        var mb = 1024 * 1024L;
        await hostMetrics.AppendHostMetricAsync(new HostMetricSample(
            "node-a",
            DateTime.UtcNow.AddMinutes(-10),
            MemoryLoadPercent: 40,
            ProcessCpuPercent: 10,
            HostCpuPercent: 20,
            StateDiskFreeBytes: 900 * mb,
            SpillDiskFreeBytes: 800 * mb));
        await hostMetrics.AppendHostMetricAsync(new HostMetricSample(
            "node-a",
            DateTime.UtcNow.AddMinutes(-5),
            MemoryLoadPercent: 80,
            ProcessCpuPercent: 30,
            HostCpuPercent: 60,
            StateDiskFreeBytes: 700 * mb,
            SpillDiskFreeBytes: 650 * mb));
        await hostMetrics.AppendHostMetricAsync(new HostMetricSample(
            "node-a",
            DateTime.UtcNow.Date.AddDays(-7),
            MemoryLoadPercent: 30,
            ProcessCpuPercent: 12,
            HostCpuPercent: 20,
            StateDiskFreeBytes: 900 * mb,
            SpillDiskFreeBytes: 800 * mb));
        var firstRun = await jobHistory.LogJobStartAsync("capacity-job");
        await jobHistory.LogJobEndAsync(firstRun, "SUCCESS", rowsProcessed: 10, peakMemoryBytes: 256 * mb, cpuTimeSeconds: 1.5);
        var failedRun = await jobHistory.LogJobStartAsync("capacity-job");
        await jobHistory.LogJobEndAsync(failedRun, "FAILURE", "boom", rowsProcessed: 0, peakMemoryBytes: 512 * mb, cpuTimeSeconds: 2.5);
        await jobHistory.RollUpJobHistoryAsync();
        await hostMetrics.RollUpHostMetricsAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var utcNow = DateTime.UtcNow;
            db.PortalExecutionJobs.AddRange(
                new PortalExecutionJob
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = "Report",
                    ReportId = 42,
                    UserId = 7,
                    Status = "Succeeded",
                    CreatedAt = utcNow.AddMinutes(-20),
                    StartedAt = utcNow.AddMinutes(-19),
                    CompletedAt = utcNow.AddMinutes(-18),
                    RowsProcessed = 100,
                    PeakMemoryBytes = 128 * mb,
                    CpuTimeSeconds = 0.5
                },
                new PortalExecutionJob
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = "DatasetRefresh",
                    ReportId = 42,
                    UserId = 7,
                    Status = "Failed",
                    CreatedAt = utcNow.AddMinutes(-15),
                    StartedAt = utcNow.AddMinutes(-14),
                    CompletedAt = utcNow.AddMinutes(-13),
                    RowsProcessed = 0,
                    PeakMemoryBytes = 64 * mb,
                    CpuTimeSeconds = 0.25
                });
            db.AdminServiceRuns.Add(new AdminServiceRun
            {
                ServiceName = "capacity-report",
                Outcome = "Sent",
                StartedAtUtc = utcNow.AddDays(-400)
            });
            await db.SaveChangesAsync();
        }

        var run = await NewCapacityReport(factory).RunOnceAsync(CancellationToken.None);

        Assert.Equal("Sent", run.Outcome);
        Assert.Contains("capacity report", factory.Sender.Sent[^1].Subject);
        Assert.Contains("memory max/p95", factory.Sender.Sent[^1].Body);
        Assert.Contains("CPU max/p95", factory.Sender.Sent[^1].Body);
        Assert.Contains("2 run(s), 1 non-success (50.0%)", factory.Sender.Sent[^1].Body);
        Assert.Contains("Execution p95:", factory.Sender.Sent[^1].Body);
        Assert.Contains("Scheduled job breakdown:", factory.Sender.Sent[^1].Body);
        Assert.Contains("capacity-job: runs 2, failures 1 (50.0%)", factory.Sender.Sent[^1].Body);
        Assert.Contains("Portal execution breakdown:", factory.Sender.Sent[^1].Body);
        Assert.Contains("Report|report:42|owner:user:7: runs 1", factory.Sender.Sent[^1].Body);
        Assert.Contains("DatasetRefresh|report:42|owner:user:7: runs 1, failures 1", factory.Sender.Sent[^1].Body);
        Assert.Contains("Portal queue diagnosis:", factory.Sender.Sent[^1].Body);
        Assert.Contains("p95 queue wait", factory.Sender.Sent[^1].Body);
        Assert.Contains("execution slots are likely saturated", factory.Sender.Sent[^1].Body);
        Assert.Contains("Portal hourly pressure - last 30 days:", factory.Sender.Sent[^1].Body);
        Assert.Contains("busiest queued hour", factory.Sender.Sent[^1].Body);
        Assert.Contains("active-slot cap reached", factory.Sender.Sent[^1].Body);
        Assert.Contains("Historical planning trend - last 30 days", factory.Sender.Sent[^1].Body);
        Assert.Contains("saturation indicators:", factory.Sender.Sent[^1].Body);
        Assert.Contains("watch memory, state storage, spill storage", factory.Sender.Sent[^1].Body);
        Assert.Contains("disk forecast:", factory.Sender.Sent[^1].Body);
        Assert.Contains("bottleneck guide:", factory.Sender.Sent[^1].Body);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var rows = await db.AdminServiceRuns.Where(r => r.ServiceName == "capacity-report").ToListAsync();
            var row = Assert.Single(rows); // the 400-day-old row was pruned
            Assert.Equal("Sent", row.Outcome);
        }
    }

    [Fact]
    public async Task AdminServiceRun_EmitsLowCardinalityBackgroundTelemetry()
    {
        var stoppedActivities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BackgroundServiceObservability.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stoppedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BackgroundServiceObservability.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        meterListener.Start();

        using var factory = new AdminServicesFactory();
        var run = await NewCapacityReport(factory).RunOnceAsync(CancellationToken.None);

        Assert.Equal("Sent", run.Outcome);
        Assert.Contains(stoppedActivities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.Component) == "portal"
            && Tag(activity, ObservabilityConventions.Tags.WorkloadKind) == "background"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "capacity-report"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == "admin_digest_run"
            && Tag(activity, ObservabilityConventions.Tags.Status) == "sent");
        Assert.Contains(measurements, measurement => measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Component, "portal")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.WorkloadKind, "background")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "capacity-report")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "sent"));
        Assert.DoesNotContain(measurements, measurement => measurement.Tags.Any(tag =>
            tag.Value is string value
            && (value.Contains("ops@example.com", StringComparison.OrdinalIgnoreCase)
                || value.Contains("mailer", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public async Task StatusApi_ReportsServicesAndHistory()
    {
        using var factory = new AdminServicesFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/services")).StatusCode);

        await NewCapacityReport(factory).RunOnceAsync(CancellationToken.None);

        var token = await GetAdminTokenAsync(client);
        var response = await SendAsync(client, HttpMethod.Get, token, "/api/admin/services");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonArray>(Json);
        Assert.Equal(3, body!.Count);
        var capacity = body.Single(n => n!["name"]!.GetValue<string>() == "capacity-report");
        Assert.True(capacity!["enabled"]!.GetValue<bool>());
        Assert.Equal("Sent", capacity["lastRun"]!["outcome"]!.GetValue<string>());

        var history = await SendAsync(client, HttpMethod.Get, token, "/api/admin/services/capacity-report/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Single((await history.Content.ReadFromJsonAsync<JsonArray>(Json))!);
    }

    // ── fixtures ────────────────────────────────────────────────────────────────

    private static FailureDigestAdminService NewFailureDigest(AdminServicesFactory factory) => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        factory.Services.GetRequiredService<PortalConfig>(),
        factory.Services.GetRequiredService<IClusterLockStore>(),
        NullLogger<FailureDigestAdminService>.Instance);

    private static BackupReportAdminService NewBackupReport(AdminServicesFactory factory) => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        factory.Services.GetRequiredService<PortalConfig>(),
        factory.Services.GetRequiredService<IClusterLockStore>(),
        NullLogger<BackupReportAdminService>.Instance);

    private static CapacityReportAdminService NewCapacityReport(AdminServicesFactory factory) => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        factory.Services.GetRequiredService<PortalConfig>(),
        factory.Services.GetRequiredService<IClusterLockStore>(),
        NullLogger<CapacityReportAdminService>.Instance);

    private sealed class AdminServicesFactory : PortalWebFactory
    {
        public FakeSender Sender { get; } = new();

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            foreach (var schedule in new AdminServiceScheduleConfig[]
            {
                config.AdminServices.FailureDigest,
                config.AdminServices.BackupReport,
                config.AdminServices.CapacityReport
            })
            {
                schedule.Enabled = true;
                schedule.Recipients = "ops@example.com";
                schedule.SmtpAlias = "mailer";
                schedule.MaxAttempts = 2;
                schedule.RetryDelaySeconds = 0;
            }
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IAdminNotificationSender>();
            services.AddSingleton<IAdminNotificationSender>(Sender);
        }
    }

    private sealed class FakeSender : IAdminNotificationSender
    {
        public List<AdminNotification> Sent { get; } = new();
        public int FailuresBeforeSuccess { get; set; }

        public Task<(bool Success, string? Error)> SendAsync(AdminNotification notification, CancellationToken ct)
        {
            if (FailuresBeforeSuccess > 0)
            {
                FailuresBeforeSuccess--;
                return Task.FromResult<(bool, string?)>((false, "smtp down"));
            }

            Sent.Add(notification);
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    private static string? Tag(Activity activity, string key)
    {
        var value = activity.TagObjects.FirstOrDefault(t => t.Key == key).Value;
        return value?.ToString();
    }

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>();
        foreach (var tag in tags)
            result[tag.Key] = tag.Value;
        return result;
    }

    private static bool HasTag(Dictionary<string, object?> tags, string key, object value) =>
        tags.TryGetValue(key, out var actual) && Equals(actual, value);

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await SendAsync(client, HttpMethod.Post, initial, "/api/auth/change-password",
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        return body!["token"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        string token, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
