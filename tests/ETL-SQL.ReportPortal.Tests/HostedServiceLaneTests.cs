using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Hosted-service integration lane (P2.1). Unlike every other portal test, these run the real
/// <c>IHostedService</c> pipeline inside the host: startup validators that stop the application,
/// instance-lock acquisition, and the background maintenance/poll loops — all against the
/// fixture's isolated temp-directory databases and, where time matters, a controlled clock.
/// </summary>
[Trait("Category", "Portal")]
public sealed class HostedServiceLaneTests
{
    private static bool WaitForStop(IServiceProvider services, TimeSpan timeout)
        => services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.WaitHandle.WaitOne(timeout);

    /// <summary>
    /// With valid configuration every hosted service starts: the host serves health checks,
    /// the execution job service holds the single-instance lock files, and the shortened
    /// poller/maintenance loops tick without destabilizing the host.
    /// </summary>
    [Fact]
    public async Task ValidConfiguration_AllHostedServicesStart_HostStaysHealthy()
    {
        using var factory = new HostedPortalFactory();
        var client = factory.CreateClient();

        var first = await client.GetAsync("/health");
        Assert.True(first.IsSuccessStatusCode, $"initial /health returned {(int)first.StatusCode}");

        // The execution job service acquired the per-storage-root instance locks at startup.
        Assert.True(File.Exists(Path.Combine(factory.TempDir, "portal.instance.lock")),
            "expected the portal instance lock file to be held");

        // Let the 1s poller and purge loops run a few iterations, then confirm the host survived.
        await Task.Delay(TimeSpan.FromSeconds(3));
        var second = await client.GetAsync("/health");
        Assert.True(second.IsSuccessStatusCode, $"post-loop /health returned {(int)second.StatusCode}");
        Assert.False(WaitForStop(factory.Services, TimeSpan.Zero),
            "host unexpectedly began shutting down");
    }

    /// <summary>A JWT secret under 32 characters is fatal: the started host shuts itself down.</summary>
    [Fact]
    public void ShortJwtSecret_StopsApplicationAtStartup()
    {
        using var factory = new HostedPortalFactory(portalConfig: cfg => cfg.Jwt.Secret = "short");
        _ = factory.Services; // force host start

        Assert.True(WaitForStop(factory.Services, TimeSpan.FromSeconds(15)),
            "expected JwtSecretValidationService to stop the application");
    }

    /// <summary>
    /// A missing dataset at-rest key without the explicit machine fallback is fatal at startup.
    /// </summary>
    [Fact]
    public void MissingDatasetAtRestKey_WithoutFallback_StopsApplication()
    {
        using var factory = new HostedPortalFactory(portalConfig: cfg => cfg.Dataset.AtRestKey = null);
        _ = factory.Services;

        Assert.True(WaitForStop(factory.Services, TimeSpan.FromSeconds(15)),
            "expected DatasetAtRestKeyValidationService to stop the application");
    }

    /// <summary>
    /// The documented dev/standalone opt-in (Portal:Dataset:AllowMachineFallback) downgrades the
    /// missing key to a warning: the host starts and serves requests.
    /// </summary>
    [Fact]
    public async Task MissingDatasetAtRestKey_WithMachineFallback_HostStarts()
    {
        using var factory = new HostedPortalFactory(portalConfig: cfg =>
        {
            cfg.Dataset.AtRestKey = null;
            cfg.Dataset.AllowMachineFallback = true;
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode, $"/health returned {(int)response.StatusCode}");
        Assert.False(WaitForStop(factory.Services, TimeSpan.Zero),
            "host unexpectedly began shutting down");
    }

    /// <summary>
    /// The audit retention sweep (opt-in via Portal:Audit:RetentionDays) runs in-host and honors
    /// the injected clock: with "now" pinned to 2030 and 30-day retention, a 2029-06 row is
    /// purged while a row within the window survives.
    /// </summary>
    [Fact]
    public async Task AuditRetention_PurgesOldRows_UnderControlledClock()
    {
        var pinnedNow = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var factory = new HostedPortalFactory(
            portalConfig: cfg =>
            {
                cfg.Audit.RetentionDays = 30;
                cfg.Audit.PurgeIntervalSeconds = 1;
            },
            clock: new FixedClock(pinnedNow));
        _ = factory.CreateClient(); // start host

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.AuditLogs.AddRange(
                new AuditLog
                {
                    Action = "OLD_EVENT",
                    Timestamp = new DateTime(2029, 6, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new AuditLog
                {
                    Action = "RECENT_EVENT",
                    Timestamp = new DateTime(2029, 12, 20, 0, 0, 0, DateTimeKind.Utc)
                });
            await db.SaveChangesAsync();
        }

        var purged = false;
        for (var attempt = 0; attempt < 60 && !purged; attempt++)
        {
            await Task.Delay(250);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            purged = !await db.AuditLogs.AnyAsync(a => a.Action == "OLD_EVENT");
        }

        Assert.True(purged, "expected the retention sweep to delete the out-of-window audit row");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "RECENT_EVENT"),
                "expected the in-window audit row to survive");
        }
    }

    /// <summary>
    /// The refresh-token purge loop runs in-host on the lane's 1s cadence and uses the injected
    /// clock for its expiry cutoff: with "now" pinned to 2030, a token expiring in 2028 (which the
    /// real wall clock would still consider live) is purged, while a 2031 token is kept.
    /// </summary>
    [Fact]
    public async Task RefreshTokenMaintenance_PurgesByControlledClock_KeepsLiveRows()
    {
        var pinnedNow = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var factory = new HostedPortalFactory(clock: new FixedClock(pinnedNow));
        _ = factory.CreateClient(); // start host (migrates DB, seeds the first-run admin)

        const string expiredToken = "hash_expired_under_pinned_clock";
        const string liveToken = "hash_live_under_pinned_clock";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.OrderBy(u => u.Id).FirstAsync();
            db.RefreshTokens.AddRange(
                new RefreshToken
                {
                    UserId = user.Id,
                    Token = expiredToken,
                    ExpiresAt = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new RefreshToken
                {
                    UserId = user.Id,
                    Token = liveToken,
                    ExpiresAt = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
            await db.SaveChangesAsync();
        }

        var purged = false;
        for (var attempt = 0; attempt < 60 && !purged; attempt++)
        {
            await Task.Delay(250);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            purged = !await db.RefreshTokens.AnyAsync(t => t.Token == expiredToken);
        }

        Assert.True(purged, "expected the purge loop to delete the token expired under the pinned clock");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.True(await db.RefreshTokens.AnyAsync(t => t.Token == liveToken),
                "expected the still-live token to survive the purge");
        }
    }
}
