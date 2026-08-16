using System.Security.Claims;
using ETL_SQL.Portal.Data;
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

    // ── Schedules ─────────────────────────────────────────────────────────────

    [HttpGet("schedules")]
    public async Task<IActionResult> GetSchedules([FromQuery] int limit = 1000, [FromQuery] int offset = 0)
    {
        var schedules = await proxy.GetSchedulesAsync(limit, offset);
        return Ok(schedules);
    }

    [HttpGet("schedules/{name}")]
    public async Task<IActionResult> GetSchedule(string name)
    {
        var schedule = await proxy.GetScheduleAsync(name);
        return schedule is null ? NotFound(new { Error = $"Schedule '{name}' not found." }) : Ok(schedule);
    }

    [HttpPost("schedules")]
    public async Task<IActionResult> CreateSchedule([FromBody] CreateScheduleRequest req)
    {
        using var resp = await proxy.CreateScheduleAsync(req);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "ScheduleCreated", "Schedule", req.Name, null);
        return StatusCode(201);
    }

    [HttpPut("schedules/{name}")]
    public async Task<IActionResult> UpdateSchedule(string name, [FromBody] UpdateScheduleRequest req)
    {
        using var resp = await proxy.UpdateScheduleAsync(name, req);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "ScheduleUpdated", "Schedule", name, null);
        return Ok();
    }

    [HttpDelete("schedules/{name}")]
    public async Task<IActionResult> DeleteSchedule(string name)
    {
        using var resp = await proxy.DeleteScheduleAsync(name);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "ScheduleDeleted", "Schedule", name, null);
        return Ok();
    }

    [HttpGet("jobs/{name}/schedules")]
    public async Task<IActionResult> GetJobSchedules(string name)
    {
        var links = await proxy.GetJobSchedulesAsync(name);
        return Ok(links);
    }

    [HttpPost("jobs/{name}/schedules/{scheduleName}")]
    public async Task<IActionResult> AttachJobSchedule(string name, string scheduleName)
    {
        using var resp = await proxy.AttachJobScheduleAsync(name, scheduleName);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "JobScheduleAttached", "Job", name, $"Schedule={scheduleName}");
        return Ok();
    }

    [HttpDelete("jobs/{name}/schedules/{scheduleName}")]
    public async Task<IActionResult> DetachJobSchedule(string name, string scheduleName)
    {
        using var resp = await proxy.DetachJobScheduleAsync(name, scheduleName);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "JobScheduleDetached", "Job", name, $"Schedule={scheduleName}");
        return Ok();
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications([FromQuery] int limit = 1000, [FromQuery] int offset = 0)
    {
        var notifications = await proxy.GetNotificationsAsync(limit, offset);
        return Ok(notifications);
    }

    [HttpGet("notifications/{name}")]
    public async Task<IActionResult> GetNotification(string name)
    {
        var notification = await proxy.GetNotificationAsync(name);
        return notification is null ? NotFound(new { Error = $"Notification '{name}' not found." }) : Ok(notification);
    }

    [HttpPost("notifications")]
    public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationRequest req)
    {
        using var resp = await proxy.CreateNotificationAsync(req);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "NotificationCreated", "Notification", req.Name, null);
        return StatusCode(201);
    }

    [HttpPut("notifications/{name}")]
    public async Task<IActionResult> UpdateNotification(string name, [FromBody] UpdateNotificationRequest req)
    {
        using var resp = await proxy.UpdateNotificationAsync(name, req);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "NotificationUpdated", "Notification", name, null);
        return Ok();
    }

    [HttpDelete("notifications/{name}")]
    public async Task<IActionResult> DeleteNotification(string name)
    {
        using var resp = await proxy.DeleteNotificationAsync(name);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "NotificationDeleted", "Notification", name, null);
        return Ok();
    }

    [HttpGet("jobs/{name}/notifications")]
    public async Task<IActionResult> GetJobNotifications(string name)
    {
        var links = await proxy.GetJobNotificationsAsync(name);
        return Ok(links);
    }

    [HttpPost("jobs/{name}/notifications/{notificationName}")]
    public async Task<IActionResult> AttachJobNotification(string name, string notificationName, [FromBody] LinkJobNotificationRequest? req)
    {
        using var resp = await proxy.AttachJobNotificationAsync(name, notificationName, req?.Trigger ?? "Completion");
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "JobNotificationAttached", "Job", name, $"Notification={notificationName}; Trigger={req?.Trigger ?? "Completion"}");
        return Ok();
    }

    [HttpDelete("jobs/{name}/notifications/{notificationName}")]
    public async Task<IActionResult> DetachJobNotification(string name, string notificationName, [FromQuery] string? trigger = null)
    {
        using var resp = await proxy.DetachJobNotificationAsync(name, notificationName, trigger);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "JobNotificationDetached", "Job", name, $"Notification={notificationName}");
        return Ok();
    }

    [HttpPost("notifications/{name}/dispatch")]
    public Task<IActionResult> DispatchNotification(string name, [FromBody] OrchestratorNotificationDispatchRequest req, CancellationToken ct) =>
        RelayAsync(
            () => proxy.DispatchNotificationAsync(name, req, ct),
            () => audit.LogAsync(CurrentUserId, "NotificationDispatched", "Notification", name, $"SourceKind={req.SourceKind}; Title={req.Title}"));

    // ── Watermarks & Job State ──────────────────────────────────────────────────

    [HttpGet("jobs/{name}/state")]
    public async Task<IActionResult> GetJobStates(string name)
    {
        var states = await proxy.GetJobStatesAsync(name);
        return Ok(states);
    }

    [HttpPut("jobs/{name}/state/{key}")]
    public async Task<IActionResult> SetJobState(string name, string key, [FromBody] SetJobStateRequest req)
    {
        using var resp = await proxy.SetJobStateAsync(name, key, req?.Value);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "JobStateUpdated", "Job", name, $"Key={key}; Value={req?.Value}");
        return Ok();
    }

    [HttpDelete("jobs/{name}/state/{key}")]
    public async Task<IActionResult> DeleteJobState(string name, string key)
    {
        using var resp = await proxy.DeleteJobStateAsync(name, key);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        await audit.LogAsync(CurrentUserId, "JobStateReset", "Job", name, $"Key={key}");
        return Ok();
    }

    // ── Job Audit Trail ────────────────────────────────────────────────────────

    [HttpGet("jobs/{name}/audit")]
    public async Task<IActionResult> GetJobAuditTrail(
        string name,
        [FromServices] PortalDbContext db,
        [FromServices] DatasetTenantScope? tenantScope,
        [FromQuery] int limit = 50)
    {
        var tenantId = tenantScope?.TenantId ?? "portal-host";
        var unescaped = Uri.UnescapeDataString(name);
        var entries = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.AuditLogs
                .Where(a => a.TenantId == tenantId &&
                    (a.ResourceId == unescaped || a.ResourceId == name) &&
                    (a.ResourceType == "Job" || a.ResourceType == "JobRun" || a.ResourceType == "OrchestratorJob" || a.ResourceType == "JobScript" || a.Action.StartsWith("Job")))
                .OrderByDescending(a => a.Timestamp)
                .Take(Math.Clamp(limit, 1, 200)));
        return Ok(entries);
    }

    // ── Cross-Job Dependencies ─────────────────────────────────────────────────

    [HttpGet("jobs/{name}/dependencies")]
    public async Task<IActionResult> GetJobDependencies(string name)
    {
        var allJobs = await proxy.GetJobsAsync();
        var currentJob = allJobs.FirstOrDefault(j => j.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (currentJob == null) return NotFound(new { Error = $"Job '{name}' not found." });

        var jobOutputs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var jobInputs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var jobTriggers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in allJobs)
        {
            var outs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trigs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(job.Script))
            {
                var projection = scriptDag.Project(job.Script);
                if (projection.Parsed && projection.Dag != null)
                {
                    foreach (var edge in projection.Dag.Edges)
                    {
                        if (edge.Label != null && edge.Label.Contains("writes", StringComparison.OrdinalIgnoreCase))
                            outs.Add(edge.Target);
                        else if (edge.Label != null && edge.Label.Contains("reads", StringComparison.OrdinalIgnoreCase))
                            ins.Add(edge.Source);
                    }
                    foreach (var node in projection.Dag.Nodes)
                    {
                        if (node.Type == "table" || node.Type == "io")
                        {
                            if (node.Label.StartsWith("INTO", StringComparison.OrdinalIgnoreCase) ||
                                node.Label.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase))
                                outs.Add(node.Label);
                            else if (node.Label.StartsWith("FROM", StringComparison.OrdinalIgnoreCase))
                                ins.Add(node.Label);
                        }
                    }
                }

                // Look for direct trigger statements or script references
                foreach (var other in allJobs)
                {
                    if (other.Name.Equals(job.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (job.Script.Contains(other.Name, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(other.TargetPath) && job.Script.Contains(other.TargetPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        trigs.Add(other.Name);
                    }
                }
            }

            jobOutputs[job.Name] = outs;
            jobInputs[job.Name] = ins;
            jobTriggers[job.Name] = trigs;
        }

        var upstreamJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downstreamJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<JobDependencyEdgeDto>();

        // Find upstreams (jobs that produce data currentJob consumes or trigger currentJob)
        var myInputs = jobInputs.GetValueOrDefault(currentJob.Name, []);
        foreach (var other in allJobs)
        {
            if (other.Name.Equals(currentJob.Name, StringComparison.OrdinalIgnoreCase)) continue;
            var otherOuts = jobOutputs.GetValueOrDefault(other.Name, []);
            var matching = otherOuts.Intersect(myInputs, StringComparer.OrdinalIgnoreCase).ToList();
            if (matching.Count > 0)
            {
                upstreamJobs.Add(other.Name);
                edges.Add(new JobDependencyEdgeDto(other.Name, currentJob.Name, "Data", $"Writes {string.Join(", ", matching)}"));
            }
            if (jobTriggers.GetValueOrDefault(other.Name, []).Contains(currentJob.Name))
            {
                upstreamJobs.Add(other.Name);
                edges.Add(new JobDependencyEdgeDto(other.Name, currentJob.Name, "Trigger", "Direct execution trigger"));
            }
        }

        // Find downstreams (jobs that consume data currentJob produces or that currentJob triggers)
        var myOuts = jobOutputs.GetValueOrDefault(currentJob.Name, []);
        foreach (var other in allJobs)
        {
            if (other.Name.Equals(currentJob.Name, StringComparison.OrdinalIgnoreCase)) continue;
            var otherIns = jobInputs.GetValueOrDefault(other.Name, []);
            var matching = myOuts.Intersect(otherIns, StringComparer.OrdinalIgnoreCase).ToList();
            if (matching.Count > 0)
            {
                downstreamJobs.Add(other.Name);
                edges.Add(new JobDependencyEdgeDto(currentJob.Name, other.Name, "Data", $"Reads {string.Join(", ", matching)}"));
            }
            if (jobTriggers.GetValueOrDefault(currentJob.Name, []).Contains(other.Name))
            {
                downstreamJobs.Add(other.Name);
                edges.Add(new JobDependencyEdgeDto(currentJob.Name, other.Name, "Trigger", "Direct execution trigger"));
            }
        }

        var neighborNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentJob.Name };
        foreach (var u in upstreamJobs) neighborNames.Add(u);
        foreach (var d in downstreamJobs) neighborNames.Add(d);

        var nodes = allJobs
            .Where(j => neighborNames.Contains(j.Name))
            .Select(j => new JobDependencyNodeDto(
                j.Name,
                j.Name,
                j.DisplayName,
                j.IsEnabled,
                j.LastRun,
                j.NextRun,
                j.Name.Equals(currentJob.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var chain = new JobDependencyChainDto(
            currentJob.Name,
            nodes,
            edges,
            upstreamJobs.ToList(),
            downstreamJobs.ToList(),
            []);

        return Ok(chain);
    }

    // ── Bundles, Data Quality & Stewardship ────────────────────────────────────

    [HttpGet("data-quality/status")]
    public Task<IActionResult> GetDataQualityStatus([FromQuery] int limit = 1000, CancellationToken ct = default) =>
        RelayAsync(() => proxy.GetDataQualityStatusResponseAsync(limit, ct));

    [HttpGet("data-quality/failures")]
    public Task<IActionResult> GetDataQualityFailures([FromQuery] int limit = 1000, CancellationToken ct = default) =>
        RelayAsync(() => proxy.GetDataQualityFailuresResponseAsync(limit, ct));

    [HttpGet("stewardship/score")]
    public Task<IActionResult> GetStewardshipScore([FromQuery] int limit = 1000, CancellationToken ct = default) =>
        RelayAsync(() => proxy.GetStewardshipScoreResponseAsync(limit, ct));

    [HttpGet("stewardship/gaps")]
    public Task<IActionResult> GetStewardshipGaps([FromQuery] int limit = 1000, CancellationToken ct = default) =>
        RelayAsync(() => proxy.GetStewardshipGapsResponseAsync(limit, ct));

    [HttpGet("bundles")]
    public Task<IActionResult> GetBundles(CancellationToken ct) =>
        RelayAsync(() => proxy.GetBundlesResponseAsync(ct));

    [HttpGet("bundles/{name}/versions")]
    public Task<IActionResult> GetBundleVersions(string name, CancellationToken ct) =>
        RelayAsync(() => proxy.GetBundleVersionsResponseAsync(name, ct));

    [HttpGet("bundles/{name}/versions/{version:int}/dependencies")]
    public Task<IActionResult> GetBundleDependencies(string name, int version, CancellationToken ct) =>
        RelayAsync(() => proxy.GetBundleDependenciesResponseAsync(name, version, ct));

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
    public Task<IActionResult> SetGrant(
        string kind, string name, string principalKind, string principalId,
        [FromBody] SetGrantRequest request, CancellationToken ct) =>
        RelayAsync(
            () => proxy.SetObjectGrantAsync(
                kind, name, principalKind, principalId, request?.Permission ?? "", ct),
            // Audited on the Portal side as well as the Orchestrator's own security event: this is
            // where a human clicked, and the two records answer different questions during an incident.
            () => audit.LogAsync(CurrentUserId, "ORCHESTRATOR_GRANT", $"Orchestrator{kind}", name,
                $"{principalKind}:{principalId}={request?.Permission}"));

    [HttpDelete("authorization/{kind}/{name}/{principalKind}/{principalId}")]
    public Task<IActionResult> RevokeGrant(
        string kind, string name, string principalKind, string principalId, CancellationToken ct) =>
        RelayAsync(
            () => proxy.DeleteObjectGrantAsync(kind, name, principalKind, principalId, ct),
            () => audit.LogAsync(CurrentUserId, "ORCHESTRATOR_REVOKE", $"Orchestrator{kind}", name,
                $"{principalKind}:{principalId}"));

    // ── Ownership ─────────────────────────────────────────────────────────────
    //
    // Reassignment and adoption are administrator acts in the Orchestrator, and relayed here on the
    // same pass-through terms as grants. Both are audited on the Portal side too: an owner change is
    // the one act that can hand someone authority over an object indefinitely, so the record of which
    // human asked for it matters as much as the record of what the store did.

    public sealed record SetOwnerRequest(string PrincipalKind, string PrincipalId);
    public sealed record AdoptRequest(string PrincipalKind, string PrincipalId, string? Kind = null);

    [HttpGet("authorization/unowned")]
    public Task<IActionResult> GetUnownedObjects(CancellationToken ct) =>
        RelayAsync(() => proxy.GetUnownedObjectsAsync(ct));

    [HttpPut("authorization/{kind}/{name}/owner")]
    public Task<IActionResult> SetOwner(
        string kind, string name, [FromBody] SetOwnerRequest request, CancellationToken ct) =>
        RelayAsync(
            () => proxy.SetObjectOwnerAsync(
                kind, name, request?.PrincipalKind ?? "", request?.PrincipalId ?? "", ct),
            () => audit.LogAsync(CurrentUserId, "ORCHESTRATOR_SET_OWNER", $"Orchestrator{kind}", name,
                $"{request?.PrincipalKind}:{request?.PrincipalId}"));

    [HttpPost("authorization/adopt")]
    public Task<IActionResult> AdoptUnownedObjects([FromBody] AdoptRequest request, CancellationToken ct) =>
        RelayAsync(
            () => proxy.AdoptUnownedObjectsAsync(
                request?.PrincipalKind ?? "", request?.PrincipalId ?? "", request?.Kind, ct),
            () => audit.LogAsync(CurrentUserId, "ORCHESTRATOR_ADOPT", "Orchestrator", request?.Kind ?? "ALL",
                $"{request?.PrincipalKind}:{request?.PrincipalId}"));

    /// <summary>
    /// Forwards one grant call and returns the Orchestrator's own answer.
    ///
    /// <para>The status code is passed through rather than reinterpreted, because the distinctions it
    /// draws are load-bearing: 403 means the caller may not manage this object, 404 means it does not
    /// exist <em>in their tenant</em>, and collapsing either into a generic failure would lose the
    /// only signal that tells an administrator which problem they have.</para>
    ///
    /// <para><paramref name="auditSuccess"/> runs only when the Orchestrator accepted the change, and
    /// writes rather than stages it. Staging binds an audit row to the mutation's own unit of work,
    /// which is the right shape when both live in the Portal's database — but the mutation here
    /// happens in the Orchestrator's store over HTTP, so there is no shared transaction to commit
    /// with, and a staged row that nothing saves is simply discarded when the request scope ends.
    /// Recording only accepted changes follows the trigger and kill paths: a refused grant must not
    /// leave a trail that reads as though access had been widened.</para>
    /// </summary>
    private async Task<IActionResult> RelayAsync(
        Func<Task<HttpResponseMessage?>> send, Func<Task>? auditSuccess = null)
    {
        using var response = await send();
        if (response is null)
            return StatusCode(503, new { Error = "Orchestrator service unavailable." });

        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode && auditSuccess is not null) await auditSuccess();
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
