using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/orchestrator")]
[Authorize(Policy = "OrchestratorAccess")]
public class OrchestratorController(OrchestratorProxyService proxy) : ControllerBase
{
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
        using var resp = await proxy.UpdateJobAsync(name, req);
        if (resp == null) return StatusCode(503, new { Error = "Orchestrator service unavailable." });
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return StatusCode((int)resp.StatusCode, body);
        }
        return NoContent();
    }

    [HttpDelete("jobs/{name}")]
    public async Task<IActionResult> DeleteJob(string name)
    {
        using var resp = await proxy.DeleteJobAsync(name);
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

        if (string.IsNullOrWhiteSpace(job.Script))
            return Ok(new DagDto([], []));

        List<DagNodeDto> nodes = [];
        List<DagEdgeDto> edges = [];

        try
        {
            var tokens = new Lexer(job.Script).Tokenize();
            var script = new CoreParser(tokens, job.Script).Parse();
            int seq = 0;
            BuildStatementDag(script.Statements, nodes, edges, ref seq, null);
        }
        catch (Exception ex)
        {
            return UnprocessableEntity(new { Error = $"Could not parse job script: {ex.Message}" });
        }

        return Ok(new DagDto(nodes, edges));
    }

    private static void BuildStatementDag(
        IEnumerable<Statement> statements,
        List<DagNodeDto> nodes,
        List<DagEdgeDto> edges,
        ref int seq,
        string? prevId)
    {
        foreach (var stmt in statements)
        {
            // Skip pure housekeeping statements to reduce clutter
            if (stmt is DeclareStatement or SetVariableStatement or PrintStatement)
                continue;

            var id = $"s{seq++}";
            var (label, type) = ClassifyStatement(stmt);
            nodes.Add(new DagNodeDto(id, label, type, new { line = stmt.Line }));

            if (prevId is not null)
                edges.Add(new DagEdgeDto(prevId, id, null));

            prevId = id;
        }
    }

    private static (string label, string type) ClassifyStatement(Statement stmt) => stmt switch
    {
        InsertStatement s             => ($"INSERT → {s.TargetTable.TableName}", "io"),
        SelectStatement s             => s.IntoTable is not null
                                         ? ($"SELECT INTO {s.IntoTable.TableName}", "io")
                                         : ("SELECT", "statement"),
        CreateTableStatement s        => ($"CREATE {s.TargetTable.TableName}", "statement"),
        CreateConnectionStatement s   => ($"CONNECT {s.name}", "connection"),
        IfStatement _                 => ("IF", "conditional"),
        WhileStatement _              => ("WHILE", "loop"),
        ForStatement s                => ($"FOR @{s.VariableName}", "loop"),
        ForeachStatement s            => ($"FOR EACH @{s.VariableName}", "loop"),
        ParallelStatement _           => ("PARALLEL", "loop"),
        ExecuteStatement s            => ($"CALL {s.ProcedureName}", "procedure"),
        RunScriptStatement _          => ("RUN SCRIPT", "io"),
        BulkInsertStatement s         => ($"BULK INSERT → {s.TargetTable.TableName}", "io"),
        CreateDatasetStatement s      => ($"DATASET {s.TempTableName}", "dataset"),
        RefreshPortalDatasetStatement s => ($"REFRESH {s.DatasetName}", "dataset"),
        _                             => (stmt.GetType().Name.Replace("Statement", ""), "statement"),
    };

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
