using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

public sealed record HostedJobItemDto(
    string Kind,
    string JobName,
    string DisplayName,
    int? PipelineId,
    long? PipelineVersion,
    string? FolderPath,
    bool IsEnabled,
    string? LastStatus,
    long? LastRunId,
    DateTime? LastRunAt,
    long? ActiveRunId,
    DateTime? NextScheduledRunAt,
    bool CanRun,
    bool CanStop,
    string PortalPath);

public sealed record HostedJobsPageDto(
    IReadOnlyList<HostedJobItemDto> Items,
    int Total,
    int Offset,
    int Limit,
    bool HasMore,
    bool OrchestratorOnline);

public sealed record HostedJobRecentRunDto(
    long RunId,
    string JobName,
    string Status,
    bool IsTerminal,
    DateTime StartTime,
    DateTime? EndTime,
    long? DurationMs,
    long RowsProcessed,
    string? ErrorMessage,
    bool CanStop,
    string PortalPath);

public sealed record HostedJobRunStatementDto(
    string Statement,
    long DurationMs,
    long RowsProcessed,
    bool Failed);

public sealed record HostedJobRunDetailDto(
    long RunId,
    string JobName,
    string Status,
    bool IsTerminal,
    DateTime StartTime,
    DateTime? EndTime,
    string? ErrorMessage,
    long RowsProcessed,
    bool LogTruncated,
    IReadOnlyList<HostedJobRunStatementDto> Statements);

public sealed record HostedJobTriggerResponseDto(string JobName, string Message);

public sealed record HostedJobCancelResponseDto(string JobName, long RunId, string Message);

/// <summary>
/// Minimal, read-first Portal contract for the VS Code Hosted Jobs &amp; Recent Runs tree (ST59).
/// Filters by tenant and caller before paging, strips script bodies/credentials/admin data,
/// returns only relative Portal paths, and rechecks authoritative grants on every Run and Stop.
/// </summary>
[ApiController]
[Route("api/studio/hosted-jobs")]
[Authorize]
[RequirePortalModule("Designer")]
[RequireStudioCapability(StudioCapabilities.StudioAccess)]
public sealed class StudioHostedJobsController(
    PortalDbContext db,
    FolderPermissionService folderPermissions,
    StudioAuthorizationService studioAuth,
    PortalConfig portalConfig,
    OrchestratorProxyService orchestrator,
    AuditService audit,
    DatasetTenantScope? tenantScope = null,
    PortalTenantCatalogScope? catalogScope = null) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly DatasetTenantScope _tenantScope = tenantScope ?? new DatasetTenantScope(portalConfig);
    private PortalTenantCatalogScope CatalogScope => catalogScope ?? new PortalTenantCatalogScope(db, _tenantScope);
    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    public async Task<ActionResult<HostedJobsPageDto>> List(
        CancellationToken ct,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        Response.Headers.CacheControl = "no-store";
        if (limit is < 1 or > 100 || offset < 0)
            return BadRequest(new PipelineRunRefusalDto("invalid_paging", "Invalid limit or offset."));

        var canRunCapability = CallerCanRun();

        // 1. Authorized saved Portal pipelines for this tenant and caller.
        var pipelineCandidates = CatalogScope.Documents.AsNoTracking()
            .Where(report => !report.IsDeleted && report.Kind == CatalogDocumentKind.Pipeline);
        var visiblePipelines = await folderPermissions.FilterVisibleReportsAsync(
            pipelineCandidates, FolderPermission.Execute, User, ct);
        var pipelineList = await visiblePipelines
            .Include(report => report.Folder)
            .OrderBy(report => report.Id)
            .ToListAsync(ct);
        var pipelinePerms = await folderPermissions.GetEffectiveReportPermissionsAsync(pipelineList, User, ct);
        var authorizedPipelines = pipelineList
            .Where(report => pipelinePerms[report.Id].AtLeast(FolderPermission.Execute))
            .ToDictionary(report => StudioPipelineRunsController.JobName(report.Id), StringComparer.OrdinalIgnoreCase);

        // 2. Authorized Orchestrator jobs for this tenant and caller (filtered by Orchestrator before return).
        var authorizedActivity = await ReadFederatedActivityAsync(ct);
        var orchestratorOnline = authorizedActivity is not null;
        var activityByJob = authorizedActivity ?? new Dictionary<string, ScheduledJobActivity>(StringComparer.OrdinalIgnoreCase);

        // 3. Merge authorized saved pipelines + authorized non-pipeline Orchestrator jobs BEFORE paging.
        var merged = new List<(string Kind, string JobName, string DisplayName, Report? Pipeline, ScheduledJobActivity? Activity)>();

        foreach (var (jobName, pipeline) in authorizedPipelines)
        {
            activityByJob.TryGetValue(jobName, out var activity);
            merged.Add(("pipeline", jobName, pipeline.Name, pipeline, activity));
        }

        foreach (var (jobName, activity) in activityByJob)
        {
            if (jobName.StartsWith("PIPELINE:", StringComparison.OrdinalIgnoreCase))
                continue;
            var displayName = string.IsNullOrWhiteSpace(activity.Job.DisplayName)
                ? activity.Job.Name
                : activity.Job.DisplayName;
            merged.Add(("job", jobName, displayName, null, activity));
        }

        var ordered = merged
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.JobName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = ordered.Count;
        var slice = ordered.Skip(offset).Take(limit).ToList();

        // 4. Enrich only the paged slice with latest run status / active run ID (strip all sensitive fields).
        var resultItems = new List<HostedJobItemDto>(slice.Count);
        foreach (var entry in slice)
        {
            string? lastStatus = null;
            long? lastRunId = null;
            DateTime? lastRunAt = entry.Activity?.Job.LastRun;
            long? activeRunId = null;
            var canCancel = false;

            if (entry.Activity is not null)
            {
                var hist = await orchestrator.ReadHistoryAsync(entry.JobName, 1, ct, requireVerifiedCaller: true);
                var latest = hist.StatusCode == StatusCodes.Status200OK && hist.Value is { Count: > 0 } ? hist.Value[0] : null;
                if (latest is not null)
                {
                    lastStatus = latest.Status;
                    lastRunId = latest.Id;
                    lastRunAt = latest.StartTime;
                    if (!IsTerminalStatus(latest.Status))
                    {
                        activeRunId = latest.Id;
                        canCancel = latest.CanCancel;
                    }
                }
            }

            var isEnabled = entry.Activity?.Job.IsEnabled ?? true;
            var nextRun = entry.Activity?.Job.DisplayNextRun;
            var portalPath = entry.Pipeline is not null
                ? $"/studio.html?reportId={entry.Pipeline.Id}"
                : $"/orchestrator.html?job={Uri.EscapeDataString(entry.JobName)}";

            resultItems.Add(new HostedJobItemDto(
                Kind: entry.Kind,
                JobName: entry.JobName,
                DisplayName: entry.DisplayName,
                PipelineId: entry.Pipeline?.Id,
                PipelineVersion: entry.Pipeline?.Version,
                FolderPath: entry.Pipeline?.Folder?.Path,
                IsEnabled: isEnabled,
                LastStatus: lastStatus,
                LastRunId: lastRunId,
                LastRunAt: lastRunAt,
                ActiveRunId: activeRunId,
                NextScheduledRunAt: nextRun,
                CanRun: canRunCapability && entry.Activity?.CanExecute == true,
                CanStop: canRunCapability && canCancel,
                PortalPath: portalPath));
        }

        return Ok(new HostedJobsPageDto(
            Items: resultItems,
            Total: total,
            Offset: offset,
            Limit: limit,
            HasMore: offset + resultItems.Count < total,
            OrchestratorOnline: orchestratorOnline));
    }

    [HttpGet("{jobName}/runs")]
    public async Task<ActionResult<IReadOnlyList<HostedJobRecentRunDto>>> ListRuns(
        string jobName,
        CancellationToken ct,
        [FromQuery] int limit = 10)
    {
        Response.Headers.CacheControl = "no-store";
        var unescaped = Uri.UnescapeDataString(jobName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(unescaped))
            return BadRequest(new PipelineRunRefusalDto("invalid_job", "Job name is required."));

        var pipelineCheck = await VerifyPipelineAccessIfApplicableAsync(unescaped, ct);
        if (pipelineCheck is not null) return pipelineCheck;

        if (!await orchestrator.RequiresFederatedIdentityAsync(ct))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("federated_orchestrator_required", "Hosted jobs require a connected Orchestrator with caller authentication."));

        var read = await orchestrator.ReadHistoryAsync(unescaped, Math.Clamp(limit, 1, 50), ct, requireVerifiedCaller: true);
        if (read.StatusCode != StatusCodes.Status200OK)
        {
            return read.StatusCode switch
            {
                StatusCodes.Status403Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                    new PipelineRunRefusalDto("job_read_denied", "You do not have permission to view runs for this job.")),
                StatusCodes.Status404NotFound => Ok(Array.Empty<HostedJobRecentRunDto>()),
                _ => StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new PipelineRunRefusalDto("orchestrator_unreachable", "Could not read job history from the Orchestrator."))
            };
        }

        var canRun = CallerCanRun();
        var runs = (read.Value ?? [])
            .Select(entry =>
            {
                var isTerminal = IsTerminalStatus(entry.Status);
                long? durationMs = entry.EndTime.HasValue
                    ? Math.Max(0, (long)(entry.EndTime.Value - entry.StartTime).TotalMilliseconds)
                    : null;
                return new HostedJobRecentRunDto(
                    RunId: entry.Id,
                    JobName: unescaped,
                    Status: entry.Status,
                    IsTerminal: isTerminal,
                    StartTime: entry.StartTime,
                    EndTime: entry.EndTime,
                    DurationMs: durationMs,
                    RowsProcessed: entry.RowsProcessed,
                    ErrorMessage: Redact(entry.ErrorMessage),
                    CanStop: canRun && !isTerminal && entry.CanCancel,
                    PortalPath: $"/orchestrator.html?job={Uri.EscapeDataString(unescaped)}&run={entry.Id}");
            })
            .ToArray();

        return Ok(runs);
    }

    [HttpGet("{jobName}/runs/{runId:long}")]
    public async Task<ActionResult<HostedJobRunDetailDto>> GetRunDetail(string jobName, long runId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var unescaped = Uri.UnescapeDataString(jobName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(unescaped))
            return BadRequest(new PipelineRunRefusalDto("invalid_job", "Job name is required."));

        var pipelineCheck = await VerifyPipelineAccessIfApplicableAsync(unescaped, ct);
        if (pipelineCheck is not null) return pipelineCheck;

        if (!await orchestrator.RequiresFederatedIdentityAsync(ct))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("federated_orchestrator_required", "Hosted jobs require a connected Orchestrator with caller authentication."));

        using var resp = await orchestrator.GetJobRunAsync(runId, ct, requireVerifiedCaller: true);
        if (resp is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("orchestrator_unreachable", "Could not reach the Orchestrator."));
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden,
                new PipelineRunRefusalDto("run_read_denied", "You do not have permission to read that run."));
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return NotFound(new PipelineRunRefusalDto("run_not_found", $"Run {runId} was not found."));
        if (!resp.IsSuccessStatusCode)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("orchestrator_unreachable", "Could not read run detail from the Orchestrator."));

        var run = await resp.Content.ReadFromJsonAsync<OrchestratorRunPayload>(Json, ct);
        if (run is null || !string.Equals(run.JobName, unescaped, StringComparison.OrdinalIgnoreCase))
            return NotFound(new PipelineRunRefusalDto("run_not_found", $"Run {runId} does not belong to job '{unescaped}'."));

        var statements = (run.Statements ?? [])
            .Select(stmt => new HostedJobRunStatementDto(
                Redact(stmt.Statement) ?? string.Empty,
                stmt.DurationMs,
                stmt.RowsProcessed,
                stmt.Failed))
            .ToArray();

        return Ok(new HostedJobRunDetailDto(
            RunId: run.RunId,
            JobName: unescaped,
            Status: run.Status,
            IsTerminal: run.IsTerminal,
            StartTime: run.StartTime,
            EndTime: run.EndTime,
            ErrorMessage: Redact(run.ErrorMessage),
            RowsProcessed: run.RowsProcessed,
            LogTruncated: run.LogTruncated,
            Statements: statements));
    }

    [HttpPost("{jobName}/trigger")]
    public async Task<IActionResult> TriggerJob(string jobName, CancellationToken ct)
    {
        var unescaped = Uri.UnescapeDataString(jobName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(unescaped))
            return BadRequest(new PipelineRunRefusalDto("invalid_job", "Job name is required."));

        if (!CallerCanRun())
            return StatusCode(StatusCodes.Status403Forbidden,
                new PipelineRunRefusalDto("run_capability_denied", "This account does not hold ScriptRun permission."));

        if (unescaped.StartsWith("PIPELINE:", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new PipelineRunRefusalDto(
                "use_pipeline_run_endpoint",
                "Saved pipelines must be submitted through /api/studio/pipelines/{id}/runs with an explicit saved version."));

        if (!await orchestrator.RequiresFederatedIdentityAsync(ct))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("federated_orchestrator_required", "Hosted jobs require a connected Orchestrator with caller authentication."));

        using var resp = await orchestrator.TriggerJobAsync(unescaped, ct: ct, requireVerifiedCaller: true);
        if (resp is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("orchestrator_unreachable", "Could not reach the Orchestrator."));

        if (resp.StatusCode == HttpStatusCode.Accepted || resp.IsSuccessStatusCode)
        {
            await audit.LogAsync(CurrentUserId, "HOSTED_JOB_TRIGGERED", "Job", unescaped, $"job={unescaped}");
            return Accepted(new HostedJobTriggerResponseDto(unescaped, $"Job '{unescaped}' queued for execution."));
        }

        return resp.StatusCode switch
        {
            HttpStatusCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new PipelineRunRefusalDto("run_grant_denied", "The Orchestrator refused execution for this account.")),
            HttpStatusCode.NotFound => NotFound(new PipelineRunRefusalDto("job_not_found", $"Job '{unescaped}' was not found.")),
            HttpStatusCode.Conflict => Conflict(new PipelineRunRefusalDto("job_already_running", $"Job '{unescaped}' already has a run in progress.")),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("orchestrator_unreachable", "The Orchestrator could not start the job."))
        };
    }

    [HttpPost("{jobName}/runs/{runId:long}/cancel")]
    public async Task<IActionResult> CancelRun(string jobName, long runId, CancellationToken ct)
    {
        var unescaped = Uri.UnescapeDataString(jobName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(unescaped))
            return BadRequest(new PipelineRunRefusalDto("invalid_job", "Job name is required."));

        if (!CallerCanRun())
            return StatusCode(StatusCodes.Status403Forbidden,
                new PipelineRunRefusalDto("cancel_denied", "This account does not hold ScriptRun permission."));

        var pipelineCheck = await VerifyPipelineAccessIfApplicableAsync(unescaped, ct);
        if (pipelineCheck is not null) return pipelineCheck;

        if (!await orchestrator.RequiresFederatedIdentityAsync(ct))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("federated_orchestrator_required", "Hosted jobs require a connected Orchestrator with caller authentication."));

        // Verify the run belongs to this exact job in this tenant before signalling cancellation.
        using (var readResp = await orchestrator.GetJobRunAsync(runId, ct, requireVerifiedCaller: true))
        {
            if (readResp is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new PipelineRunRefusalDto("orchestrator_unreachable", "Could not reach the Orchestrator."));
            if (readResp.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new PipelineRunRefusalDto("cancel_denied", "You do not have permission to cancel that run."));
            if (readResp.StatusCode == HttpStatusCode.NotFound)
                return NotFound(new PipelineRunRefusalDto("run_not_found", $"Run {runId} was not found."));
            if (!readResp.IsSuccessStatusCode)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new PipelineRunRefusalDto("orchestrator_unreachable", "Could not verify the run on the Orchestrator."));

            var run = await readResp.Content.ReadFromJsonAsync<OrchestratorRunPayload>(Json, ct);
            if (run is null || !string.Equals(run.JobName, unescaped, StringComparison.OrdinalIgnoreCase))
                return NotFound(new PipelineRunRefusalDto("run_not_found", $"Run {runId} does not belong to job '{unescaped}'."));
        }

        using var cancelResp = await orchestrator.CancelJobRunAsync(runId, ct, requireVerifiedCaller: true);
        if (cancelResp is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("orchestrator_unreachable", "Could not reach the Orchestrator."));

        if (cancelResp.StatusCode == HttpStatusCode.Accepted)
        {
            await audit.LogAsync(CurrentUserId, "HOSTED_JOB_RUN_CANCEL_REQUESTED", "Job", unescaped, $"job={unescaped}; run={runId}");
            return Accepted(new HostedJobCancelResponseDto(unescaped, runId, $"Cancellation requested for run #{runId}."));
        }

        return cancelResp.StatusCode switch
        {
            HttpStatusCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new PipelineRunRefusalDto("cancel_denied", "The Orchestrator refused cancellation for this account.")),
            HttpStatusCode.Conflict => Conflict(new PipelineRunRefusalDto("run_already_finished", $"Run {runId} has already finished.")),
            HttpStatusCode.NotFound => NotFound(new PipelineRunRefusalDto("run_not_found", $"Run {runId} was not found.")),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new PipelineRunRefusalDto("orchestrator_unreachable", "Could not signal cancellation on the Orchestrator."))
        };
    }

    private async Task<Dictionary<string, ScheduledJobActivity>?> ReadFederatedActivityAsync(CancellationToken ct)
    {
        if (!await orchestrator.RequiresFederatedIdentityAsync(ct))
            return null;

        const int pageSize = 1000;
        var activity = new Dictionary<string, ScheduledJobActivity>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        while (true)
        {
            using var response = await orchestrator.GetJobActivityAsync(
                "all", "all", null, pageSize, offset, ct, requireVerifiedCaller: true);
            if (response is null || !response.IsSuccessStatusCode)
                return null;

            var page = await response.Content.ReadFromJsonAsync<ScheduledJobActivityPage>(Json, ct);
            if (page is null || page.Offset != offset)
                return null;

            foreach (var item in page.Items)
                activity[item.Job.Name] = item;

            offset += page.Items.Count;
            if (page.Items.Count == 0 || offset >= page.Total)
                return activity;
        }
    }

    private async Task<ActionResult?> VerifyPipelineAccessIfApplicableAsync(string jobName, CancellationToken ct)
    {
        if (!jobName.StartsWith("PIPELINE:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!int.TryParse(jobName["PIPELINE:".Length..], out var pipelineId) || pipelineId <= 0)
            return NotFound(new PipelineRunRefusalDto("job_not_found", "Pipeline job was not found."));

        var doc = await CatalogScope.Documents.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == pipelineId && !r.IsDeleted && r.Kind == CatalogDocumentKind.Pipeline, ct);
        if (doc is null)
            return NotFound(new PipelineRunRefusalDto("job_not_found", "Pipeline job was not found."));

        var perms = await folderPermissions.GetEffectiveReportPermissionsAsync([doc], User, ct);
        if (!perms.TryGetValue(doc.Id, out var perm) || !perm.AtLeast(FolderPermission.Execute))
            return StatusCode(StatusCodes.Status403Forbidden,
                new PipelineRunRefusalDto("pipeline_access_denied", "You do not have Execute permission on this pipeline."));

        return null;
    }

    private bool CallerCanRun()
    {
        var isNative = string.Equals(
            User.FindFirst(TokenService.IdentityTypeClaim)?.Value,
            TokenService.NativeClientIdentityType,
            StringComparison.Ordinal);
        if (isNative)
        {
            var hasScope = User.FindAll(TokenService.ScopeClaim)
                .Any(c => string.Equals(c.Value, NativeClientScopes.StudioRun, StringComparison.OrdinalIgnoreCase));
            if (!hasScope) return false;
        }

        return studioAuth.HasCapability(User, StudioCapabilities.ScriptRun);
    }

    private static bool IsTerminalStatus(string? status) =>
        status is not null
        && !string.Equals(status, "RUNNING", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(status, "QUEUED", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private static string? Redact(string? text) =>
        string.IsNullOrEmpty(text) ? text : SecretRedactor.Redact(text);

    private sealed record OrchestratorRunStatementPayload(
        string Statement, long DurationMs, long RowsProcessed, bool Failed);

    private sealed record OrchestratorRunPayload(
        long RunId, string JobName, string Status, bool IsTerminal,
        DateTime StartTime, DateTime? EndTime, string? ErrorMessage,
        long RowsProcessed, bool LogTruncated,
        List<OrchestratorRunStatementPayload>? Statements);
}
