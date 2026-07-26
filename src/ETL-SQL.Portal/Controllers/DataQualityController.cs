using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/data-quality")]
[Authorize]
[RequirePortalModule("Reporting")]
public sealed class DataQualityController(
    IJobHistoryStore jobHistory,
    IJobChannel jobChannel,
    ILogger<DataQualityController> logger) : ControllerBase
{
    private const string QuarantineManifestPrefix = "dq:quarantine-manifest:";

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

        var query = q?.Trim();
        var items = states
            .Where(s => s.StateKey.StartsWith(QuarantineManifestPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(TryReadManifest)
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
        if (disposition is not (DataQualityColumns.ReleasedStatus or DataQualityColumns.DiscardedStatus))
            return BadRequest(new { error = "Disposition must be 'released' or 'discarded'." });

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

    private static QuarantineQueueItemDto? TryReadManifest(JobStateEntry state)
    {
        if (string.IsNullOrWhiteSpace(state.StateValue)) return null;

        try
        {
            var manifest = ReadManifest(state);
            if (manifest == null) return null;
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
                $"REPLAY QUARANTINE {manifest.QuarantineTarget};");
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
