using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>What one admin service run produced: null content means nothing to report this interval.</summary>
public sealed record AdminDigestContent(string Subject, string Body, string Detail);

/// <summary>
/// Base for the native admin background services (failure digest, backup report, capacity report)
/// that replace the samples/admin_operations scheduler scripts.
///
/// <para><b>HA:</b> each interval is gated by an <see cref="IClusterLockStore"/> lock with a TTL of
/// one interval, so exactly one node runs per interval and restarts do not re-send (same pattern as
/// <see cref="OperationalMetricsDigestService"/>). Every run — sent, skipped, or failed — is
/// recorded as an <see cref="AdminServiceRun"/> row (pruned per retention config) and audited.
/// Delivery is retried up to MaxAttempts within a run; failures never take down the host.</para>
/// </summary>
public abstract class AdminDigestServiceBase(
    IServiceScopeFactory scopeFactory,
    PortalConfig config,
    IClusterLockStore lockStore,
    ILogger log) : BackgroundService
{
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    /// <summary>Stable service name used for the cluster lock, run history, and the status API.</summary>
    public abstract string ServiceName { get; }

    protected abstract AdminServiceScheduleConfig Schedule { get; }

    protected PortalConfig Config => config;

    /// <summary>Builds this interval's report, or null when there is nothing to send (AlertOnly modes).</summary>
    protected abstract Task<AdminDigestContent?> BuildAsync(IServiceProvider scope, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = Schedule;
        if (!cfg.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(cfg.Recipients) || string.IsNullOrWhiteSpace(cfg.SmtpAlias))
        {
            log.LogWarning("{Service} is enabled but has no recipients or SMTP alias configured; it will not run.", ServiceName);
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, cfg.IntervalHours));
        var poll = TimeSpan.FromMinutes(Math.Clamp(interval.TotalMinutes / 4, 5, 60));
        var nextEligibleUtc = DateTime.MinValue;

        log.LogInformation(
            "{Service} started: every {Interval}h to {Recipients} via SMTP '{Alias}'.",
            ServiceName, interval.TotalHours, cfg.Recipients, cfg.SmtpAlias);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextEligibleUtc
                    && await lockStore.TryAcquireLockAsync($"portal-admin-{ServiceName}", _owner, interval))
                {
                    // We won this interval. The lock is deliberately not renewed: its expiry after one
                    // interval is what re-enables the next run, here or on another node.
                    nextEligibleUtc = DateTime.UtcNow.Add(interval);
                    await RunOnceAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "{Service} cycle failed; will retry next poll.", ServiceName);
            }

            try
            {
                await Task.Delay(poll, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One complete run: build, deliver with retries, record history + audit. Public for tests.</summary>
    public async Task<AdminServiceRun> RunOnceAsync(CancellationToken ct)
    {
        var cfg = Schedule;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun("portal", ServiceName, "admin_digest_run");
        var run = new AdminServiceRun
        {
            ServiceName = ServiceName,
            StartedAtUtc = DateTime.UtcNow,
            NodeName = Environment.MachineName
        };

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        try
        {
            var content = await BuildAsync(sp, ct);
            if (content == null)
            {
                run.Outcome = "Skipped";
                run.Detail = "Nothing to report this interval.";
            }
            else
            {
                var sender = sp.GetRequiredService<IAdminNotificationSender>();
                var maxAttempts = Math.Max(1, cfg.MaxAttempts);
                string? lastError = null;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    run.Attempts = attempt;
                    var (success, error) = await sender.SendAsync(
                        new AdminNotification(cfg.SmtpAlias, cfg.Sender, cfg.Recipients, content.Subject, content.Body, ServiceName),
                        ct);
                    if (success)
                    {
                        run.Outcome = "Sent";
                        run.Detail = content.Detail;
                        lastError = null;
                        break;
                    }

                    lastError = error;
                    log.LogWarning("{Service} delivery attempt {Attempt}/{Max} failed: {Error}",
                        ServiceName, attempt, maxAttempts, error);
                    if (attempt < maxAttempts)
                        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, cfg.RetryDelaySeconds)), ct);
                }

                if (lastError != null)
                {
                    run.Outcome = "Failed";
                    run.Detail = lastError;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            run.Outcome = "Failed";
            run.Detail = ETL_SQL.Core.Common.SecretRedactor.Redact(ex.Message);
            log.LogWarning(ex, "{Service} run failed.", ServiceName);
        }

        run.CompletedAtUtc = DateTime.UtcNow;
        await RecordRunAsync(sp, run, ct);
        sw.Stop();
        BackgroundServiceObservability.CompleteRun(
            activity,
            "portal",
            ServiceName,
            "admin_digest_run",
            run.Outcome.ToLowerInvariant(),
            sw.ElapsedMilliseconds,
            run.Attempts);
        return run;
    }

    private async Task RecordRunAsync(IServiceProvider sp, AdminServiceRun run, CancellationToken ct)
    {
        try
        {
            var db = sp.GetRequiredService<PortalDbContext>();
            var audit = sp.GetRequiredService<AuditService>();
            db.AdminServiceRuns.Add(run);
            audit.Stage(null, "ADMIN_SERVICE_RUN", "AdminService", ServiceName,
                $"Outcome={run.Outcome}; Attempts={run.Attempts}; Detail={run.Detail}",
                actorType: "System");

            var retentionDays = Math.Max(1, config.AdminServices.RunHistoryRetentionDays);
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var stale = await db.AdminServiceRuns
                .Where(r => r.ServiceName == ServiceName && r.StartedAtUtc < cutoff)
                .ToListAsync(ct);
            db.AdminServiceRuns.RemoveRange(stale);

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "{Service} could not record its run history.", ServiceName);
        }
    }
}
