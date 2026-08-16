using System.Security.Claims;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/orchestrator")]
[Authorize(Policy = "OrchestratorAccess")]
public class OrchestratorController(
    OrchestratorProxyService proxy,
    AuditService audit,
    ScriptDagProjectionService scriptDag,
    OperationsTriageService triage) : ControllerBase
{
    /// <summary>Upper bound on one bulk re-run, so a mis-click cannot enqueue the whole estate.</summary>
    private const int MaxRerunBatch = 50;

    // ── Status & metrics ──────────────────────────────────────────────────────

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var online = await proxy.IsOnlineAsync();
        if (!online) return Ok(new { Online = false, Status = "Offline" });
        var status = await proxy.GetStatusAsync();
        return Ok(new { Online = true, Status = status });
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        var metrics = await proxy.GetMetricsAsync();
        if (metrics == null) return Ok(new { Online = false });
        return Ok(new { Online = true, Metrics = metrics });
    }

    // ── Scheduled jobs ────────────────────────────────────────────────────────

    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs()
    {
        var jobs = await proxy.GetJobsAsync();
        return Ok(jobs);
    }

    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest req)
    {
        using var resp = await proxy.CreateJobAsync(req);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        return StatusCode(201);
    }

    [HttpPut("jobs/{name}")]
    public async Task<IActionResult> UpdateJob(string name, [FromBody] UpdateJobRequest req)
    {
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        using var resp = await proxy.UpdateJobAsync(name, req, expectedVersion.Value);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        if (!string.IsNullOrEmpty(req.ScriptText))
        {
            await audit.LogAsync(CurrentUserId, "JobScriptEdited", "Job", name, null);
        }
        return NoContent();
    }

    [HttpDelete("jobs/{name}")]
    public async Task<IActionResult> DeleteJob(string name)
    {
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        using var resp = await proxy.DeleteJobAsync(name, expectedVersion.Value);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        return Ok();
    }

    [HttpGet("jobs/{name}/history")]
    public async Task<IActionResult> GetHistory(string name, [FromQuery] int limit = 50)
    {
        var history = await proxy.GetHistoryAsync(name, limit);
        return Ok(history);
    }

    /// <summary>The signed-in principal, for audit attribution.</summary>
    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpPost("jobs/{name}/trigger")]
    public async Task<IActionResult> TriggerJob(
        string name,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
        TriggerJobRequest? request = null)
    {
        using var resp = await proxy.TriggerJobAsync(name, request?.Variables);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }

        // Audited only on success, and at the Portal edge: the Orchestrator is reached with a
        // shared service key, so this is the only place that knows which human clicked. Triggering
        // a job out of schedule is a privileged act whether it was done from the triage board or
        // one job at a time, and only the former was recorded.
        var overrideNames = request?.Variables?.Keys
            .Select(variable => variable.StartsWith('@') ? variable : "@" + variable)
            .OrderBy(variable => variable, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        await audit.LogAsync(
            CurrentUserId,
            overrideNames.Length == 0 ? "JobTriggered" : "JobTriggeredWithVariableOverrides",
            "Job",
            name,
            overrideNames.Length == 0
                ? null
                : $"OverrideCount={overrideNames.Length}; Names={string.Join(',', overrideNames)}");
        return Accepted();
    }

    [HttpPost("runs/{historyId:long}/resume")]
    public async Task<IActionResult> ResumeRun(long historyId)
    {
        using var resp = await proxy.ResumeRunAsync(historyId);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, body);

        await audit.LogAsync(
            CurrentUserId,
            "JobRunResumedFromNamedCheckpoint",
            "JobRun",
            historyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "ResumeMode=NamedCheckpoint");
        return Accepted(body);
    }

    // ── Operations triage ─────────────────────────────────────────────────────

    /// <summary>
    /// Cross-job triage board: failures grouped into incidents, in-flight runs, and enabled jobs
    /// whose occurrence passed unclaimed. Reads shared state directly, so it still answers when the
    /// Orchestrator service itself is unreachable.
    /// </summary>
    [HttpGet("triage")]
    public async Task<IActionResult> GetTriage(
        [FromQuery] int lookbackHours = 24,
        [FromQuery] int graceMinutes = OperationsTriageService.DefaultGraceMinutes,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string>? readableJobs = User.IsInRole("Admin")
            ? null
            : (await proxy.GetJobsAsync())
                .Select(job => job.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var board = await triage.BuildAsync(
            lookbackHours, graceMinutes, readableJobs, cancellationToken);
        return Ok(board);
    }

    /// <summary>Statement, quality, and script-integrity evidence for one durable run.</summary>
    [HttpGet("triage/runs/{runId:long}")]
    public async Task<IActionResult> GetTriageRun(
        long runId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string>? readableJobs = User.IsInRole("Admin")
            ? null
            : (await proxy.GetJobsAsync())
                .Select(job => job.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var detail = await triage.GetRunDetailAsync(runId, readableJobs, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// Re-runs several failed jobs in one action. Each trigger is reported individually: a bulk
    /// re-run where one job is missing should still start the rest, and the operator needs to know
    /// which ones did not go.
    /// </summary>
    [HttpPost("jobs/rerun")]
    public async Task<IActionResult> RerunJobs([FromBody] TriageRerunRequest req)
    {
        var names = (req?.JobNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
            return BadRequest(new { Error = "At least one job name is required." });
        if (names.Count > MaxRerunBatch)
            return BadRequest(new { Error = $"At most {MaxRerunBatch} jobs may be re-run in one request." });

        var userId = CurrentUserId;

        var results = new List<TriageRerunResultDto>(names.Count);
        foreach (var name in names)
        {
            using var resp = await proxy.TriggerJobAsync(name);
            if (resp is null)
            {
                results.Add(new TriageRerunResultDto(name, false, "Orchestrator service unavailable."));
                continue;
            }
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                results.Add(new TriageRerunResultDto(name, false, body));
                continue;
            }

            // Attributed per job, not once per batch: the audit trail has to answer "who re-ran this
            // job" for each job independently of how it was grouped in the UI.
            await audit.LogAsync(userId, "JobRerunFromTriage", "Job", name, null);
            results.Add(new TriageRerunResultDto(name, true, null));
        }

        return Ok(new TriageRerunResponseDto(names.Count, results.Count(r => r.Triggered), results));
    }

    [HttpPost("jobs/{name}/kill")]
    public async Task<IActionResult> KillJob(string name)
    {
        using var resp = await proxy.KillJobAsync(name);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }

        // Killing a running job destroys in-flight work; it should never be the one privileged
        // Orchestrator action with no record of who did it.
        await audit.LogAsync(CurrentUserId, "JobKilled", "Job", name, null);
        return Ok();
    }

    // ── Job script DAG ───────────────────────────────────────────────────────

    [HttpGet("jobs/{name}/dag")]
    public async Task<IActionResult> GetJobDag(string name)
    {
        var jobs = await proxy.GetJobsAsync();
        var job = jobs.FirstOrDefault(j =>
            j.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (job is null) return NotFound();

        var projection = scriptDag.Project(job.Script);
        return projection.Parsed
            ? Ok(projection.Dag)
            : UnprocessableEntity(new { Error = projection.Error });
    }

    // ── Script browser ────────────────────────────────────────────────────────

    [HttpGet("scripts")]
    public async Task<IActionResult> GetScripts()
    {
        var scripts = await proxy.GetScriptsAsync();
        if (scripts == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        return Ok(scripts);
    }

    [HttpGet("scripts/content")]
    public async Task<IActionResult> GetScriptContent([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { Error = "path is required." });
        var content = await proxy.GetScriptContentAsync(path);
        if (content == null) return StatusCode(503, new { Error = "Script unavailable." });
        return Ok(new { Path = path, Content = content });
    }

    // ── Per-object grants ─────────────────────────────────────────────────────
    //
    // Pure pass-through. Every one of these forwards the caller's own signed assertion and returns
    // what the Orchestrator said, verbatim: it owns the grant store and already decides tenant,
    // ownership and scope. Re-deciding here would be a second permission model — the thing this whole
    // item exists to prevent — and a Portal-side cache of grants would be the copy that goes stale
    // while looking authoritative.

    public sealed record SetGrantRequest(string Permission);

    [HttpGet("authorization/{kind}/{name}")]
    public Task<IActionResult> ListGrants(string kind, string name, CancellationToken ct) =>
        RelayAsync(() => proxy.GetObjectGrantsAsync(kind, name, ct));

    [HttpPut("authorization/{kind}/{name}/{principalKind}/{principalId}")]
    public async Task<IActionResult> SetGrant(
        string kind, string name, string principalKind, string principalId,
        [FromBody] SetGrantRequest request, CancellationToken ct)
    {
        // Audited on the Portal side as well as the Orchestrator's own security event: this is where
        // a human clicked, and the two records answer different questions during an incident.
        audit.Stage(CurrentUserId, "ORCHESTRATOR_GRANT", $"Orchestrator{kind}", name,
            $"{principalKind}:{principalId}={request?.Permission}");
        return await RelayAsync(() => proxy.SetObjectGrantAsync(
            kind, name, principalKind, principalId, request?.Permission ?? "", ct));
    }

    [HttpDelete("authorization/{kind}/{name}/{principalKind}/{principalId}")]
    public async Task<IActionResult> RevokeGrant(
        string kind, string name, string principalKind, string principalId, CancellationToken ct)
    {
        audit.Stage(CurrentUserId, "ORCHESTRATOR_REVOKE", $"Orchestrator{kind}", name,
            $"{principalKind}:{principalId}");
        return await RelayAsync(() => proxy.DeleteObjectGrantAsync(kind, name, principalKind, principalId, ct));
    }

    /// <summary>
    /// Forwards one grant call and returns the Orchestrator's own answer.
    ///
    /// <para>The status code is passed through rather than reinterpreted, because the distinctions it
    /// draws are load-bearing: 403 means the caller may not manage this object, 404 means it does not
    /// exist <em>in their tenant</em>, and collapsing either into a generic failure would lose the
    /// only signal that tells an administrator which problem they have.</para>
    /// </summary>
    private async Task<IActionResult> RelayAsync(Func<Task<HttpResponseMessage?>> send)
    {
        using var response = await send();
        if (response is null)
            return StatusCode(503, new { Error = "Orchestrator service unavailable." });

        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return StatusCode((int)response.StatusCode);

        Response.StatusCode = (int)response.StatusCode;
        return Content(body, "application/json");
    }

    // ── Service control ───────────────────────────────────────────────────────

    [HttpPost("service/stop")]
    public async Task<IActionResult> StopService()
    {
        using var resp = await proxy.StopServiceAsync();
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        return Ok(new { Message = "Stop signal sent. Service will restart if managed by OS supervisor." });
    }
}
