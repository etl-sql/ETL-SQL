using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Authorize]
public class SubscriptionsController(
    PortalDbContext        db,
    PortalConfig           config,
    OrchestratorDbLocator  dbLocator,
    AuditService           audit,
    SmtpPasswordProtector  pwdProtector) : ControllerBase
{
    private const string SubPrefix = "SUB:";

    private int  CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin       => User.IsInRole("Admin");

    // ── Subscription CRUD ──────────────────────────────────────────────────────

    /// <summary>List subscriptions the current user owns (admins see all).</summary>
    [HttpGet("api/subscriptions")]
    public async Task<IActionResult> List()
    {
        var userId = CurrentUserId;
        var subs = await db.Subscriptions
            .Include(s => s.Report)
            .Where(s => IsAdmin || s.UserId == userId)
            .Select(s => ToDto(s))
            .ToListAsync();
        return Ok(subs);
    }

    [HttpGet("api/subscriptions/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var sub = await db.Subscriptions.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!IsAdmin && sub.UserId != CurrentUserId) return Forbid();
        return Ok(ToDto(sub));
    }

    /// <summary>
    /// Creates a subscription. On success, writes a generated .etlsql job script to the
    /// script root and registers a JobDefinition in the Orchestrator's database.
    /// </summary>
    [HttpPost("api/subscriptions")]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == req.ReportId && !r.IsDeleted);
        if (report is null) return NotFound(new { error = "Report not found" });

        if (!Enum.TryParse<SubscriptionFormat>(req.Format, true, out var format))
            return BadRequest(new { error = "Format must be PDF, CSV, Markdown, or Link" });

        var (interval, unit) = ParseSchedule(req.Schedule);
        if (interval == 0) return BadRequest(new { error = "Invalid schedule. Use Daily, Weekly, Monthly, or Hourly." });

        // Resolve recipient email — use supplied address or fall back to user's profile email
        var recipientEmail = req.RecipientEmail?.Trim();
        if (string.IsNullOrEmpty(recipientEmail))
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId);
            recipientEmail = user?.Email;
        }
        if (string.IsNullOrEmpty(recipientEmail))
            return BadRequest(new { error = "No recipient email. Supply one or add an email to your profile." });

        // SMTP connection required for non-link formats
        SmtpConnection? smtp = null;
        if (format != SubscriptionFormat.Link)
        {
            if (string.IsNullOrEmpty(req.SmtpAlias))
                return BadRequest(new { error = "SmtpAlias is required for attachment delivery." });
            smtp = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Alias == req.SmtpAlias);
            if (smtp is null) return BadRequest(new { error = $"SMTP connection '{req.SmtpAlias}' not found." });
        }

        // Save subscription record first to get the Id
        var sub = new Subscription
        {
            ReportId         = req.ReportId,
            UserId           = CurrentUserId,
            Schedule         = req.Schedule,
            DeliverOnRefresh = false,
            Format           = format,
            SmtpAlias        = req.SmtpAlias ?? string.Empty,
            Recipients       = recipientEmail,
            IsActive         = true
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        // Generate the job script and register with Orchestrator
        var scriptPath = GenerateJobScript(sub, report, smtp, recipientEmail, req.AtTime);
        sub.ScriptPath = scriptPath;

        var jobName = $"{SubPrefix}{sub.Id}:{report.Name}";
        var jobDef  = new JobDefinition(
            Name:               jobName,
            Script:             scriptPath,
            Interval:           interval,
            Unit:               unit,
            AtTime:             req.AtTime,
            LastRun:            null,
            NextRun:            null,
            IsEnabled:          true,
            MaxRetries:         3,
            RetryDelaySeconds:  60);

        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is not null)
        {
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            await store.SaveJobAsync(jobDef);
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_SUBSCRIPTION", "Subscription", sub.Id.ToString(), jobName);
        return CreatedAtAction(nameof(Get), new { id = sub.Id }, ToDto(sub));
    }

    [HttpDelete("api/subscriptions/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var sub = await db.Subscriptions.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!IsAdmin && sub.UserId != CurrentUserId) return Forbid();

        var jobName = $"{SubPrefix}{sub.Id}:{sub.Report?.Name}";

        // Remove from Orchestrator
        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is not null)
        {
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            await store.DeleteJobAsync(jobName);
        }

        // Delete generated script file
        if (!string.IsNullOrEmpty(sub.ScriptPath) && System.IO.File.Exists(sub.ScriptPath))
            System.IO.File.Delete(sub.ScriptPath);

        db.Subscriptions.Remove(sub);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_SUBSCRIPTION", "Subscription", id.ToString());
        return NoContent();
    }

    /// <summary>Returns Orchestrator JobHistory entries for this subscription.</summary>
    [HttpGet("api/subscriptions/{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id, [FromQuery] int limit = 50)
    {
        var sub = await db.Subscriptions.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!IsAdmin && sub.UserId != CurrentUserId) return Forbid();

        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is null)
            return Ok(Array.Empty<object>());

        var jobName = $"{SubPrefix}{sub.Id}:{sub.Report?.Name}";
        var store   = new SQLiteJobHistoryStore(orchDbPath);
        await store.InitializeAsync();
        var history = await store.GetHistoryAsync(jobName, limit);
        return Ok(history);
    }

    // ── SMTP connections (Admin only) ──────────────────────────────────────────

    [HttpGet("api/admin/smtp")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ListSmtp()
    {
        var conns = await db.SmtpConnections
            .Select(c => new SmtpConnectionDto(c.Id, c.Alias, c.Host, c.Port, c.Username, c.FromAddress, c.UseSsl))
            .ToListAsync();
        return Ok(conns);
    }

    [HttpPost("api/admin/smtp")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateSmtp([FromBody] CreateSmtpRequest req)
    {
        if (await db.SmtpConnections.AnyAsync(c => c.Alias == req.Alias))
            return Conflict(new { error = "Alias already exists" });

        var conn = new SmtpConnection
        {
            Alias             = req.Alias,
            Host              = req.Host,
            Port              = req.Port,
            Username          = req.Username,
            EncryptedPassword = req.Password is not null ? pwdProtector.Protect(req.Password) : null,
            FromAddress       = req.FromAddress,
            UseSsl            = req.UseSsl
        };
        db.SmtpConnections.Add(conn);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_SMTP", "SmtpConnection", conn.Id.ToString(), req.Alias);
        return Ok(new SmtpConnectionDto(conn.Id, conn.Alias, conn.Host, conn.Port, conn.Username, conn.FromAddress, conn.UseSsl));
    }

    [HttpPut("api/admin/smtp/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateSmtp(int id, [FromBody] UpdateSmtpRequest req)
    {
        var conn = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return NotFound();

        if (req.Host        is not null) conn.Host        = req.Host;
        if (req.Port.HasValue)           conn.Port        = req.Port.Value;
        if (req.Username    is not null) conn.Username    = req.Username;
        if (req.Password    is not null) conn.EncryptedPassword = pwdProtector.Protect(req.Password);
        if (req.FromAddress is not null) conn.FromAddress = req.FromAddress;
        if (req.UseSsl.HasValue)         conn.UseSsl      = req.UseSsl.Value;

        await db.SaveChangesAsync();
        return Ok(new SmtpConnectionDto(conn.Id, conn.Alias, conn.Host, conn.Port, conn.Username, conn.FromAddress, conn.UseSsl));
    }

    [HttpDelete("api/admin/smtp/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteSmtp(int id)
    {
        var conn = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return NotFound();
        db.SmtpConnections.Remove(conn);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_SMTP", "SmtpConnection", id.ToString());
        return NoContent();
    }

    // ── Script generation ──────────────────────────────────────────────────────

    private string GenerateJobScript(
        Subscription sub, Report report, SmtpConnection? smtp,
        string recipientEmail, string? atTime)
    {
        var scriptDir  = System.IO.Path.GetFullPath(config.ScriptRootPath);
        var scriptName = $"sub_{sub.Id}_{SanitizeName(report.Name)}.etlsql";
        var scriptPath = System.IO.Path.Combine(scriptDir, "subscriptions", scriptName);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(scriptPath)!);

        var reportTitle = report.Name;
        var sb = new StringBuilder();
        sb.AppendLine($"-- Subscription {sub.Id}: {reportTitle}");
        sb.AppendLine($"-- Recipient: {recipientEmail}");
        sb.AppendLine($"-- Schedule: {sub.Schedule}");
        sb.AppendLine($"-- Generated by ETL-SQL Report Portal — do not edit manually");
        sb.AppendLine();

        if (sub.Format != SubscriptionFormat.Link)
        {
            var ext        = sub.Format == SubscriptionFormat.CSV ? "csv"
                           : sub.Format == SubscriptionFormat.Markdown ? "md"
                           : "pdf";
            var formatName = sub.Format == SubscriptionFormat.CSV ? "CSV"
                           : sub.Format == SubscriptionFormat.Markdown ? "MARKDOWN"
                           : "PDF";
            var outputPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"sub_{sub.Id}_output.{ext}");

            sb.AppendLine($"EXPORT REPORT '{report.ScriptPath}' FORMAT {formatName} TO '{outputPath}';");
            sb.AppendLine();

            var password = pwdProtector.Unprotect(smtp!.EncryptedPassword) ?? string.Empty;
            var fromAddr = smtp.FromAddress ?? smtp.Username ?? "etlsql@localhost";

            sb.AppendLine($"CREATE CONNECTION __sub_smtp TYPE SMTP (");
            sb.AppendLine($"    HOST     = '{Esc(smtp.Host)}',");
            sb.AppendLine($"    PORT     = {smtp.Port},");
            if (!string.IsNullOrEmpty(smtp.Username))
                sb.AppendLine($"    USERNAME = '{Esc(smtp.Username)}',");
            if (!string.IsNullOrEmpty(password))
                sb.AppendLine($"    PASSWORD = '{Esc(password)}',");
            sb.AppendLine($"    USE_SSL  = '{smtp.UseSsl.ToString().ToLower()}'");
            sb.AppendLine($");");
            sb.AppendLine();
            sb.AppendLine($"SEND EMAIL");
            sb.AppendLine($"    TO      '{Esc(recipientEmail)}'");
            sb.AppendLine($"    FROM    '{Esc(fromAddr)}'");
            sb.AppendLine($"    SUBJECT 'Report: {Esc(reportTitle)}'");
            sb.AppendLine($"    BODY    'Please find the attached report: {Esc(reportTitle)}.'");
            sb.AppendLine($"    ATTACHMENTS '{outputPath}'");
            sb.AppendLine($"    AT __sub_smtp;");
        }
        else
        {
            // Link-only: just send a portal URL — no SMTP connection needed in this path
            // (SMTP alias must still be configured; the admin fills in the inline connection here)
            if (smtp is not null)
            {
                var password = pwdProtector.Unprotect(smtp.EncryptedPassword) ?? string.Empty;
                var fromAddr = smtp.FromAddress ?? smtp.Username ?? "etlsql@localhost";
                var portalUrl = $"{{portal_url}}/index.html#report/{report.Id}";

                sb.AppendLine($"CREATE CONNECTION __sub_smtp TYPE SMTP (");
                sb.AppendLine($"    HOST     = '{Esc(smtp.Host)}',");
                sb.AppendLine($"    PORT     = {smtp.Port},");
                if (!string.IsNullOrEmpty(smtp.Username))
                    sb.AppendLine($"    USERNAME = '{Esc(smtp.Username)}',");
                if (!string.IsNullOrEmpty(password))
                    sb.AppendLine($"    PASSWORD = '{Esc(password)}',");
                sb.AppendLine($"    USE_SSL  = '{smtp.UseSsl.ToString().ToLower()}'");
                sb.AppendLine($");");
                sb.AppendLine();
                sb.AppendLine($"SEND EMAIL");
                sb.AppendLine($"    TO      '{Esc(recipientEmail)}'");
                sb.AppendLine($"    FROM    '{Esc(fromAddr)}'");
                sb.AppendLine($"    SUBJECT 'Report ready: {Esc(reportTitle)}'");
                sb.AppendLine($"    BODY    'Your report is ready. View it here: {portalUrl}'");
                sb.AppendLine($"    AT __sub_smtp;");
            }
        }

        System.IO.File.WriteAllText(scriptPath, sb.ToString());
        return scriptPath;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (int interval, string unit) ParseSchedule(string? schedule) =>
        schedule?.ToUpperInvariant() switch
        {
            "HOURLY"  => (1,  "HOUR"),
            "DAILY"   => (1,  "DAY"),
            "WEEKLY"  => (1,  "WEEK"),
            "MONTHLY" => (1,  "MONTH"),
            _         => (0,  "DAY")
        };

    private static SubscriptionDto ToDto(Subscription s) => new(
        s.Id, s.ReportId, s.Report?.Name ?? "",
        s.Schedule, s.DeliverOnRefresh, s.Format.ToString(),
        s.SmtpAlias, s.Recipients, s.LastSentAt, s.NextRunAt, s.FailCount, s.IsActive);

    private static string SanitizeName(string name) =>
        new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static string Esc(string? s) => (s ?? string.Empty).Replace("'", "\\'");
}
