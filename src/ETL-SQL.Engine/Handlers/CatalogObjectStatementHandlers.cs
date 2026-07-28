using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Scheduling;
using Microsoft.Extensions.Configuration;

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
    /// Applies a <c>SECRET:</c>-free identity for attribution. The Orchestrator has no identity model
    /// of its own, so this is who the calling host says is acting — attribution, never authorization.
    /// </summary>
    public static string? ActingIdentity(IExecutionContext context)
    {
        var user = context.ExecutionIdentity?.EffectiveUser;
        return string.IsNullOrWhiteSpace(user) ? null : user;
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
}

/// <summary>Handles <c>CREATE [OR ALTER|OR REPLACE] SCHEDULE</c>.</summary>
public class CreateScheduleStatementHandler(IJobCatalogStore? catalog = null, IConfiguration? configuration = null) : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreateScheduleStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateScheduleStatement)statement;
        var store = CatalogStatementSupport.Require(catalog, stmt, "CREATE SCHEDULE");

        var existing = await store.GetScheduleAsync(stmt.Name);
        if (existing is not null && stmt.Mode == ObjectCreationMode.Create)
            throw new ExecutionException(
                $"Schedule '{stmt.Name}' already exists. Use CREATE OR ALTER SCHEDULE to update it, " +
                $"CREATE OR REPLACE SCHEDULE to redefine it, or DROP SCHEDULE {stmt.Name} first.",
                null, stmt.Line, stmt.Column);

        // The zone is resolved and stored now, not read at each fire: otherwise editing the
        // configured default would silently move every schedule that relied on it.
        var timeZone = stmt.TimeZone ?? CatalogStatementSupport.DefaultTimeZone(configuration);
        CatalogStatementSupport.ValidateSchedule(stmt.Cron, timeZone, stmt);

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

        var existing = await store.GetNotificationAsync(stmt.Name);
        if (existing is not null && stmt.Mode == ObjectCreationMode.Create)
            throw new ExecutionException(
                $"Notification '{stmt.Name}' already exists. Use CREATE OR ALTER NOTIFICATION to " +
                $"update it, CREATE OR REPLACE NOTIFICATION to redefine it, or DROP NOTIFICATION " +
                $"{stmt.Name} first.",
                null, stmt.Line, stmt.Column);

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
            var existing = await store.GetScheduleAsync(stmt.Name)
                ?? throw NotFound(stmt, kind);

            var cron = stmt.Cron ?? existing.Cron;
            var timeZone = stmt.TimeZone ?? existing.TimeZone;
            CatalogStatementSupport.ValidateSchedule(cron, timeZone, stmt);

            await store.SaveScheduleAsync(existing with
            {
                Cron = cron,
                TimeZone = timeZone,
                DisplayName = stmt.Metadata.DisplayName ?? existing.DisplayName,
                Description = stmt.Metadata.Description ?? existing.Description,
                Options = CatalogStatementSupport.SerializeOptions(stmt.Metadata.Options) ?? existing.Options,
                ModifiedBy = identity
            });
        }
        else
        {
            var existing = await store.GetNotificationAsync(stmt.Name)
                ?? throw NotFound(stmt, kind);

            await store.SaveNotificationAsync(existing with
            {
                ConnectionName = stmt.ConnectionName ?? existing.ConnectionName,
                Recipient = stmt.Recipient ?? existing.Recipient,
                DisplayName = stmt.Metadata.DisplayName ?? existing.DisplayName,
                Description = stmt.Metadata.Description ?? existing.Description,
                Options = CatalogStatementSupport.SerializeOptions(stmt.Metadata.Options) ?? existing.Options,
                ModifiedBy = identity
            });
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

        var exists = stmt.Kind == CatalogObjectKind.Schedule
            ? await store.GetScheduleAsync(stmt.Name) is not null
            : await store.GetNotificationAsync(stmt.Name) is not null;

        if (!exists)
        {
            if (stmt.IfExists)
            {
                context.Log($"{kind} '{stmt.Name}' did not exist (IF EXISTS specified).");
                return;
            }
            throw new ExecutionException($"{kind} '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);
        }

        var blockers = stmt.Kind == CatalogObjectKind.Schedule
            ? await store.DeleteScheduleAsync(stmt.Name)
            : await store.DeleteNotificationAsync(stmt.Name);

        // Restrict, not cascade: dropping a shared object out from under the jobs that use it would
        // silently unschedule or silence them. Naming them is what makes the failure actionable.
        if (blockers.Count > 0)
            throw new ExecutionException(
                $"{kind} '{stmt.Name}' is still attached to {blockers.Count} job(s): " +
                $"{string.Join(", ", blockers.OrderBy(b => b, StringComparer.OrdinalIgnoreCase))}. " +
                $"Detach it with ALTER JOB <job> REMOVE {kind} {stmt.Name} before dropping it.",
                null, stmt.Line, stmt.Column);

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

        var matched = stmt.Kind == CatalogObjectKind.Schedule
            ? await store.SetScheduleEnabledAsync(stmt.Name, stmt.IsEnabled)
            : await store.SetNotificationEnabledAsync(stmt.Name, stmt.IsEnabled);

        if (!matched)
            throw new ExecutionException($"{kind} '{stmt.Name}' does not exist.", null, stmt.Line, stmt.Column);

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

        if (stmt.Kind == CatalogObjectKind.Schedule)
            await ApplyScheduleAsync(stmt, store, context);
        else
            await ApplyNotificationAsync(stmt, store, context);
    }

    private static async Task ApplyScheduleAsync(
        AlterJobAttachmentStatement stmt, IJobCatalogStore store, IExecutionContext context)
    {
        if (stmt.Action == JobAttachmentAction.Remove)
        {
            var removed = await store.RemoveJobScheduleAsync(stmt.JobName, stmt.TargetName);
            context.Log(removed
                ? $"Schedule '{stmt.TargetName}' detached from job '{stmt.JobName}'."
                : $"Schedule '{stmt.TargetName}' was not attached to job '{stmt.JobName}'; nothing to do.");
            return;
        }

        var schedule = await store.GetScheduleAsync(stmt.TargetName)
            ?? throw new ExecutionException(
                $"Schedule '{stmt.TargetName}' does not exist. Create it before attaching it to a job.",
                null, stmt.Line, stmt.Column);

        // The link is armed at the schedule's next occurrence, never left empty: an unarmed link is
        // dormant in this model, so a job attached without one would silently never run.
        var nextRun = CronSchedule.GetNextOccurrence(schedule.Cron, schedule.TimeZone);
        var added = await store.AddJobScheduleAsync(stmt.JobName, schedule.Name, nextRun);

        context.Log(added
            ? $"Job '{stmt.JobName}' now runs on schedule '{schedule.Name}'"
              + (nextRun is null
                  ? " — which has no further occurrence, so the link is dormant."
                  : $"; next run {nextRun:u}.")
            : $"Job '{stmt.JobName}' already runs on schedule '{schedule.Name}'; left as it is.");
    }

    private static async Task ApplyNotificationAsync(
        AlterJobAttachmentStatement stmt, IJobCatalogStore store, IExecutionContext context)
    {
        if (!Enum.TryParse<NotificationTrigger>(stmt.Trigger, ignoreCase: true, out var trigger))
            throw new ExecutionException(
                $"'{stmt.Trigger}' is not a notification trigger. Use SUCCESS, FAILURE, or COMPLETION.",
                null, stmt.Line, stmt.Column);

        if (stmt.Action == JobAttachmentAction.Remove)
        {
            var removed = await store.RemoveJobNotificationAsync(stmt.JobName, stmt.TargetName, trigger);
            context.Log(removed
                ? $"Notification '{stmt.TargetName}' ON {trigger.ToString().ToUpperInvariant()} detached from job '{stmt.JobName}'."
                : $"Notification '{stmt.TargetName}' ON {trigger.ToString().ToUpperInvariant()} was not attached to job '{stmt.JobName}'; nothing to do.");
            return;
        }

        var notification = await store.GetNotificationAsync(stmt.TargetName)
            ?? throw new ExecutionException(
                $"Notification '{stmt.TargetName}' does not exist. Create it before attaching it to a job.",
                null, stmt.Line, stmt.Column);

        try
        {
            var added = await store.AddJobNotificationAsync(stmt.JobName, notification.Name, trigger);
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
