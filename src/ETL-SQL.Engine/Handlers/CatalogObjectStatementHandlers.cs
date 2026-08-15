using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Shared plumbing for the scheduler-catalog statements (<c>SCHEDULE</c>, <c>NOTIFICATION</c>, and
/// the <c>ALTER JOB … ADD|REMOVE</c> attachments).
/// </summary>
internal static class CatalogStatementSupport
{
    /// <summary>
    /// The metadata bag is stored as JSON rather than as columns because none of it is ever read by
    /// the scheduler — it exists so an operator can label and classify an object whose name must not
    /// change. Anything the scheduler acts on gets a real column instead.
    /// </summary>
    public static string? SerializeOptions(Dictionary<string, string>? options) =>
        options is null || options.Count == 0 ? null : JsonSerializer.Serialize(options);

    /// <summary>
    /// Applies the host-verified principal key used for ownership and attribution.
    /// </summary>
    public static string? ActingIdentity(IExecutionContext context)
    {
        var user = context.ExecutionIdentity?.EffectiveUser;
        return string.IsNullOrWhiteSpace(user) ? null : user;
    }

    /// <summary>
    /// The tenant a script's object names are resolved in — the server-verified binding on the
    /// execution identity, or null for the unbound (Solo) scope. A statement naming an object always
    /// means <em>this tenant's</em> object; there is no cross-tenant addressing from script.
    /// </summary>
    public static string? ActingTenant(IExecutionContext context) => context.ExecutionIdentity?.TenantId;

    public static void DemandCreate(IExecutionContext context, Statement statement, string kind)
    {
        var authorizer = context.ServiceProvider.GetService<IOrchestratorObjectAuthorizer>();
        if (authorizer is null || authorizer.CanCreate(context.ExecutionIdentity)) return;
        throw new ExecutionException(
            $"The authenticated principal may not create {kind} objects on this Orchestrator.",
            null, statement.Line, statement.Column);
    }

    /// <summary>
    /// Demands one permission on an object the caller has already loaded. The object's surrogate id
    /// and tenant binding come from that definition rather than being re-resolved from the name here:
    /// a name identifies an object only within a tenant, so re-resolving would reintroduce exactly
    /// the ambiguity the surrogate id exists to remove. A definition with no id predates identity
    /// assignment and is refused rather than allowed.
    /// </summary>
    public static Task DemandAsync(
        IExecutionContext context, Statement statement, OrchestratorObjectKind kind, string name,
        JobId objectId, string? objectTenantId, OrchestratorObjectPermission permission, string? owner) =>
        DemandCoreAsync(context, statement, kind, name, objectId.ToString(), objectTenantId, permission, owner);

    public static Task DemandAsync(
        IExecutionContext context, Statement statement, OrchestratorObjectKind kind, string name,
        ScheduleId objectId, string? objectTenantId, OrchestratorObjectPermission permission, string? owner) =>
        DemandCoreAsync(context, statement, kind, name, objectId.ToString(), objectTenantId, permission, owner);

    public static Task DemandAsync(
        IExecutionContext context, Statement statement, OrchestratorObjectKind kind, string name,
        NotificationId objectId, string? objectTenantId, OrchestratorObjectPermission permission, string? owner) =>
        DemandCoreAsync(context, statement, kind, name, objectId.ToString(), objectTenantId, permission, owner);

    // Private, and reached only through the typed overloads above: the grant store keys heterogeneous
    // object kinds in one column so it takes a string, but nothing may hand it one directly, or a
    // name would once again be accepted where an identity belongs.
    private static async Task DemandCoreAsync(
        IExecutionContext context,
        Statement statement,
        OrchestratorObjectKind kind,
        string name,
        string? objectId,
        string? objectTenantId,
        OrchestratorObjectPermission permission,
        string? owner)
    {
        var authorizer = context.ServiceProvider.GetService<IOrchestratorObjectAuthorizer>();
        if (authorizer is null) return;
        if (!string.IsNullOrWhiteSpace(objectId) && await authorizer.CanAsync(
                context.ExecutionIdentity, kind, objectId, objectTenantId, permission, owner,
                context.CancellationToken))
            return;
        throw new ExecutionException(
            $"The authenticated principal lacks {permission.ToString().ToUpperInvariant()} authority on {kind.ToString().ToUpperInvariant()} '{name}'.",
            null, statement.Line, statement.Column);
    }

    /// <summary>
    /// Validates a cron expression and time zone, reporting failures at the offending statement.
    /// </summary>
    public static void ValidateSchedule(string cron, string timeZone, Statement statement)
    {
        try
        {
            CronSchedule.Validate(cron, timeZone);
        }
        catch (ArgumentException ex)
        {
            throw new ExecutionException(ex.Message, null, statement.Line, statement.Column);
        }
    }

    /// <summary>
    /// The configured default zone, resolved once when a schedule is written rather than at each
    /// fire — otherwise editing appsettings would silently move every schedule that relied on it.
    /// </summary>
    public static string DefaultTimeZone(IConfiguration? configuration)
    {
        var configured = configuration?["Scheduler:DefaultTimeZone"];
        return string.IsNullOrWhiteSpace(configured) ? CronSchedule.DefaultTimeZone : configured;
    }

    public static IJobCatalogStore Require(IJobCatalogStore? catalog, Statement statement, string what)
    {
        if (catalog is not null) return catalog;
        throw new ExecutionException(
            $"{what} needs an orchestrator catalog, and this host has none configured. Run it against " +
            "an orchestrator — EXECUTE <orchestrator> BEGIN … END — or configure a local job store.",
            null, statement.Line, statement.Column);
    }

    public static void AuditMutation(IExecutionContext context, string action, string target, string reason)
    {
        var policy = context.ExecutionPolicy;
        var actor = context.ExecutionIdentity?.RealUser
                    ?? context.ExecutionIdentity?.EffectiveUser
                    ?? policy?.Actor
                    ?? "system";
        var effective = context.ExecutionIdentity?.EffectiveUser
                        ?? policy?.Actor
                        ?? actor;
        SecurityEventRuntime.Emit(SecurityEventContract.Create(
            SecurityEventSeverity.Information,
            SecurityEventType.CatalogMutation,
            actor,
            effective,
            target,
            SecurityEventDecision.Allowed,
            $"{action}: {reason}") with
        {
            ScriptHash = policy?.ScriptHash,
            JobId = policy?.JobId,
            CorrelationId = policy?.CorrelationId,
            PolicyVersion = policy?.PolicyVersion,
            PolicyHash = policy?.PolicyHash
        });
    }
}

/// <summary>Handles <c>CREATE [OR ALTER|OR REPLACE] SCHEDULE</c>.</summary>
public class CreateScheduleStatementHandler(IJobCatalogStore? catalog = null, IConfiguration? configuration = null) : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreateScheduleStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateScheduleStatement)statement;
        var store = CatalogStatementSupport.Require(catalog, stmt, "CREATE SCHEDULE");

        var existing = await store.GetScheduleAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name);
        if (existing is not null && stmt.Mode == ObjectCreationMode.Create)
            throw new ExecutionException(
                $"Schedule '{stmt.Name}' already exists. Use CREATE OR ALTER SCHEDULE to update it, " +
                $"CREATE OR REPLACE SCHEDULE to redefine it, or DROP SCHEDULE {stmt.Name} first.",
                null, stmt.Line, stmt.Column);
        if (existing is null) CatalogStatementSupport.DemandCreate(context, stmt, "SCHEDULE");
        else await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Schedule, stmt.Name,
            existing.Id, existing.TenantId, OrchestratorObjectPermission.Manage, existing.CreatedBy);

        // The zone is resolved and stored now, not read at each fire: otherwise editing the
        // configured default would silently move every schedule that relied on it.
        var timeZone = stmt.TimeZone ?? CatalogStatementSupport.DefaultTimeZone(configuration);
        CatalogStatementSupport.ValidateSchedule(stmt.Cron, timeZone, stmt);

        if (context.IsWhatIf)
        {
            var action = existing is null ? "create" : stmt.Mode == ObjectCreationMode.CreateOrReplace ? "replace" : "alter";
            context.Log(
                $"WHAT IF: Would {action} schedule '{stmt.Name}' on '{stmt.Cron}' in time zone '{timeZone}'.",
                ConsoleColor.Yellow);
            return;
        }

        var identity = CatalogStatementSupport.ActingIdentity(context);
        await store.SaveScheduleAsync(new ScheduleDefinition(
            stmt.Name,
            stmt.Cron,
            timeZone,
            IsEnabled: existing?.IsEnabled ?? true,
            DisplayName: stmt.Metadata.DisplayName,
            Description: stmt.Metadata.Description,
            Options: CatalogStatementSupport.SerializeOptions(stmt.Metadata.Options),
            CreatedBy: identity,
            ModifiedBy: identity));

        CatalogStatementSupport.AuditMutation(
            context,
            existing is null ? "CREATE_SCHEDULE" : stmt.Mode == ObjectCreationMode.CreateOrReplace ? "REPLACE_SCHEDULE" : "ALTER_SCHEDULE",
            $"SCHEDULE:{stmt.Name}",
            $"Schedule '{stmt.Name}' {(existing is null ? "created" : "updated")}.");
        context.Log($"Schedule '{stmt.Name}' {(existing is null ? "created" : "updated")}.", ConsoleColor.Green);
    }
}

/// <summary>Handles <c>CREATE [OR ALTER|OR REPLACE] NOTIFICATION</c>.</summary>
public class CreateNotificationStatementHandler(IJobCatalogStore? catalog = null) : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreateNotificationStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateNotificationStatement)statement;
        var store = CatalogStatementSupport.Require(catalog, stmt, "CREATE NOTIFICATION");

        var existing = await store.GetNotificationAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name);
        if (existing is not null && stmt.Mode == ObjectCreationMode.Create)
            throw new ExecutionException(
                $"Notification '{stmt.Name}' already exists. Use CREATE OR ALTER NOTIFICATION to " +
                $"update it, CREATE OR REPLACE NOTIFICATION to redefine it, or DROP NOTIFICATION " +
                $"{stmt.Name} first.",
                null, stmt.Line, stmt.Column);
        if (existing is null) CatalogStatementSupport.DemandCreate(context, stmt, "NOTIFICATION");
        else await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Notification, stmt.Name,
            existing.Id, existing.TenantId, OrchestratorObjectPermission.Manage, existing.CreatedBy);

        if (context.IsWhatIf)
        {
            var action = existing is null ? "create" : stmt.Mode == ObjectCreationMode.CreateOrReplace ? "replace" : "alter";
            context.Log(
                $"WHAT IF: Would {action} notification '{stmt.Name}' using connection '{stmt.ConnectionName}'.",
                ConsoleColor.Yellow);
            return;
        }

        var identity = CatalogStatementSupport.ActingIdentity(context);
        await store.SaveNotificationAsync(new NotificationDefinition(
            stmt.Name,
            stmt.ConnectionName,
            stmt.Recipient,
            IsEnabled: existing?.IsEnabled ?? true,
            DisplayName: stmt.Metadata.DisplayName,
            Description: stmt.Metadata.Description,
            Options: CatalogStatementSupport.SerializeOptions(stmt.Metadata.Options),
            CreatedBy: identity,
            ModifiedBy: identity));

        CatalogStatementSupport.AuditMutation(
            context,
            existing is null ? "CREATE_NOTIFICATION" : stmt.Mode == ObjectCreationMode.CreateOrReplace ? "REPLACE_NOTIFICATION" : "ALTER_NOTIFICATION",
            $"NOTIFICATION:{stmt.Name}",
            $"Notification '{stmt.Name}' {(existing is null ? "created" : "updated")}.");
        context.Log($"Notification '{stmt.Name}' {(existing is null ? "created" : "updated")}.", ConsoleColor.Green);
    }
}

/// <summary>Handles <c>ALTER SCHEDULE</c> and <c>ALTER NOTIFICATION</c>.</summary>
public class AlterCatalogObjectStatementHandler(IJobCatalogStore? catalog = null) : IStatementHandler
{
    public Type SupportedStatementType => typeof(AlterCatalogObjectStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AlterCatalogObjectStatement)statement;
        var kind = stmt.Kind.ToString().ToUpperInvariant();
        var store = CatalogStatementSupport.Require(catalog, stmt, $"ALTER {kind}");
        var identity = CatalogStatementSupport.ActingIdentity(context);

        if (stmt.Kind == CatalogObjectKind.Schedule)
        {
            var existing = await store.GetScheduleAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name)
                ?? throw NotFound(stmt, kind);
            await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Schedule,
                stmt.Name, existing.Id, existing.TenantId,
                OrchestratorObjectPermission.Manage, existing.CreatedBy);

            var cron = stmt.Cron ?? existing.Cron;
            var timeZone = stmt.TimeZone ?? existing.TimeZone;
            CatalogStatementSupport.ValidateSchedule(cron, timeZone, stmt);

            if (context.IsWhatIf)
            {
                context.Log(
                    $"WHAT IF: Would alter schedule '{stmt.Name}' to cron '{cron}' in time zone '{timeZone}'.",
                    ConsoleColor.Yellow);
                return;
            }

            await store.SaveScheduleAsync(existing with
            {
                Cron = cron,
                TimeZone = timeZone,
                DisplayName = stmt.Metadata.DisplayName ?? existing.DisplayName,
                Description = stmt.Metadata.Description ?? existing.Description,
                Options = CatalogStatementSupport.SerializeOptions(stmt.Metadata.Options) ?? existing.Options,
                ModifiedBy = identity
            });
            CatalogStatementSupport.AuditMutation(
                context,
                "ALTER_SCHEDULE",
                $"SCHEDULE:{stmt.Name}",
                $"Schedule '{stmt.Name}' updated.");
        }
        else
        {
            var existing = await store.GetNotificationAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name)
                ?? throw NotFound(stmt, kind);
            await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Notification,
                stmt.Name, existing.Id, existing.TenantId,
                OrchestratorObjectPermission.Manage, existing.CreatedBy);

            if (context.IsWhatIf)
            {
                context.Log(
                    $"WHAT IF: Would alter notification '{stmt.Name}'.",
                    ConsoleColor.Yellow);
                return;
            }

            await store.SaveNotificationAsync(existing with
            {
                ConnectionName = stmt.ConnectionName ?? existing.ConnectionName,
                Recipient = stmt.Recipient ?? existing.Recipient,
                DisplayName = stmt.Metadata.DisplayName ?? existing.DisplayName,
                Description = stmt.Metadata.Description ?? existing.Description,
                Options = CatalogStatementSupport.SerializeOptions(stmt.Metadata.Options) ?? existing.Options,
                ModifiedBy = identity
            });
            CatalogStatementSupport.AuditMutation(
                context,
                "ALTER_NOTIFICATION",
                $"NOTIFICATION:{stmt.Name}",
                $"Notification '{stmt.Name}' updated.");
        }

        context.Log($"{kind} '{stmt.Name}' updated.", ConsoleColor.Green);
    }

    /// <summary>
    /// Names cannot be renamed in this model, so a name that does not resolve is either a typo or a
    /// missing object. Saying so beats "not found", which reads as the second when it is usually the
    /// first.
    /// </summary>
    private static ExecutionException NotFound(AlterCatalogObjectStatement stmt, string kind) =>
        new($"{kind} '{stmt.Name}' does not exist. Names are case-insensitive and are never renamed, " +
            $"so check the spelling or CREATE {kind} {stmt.Name} first.",
            null, stmt.Line, stmt.Column);
}

/// <summary>Handles <c>DROP SCHEDULE|NOTIFICATION [IF EXISTS]</c>.</summary>
public class DropCatalogObjectStatementHandler(IJobCatalogStore? catalog = null) : IStatementHandler
{
    public Type SupportedStatementType => typeof(DropCatalogObjectStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (DropCatalogObjectStatement)statement;
        var kind = stmt.Kind.ToString().ToUpperInvariant();
        var store = CatalogStatementSupport.Require(catalog, stmt, $"DROP {kind}");

        var schedule = stmt.Kind == CatalogObjectKind.Schedule ? await store.GetScheduleAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name) : null;
        var notification = stmt.Kind == CatalogObjectKind.Notification ? await store.GetNotificationAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name) : null;
        var exists = schedule is not null || notification is not null;

        if (!exists)
        {
            if (stmt.IfExists)
            {
                context.Log($"{kind} '{stmt.Name}' did not exist (IF EXISTS specified).");
                return;
            }
            throw new ExecutionException($"{kind} '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);
        }
        if (schedule is not null)
            await CatalogStatementSupport.DemandAsync(
                context, stmt, OrchestratorObjectKind.Schedule, stmt.Name,
                schedule.Id, schedule.TenantId, OrchestratorObjectPermission.Manage, schedule.CreatedBy);
        else
            await CatalogStatementSupport.DemandAsync(
                context, stmt, OrchestratorObjectKind.Notification, stmt.Name,
                notification!.Id, notification.TenantId, OrchestratorObjectPermission.Manage, notification.CreatedBy);

        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would drop {kind.ToLowerInvariant()} '{stmt.Name}'.", ConsoleColor.Yellow);
            return;
        }

        // Deleted by the identity the name resolved to a moment ago, not by the name again.
        var blockers = schedule is not null
            ? await store.DeleteScheduleAsync(schedule.Id)
            : await store.DeleteNotificationAsync(notification!.Id);

        // Restrict, not cascade: dropping a shared object out from under the jobs that use it would
        // silently unschedule or silence them. Naming them is what makes the failure actionable.
        if (blockers.Count > 0)
            throw new ExecutionException(
                $"{kind} '{stmt.Name}' is still attached to {blockers.Count} job(s): " +
                $"{string.Join(", ", blockers.OrderBy(b => b, StringComparer.OrdinalIgnoreCase))}. " +
                $"Detach it with ALTER JOB <job> REMOVE {kind} {stmt.Name} before dropping it.",
                null, stmt.Line, stmt.Column);

        // Grants are retired by the id captured before the delete, so nothing depends on the name
        // still resolving — and an object later created with the same name gets none of them.
        var droppedId = schedule is not null ? schedule.Id.ToString() : notification!.Id.ToString();
        if (context.ServiceProvider.GetService<IOrchestratorAuthorizationStore>() is { } grants
            && droppedId.Length > 0)
            await grants.DeleteObjectGrantsAsync(droppedId, context.CancellationToken);

        CatalogStatementSupport.AuditMutation(
            context,
            $"DROP_{kind}",
            $"{kind}:{stmt.Name}",
            $"{kind} '{stmt.Name}' dropped.");
        context.Log($"{kind} '{stmt.Name}' dropped.", ConsoleColor.Green);
    }
}

/// <summary>Handles <c>ENABLE|DISABLE SCHEDULE|NOTIFICATION</c>.</summary>
public class SetCatalogObjectEnabledStatementHandler(IJobCatalogStore? catalog = null) : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetCatalogObjectEnabledStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetCatalogObjectEnabledStatement)statement;
        var kind = stmt.Kind.ToString().ToUpperInvariant();
        var verb = stmt.IsEnabled ? "ENABLE" : "DISABLE";
        var store = CatalogStatementSupport.Require(catalog, stmt, $"{verb} {kind}");

        var schedule = stmt.Kind == CatalogObjectKind.Schedule ? await store.GetScheduleAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name) : null;
        var notification = stmt.Kind == CatalogObjectKind.Notification ? await store.GetNotificationAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name) : null;
        var exists = schedule is not null || notification is not null;
        if (!exists)
            throw new ExecutionException($"{kind} '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);
        if (schedule is not null)
            await CatalogStatementSupport.DemandAsync(
                context, stmt, OrchestratorObjectKind.Schedule, stmt.Name,
                schedule.Id, schedule.TenantId, OrchestratorObjectPermission.Manage, schedule.CreatedBy);
        else
            await CatalogStatementSupport.DemandAsync(
                context, stmt, OrchestratorObjectKind.Notification, stmt.Name,
                notification!.Id, notification.TenantId, OrchestratorObjectPermission.Manage, notification.CreatedBy);

        if (context.IsWhatIf)
        {
            context.Log(
                $"WHAT IF: Would {(stmt.IsEnabled ? "enable" : "disable")} {kind.ToLowerInvariant()} '{stmt.Name}'.",
                ConsoleColor.Yellow);
            return;
        }

        var matched = schedule is not null
            ? await store.SetScheduleEnabledAsync(schedule.Id, stmt.IsEnabled)
            : await store.SetNotificationEnabledAsync(notification!.Id, stmt.IsEnabled);

        if (!matched)
            throw new ExecutionException($"{kind} '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);

        CatalogStatementSupport.AuditMutation(
            context,
            $"{verb}_{kind}",
            $"{kind}:{stmt.Name}",
            $"{kind} '{stmt.Name}' {(stmt.IsEnabled ? "enabled" : "disabled")}.");
        context.Log($"{kind} '{stmt.Name}' {(stmt.IsEnabled ? "enabled" : "disabled")}.", ConsoleColor.Green);
    }
}

/// <summary>Handles <c>ALTER JOB … ADD|REMOVE SCHEDULE|NOTIFICATION</c>.</summary>
/// <remarks>
/// Both directions are idempotent, and the messages say so. An exported configuration script
/// re-issues every attachment on replay, so "already attached" is the expected outcome of a normal
/// import rather than a problem to report as one.
/// </remarks>
public class AlterJobAttachmentStatementHandler(IJobCatalogStore? catalog = null) : IStatementHandler
{
    public Type SupportedStatementType => typeof(AlterJobAttachmentStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AlterJobAttachmentStatement)statement;
        var kind = stmt.Kind.ToString().ToUpperInvariant();
        var verb = stmt.Action.ToString().ToUpperInvariant();
        var store = CatalogStatementSupport.Require(catalog, stmt, $"ALTER JOB … {verb} {kind}");

        // The job is resolved once, here, whether or not an authorizer is configured: the link tables
        // are keyed by identity, so a name that never resolved would previously have produced a
        // successful-looking statement that attached nothing.
        var jobs = context.ServiceProvider.GetService<IJobHistoryStore>() ?? store as IJobHistoryStore
            ?? throw new ExecutionException("The shared Orchestrator job store is unavailable.", null, stmt.Line, stmt.Column);
        var job = await jobs.GetJobAsync(CatalogStatementSupport.ActingTenant(context), stmt.JobName)
            ?? throw new ExecutionException($"Job '{stmt.JobName}' does not exist.", null, stmt.Line, stmt.Column);
        if (context.ServiceProvider.GetService<IOrchestratorObjectAuthorizer>() is not null)
            await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Job,
                job.Name, job.Id, job.TenantId, OrchestratorObjectPermission.Manage, job.CreatedBy);

        if (context.IsWhatIf)
        {
            context.Log(
                $"WHAT IF: Would {verb.ToLowerInvariant()} {kind.ToLowerInvariant()} '{stmt.TargetName}' " +
                $"{(stmt.Action == JobAttachmentAction.Add ? "to" : "from")} job '{stmt.JobName}'.",
                ConsoleColor.Yellow);
            return;
        }

        if (stmt.Kind == CatalogObjectKind.Schedule)
            await ApplyScheduleAsync(stmt, job.Id, store, context);
        else
            await ApplyNotificationAsync(stmt, job.Id, store, context);
    }

    private static async Task ApplyScheduleAsync(
        AlterJobAttachmentStatement stmt, JobId jobId, IJobCatalogStore store, IExecutionContext context)
    {
        if (stmt.Action == JobAttachmentAction.Remove)
        {
            // Detaching resolves the schedule too, but tolerates a name that does not: "remove what is
            // not there" is a no-op in this model, not an error.
            var attached = await store.GetScheduleAsync(CatalogStatementSupport.ActingTenant(context), stmt.TargetName);
            var removed = attached is not null && await store.RemoveJobScheduleAsync(jobId, attached.Id);
            if (removed)
                CatalogStatementSupport.AuditMutation(
                    context,
                    "DETACH_SCHEDULE",
                    $"JOB:{stmt.JobName}/SCHEDULE:{stmt.TargetName}",
                    $"Schedule '{stmt.TargetName}' detached from job '{stmt.JobName}'.");
            context.Log(removed
                ? $"Schedule '{stmt.TargetName}' detached from job '{stmt.JobName}'."
                : $"Schedule '{stmt.TargetName}' was not attached to job '{stmt.JobName}'; nothing to do.");
            return;
        }

        var schedule = await store.GetScheduleAsync(CatalogStatementSupport.ActingTenant(context), stmt.TargetName)
            ?? throw new ExecutionException(
                $"Schedule '{stmt.TargetName}' does not exist. Create it before attaching it to a job.",
                null, stmt.Line, stmt.Column);
        await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Schedule,
            schedule.Name, schedule.Id, schedule.TenantId,
            OrchestratorObjectPermission.Read, schedule.CreatedBy);

        // The link is armed at the schedule's next occurrence, never left empty: an unarmed link is
        // dormant in this model, so a job attached without one would silently never run.
        var nextRun = CronSchedule.GetNextOccurrence(schedule.Cron, schedule.TimeZone);
        var added = await store.AddJobScheduleAsync(jobId, schedule.Id, nextRun);
        if (added)
            CatalogStatementSupport.AuditMutation(
                context,
                "ATTACH_SCHEDULE",
                $"JOB:{stmt.JobName}/SCHEDULE:{schedule.Name}",
                $"Schedule '{schedule.Name}' attached to job '{stmt.JobName}'.");

        context.Log(added
            ? $"Job '{stmt.JobName}' now runs on schedule '{schedule.Name}'"
              + (nextRun is null
                  ? " — which has no further occurrence, so the link is dormant."
                  : $"; next run {nextRun:u}.")
            : $"Job '{stmt.JobName}' already runs on schedule '{schedule.Name}'; left as it is.");
    }

    private static async Task ApplyNotificationAsync(
        AlterJobAttachmentStatement stmt, JobId jobId, IJobCatalogStore store, IExecutionContext context)
    {
        if (!Enum.TryParse<NotificationTrigger>(stmt.Trigger, ignoreCase: true, out var trigger))
            throw new ExecutionException(
                $"'{stmt.Trigger}' is not a notification trigger. Use SUCCESS, FAILURE, or COMPLETION.",
                null, stmt.Line, stmt.Column);

        if (stmt.Action == JobAttachmentAction.Remove)
        {
            var attached = await store.GetNotificationAsync(CatalogStatementSupport.ActingTenant(context), stmt.TargetName);
            var removed = attached is not null
                && await store.RemoveJobNotificationAsync(jobId, attached.Id, trigger);
            if (removed)
                CatalogStatementSupport.AuditMutation(
                    context,
                    "DETACH_NOTIFICATION",
                    $"JOB:{stmt.JobName}/NOTIFICATION:{stmt.TargetName}/ON:{trigger.ToString().ToUpperInvariant()}",
                    $"Notification '{stmt.TargetName}' ON {trigger.ToString().ToUpperInvariant()} detached from job '{stmt.JobName}'.");
            context.Log(removed
                ? $"Notification '{stmt.TargetName}' ON {trigger.ToString().ToUpperInvariant()} detached from job '{stmt.JobName}'."
                : $"Notification '{stmt.TargetName}' ON {trigger.ToString().ToUpperInvariant()} was not attached to job '{stmt.JobName}'; nothing to do.");
            return;
        }

        var notification = await store.GetNotificationAsync(CatalogStatementSupport.ActingTenant(context), stmt.TargetName)
            ?? throw new ExecutionException(
                $"Notification '{stmt.TargetName}' does not exist. Create it before attaching it to a job.",
                null, stmt.Line, stmt.Column);
        await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Notification,
            notification.Name, notification.Id, notification.TenantId,
            OrchestratorObjectPermission.Read, notification.CreatedBy);

        try
        {
            var added = await store.AddJobNotificationAsync(jobId, notification.Id, trigger);
            if (added)
                CatalogStatementSupport.AuditMutation(
                    context,
                    "ATTACH_NOTIFICATION",
                    $"JOB:{stmt.JobName}/NOTIFICATION:{notification.Name}/ON:{trigger.ToString().ToUpperInvariant()}",
                    $"Notification '{notification.Name}' ON {trigger.ToString().ToUpperInvariant()} attached to job '{stmt.JobName}'.");
            context.Log(added
                ? $"Job '{stmt.JobName}' now notifies '{notification.Name}' ON {trigger.ToString().ToUpperInvariant()}."
                : $"Job '{stmt.JobName}' already notifies '{notification.Name}' ON {trigger.ToString().ToUpperInvariant()}; left as it is.");
        }
        catch (InvalidOperationException ex)
        {
            // Overlapping triggers would deliver twice for one run. The store detects it; surfacing
            // it as a statement error is what makes it visible where it can still be fixed.
            throw new ExecutionException(ex.Message, null, stmt.Line, stmt.Column);
        }
    }
}
