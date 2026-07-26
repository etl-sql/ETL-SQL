using System.Text.Json;
using ETL_SQL.Core.Data;
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
