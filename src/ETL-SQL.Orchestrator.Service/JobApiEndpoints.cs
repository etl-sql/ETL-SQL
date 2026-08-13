using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Core.Observability;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Reporting;
using ETL_SQL.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Service
{
    /// <summary>
    /// Minimal-API endpoints exposed by the Orchestrator Service over HTTP.
    ///
    /// Ad-hoc execution routes (all require X-Orchestrator-Key header):
    ///   POST   /jobs          — submit a script for ad-hoc execution
    ///   DELETE /jobs/{id}     — cancel a running or queued ad-hoc job
    ///   GET    /jobs/{id}     — get the status of an ad-hoc job
    ///   GET    /health        — liveness probe (always 200 OK, no auth)
    ///   GET    /metrics       — concurrency metrics (no auth)
    ///
    /// Scheduled job management (all require X-Orchestrator-Key header):
    ///   GET    /api/scheduled-jobs              — list all jobs (enabled + disabled)
    ///   POST   /api/scheduled-jobs              — create a job
    ///   PUT    /api/scheduled-jobs/{name}       — update a job (enable/disable/reschedule)
    ///   DELETE /api/scheduled-jobs/{name}       — delete job and its history
    ///   GET    /api/scheduled-jobs/{name}/history — execution history for a job
    ///   POST   /api/scheduled-jobs/{name}/trigger — trigger an immediate out-of-schedule run
    ///   POST   /api/scheduled-jobs/{name}/kill    — cancel the currently running instance
    ///   GET    /api/data-quality/status           — canonical counts-only run quality status
    ///   GET    /api/data-quality/failures         — normalized rule failure counts
    ///   GET    /api/scripts                     — list .etlsql files under ScriptRoot
    ///   GET    /api/scripts/content             — read script file content (?path=relative)
    ///
    /// Service management (require X-Orchestrator-Key header):
    ///   GET    /management/status  — uptime, version, process info
    ///   POST   /management/stop    — graceful stop (OS supervisor restarts)
    /// </summary>
    public static class JobApiEndpoints
    {
        private static readonly ConcurrentDictionary<string, JobEntry> _jobs = new();
        private static readonly DateTime _startTime = DateTime.UtcNow;

        public static void MapJobApi(this IEndpointRouteBuilder app)
        {
            // ── Health & Metrics (no auth required — liveness/readiness probes) ─
            app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }))
               .WithName("health");

            app.MapGet("/metrics", (SchedulerService scheduler, ChildProcessTracker tracker) =>
            {
                var m = scheduler.GetMetrics();
                return Results.Ok(new
                {
                    active_jobs = m.ActiveJobs,
                    queued_jobs = m.QueuedJobs,
                    max_jobs = m.MaxJobs,
                    available_slots = m.AvailableSlots,
                    active_processes = tracker.ActiveCount
                });
            }).WithName("getMetrics");

            app.MapGet("/metrics/prometheus", async (
                SchedulerService scheduler,
                ChildProcessTracker tracker,
                IJobHistoryStore historyStore,
                IServiceProvider services) =>
            {
                var m = scheduler.GetMetrics();
                return Results.Text(
                    await BuildPrometheusMetricsAsync(
                        m,
                        tracker.ActiveCount,
                        historyStore,
                        services.GetService<IHostMetricsStore>()),
                    "text/plain; version=0.0.4; charset=utf-8");
            }).WithName("getPrometheusMetrics");

            // Platform lifecycle is deliberately not a tenant-admin API. The ordinary API key is
            // necessary but insufficient: the Portal must also issue a short-lived signed identity
            // assertion whose subject type and role identify attributed platform automation and
            // whose tenant is fixed by the signed organization-policy grant.
            app.MapPost("/api/platform/shared-tenants/lifecycle", async (
                HttpContext ctx,
                IConfiguration cfg,
                ISharedTenantLifecycleStore store,
                ISandboxAdmissionLedger admissions,
                SharedTenantLifecycleRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var caller = RequestCaller(ctx);
                if (!string.Equals(caller.SubjectType, "platform", StringComparison.Ordinal)
                    || !caller.IsInRole("PlatformLifecycle")
                    || string.IsNullOrWhiteSpace(caller.TenantId))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                try
                {
                    if (!Enum.TryParse<SharedTenantLifecycleKind>(request.Kind, true, out var kind))
                        return Results.BadRequest(new { Error = "Lifecycle kind must be Provision, Upgrade, or Delete." });
                    var tenantContext = TenantContext.FromVerifiedCredential(caller.TenantId);
                    var activeAdmissions = 0;
                    if (kind != SharedTenantLifecycleKind.Provision)
                    {
                        await admissions.CancelTenantQueuedAsync(tenantContext, cancellationToken);
                        activeAdmissions = (await admissions.ListTenantOpenAsync(
                                tenantContext, cancellationToken))
                            .Count(value => value.State is SandboxAdmissionState.Active
                                or SandboxAdmissionState.Retained);
                    }
                    var result = await store.ApplySharedTenantLifecycleAsync(
                        tenantContext,
                        new SharedTenantLifecycleCommand(
                            request.OperationId, kind, caller.SubjectId,
                            request.AuthorizationReference, request.TargetRelease,
                            request.MaxConcurrentJobs, request.MaxStorageMb,
                            request.MaxReportSessions, DateTimeOffset.UtcNow,
                            activeAdmissions),
                        cancellationToken);
                    if (kind == SharedTenantLifecycleKind.Delete && result.Status == "Completed")
                        await admissions.PurgeTenantTerminalAsync(tenantContext, cancellationToken);
                    return result.Status == "Draining"
                        ? Results.Accepted(value: result)
                        : Results.Ok(result);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { Error = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { Error = ex.Message });
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            }).WithName("applySharedTenantLifecycle");

            // ── Ad-hoc job execution (authenticated — see ApiKeyDenied) ──────────
            app.MapPost("/jobs", (HttpContext ctx, IConfiguration cfg, JobSubmitRequest request, IServiceScopeFactory scopeFactory, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var caller = RequestCaller(ctx);
                var jobId = Guid.NewGuid().ToString("N")[..8];
                var cts = new CancellationTokenSource();
                var entry = new JobEntry(jobId, cts, ctx.TraceIdentifier, caller.PrincipalKey);
                _jobs[jobId] = entry;

                logger.LogInformation("Job {JobId} submitted by {Actor} (label={Label})", jobId,
                    caller.AuditActor, ETL_SQL.Core.Common.LogSanitizer.Clean(request.Label));
                _ = RunJobAsync(entry, request, ToExecutionIdentity(caller), scopeFactory, logger, cts.Token);

                return Results.Accepted($"/jobs/{jobId}", new { JobId = jobId });
            }).WithName("submitJob");

            app.MapDelete("/jobs/{id}", (string id, HttpContext ctx, IConfiguration cfg, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                if (!_jobs.TryGetValue(id, out var entry))
                    return Results.NotFound(new { Error = $"Job '{id}' not found." });
                if (!CanAccessAdHoc(RequestCaller(ctx), entry))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                logger.LogInformation("Cancelling job {JobId} by {Actor}",
                    ETL_SQL.Core.Common.LogSanitizer.Clean(id), RequestActor(ctx));
                entry.Cts.Cancel();
                entry.Status = JobRunStatus.Cancelled;
                return Results.Ok(new { JobId = id, Status = "Cancelled" });
            }).WithName("cancelJob");

            app.MapGet("/jobs/{id}", (string id, HttpContext ctx, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                if (!_jobs.TryGetValue(id, out var entry))
                    return Results.NotFound(new { Error = $"Job '{id}' not found." });
                if (!CanAccessAdHoc(RequestCaller(ctx), entry))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                var includeSensitivePayload = ApiKeyAcceptedForSensitivePayload(ctx, cfg);
                return Results.Ok(new JobStatusResponse
                {
                    JobId = entry.JobId,
                    Status = entry.Status,
                    RowsProcessed = entry.RowsProcessed,
                    ExecutionTimeMs = entry.ExecutionTimeMs,
                    PeakMemoryBytes = entry.PeakMemoryBytes,
                    CpuTimeSeconds = entry.CpuTimeSeconds,
                    ErrorMessage = entry.ErrorMessage,
                    ReportManifestJson = includeSensitivePayload ? entry.ReportManifestJson : null
                });
            }).WithName("getJobStatus");

            // ── Scheduled job management ──────────────────────────────────────

            app.MapGet("/api/scheduled-jobs", async (HttpContext ctx, IJobHistoryStore store,
                OrchestratorObjectAuthorizationService authorization, IConfiguration cfg,
                int limit = 100, int offset = 0) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var jobs = await store.GetJobsPageAsync(limit, offset);
                var caller = RequestCaller(ctx);
                var visible = new List<JobDefinition>();
                foreach (var job in jobs)
                {
                    if (await authorization.CanAsync(caller, OrchestratorObjectKind.Job, job.Name,
                            OrchestratorObjectPermission.Read, job.CreatedBy, ctx.RequestAborted))
                        visible.Add(job);
                }
                return Results.Ok(visible);
            }).WithName("listScheduledJobs");

            app.MapPost("/api/scheduled-jobs", async (HttpContext ctx, CreateScheduledJobRequest req,
                IJobHistoryStore store, IJobCatalogStore catalog, IBundleStore bundleStore,
                OrchestratorObjectAuthorizationService authorization, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(req.Name))
                    return Results.BadRequest(new { Error = "Name is required." });
                if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                JobDefinition job;
                var normalized = !string.IsNullOrWhiteSpace(req.TargetPath)
                    || !string.IsNullOrWhiteSpace(req.JobType);
                var existing = await store.GetJobAsync(req.Name);
                if (existing is not null && !CanAccessJobTenant(existing, requestTenant))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                var caller = RequestCaller(ctx);
                if (existing is null && !OrchestratorObjectAuthorizationService.CanCreate(caller))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                if (existing is not null && !await authorization.CanAsync(
                        caller, OrchestratorObjectKind.Job, existing.Name,
                        OrchestratorObjectPermission.Manage, existing.CreatedBy, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                if (normalized)
                {
                    if (req.Name.StartsWith("sub_", StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(new { Error = "Job names beginning with 'sub_' are reserved for generated subscriptions." });
                    if (!Enum.TryParse<JobTargetKind>(req.JobType, ignoreCase: true, out var targetKind))
                        return Results.BadRequest(new { Error = "JobType must be SCRIPT or REPORT." });
                    if (string.IsNullOrWhiteSpace(req.TargetPath))
                        return Results.BadRequest(new { Error = "TargetPath is required." });
                    if (!Enum.TryParse<ObjectCreationMode>(req.Mode, ignoreCase: true, out var mode))
                        mode = ObjectCreationMode.Create;
                    if (existing is not null && mode == ObjectCreationMode.Create)
                        return Results.Conflict(new
                        {
                            Error = $"Job '{req.Name}' already exists. Use CREATE OR ALTER or CREATE OR REPLACE."
                        });

                    var targetPath = targetKind == JobTargetKind.Script
                        ? await PinBundleTargetAsync(req.TargetPath, bundleStore)
                        : req.TargetPath;
                    var scriptText = targetKind == JobTargetKind.Script
                        ? new RunScriptStatement(
                            new LiteralExpression(targetPath, TokenType.STRING_LITERAL),
                            []).ToSql()
                        : string.Empty;
                    var scriptHash = targetKind == JobTargetKind.Script
                        ? "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scriptText))).ToLowerInvariant()
                        : null;
                    var patches = existing is not null && mode == ObjectCreationMode.CreateOrAlter;

                    job = new JobDefinition(
                        req.Name,
                        scriptText,
                        1,
                        "HOUR",
                        null,
                        existing?.LastRun,
                        null,
                        existing?.IsEnabled ?? true,
                        req.MaxRetries ?? (patches ? existing!.MaxRetries : 0),
                        req.RetryDelaySeconds ?? (patches ? existing!.RetryDelaySeconds : 30),
                        scriptHash,
                        targetKind == JobTargetKind.Script ? req.HashPolicy ?? "Warn" : "Off",
                        existing?.Version ?? 1,
                        targetKind,
                        targetPath,
                        req.DisplayName ?? (patches ? existing!.DisplayName : req.Name),
                        req.Description ?? (patches ? existing!.Description : null),
                        req.Options is null ? (patches ? existing!.Options : null) : JsonSerializer.Serialize(req.Options),
                        existing?.CreatedBy ?? caller.PrincipalKey,
                        ReadActor(ctx),
                        existing?.TenantId ?? requestTenant);

                    await store.SaveJobAsync(job);
                    if (existing is not null && mode == ObjectCreationMode.CreateOrReplace)
                    {
                        foreach (var link in await catalog.GetJobSchedulesAsync(req.Name))
                            await catalog.RemoveJobScheduleAsync(req.Name, link.ScheduleName);
                        foreach (var link in await catalog.GetJobNotificationsAsync(req.Name))
                            await catalog.RemoveJobNotificationAsync(req.Name, link.NotificationName, link.Trigger);
                    }
                }
                else
                {
                    // Compatibility for the Portal management API while its UI is migrated to the
                    // normalized job/schedule endpoints. ETL-SQL source can no longer produce this form.
                    if (string.IsNullOrWhiteSpace(req.ScriptText))
                        return Results.BadRequest(new { Error = "TargetPath is required for normalized jobs." });
                    if (!req.Interval.HasValue || req.Interval <= 0)
                        return Results.BadRequest(new { Error = "Interval must be positive." });

                    var validUnits = new[] { "SECOND", "MINUTE", "HOUR", "DAY", "WEEK", "MONTH" };
                    if (!validUnits.Contains((req.Unit ?? "").ToUpperInvariant()))
                        return Results.BadRequest(new { Error = $"Unit must be one of: {string.Join(", ", validUnits)}" });

                    var scriptText = await PinBundlePathsAsync(req.ScriptText, bundleStore);
                    job = new JobDefinition(
                        req.Name, scriptText, req.Interval.Value, (req.Unit ?? "HOUR").ToUpperInvariant(),
                        req.AtTime, null, null, true, req.MaxRetries ?? 0, req.RetryDelaySeconds ?? 30,
                        null, req.HashPolicy ?? "Warn", CreatedBy: caller.PrincipalKey,
                        ModifiedBy: ReadActor(ctx), TenantId: requestTenant);
                    await store.SaveJobAsync(job);
                }

                return Results.Created($"/api/scheduled-jobs/{Uri.EscapeDataString(req.Name)}", job);
            }).WithName("createScheduledJob");

            app.MapPut("/api/scheduled-jobs/{name}", async (HttpContext ctx, string name,
                UpdateScheduledJobRequest req, IJobHistoryStore store, IBundleStore bundleStore,
                OrchestratorObjectAuthorizationService authorization, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                var expectedVersion = ReadExpectedVersion(ctx);
                if (expectedVersion is null)
                    return Results.Json(
                        new { Error = "If-Match with the current job version is required." },
                        statusCode: StatusCodes.Status428PreconditionRequired);

                var existing = await store.GetJobAsync(Uri.UnescapeDataString(name));
                if (existing == null)
                    return Results.NotFound(new { Error = $"Job '{name}' not found." });
                if (!CanAccessJobTenant(existing, requestTenant))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                if (!await authorization.CanAsync(
                        RequestCaller(ctx), OrchestratorObjectKind.Job, existing.Name,
                        OrchestratorObjectPermission.Manage, existing.CreatedBy, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                var pinnedScript = req.ScriptText != null ? await PinBundlePathsAsync(req.ScriptText, bundleStore) : null;
                var targetPath = req.TargetPath;
                if (targetPath is not null && existing.JobType == JobTargetKind.Script)
                    targetPath = await PinBundleTargetAsync(targetPath, bundleStore);
                if (targetPath is not null && existing.JobType == JobTargetKind.Script)
                    pinnedScript = new RunScriptStatement(
                        new LiteralExpression(targetPath, TokenType.STRING_LITERAL),
                        []).ToSql();
                var scriptHash = pinnedScript is not null && existing.JobType == JobTargetKind.Script
                    ? "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pinnedScript))).ToLowerInvariant()
                    : existing.ScriptHash;
                var updated = existing with
                {
                    Script = pinnedScript ?? existing.Script,
                    TargetPath = targetPath ?? existing.TargetPath,
                    Interval = req.Interval ?? existing.Interval,
                    Unit = req.Unit != null ? req.Unit.ToUpperInvariant() : existing.Unit,
                    AtTime = req.AtTime ?? existing.AtTime,
                    IsEnabled = req.IsEnabled ?? existing.IsEnabled,
                    MaxRetries = req.MaxRetries ?? existing.MaxRetries,
                    RetryDelaySeconds = req.RetryDelaySeconds ?? existing.RetryDelaySeconds,
                    DisplayName = req.DisplayName ?? existing.DisplayName,
                    Description = req.Description ?? existing.Description,
                    Options = req.Options is null ? existing.Options : JsonSerializer.Serialize(req.Options),
                    ScriptHash = scriptHash,
                    HashPolicy = req.HashPolicy ?? existing.HashPolicy,
                    ModifiedBy = ReadActor(ctx)
                };

                if (!await store.TrySaveJobAsync(updated, expectedVersion.Value))
                {
                    var current = await store.GetJobAsync(existing.Name);
                    return Results.Conflict(new
                    {
                        Error = "The job changed after it was read. Refresh it and retry.",
                        Current = current
                    });
                }
                return Results.Ok(await store.GetJobAsync(existing.Name));
            }).WithName("updateScheduledJob");

            app.MapDelete("/api/scheduled-jobs/{name}", async (HttpContext ctx, string name,
                IJobHistoryStore store, IOrchestratorAuthorizationStore authorizationStore,
                OrchestratorObjectAuthorizationService authorization, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var expectedVersion = ReadExpectedVersion(ctx);
                if (expectedVersion is null)
                    return Results.Json(
                        new { Error = "If-Match with the current job version is required." },
                        statusCode: StatusCodes.Status428PreconditionRequired);

                var unescaped = Uri.UnescapeDataString(name);
                var existing = await store.GetJobAsync(unescaped);
                if (existing is null)
                    return Results.NotFound(new { Error = $"Job '{name}' not found." });
                if (!await authorization.CanAsync(
                        RequestCaller(ctx), OrchestratorObjectKind.Job, existing.Name,
                        OrchestratorObjectPermission.Manage, existing.CreatedBy, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                if (!await store.TryDeleteJobAsync(unescaped, expectedVersion.Value))
                {
                    var current = await store.GetJobAsync(unescaped);
                    return Results.Conflict(new
                    {
                        Error = "The job changed after it was read. Refresh it and retry.",
                        Current = current
                    });
                }
                await authorizationStore.DeleteObjectGrantsAsync(
                    OrchestratorObjectKind.Job, unescaped, ctx.RequestAborted);
                return Results.Ok(new { Deleted = unescaped });
            }).WithName("deleteScheduledJob");

            app.MapGet("/api/scheduled-jobs/{name}/history", async (HttpContext ctx, string name,
                IJobHistoryStore store, OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg, int limit = 50) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var unescaped = Uri.UnescapeDataString(name);
                var job = await store.GetJobAsync(unescaped);
                if (job is null) return Results.NotFound();
                if (!await authorization.CanAsync(
                        RequestCaller(ctx), OrchestratorObjectKind.Job, job.Name,
                        OrchestratorObjectPermission.Read, job.CreatedBy, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                var history = await store.GetHistoryAsync(unescaped, Math.Clamp(limit, 1, 1000));
                return Results.Ok(history);
            }).WithName("getScheduledJobHistory");

            app.MapPost("/api/job-runs/{historyId:long}/resume", async (HttpContext ctx, long historyId,
                SchedulerService scheduler, IJobHistoryStore store,
                OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var history = await store.GetHistoryEntryAsync(historyId);
                if (history is null) return Results.NotFound(new { Error = $"Run '{historyId}' not found." });
                var job = await store.GetJobAsync(history.JobName);
                if (job is null) return Results.NotFound(new { Error = $"Job '{history.JobName}' not found." });
                if (!await authorization.CanAsync(
                        RequestCaller(ctx), OrchestratorObjectKind.Job, job.Name,
                        OrchestratorObjectPermission.Execute, job.CreatedBy, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                var result = await scheduler.ResumeJobAsync(historyId);
                if (result.Status == ResumeTriggerStatus.RunNotFound)
                    return Results.NotFound(new { Error = result.Reason });
                if (result.Status == ResumeTriggerStatus.JobNotFound)
                    return Results.NotFound(new { Error = result.Reason, result.CheckpointLabel });
                if (result.Status is ResumeTriggerStatus.NotResumable or ResumeTriggerStatus.AlreadyRunning)
                    return Results.Conflict(new { Error = result.Reason, result.CheckpointLabel });

                var actor = RequestActor(ctx);
                logger.LogWarning(
                    "Run {HistoryId} resumed from named checkpoint {CheckpointLabel} by {Actor}.",
                    historyId, result.CheckpointLabel, actor);
                EmitNamedCheckpointResumeAudit(
                    actor, historyId, result.CheckpointLabel!, ctx.TraceIdentifier);
                return Results.Accepted(value: new
                {
                    Message = $"Run {historyId} queued to resume from checkpoint '{result.CheckpointLabel}'.",
                    result.CheckpointLabel
                });
            }).WithName("resumeScheduledJobRun");

            app.MapGet("/api/history", async (HttpContext ctx, IJobHistoryStore store,
                OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg, string? jobName = null, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var history = (await store.GetHistoryAsync(jobName, Math.Clamp(limit, 1, 1000))).ToList();
                return Results.Ok(await FilterReadableJobRowsAsync(
                    history, row => row.JobName, RequestCaller(ctx), store, authorization, ctx.RequestAborted));
            }).WithName("getAllJobHistory");

            app.MapGet("/api/data-quality/status", async (HttpContext ctx, IJobHistoryStore store,
                OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg, int limit = 1000) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var rows = await store.GetDataQualityStatusesAsync(Math.Clamp(limit, 1, 10000));
                return Results.Ok(await FilterReadableJobRowsAsync(
                    rows.Where(row => !string.IsNullOrWhiteSpace(row.JobName)).ToList(),
                    row => row.JobName!, RequestCaller(ctx), store, authorization, ctx.RequestAborted));
            }).WithName("getDataQualityStatus");

            app.MapGet("/api/data-quality/failures", async (HttpContext ctx, IJobHistoryStore store,
                OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg, int limit = 1000) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var rows = await store.GetDataQualityFailuresAsync(Math.Clamp(limit, 1, 10000));
                return Results.Ok(await FilterReadableJobRowsAsync(
                    rows, row => row.JobName, RequestCaller(ctx), store, authorization, ctx.RequestAborted));
            }).WithName("getDataQualityFailures");

            app.MapGet("/api/stewardship/score", async (HttpContext ctx, ILineageCatalogStore store,
                IConfiguration cfg, int limit = 1000) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var evaluation = await EvaluateStewardshipAsync(store, limit);
                return Results.Ok(evaluation.Scores);
            }).WithName("getStewardshipScore");

            app.MapGet("/api/stewardship/gaps", async (HttpContext ctx, ILineageCatalogStore store,
                IConfiguration cfg, int limit = 1000) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var evaluation = await EvaluateStewardshipAsync(store, limit);
                return Results.Ok(evaluation.Gaps);
            }).WithName("getStewardshipGaps");

            app.MapGet("/api/schedules", async (HttpContext ctx, IJobCatalogStore catalog,
                OrchestratorObjectAuthorizationService authorization, IConfiguration cfg,
                int limit = 100, int offset = 0) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var caller = RequestCaller(ctx);
                var visible = new List<ScheduleDefinition>();
                foreach (var schedule in await catalog.GetSchedulesAsync(Math.Clamp(limit, 1, 1000), Math.Max(0, offset)))
                    if (await authorization.CanAsync(caller, OrchestratorObjectKind.Schedule, schedule.Name,
                            OrchestratorObjectPermission.Read, schedule.CreatedBy, ctx.RequestAborted))
                        visible.Add(schedule);
                return Results.Ok(visible);
            }).WithName("listSchedules");

            app.MapGet("/api/schedules/{name}", async (HttpContext ctx, string name,
                IJobCatalogStore catalog, OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var schedule = await catalog.GetScheduleAsync(Uri.UnescapeDataString(name));
                if (schedule is null) return Results.NotFound();
                return await authorization.CanAsync(RequestCaller(ctx), OrchestratorObjectKind.Schedule,
                        schedule.Name, OrchestratorObjectPermission.Read, schedule.CreatedBy, ctx.RequestAborted)
                    ? Results.Ok(schedule)
                    : Results.StatusCode(StatusCodes.Status403Forbidden);
            }).WithName("getSchedule");

            app.MapGet("/api/notifications", async (HttpContext ctx, IJobCatalogStore catalog,
                OrchestratorObjectAuthorizationService authorization, IConfiguration cfg,
                int limit = 100, int offset = 0) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var caller = RequestCaller(ctx);
                var visible = new List<NotificationDefinition>();
                foreach (var notification in await catalog.GetNotificationsAsync(Math.Clamp(limit, 1, 1000), Math.Max(0, offset)))
                    if (await authorization.CanAsync(caller, OrchestratorObjectKind.Notification, notification.Name,
                            OrchestratorObjectPermission.Read, notification.CreatedBy, ctx.RequestAborted))
                        visible.Add(notification);
                return Results.Ok(visible);
            }).WithName("listNotifications");

            app.MapGet("/api/notifications/{name}", async (HttpContext ctx, string name,
                IJobCatalogStore catalog, OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var notification = await catalog.GetNotificationAsync(Uri.UnescapeDataString(name));
                if (notification is null) return Results.NotFound();
                return await authorization.CanAsync(RequestCaller(ctx), OrchestratorObjectKind.Notification,
                        notification.Name, OrchestratorObjectPermission.Read, notification.CreatedBy, ctx.RequestAborted)
                    ? Results.Ok(notification)
                    : Results.StatusCode(StatusCodes.Status403Forbidden);
            }).WithName("getNotification");

            app.MapPost("/api/notifications/{name}/dispatch", async (HttpContext ctx, string name,
                DispatchNotificationApiRequest req, NotificationDispatchService dispatch,
                IJobCatalogStore catalog, OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg,
                CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var notificationName = Uri.UnescapeDataString(name);
                var notification = await catalog.GetNotificationAsync(notificationName);
                if (notification is null)
                    return Results.NotFound(new { Error = $"Notification '{notificationName}' was not found." });
                if (!await authorization.CanAsync(
                        RequestCaller(ctx), OrchestratorObjectKind.Notification, notification.Name,
                        OrchestratorObjectPermission.Execute, notification.CreatedBy, ct))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                var sourceKind = string.IsNullOrWhiteSpace(req.SourceKind)
                    ? "ALERT"
                    : req.SourceKind.Trim().ToUpperInvariant();
                if (sourceKind != "ALERT" && sourceKind != "JOB" && sourceKind != "SUBSCRIPTION")
                    return Results.BadRequest(new { Error = "SourceKind must be ALERT, JOB, or SUBSCRIPTION." });
                if (string.IsNullOrWhiteSpace(req.Title))
                    return Results.BadRequest(new { Error = "Title is required." });
                if (string.IsNullOrWhiteSpace(req.Text))
                    return Results.BadRequest(new { Error = "Text is required." });

                NotificationDispatchPayload payload;
                try
                {
                    payload = new NotificationDispatchPayload(
                        NotificationName: notificationName,
                        SourceKind: sourceKind,
                        Title: req.Title.Trim(),
                        Text: req.Text.Trim(),
                        Trigger: req.Trigger,
                        Status: req.Status,
                        JobName: req.JobName,
                        AlertName: req.AlertName,
                        ReportId: req.ReportId,
                        HistoryId: req.HistoryId,
                        RowsProcessed: req.RowsProcessed ?? 0,
                        RecipientOverride: req.RecipientOverride,
                        ErrorMessage: req.ErrorMessage,
                        Actor: ReadActor(ctx),
                        AttachmentPaths: req.AttachmentPaths);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    return Results.BadRequest(new { Error = ex.Message });
                }

                var result = await dispatch.DispatchNotificationAsync(payload, ct);

                return result.Delivered
                    ? Results.Ok(result)
                    : Results.Json(result, statusCode: result.Skipped
                        ? StatusCodes.Status202Accepted
                        : StatusCodes.Status502BadGateway);
            }).WithName("dispatchNotification");

            app.MapGet("/api/admin/connections", async (HttpContext ctx, IServiceProvider services,
                IConfiguration cfg, CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                var aliases = await writable.ListAsync(ct);
                var entries = new List<ConnectionCatalogEntryResponse>();
                foreach (var alias in aliases)
                {
                    var status = await writable.GetStatusAsync(alias, ct);
                    SharedConnectionDefinition? definition = null;
                    if (status != SecretLifecycleStatus.NotFound)
                    {
                        try { definition = await writable.GetDefinitionAsync(alias, ct); }
                        catch { }
                    }

                    entries.Add(ToConnectionCatalogEntry(alias, status, definition));
                }

                return Results.Ok(entries);
            }).WithName("listAdminConnections");

            app.MapGet("/api/admin/connections/export", async (HttpContext ctx, IServiceProvider services,
                IConfiguration cfg, CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                var aliases = await writable.ListAsync(ct);
                var entries = new List<ConnectionCatalogEntryResponse>();
                foreach (var alias in aliases)
                {
                    var status = await writable.GetStatusAsync(alias, ct);
                    SharedConnectionDefinition? definition = null;
                    if (status != SecretLifecycleStatus.NotFound)
                    {
                        try { definition = await writable.GetDefinitionAsync(alias, ct); }
                        catch { }
                    }

                    entries.Add(ToConnectionCatalogEntry(alias, status, definition));
                }

                EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_EXPORT", null, $"Count={entries.Count}");
                return Results.Ok(entries);
            }).WithName("exportAdminConnections");

            app.MapPost("/api/admin/connections/import", async (HttpContext ctx,
                List<ConnectionCatalogEntryResponse>? entries, IServiceProvider services, IConfiguration cfg,
                CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });
                if (entries is null || entries.Count == 0)
                    return Results.BadRequest(new { Error = "No entries to import." });

                var created = 0;
                var updated = 0;
                try
                {
                    foreach (var entry in entries)
                    {
                        var statusBefore = await writable.GetStatusAsync(entry.Alias, ct);
                        var definition = new SharedConnectionDefinition(
                            entry.Alias,
                            entry.ConnectorType,
                            entry.Target,
                            new Dictionary<string, string>(entry.Options ?? [], StringComparer.OrdinalIgnoreCase),
                            Disabled: false,
                            entry.SensitiveFields);
                        var rawCredential = SharedConnectionValidator.FindRawCredential(definition.Options, definition.Target);
                        if (rawCredential is not null)
                        {
                            EmitConnectionAudit(RequestActor(ctx),
                                "SHARED_CONNECTION_IMPORT_DENIED",
                                entry.Alias,
                                $"Connection option '{rawCredential}' must be a SECRET: or ENC: reference.",
                                allowed: false);
                            return Results.BadRequest(new { Error = $"Connection option '{rawCredential}' must be a SECRET: or ENC: reference, not a raw credential value." });
                        }

                        await writable.StoreAsync(definition, ct);
                        if (entry.Status.Equals("disabled", StringComparison.OrdinalIgnoreCase))
                            await writable.DisableAsync(entry.Alias, ct);

                        if (statusBefore == SecretLifecycleStatus.NotFound) created++;
                        else updated++;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return Results.BadRequest(new { Error = ex.Message });
                }

                EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_IMPORT", null, $"Created={created}; Updated={updated}");
                return Results.Ok(new { Created = created, Updated = updated });
            }).WithName("importAdminConnections");

            app.MapGet("/api/admin/connections/{alias}", async (HttpContext ctx, string alias,
                IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                var decodedAlias = Uri.UnescapeDataString(alias);
                var status = await writable.GetStatusAsync(decodedAlias, ct);
                if (status == SecretLifecycleStatus.NotFound)
                    return Results.NotFound(new { Error = $"Connection '{decodedAlias}' was not found." });

                var definition = await writable.GetDefinitionAsync(decodedAlias, ct);

                return Results.Ok(ToConnectionCatalogEntry(decodedAlias, status, definition));
            }).WithName("getAdminConnection");

            app.MapPut("/api/admin/connections/{alias}", async (HttpContext ctx, string alias,
                SetConnectionCatalogEntryRequest request, IServiceProvider services, IConfiguration cfg,
                CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                var entry = new SharedConnectionDefinition(
                    Uri.UnescapeDataString(alias),
                    request?.ConnectorType ?? "",
                    request?.Target,
                    new Dictionary<string, string>(request?.Options ?? [], StringComparer.OrdinalIgnoreCase),
                    Disabled: false,
                    request?.SensitiveFields);
                var rawCredential = SharedConnectionValidator.FindRawCredential(entry.Options, entry.Target);
                if (rawCredential is not null)
                {
                    EmitConnectionAudit(RequestActor(ctx),
                        "SHARED_CONNECTION_WRITE_DENIED",
                        entry.Alias,
                        $"Connection option '{rawCredential}' must be a SECRET: or ENC: reference.",
                        allowed: false);
                    return Results.BadRequest(new { Error = $"Connection option '{rawCredential}' must be a SECRET: or ENC: reference, not a raw credential value." });
                }

                try
                {
                    var existed = await writable.GetStatusAsync(entry.Alias, ct) != SecretLifecycleStatus.NotFound;
                    await writable.StoreAsync(entry, ct);
                    EmitConnectionAudit(RequestActor(ctx),
                        existed ? "SHARED_CONNECTION_UPDATE" : "SHARED_CONNECTION_CREATE",
                        entry.Alias,
                        $"ConnectorType={entry.ConnectorType}");
                    return Results.NoContent();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return Results.BadRequest(new { Error = ex.Message });
                }
            }).WithName("setAdminConnection");

            app.MapPost("/api/admin/connections/{alias}/enable", async (HttpContext ctx, string alias,
                IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                try
                {
                    var decodedAlias = Uri.UnescapeDataString(alias);
                    await writable.EnableAsync(decodedAlias, ct);
                    EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_ENABLE", decodedAlias, "Status=active");
                    return Results.NoContent();
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new { Error = ex.Message });
                }
            }).WithName("enableAdminConnection");

            app.MapPost("/api/admin/connections/{alias}/disable", async (HttpContext ctx, string alias,
                IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                try
                {
                    var decodedAlias = Uri.UnescapeDataString(alias);
                    await writable.DisableAsync(decodedAlias, ct);
                    EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_DISABLE", decodedAlias, "Status=disabled");
                    return Results.NoContent();
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new { Error = ex.Message });
                }
            }).WithName("disableAdminConnection");

            app.MapPost("/api/admin/connections/{alias}/test", async (HttpContext ctx, string alias,
                IServiceProvider services, IConfiguration cfg, ConnectionDiagnosticEngine diagnostics,
                ISecretProvider secrets, ETL_SQL.Services.SecurityService security, CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                var decodedAlias = Uri.UnescapeDataString(alias);
                var status = await writable.GetStatusAsync(decodedAlias, ct);
                if (status == SecretLifecycleStatus.NotFound)
                    return Results.NotFound(new { Error = $"Connection '{decodedAlias}' was not found." });
                if (status == SecretLifecycleStatus.Disabled)
                {
                    EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_TEST_DENIED", decodedAlias, "Status=disabled", allowed: false);
                    return Results.Conflict(new { Alias = decodedAlias, Status = "disabled" });
                }

                SharedConnectionDefinition entry;
                try
                {
                    entry = await writable.ResolveAsync(decodedAlias, null, ct);
                }
                catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
                {
                    EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_TEST_DENIED", decodedAlias, "Status=unresolvable", allowed: false);
                    return Results.Conflict(new { Alias = decodedAlias, Status = "unresolvable", Error = ex.Message });
                }

                try
                {
                    var target = await ResolveSecretReferenceAsync(entry.Target, secrets, ct);
                    var options = await ResolveSecretReferencesAsync(entry.Options, secrets, ct);
                    var snapshot = ExecutionPolicySnapshot.Capture(
                        EnterprisePolicyRuntime.Current,
                        Environment.UserName,
                        ScriptExecutionMode.Batch,
                        "orchestrator-connection-test");
                    var timeoutSeconds = cfg.GetValue<int?>("Engine:Diagnostics:ProbeTimeoutSeconds") ?? 5;
                    var report = await diagnostics.DiagnoseAsync(
                        decodedAlias, entry.ConnectorType, target, options, security, snapshot, timeoutSeconds, ct);

                    EmitConnectionAudit(RequestActor(ctx),
                        "SHARED_CONNECTION_TEST",
                        decodedAlias,
                        report.Succeeded ? "Outcome=ok" : "Outcome=failed");
                    return Results.Ok(new
                    {
                        Alias = decodedAlias,
                        Succeeded = report.Succeeded,
                        Steps = report.Steps.Select(s => new
                        {
                            s.Layer,
                            Status = s.Status.ToString().ToLowerInvariant(),
                            s.Detail,
                            s.Remedy
                        })
                    });
                }
                catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
                {
                    EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_TEST_DENIED", decodedAlias, "Status=unresolvable", allowed: false);
                    return Results.Conflict(new { Alias = decodedAlias, Status = "unresolvable", Error = ex.Message });
                }
            }).WithName("testAdminConnection");

            app.MapGet("/api/admin/connections/{alias}/impact", async (HttpContext ctx, string alias,
                IServiceProvider services, IConfiguration cfg, IJobHistoryStore store, CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                var decodedAlias = Uri.UnescapeDataString(alias);
                if (await writable.GetStatusAsync(decodedAlias, ct) == SecretLifecycleStatus.NotFound)
                    return Results.NotFound(new { Error = $"Connection '{decodedAlias}' was not found." });

                var reference = $"SHARED:{decodedAlias}";
                var consumers = new List<ImpactConsumerResponse>();
                foreach (var job in await store.GetAllJobsAsync())
                {
                    var content = File.Exists(job.Script)
                        ? await ReadBoundedAsync(job.Script, ct)
                        : job.Script;
                    if (ContainsReference(content, reference))
                    {
                        consumers.Add(new ImpactConsumerResponse(
                            "ScheduledJob",
                            job.Name,
                            job.TargetPath ?? job.Script,
                            job.LastRun,
                            null));
                    }
                }

                EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_IMPACT", decodedAlias, $"ConsumerCount={consumers.Count}");
                return Results.Ok(new ImpactReportResponse(reference, consumers.Count, consumers));
            }).WithName("impactAdminConnection");

            app.MapDelete("/api/admin/connections/{alias}", async (HttpContext ctx, string alias,
                IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (services.GetService<IConnectionCatalogProvider>() is not IWritableConnectionCatalogProvider writable)
                    return Results.NotFound(new { Error = "No writable connection catalog is configured." });

                try
                {
                    var decodedAlias = Uri.UnescapeDataString(alias);
                    await writable.DeleteAsync(decodedAlias, ct);
                    EmitConnectionAudit(RequestActor(ctx), "SHARED_CONNECTION_DELETE", decodedAlias, "Status=deleted");
                    return Results.NoContent();
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new { Error = ex.Message });
                }
            }).WithName("deleteAdminConnection");

            app.MapGet("/api/lineage/history/table/{name}", async (HttpContext ctx, string name,
                ILineageCatalogStore catalog, IConfiguration cfg, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var entries = await catalog.GetHistoryForTableAsync(Uri.UnescapeDataString(name), limit);
                return Results.Ok(entries);
            }).WithName("getLineageHistoryForTable");

            app.MapGet("/api/lineage/history/tag/{key}", async (HttpContext ctx, string key,
                ILineageCatalogStore catalog, IConfiguration cfg, string? value = null, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var entries = await catalog.GetHistoryForTagAsync(Uri.UnescapeDataString(key), value, limit);
                return Results.Ok(entries);
            }).WithName("getLineageHistoryForTag");

            app.MapGet("/api/lineage/history/missing-tags", async (HttpContext ctx,
                ILineageCatalogStore catalog, IConfiguration cfg, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var entries = await catalog.GetMissingMetadataAsync(
                    ETL_SQL.Common.StewardshipTagCatalog.RequiredStewardshipTags.ToArray(),
                    Math.Clamp(limit, 1, 1000));
                return Results.Ok(entries);
            }).WithName("getLineageHistoryMissingTags");

            app.MapGet("/api/lineage/history/protected-data", async (HttpContext ctx,
                ILineageCatalogStore catalog, IConfiguration cfg, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                limit = Math.Clamp(limit, 1, 1000);
                var entries = await catalog.GetRecentLineageAsync(Math.Max(limit * 20, 1000));
                return Results.Ok(LineageProtectedData.FromHistory(entries).Take(limit));
            }).WithName("getLineageHistoryProtectedData");

            app.MapGet("/api/lineage/history/protected-data/suggestions", async (HttpContext ctx,
                ILineageCatalogStore catalog, IConfiguration cfg, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                limit = Math.Clamp(limit, 1, 1000);
                var entries = await catalog.GetRecentLineageAsync(Math.Max(limit * 20, 1000));
                return Results.Ok(LineageProtectedData.SuggestFromHistory(entries).Take(limit));
            }).WithName("getLineageHistoryProtectedDataSuggestions");

            app.MapPost("/api/scheduled-jobs/{name}/trigger", async (HttpContext ctx, string name,
                SchedulerService scheduler, IJobHistoryStore store,
                OrchestratorObjectAuthorizationService authorization, IConfiguration cfg,
                ILogger<Program> logger, TriggerScheduledJobRequest? request = null) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var unescaped = Uri.UnescapeDataString(name);
                if (!TryNormalizeVariableOverrides(request?.Variables, out var overrides, out var validationError))
                    return Results.BadRequest(new { Error = validationError });
                var job = await store.GetJobAsync(unescaped);
                if (job is null) return Results.NotFound(new { Error = $"Job '{name}' not found." });
                var required = overrides.Count == 0
                    ? OrchestratorObjectPermission.Execute
                    : OrchestratorObjectPermission.Override;
                if (!await authorization.CanAsync(
                        RequestCaller(ctx), OrchestratorObjectKind.Job, job.Name,
                        required, job.CreatedBy, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                var triggerResult = await scheduler.TriggerJobWithOverridesAsync(unescaped, overrides);
                if (triggerResult == ManualTriggerResult.JobNotFound)
                    return Results.NotFound(new { Error = $"Job '{name}' not found." });
                if (triggerResult == ManualTriggerResult.AlreadyRunning)
                    return Results.Conflict(new
                    {
                        Error = $"Job '{unescaped}' already has an execution in progress. Retry the one-run trigger after it finishes."
                    });

                var actor = RequestActor(ctx);
                logger.LogInformation(
                    "Job {JobName} triggered out of schedule by {Actor} with {OverrideCount} variable overrides ({OverrideNames}).",
                    unescaped, actor, overrides.Count, string.Join(',', overrides.Keys));
                if (overrides.Count > 0)
                    EmitVariableOverrideAudit(actor, unescaped, overrides.Keys, ctx.TraceIdentifier);
                return Results.Accepted(uri: (string?)null, value: new { Message = $"Job '{unescaped}' queued for immediate execution." });
            }).WithName("triggerScheduledJob");

            app.MapPost("/api/scheduled-jobs/{name}/kill", async (HttpContext ctx, string name,
                IJobManager jobManager, IJobHistoryStore store,
                OrchestratorObjectAuthorizationService authorization, IConfiguration cfg,
                ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var unescaped = Uri.UnescapeDataString(name);
                var job = await store.GetJobAsync(unescaped);
                if (job is null) return Results.NotFound(new { Error = $"Job '{name}' not found." });
                if (!await authorization.CanAsync(
                        RequestCaller(ctx), OrchestratorObjectKind.Job, job.Name,
                        OrchestratorObjectPermission.Manage, job.CreatedBy, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                var history = await store.GetHistoryAsync(unescaped, 10);
                var running = history.FirstOrDefault(h => h.Status == "RUNNING" && h.EndTime == null);
                if (running == null)
                    return Results.NotFound(new { Error = $"No running instance of job '{name}' found." });

                var killed = jobManager.KillJob(running.Id);
                if (killed)
                    logger.LogWarning(
                        "Job {JobName} (historyId={HistoryId}) killed by {Actor}.",
                        unescaped, running.Id, RequestActor(ctx));
                return killed
                    ? Results.Ok(new { Message = $"Job '{unescaped}' (historyId={running.Id}) kill signal sent." })
                    : Results.Problem($"Kill signal for history entry {running.Id} did not match a running job.");
            }).WithName("killScheduledJob");

            // ── Per-object authorization ─────────────────────────────────────

            app.MapGet("/api/authorization/{kind}/{name}", async (
                HttpContext ctx,
                string kind,
                string name,
                IJobHistoryStore jobs,
                IJobCatalogStore catalog,
                IOrchestratorAuthorizationStore grants,
                OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (!TryParseObjectKind(kind, out var objectKind))
                    return Results.BadRequest(new { Error = "Kind must be JOB, SCHEDULE, or NOTIFICATION." });
                var objectName = Uri.UnescapeDataString(name);
                var owner = await ReadObjectOwnerAsync(objectKind, objectName, jobs, catalog);
                if (owner.Exists == false) return Results.NotFound();
                if (!await authorization.CanAsync(
                        RequestCaller(ctx), objectKind, objectName,
                        OrchestratorObjectPermission.Manage, owner.Owner, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                return Results.Ok(await grants.GetObjectGrantsAsync(objectKind, objectName, ctx.RequestAborted));
            }).WithName("getOrchestratorObjectGrants");

            app.MapPut("/api/authorization/{kind}/{name}/{principalKind}/{principalId}", async (
                HttpContext ctx,
                string kind,
                string name,
                string principalKind,
                string principalId,
                SetObjectGrantRequest request,
                IJobHistoryStore jobs,
                IJobCatalogStore catalog,
                IOrchestratorAuthorizationStore grants,
                OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (!TryParseObjectKind(kind, out var objectKind)
                    || !Enum.TryParse<OrchestratorPrincipalKind>(principalKind, true, out var parsedPrincipalKind)
                    || !Enum.TryParse<OrchestratorObjectPermission>(request.Permission, true, out var permission))
                    return Results.BadRequest(new
                    {
                        Error = "Use JOB|SCHEDULE|NOTIFICATION, USER|GROUP|SERVICE, and READ|EXECUTE|OVERRIDE|MANAGE."
                    });
                var objectName = Uri.UnescapeDataString(name);
                var normalizedPrincipalId = Uri.UnescapeDataString(principalId).Trim();
                if (normalizedPrincipalId.Length is 0 or > 128)
                    return Results.BadRequest(new { Error = "PrincipalId must contain 1 to 128 characters." });
                var owner = await ReadObjectOwnerAsync(objectKind, objectName, jobs, catalog);
                if (owner.Exists == false) return Results.NotFound();
                var caller = RequestCaller(ctx);
                if (!await authorization.CanAsync(
                        caller, objectKind, objectName,
                        OrchestratorObjectPermission.Manage, owner.Owner, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                var grant = new OrchestratorObjectGrant(
                    objectKind, objectName, parsedPrincipalKind, normalizedPrincipalId,
                    permission, caller.PrincipalKey);
                await grants.SaveObjectGrantAsync(grant, ctx.RequestAborted);
                EmitObjectAuthorizationAudit(caller.AuditActor, grant, "GRANT", ctx.TraceIdentifier);
                return Results.Ok(grant);
            }).WithName("setOrchestratorObjectGrant");

            app.MapDelete("/api/authorization/{kind}/{name}/{principalKind}/{principalId}", async (
                HttpContext ctx,
                string kind,
                string name,
                string principalKind,
                string principalId,
                IJobHistoryStore jobs,
                IJobCatalogStore catalog,
                IOrchestratorAuthorizationStore grants,
                OrchestratorObjectAuthorizationService authorization,
                IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (!TryParseObjectKind(kind, out var objectKind)
                    || !Enum.TryParse<OrchestratorPrincipalKind>(principalKind, true, out var parsedPrincipalKind))
                    return Results.BadRequest(new { Error = "Kind or principal kind is invalid." });
                var objectName = Uri.UnescapeDataString(name);
                var normalizedPrincipalId = Uri.UnescapeDataString(principalId).Trim();
                var owner = await ReadObjectOwnerAsync(objectKind, objectName, jobs, catalog);
                if (owner.Exists == false) return Results.NotFound();
                var caller = RequestCaller(ctx);
                if (!await authorization.CanAsync(
                        caller, objectKind, objectName,
                        OrchestratorObjectPermission.Manage, owner.Owner, ctx.RequestAborted))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                var deleted = await grants.DeleteObjectGrantAsync(
                    objectKind, objectName, parsedPrincipalKind, normalizedPrincipalId, ctx.RequestAborted);
                if (deleted)
                {
                    EmitObjectAuthorizationAudit(
                        caller.AuditActor,
                        new OrchestratorObjectGrant(
                            objectKind, objectName, parsedPrincipalKind, normalizedPrincipalId,
                            OrchestratorObjectPermission.Read, caller.PrincipalKey),
                        "REVOKE",
                        ctx.TraceIdentifier);
                }
                return deleted ? Results.NoContent() : Results.NotFound();
            }).WithName("deleteOrchestratorObjectGrant");

            // ── Script browser ────────────────────────────────────────────────

            app.MapGet("/api/scripts", (HttpContext ctx, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var root = GetScriptRoot(cfg);
                if (!Directory.Exists(root))
                    return Results.Ok(new { Root = root, Files = Array.Empty<string>() });

                var files = Directory.EnumerateFiles(root, "*.etlsql", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                    .OrderBy(f => f)
                    .ToArray();

                return Results.Ok(new { Root = root, Files = files });
            }).WithName("listScripts");

            app.MapGet("/api/scripts/content", async (HttpContext ctx, IConfiguration cfg, string path) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(path))
                    return Results.BadRequest(new { Error = "path query parameter is required." });

                var fullRoot = Path.GetFullPath(GetScriptRoot(cfg));
                if (!Directory.Exists(fullRoot))
                    return Results.NotFound(new { Error = $"Script '{path}' not found." });

                // Allowlist: only an actual *.etlsql script under the configured root may be read. The
                // request value is used solely to select a match; the path handed to the reader comes
                // from the directory enumeration, never directly from the request. This both prevents
                // traversal outside the root and reading non-script files.
                var requested = Path.GetFullPath(Path.Combine(fullRoot, path));
                var match = Directory.EnumerateFiles(fullRoot, "*.etlsql", SearchOption.AllDirectories)
                    .FirstOrDefault(f => string.Equals(Path.GetFullPath(f), requested, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return Results.NotFound(new { Error = $"Script '{path}' not found." });

                var content = await File.ReadAllTextAsync(match);
                return Results.Ok(new { Path = path, Content = content });
            }).WithName("getScriptContent");

            // ── Published bundle lockbox ───────────────────────────────────

            app.MapGet("/api/bundles", async (HttpContext ctx, IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                return Results.Ok(await store.GetBundlesAsync());
            }).WithName("listBundles");

            app.MapPost("/api/bundles", async (HttpContext ctx, PublishBundleApiRequest req,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (req.Bundle == null)
                    return Results.BadRequest(new { Error = "Bundle is required." });
                if (string.IsNullOrWhiteSpace(req.Bundle.BundleName))
                    return Results.BadRequest(new { Error = "BundleName is required." });
                if (string.IsNullOrWhiteSpace(req.Bundle.EntryPath))
                    return Results.BadRequest(new { Error = "EntryPath is required." });
                if (req.Bundle.Files == null || req.Bundle.Files.Count == 0)
                    return Results.BadRequest(new { Error = "At least one file is required." });

                var reencrypted = BundlePublishSupport.ReEncryptRequest(req.Bundle, req.Password, SecurityService.GetMachineKey());
                var version = await store.PublishBundleAsync(reencrypted);
                return Results.Ok(version);
            }).WithName("publishBundle");

            app.MapGet("/api/bundles/{name}/versions", async (HttpContext ctx, string name,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                return Results.Ok(await store.GetVersionsAsync(Uri.UnescapeDataString(name)));
            }).WithName("listBundleVersions");

            app.MapGet("/api/bundles/{name}/versions/{version:int}", async (HttpContext ctx, string name, int version,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var result = await store.GetVersionAsync(Uri.UnescapeDataString(name), version);
                return result == null ? Results.NotFound(new { Error = $"Bundle '{name}' version {version} not found." }) : Results.Ok(result);
            }).WithName("getBundleVersion");

            app.MapGet("/api/bundles/{name}/versions/{version:int}/files", async (HttpContext ctx, string name, int version,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (await store.GetVersionAsync(Uri.UnescapeDataString(name), version) == null)
                    return Results.NotFound(new { Error = $"Bundle '{name}' version {version} not found." });
                return Results.Ok(await store.GetFilesAsync(Uri.UnescapeDataString(name), version));
            }).WithName("listBundleFiles");

            app.MapGet("/api/bundles/{name}/versions/{version:int}/dependencies", async (HttpContext ctx, string name, int version,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (await store.GetVersionAsync(Uri.UnescapeDataString(name), version) == null)
                    return Results.NotFound(new { Error = $"Bundle '{name}' version {version} not found." });
                return Results.Ok(await store.GetDependenciesAsync(Uri.UnescapeDataString(name), version));
            }).WithName("listBundleDependencies");

            // ── Service management ────────────────────────────────────────────

            app.MapGet("/management/status", (HttpContext ctx, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var proc = Process.GetCurrentProcess();
                return Results.Ok(new
                {
                    Status = "Running",
                    UptimeSeconds = (DateTime.UtcNow - _startTime).TotalSeconds,
                    ProcessId = proc.Id,
                    StartedAt = _startTime,
                    Version = typeof(JobApiEndpoints).Assembly.GetName().Version?.ToString() ?? "unknown"
                });
            }).WithName("serviceStatus");

            app.MapPost("/management/stop", (HttpContext ctx, IConfiguration cfg,
                IHostApplicationLifetime lifetime, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                logger.LogWarning("Graceful stop requested via management API.");
                lifetime.StopApplication();
                return Results.Ok(new { Message = "Stop signal sent. Service will shut down and restart if managed by OS supervisor." });
            }).WithName("serviceStop");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string? _cachedApiKey;
        private static string[] _cachedPreviousApiKeys = Array.Empty<string>();
        private static byte[]? _cachedApiKeyDigest;
        private static byte[][] _cachedPreviousApiKeyDigests = Array.Empty<byte[]>();
        private static string? _cachedScriptRoot;
        private static string? _cachedConfigFingerprint;
        private static readonly object _configLock = new();
        private static bool _configInitialized = false;

        private static void InitializeCache(IConfiguration cfg)
        {
            var apiKey = cfg["Orchestrator:ApiKey"];
            var previousApiKeys = cfg.GetSection("Orchestrator:PreviousApiKeys").Get<string[]>() ?? Array.Empty<string>();
            var maxPreviousApiKeys = Math.Max(0, cfg.GetValue<int?>("Orchestrator:MaxPreviousApiKeys") ?? 1);
            var scriptRoot = cfg["Orchestrator:ScriptRoot"] ?? AppDomain.CurrentDomain.BaseDirectory;
            var fingerprint = string.Join('\u001f', [apiKey ?? "", scriptRoot, maxPreviousApiKeys.ToString(), .. previousApiKeys]);
            if (_configInitialized && string.Equals(_cachedConfigFingerprint, fingerprint, StringComparison.Ordinal))
                return;

            lock (_configLock)
            {
                if (_configInitialized && string.Equals(_cachedConfigFingerprint, fingerprint, StringComparison.Ordinal))
                    return;

                _cachedApiKey = apiKey;
                _cachedPreviousApiKeys = previousApiKeys;
                _cachedScriptRoot = scriptRoot;
                _cachedApiKeyDigest = null;

                if (!string.IsNullOrWhiteSpace(_cachedApiKey))
                {
                    _cachedApiKeyDigest = SHA256.HashData(Encoding.UTF8.GetBytes(_cachedApiKey));
                }

                var digests = new List<byte[]>();
                foreach (var k in _cachedPreviousApiKeys.Take(maxPreviousApiKeys).Where(key => !string.IsNullOrWhiteSpace(key)))
                {
                    digests.Add(SHA256.HashData(Encoding.UTF8.GetBytes(k)));
                }
                _cachedPreviousApiKeyDigests = digests.ToArray();

                _cachedConfigFingerprint = fingerprint;
                _configInitialized = true;
            }
        }

        private const string CallerItemKey = "ETL_SQL.Orchestrator.VerifiedCaller";

        /// <summary>The cryptographically verified caller, or the explicit legacy service principal.</summary>
        internal static OrchestratorCaller RequestCaller(HttpContext ctx) =>
            ctx.Items.TryGetValue(CallerItemKey, out var value) && value is OrchestratorCaller caller
                ? caller
                : new OrchestratorCaller("service", "unverified", "Unverified caller", [], []);

        internal static bool TryResolveRequestTenant(
            HttpContext context,
            IConfiguration configuration,
            out string? tenantId)
        {
            tenantId = RequestCaller(context).TenantId;
            var configuredValue = configuration["Orchestrator:TenantId"];
            if (string.IsNullOrWhiteSpace(configuredValue))
                return true;

            var configured = TenantId.FromTrustedSource(configuredValue).Value;
            if (tenantId is not null && !string.Equals(tenantId, configured, StringComparison.Ordinal))
                return false;
            tenantId = configured;
            return true;
        }

        internal static bool CanAccessJobTenant(JobDefinition job, string? requestTenant) =>
            job.TenantId is null ||
            string.Equals(job.TenantId, requestTenant, StringComparison.Ordinal);

        /// <summary>Stable, sanitized audit identity derived only from the verified assertion.</summary>
        internal static string RequestActor(HttpContext ctx)
        {
            var actor = RequestCaller(ctx).AuditActor;
            return actor.Length <= 192 ? actor : actor[..192];
        }

        private static ExecutionIdentity ToExecutionIdentity(OrchestratorCaller caller) => new()
        {
            EffectiveUser = caller.PrincipalKey,
            RealUser = caller.AuditActor,
            IsAdmin = caller.IsInRole("Admin"),
            TenantId = caller.TenantId,
            Roles = caller.Roles,
            Groups = caller.GroupIds
        };

        private static bool CanAccessAdHoc(OrchestratorCaller caller, JobEntry entry) =>
            caller.IsInRole("Admin")
            || string.Equals(caller.PrincipalKey, entry.OwnerPrincipal, StringComparison.OrdinalIgnoreCase);

        private static async Task<IReadOnlyList<T>> FilterReadableJobRowsAsync<T>(
            IReadOnlyList<T> rows,
            Func<T, string> jobName,
            OrchestratorCaller caller,
            IJobHistoryStore jobs,
            OrchestratorObjectAuthorizationService authorization,
            CancellationToken cancellationToken)
        {
            var access = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var visible = new List<T>();
            foreach (var row in rows)
            {
                var name = jobName(row);
                if (!access.TryGetValue(name, out var canRead))
                {
                    var job = await jobs.GetJobAsync(name);
                    canRead = job is not null && await authorization.CanAsync(
                        caller, OrchestratorObjectKind.Job, name,
                        OrchestratorObjectPermission.Read, job.CreatedBy, cancellationToken);
                    access[name] = canRead;
                }
                if (canRead) visible.Add(row);
            }
            return visible;
        }

        internal static bool TryNormalizeVariableOverrides(
            IReadOnlyDictionary<string, string>? requested,
            out IReadOnlyDictionary<string, string> normalized,
            out string? error)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            normalized = result;
            error = null;
            if (requested is null || requested.Count == 0) return true;
            if (requested.Count > 32)
            {
                error = "At most 32 variable overrides may be supplied.";
                return false;
            }

            foreach (var (rawName, value) in requested)
            {
                var name = rawName?.Trim() ?? string.Empty;
                if (name.StartsWith('@')) name = name[1..];
                if (name.Length is 0 or > 128 || !IsVariableName(name))
                {
                    error = $"Variable name '{rawName}' is invalid. Use letters, numbers, and underscores, beginning with a letter or underscore.";
                    return false;
                }
                if (value is null || value.Length > 4096)
                {
                    error = $"Variable '@{name}' must have a value no longer than 4096 characters.";
                    return false;
                }
                if (!result.TryAdd("@" + name, value))
                {
                    error = $"Variable '@{name}' was supplied more than once.";
                    return false;
                }
            }
            return true;
        }

        private static bool IsVariableName(string name) =>
            (char.IsLetter(name[0]) || name[0] == '_')
            && name.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');

        private static void EmitVariableOverrideAudit(
            string actor,
            string jobName,
            IEnumerable<string> variableNames,
            string correlationId)
        {
            var names = variableNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                SecurityEventSeverity.Warning,
                SecurityEventType.OverrideAttempt,
                actor,
                actor,
                $"JOB:{jobName}",
                SecurityEventDecision.Allowed,
                $"MANUAL_TRIGGER_VARIABLE_OVERRIDE: Count={names.Length}; Names={string.Join(',', names)}") with
            {
                HostName = Environment.MachineName,
                JobId = jobName,
                CorrelationId = correlationId
            });
        }

        private static void EmitNamedCheckpointResumeAudit(
            string actor,
            long historyId,
            string checkpointLabel,
            string correlationId)
        {
            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                SecurityEventSeverity.Warning,
                SecurityEventType.CatalogMutation,
                actor,
                actor,
                $"JOB_RUN:{historyId}",
                SecurityEventDecision.Allowed,
                $"NAMED_CHECKPOINT_RESUME: Label={checkpointLabel}") with
            {
                HostName = Environment.MachineName,
                CorrelationId = correlationId
            });
        }

        private static bool TryParseObjectKind(string raw, out OrchestratorObjectKind objectKind) =>
            Enum.TryParse(raw, ignoreCase: true, out objectKind);

        private static async Task<(bool Exists, string? Owner)> ReadObjectOwnerAsync(
            OrchestratorObjectKind objectKind,
            string objectName,
            IJobHistoryStore jobs,
            IJobCatalogStore catalog) => objectKind switch
            {
                OrchestratorObjectKind.Job => await jobs.GetJobAsync(objectName) is { } job
                    ? (true, job.CreatedBy)
                    : (false, null),
                OrchestratorObjectKind.Schedule => await catalog.GetScheduleAsync(objectName) is { } schedule
                    ? (true, schedule.CreatedBy)
                    : (false, null),
                OrchestratorObjectKind.Notification => await catalog.GetNotificationAsync(objectName) is { } notification
                    ? (true, notification.CreatedBy)
                    : (false, null),
                _ => (false, null)
            };

        private static void EmitObjectAuthorizationAudit(
            string actor,
            OrchestratorObjectGrant grant,
            string action,
            string correlationId)
        {
            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                SecurityEventSeverity.Warning,
                SecurityEventType.CatalogMutation,
                actor,
                actor,
                $"{grant.ObjectKind.ToString().ToUpperInvariant()}:{grant.ObjectName}",
                SecurityEventDecision.Allowed,
                $"ACL_{action}: {grant.PrincipalKind}:{grant.PrincipalId}; Permission={grant.Permission}") with
            {
                HostName = Environment.MachineName,
                CorrelationId = correlationId
            });
        }

        private static bool ApiKeyDenied(HttpContext ctx, IConfiguration cfg)
        {
            InitializeCache(cfg);
            ctx.Request.Headers.TryGetValue("X-Orchestrator-Key", out var provided);
            if (!ApiKeyAcceptedInternal(provided.ToString())) return true;
            return !FederatedIdentityAccepted(ctx, cfg);
        }

        internal static bool FederatedIdentityAccepted(HttpContext ctx, IConfiguration cfg)
        {
            var signingSecret = cfg["Orchestrator:IdentitySigningSecret"];
            var required = cfg.GetValue<bool?>("Orchestrator:RequireFederatedIdentity")
                ?? OrchestratorStartup.BindsToNonLoopback(cfg);
            if (!required)
            {
                ctx.Items[CallerItemKey] = new OrchestratorCaller(
                    "service", "legacy-api-key", "Legacy API-key client", [], []);
                return true;
            }
            if (string.IsNullOrWhiteSpace(signingSecret)) return false;

            ctx.Request.Headers.TryGetValue(OrchestratorIdentityAssertion.HeaderName, out var assertion);
            if (!OrchestratorIdentityAssertion.TryValidate(
                    assertion.ToString(), signingSecret, out var caller, out _)
                || caller is null)
                return false;

            ctx.Items[CallerItemKey] = caller;
            return true;
        }

        private static bool ApiKeyAcceptedForSensitivePayload(HttpContext ctx, IConfiguration cfg)
        {
            InitializeCache(cfg);
            ctx.Request.Headers.TryGetValue("X-Orchestrator-Key", out var provided);
            return ApiKeyAcceptedInternal(provided.ToString(), requireConfiguredKey: true);
        }

        internal static bool ApiKeyAcceptedInternal(
            string? provided,
            bool requireConfiguredKey = false)
        {
            var configuredCount = (_cachedApiKeyDigest != null ? 1 : 0) + _cachedPreviousApiKeyDigests.Length;
            if (configuredCount == 0)
                return !requireConfiguredKey;
            if (string.IsNullOrEmpty(provided))
                return false;

            var providedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(provided));

            if (_cachedApiKeyDigest != null && CryptographicOperations.FixedTimeEquals(_cachedApiKeyDigest, providedDigest))
                return true;

            foreach (var prevDigest in _cachedPreviousApiKeyDigests)
            {
                if (CryptographicOperations.FixedTimeEquals(prevDigest, providedDigest))
                    return true;
            }

            return false;
        }

        internal static bool ApiKeyAccepted(
            IConfiguration cfg,
            string? provided,
            bool requireConfiguredKey = false)
        {
            InitializeCache(cfg);
            return ApiKeyAcceptedInternal(provided, requireConfiguredKey);
        }

        private static string GetScriptRoot(IConfiguration cfg)
        {
            InitializeCache(cfg);
            return _cachedScriptRoot!;
        }

        private static async Task<string> PinBundlePathsAsync(string scriptText, IBundleStore store)
        {
            var parsed = new Parser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
            if (parsed.Statements.Count != 1 || parsed.Statements[0] is not RunScriptStatement run)
                return scriptText;
            if (run.PathExpression is not LiteralExpression lit || lit.Value is not string path)
                return scriptText;
            if (!BundleUri.TryParse(path, out var uri) || uri == null || uri.Version.HasValue)
                return scriptText;
            var latest = await store.GetLatestVersionAsync(uri.BundleName);
            return latest == null
                ? scriptText
                : new RunScriptStatement(new LiteralExpression(uri.ToPinnedString(latest.Version), TokenType.STRING_LITERAL), run.Parameters).ToSql();
        }

        private static async Task<string> PinBundleTargetAsync(string targetPath, IBundleStore store)
        {
            if (!BundleUri.TryParse(targetPath, out var uri) || uri == null || uri.Version.HasValue)
                return targetPath;
            var latest = await store.GetLatestVersionAsync(uri.BundleName);
            return latest == null ? targetPath : uri.ToPinnedString(latest.Version);
        }

        private static string ReadActor(HttpContext context) => RequestActor(context);

        private static void EmitConnectionAudit(
            string actor,
            string action,
            string? alias,
            string reason,
            bool allowed = true)
        {
            var target = string.IsNullOrWhiteSpace(alias)
                ? "SHARED_CONNECTION:*"
                : $"SHARED_CONNECTION:{alias}";

            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                allowed ? SecurityEventSeverity.Information : SecurityEventSeverity.Error,
                allowed ? SecurityEventType.CatalogMutation : SecurityEventType.OperationDenied,
                actor,
                actor,
                target,
                allowed ? SecurityEventDecision.Allowed : SecurityEventDecision.Denied,
                $"{action}: {reason}") with
            {
                HostName = Environment.MachineName
            });
        }

        private static async Task<string> BuildPrometheusMetricsAsync(
            JobThrottleMetrics metrics,
            int activeProcesses,
            IJobHistoryStore historyStore,
            IHostMetricsStore? hostMetricsStore)
        {
            var labels = new Dictionary<string, string>
            {
                [ObservabilityConventions.PrometheusLabel(ObservabilityConventions.Tags.Environment)] =
                    Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default",
                [ObservabilityConventions.PrometheusLabel(ObservabilityConventions.Tags.Node)] = Environment.MachineName,
                [ObservabilityConventions.PrometheusLabel(ObservabilityConventions.Tags.Component)] = "orchestrator"
            };

            var now = DateTime.UtcNow;
            var windowStart = now.AddHours(-1);
            var completed = (await historyStore.GetCompletedHistoryAsync(windowStart, now, limit: 10000))
                .Where(entry => entry.EndTime is not null)
                .ToList();
            var failed = completed.Count(entry =>
                string.Equals(entry.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Status, "INTERRUPTED", StringComparison.OrdinalIgnoreCase));
            var avgDurationMs = completed.Count == 0
                ? 0
                : completed.Average(entry => Math.Max(0, (entry.EndTime!.Value - entry.StartTime).TotalMilliseconds));
            var rowsProcessed = completed.Sum(entry => Math.Max(0, entry.RowsProcessed));
            var peakMemoryBytes = completed.Count == 0
                ? 0
                : completed.Max(entry => Math.Max(0, entry.PeakMemoryBytes));
            var cpuTimeSeconds = completed.Sum(entry => Math.Max(0, entry.CpuTimeSeconds));

            HostMetricSample? host = null;
            if (hostMetricsStore is not null)
            {
                host = (await hostMetricsStore.GetHostMetricsAsync(Environment.MachineName, windowStart, limit: 1))
                    .FirstOrDefault();
            }

            var sb = new StringBuilder();
            AppendGauge(sb, "etlsql_orchestrator_jobs_active",
                "Currently active Orchestrator jobs.", metrics.ActiveJobs, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_queued",
                "Currently queued Orchestrator jobs.", metrics.QueuedJobs, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_max",
                "Maximum concurrent Orchestrator jobs allowed.", metrics.MaxJobs, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_available_slots",
                "Available Orchestrator execution slots.", metrics.AvailableSlots, labels);
            AppendGauge(sb, "etlsql_orchestrator_processes_active",
                "Currently active child processes.", activeProcesses, labels);
            AppendGauge(sb, "etlsql_orchestrator_database_reachable",
                "Whether the Orchestrator database was reachable while composing this metrics snapshot.", 1, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_completed_1h",
                "Orchestrator jobs completed in the last hour.", completed.Count, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_failed_1h",
                "Orchestrator jobs failed or interrupted in the last hour.", failed, labels);
            AppendGauge(sb, "etlsql_orchestrator_job_duration_average_ms_1h",
                "Average Orchestrator job execution duration in milliseconds over the last hour.", avgDurationMs, labels);
            AppendGauge(sb, "etlsql_orchestrator_rows_processed_1h",
                "Rows processed by Orchestrator jobs completed in the last hour.", rowsProcessed, labels);
            AppendGauge(sb, "etlsql_orchestrator_peak_memory_bytes_1h",
                "Maximum peak memory reported by Orchestrator jobs completed in the last hour.", peakMemoryBytes, labels);
            AppendGauge(sb, "etlsql_orchestrator_cpu_time_seconds_1h",
                "Total CPU time reported by Orchestrator jobs completed in the last hour.", cpuTimeSeconds, labels);
            if (host is not null)
            {
                AppendGauge(sb, "etlsql_orchestrator_memory_load_percent",
                    "Latest sampled Orchestrator node memory load percentage.", host.MemoryLoadPercent, labels);
                AppendGauge(sb, "etlsql_orchestrator_process_cpu_percent",
                    "Latest sampled Orchestrator process CPU percentage.", host.ProcessCpuPercent, labels);
                if (host.HostCpuPercent is double hostCpuPercent)
                {
                    AppendGauge(sb, "etlsql_orchestrator_host_cpu_percent",
                        "Latest sampled Orchestrator host CPU percentage.", hostCpuPercent, labels);
                }
                AppendGauge(sb, "etlsql_orchestrator_state_disk_free_bytes",
                    "Latest sampled free bytes for Orchestrator state storage.", host.StateDiskFreeBytes, labels);
                AppendGauge(sb, "etlsql_orchestrator_spill_disk_free_bytes",
                    "Latest sampled free bytes for Orchestrator spill storage.", host.SpillDiskFreeBytes, labels);
            }
            AppendRuntimeGauges(sb, labels);
            return sb.ToString();
        }

        private static void AppendRuntimeGauges(StringBuilder sb, IReadOnlyDictionary<string, string> labels)
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();
            AppendGauge(sb, "etlsql_runtime_process_working_set_bytes",
                "Current process working set in bytes.", process.WorkingSet64, labels);
            AppendGauge(sb, "etlsql_runtime_process_private_memory_bytes",
                "Current process private memory in bytes.", process.PrivateMemorySize64, labels);
            AppendGauge(sb, "etlsql_runtime_gc_heap_bytes",
                "Approximate managed heap bytes reported by GC.GetTotalMemory.", GC.GetTotalMemory(forceFullCollection: false), labels);
            AppendGauge(sb, "etlsql_runtime_gc_collections_gen0_total",
                "Total generation 0 garbage collections since process start.", GC.CollectionCount(0), labels);
            AppendGauge(sb, "etlsql_runtime_gc_collections_gen1_total",
                "Total generation 1 garbage collections since process start.", GC.CollectionCount(1), labels);
            AppendGauge(sb, "etlsql_runtime_gc_collections_gen2_total",
                "Total generation 2 garbage collections since process start.", GC.CollectionCount(2), labels);
        }

        private static void AppendGauge(
            StringBuilder sb,
            string name,
            string help,
            double value,
            IReadOnlyDictionary<string, string> labels)
        {
            sb.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
            sb.Append("# TYPE ").Append(name).AppendLine(" gauge");
            sb.Append(name).Append(FormatLabels(labels)).Append(' ')
                .AppendLine(value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string FormatLabels(IReadOnlyDictionary<string, string> labels) =>
            "{" + string.Join(",", labels.Select(label =>
                $"{label.Key}=\"{EscapeLabelValue(label.Value)}\"")) + "}";

        private static string EscapeLabelValue(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

        // ── Ad-hoc job runner (unchanged) ─────────────────────────────────────

        private static async Task RunJobAsync(JobEntry entry, JobSubmitRequest request,
            ExecutionIdentity executionIdentity,
            IServiceScopeFactory scopeFactory, ILogger logger, CancellationToken ct)
        {
            entry.Status = JobRunStatus.Running;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var activity = OrchestratorObservability.StartAdHocJobActivity(entry.JobId, entry.CorrelationId);

            using var scope = scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IScriptExecutor>();

            try
            {
                var result = await executor.ExecuteTextAsync(
                    request.ScriptText,
                    request.SessionId,
                    ct,
                    request.GetLineageJobName(entry.JobId),
                    executionIdentity: executionIdentity);
                entry.RowsProcessed = result.RowsProcessed;
                entry.PeakMemoryBytes = result.PeakMemoryBytes;
                entry.CpuTimeSeconds = result.CpuTimeSeconds;
                entry.Status = result.Success ? JobRunStatus.Completed : JobRunStatus.Failed;
                entry.ErrorMessage = result.ErrorMessage;

                if (result.Success && request.Metadata != null &&
                    request.Metadata.TryGetValue("IsReport", out var isReport) && isReport == "true")
                {
                    logger.LogInformation("Job {JobId} is a report; building manifest", entry.JobId);
                    if (executor is ScriptExecutorAdapter adapter)
                    {
                        var evaluator = adapter.LastEvaluator;
                        if (evaluator != null)
                        {
                            var builder = new ManifestBuilder(evaluator);
                            var manifest = await builder.BuildAsync("remote_script.rptsql");
                            entry.ReportManifestJson = JsonSerializer.Serialize(manifest);

                            var snapshotDir = "Snapshots";
                            Directory.CreateDirectory(snapshotDir);
                            var reportId = request.Metadata.GetValueOrDefault("ReportId", "unknown");
                            var sessionId = request.SessionId ?? entry.JobId;
                            if (reportId.Contains("..") || reportId.Contains('/') || reportId.Contains('\\') ||
                                sessionId.Contains("..") || sessionId.Contains('/') || sessionId.Contains('\\'))
                            {
                                throw new SecurityException("Invalid character or traversal sequence in ReportId or SessionId.");
                            }
                            var manifestPath = Path.Combine(snapshotDir, $"report_{reportId}_{sessionId}.snapshot.json");

                            var store = new SnapshotStore();
                            await store.SaveAsync(manifest, manifestPath);
                            logger.LogInformation("Manifest saved to {Path}", ETL_SQL.Core.Common.LogSanitizer.Clean(manifestPath));
                        }
                    }
                }

                logger.LogInformation("Job {JobId} {Status} in {ElapsedMs}ms, rows={Rows}",
                    entry.JobId, entry.Status, sw.ElapsedMilliseconds, result.RowsProcessed);
            }
            catch (OperationCanceledException)
            {
                entry.Status = JobRunStatus.Cancelled;
                logger.LogInformation("Job {JobId} was cancelled", entry.JobId);
            }
            catch (Exception ex)
            {
                entry.Status = JobRunStatus.Failed;
                entry.ErrorMessage = SecretRedactor.Redact(ex.Message);
                logger.LogError("Job {JobId} failed unexpectedly: {Message}. StackTrace: {Stack}",
                    entry.JobId, entry.ErrorMessage, SecretRedactor.Redact(ex.StackTrace));
            }
            finally
            {
                sw.Stop();
                entry.ExecutionTimeMs = sw.ElapsedMilliseconds;
                OrchestratorObservability.CompleteAdHocJobActivity(
                    activity,
                    entry.JobId,
                    entry.Status,
                    entry.ExecutionTimeMs,
                    entry.RowsProcessed,
                    entry.PeakMemoryBytes,
                    entry.CpuTimeSeconds);
            }
        }

        // ── Request / response models ─────────────────────────────────────────

        private sealed record CreateScheduledJobRequest(
            string Name,
            string? ScriptText = null,
            int? Interval = null,
            string? Unit = null,
            string? AtTime = null,
            int? MaxRetries = null,
            int? RetryDelaySeconds = null,
            string? HashPolicy = "Warn",
            string? JobType = null,
            string? TargetPath = null,
            string? DisplayName = null,
            string? Description = null,
            Dictionary<string, string>? Options = null,
            string? Mode = null
        );

        private sealed record UpdateScheduledJobRequest(
            string? ScriptText = null,
            int? Interval = null,
            string? Unit = null,
            string? AtTime = null,
            bool? IsEnabled = null,
            int? MaxRetries = null,
            int? RetryDelaySeconds = null,
            string? HashPolicy = null,
            string? TargetPath = null,
            string? DisplayName = null,
            string? Description = null,
            Dictionary<string, string>? Options = null
        );

        private sealed record TriggerScheduledJobRequest(
            Dictionary<string, string>? Variables = null
        );

        private sealed record SetObjectGrantRequest(string Permission);

        private sealed record DispatchNotificationApiRequest(
            string? SourceKind = null,
            string? Title = null,
            string? Text = null,
            string? Trigger = null,
            string? Status = null,
            string? JobName = null,
            string? AlertName = null,
            string? ReportId = null,
            long? HistoryId = null,
            long? RowsProcessed = null,
            string? RecipientOverride = null,
            string? ErrorMessage = null,
            IReadOnlyList<string>? AttachmentPaths = null
        );

        private static async Task<Dictionary<string, string>> ResolveSecretReferencesAsync(
            IReadOnlyDictionary<string, string> values,
            ISecretProvider secrets,
            CancellationToken ct)
        {
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in values)
                resolved[key] = await ResolveSecretReferenceAsync(value, secrets, ct) ?? string.Empty;
            return resolved;
        }

        private static async Task<string?> ResolveSecretReferenceAsync(
            string? value,
            ISecretProvider secrets,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase))
                return value;
            var name = value["SECRET:".Length..].Trim();
            return (await secrets.ResolveAsync(name, ct)).Value;
        }

        private sealed record SetConnectionCatalogEntryRequest(
            string ConnectorType,
            string? Target,
            Dictionary<string, string>? Options,
            List<string>? SensitiveFields = null
        );

        private sealed record ConnectionCatalogEntryResponse(
            string Alias,
            string ConnectorType,
            string? Target,
            Dictionary<string, string> Options,
            string Status,
            IReadOnlyCollection<string>? SensitiveFields = null
        );

        private sealed record ImpactConsumerResponse(
            string Type,
            string Name,
            string? Detail,
            DateTime? LastUsedAtUtc,
            long? UseCount);

        private sealed record ImpactReportResponse(
            string Reference,
            int ConsumerCount,
            IReadOnlyList<ImpactConsumerResponse> Consumers);

        private static ConnectionCatalogEntryResponse ToConnectionCatalogEntry(
            string alias,
            SecretLifecycleStatus status,
            SharedConnectionDefinition? definition)
        {
            var options = definition?.Options is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(definition.Options, StringComparer.OrdinalIgnoreCase);
            return new ConnectionCatalogEntryResponse(
                alias,
                definition?.ConnectorType ?? "",
                definition?.Target,
                options,
                status.ToString().ToLowerInvariant(),
                definition?.SensitiveFields);
        }

        private static async Task<StewardshipEvaluation> EvaluateStewardshipAsync(
            ILineageCatalogStore store, int limit)
        {
            var history = await store.GetRecentLineageAsync(Math.Clamp(limit, 1, 10000));
            WorkspacePolicyDocument? policy = null;
            var policyPath = WorkspacePolicyLoader.Find(Environment.CurrentDirectory);
            if (policyPath != null)
            {
                var loaded = WorkspacePolicyLoader.Load(policyPath);
                if (!loaded.IsValid)
                    throw new InvalidOperationException($"Workspace policy '{policyPath}' is invalid.");
                policy = loaded.Policy;
            }
            return StewardshipScoring.Evaluate(StewardshipScoring.FromHistory(history), policy);
        }

        private static async Task<string?> ReadBoundedAsync(string path, CancellationToken ct)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 1024 * 1024)
                    return null;
                return await File.ReadAllTextAsync(path, ct);
            }
            catch
            {
                return null;
            }
        }

        private static bool ContainsReference(string? text, string reference)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            var index = 0;
            while ((index = text.IndexOf(reference, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = index + reference.Length;
                if (end >= text.Length || !IsNameChar(text[end]))
                    return true;
                index = end;
            }

            return false;
        }

        private static bool IsNameChar(char c) =>
            char.IsLetterOrDigit(c) || c is '_' or '.' or '-';

        private static long? ReadExpectedVersion(HttpContext context)
        {
            var value = context.Request.Headers.IfMatch.ToString().Trim().Trim('"');
            return long.TryParse(value, out var version) && version > 0 ? version : null;
        }

        private sealed record PublishBundleApiRequest(
            BundlePublishRequest Bundle,
            string? Password = null
        );

        private sealed record SharedTenantLifecycleRequest(
            string OperationId,
            string Kind,
            string AuthorizationReference,
            string TargetRelease,
            int MaxConcurrentJobs,
            int MaxStorageMb,
            int MaxReportSessions);

        private sealed class JobEntry(
            string jobId,
            CancellationTokenSource cts,
            string? correlationId,
            string ownerPrincipal)
        {
            public string JobId { get; } = jobId;
            public CancellationTokenSource Cts { get; } = cts;
            public string? CorrelationId { get; } = correlationId;
            public string OwnerPrincipal { get; } = ownerPrincipal;
            public JobRunStatus Status { get; set; } = JobRunStatus.Queued;
            public long RowsProcessed { get; set; }
            public long ExecutionTimeMs { get; set; }
            public long PeakMemoryBytes { get; set; }
            public double CpuTimeSeconds { get; set; }
            public string? ErrorMessage { get; set; }
            public string? ReportManifestJson { get; set; }
        }
    }
}
