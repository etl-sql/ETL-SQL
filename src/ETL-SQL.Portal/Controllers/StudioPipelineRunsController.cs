using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Runs a saved catalog pipeline through the Orchestrator
/// (docs/architecture/decisions/portal-etl-documents.md, D2 A). Nothing here executes a script: the
/// Portal checks the document, reads the saved revision and hands it to the pipeline's Orchestrator
/// job under the caller's own assertion. The Orchestrator decides whether that caller may run it.
/// </summary>
[ApiController]
[Route("api/studio/pipelines")]
[Authorize]
[RequirePortalModule("Designer")]
[RequireStudioCapability(StudioCapabilities.ScriptRun,
    StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
public sealed class StudioPipelineRunsController(
    PortalDbContext db,
    FolderPermissionService folderPermissions,
    IArtifactStorage artifacts,
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

    /// <summary>The Orchestrator job a pipeline document runs through.</summary>
    public static string JobName(int pipelineId) => $"PIPELINE:{pipelineId}";

    [HttpPost("{id:int}/runs")]
    public async Task<IActionResult> Submit(int id, [FromBody] SubmitPipelineRunRequest request, CancellationToken ct)
    {
        var pipeline = await RunnablePipelineAsync(id, ct);
        if (pipeline.Refusal is not null) return pipeline.Refusal;
        var document = pipeline.Document!;

        // Run means the saved revision. An author looking at an older or unsaved one is told so
        // instead of running text they are not looking at.
        if (request.Version != document.Version)
            return Conflict(new PipelineRunRefusalDto(
                "stale_revision",
                $"Run uses the saved revision. This pipeline is at version {document.Version}; save or reload, then run again.",
                document.Version));

        if (!orchestrator.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new PipelineRunRefusalDto(
                "orchestrator_not_connected",
                "No Orchestrator is connected to this Portal, so a pipeline cannot run from here. "
                + "Authoring, validation and preview still work. An administrator connects one under Admin, Orchestrator."));

        var scriptKey = PortalPathGuard.ToScriptKey(portalConfig, document.ScriptPath);
        if (scriptKey is null || !await artifacts.ExistsAsync(ArtifactArea.Scripts, scriptKey, ct))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new PipelineRunRefusalDto(
                "revision_unavailable", "The saved revision of this pipeline could not be read."));
        var scriptText = await artifacts.ReadAllTextAsync(ArtifactArea.Scripts, scriptKey, ct);
        if (string.IsNullOrWhiteSpace(scriptText))
            return UnprocessableEntity(new PipelineRunRefusalDto(
                "empty_revision", "The saved revision of this pipeline is empty, so there is nothing to run."));

        var jobName = JobName(document.Id);
        using var response = await orchestrator.RunJobRevisionAsync(
            jobName, scriptText, document.Name, $"Portal pipeline {document.Folder.Path}/{document.Name}", ct);
        if (response is null) return Unreachable();
        if (response.StatusCode != HttpStatusCode.Accepted)
            return await RelayRefusalAsync(response, jobName, ct);

        var accepted = await response.Content.ReadFromJsonAsync<OrchestratorRunAccepted>(Json, ct);
        var scriptHash = "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(scriptText))).ToLowerInvariant();
        if (accepted is null || accepted.RunId <= 0)
            return StatusCode(StatusCodes.Status502BadGateway, new PipelineRunRefusalDto(
                "orchestrator_answer_unreadable", "The Orchestrator accepted the run but did not name it."));

        // Written at the Portal edge: this is the only place that knows the document and revision
        // behind the run. The Orchestrator records the same run against its job.
        await audit.LogAsync(CurrentUserId, "PIPELINE_RUN_SUBMITTED", "Report", document.Id.ToString(),
            $"version={document.Version}; hash={scriptHash}; job={jobName}; run={accepted.RunId}");
        return Accepted(new PipelineRunSubmittedDto(accepted.RunId, document.Id, document.Version, scriptHash, jobName));
    }

    [HttpGet("{id:int}/runs/{runId:long}")]
    public async Task<IActionResult> GetRun(int id, long runId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var pipeline = await RunnablePipelineAsync(id, ct);
        if (pipeline.Refusal is not null) return pipeline.Refusal;
        if (!orchestrator.IsConfigured) return Unreachable();

        using var response = await orchestrator.GetJobRunAsync(runId, ct);
        if (response is null) return Unreachable();
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
                "run_grant_denied", "The Orchestrator does not let this account read that run."));
        if (!response.IsSuccessStatusCode)
            return response.StatusCode == HttpStatusCode.NotFound ? RunNotFound(runId) : Unreachable();

        var run = await response.Content.ReadFromJsonAsync<OrchestratorJobRun>(Json, ct);
        // A run id is only an id. It is shown under this pipeline only when the Orchestrator says
        // it belongs to this pipeline's job, so another document's run is never displayed here.
        if (run is null || !string.Equals(run.JobName, JobName(id), StringComparison.Ordinal))
            return RunNotFound(runId);

        return Ok(new PipelineRunDto(
            run.RunId, id, run.Status, run.IsTerminal, run.StartTime, run.EndTime, run.ErrorMessage,
            run.RowsProcessed, run.ScriptHash, run.LogTruncated,
            [.. (run.Statements ?? []).Select(statement => new PipelineRunStatementDto(
                statement.Statement, statement.DurationMs, statement.RowsProcessed, statement.Failed))]));
    }

    [HttpPost("{id:int}/runs/{runId:long}/cancel")]
    public async Task<IActionResult> Cancel(int id, long runId, CancellationToken ct)
    {
        var pipeline = await RunnablePipelineAsync(id, ct);
        if (pipeline.Refusal is not null) return pipeline.Refusal;
        if (!orchestrator.IsConfigured) return Unreachable();

        // The run must be this pipeline's before anything is signalled.
        using (var read = await orchestrator.GetJobRunAsync(runId, ct))
        {
            if (read is null) return Unreachable();
            if (read.StatusCode == HttpStatusCode.Forbidden) return CancelDenied(runId);
            if (!read.IsSuccessStatusCode)
                return read.StatusCode == HttpStatusCode.NotFound ? RunNotFound(runId) : Unreachable();
            var run = await read.Content.ReadFromJsonAsync<OrchestratorJobRun>(Json, ct);
            if (run is null || !string.Equals(run.JobName, JobName(id), StringComparison.Ordinal))
                return RunNotFound(runId);
        }

        using var response = await orchestrator.CancelJobRunAsync(runId, ct);
        if (response is null) return Unreachable();
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            await audit.LogAsync(CurrentUserId, "PIPELINE_RUN_CANCEL_REQUESTED", "Report", id.ToString(),
                $"job={JobName(id)}; run={runId}");
            return Accepted(new PipelineRunCancelRequestedDto(runId, id));
        }

        OrchestratorRefusal? refusal = null;
        try { refusal = await response.Content.ReadFromJsonAsync<OrchestratorRefusal>(Json, ct); }
        catch (JsonException) { }
        return (response.StatusCode, refusal?.Code) switch
        {
            (HttpStatusCode.Conflict, "run_already_finished") => Conflict(new PipelineRunRefusalDto(
                "run_already_finished", refusal?.Error ?? $"Run {runId} already finished.")),
            (HttpStatusCode.Conflict, _) => Conflict(new PipelineRunRefusalDto(
                "cancel_not_signalled",
                $"The Orchestrator could not signal run {runId}, so it may still be running. "
                + (refusal?.Error ?? string.Empty))),
            (HttpStatusCode.Forbidden, _) => CancelDenied(runId),
            (HttpStatusCode.NotFound, _) => RunNotFound(runId),
            _ => Unreachable()
        };
    }

    // ── Schedule ─────────────────────────────────────────────────────────────
    //
    // The same handoff as Run, with a cadence. The saved revision is pinned into the pipeline's job
    // and the job is attached to a cron schedule of its own. The schedule keeps running that pinned
    // revision until it is set again, so a later save does not change what runs unattended.

    [HttpGet("{id:int}/schedule")]
    public async Task<IActionResult> GetSchedule(int id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var pipeline = await RunnablePipelineAsync(id, ct);
        if (pipeline.Refusal is not null) return pipeline.Refusal;
        if (!orchestrator.IsConfigured) return NotConnected();

        using var response = await orchestrator.GetJobRevisionScheduleAsync(JobName(id), ct);
        if (response is null) return Unreachable();
        // No job yet is an unscheduled pipeline, not an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Ok(new PipelineScheduleDto(id, false, null, null, false, null, null, null, false, pipeline.Document!.Version));
        if (!response.IsSuccessStatusCode) return await RelayScheduleRefusalAsync(response, JobName(id), ct);
        return Ok(await ToScheduleDtoAsync(pipeline.Document!, response, ct));
    }

    [HttpPut("{id:int}/schedule")]
    public async Task<IActionResult> SetSchedule(int id, [FromBody] SchedulePipelineRequest request, CancellationToken ct)
    {
        var pipeline = await RunnablePipelineAsync(id, ct);
        if (pipeline.Refusal is not null) return pipeline.Refusal;
        var document = pipeline.Document!;
        if (request.Version != document.Version)
            return Conflict(new PipelineRunRefusalDto(
                "stale_revision",
                $"A schedule pins the saved revision. This pipeline is at version {document.Version}; save or reload, then schedule again.",
                document.Version));
        if (string.IsNullOrWhiteSpace(request.Cron))
            return BadRequest(new PipelineRunRefusalDto("invalid_schedule", "A schedule needs a cadence, as a cron expression."));
        if (!orchestrator.IsConfigured) return NotConnected();

        var scriptText = await ReadSavedRevisionAsync(document, ct);
        if (string.IsNullOrWhiteSpace(scriptText))
            return UnprocessableEntity(new PipelineRunRefusalDto(
                "empty_revision", "The saved revision of this pipeline is empty or could not be read, so there is nothing to schedule."));

        var jobName = JobName(document.Id);
        using var response = await orchestrator.ScheduleJobRevisionAsync(
            jobName, scriptText, request.Cron.Trim(), request.TimeZone, document.Name, $"v{document.Version}", ct);
        if (response is null) return Unreachable();
        if (!response.IsSuccessStatusCode) return await RelayScheduleRefusalAsync(response, jobName, ct);

        var schedule = await ToScheduleDtoAsync(document, response, ct);
        await audit.LogAsync(CurrentUserId, "PIPELINE_SCHEDULED", "Report", document.Id.ToString(),
            $"version={document.Version}; hash={schedule.ScheduledScriptHash}; job={jobName}; cron={schedule.Cron}; zone={schedule.TimeZone}");
        return Ok(schedule);
    }

    [HttpDelete("{id:int}/schedule")]
    public async Task<IActionResult> RemoveSchedule(int id, CancellationToken ct)
    {
        var pipeline = await RunnablePipelineAsync(id, ct);
        if (pipeline.Refusal is not null) return pipeline.Refusal;
        if (!orchestrator.IsConfigured) return NotConnected();

        var jobName = JobName(id);
        using var response = await orchestrator.UnscheduleJobRevisionAsync(jobName, ct);
        if (response is null) return Unreachable();
        if (!response.IsSuccessStatusCode) return await RelayScheduleRefusalAsync(response, jobName, ct);

        await audit.LogAsync(CurrentUserId, "PIPELINE_UNSCHEDULED", "Report", id.ToString(), $"job={jobName}");
        return Ok(await ToScheduleDtoAsync(pipeline.Document!, response, ct));
    }

    private async Task<string?> ReadSavedRevisionAsync(Report document, CancellationToken ct)
    {
        var scriptKey = PortalPathGuard.ToScriptKey(portalConfig, document.ScriptPath);
        return scriptKey is not null && await artifacts.ExistsAsync(ArtifactArea.Scripts, scriptKey, ct)
            ? await artifacts.ReadAllTextAsync(ArtifactArea.Scripts, scriptKey, ct)
            : null;
    }

    private async Task<PipelineScheduleDto> ToScheduleDtoAsync(Report document, HttpResponseMessage response, CancellationToken ct)
    {
        var schedule = await response.Content.ReadFromJsonAsync<OrchestratorRevisionSchedule>(Json, ct)
            ?? new OrchestratorRevisionSchedule(false, null, null, false, null, null, null);
        var saved = await ReadSavedRevisionAsync(document, ct);
        var savedHash = saved is null
            ? null
            : "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(saved))).ToLowerInvariant();
        return new PipelineScheduleDto(
            document.Id, schedule.Scheduled, schedule.Cron, schedule.TimeZone, schedule.Enabled,
            schedule.NextRun, schedule.LastRun,
            schedule.Scheduled ? schedule.ScriptHash : null,
            schedule.Scheduled && savedHash is not null
                && string.Equals(savedHash, schedule.ScriptHash, StringComparison.OrdinalIgnoreCase),
            document.Version);
    }

    private async Task<IActionResult> RelayScheduleRefusalAsync(HttpResponseMessage response, string jobName, CancellationToken ct)
    {
        OrchestratorRefusal? refusal = null;
        try { refusal = await response.Content.ReadFromJsonAsync<OrchestratorRefusal>(Json, ct); }
        catch (JsonException) { }

        return (response.StatusCode, refusal?.Code) switch
        {
            (_, "job_not_provisioned") => StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
                "job_not_provisioned",
                $"No Orchestrator job is provisioned for this pipeline yet, and this account may not create one. "
                + $"An Orchestrator manager schedules or runs it once to create '{jobName}'.")),
            (_, "schedule_grant_denied") => StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
                "schedule_grant_denied",
                $"Scheduling changes what Orchestrator job '{jobName}' runs unattended, so it needs a MANAGE grant on that job. EXECUTE is enough to run it, not to schedule it.")),
            (HttpStatusCode.BadRequest, _) => BadRequest(new PipelineRunRefusalDto(
                "invalid_schedule", refusal?.Error ?? "The Orchestrator did not accept that schedule.")),
            (HttpStatusCode.Conflict, _) => Conflict(new PipelineRunRefusalDto(
                "job_changed", "The Orchestrator job changed while this was applied. Retry.")),
            (HttpStatusCode.Forbidden, _) => StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
                "schedule_grant_denied", "The Orchestrator does not let this account read or change that schedule.")),
            _ => Unreachable()
        };
    }

    private ObjectResult NotConnected() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new PipelineRunRefusalDto(
            "orchestrator_not_connected",
            "No Orchestrator is connected to this Portal, so a pipeline cannot be scheduled from here. "
            + "An administrator connects one under Admin, Orchestrator."));

    private sealed record OrchestratorRevisionSchedule(
        bool Scheduled, string? Cron, string? TimeZone, bool Enabled, DateTime? NextRun, DateTime? LastRun, string? ScriptHash);

    private ObjectResult CancelDenied(long runId) =>
        StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
            "cancel_denied",
            $"This account did not submit run {runId} and holds no MANAGE grant on its Orchestrator job, so it cannot stop it."));

    /// <summary>
    /// The pipeline, when it exists in this tenant and the caller may run it. A report, a deleted
    /// document and another tenant's document all answer as no pipeline does.
    /// </summary>
    private async Task<(Report? Document, IActionResult? Refusal)> RunnablePipelineAsync(int id, CancellationToken ct)
    {
        var document = await CatalogScope.Documents.AsNoTracking()
            .Include(item => item.Folder)
            .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted, ct);
        if (document is null || document.Kind != CatalogDocumentKind.Pipeline)
            return (null, NotFound(new PipelineRunRefusalDto("pipeline_not_found", "No such pipeline.")));

        var permission = await folderPermissions.GetEffectiveReportPermissionAsync(document, User);
        if (!permission.AtLeast(FolderPermission.Execute))
            return (null, StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
                "document_execute_denied", "This account does not have Execute on this pipeline.")));
        return (document, null);
    }

    /// <summary>Turns the Orchestrator's refusal into one the author can act on, keeping its code.</summary>
    private async Task<IActionResult> RelayRefusalAsync(HttpResponseMessage response, string jobName, CancellationToken ct)
    {
        OrchestratorRefusal? refusal = null;
        try { refusal = await response.Content.ReadFromJsonAsync<OrchestratorRefusal>(Json, ct); }
        catch (JsonException) { }
        var code = refusal?.Code;

        return (response.StatusCode, code) switch
        {
            (_, "job_not_provisioned") => StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
                "job_not_provisioned",
                $"No Orchestrator job is provisioned for this pipeline yet, and this account may not create one. "
                + $"An Orchestrator manager runs it once to create '{jobName}', then grants EXECUTE on it.")),
            (_, "run_grant_denied") => StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
                "run_grant_denied",
                $"This account holds no EXECUTE grant on Orchestrator job '{jobName}'. An owner or manager of the job grants it.")),
            (HttpStatusCode.Conflict, _) => Conflict(new PipelineRunRefusalDto(
                "already_running", "This pipeline already has a run in progress. Wait for it to finish, or stop it.")),
            (HttpStatusCode.Unauthorized, _) => StatusCode(StatusCodes.Status503ServiceUnavailable, new PipelineRunRefusalDto(
                "orchestrator_refused_portal",
                "The Orchestrator did not accept this Portal's credentials or the identity it presented. An administrator checks the Orchestrator connection.")),
            (HttpStatusCode.ServiceUnavailable, _) => StatusCode(StatusCodes.Status503ServiceUnavailable, new PipelineRunRefusalDto(
                code ?? "orchestrator_busy",
                refusal?.Error ?? "The Orchestrator could not start the run right now. Retry shortly.")),
            (HttpStatusCode.Forbidden, _) => StatusCode(StatusCodes.Status403Forbidden, new PipelineRunRefusalDto(
                "orchestrator_refused", "The Orchestrator refused this run.")),
            _ => StatusCode(StatusCodes.Status502BadGateway, new PipelineRunRefusalDto(
                "orchestrator_refused",
                refusal?.Error ?? $"The Orchestrator answered {(int)response.StatusCode} and the run was not started."))
        };
    }

    private ObjectResult Unreachable() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new PipelineRunRefusalDto(
            "orchestrator_unreachable",
            "The Orchestrator connected to this Portal did not answer. Nothing was run in its place; retry shortly."));

    private NotFoundObjectResult RunNotFound(long runId) =>
        NotFound(new PipelineRunRefusalDto("run_not_found", $"This pipeline has no run {runId}."));

    private sealed record OrchestratorRunAccepted(long RunId, string? JobName, string? ScriptHash);

    private sealed record OrchestratorRefusal(string? Code, string? Error);

    private sealed record OrchestratorJobRunStatement(string Statement, long DurationMs, long RowsProcessed, bool Failed);

    private sealed record OrchestratorJobRun(
        long RunId,
        string? JobName,
        string Status,
        bool IsTerminal,
        DateTime StartTime,
        DateTime? EndTime,
        string? ErrorMessage,
        long RowsProcessed,
        string? ScriptHash,
        bool LogTruncated,
        IReadOnlyList<OrchestratorJobRunStatement>? Statements);
}
