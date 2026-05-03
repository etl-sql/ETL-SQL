using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            .ToListAsync();
        return Ok(subs.Select(ToDto));
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

        var recipientEmail = req.RecipientEmail?.Trim();
        if (string.IsNullOrEmpty(recipientEmail))
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId);
            recipientEmail = user?.Email;
        }
        if (string.IsNullOrEmpty(recipientEmail))
            return BadRequest(new { error = "No recipient email. Supply one or add an email to your profile." });

        SmtpConnection? smtp = null;
        if (format != SubscriptionFormat.Link)
        {
            if (string.IsNullOrEmpty(req.SmtpAlias))
                return BadRequest(new { error = "SmtpAlias is required for attachment delivery." });
            smtp = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Alias == req.SmtpAlias);
            if (smtp is null) return BadRequest(new { error = $"SMTP connection '{req.SmtpAlias}' not found." });
        }

        var sub = new Subscription
        {
            ReportId         = req.ReportId,
            UserId           = CurrentUserId,
            Name             = req.Name,
            Schedule         = req.Schedule,
            DeliverOnRefresh = false,
            Format           = format,
            SmtpAlias        = req.SmtpAlias ?? string.Empty,
            Recipients       = recipientEmail,
            ParametersJson   = SerializeParams(req.Parameters),
            IsActive         = true
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var scriptPath = GenerateJobScript(sub, report, smtp, recipientEmail, req.AtTime, req.Parameters);
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

    /// <summary>
    /// Updates a subscription's schedule, format, SMTP alias, active state, name, or parameters.
    /// Changing parameters or format rewrites the generated job script.
    /// Changing the schedule updates the Orchestrator job definition.
    /// </summary>
    [HttpPut("api/subscriptions/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSubscriptionRequest req)
    {
        var sub = await db.Subscriptions.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!IsAdmin && sub.UserId != CurrentUserId) return Forbid();

        var scheduleChanged    = req.Schedule  is not null && req.Schedule  != sub.Schedule;
        var newFormat          = sub.Format;
        var formatChanged      = req.Format is not null &&
                                 Enum.TryParse<SubscriptionFormat>(req.Format, true, out newFormat) &&
                                 newFormat != sub.Format;
        var parametersChanged  = req.Parameters is not null;
        var scriptNeedsRewrite = formatChanged || parametersChanged;

        if (req.Name             is not null) sub.Name             = req.Name;
        if (req.Schedule         is not null) sub.Schedule         = req.Schedule;
        if (req.DeliverOnRefresh.HasValue)    sub.DeliverOnRefresh = req.DeliverOnRefresh.Value;
        if (formatChanged)                    sub.Format           = newFormat;
        if (req.SmtpAlias        is not null) sub.SmtpAlias        = req.SmtpAlias;
        if (req.Recipients       is not null) sub.Recipients       = req.Recipients;
        if (req.IsActive.HasValue)            sub.IsActive         = req.IsActive.Value;
        if (parametersChanged)               sub.ParametersJson    = SerializeParams(req.Parameters);

        if (scriptNeedsRewrite && !string.IsNullOrEmpty(sub.ScriptPath) && System.IO.File.Exists(sub.ScriptPath))
        {
            var newParams = req.Parameters ?? DeserializeParams(sub.ParametersJson);
            RewriteScriptParameters(sub.ScriptPath, newParams, formatChanged ? sub.Format : null);
        }

        // Sync the Orchestrator job if schedule or active state changed
        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is not null && (scheduleChanged || req.IsActive.HasValue))
        {
            var store   = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            var jobName = $"{SubPrefix}{sub.Id}:{sub.Report?.Name}";
            var jobs    = (await store.GetActiveJobsAsync()).ToList();
            var job     = jobs.FirstOrDefault(j => j.Name == jobName);
            if (job is not null)
            {
                var (interval, unit) = ParseSchedule(sub.Schedule);
                var updated = job with
                {
                    Interval  = interval > 0 ? interval : job.Interval,
                    Unit      = interval > 0 ? unit     : job.Unit,
                    IsEnabled = sub.IsActive
                };
                await store.SaveJobAsync(updated);
            }
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_SUBSCRIPTION", "Subscription", sub.Id.ToString());
        return Ok(ToDto(sub));
    }

    [HttpDelete("api/subscriptions/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var sub = await db.Subscriptions.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!IsAdmin && sub.UserId != CurrentUserId) return Forbid();

        var jobName = $"{SubPrefix}{sub.Id}:{sub.Report?.Name}";

        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is not null)
        {
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            await store.DeleteJobAsync(jobName);
        }

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

    // ── SMTP alias list (any authenticated user) ───────────────────────────────

    /// <summary>Returns SMTP alias names so the subscribe modal can populate a dropdown.</summary>
    [HttpGet("api/smtp-aliases")]
    public async Task<IActionResult> ListSmtpAliases()
    {
        var aliases = await db.SmtpConnections.Select(c => c.Alias).ToListAsync();
        return Ok(aliases);
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
        string recipientEmail, string? atTime,
        Dictionary<string, string>? parameters)
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

        // Inject parameter SET statements so the report script picks them up at execution time.
        // RELDATE values are stored as-is and resolved fresh by the engine on each run.
        if (parameters is { Count: > 0 })
        {
            foreach (var (k, v) in parameters)
                sb.AppendLine($"SET {k} = '{Esc(v)}';");
            sb.AppendLine();
        }

        if (sub.Format != SubscriptionFormat.Link)
        {
            var ext        = sub.Format == SubscriptionFormat.CSV      ? "csv"
                           : sub.Format == SubscriptionFormat.Markdown ? "md"
                           : "pdf";
            var formatName = sub.Format == SubscriptionFormat.CSV      ? "CSV"
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

    private static void RewriteScriptParameters(
        string scriptPath,
        Dictionary<string, string>? parameters,
        SubscriptionFormat? newFormat)
    {
        var lines = System.IO.File.ReadAllLines(scriptPath).ToList();

        // Remove old SET @param = '...' lines
        lines.RemoveAll(l => Regex.IsMatch(l, @"^SET\s+@\w+\s*=\s*'.*';\s*$", RegexOptions.IgnoreCase));

        // Find insertion point: after the leading comment block + blank line
        int insertAt = 0;
        while (insertAt < lines.Count && lines[insertAt].StartsWith("--")) insertAt++;
        if (insertAt < lines.Count && lines[insertAt] == "") insertAt++;

        if (parameters is { Count: > 0 })
        {
            var setLines = parameters.Select(p => $"SET {p.Key} = '{Esc(p.Value)}';").ToList();
            setLines.Add("");
            lines.InsertRange(insertAt, setLines);
        }

        if (newFormat.HasValue)
        {
            var fmt = newFormat.Value switch
            {
                SubscriptionFormat.CSV      => "CSV",
                SubscriptionFormat.Markdown => "MARKDOWN",
                _                           => "PDF"
            };
            for (int i = 0; i < lines.Count; i++)
                lines[i] = Regex.Replace(lines[i], @"\bFORMAT\s+(PDF|CSV|MARKDOWN|BOTH)\b", $"FORMAT {fmt}", RegexOptions.IgnoreCase);
        }

        System.IO.File.WriteAllLines(scriptPath, lines);
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

    private static SubscriptionDto ToDto(Subscription s)
    {
        var parameters = DeserializeParams(s.ParametersJson);
        var summary    = BuildParameterSummary(parameters);
        return new SubscriptionDto(
            s.Id, s.ReportId, s.Report?.Name ?? "",
            s.Name, s.Schedule, s.DeliverOnRefresh, s.Format.ToString(),
            s.SmtpAlias, s.Recipients, s.LastSentAt, s.NextRunAt, s.FailCount, s.IsActive,
            parameters, summary);
    }

    private static string? SerializeParams(Dictionary<string, string>? p) =>
        p is { Count: > 0 } ? JsonSerializer.Serialize(p) : null;

    private static Dictionary<string, string>? DeserializeParams(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch { return null; }
    }

    private static string? BuildParameterSummary(Dictionary<string, string>? p) =>
        p is { Count: > 0 }
            ? string.Join(", ", p.Select(kv => $"{kv.Key}={kv.Value}"))
            : null;

    private static string SanitizeName(string name) =>
        new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static string Esc(string? s) => (s ?? string.Empty).Replace("'", "\\'");
}
