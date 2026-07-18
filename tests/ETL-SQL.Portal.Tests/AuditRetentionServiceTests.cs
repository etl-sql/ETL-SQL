using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Observability;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class AuditRetentionServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"audit_retention_{Guid.NewGuid():N}");

    public AuditRetentionServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task PurgeExpired_EmitsLowCardinalityTelemetry()
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

        await using var db = NewDb();
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Action = "OLD_SECRET_EVENT",
                Detail = "password=do-not-leak",
                Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new AuditLog
            {
                Action = "RECENT_EVENT",
                Detail = "safe",
                Timestamp = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var removed = await AuditRetentionService.PurgeExpiredAsync(
            db, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, removed);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "RECENT_EVENT"));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "OLD_SECRET_EVENT"));
        Assert.Contains(stoppedActivities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "audit-retention"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == "purge_expired"
            && Tag(activity, ObservabilityConventions.Tags.Status) == "success"
            && Tag(activity, ObservabilityConventions.Tags.RowsProcessed) == "1");
        Assert.Contains(measurements, measurement => measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "audit-retention")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "success"));
        Assert.DoesNotContain(measurements, measurement => measurement.Tags.Any(tag =>
            tag.Value is string value
            && (value.Contains("OLD_SECRET_EVENT", StringComparison.OrdinalIgnoreCase)
                || value.Contains("do-not-leak", StringComparison.OrdinalIgnoreCase))));
    }

    private PortalDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "portal.db")}")
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        return db;
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
