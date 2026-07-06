using System.Text;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

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

    private async Task SendDigestOnceAsync(OperationalDigestConfig cfg, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<PortalDbContext>();
        var metricsService = sp.GetRequiredService<OperationalMetricsService>();
        var pwdProtector = sp.GetRequiredService<SmtpPasswordProtector>();
        var runner = sp.GetRequiredService<ISubscriptionScriptRunner>();

        var smtp = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Alias == cfg.SmtpAlias, ct);
        if (smtp is null)
        {
            log.LogWarning("Operational digest SMTP alias '{Alias}' no longer exists; skipping.", cfg.SmtpAlias);
            return;
        }

        var metrics = await metricsService.GetAsync(ct);
        var content = OperationalMetricsDigest.Build(metrics, cfg);

        if (cfg.AlertOnly && !content.HasAlerts)
        {
            log.LogDebug("Operational digest: alert-only mode and no alerts this interval; not sending.");
            return;
        }

        var password = pwdProtector.Unprotect(smtp.EncryptedPassword);
        if (!string.IsNullOrEmpty(smtp.EncryptedPassword) && password is null)
        {
            log.LogWarning("Operational digest: SMTP credential could not be resolved; skipping.");
            return;
        }

        var fromAddr = !string.IsNullOrWhiteSpace(cfg.Sender)
            ? cfg.Sender
            : smtp.FromAddress ?? smtp.Username ?? "etlsql@localhost";

        var script = ComposeScript(smtp, password, fromAddr, cfg.Recipients, content);

        var (success, error) = await runner.RunAsync(script, $"opsdigest-{Guid.NewGuid():N}", ct);
        if (success)
        {
            log.LogInformation(
                "Operational digest sent ({AlertCount} alert(s)) to {Recipients}.",
                content.Alerts.Count, cfg.Recipients);
        }
        else
        {
            var safe = ETL_SQL.Core.Common.SecretRedactor.Redact(
                (error ?? "unknown error").Replace(password ?? "\0", "***")) ?? "delivery failed";
            log.LogWarning("Operational digest send failed: {Error}", safe);
        }
    }

    private static string ComposeScript(
        SmtpConnection smtp, string? password, string fromAddr, string recipients,
        OperationalMetricsDigest.DigestContent content)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CREATE CONNECTION __opsdigest_smtp AS SMTP(");
        sb.AppendLine($"    HOST     = '{Esc(smtp.Host)}',");
        sb.AppendLine($"    PORT     = {smtp.Port},");
        if (!string.IsNullOrEmpty(smtp.Username))
            sb.AppendLine($"    USERNAME = '{Esc(smtp.Username)}',");
        if (!string.IsNullOrEmpty(password))
            sb.AppendLine($"    PASSWORD = '{Esc(password)}',");
        sb.AppendLine($"    USE_SSL  = '{smtp.UseSsl.ToString().ToLowerInvariant()}'");
        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("SEND EMAIL");
        sb.AppendLine($"    TO      '{Esc(recipients)}'");
        sb.AppendLine($"    FROM    '{Esc(fromAddr)}'");
        sb.AppendLine($"    SUBJECT '{Esc(content.Subject)}'");
        sb.AppendLine($"    BODY    '{Esc(content.Body)}'");
        sb.AppendLine("    AT __opsdigest_smtp;");
        return sb.ToString();
    }

    // The lexer escapes a quote by doubling it ('' -> '); newlines inside a string literal are kept
    // verbatim, so the multi-line body needs no special handling.
    private static string Esc(string? s) => (s ?? string.Empty).Replace("'", "''");
}
