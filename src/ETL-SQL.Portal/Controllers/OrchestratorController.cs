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
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? userId = uid is not null && int.TryParse(uid, out var id) ? id : null;
            await audit.LogAsync(userId, "JobScriptEdited", "Job", name, null);
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

    [HttpPost("jobs/{name}/trigger")]
    public async Task<IActionResult> TriggerJob(string name)
    {
        using var resp = await proxy.TriggerJobAsync(name);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        return Accepted();
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
        var board = await triage.BuildAsync(lookbackHours, graceMinutes, cancellationToken);
        return Ok(board);
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

        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? userId = uid is not null && int.TryParse(uid, out var id) ? id : null;

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

    // ── Service control ───────────────────────────────────────────────────────

    [HttpPost("service/stop")]
    public async Task<IActionResult> StopService()
    {
        using var resp = await proxy.StopServiceAsync();
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        return Ok(new { Message = "Stop signal sent. Service will restart if managed by OS supervisor." });
    }
}
