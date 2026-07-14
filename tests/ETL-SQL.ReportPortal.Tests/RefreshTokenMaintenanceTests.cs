using System.Diagnostics;
using System.Diagnostics.Metrics;
using ETL_SQL.Core.Observability;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public sealed class RefreshTokenMaintenanceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"refresh_token_maint_{Guid.NewGuid():N}");

    public RefreshTokenMaintenanceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
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

    /// <summary>
    /// The purge deletes every token past its expiry (revoked or not) and keeps every live
    /// token — including revoked-but-live rows, which are the evidence reuse detection needs.
    /// </summary>
    [Fact]
    public async Task PurgeExpired_DeletesOnlyExpiredRows()
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

        using var db = NewDb();
        var user = new PortalUser { UserName = "purge_user", IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        RefreshToken Make(string name, TimeSpan expiresIn, bool revoked) => new()
        {
            UserId = user.Id,
            Token = $"hash_{name}",
            ExpiresAt = now + expiresIn,
            RevokedAt = revoked ? now.AddHours(-1) : null
        };

        db.RefreshTokens.AddRange(
            Make("expired", TimeSpan.FromDays(-1), revoked: false),
            Make("expired_revoked", TimeSpan.FromDays(-1), revoked: true),
            Make("live", TimeSpan.FromDays(1), revoked: false),
            Make("live_revoked", TimeSpan.FromDays(1), revoked: true));
        await db.SaveChangesAsync();

        var removed = await RefreshTokenMaintenanceService.PurgeExpiredAsync(db, now);

        Assert.Equal(2, removed);
        var remaining = await db.RefreshTokens.Select(t => t.Token).ToListAsync();
        Assert.Equal(["hash_live", "hash_live_revoked"], remaining.OrderBy(t => t));
        Assert.Contains(stoppedActivities, activity =>
            activity.OperationName == "background_service.run"
            && Tag(activity, ObservabilityConventions.Tags.ServiceName) == "refresh-token-maintenance"
            && Tag(activity, BackgroundServiceObservability.OperationTag) == "purge_expired"
            && Tag(activity, ObservabilityConventions.Tags.Status) == "success"
            && Tag(activity, ObservabilityConventions.Tags.RowsProcessed) == "2");
        Assert.Contains(measurements, measurement => measurement.Name == "etlsql.background_service.run.completed"
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.ServiceName, "refresh-token-maintenance")
            && HasTag(measurement.Tags, ObservabilityConventions.Tags.Status, "success"));
        Assert.DoesNotContain(measurements, measurement => measurement.Tags.Any(tag =>
            tag.Value is string value && value.Contains("hash_", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The security-state cache serves repeated lookups without a DB roundtrip (a direct DB
    /// change is invisible until eviction) and Evict forces the next read to reload.
    /// </summary>
    [Fact]
    public async Task UserSecurityStateCache_CachesUntilEvicted()
    {
        using var db = NewDb();
        var user = new PortalUser
        {
            UserName = "cache_user",
            IsActive = true,
            SecurityStamp = "stamp-1"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var cache = new UserSecurityStateCache(new MemoryCache(new MemoryCacheOptions()));

        var first = await cache.GetAsync(user.Id, db);
        Assert.NotNull(first);
        Assert.True(first.IsActive);
        Assert.Equal("stamp-1", first.SecurityStamp);

        // Change the row out-of-band: the cached state keeps serving.
        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, false)
                .SetProperty(u => u.SecurityStamp, "stamp-2"));
        var cached = await cache.GetAsync(user.Id, db);
        Assert.NotNull(cached);
        Assert.True(cached.IsActive);
        Assert.Equal("stamp-1", cached.SecurityStamp);

        // Eviction makes the change visible immediately.
        cache.Evict(user.Id);
        var reloaded = await cache.GetAsync(user.Id, db);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsActive);
        Assert.Equal("stamp-2", reloaded.SecurityStamp);
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
