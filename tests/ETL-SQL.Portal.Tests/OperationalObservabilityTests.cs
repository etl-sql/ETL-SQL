using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P2.8 operational observability: the operational metrics snapshot (active/queued executions,
/// recent failure rates, storage usage) and the portal-wide log-hygiene guarantee that credentials
/// are sanitized out of log output.
/// </summary>
[Trait("Category", "Portal")]
public sealed class OperationalObservabilityTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ops_metrics_" + Guid.NewGuid().ToString("N")[..8]);

    public OperationalObservabilityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task Metrics_ReportExecutionsQueueFailuresAndStorage()
    {
        var datasetDir = Path.Combine(_root, "datasets");
        var snapshotDir = Path.Combine(_root, "snapshots");
        Directory.CreateDirectory(datasetDir);
        Directory.CreateDirectory(snapshotDir);
        await File.WriteAllBytesAsync(Path.Combine(datasetDir, "ds.parquet"), new byte[1024]);
        await File.WriteAllBytesAsync(Path.Combine(snapshotDir, "snap.json"), new byte[256]);

        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}")
            .Options;
        await using var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = new PortalConfig
        {
            DatasetRootPath = datasetDir,
            SnapshotDirectory = snapshotDir,
            Resources = new ResourcesConfig { MaxConcurrentReportExecutions = 4, MaxConcurrentExecutionsPerUser = 2 }
        };
        config.OperationalDigest.QueueDepthAlertThreshold = 2;
        config.OperationalDigest.SnapshotFreshnessHours = 24;
        config.OperationalDigest.DatasetFreshnessHours = 24;
        config.OperationalDigest.PolicyVersionExpiryWarningHours = 72;

        // A user + folder + report so the seeded subscriptions satisfy their foreign keys.
        var owner = new PortalUser { UserName = "ops_owner", Email = "ops@test.local", IsActive = true };
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var folder = new Folder { Name = "ops", Path = "/ops", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        var report = new Report
        {
            FolderId = folder.Id,
            Name = "Ops Report",
            ScriptPath = "ops.rptsql",
            CreatedBy = owner.Id,
            LastRefreshCompletedAt = now.AddHours(-30)
        };
        var freshReport = new Report
        {
            FolderId = folder.Id,
            Name = "Fresh Ops Report",
            ScriptPath = "fresh.rptsql",
            CreatedBy = owner.Id,
            LastRefreshCompletedAt = now.AddHours(-2)
        };
        db.Reports.AddRange(report, freshReport);
        await db.SaveChangesAsync();
        db.Datasets.AddRange(
            new Dataset
            {
                Name = "stale-dataset",
                FolderPath = "/ops",
                ParquetFilePath = Path.Combine(datasetDir, "stale.parquet"),
                SourceQuery = "SELECT 1",
                LastRefresh = now.AddHours(-30)
            },
            new Dataset
            {
                Name = "fresh-dataset",
                FolderPath = "/ops",
                ParquetFilePath = Path.Combine(datasetDir, "fresh.parquet"),
                SourceQuery = "SELECT 1",
                LastRefresh = now.AddHours(-2)
            });
        await db.SaveChangesAsync();

        PortalExecutionJob Job(string status, bool completed) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ReportId = report.Id,
            UserId = owner.Id,
            Status = status,
            CreatedAt = completed ? now.AddMinutes(-10) : now.AddMinutes(-5),
            StartedAt = completed ? now.AddMinutes(-2) : null,
            CompletedAt = completed ? now : null,
            RowsProcessed = completed ? 100 : 0,
            PeakMemoryBytes = completed ? 1024 : 0
        };
        db.PortalExecutionJobs.AddRange(
            Job("Running", false),
            Job("Pending", false),
            Job("Pending", false),
            Job("Failed", true),
            Job("Cancelled", true),
            Job("Completed", true));

        int trig = 0;
        SubscriptionDelivery Delivery(string outcome) => new()
        {
            DeliveryId = $"delivery-{Guid.NewGuid():N}",
            SubscriptionId = 1,
            TriggerKey = $"t{trig++}",
            Outcome = outcome,
            CompletedAt = now
        };
        db.SubscriptionDeliveries.AddRange(
            Delivery("Delivered"), Delivery("Delivered"), Delivery("Failed"), Delivery("Denied"));

        db.Subscriptions.AddRange(
            new Subscription { ReportId = report.Id, UserId = owner.Id, SmtpAlias = "a", Recipients = "r", IsActive = true },
            new Subscription { ReportId = report.Id, UserId = owner.Id, SmtpAlias = "a", Recipients = "r", IsActive = true },
            new Subscription { ReportId = report.Id, UserId = owner.Id, SmtpAlias = "a", Recipients = "r", IsActive = false });
        db.SmtpConnections.Add(new SmtpConnection { Alias = "a", Host = "h", Port = 25 });
        db.AuditOutboxMessages.AddRange(
            new AuditOutboxMessage
            {
                Status = "Pending",
                Action = "TEST_PENDING",
                PayloadJson = "{\"event\":\"pending\"}",
                CreatedAt = now.AddMinutes(-15),
                UpdatedAt = now.AddMinutes(-15)
            },
            new AuditOutboxMessage
            {
                Status = "Failed",
                Action = "TEST_FAILED",
                PayloadJson = "{\"event\":\"failed\"}",
                CreatedAt = now.AddMinutes(-10),
                UpdatedAt = now.AddMinutes(-10)
            });
        db.PolicyVersions.AddRange(
            new PolicyVersionEntity
            {
                Tenant = "acme",
                Environment = "prod",
                PolicyVersion = "expired",
                PolicyHash = "hash-expired",
                IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                Author = "ops",
                RolloutState = "Active"
            },
            new PolicyVersionEntity
            {
                Tenant = "contoso",
                Environment = "prod",
                PolicyVersion = "expiring",
                PolicyHash = "hash-expiring",
                IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(4),
                Author = "ops",
                RolloutState = "Active"
            },
            new PolicyVersionEntity
            {
                Tenant = "fabrikam",
                Environment = "prod",
                PolicyVersion = "healthy",
                PolicyHash = "hash-healthy",
                IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(14),
                Author = "ops",
                RolloutState = "Active"
            });
        await db.SaveChangesAsync();

        var node = new PortalNodeIdentity();
        var m = await new OperationalMetricsService(db, config, node).GetAsync();

        Assert.Equal(1, m.ActiveExecutions);
        Assert.Equal(2, m.QueuedExecutions);
        Assert.Equal(4, m.ExecutionCap);
        Assert.Equal(2, m.PerUserExecutionCap);
        Assert.Equal(3, m.RecentExecutions);          // Failed + Cancelled + Completed
        Assert.Equal(2, m.RecentExecutionFailures);   // Failed + Cancelled
        Assert.True(m.AverageExecutionDurationMs > 0);
        Assert.True(m.AverageQueuedExecutionAgeSeconds > 0);
        Assert.NotEmpty(m.HourlyExecutionLoad);
        var currentHour = m.HourlyExecutionLoad.Single(b =>
            b.HourUtc == new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc));
        Assert.Equal(3, currentHour.Executions);
        Assert.Equal(2, currentHour.Failures);
        Assert.Equal(300, currentHour.RowsProcessed);
        Assert.Equal(1024, currentHour.PeakMemoryBytes);
        Assert.Equal(4, m.RecentDeliveries);
        Assert.Equal(2, m.RecentDeliveryFailures);    // Failed + Denied
        Assert.Equal(1024, m.DatasetStorageBytes);
        Assert.Equal(256, m.SnapshotStorageBytes);
        Assert.False(m.StorageUsageSampleStale);
        Assert.Null(m.StorageUsageLastSuccessfulSampleUtc);
        Assert.Null(m.StorageUsageLastFailureUtc);
        Assert.Equal(1, m.StaleSnapshots);
        Assert.Equal(1, m.StaleDatasets);
        Assert.Equal(1, m.ActivePolicyVersionsExpiring);
        Assert.Equal(1, m.ActivePolicyVersionsExpired);
        Assert.Equal(2, m.ActiveSubscriptions);
        Assert.Equal(1, m.SmtpConnections);
        Assert.Equal(1, m.AuditOutboxPending);
        Assert.Equal(1, m.AuditOutboxFailed);
        Assert.True(m.AuditOutboxPendingBytes > 0);
        Assert.True(m.AuditOutboxOldestPendingAgeSeconds > 0);
        Assert.True(m.SecurityEventPending >= 0);
        Assert.True(m.SecurityEventStoredBytes >= 0);
        Assert.True(m.DatabaseConnectivityHealthy);
        Assert.False(m.DatabasePoolExhaustionSuspected);
        Assert.True(m.PolicyAuthorityHealthy);
        Assert.Equal(0, m.UnhealthyFleetNodes);
        Assert.Equal("Standalone", m.Topology);
        Assert.Equal(node.NodeId, m.NodeId);
        Assert.Equal("ETLSQL_PORTAL_AFFINITY", m.AffinityCookieName);

        var prometheus = await new PortalPrometheusMetricsExporter(
            new OperationalMetricsService(db, config, node),
            config).ExportAsync();
        Assert.Contains("# TYPE etlsql_portal_execution_active gauge", prometheus);
        Assert.Contains("etlsql_portal_execution_queued", prometheus);
        Assert.Contains("etlsql_portal_dataset_storage_bytes", prometheus);
        Assert.Contains("etlsql_portal_storage_usage_sample_stale", prometheus);
        Assert.Contains("etlsql_portal_storage_usage_last_success_age_seconds", prometheus);
        Assert.Contains("etlsql_portal_storage_usage_last_failure_age_seconds", prometheus);
        Assert.Contains("etlsql_portal_stale_snapshots", prometheus);
        Assert.Contains("etlsql_portal_stale_datasets", prometheus);
        Assert.Contains("etlsql_portal_policy_versions_expiring", prometheus);
        Assert.Contains("etlsql_portal_policy_versions_expired", prometheus);
        Assert.Contains("etlsql_portal_policy_authority_healthy", prometheus);
        Assert.Contains("etlsql_portal_client_certificate_expires_in_seconds", prometheus);
        Assert.Contains("etlsql_portal_database_connectivity_healthy", prometheus);
        Assert.Contains("etlsql_portal_database_pool_exhaustion_suspected", prometheus);
        Assert.Contains("etlsql_portal_fleet_nodes_unhealthy", prometheus);
        Assert.Contains("etlsql_portal_audit_outbox_pending", prometheus);
        Assert.Contains("etlsql_security_event_pending", prometheus);
        Assert.Contains("etlsql_portal_operational_alert_active", prometheus);
        Assert.Contains("alert_code=\"portal_execution_queue_depth\"", prometheus);
        Assert.Contains("alert_code=\"portal_stale_snapshots\"", prometheus);
        Assert.Contains("alert_code=\"portal_stale_datasets\"", prometheus);
        Assert.Contains("alert_code=\"portal_policy_version_expiring\"", prometheus);
        Assert.Contains("alert_code=\"portal_policy_version_expired\"", prometheus);
        Assert.Contains("runbook=\"docs/architecture/decisions/Alerting_Service_Objectives.md#portal-execution-queue-depth\"", prometheus);
        Assert.Contains($"node=\"{node.NodeId}\"", prometheus);
        Assert.DoesNotContain(datasetDir, prometheus);
        Assert.DoesNotContain(snapshotDir, prometheus);
    }

    [Fact]
    public async Task StorageUsageSampler_RetainsLastSuccessfulValuesAfterBoundedFailure()
    {
        var datasetDir = Path.Combine(_root, "sample-datasets");
        var snapshotDir = Path.Combine(_root, "sample-snapshots");
        Directory.CreateDirectory(datasetDir);
        Directory.CreateDirectory(snapshotDir);
        await File.WriteAllBytesAsync(Path.Combine(datasetDir, "a.bin"), new byte[128]);
        await File.WriteAllBytesAsync(Path.Combine(datasetDir, "b.bin"), new byte[256]);
        await File.WriteAllBytesAsync(Path.Combine(snapshotDir, "snapshot.bin"), new byte[64]);

        var config = new PortalConfig
        {
            DatasetRootPath = datasetDir,
            SnapshotDirectory = snapshotDir,
            Resources = new ResourcesConfig
            {
                StorageUsageSampleIntervalSeconds = 30,
                StorageUsageSampleTimeoutSeconds = 5,
                StorageUsageSampleMaxFiles = 10
            }
        };
        var sampler = new PortalStorageUsageSampler(config, NullLogger<PortalStorageUsageSampler>.Instance);

        await sampler.SampleOnceAsync(CancellationToken.None);

        Assert.Equal(384, sampler.DatasetStorageBytes);
        Assert.Equal(64, sampler.SnapshotStorageBytes);
        Assert.NotNull(sampler.LastSuccessfulSampleUtc);
        Assert.Null(sampler.LastFailureUtc);
        Assert.False(sampler.IsStale);

        config.Resources.StorageUsageSampleMaxFiles = 1;
        await sampler.SampleOnceAsync(CancellationToken.None);

        Assert.Equal(384, sampler.DatasetStorageBytes);
        Assert.Equal(64, sampler.SnapshotStorageBytes);
        Assert.NotNull(sampler.LastFailureUtc);
        Assert.Contains("file limit", sampler.LastFailureMessage);
    }

    [Fact]
    public async Task StorageUsageMeasurement_HonorsFileLimitAndCancellation()
    {
        var dir = Path.Combine(_root, "measurement");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "one.bin"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(dir, "two.bin"), new byte[20]);

        var complete = PortalStorageUsageSampler.MeasureDirectoryDetailed(dir, maxFiles: 10, CancellationToken.None);
        Assert.True(complete.IsComplete);
        Assert.Equal(30, complete.Bytes);
        Assert.Equal(2, complete.FilesVisited);
        Assert.Null(complete.Error);

        var bounded = PortalStorageUsageSampler.MeasureDirectoryDetailed(dir, maxFiles: 1, CancellationToken.None);
        Assert.False(bounded.IsComplete);
        Assert.Equal(1, bounded.FilesVisited);
        Assert.Contains("file limit", bounded.Error);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            PortalStorageUsageSampler.MeasureDirectoryDetailed(dir, maxFiles: 10, cts.Token));
    }

    [Fact]
    public async Task MetricsEndpoint_RequiresAdmin()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Admin can read the snapshot.
        var adminReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/metrics/operational");
        adminReq.Headers.Authorization = new("Bearer", adminToken);
        var adminResp = await client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.OK, adminResp.StatusCode);
        var body = await adminResp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(body!.ContainsKey("activeExecutions"));
        Assert.True(body.ContainsKey("datasetStorageBytes"));
        Assert.True(body.ContainsKey("hourlyExecutionLoad"));
        Assert.True(body.ContainsKey("averageQueuedExecutionAgeSeconds"));
        Assert.True(body.ContainsKey("auditOutboxPending"));
        Assert.True(body.ContainsKey("securityEventPending"));

        // A non-admin is rejected.
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = JsonContent.Create(new
            {
                username = $"viewer_{suffix}",
                email = $"viewer_{suffix}@test.local",
                password = "Initial@Test1!",
                role = "Viewer"
            })
        };
        create.Headers.Authorization = new("Bearer", adminToken);
        (await client.SendAsync(create)).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = $"viewer_{suffix}", password = "Initial@Test1!" });
        var viewerToken = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();

        var viewerReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/metrics/operational");
        viewerReq.Headers.Authorization = new("Bearer", viewerToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(viewerReq)).StatusCode);
    }

    [Fact]
    public async Task PrometheusMetricsEndpoint_AllowsReadOnlyScrape()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("# HELP etlsql_portal_execution_active", body);
        Assert.Contains("# HELP etlsql_portal_audit_outbox_pending", body);
        Assert.Contains("etlsql_portal_database_reachable", body);
        Assert.Contains("# HELP etlsql_security_event_pending", body);
        Assert.Contains("# HELP etlsql_runtime_process_working_set_bytes", body);
        Assert.Contains("etlsql_runtime_gc_heap_bytes", body);
        Assert.Contains("etlsql_runtime_gc_collections_gen2_total", body);
        Assert.Contains("component=\"portal\"", body);
        Assert.DoesNotContain("Portal__Jwt__Secret", body);
        Assert.DoesNotContain("ConnectionString", body);
    }

    [Fact]
    public async Task PrometheusMetricsEndpoint_IsRateLimitedPerClient()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();

        for (var request = 0; request < 12; request++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/metrics")).StatusCode);

        var rejected = await client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
    }

    [Fact]
    public void PortalObservability_EmitsLowCardinalityExecutionMetrics()
    {
        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PortalObservability.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, ToDictionary(tags))));
        listener.Start();

        var job = new ExecutionJob("job-metrics", ReportId: 42, UserId: 7)
        {
            Status = JobStatus.Completed,
            StartedAt = DateTime.UtcNow.AddMilliseconds(-250),
            CompletedAt = DateTime.UtcNow,
            RowsProcessed = 123,
            PeakMemoryBytes = 456,
            CpuTimeSeconds = 0.25
        };

        PortalObservability.CompleteExecutionJobActivity(null, job, workloadKind: "Interactive");

        Assert.Contains(measurements, m => m.Name == "etlsql.portal.execution.completed"
            && HasTag(m.Tags, PortalObservability.Tags.Node, Environment.MachineName)
            && HasTag(m.Tags, PortalObservability.Tags.Component, "portal")
            && HasTag(m.Tags, PortalObservability.Tags.Status, "Completed")
            && HasTag(m.Tags, PortalObservability.Tags.WorkloadKind, "Interactive"));
        Assert.Contains(measurements, m => m.Name == "etlsql.portal.execution.rows_processed" && m.Value == 123);
        Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(PortalObservability.Tags.JobId));
        Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(PortalObservability.Tags.ReportId));
        Assert.DoesNotContain(measurements, m => m.Tags.ContainsKey(PortalObservability.Tags.UserId));
    }

    /// <summary>
    /// Portal-wide log hygiene: even when a downstream error echoes the SMTP credential, the
    /// delivery executor sanitizes it before it reaches the log, the persisted failure detail, or
    /// the audit record.
    /// </summary>
    [Fact]
    public async Task DeliveryFailure_SanitizesSmtpCredentialFromLogsAndAudit()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        var protector = scope.ServiceProvider.GetRequiredService<SmtpPasswordProtector>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string smtpPassword = "Sup3r-Secret-SMTP-Pw!";

        var owner = new PortalUser { UserName = $"o_{suffix}", Email = $"o_{suffix}@test.local", IsActive = true };
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var folder = new Folder { Name = $"f_{suffix}", Path = $"/f_{suffix}", OwnerId = owner.Id };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var scriptPath = Path.Combine(config.ScriptRootPath, $"log_{suffix}.rptsql");
        await File.WriteAllTextAsync(scriptPath, "SELECT 1 AS V INTO #d;");
        var report = new Report { FolderId = folder.Id, Name = $"R {suffix}", ScriptPath = scriptPath, CreatedBy = owner.Id };
        db.Reports.Add(report);
        SmtpCatalogSeed.Add(db, $"smtp_{suffix}", defaultFrom: "p@test.local");
        await db.SaveChangesAsync();
        var sub = new Subscription
        {
            ReportId = report.Id,
            UserId = owner.Id,
            Format = SubscriptionFormat.CSV,
            SmtpAlias = $"smtp_{suffix}",
            Recipients = "r@test.local",
            IsActive = true
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var capturing = new CapturingLogger<SubscriptionDeliveryService>();
        // A runner whose error text echoes the SMTP password — the worst case for a credential leak.
        var runner = new EchoingFailureRunner($"SMTP AUTH rejected for password {smtpPassword}");
        var service = new SubscriptionDeliveryService(
            db, config, new PortalConnectionCatalogService(db), new FolderPermissionService(db),
            new AuditService(db, new HttpContextAccessor()), runner, capturing);

        var result = await service.DeliverAsync(sub.Id, "trigger-log");
        Assert.Equal(SubscriptionDeliveryOutcome.Failed, result.Outcome);

        // The credential must not appear anywhere observable.
        Assert.DoesNotContain(capturing.Messages, msg => msg.Contains(smtpPassword));
        Assert.DoesNotContain(smtpPassword, result.Reason);
        var ledger = await db.SubscriptionDeliveries.SingleAsync(d => d.SubscriptionId == sub.Id);
        Assert.DoesNotContain(smtpPassword, ledger.Detail);
        var audit = await db.AuditLogs.Where(a => a.Action == "SUBSCRIPTION_DELIVERY_FAILED").ToListAsync();
        Assert.DoesNotContain(audit, a => a.Detail != null && a.Detail.Contains(smtpPassword));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private sealed class EchoingFailureRunner(string error) : ISubscriptionScriptRunner
    {
        public Task<(bool Success, string? Error)> RunAsync(string scriptText, string sessionId, CancellationToken ct,
            ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null)
            => Task.FromResult<(bool, string?)>((false, error));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
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
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })
        };
        change.Headers.Authorization = new("Bearer", token);
        (await client.SendAsync(change)).EnsureSuccessStatusCode();
        var relogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Tests99!" });
        return (await relogin.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }
}

