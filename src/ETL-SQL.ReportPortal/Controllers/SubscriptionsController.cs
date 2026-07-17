using System.Security.Claims;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Filters;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Authorize]
[RequirePortalModule("Reporting")]
public class SubscriptionsController(
    PortalDbContext db,
    OrchestratorDbLocator dbLocator,
    IOrchestratorStoreFactory orchestratorStoreFactory,
    AuditService audit,
    SubscriptionDeliveryStatusService deliveryStatus,
    FolderPermissionService folderPermissions,
    SmtpPasswordProtector pwdProtector,
    IDatasetRegistry datasetRegistry,
    SubscriptionScriptService subscriptionScripts,
    SubscriptionQueryService subscriptionQueries) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    // ── Subscription CRUD ──────────────────────────────────────────────────────

    [HttpPost("api/subscriptions/refresh-jobs")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateRefreshJob([FromBody] CreateRefreshJobRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ReportName)
            || string.IsNullOrWhiteSpace(req.Schedule)
            || string.IsNullOrWhiteSpace(req.OrchestratorAlias))
        {
            return BadRequest(new { error = "ReportName, Schedule, and OrchestratorAlias are required." });
        }

        var target = req.ReportName.Trim().ToLower();
        var reports = await db.Reports
            .AsNoTracking()
            .Include(r => r.Folder)
            .Where(r => !r.IsDeleted && (
                r.Name.ToLower() == target
                || (r.Folder.Path + "/" + r.Name).ToLower() == target
            ))
            .ToListAsync();
        var matches = reports;
        if (matches.Count == 0) return NotFound(new { error = $"Report '{req.ReportName}' not found." });
        if (matches.Count > 1) return Conflict(new { error = $"Report '{req.ReportName}' is ambiguous." });

        var report = matches[0];
        var alias = req.OrchestratorAlias.Trim();
        var jobName = $"portal-refresh:{alias}:{report.Id}";
        await datasetRegistry.RegisterRefreshJobAsync(report.Id, jobName, req.Schedule.Trim());
        await audit.LogAsync(
            CurrentUserId,
            "CREATE_REFRESH_JOB",
            "Report",
            report.Id.ToString(),
            $"Alias={alias}; Schedule={req.Schedule.Trim()}");
        return Ok(new { reportId = report.Id, jobName, schedule = req.Schedule.Trim(), orchestratorAlias = alias });
    }

    /// <summary>List subscriptions the current user owns (admins see all).</summary>
    [HttpGet("api/subscriptions")]
    public async Task<IActionResult> List()
    {
        return Ok(await subscriptionQueries.ListAsync(CurrentUserId, IsAdmin));
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
        return Ok(await subscriptionQueries.GetAdminCatalogAsync(q, status, format, page, pageSize));
    }

    [HttpGet("api/subscriptions/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var sub = await subscriptionQueries.LoadAsync(id);
        if (sub is null) return NotFound();
        if (!IsAdmin && sub.UserId != CurrentUserId) return Forbid();
        OptimisticConcurrency.SetETag(Response, sub.Version);
        return Ok(subscriptionQueries.ToDto(sub));
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
            ParametersJson = SubscriptionQueryService.SerializeParams(req.Parameters),
            IsActive = true
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        // Row first (it is the source of truth and carries everything needed to rebuild the
        // rest), then script, then ScriptPath, then the Orchestrator job. A crash anywhere in
        // between is healed by SubscriptionScriptMaintenance at the next startup.
        var scriptPath = subscriptionScripts.WriteTriggerScript(sub, report);
        sub.ScriptPath = scriptPath;
        await db.SaveChangesAsync();

        var jobDef = SubscriptionOrchestration.BuildJobDefinition(sub, report.Name, scriptPath);
        var orchDbPath = dbLocator.Resolve();
        if (orchestratorStoreFactory.Provider == DatabaseProvider.Postgres || orchDbPath is not null)
        {
            var store = orchestratorStoreFactory.Create(orchDbPath);
            await store.InitializeAsync();
            await store.SaveJobAsync(jobDef);
        }

        await audit.LogAsync(CurrentUserId, "CREATE_SUBSCRIPTION", "Subscription", sub.Id.ToString(), jobDef.Name);
        return CreatedAtAction(nameof(Get), new { id = sub.Id }, subscriptionQueries.ToDto(sub));
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
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, sub, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, subscriptionQueries.ToDto(sub));

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
        if (parametersChanged) sub.ParametersJson = SubscriptionQueryService.SerializeParams(req.Parameters);

        if (scriptNeedsRewrite && !string.IsNullOrEmpty(sub.ScriptPath))
        {
            if (!subscriptionScripts.TryResolve(sub.ScriptPath, out _))
                return Forbid();

            // Format/parameter/recipient changes live in the subscription row and are read by
            // the delivery service at send time; rewriting just heals any legacy script content.
            subscriptionScripts.WriteTriggerScript(sub, sub.Report);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(sub).ReloadAsync();
            return OptimisticConcurrency.Conflict(this, subscriptionQueries.ToDto(sub));
        }

        // Sync the Orchestrator job if schedule or active state changed
        var orchDbPath = dbLocator.Resolve();
        if ((orchestratorStoreFactory.Provider == DatabaseProvider.Postgres || orchDbPath is not null)
            && (scheduleChanged || req.IsActive.HasValue))
        {
            var store = orchestratorStoreFactory.Create(orchDbPath);
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

        await audit.LogAsync(CurrentUserId, "UPDATE_SUBSCRIPTION", "Subscription", sub.Id.ToString());
        OptimisticConcurrency.SetETag(Response, sub.Version);
        return Ok(subscriptionQueries.ToDto(sub));
    }

    [HttpPost("api/admin/subscriptions/bulk-status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkUpdateStatus([FromBody] BulkSubscriptionStatusRequest req)
    {
        var items = (req.Subscriptions ?? []).GroupBy(item => item.Id).Select(group => group.First()).ToList();
        if (items.Count == 0) return BadRequest(new { error = "Select at least one subscription." });

        var orchDbPath = dbLocator.Resolve();
        IJobHistoryStore? store = null;
        if (orchestratorStoreFactory.Provider == DatabaseProvider.Postgres || orchDbPath is not null)
        {
            store = orchestratorStoreFactory.Create(orchDbPath);
            await store.InitializeAsync();
        }

        var results = new List<BulkMutationResult>();
        var updated = 0;
        foreach (var item in items)
        {
            var sub = await db.Subscriptions
                .Include(value => value.Report)
                .FirstOrDefaultAsync(value => value.Id == item.Id);
            if (sub is null)
            {
                results.Add(new(item.Id, "NotFound"));
                continue;
            }
            if (!OptimisticConcurrency.Prepare(db, sub, item.Version))
            {
                results.Add(new(item.Id, "Conflict", sub.Version));
                continue;
            }

            sub.IsActive = req.IsActive;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                await db.Entry(sub).ReloadAsync();
                results.Add(new(item.Id, "Conflict", sub.Version));
                continue;
            }

            if (store is not null)
            {
                var jobName = SubscriptionOrchestration.JobName(sub.Id, sub.Report.Name);
                var job = await store.GetJobAsync(jobName);
                if (job is not null)
                    await store.SaveJobAsync(job with { IsEnabled = req.IsActive });
            }
            results.Add(new(item.Id, "Updated", sub.Version));
            updated++;
        }

        await audit.LogAsync(CurrentUserId, "BULK_UPDATE_SUBSCRIPTION_STATUS", "Subscription", null,
            $"{updated} subscriptions set active={req.IsActive}");
        return Ok(new { Updated = updated, Results = results });
    }

    [HttpDelete("api/subscriptions/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var sub = await db.Subscriptions.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == id);
        if (sub is null) return NotFound();
        if (!IsAdmin && sub.UserId != CurrentUserId) return Forbid();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, sub, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, subscriptionQueries.ToDto(sub));

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(sub).ReloadAsync();
            return OptimisticConcurrency.Conflict(this, subscriptionQueries.ToDto(sub));
        }

        var jobName = SubscriptionOrchestration.JobName(sub.Id, sub.Report?.Name);
        string? resolvedScriptPath = null;
        if (!string.IsNullOrEmpty(sub.ScriptPath)
            && !subscriptionScripts.TryResolve(sub.ScriptPath, out resolvedScriptPath))
            return Forbid();

        var orchDbPath = dbLocator.Resolve();
        if (orchestratorStoreFactory.Provider == DatabaseProvider.Postgres || orchDbPath is not null)
        {
            var store = orchestratorStoreFactory.Create(orchDbPath);
            await store.InitializeAsync();
            await store.DeleteJobAsync(jobName);
        }

        if (!string.IsNullOrEmpty(resolvedScriptPath) && System.IO.File.Exists(resolvedScriptPath))
            System.IO.File.Delete(resolvedScriptPath);

        db.Subscriptions.Remove(sub);
        audit.Stage(CurrentUserId, "DELETE_SUBSCRIPTION", "Subscription", id.ToString());
        await db.SaveChangesAsync();
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
            .Select(c => new SmtpConnectionDto(c.Id, c.Alias, c.Host, c.Port, c.Username, c.FromAddress, c.UseSsl, c.Version))
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
        return Ok(new SmtpConnectionDto(conn.Id, conn.Alias, conn.Host, conn.Port, conn.Username, conn.FromAddress, conn.UseSsl, conn.Version));
    }

    [HttpPut("api/admin/smtp/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateSmtp(int id, [FromBody] UpdateSmtpRequest req)
    {
        var conn = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, conn, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToSmtpDto(conn));

        if (req.Host is not null) conn.Host = req.Host;
        if (req.Port.HasValue) conn.Port = req.Port.Value;
        if (req.Username is not null) conn.Username = req.Username;
        if (req.Password is not null) conn.EncryptedPassword = pwdProtector.Protect(req.Password);
        if (req.FromAddress is not null) conn.FromAddress = req.FromAddress;
        if (req.UseSsl.HasValue) conn.UseSsl = req.UseSsl.Value;

        // SMTP definitions are credential-bearing: the change and its audit row share one
        // commit (P1.6). The detail never includes the password.
        audit.Stage(CurrentUserId, "UPDATE_SMTP", "SmtpConnection", id.ToString(),
            $"{conn.Alias}{(req.Password is not null ? " (password rotated)" : "")}");
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(conn).ReloadAsync();
            return OptimisticConcurrency.Conflict(this, ToSmtpDto(conn));
        }
        OptimisticConcurrency.SetETag(Response, conn.Version);
        return Ok(ToSmtpDto(conn));
    }

    [HttpDelete("api/admin/smtp/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteSmtp(int id)
    {
        var conn = await db.SmtpConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return NotFound();
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, conn, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToSmtpDto(conn));
        db.SmtpConnections.Remove(conn);
        audit.Stage(CurrentUserId, "DELETE_SMTP", "SmtpConnection", id.ToString());
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.SmtpConnections.FindAsync(id);
            return current is null ? NotFound() : OptimisticConcurrency.Conflict(this, ToSmtpDto(current));
        }
        return NoContent();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static SmtpConnectionDto ToSmtpDto(SmtpConnection connection) =>
        new(
            connection.Id,
            connection.Alias,
            connection.Host,
            connection.Port,
            connection.Username,
            connection.FromAddress,
            connection.UseSsl,
            connection.Version);

}
