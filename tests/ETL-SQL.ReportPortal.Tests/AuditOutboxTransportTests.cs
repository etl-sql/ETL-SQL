using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using ETL_SQL.Core.Observability;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public sealed class AuditOutboxTransportTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "audit_transport_" + Guid.NewGuid().ToString("N")[..8]);

    public AuditOutboxTransportTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    [Fact]
    public async Task DrainOnceAsync_PostsPendingBatch_AndMarksRowsDelivered()
    {
        using var telemetry = new BackgroundTelemetryCapture();
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (provider, config) = await CreateProviderAsync("delivered.db", handler);
        await SeedAuditAsync(provider);

        var service = NewService(provider, config, handler);
        var processed = await service.DrainOnceAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var outbox = await db.AuditOutboxMessages.SingleAsync();

        Assert.Equal(1, processed);
        Assert.Equal("Delivered", outbox.Status);
        Assert.NotNull(outbox.DeliveredAt);
        Assert.Contains(outbox.EventId, handler.LastBody);
        Assert.Contains("CREATE_USER", handler.LastBody);
        Assert.Contains(telemetry.Activities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "audit-outbox-transport"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == "drain"
            && Tag(activity, ObservabilityConventions.Tags.Status) == "delivered"
            && Tag(activity, ObservabilityConventions.Tags.RowsProcessed) == "1");
        Assert.Contains(telemetry.Measurements, measurement =>
            measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "audit-outbox-transport")
            && HasTag(measurement.Tags, BackgroundServiceObservability.OperationTag, "drain")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "delivered"));
        Assert.DoesNotContain(telemetry.Measurements, measurement => measurement.Tags.Any(tag =>
            tag.Value is string value
            && (value.Contains("CREATE_USER", StringComparison.OrdinalIgnoreCase)
                || value.Contains("created", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public async Task DrainOnceAsync_RetriesWithBackoff_ThenMarksFailedAtAttemptLimit()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var (provider, config) = await CreateProviderAsync("failed.db", handler);
        config.Audit.TransportMaxAttempts = 2;
        await SeedAuditAsync(provider);

        var service = NewService(provider, config, handler);
        await service.DrainOnceAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var outbox = await db.AuditOutboxMessages.SingleAsync();
            Assert.Equal("Pending", outbox.Status);
            Assert.Equal(1, outbox.Attempts);
            Assert.NotNull(outbox.NextAttemptAt);
            outbox.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        await service.DrainOnceAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var outbox = await db.AuditOutboxMessages.SingleAsync();
            Assert.Equal("Failed", outbox.Status);
            Assert.Equal(2, outbox.Attempts);
            Assert.Null(outbox.NextAttemptAt);
            Assert.Contains("503", outbox.LastError);
        }
    }

    [Fact]
    public async Task DrainOnceAsync_SecondNodeSkipsRowsClaimedByFirstNode()
    {
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        var handler = new BlockingHandler(async request =>
        {
            Interlocked.Increment(ref sends);
            sendStarted.SetResult();
            await releaseSend.Task;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var (provider, config) = await CreateProviderAsync("claim.db", handler);
        config.Audit.TransportLockSeconds = 60;
        await SeedAuditAsync(provider);

        var first = NewService(provider, config, handler).DrainOnceAsync();
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondProcessed = await NewService(provider, config, handler).DrainOnceAsync();
        releaseSend.SetResult();
        var firstProcessed = await first;

        Assert.Equal(1, firstProcessed);
        Assert.Equal(0, secondProcessed);
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task DrainOnceAsync_RejectsNonHttpsEndpoint()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (provider, config) = await CreateProviderAsync("http.db", handler);
        config.Audit.TransportEndpoint = "http://audit.example.test/events";
        await SeedAuditAsync(provider);

        var service = NewService(provider, config, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DrainOnceAsync());
    }

    // ── P2.1 retention + disk-size safeguards ──────────────────────────────────────

    [Fact]
    public async Task PruneAsync_PurgesDeliveredRowsPastRetentionWindow()
    {
        using var telemetry = new BackgroundTelemetryCapture();
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (provider, config) = await CreateProviderAsync("retention.db", handler);
        config.Audit.OutboxDeliveredRetentionMinutes = 60;

        var now = DateTime.UtcNow;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.AuditOutboxMessages.Add(NewRow("Delivered", deliveredAt: now.AddHours(-2)));   // stale → purged
            db.AuditOutboxMessages.Add(NewRow("Delivered", deliveredAt: now.AddMinutes(-5))); // recent → kept
            db.AuditOutboxMessages.Add(NewRow("Pending"));                                    // pending → kept
            await db.SaveChangesAsync();
        }

        var service = NewService(provider, config, handler);
        var removed = await service.PruneAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal(1, removed);
            Assert.Equal(0, await db.AuditOutboxMessages.CountAsync(x => x.Status == "Delivered" && x.DeliveredAt < now.AddHours(-1)));
            Assert.Equal(2, await db.AuditOutboxMessages.CountAsync());
        }
        Assert.Contains(telemetry.Activities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "audit-outbox-transport"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == "prune"
            && Tag(activity, ObservabilityConventions.Tags.Status) == "success"
            && Tag(activity, ObservabilityConventions.Tags.RowsProcessed) == "1");
        Assert.Contains(telemetry.Measurements, measurement =>
            measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "audit-outbox-transport")
            && HasTag(measurement.Tags, BackgroundServiceObservability.OperationTag, "prune")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "success"));
    }

    [Fact]
    public async Task PruneAsync_ShedsOldestRowsOverSizeCap_WhenDeliveryNotRequired()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (provider, config) = await CreateProviderAsync("shed.db", handler);
        config.Audit.RequireRemoteDelivery = false;
        var payload = new string('x', 1000);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            for (var i = 0; i < 10; i++)
                db.AuditOutboxMessages.Add(NewRow("Pending", payload: payload));
            await db.SaveChangesAsync();
        }
        config.Audit.OutboxMaxBytes = 5000; // ~10kB queued → must shed below 5kB

        var service = NewService(provider, config, handler);
        var removed = await service.PruneAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var remainingBytes = await db.AuditOutboxMessages.SumAsync(x => (long)x.PayloadJson.Length);
            Assert.True(removed > 0);
            Assert.True(remainingBytes < config.Audit.OutboxMaxBytes,
                $"expected queue below {config.Audit.OutboxMaxBytes} bytes, got {remainingBytes}");
        }
    }

    [Fact]
    public async Task PruneAsync_NeverShedsRows_WhenRemoteDeliveryRequired()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (provider, config) = await CreateProviderAsync("noshed.db", handler);
        config.Audit.RequireRemoteDelivery = true;
        config.Audit.OutboxMaxBytes = 100; // far below queued payload
        var payload = new string('y', 500);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.AuditOutboxMessages.Add(NewRow("Pending", payload: payload));
            db.AuditOutboxMessages.Add(NewRow("Pending", payload: payload));
            await db.SaveChangesAsync();
        }

        var service = NewService(provider, config, handler);
        var removed = await service.PruneAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal(0, removed);
            Assert.Equal(2, await db.AuditOutboxMessages.CountAsync()); // mandatory delivery: nothing dropped
        }
    }

    // ── P2.2 duplicate audit delivery (collector-side dedup key is stable) ──────────

    [Fact]
    public async Task Redelivery_AfterUncommittedMark_SendsSameEventId_ForCollectorDedup()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (provider, config) = await CreateProviderAsync("dedup.db", handler);
        await SeedAuditAsync(provider);

        var service = NewService(provider, config, handler);
        await service.DrainOnceAsync();
        var firstBody = handler.LastBody;

        // Simulate a crash that delivered the batch but lost the "Delivered" commit: the row is
        // still Pending, so the next sweep resends it. The collector must be able to dedup it.
        string eventId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var row = await db.AuditOutboxMessages.SingleAsync();
            eventId = row.EventId;
            row.Status = "Pending";
            row.DeliveredAt = null;
            row.LockedUntil = null;
            await db.SaveChangesAsync();
        }

        await service.DrainOnceAsync();
        var secondBody = handler.LastBody;

        Assert.Contains(eventId, firstBody);
        Assert.Contains(eventId, secondBody);
    }

    private static AuditOutboxMessage NewRow(string status, DateTime? deliveredAt = null, string? payload = null) =>
        new()
        {
            Action = "CREATE_USER",
            ResourceType = "User",
            ResourceId = "42",
            PayloadJson = payload is null ? "{\"action\":\"CREATE_USER\"}" : $"{{\"p\":\"{payload}\"}}",
            Status = status,
            DeliveredAt = deliveredAt,
            OccurredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private async Task<(ServiceProvider Provider, PortalConfig Config)> CreateProviderAsync(
        string dbName,
        HttpMessageHandler handler)
    {
        var config = new PortalConfig
        {
            Audit =
            {
                TransportEndpoint = "https://audit.example.test/events",
                TransportBatchSize = 10,
                TransportIntervalSeconds = 1,
                TransportTimeoutSeconds = 5,
                TransportMaxAttempts = 3
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddDbContext<PortalDbContext>(options =>
            options.UseSqlite($"Data Source={Path.Combine(_scratch, dbName)}"));
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Database.MigrateAsync();
        return (provider, config);
    }

    private static async Task SeedAuditAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = new AuditService(db, new HttpContextAccessor());
        await audit.LogAsync(42, "CREATE_USER", "User", "42", "created");
    }

    private static AuditOutboxTransportService NewService(
        ServiceProvider provider,
        PortalConfig config,
        HttpMessageHandler handler) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) },
            TimeProvider.System,
            NullLogger<AuditOutboxTransportService>.Instance);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private sealed class BlockingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            await responder(request);
    }

    private sealed class BackgroundTelemetryCapture : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;

        public List<Activity> Activities { get; } = new();
        public List<(string Name, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = new();

        public BackgroundTelemetryCapture()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == BackgroundServiceObservability.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => Activities.Add(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == BackgroundServiceObservability.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Measurements.Add((instrument.Name, value, ToDictionary(tags))));
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Measurements.Add((instrument.Name, value, ToDictionary(tags))));
            _meterListener.Start();
        }

        public void Dispose()
        {
            _activityListener.Dispose();
            _meterListener.Dispose();
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
}
