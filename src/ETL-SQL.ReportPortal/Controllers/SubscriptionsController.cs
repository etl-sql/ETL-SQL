using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Authorize]
public class SubscriptionsController(
    PortalDbContext db,
    PortalConfig config,
    OrchestratorDbLocator dbLocator,
    AuditService audit,
    SubscriptionDeliveryStatusService deliveryStatus,
    FolderPermissionService folderPermissions,
    SmtpPasswordProtector pwdProtector) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

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

    [HttpGet("api/admin/subscriptions/catalog")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdminCatalog(
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] string? format = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Subscriptions.Include(s => s.Report).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(s =>
                EF.Functions.Like(s.Name ?? "", term) ||
                EF.Functions.Like(s.Recipients, term) ||
                EF.Functions.Like(s.Report.Name, term));
        }
        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(s => s.IsActive);
        else if (string.Equals(status, "paused", StringComparison.OrdinalIgnoreCase))
            query = query.Where(s => !s.IsActive);
        else if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            query = query.Where(s => s.FailCount > 0);
        if (!string.IsNullOrWhiteSpace(format) &&
            Enum.TryParse<SubscriptionFormat>(format, true, out var parsedFormat))
            query = query.Where(s => s.Format == parsedFormat);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(s => s.Report.Name).ThenBy(s => s.Recipients)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return Ok(new PagedResult<SubscriptionDto>(items.Select(ToDto).ToList(), total, page, pageSize));
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
        // COMPAT_BREAK: 0.10
        if (!await folderPermissions.HasPermissionAsync(report.FolderId, FolderPermission.Read, User))
            return Forbid();

        if (!Enum.TryParse<SubscriptionFormat>(req.Format, true, out var format))
            return BadRequest(new { error = "Format must be PDF, CSV, Markdown, or Link" });

        var (interval, _) = SubscriptionOrchestration.ParseSchedule(req.Schedule);
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
        if (!string.IsNullOrEmpty(req.SmtpAlias))
        {
            smtp = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Alias == req.SmtpAlias);
            if (smtp is null) return BadRequest(new { error = $"SMTP connection '{req.SmtpAlias}' not found." });
        }
        else if (format != SubscriptionFormat.Link)
        {
            return BadRequest(new { error = "SmtpAlias is required for attachment delivery." });
        }

        var sub = new Subscription
        {
            ReportId = req.ReportId,
            UserId = CurrentUserId,
            Name = req.Name,
            Schedule = req.Schedule,
            AtTime = req.AtTime,
            DeliverOnRefresh = false,
            Format = format,
            SmtpAlias = req.SmtpAlias ?? string.Empty,
            Recipients = recipientEmail,
            ParametersJson = SerializeParams(req.Parameters),
            IsActive = true
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        // Row first (it is the source of truth and carries everything needed to rebuild the
        // rest), then script, then ScriptPath, then the Orchestrator job. A crash anywhere in
        // between is healed by SubscriptionScriptMaintenance at the next startup.
        var scriptPath = GenerateJobScript(sub, report);
        sub.ScriptPath = scriptPath;
        await db.SaveChangesAsync();

        var jobDef = SubscriptionOrchestration.BuildJobDefinition(sub, report.Name, scriptPath);
        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is not null)
        {
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            await store.SaveJobAsync(jobDef);
        }

        await audit.LogAsync(CurrentUserId, "CREATE_SUBSCRIPTION", "Subscription", sub.Id.ToString(), jobDef.Name);
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

        var scheduleChanged = req.Schedule is not null && req.Schedule != sub.Schedule;
        var newFormat = sub.Format;
        var formatChanged = req.Format is not null &&
                                 Enum.TryParse<SubscriptionFormat>(req.Format, true, out newFormat) &&
                                 newFormat != sub.Format;
        var parametersChanged = req.Parameters is not null;
        var smtpAliasChanged = req.SmtpAlias is not null && req.SmtpAlias != sub.SmtpAlias;
        var recipientsChanged = req.Recipients is not null && req.Recipients != sub.Recipients;
        var scriptNeedsRewrite = formatChanged || parametersChanged || smtpAliasChanged || recipientsChanged;

        if (req.Name is not null) sub.Name = req.Name;
        if (req.Schedule is not null) sub.Schedule = req.Schedule;
        if (req.DeliverOnRefresh.HasValue) sub.DeliverOnRefresh = req.DeliverOnRefresh.Value;
        if (formatChanged) sub.Format = newFormat;
        if (req.SmtpAlias is not null) sub.SmtpAlias = req.SmtpAlias;
        if (req.Recipients is not null) sub.Recipients = req.Recipients;
        if (req.IsActive.HasValue) sub.IsActive = req.IsActive.Value;
        if (parametersChanged) sub.ParametersJson = SerializeParams(req.Parameters);

        if (scriptNeedsRewrite && !string.IsNullOrEmpty(sub.ScriptPath))
        {
            if (!TryResolveSubscriptionScript(sub.ScriptPath, out _))
                return Forbid();

            // Format/parameter/recipient changes live in the subscription row and are read by
            // the delivery service at send time; rewriting just heals any legacy script content.
            GenerateJobScript(sub, sub.Report);
        }

        // Sync the Orchestrator job if schedule or active state changed
        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is not null && (scheduleChanged || req.IsActive.HasValue))
        {
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            var jobName = SubscriptionOrchestration.JobName(sub.Id, sub.Report?.Name);
            var job = await store.GetJobAsync(jobName);
            if (job is not null)
            {
                var (interval, unit) = SubscriptionOrchestration.ParseSchedule(sub.Schedule);
                var updated = job with
                {
                    Interval = interval > 0 ? interval : job.Interval,
                    Unit = interval > 0 ? unit : job.Unit,
                    IsEnabled = sub.IsActive
                };
                await store.SaveJobAsync(updated);
            }
            else if (sub.Report is not null && !string.IsNullOrEmpty(sub.ScriptPath))
            {
                // Heal a missing job from row state (e.g. a crash between the portal row and
                // the job DB during create) instead of leaving the subscription dormant.
                await store.SaveJobAsync(
                    SubscriptionOrchestration.BuildJobDefinition(sub, sub.Report.Name, sub.ScriptPath));
            }
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_SUBSCRIPTION", "Subscription", sub.Id.ToString());
        return Ok(ToDto(sub));
    }

    [HttpPost("api/admin/subscriptions/bulk-status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkUpdateStatus([FromBody] BulkSubscriptionStatusRequest req)
    {
        var ids = req.SubscriptionIds.Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { error = "Select at least one subscription." });

        var subs = await db.Subscriptions
            .Include(s => s.Report)
            .Where(s => ids.Contains(s.Id))
            .ToListAsync();
        foreach (var sub in subs)
            sub.IsActive = req.IsActive;

        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is not null)
        {
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            foreach (var sub in subs)
            {
                var jobName = SubscriptionOrchestration.JobName(sub.Id, sub.Report.Name);
                var job = await store.GetJobAsync(jobName);
                if (job is not null)
                    await store.SaveJobAsync(job with { IsEnabled = req.IsActive });
            }
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "BULK_UPDATE_SUBSCRIPTION_STATUS", "Subscription", null,
            $"{subs.Count} subscriptions set active={req.IsActive}");
        return Ok(new { Updated = subs.Count });
    }

    [HttpDelete("api/subscriptions/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var sub = await db.Subscriptions.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!IsAdmin && sub.UserId != CurrentUserId) return Forbid();

        var jobName = SubscriptionOrchestration.JobName(sub.Id, sub.Report?.Name);
        string? resolvedScriptPath = null;
        if (!string.IsNullOrEmpty(sub.ScriptPath)
            && !TryResolveSubscriptionScript(sub.ScriptPath, out resolvedScriptPath))
            return Forbid();

        var orchDbPath = dbLocator.Resolve();
        if (orchDbPath is not null)
        {
            var store = new SQLiteJobHistoryStore(orchDbPath);
            await store.InitializeAsync();
            await store.DeleteJobAsync(jobName);
        }

        if (!string.IsNullOrEmpty(resolvedScriptPath) && System.IO.File.Exists(resolvedScriptPath))
            System.IO.File.Delete(resolvedScriptPath);

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

        var history = await deliveryStatus.SynchronizeAsync(sub, limit);
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
            Alias = req.Alias,
            Host = req.Host,
            Port = req.Port,
            Username = req.Username,
            EncryptedPassword = req.Password is not null ? pwdProtector.Protect(req.Password) : null,
            FromAddress = req.FromAddress,
            UseSsl = req.UseSsl
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

        if (req.Host is not null) conn.Host = req.Host;
        if (req.Port.HasValue) conn.Port = req.Port.Value;
        if (req.Username is not null) conn.Username = req.Username;
        if (req.Password is not null) conn.EncryptedPassword = pwdProtector.Protect(req.Password);
        if (req.FromAddress is not null) conn.FromAddress = req.FromAddress;
        if (req.UseSsl.HasValue) conn.UseSsl = req.UseSsl.Value;

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

    /// <summary>
    /// Writes the persisted Orchestrator job script for the subscription. The script is a
    /// credential-free trigger only (P0.1): the portal's SubscriptionDeliveryService performs
    /// the actual export and email delivery in-process after delivery-time reauthorization,
    /// so no SMTP credential, recipient, or parameter is ever written to disk.
    /// </summary>
    private string GenerateJobScript(Subscription sub, Report report)
    {
        // COMPAT_BREAK: 0.11
        var scriptName = SubscriptionOrchestration.ScriptFileName(sub.Id, report.Name);
        if (!PortalPathGuard.TryResolveScript(config, System.IO.Path.Combine("subscriptions", scriptName), out var scriptPath))
            throw new InvalidOperationException("Subscription script path must be within the configured script root.");

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(scriptPath)!);
        SubscriptionTriggerScript.Write(scriptPath, sub.Id);
        return scriptPath;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool TryResolveSubscriptionScript(string? scriptPath, out string resolved)
    {
        resolved = string.Empty;
        return !string.IsNullOrWhiteSpace(scriptPath)
            && PortalPathGuard.TryResolveScript(config, scriptPath, out resolved);
    }

    private static SubscriptionDto ToDto(Subscription s)
    {
        var parameters = DeserializeParams(s.ParametersJson);
        var summary = BuildParameterSummary(parameters);
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
}
