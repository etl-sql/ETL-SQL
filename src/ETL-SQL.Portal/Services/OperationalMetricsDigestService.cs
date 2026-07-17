using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Emails administrators a periodic operational-metrics digest (and alerts on threshold breaches) from
/// <see cref="OperationalMetricsService"/>. Disabled unless <c>Portal:OperationalDigest:Enabled</c> is set
/// with recipients and an SMTP alias.
///
/// <para><b>HA:</b> the send cadence is gated by an <see cref="IClusterLockStore"/> lock with a TTL of one
/// interval, so exactly one node sends per interval and a node restart within the interval does not
/// re-send. The credential is decrypted per send and the composed SEND EMAIL script runs in-process via
/// <see cref="ISubscriptionScriptRunner"/> — the SMTP password never leaves the portal. Best-effort: any
/// failure is logged and retried next interval, never taking down the host.</para>
/// </summary>
public sealed class OperationalMetricsDigestService(
    IServiceScopeFactory scopeFactory,
    PortalConfig config,
    IClusterLockStore lockStore,
    ILogger<OperationalMetricsDigestService> log) : BackgroundService
{
    private const string LockName = "portal-operational-digest";
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = config.OperationalDigest;
        if (!cfg.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(cfg.Recipients) || string.IsNullOrWhiteSpace(cfg.SmtpAlias))
        {
            log.LogWarning(
                "Operational digest is enabled but has no recipients or SMTP alias configured; it will not run.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, cfg.IntervalHours));
        var poll = TimeSpan.FromMinutes(Math.Clamp(interval.TotalMinutes / 4, 5, 60));
        // Eligible immediately on first boot; the cluster lock's interval-length TTL then throttles the
        // cadence cluster-wide and prevents restart spam. Local guard stops a node re-sending its own lease.
        var nextEligibleUtc = DateTime.MinValue;

        log.LogInformation(
            "Operational digest started: every {Interval}h to {Recipients} via SMTP '{Alias}'{Mode}.",
            interval.TotalHours, cfg.Recipients, cfg.SmtpAlias, cfg.AlertOnly ? " (alert-only)" : "");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextEligibleUtc
                    && await lockStore.TryAcquireLockAsync(LockName, _owner, interval))
                {
                    // We won this interval's send. Do not renew the lock: letting it expire after one
                    // interval is exactly what re-enables the next send (here or on another node).
                    nextEligibleUtc = DateTime.UtcNow.Add(interval);
                    await SendDigestOnceAsync(cfg, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Operational digest cycle failed; will retry next poll.");
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

    internal async Task SendDigestOnceAsync(OperationalDigestConfig cfg, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = BackgroundServiceObservability.StartRun("portal", "operational-digest", "send");
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var metricsService = sp.GetRequiredService<OperationalMetricsService>();
        var sender = sp.GetRequiredService<IAdminNotificationSender>();

        var metrics = await metricsService.GetAsync(ct);
        var content = OperationalMetricsDigest.Build(metrics, cfg);

        if (cfg.AlertOnly && !content.HasAlerts)
        {
            log.LogDebug("Operational digest: alert-only mode and no alerts this interval; not sending.");
            CompleteDigest(activity, sw, "skipped");
            return;
        }

        var (success, error) = await sender.SendAsync(
            new AdminNotification(cfg.SmtpAlias, cfg.Sender, cfg.Recipients, content.Subject, content.Body, "opsdigest"),
            ct);
        if (success)
        {
            log.LogInformation(
                "Operational digest sent ({AlertCount} alert(s)) to {Recipients}.",
                content.Alerts.Count, cfg.Recipients);
            CompleteDigest(activity, sw, "sent");
        }
        else
        {
            log.LogWarning("Operational digest send failed: {Error}", error);
            CompleteDigest(activity, sw, "failed");
        }
    }

    private static void CompleteDigest(System.Diagnostics.Activity? activity, System.Diagnostics.Stopwatch sw, string status)
    {
        sw.Stop();
        BackgroundServiceObservability.CompleteRun(
            activity,
            "portal",
            "operational-digest",
            "send",
            status,
            sw.ElapsedMilliseconds);
    }
}
