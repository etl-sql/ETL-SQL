using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Service;

/// <summary>
/// Runs addressed by identity. A job is the grantable slot; a caller with EXECUTE on it submits a
/// script revision for one run and follows or cancels that run by its history id.
///
///   POST /api/scheduled-jobs/{name}/runs   — run one submitted revision through the job
///   GET  /api/job-runs/{historyId}         — status and bounded statement log of one run
///   POST /api/job-runs/{historyId}/cancel  — cancel that run
/// </summary>
public static partial class JobApiEndpoints
{
    /// <summary>Upper bound on one submitted revision, in characters.</summary>
    internal const int MaxRevisionCharacters = 1_000_000;

    /// <summary>Statements returned with a run. The store already keeps failures and the slowest.</summary>
    internal const int MaxRunLogStatements = 200;

    private static void MapJobRevisionRuns(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/scheduled-jobs/{name}/runs", async (HttpContext ctx, string name,
            RunJobRevisionRequest req, SchedulerService scheduler, IJobHistoryStore store,
            OrchestratorObjectAuthorizationService authorization, IConfiguration cfg,
            ILogger<OrchestratorHostedService> logger) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.ScriptText))
                return Results.BadRequest(new { Error = "ScriptText is required." });
            if (req.ScriptText.Length > MaxRevisionCharacters)
                return Results.BadRequest(new { Error = $"ScriptText exceeds the {MaxRevisionCharacters} character limit." });
            if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var unescaped = Uri.UnescapeDataString(name);
            var caller = RequestCaller(ctx);
            var job = await store.GetJobAsync(requestTenant, unescaped);
            if (job is null)
            {
                // Same answer whether the caller forgot to ask or may not create: no job is provisioned.
                if (!req.ProvisionIfMissing || !OrchestratorObjectAuthorizationService.CanCreate(caller))
                    return Results.Json(
                        new RunRefusal("job_not_provisioned",
                            $"No Orchestrator job named '{unescaped}' is provisioned for this caller."),
                        statusCode: req.ProvisionIfMissing
                            ? StatusCodes.Status403Forbidden
                            : StatusCodes.Status404NotFound);

                // Enabled, unscheduled and never due: the slot exists to be run on request. Its stored
                // script is the revision that provisioned it; later runs supply their own text.
                await store.SaveJobAsync(new JobDefinition(
                    unescaped, req.ScriptText, 1, "DAY", null, null, SchedulerService.ManualOnlyNextRun,
                    HashPolicy: "Off",
                    DisplayName: string.IsNullOrWhiteSpace(req.DisplayName) ? unescaped : req.DisplayName.Trim(),
                    Description: string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                    CreatedBy: caller.PrincipalKey,
                    ModifiedBy: ReadActor(ctx),
                    TenantId: requestTenant));
                job = await store.GetJobAsync(requestTenant, unescaped);
                if (job is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                EmitObjectMutationAudit(
                    caller, $"JOB:{unescaped}", "CREATE_JOB",
                    $"Job '{unescaped}' provisioned as a run slot.", ctx.TraceIdentifier);
            }

            if (!await authorization.CanAsync(
                    caller, OrchestratorObjectKind.Job, job.Id, job.TenantId,
                    OrchestratorObjectPermission.Execute, job.CreatedBy, ctx.RequestAborted))
                return Results.Json(
                    new RunRefusal("run_grant_denied",
                        $"This caller holds no EXECUTE grant on Orchestrator job '{unescaped}'."),
                    statusCode: StatusCodes.Status403Forbidden);

            var result = await scheduler.RunRevisionAsync(
                job.Id, req.ScriptText, ToExecutionIdentity(caller), caller.PrincipalKey, ctx.RequestAborted);
            switch (result.Status)
            {
                case RevisionRunStatus.JobNotFound:
                    return Results.NotFound(new RunRefusal("job_not_provisioned", $"Job '{unescaped}' not found."));
                case RevisionRunStatus.AlreadyRunning:
                    return Results.Conflict(new RunRefusal("already_running",
                        $"Job '{unescaped}' already has an execution in progress. Retry after it finishes."));
                case RevisionRunStatus.NodeOverloaded:
                    ctx.Response.Headers.RetryAfter = "30";
                    return Results.Json(
                        new RunRefusal("node_overloaded",
                            $"Job '{unescaped}' was not started: this Orchestrator node is overloaded. Retry shortly."),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                case RevisionRunStatus.NotStarted:
                    return Results.Json(
                        new RunRefusal("not_started",
                            $"Job '{unescaped}' did not start a run. Another Orchestrator instance may hold it; retry shortly."),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var scriptHash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(req.ScriptText))).ToLowerInvariant();
            logger.LogInformation(
                "Job {JobName}: revision {ScriptHash} submitted by {Actor} as run {RunId}.",
                unescaped, scriptHash, RequestActor(ctx), result.RunId);
            EmitObjectMutationAudit(
                caller, $"JOB:{unescaped}", "RUN_JOB_REVISION",
                $"Run {result.RunId} of job '{unescaped}' started from submitted revision {scriptHash}.",
                ctx.TraceIdentifier, SecurityEventSeverity.Warning);
            return Results.Accepted($"/api/job-runs/{result.RunId}",
                new RunAccepted(result.RunId!.Value, unescaped, scriptHash));
        }).WithName("runScheduledJobRevision");

        app.MapGet("/api/job-runs/{historyId:long}", async (HttpContext ctx, long historyId,
            IJobHistoryStore store, OrchestratorObjectAuthorizationService authorization,
            IConfiguration cfg) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            var (history, job) = await ResolveRunAsync(ctx, cfg, store, historyId);
            // One answer for "no such run" and "another tenant's run".
            if (history is null || job is null)
                return Results.NotFound(new { Error = $"Run '{historyId}' not found." });
            if (!await authorization.CanAsync(
                    RequestCaller(ctx), OrchestratorObjectKind.Job, job.Id, job.TenantId,
                    OrchestratorObjectPermission.Read, job.CreatedBy, ctx.RequestAborted))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var statements = await store.GetJobStatementMetricsAsync(historyId);
            return Results.Ok(new JobRunResponse(
                history.Id,
                job.Name,
                history.Status,
                IsTerminalRunStatus(history),
                history.StartTime,
                history.EndTime,
                history.ErrorMessage,
                history.RowsProcessed,
                history.ScriptHashAtRunTime,
                statements.Count > MaxRunLogStatements,
                [.. statements.Take(MaxRunLogStatements).Select(statement => new JobRunStatement(
                    statement.Statement, statement.DurationMs, statement.RowsProcessed, statement.Failed))]));
        }).WithName("getScheduledJobRun");

        app.MapPost("/api/job-runs/{historyId:long}/cancel", async (HttpContext ctx, long historyId,
            SchedulerService scheduler, IJobHistoryStore store,
            OrchestratorObjectAuthorizationService authorization, IConfiguration cfg,
            ILogger<OrchestratorHostedService> logger) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            var (history, job) = await ResolveRunAsync(ctx, cfg, store, historyId);
            if (history is null || job is null)
                return Results.NotFound(new { Error = $"Run '{historyId}' not found." });

            var caller = RequestCaller(ctx);
            if (!await authorization.CanAsync(
                    caller, OrchestratorObjectKind.Job, job.Id, job.TenantId,
                    OrchestratorObjectPermission.Read, job.CreatedBy, ctx.RequestAborted))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            // Anyone who can read the run may be told it is over. Who submitted it is only known
            // while it runs, so this comes before the ownership check below.
            if (IsTerminalRunStatus(history))
                return Results.Conflict(new RunCancelRefusal(
                    "run_already_finished", $"Run {historyId} already finished as {history.Status}.", history.Status));

            // MANAGE stops any run of the job. EXECUTE stops only the run that caller submitted:
            // being allowed to start work is not being allowed to stop a colleague's.
            var manages = await authorization.CanAsync(
                caller, OrchestratorObjectKind.Job, job.Id, job.TenantId,
                OrchestratorObjectPermission.Manage, job.CreatedBy, ctx.RequestAborted);
            var ownsRun = !manages
                && string.Equals(scheduler.GetRunSubmitter(historyId), caller.PrincipalKey, StringComparison.OrdinalIgnoreCase)
                && await authorization.CanAsync(
                    caller, OrchestratorObjectKind.Job, job.Id, job.TenantId,
                    OrchestratorObjectPermission.Execute, job.CreatedBy, ctx.RequestAborted);
            if (!manages && !ownsRun)
                return Results.Json(
                    new RunRefusal("cancel_denied",
                        $"This caller did not submit run {historyId} and holds no MANAGE grant on job '{job.Name}'."),
                    statusCode: StatusCodes.Status403Forbidden);

            if (!scheduler.CancelRun(historyId))
            {
                // Either it finished in the last moment or another node is running it. Say which.
                var current = await store.GetHistoryEntryAsync(historyId) ?? history;
                return IsTerminalRunStatus(current)
                    ? Results.Conflict(new RunCancelRefusal(
                        "run_already_finished", $"Run {historyId} already finished as {current.Status}.", current.Status))
                    : Results.Conflict(new RunCancelRefusal(
                        "not_running_here",
                        $"Run {historyId} is recorded as running, but not on this Orchestrator node, so it could not be signalled.",
                        current.Status));
            }

            logger.LogWarning("Run {HistoryId} of job {JobName} cancelled by {Actor}.", historyId, job.Name, RequestActor(ctx));
            EmitObjectMutationAudit(
                caller, $"JOB:{job.Name}", "CANCEL_JOB_RUN",
                $"Cancellation requested for run {historyId} of job '{job.Name}'.",
                ctx.TraceIdentifier, SecurityEventSeverity.Warning);
            // Accepted, not done: the run records CANCELLED when it actually stops.
            return Results.Accepted($"/api/job-runs/{historyId}", new RunCancelAccepted(historyId, job.Name));
        }).WithName("cancelScheduledJobRun");
    }

    /// <summary>
    /// A run and the job it belongs to, both resolved inside the caller's tenant. The job is found by
    /// the run's own job id where it has one, so a later object that took the name is not mistaken
    /// for the run's owner.
    /// </summary>
    private static async Task<(JobHistoryEntry? History, JobDefinition? Job)> ResolveRunAsync(
        HttpContext ctx, IConfiguration cfg, IJobHistoryStore store, long historyId)
    {
        if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant)) return (null, null);
        var history = await store.GetHistoryEntryAsync(historyId);
        if (history is null) return (null, null);
        var job = await store.GetJobAsync(requestTenant, history.JobName);
        if (job is null || (history.JobId.IsAssigned && job.Id != history.JobId)) return (null, null);
        return (history, job);
    }

    private static bool IsTerminalRunStatus(JobHistoryEntry history) =>
        history.EndTime is not null
        || !string.Equals(history.Status, "RUNNING", StringComparison.OrdinalIgnoreCase);

    private sealed record RunJobRevisionRequest(
        string ScriptText,
        bool ProvisionIfMissing = false,
        string? DisplayName = null,
        string? Description = null);

    /// <summary>A refusal a client can act on: <see cref="Code"/> is stable, <see cref="Error"/> is for people.</summary>
    public sealed record RunRefusal(string Code, string Error);

    public sealed record RunAccepted(long RunId, string JobName, string ScriptHash);

    public sealed record RunCancelAccepted(long RunId, string JobName);

    /// <summary>Why a run was not signalled, with the status it holds now.</summary>
    public sealed record RunCancelRefusal(string Code, string Error, string Status);

    public sealed record JobRunStatement(string Statement, long DurationMs, long RowsProcessed, bool Failed);

    public sealed record JobRunResponse(
        long RunId,
        string JobName,
        string Status,
        bool IsTerminal,
        DateTime StartTime,
        DateTime? EndTime,
        string? ErrorMessage,
        long RowsProcessed,
        string? ScriptHash,
        bool LogTruncated,
        IReadOnlyList<JobRunStatement> Statements);
}
