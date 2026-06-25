using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Reporting;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ExecutionController(
    PortalDbContext db,
    ExecutionJobService jobService,
    SessionCache sessions,
    AuditService audit,
    PortalConfig portalConfig,
    FolderPermissionService folderPermissions,
    ETL_SQL.Core.Storage.IArtifactStorage artifacts,
    SnapshotPackageService snapshotPackages) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    private Task<FolderPermission?> GetEffectivePermissionAsync(int folderId) =>
        folderPermissions.GetEffectivePermissionAsync(folderId, User);

    // ── 2.1  POST /api/reports/{id}/execute ──────────────────────────────────

    [HttpPost("reports/{id:int}/execute")]
    public async Task<IActionResult> Execute(int id, [FromBody] ExecuteRequest? req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Execute) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        string? scriptHash = null;
        if (System.IO.File.Exists(resolvedScriptPath))
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(resolvedScriptPath);
            scriptHash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        var jobId = await jobService.EnqueueExecutionAsync(
            id,
            CurrentUserId,
            resolvedScriptPath,
            req?.Parameters,
            isAdministrator: IsAdmin);
        await audit.LogAsync(CurrentUserId, "EXECUTE_REPORT", "Report", id.ToString(), scriptHash);

        return Accepted(new { jobId });
    }

    // ── 2.1  GET /api/jobs/{jobId} ────────────────────────────────────────────

    [HttpGet("jobs/{jobId}")]
    public async Task<IActionResult> GetJob(string jobId)
    {
        var job = await jobService.GetAsync(jobId);
        if (job is null) return NotFound();

        return Ok(new JobStatusResponse(
            job.Id, job.Status.ToString(),
            job.CreatedAt, job.StartedAt, job.CompletedAt,
            job.ManifestPath, job.Error));
    }

    [HttpDelete("jobs/{jobId}")]
    public async Task<IActionResult> CancelJob(string jobId)
    {
        var job = await jobService.GetAsync(jobId);
        if (job is null) return NotFound();
        if (!IsAdmin && job.UserId != CurrentUserId) return Forbid();

        var reason = $"Execution was cancelled by user {CurrentUserId}.";
        var cancelled = await jobService.CancelAsync(jobId, reason);
        if (!cancelled)
        {
            var current = await jobService.GetAsync(jobId);
            return Conflict(new
            {
                jobId,
                status = current?.Status.ToString() ?? "Unknown",
                error = current?.Error
            });
        }

        await audit.LogAsync(CurrentUserId, "CANCEL_EXECUTION_JOB", "ExecutionJob", jobId);
        return Accepted(new { jobId, status = JobStatus.Cancelled.ToString() });
    }

    // ── 2.2  GET /api/reports/{id}/snapshot ──────────────────────────────────

    [HttpGet("reports/{id:int}/snapshot")]
    public async Task<IActionResult> GetSnapshot(int id, [FromQuery] bool includeManifest = false)
    {
        var report = await db.Reports
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        var snapshot = await db.ReportSnapshots
            .Where(s => s.ReportId == id)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();

        if (snapshot is null)
            return NotFound(new { error = "No snapshot available. Execute the report first." });

        var manifestKey = PortalPathGuard.ToSnapshotKey(portalConfig, snapshot.ManifestPath);
        if (manifestKey is null)
            return Forbid();

        bool isStale = false;
        if (System.IO.File.Exists(resolvedScriptPath))
            isStale = System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath) > snapshot.BuiltAt;

        object? manifest = null;
        if (includeManifest && await artifacts.ExistsAsync(ETL_SQL.Core.Storage.ArtifactArea.Snapshots, manifestKey))
        {
            var json = await snapshotPackages.LoadLayoutJsonAsync(manifestKey);
            manifest = JsonDocument.Parse(json).RootElement;
        }

        report.LastViewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "VIEW_SNAPSHOT", "Report", id.ToString());
        return Ok(new SnapshotResponse(id, snapshot.ManifestPath, snapshot.BuiltAt, isStale, manifest));
    }

    // ── 2.2  GET /api/reports/{id}/manifest (alias used by report-runtime.js) ──

    [HttpGet("reports/{id:int}/manifest")]
    public Task<IActionResult> GetManifestAlias(int id) => GetManifest(id);

    // ── 2.2  GET /api/reports/{id}/snapshot/manifest ─────────────────────────
    // Returns the raw manifest JSON (for the report viewer JS to consume).

    [HttpGet("reports/{id:int}/snapshot/manifest")]
    public async Task<IActionResult> GetManifest(int id)
    {
        var resolved = await ResolveReadableSnapshotKeyAsync(id);
        if (resolved.Error is not null) return resolved.Error;

        var json = await snapshotPackages.LoadLightweightLayoutJsonAsync(
            resolved.Key!,
            visualIndex => Url.Action(nameof(GetSnapshotRows), new { id, visualIndex })
                ?? $"/api/reports/{id}/snapshot/rows/{visualIndex}",
            visualIndex => Url.Action(nameof(GetSnapshotArrowRows), new { id, visualIndex })
                ?? $"/api/reports/{id}/snapshot/rows/{visualIndex}.arrow");
        return Content(json, "application/json");
    }

    // ── 2.2  GET /api/reports/{id}/snapshot/rows/{visualIndex} ───────────────
    // Returns rows for one visual when a large snapshot table was omitted from the
    // browser manifest. Permission checks mirror the manifest endpoint.

    [HttpGet("reports/{id:int}/snapshot/rows/{visualIndex:int}")]
    public async Task<IActionResult> GetSnapshotRows(int id, int visualIndex)
    {
        var resolved = await ResolveReadableSnapshotKeyAsync(id);
        if (resolved.Error is not null) return resolved.Error;

        var rows = await snapshotPackages.LoadRowsAsync(resolved.Key!, visualIndex, HttpContext.RequestAborted);
        return rows is null
            ? NotFound(new { error = "Snapshot visual rows are not available." })
            : Ok(rows);
    }

    // ── 2.2  GET /api/reports/{id}/snapshot/rows/{visualIndex}.arrow ─────────
    // Exposes the stored Arrow IPC table for clients that can consume Arrow
    // directly; the browser runtime currently uses the JSON row endpoint.

    [HttpGet("reports/{id:int}/snapshot/rows/{visualIndex:int}.arrow")]
    public async Task<IActionResult> GetSnapshotArrowRows(int id, int visualIndex)
    {
        var resolved = await ResolveReadableSnapshotKeyAsync(id);
        if (resolved.Error is not null) return resolved.Error;

        var table = await snapshotPackages.LoadArrowTableAsync(resolved.Key!, visualIndex, HttpContext.RequestAborted);
        return table is null
            ? NotFound(new { error = "Snapshot Arrow table is not available." })
            : File(table, "application/vnd.apache.arrow.stream");
    }

    private async Task<(string? Key, IActionResult? Error)> ResolveReadableSnapshotKeyAsync(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return (null, NotFound());

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return (null, Forbid());

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out _))
            return (null, Forbid());

        var snapshot = await db.ReportSnapshots
            .Where(s => s.ReportId == id)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();

        if (snapshot is null)
            return (null, NotFound(new { error = "No snapshot available." }));

        var manifestKey = PortalPathGuard.ToSnapshotKey(portalConfig, snapshot.ManifestPath);
        if (manifestKey is null)
            return (null, Forbid());

        if (!await artifacts.ExistsAsync(ETL_SQL.Core.Storage.ArtifactArea.Snapshots, manifestKey))
            return (null, NotFound(new { error = "No snapshot available." }));

        return (manifestKey, null);
    }

    // ── 2.4  POST /api/reports/{id}/refresh ──────────────────────────────────

    [HttpPost("reports/{id:int}/refresh")]
    public async Task<IActionResult> Refresh(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Execute) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        string? existingJobId = await jobService.GetActiveRefreshJobIdAsync(id);
        bool alreadyRunning = existingJobId is not null;

        var jobId = alreadyRunning
            ? existingJobId!
            : await jobService.EnqueueRefreshAsync(
                id,
                CurrentUserId,
                resolvedScriptPath,
                isAdministrator: IsAdmin);

        if (!alreadyRunning)
            await audit.LogAsync(CurrentUserId, "REFRESH_REPORT", "Report", id.ToString());

        return Accepted(new RefreshResponse(jobId, alreadyRunning));
    }

    // ── 2.3  POST /api/reports/{id}/parameter ────────────────────────────────
    // Applies a single parameter to the user's session without touching the snapshot.

    [HttpPost("reports/{id:int}/parameter")]
    public async Task<IActionResult> SetParameter(int id, [FromBody] ParameterUpdateRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "name is required" });

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        var svc = await GetOrRebuildSessionAsync(id, resolvedScriptPath);
        var manifest = await svc.SetParameterAsync(req.Name, req.Value ?? string.Empty, req.IsInteraction);
        await TryPersistAdHocLineageAsync(id, resolvedScriptPath, svc);
        return Ok(manifest);
    }

    // ── 2.3  POST /api/reports/{id}/parameters ───────────────────────────────

    [HttpPost("reports/{id:int}/parameters")]
    public async Task<IActionResult> SetParameters(int id, [FromBody] BatchParameterRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        // Empty params is valid for non-interaction calls (cross-filter deselect = reset to clean state).
        if (req.Params is null && req.IsInteraction)
            return BadRequest(new { error = "params array is required" });

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        var svc = await GetOrRebuildSessionAsync(id, resolvedScriptPath);
        var updates = (req.Params ?? Enumerable.Empty<ParameterUpdateRequest>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => (p.Name, p.Value));
        var manifest = await svc.SetParametersAsync(updates, req.IsInteraction, req.PageName);
        await TryPersistAdHocLineageAsync(id, resolvedScriptPath, svc);
        return Ok(manifest);
    }

    // ── 2.4  POST /api/reports/{id}/drill ────────────────────────────────────

    [HttpPost("reports/{id:int}/drill")]
    public async Task<IActionResult> Drill(int id, [FromBody] DrillRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        if (string.IsNullOrWhiteSpace(req.VisualName))
            return BadRequest(new { error = "visualName is required" });

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        var svc = await GetOrRebuildSessionAsync(id, resolvedScriptPath);
        var manifest = req.Direction?.ToUpperInvariant() == "UP"
            ? await svc.DrillUpAsync(req.VisualName, req.TargetDepth)
            : await svc.DrillInAsync(req.VisualName, req.ClickedValue ?? "");
        await TryPersistAdHocLineageAsync(id, resolvedScriptPath, svc);
        return manifest is null ? NotFound() : Ok(manifest);
    }

    // ── 2.4  POST /api/reports/{id}/refresh-visuals ──────────────────────────

    [HttpPost("reports/{id:int}/refresh-visuals")]
    public async Task<IActionResult> RefreshVisuals(int id, [FromBody] RefreshVisualsRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        if (req.Visuals is null || req.Visuals.Count == 0 || req.Visuals.All(string.IsNullOrWhiteSpace))
            return BadRequest(new { error = "visuals is required" });

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        var svc = await GetOrRebuildSessionAsync(id, resolvedScriptPath);
        var manifest = await svc.RefreshVisualsAsync(req.Visuals);
        await TryPersistAdHocLineageAsync(id, resolvedScriptPath, svc);
        return manifest is null ? NotFound() : Ok(manifest);
    }

    private async Task TryPersistAdHocLineageAsync(int id, string scriptPath, ETL_SQL.ReportHosting.DashboardService svc)
    {
        if (portalConfig.Resources.PersistAdHocInteractions)
        {
            try
            {
                var lineage = svc.CurrentLineageTracker?.GetFullLineage().ToList();
                if (lineage is { Count: > 0 })
                {
                    var catalog = HttpContext.RequestServices.GetService<ILineageCatalogStore>();
                    if (catalog != null)
                    {
                        await catalog.SaveLineageAsync(
                            lineage,
                            $"report:{id}:interaction",
                            scriptPath,
                            DateTime.UtcNow);
                    }
                }
            }
            catch (Exception ex)
            {
                // Fire and forget
                await audit.LogAsync(CurrentUserId, "PERSIST_LINEAGE_FAILED", "Report", id.ToString(), ex.Message);
            }
        }
    }

    // ── Session helper ────────────────────────────────────────────────────────

    private async Task<ETL_SQL.ReportHosting.DashboardService> GetOrRebuildSessionAsync(
        int reportId, string scriptPath)
    {
        var svc = sessions.GetOrCreate(
            reportId,
            CurrentUserId,
            scriptPath,
            isAdministrator: IsAdmin);

        // If session was just created and there is a snapshot, verify it is available from shared
        // artifact storage. DashboardService doesn't expose a direct seed-from-manifest API, so
        // the first real interaction still rebuilds; this check is only a cheap availability probe.
        // so the user doesn't wait for a full re-execution on parameter change.
        if (svc.IsStale())
        {
            var snapshot = await db.ReportSnapshots
                .Where(s => s.ReportId == reportId)
                .OrderByDescending(s => s.BuiltAt)
                .FirstOrDefaultAsync();

            if (snapshot is not null
                && PortalPathGuard.ToSnapshotKey(portalConfig, snapshot.ManifestPath) is { } manifestKey
                && await artifacts.ExistsAsync(ETL_SQL.Core.Storage.ArtifactArea.Snapshots, manifestKey))
            {
                // Snapshot exists in the shared storage backend.
            }
        }

        return svc;
    }
}
