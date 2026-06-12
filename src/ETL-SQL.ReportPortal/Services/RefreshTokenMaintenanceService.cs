using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Periodically deletes expired refresh-token rows so the table cannot grow without bound.
/// Revoked-but-unexpired rows are deliberately kept: they are the evidence the reuse
/// detection in <c>AuthController.Refresh</c> needs to spot a replayed (stolen) token.
/// Once a row is past <c>ExpiresAt</c> it can never authenticate or signal reuse, so it is
/// safe to remove.
/// </summary>
public sealed class RefreshTokenMaintenanceService(
    IServiceScopeFactory scopeFactory,
    PortalConfig config,
    TimeProvider clock,
    ILogger<RefreshTokenMaintenanceService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, config.Jwt.RefreshTokenPurgeIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var removed = await PurgeExpiredAsync(db, clock.GetUtcNow().UtcDateTime, stoppingToken);
                if (removed > 0)
                    log.LogInformation("Purged {Count} expired refresh tokens", removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Refresh token purge failed; will retry next interval");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Deletes every refresh token past its expiry, revoked or not.</summary>
    internal static Task<int> PurgeExpiredAsync(
        PortalDbContext db, DateTime utcNow, CancellationToken ct = default) =>
        db.RefreshTokens
            .Where(t => t.ExpiresAt <= utcNow)
            .ExecuteDeleteAsync(ct);
}
