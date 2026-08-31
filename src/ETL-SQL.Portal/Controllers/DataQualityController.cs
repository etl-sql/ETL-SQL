using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/data-quality")]
[Authorize(Policy = "DataQualityStewardAccess")]
[RequirePortalModule("Reporting")]
public sealed class DataQualityController(
    ETL_SQL.Portal.Services.PortalTenantJobEvidenceStore jobHistory,
    IJobChannel jobChannel,
    IServiceProvider services,
    ETL_SQL.Common.ILogger engineLogger,
    ETL_SQL.Portal.Data.PortalDbContext db,
    PortalConfig portalConfig,
    ETL_SQL.Portal.Services.AuditService audit,
    ETL_SQL.Portal.Services.ReportScriptInspectionService scriptInspection,
    ILogger<DataQualityController> logger) : ControllerBase
{
    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    private const string QuarantineManifestPrefix = "dq:quarantine-manifest:";

    /// <summary>
    /// One durable record per (kind, target). Bounded on purpose: the audit log is the history of
    /// who submitted what, while this answers the operational question — is something in flight
    /// against this target right now, and how did the last one end.
    /// </summary>
    private const string QuarantineSubmissionPrefix = "dq:quarantine-submission:";

    private static readonly HashSet<string> TerminalJobStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Completed", "Failed", "Cancelled", "Unknown"
    };
    private static readonly HashSet<string> AllowedRowStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        DataQualityColumns.QuarantinedStatus,
        DataQualityColumns.ReleasedStatus,
        DataQualityColumns.ReplayingStatus,
        DataQualityColumns.DiscardedStatus,
        DataQualityColumns.ReplayedStatus
    };

    [HttpGet("quarantine")]
    public async Task<IActionResult> GetQuarantineQueue(
        [FromQuery] string? jobName = null,
        [FromQuery] string? q = null,
        [FromQuery] bool? replayable = null,
        [FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        var scanLimit = Math.Min(limit * 10, 5000);
        var states = await jobHistory.GetJobStatesAsync(
            string.IsNullOrWhiteSpace(jobName) ? null : jobName.Trim(),
            scanLimit);

        // The queue and the row endpoint must reach the same verdict from the same inputs, or the
        // list offers a row editor that then refuses — so both resolve readability the same way
        // rather than the queue guessing from the target string.
        var previewEnabled = portalConfig.DataQuality.AllowConnectionPreview;
        var usableAliases = previewEnabled
            ? await ResolveUsableConnectionAliasesAsync(HttpContext.RequestAborted)
            : null;

        var query = q?.Trim();
        var items = states
            .Where(s => s.StateKey.StartsWith(QuarantineManifestPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(state => TryReadManifest(state, previewEnabled, usableAliases))
            .Where(item => item != null)
            .Select(item => item!)
            .Where(item => replayable == null || item.IsReplayable == replayable.Value)
            .Where(item => string.IsNullOrWhiteSpace(query) || Matches(item, query!))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.JobName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.QuarantineTarget, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        return Ok(items);
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs()
    {
        var jobs = await jobHistory.GetAllJobsAsync();
        var result = jobs
            .Select(j => new { Name = j.Name, DisplayName = j.DisplayName ?? j.Name, Script = j.Script, Description = j.Description })
            .OrderBy(j => j.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Ok(result);
    }

    /// <summary>
    /// Quality trend for a job: per-run quarantine/warn outcomes plus the rules that fire most.
    /// This is the read surface for the metrics the engine has been persisting per run — without
    /// it a steward can see the current quarantine queue but not whether quality is degrading.
    /// </summary>
    [HttpGet("trend")]
    public async Task<IActionResult> GetQualityTrend(
        [FromQuery] string jobName,
        [FromQuery] int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(jobName))
            return BadRequest(new { error = "JobName is required." });

        limit = Math.Clamp(limit, 1, 200);
        var normalizedJobName = jobName.Trim();
        var history = await jobHistory.GetHistoryAsync(normalizedJobName, limit);
        var normalizedFailures = await jobHistory.GetDataQualityFailuresForJobAsync(
            normalizedJobName, Math.Min(10000, Math.Max(1000, limit * 100)));
        var failuresByRun = normalizedFailures
            .GroupBy(failure => failure.RunId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<JobDataQualityFailure>)group.ToList());

        var runs = history
            .Where(h => h.EndTime.HasValue)
            .OrderByDescending(h => h.EndTime ?? h.StartTime)
            .Take(limit)
            .Select(entry => ToRunDto(entry,
                failuresByRun.TryGetValue(entry.Id, out var failures) ? failures : []))
            .ToList();

        if (runs.Count == 0)
            return Ok(new DataQualityTrendDto(jobName.Trim(), 0, 0, 0, 0, null, null, null, [], []));

        var rated = runs.Where(r => r.QuarantineRate.HasValue).ToList();
        decimal? averageRate = rated.Count > 0 ? rated.Average(r => r.QuarantineRate!.Value) : null;
        decimal? latestRate = runs[0].QuarantineRate;

        // Compare the latest run against the mean of the ones before it, so a single bad run reads
        // as a spike rather than quietly averaging away.
        var priorRated = rated.Skip(1).ToList();
        decimal? delta = latestRate.HasValue && priorRated.Count > 0
            ? latestRate.Value - priorRated.Average(r => r.QuarantineRate!.Value)
            : null;

        // Grouped including CountsOnly, so a legacy run's counts are never folded into a structured
        // row: the two agree on column and rule and are silent about everything else, and summing
        // them would attribute failures to a target table the legacy run never named.
        var topFailures = runs
            .SelectMany(r => r.RuleFailures)
            .GroupBy(f => (f.TargetTable, f.Column, f.Rule, f.Action, f.Owner, f.CountsOnly))
            .Select(g => new DataQualityRuleFailureDto(
                g.Key.Column, g.Key.Rule, g.Sum(f => f.Count),
                g.Key.TargetTable, g.Key.Action, g.Key.Owner, g.Key.CountsOnly))
            .OrderByDescending(f => f.Count)
            .ThenBy(f => f.Column, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return Ok(new DataQualityTrendDto(
            normalizedJobName,
            runs.Count,
            runs.Sum(r => r.RowsProcessed),
            runs.Sum(r => r.RowsQuarantined),
            runs.Sum(r => r.RowsWarned),
            averageRate,
            latestRate,
            delta,
            topFailures,
            runs));
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetQualityRules([FromQuery] string jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName))
            return BadRequest(new { error = "JobName is required." });

        var normalizedJobName = jobName.Trim();
        var definition = await jobHistory.GetJobAsync(normalizedJobName);
        var scriptPath = definition?.Script;
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            var manifest = (await jobHistory.GetJobStatesAsync(normalizedJobName, 500))
                .Where(state => state.StateKey.StartsWith(QuarantineManifestPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(ReadManifest)
                .Where(value => value is not null)
                .OrderByDescending(value => value!.UpdatedAtUtc)
                .FirstOrDefault();
            scriptPath = manifest?.ScriptPath;
        }

        if (string.IsNullOrWhiteSpace(scriptPath))
            return Ok(Array.Empty<DataQualityRuleDefinitionDto>());

        var lineage = await scriptInspection.ReadScriptLineageAsync(scriptPath);
        var rules = new List<DataQualityRuleDefinitionDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in lineage.Where(value => ColumnRuleParser.HasRuleTags(value.Tags)))
        {
            IReadOnlyList<ColumnRuleBinding> bindings;
            try { bindings = ColumnRuleParser.ParseBindings(entry.Tags); }
            catch (ColumnRuleParseException) { continue; }

            foreach (var binding in bindings)
                foreach (var rule in binding.Rules)
                {
                    var key = $"{entry.Target}|{entry.TargetColumn}|{binding.ExpectKey}|{rule.Text}|{binding.Action}";
                    if (!seen.Add(key)) continue;
                    rules.Add(new DataQualityRuleDefinitionDto(
                        entry.Target,
                        entry.TargetColumn,
                        binding.ClauseLabel,
                        rule.Text,
                        binding.Action.ToString().ToUpperInvariant() + (binding.ActionExplicit ? "" : " (default)"),
                        scriptPath,
                        entry.Line));
                }
        }

        return Ok(rules
            .OrderBy(value => value.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.TargetColumn, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Rule, StringComparer.OrdinalIgnoreCase));
    }

    [HttpGet("rules/all")]
    public async Task<IActionResult> GetAllQualityRules()
    {
        var jobs = await jobHistory.GetAllJobsAsync();
        var rules = new List<DataQualityRuleDefinitionDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in jobs)
        {
            var scriptPath = job.Script;
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                var manifest = (await jobHistory.GetJobStatesAsync(job.Name, 500))
                    .Where(state => state.StateKey.StartsWith(QuarantineManifestPrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(ReadManifest)
                    .Where(value => value is not null)
                    .OrderByDescending(value => value!.UpdatedAtUtc)
                    .FirstOrDefault();
                scriptPath = manifest?.ScriptPath;
            }

            if (string.IsNullOrWhiteSpace(scriptPath)) continue;

            try
            {
                var lineage = await scriptInspection.ReadScriptLineageAsync(scriptPath);
                foreach (var entry in lineage.Where(value => ColumnRuleParser.HasRuleTags(value.Tags)))
                {
                    IReadOnlyList<ColumnRuleBinding> bindings;
                    try { bindings = ColumnRuleParser.ParseBindings(entry.Tags); }
                    catch (ColumnRuleParseException) { continue; }

                    foreach (var binding in bindings)
                        foreach (var rule in binding.Rules)
                        {
                            var key = $"{job.Name}|{entry.Target}|{entry.TargetColumn}|{binding.ExpectKey}|{rule.Text}|{binding.Action}";
                            if (!seen.Add(key)) continue;
                            rules.Add(new DataQualityRuleDefinitionDto(
                                entry.Target,
                                entry.TargetColumn,
                                binding.ClauseLabel,
                                rule.Text,
                                binding.Action.ToString().ToUpperInvariant() + (binding.ActionExplicit ? "" : " (default)"),
                                scriptPath,
                                entry.Line,
                                job.Name));
                        }
                }
            }
            catch { }
        }

        return Ok(rules
            .OrderBy(value => value.JobName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.TargetColumn, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Shared-connection aliases this caller may use. Steward access gates the feature; this gates
    /// the data — quarantined rows are raw source rows, so reading them takes the same authority as
    /// using the connection they came from.
    /// </summary>
    private async Task<IReadOnlyCollection<string>> ResolveUsableConnectionAliasesAsync(
        CancellationToken cancellationToken)
    {
        var catalog = services.GetService<ETL_SQL.Portal.Services.PortalConnectionCatalogService>();
        if (catalog is null) return [];

        var identity = await BuildExecutionIdentityAsync(cancellationToken);
        return [.. await catalog.ListUsableAliasesAsync(identity, cancellationToken)];
    }

    private static DataQualityRunDto ToRunDto(
        JobHistoryEntry entry,
        IReadOnlyList<JobDataQualityFailure> normalizedFailures)
    {
        decimal? quarantineRate = entry.RowsProcessed > 0
            ? (decimal)entry.RowsQuarantined / entry.RowsProcessed
            : null;
        decimal? warnRate = entry.RowsProcessed > 0
            ? (decimal)entry.RowsWarned / entry.RowsProcessed
            : null;

        return new DataQualityRunDto(
            entry.Id,
            entry.JobName,
            entry.StartTime,
            entry.EndTime,
            entry.Status,
            entry.RowsProcessed,
            entry.RowsQuarantined,
            entry.RowsWarned,
            quarantineRate,
            warnRate,
            normalizedFailures.Count > 0
                ? normalizedFailures.Select(failure => new DataQualityRuleFailureDto(
                    failure.ColumnName,
                    failure.Rule,
                    failure.FailureCount,
                    failure.TargetTable,
                    failure.Action,
                    failure.Owner)).ToList()
                : ParseRuleFailures(entry.DataQualityFailures));
    }

    /// <summary>
    /// Parses the compact <c>column:rule=count;…</c> history payload. Rule text can itself contain
    /// ':' and '=' (a MATCHES regex, for instance), so the column is taken up to the first ':' and
    /// the count from the last '='.
    /// </summary>
    private static IReadOnlyList<DataQualityRuleFailureDto> ParseRuleFailures(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return [];

        var failures = new List<DataQualityRuleFailureDto>();
        foreach (var entry in payload.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = entry.IndexOf(':');
            int equals = entry.LastIndexOf('=');
            if (colon <= 0 || equals <= colon) continue;
            if (!long.TryParse(entry[(equals + 1)..], out var count)) continue;

            failures.Add(new DataQualityRuleFailureDto(
                entry[..colon],
                entry[(colon + 1)..equals],
                count,
                CountsOnly: true));
        }
        return failures;
    }

    [HttpGet("quarantine/rows")]
    public async Task<IActionResult> GetQuarantineRows(
        [FromQuery] string quarantineTarget,
        [FromQuery] string? jobName = null,
        [FromQuery] string status = DataQualityColumns.QuarantinedStatus,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(quarantineTarget))
            return BadRequest(new { error = "QuarantineTarget is required." });

        status = string.IsNullOrWhiteSpace(status) ? DataQualityColumns.QuarantinedStatus : status.Trim();
        if (!AllowedRowStatuses.Contains(status))
            return BadRequest(new { error = "Unsupported quarantine row status filter." });

        limit = Math.Clamp(limit, 1, 200);
        var manifest = await FindManifestAsync(quarantineTarget, jobName);
        if (manifest is null)
            return NotFound(new { error = "Quarantine replay manifest was not found." });
        if (!IsSafeReplayTarget(manifest.QuarantineTarget))
            return Conflict(new { error = "Quarantine replay manifest contains an invalid target name." });

        // Refuse a read we already know this process cannot perform, and say why. Running it
        // anyway produces either an engine resolution error dressed up as a 502, or — for a
        // #temp target, which auto-creates empty — an empty result that reads as "nothing was
        // quarantined". Both are worse than declining with a reason.
        var previewEnabled = portalConfig.DataQuality.AllowConnectionPreview;
        var usableAliases = previewEnabled
            ? await ResolveUsableConnectionAliasesAsync(cancellationToken)
            : null;
        var readability = ETL_SQL.Portal.Services.QuarantineTargetReadability
            .Describe(manifest.QuarantineTarget, manifest, previewEnabled, usableAliases);
        if (!readability.Readable)
        {
            return Conflict(new
            {
                error = readability.Reason,
                reviewStatement = ETL_SQL.Portal.Services.QuarantineTargetReadability
                    .BuildReviewStatement(manifest.QuarantineTarget)
            });
        }

        var where = status.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" WHERE {DataQualityColumns.Status} = {ToSqlLiteral(status)}";
        // The connection is bootstrapped from the *manifest's* alias and connector type — never
        // from the request — and the only statement that follows is a SELECT from the manifest's
        // target. Resolving it as SHARED: sends it through the governed path, so policy, secret
        // resolution, and redaction apply exactly as they do to any script using that connection.
        var bootstrap = $"CREATE CONNECTION {readability.ConnectionAlias} AS "
            + $"{readability.ConnectorType}('SHARED:{readability.ConnectionAlias}');\n";
        var script = bootstrap
            + $"SET MAX_LAST_RESULT_ROWS = {limit};\nSELECT * FROM {manifest.QuarantineTarget}{where};";

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            var sessionContext = new CliContext
            {
                Command = "run",
                BatchSize = limit,
                IsSilentMode = true,
                SessionId = $"dq-rows-{Guid.NewGuid():N}"
            };
            await using var session = new ExecutionSession(services, sessionContext, engineLogger);
            // Quarantine rows are copies of raw source data, so the preview must run under the
            // caller's execution identity — row-level security and PII controls apply here exactly
            // as they do to any other data view.
            var identity = await BuildExecutionIdentityAsync(timeout.Token);
            var result = await session.ExecuteAsync(
                script, timeout.Token, "portal-data-quality-rows", executionIdentity: identity);
            if (!result.Success)
            {
                var message = result.Diagnostics.Count > 0
                    ? string.Join("; ", result.Diagnostics.Select(d => d.Message))
                    : "Unable to read quarantine rows.";
                return StatusCode(502, new { error = ETL_SQL.Core.Common.SecretRedactor.Redact(message) });
            }

            // Reading raw quarantined source rows is a data-access event, not a page view — the
            // same reason dispositions are audited.
            await audit.LogAsync(CurrentUserId, "READ_QUARANTINE_ROWS", "QuarantineTarget",
                manifest.QuarantineTarget,
                $"connection={readability.ConnectionAlias}; status={status}; limit={limit}");

            var table = session.LastEvaluator?.LastResult ?? new DataTable();
            var columns = table.ColumnNames;
            var rows = table.Rows
                .Take(limit)
                .Select(row => columns.ToDictionary<string, string, object?>(
                    column => column,
                    column => row[column],
                    StringComparer.OrdinalIgnoreCase))
                .Cast<IReadOnlyDictionary<string, object?>>()
                .ToList();

            return Ok(new QuarantineRowsResponse(
                manifest.QuarantineTarget,
                status,
                columns,
                rows,
                table.IsCapped || table.Rows.Count >= limit || table.TotalRowsMatched > limit));
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, new { error = "Quarantine row preview timed out." });
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to preview quarantine rows for {Target}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(manifest.QuarantineTarget));
            return StatusCode(502, new { error = "Unable to read quarantine rows." });
        }
    }

    [HttpPost("quarantine/replay")]
    public async Task<IActionResult> ReplayQuarantine(
        [FromBody] ReplayQuarantineRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QuarantineTarget))
            return BadRequest(new { error = "QuarantineTarget is required." });

        var manifest = await FindManifestAsync(request.QuarantineTarget, request.JobName);
        if (manifest is null)
            return NotFound(new { error = "Quarantine replay manifest was not found." });

        if (!manifest.IsReplayable)
        {
            return Conflict(new
            {
                error = manifest.NonReplayableReason
                    ?? "This quarantine target is not replayable from Portal."
            });
        }

        if (!IsSafeReplayTarget(manifest.QuarantineTarget))
            return Conflict(new { error = "Quarantine replay manifest contains an invalid target name." });

        var replayStatement = $"REPLAY QUARANTINE {manifest.QuarantineTarget};";
        try
        {
            var jobId = await jobChannel.SubmitJobAsync(new JobSubmitRequest
            {
                ScriptText = replayStatement,
                Label = $"Data quality replay: {manifest.QuarantineTarget}",
                SessionId = $"dq-replay-{Guid.NewGuid():N}",
                Metadata = new Dictionary<string, string>
                {
                    ["Workload"] = "DataQualityReplay",
                    ["JobName"] = manifest.JobName,
                    ["QuarantineTarget"] = manifest.QuarantineTarget
                }
            }, cancellationToken);

            await RecordSubmissionAsync(new QuarantineSubmissionRecord(
                jobId, "Replay", manifest.JobName, manifest.QuarantineTarget,
                DateTimeOffset.UtcNow, CurrentUserId, "Submitted", DateTimeOffset.UtcNow));

            // Replay re-runs a production load, so record who triggered it.
            await audit.LogAsync(
                CurrentUserId,
                "DATA_QUALITY_REPLAY",
                "QuarantineTarget",
                manifest.QuarantineTarget,
                $"job={manifest.JobName}; section={manifest.SectionLabel}; submittedJob={jobId}");

            return Accepted(new ReplayQuarantineResponse(jobId, replayStatement));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to submit quarantine replay for {Target}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(manifest.QuarantineTarget));
            return StatusCode(503, new { error = "Unable to submit quarantine replay job." });
        }
    }

    /// <summary>
    /// Status of a submitted replay or disposition.
    ///
    /// <para>Deliberately its own route. <c>GET /api/jobs/{id}</c> is the report-execution
    /// namespace, backed by <c>PortalExecutionJobs</c>; these ids come from <c>IJobChannel</c> and
    /// were never in that table, so polling there answered 404 forever — which the client read as a
    /// transient outage and retried every second for the life of the tab.</para>
    ///
    /// <para>Reconciles as it reads: a non-terminal record is refreshed from the channel and the
    /// outcome written back, so the answer survives the browser that asked for it.</para>
    /// </summary>
    [HttpGet("jobs/{jobId}")]
    public async Task<IActionResult> GetSubmissionStatus(string jobId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return BadRequest(new { error = "JobId is required." });

        var record = await FindSubmissionAsync(jobId.Trim());
        if (record is null)
            return NotFound(new { error = "No data-quality submission was recorded for this job id." });

        if (TerminalJobStatuses.Contains(record.Status))
            return Ok(ToStatusDto(record));

        string status;
        string? error = null;
        string? unknownReason = null;
        try
        {
            var live = await jobChannel.GetStatusAsync(record.JobId, cancellationToken);
            status = live.Status.ToString();
            error = live.ErrorMessage;

            // An in-process channel holds job state in memory and reports "not found" as a failure
            // once the process has restarted. Reporting that verbatim would tell a steward their
            // replay failed when it may have completed — so a not-found answer for a job we know we
            // submitted is Unknown, which is the truth.
            if (string.Equals(error, "Job not found.", StringComparison.OrdinalIgnoreCase))
            {
                status = "Unknown";
                error = null;
                unknownReason = "The service that was running this job no longer has a record of it, "
                    + "usually because it restarted. Check the job history for the outcome.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            // Reachability is not an outcome. Leave the record alone and say so, rather than
            // recording a terminal state we did not observe.
            logger.LogWarning(ex, "Could not reach the job channel for data-quality job {JobId}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(record.JobId));
            return Ok(ToStatusDto(record) with
            {
                UnknownReason = "The job service is unreachable; this is the last status recorded."
            });
        }

        var updated = record with
        {
            Status = status,
            Error = error,
            StatusUpdatedAtUtc = DateTimeOffset.UtcNow
        };
        if (!string.Equals(updated.Status, record.Status, StringComparison.OrdinalIgnoreCase))
            await RecordSubmissionAsync(updated);

        return Ok(ToStatusDto(updated) with { UnknownReason = unknownReason });
    }

    private static QuarantineSubmissionStatusDto ToStatusDto(QuarantineSubmissionRecord record) =>
        new(record.JobId,
            record.Kind,
            record.QuarantineTarget,
            record.Status,
            TerminalJobStatuses.Contains(record.Status),
            record.SubmittedAtUtc,
            record.StatusUpdatedAtUtc,
            record.Error);

    [HttpPost("quarantine/disposition")]
    public async Task<IActionResult> UpdateQuarantineDisposition(
        [FromBody] QuarantineDispositionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QuarantineTarget))
            return BadRequest(new { error = "QuarantineTarget is required." });
        if (request.RowIds is null || request.RowIds.Count == 0)
            return BadRequest(new { error = "At least one row id is required." });
        if (request.RowIds.Count > 500)
            return BadRequest(new { error = "A disposition update is limited to 500 row ids." });

        var disposition = request.Disposition?.Trim().ToLowerInvariant();
        if (disposition is not (
                DataQualityColumns.ReleasedStatus
                or DataQualityColumns.ReplayedStatus
                or DataQualityColumns.DiscardedStatus))
            return BadRequest(new { error = "Disposition must be 'released', 'replayed', or 'discarded'." });

        var manifest = await FindManifestAsync(request.QuarantineTarget, request.JobName);
        if (manifest is null)
            return NotFound(new { error = "Quarantine replay manifest was not found." });
        if (!IsSafeReplayTarget(manifest.QuarantineTarget))
            return Conflict(new { error = "Quarantine replay manifest contains an invalid target name." });
        if (disposition == DataQualityColumns.ReleasedStatus && !manifest.IsReplayable)
        {
            return Conflict(new
            {
                error = manifest.NonReplayableReason
                    ?? "This quarantine target is not replayable, so rows cannot be released for replay."
            });
        }

        if (!TryBuildDispositionStatement(manifest.QuarantineTarget, disposition, request.RowIds, request.Changes, out var statement, out var error))
            return BadRequest(new { error });

        try
        {
            var jobId = await jobChannel.SubmitJobAsync(new JobSubmitRequest
            {
                ScriptText = statement,
                Label = $"Data quality disposition: {manifest.QuarantineTarget}",
                SessionId = $"dq-disposition-{Guid.NewGuid():N}",
                Metadata = new Dictionary<string, string>
                {
                    ["Workload"] = "DataQualityDisposition",
                    ["JobName"] = manifest.JobName,
                    ["QuarantineTarget"] = manifest.QuarantineTarget,
                    ["Disposition"] = disposition
                }
            }, cancellationToken);

            await RecordSubmissionAsync(new QuarantineSubmissionRecord(
                jobId, "Disposition", manifest.JobName, manifest.QuarantineTarget,
                DateTimeOffset.UtcNow, CurrentUserId, "Submitted", DateTimeOffset.UtcNow,
                Disposition: disposition, RowCount: request.RowIds.Count));

            // Audit the decision, not just the mechanics: who released or discarded which rows,
            // and why. "Why did we drop these 400 rows, and who decided?" is the question an audit
            // actually asks, and the quarantine table itself cannot answer it — its schema is
            // frozen, and a note column there would be editable by the same person.
            await audit.LogAsync(
                CurrentUserId,
                disposition == DataQualityColumns.DiscardedStatus
                    ? "DATA_QUALITY_DISCARD"
                    : "DATA_QUALITY_RELEASE",
                "QuarantineTarget",
                manifest.QuarantineTarget,
                BuildDispositionAuditDetail(manifest, request, disposition, jobId));

            return Accepted(new QuarantineDispositionResponse(jobId, statement));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to submit quarantine disposition for {Target}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(manifest.QuarantineTarget));
            return StatusCode(503, new { error = "Unable to submit quarantine disposition job." });
        }
    }

    /// <summary>
    /// Resolves the caller's roles and groups so row-level security applies to the quarantine row
    /// preview. Mirrors <c>PortalDesignerPreviewService.BuildIdentityAsync</c>.
    /// </summary>
    private async Task<ETL_SQL.Core.Governance.ExecutionIdentity?> BuildExecutionIdentityAsync(
        CancellationToken cancellationToken)
    {
        var claimIdentity = ETL_SQL.Portal.Services.PortalDesignerSchemaService.BuildIdentity(User);
        if (claimIdentity.EffectiveUserId is not int userId)
            return claimIdentity;

        var portalUser = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Users),
                u => u.Id == userId,
                cancellationToken);
        if (portalUser is null) return null;

        var roles = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId && r.Name != null
            select r.Name!,
            cancellationToken);
        var groups = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            from ug in db.UserGroups
            join g in db.Groups on ug.GroupId equals g.Id
            where ug.UserId == userId
            select g.Name,
            cancellationToken);

        var name = portalUser.UserName ?? claimIdentity.EffectiveUser ?? userId.ToString();
        return claimIdentity with
        {
            EffectiveUser = name,
            RealUser = name,
            IsAdmin = roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) || User.IsInRole("Admin"),
            AdminBypassesRowLevelSecurity = portalConfig.Security.AdminBypassRowLevelSecurity,
            Roles = roles,
            Groups = groups
        };
    }

    private async Task<QuarantineReplayManifest?> FindManifestAsync(string quarantineTarget, string? jobName)
    {
        var states = await jobHistory.GetJobStatesAsync(
            string.IsNullOrWhiteSpace(jobName) ? null : jobName.Trim(),
            5000);

        return states
            .Where(s => s.StateKey.StartsWith(QuarantineManifestPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(ReadManifest)
            .Where(manifest => manifest != null)
            .Select(manifest => manifest!)
            .Where(manifest => manifest.QuarantineTarget.Equals(quarantineTarget.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(manifest => manifest.UpdatedAtUtc)
            .FirstOrDefault();
    }

    private static QuarantineQueueItemDto? TryReadManifest(
        JobStateEntry state,
        bool previewEnabled,
        IReadOnlyCollection<string>? usableAliases)
    {
        if (string.IsNullOrWhiteSpace(state.StateValue)) return null;

        try
        {
            var manifest = ReadManifest(state);
            if (manifest == null) return null;
            var readability = ETL_SQL.Portal.Services.QuarantineTargetReadability
                .Describe(manifest.QuarantineTarget, manifest, previewEnabled, usableAliases);
            return new QuarantineQueueItemDto(
                manifest.JobName,
                manifest.ScriptPath,
                manifest.SectionLabel,
                manifest.SourceTable,
                manifest.QuarantineTarget,
                manifest.IsReplayable,
                manifest.NonReplayableReason,
                manifest.InputColumns,
                manifest.InputSchemaFingerprint,
                manifest.UpdatedAtUtc,
                manifest.ReplayMode,
                manifest.ProbeSourceTable,
                manifest.JoinBuildTable,
                manifest.JoinObservedN1,
                manifest.JoinNonReplayableReason,
                $"REPLAY QUARANTINE {manifest.QuarantineTarget};",
                readability.Readable,
                readability.Reason,
                ETL_SQL.Portal.Services.QuarantineTargetReadability
                    .BuildReviewStatement(manifest.QuarantineTarget));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static QuarantineReplayManifest? ReadManifest(JobStateEntry state)
    {
        if (string.IsNullOrWhiteSpace(state.StateValue)) return null;
        try { return JsonSerializer.Deserialize<QuarantineReplayManifest>(state.StateValue); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Builds the audit detail for a disposition: which rows, which job, the steward's stated
    /// reason, and which source columns were edited. Edited <em>values</em> are deliberately not
    /// recorded — quarantine rows carry raw source data, and the audit log is not an access-
    /// controlled data surface.
    /// </summary>
    private static string BuildDispositionAuditDetail(
        QuarantineReplayManifest manifest,
        QuarantineDispositionRequest request,
        string disposition,
        string jobId)
    {
        var parts = new List<string>
        {
            $"disposition={disposition}",
            $"job={manifest.JobName}",
            $"rows={request.RowIds.Count}",
            $"submittedJob={jobId}"
        };

        if (request.Changes is { Count: > 0 })
            parts.Add($"editedColumns={string.Join(",", request.Changes.Keys)}");

        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            var note = request.Note.Trim();
            if (note.Length > 500) note = note[..500] + "…";
            parts.Add($"note={note}");
        }

        // Row ids are the audit's link back to the evidence; cap the list so one bulk action
        // cannot write an unbounded audit row.
        parts.Add($"rowIds={string.Join(",", request.RowIds.Take(50))}"
            + (request.RowIds.Count > 50 ? $" (+{request.RowIds.Count - 50} more)" : ""));

        return string.Join("; ", parts);
    }

    private static bool IsSafeReplayTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > 256) return false;
        if (target[0] == '.') return false;
        var lastWasDot = false;
        foreach (var c in target)
        {
            if (c == '.')
            {
                if (lastWasDot) return false;
                lastWasDot = true;
                continue;
            }

            lastWasDot = false;
            if (!(char.IsLetterOrDigit(c) || c is '_' or '#' or '@'))
                return false;
        }

        return !lastWasDot;
    }

    private static string SubmissionKey(string kind, string target) =>
        $"{QuarantineSubmissionPrefix}{kind.ToLowerInvariant()}:{target}";

    /// <summary>
    /// Records the submission durably before the steward's browser is the only thing that knows
    /// about it. Best-effort: a job that ran is a fact whether or not we managed to note it, so a
    /// write failure is logged and never turns a successful submission into an error response.
    /// </summary>
    private async Task RecordSubmissionAsync(QuarantineSubmissionRecord record)
    {
        try
        {
            await jobHistory.SetJobStateAsync(
                record.JobName,
                SubmissionKey(record.Kind, record.QuarantineTarget),
                JsonSerializer.Serialize(record));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not record the data-quality submission for {Target}; the job was still submitted.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(record.QuarantineTarget));
        }
    }

    private async Task<QuarantineSubmissionRecord?> FindSubmissionAsync(string jobId)
    {
        var states = await jobHistory.GetJobStatesAsync(null, 5000);
        foreach (var state in states)
        {
            if (!state.StateKey.StartsWith(QuarantineSubmissionPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var record = TryReadSubmission(state.StateValue);
            if (record is not null && string.Equals(record.JobId, jobId, StringComparison.Ordinal))
                return record;
        }
        return null;
    }

    private static QuarantineSubmissionRecord? TryReadSubmission(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return JsonSerializer.Deserialize<QuarantineSubmissionRecord>(value); }
        catch (JsonException) { return null; }
    }

    private static bool TryBuildDispositionStatement(
        string target,
        string disposition,
        IReadOnlyList<string> rowIds,
        IReadOnlyDictionary<string, string?>? changes,
        out string statement,
        out string? error)
    {
        statement = string.Empty;
        error = null;

        var assignments = new List<string>();
        if (changes is not null)
        {
            if (changes.Count > 50)
            {
                error = "A disposition update is limited to 50 edited columns.";
                return false;
            }

            foreach (var (column, value) in changes)
            {
                if (string.IsNullOrWhiteSpace(column) || !IsSafeReplayTarget(column) || column.Contains('.'))
                {
                    error = $"Invalid column name '{column}'.";
                    return false;
                }
                if (DataQualityColumns.IsDataQualityColumn(column))
                {
                    error = $"Data-quality evidence column '{column}' cannot be edited from Portal.";
                    return false;
                }

                assignments.Add($"{column} = {ToSqlLiteral(value)}");
            }
        }

        assignments.Add($"{DataQualityColumns.Status} = {ToSqlLiteral(disposition)}");

        var ids = new List<string>();
        foreach (var rowId in rowIds)
        {
            if (string.IsNullOrWhiteSpace(rowId) || rowId.Length > 256 || rowId.Any(char.IsControl))
            {
                error = "Row ids must be non-empty strings without control characters.";
                return false;
            }
            ids.Add(ToSqlLiteral(rowId));
        }

        statement = $"UPDATE {target} SET {string.Join(", ", assignments)} "
            + $"WHERE {DataQualityColumns.RowId} IN ({string.Join(", ", ids)}) "
            + $"AND {DataQualityColumns.Status} = {ToSqlLiteral(DataQualityColumns.QuarantinedStatus)};";
        return true;
    }

    private static string ToSqlLiteral(string? value) =>
        value is null
            ? "NULL"
            : "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static bool Matches(QuarantineQueueItemDto item, string query) =>
        Contains(item.JobName, query)
        || Contains(item.ScriptPath, query)
        || Contains(item.SectionLabel, query)
        || Contains(item.SourceTable, query)
        || Contains(item.QuarantineTarget, query)
        || Contains(item.NonReplayableReason, query)
        || item.InputColumns.Any(column => Contains(column, query));

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
