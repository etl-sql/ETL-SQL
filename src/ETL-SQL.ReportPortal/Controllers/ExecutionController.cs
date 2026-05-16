using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using ETL_SQL.Reporting;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ExecutionController(
    PortalDbContext     db,
    ExecutionJobService jobService,
    SessionCache        sessions,
    AuditService        audit,
    PortalConfig        portalConfig) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin => User.IsInRole("Admin");

    // ── Permission helper (shared with ReportsController) ────────────────────

    private async Task<FolderPermission?> GetEffectivePermissionAsync(int folderId)
    {
        if (IsAdmin) return FolderPermission.Manage;
        var groupIds = await db.UserGroups
            .Where(ug => ug.UserId == CurrentUserId)
            .Select(ug => ug.GroupId)
            .ToListAsync();
        var perms = await db.FolderAcls
            .Where(a => a.FolderId == folderId && groupIds.Contains(a.GroupId))
            .Select(a => a.Permission)
            .ToListAsync();
        if (!perms.Any()) return null;
        return (FolderPermission)perms.Max(p => (int)p);
    }

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

        var jobId = jobService.EnqueueExecution(id, CurrentUserId, resolvedScriptPath, req?.Parameters);
        await audit.LogAsync(CurrentUserId, "EXECUTE_REPORT", "Report", id.ToString(), scriptHash);

        return Accepted(new { jobId });
    }

    // ── 2.1  GET /api/jobs/{jobId} ────────────────────────────────────────────

    [HttpGet("jobs/{jobId}")]
    public IActionResult GetJob(string jobId)
    {
        var job = jobService.Get(jobId);
        if (job is null) return NotFound();

        return Ok(new JobStatusResponse(
            job.Id, job.Status.ToString(),
            job.CreatedAt, job.StartedAt, job.CompletedAt,
            job.ManifestPath, job.Error));
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

        if (!PortalPathGuard.TryResolveSnapshot(portalConfig, snapshot.ManifestPath, out var resolvedManifestPath))
            return Forbid();

        bool isStale = false;
        if (System.IO.File.Exists(resolvedScriptPath))
            isStale = System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath) > snapshot.BuiltAt;

        object? manifest = null;
        if (includeManifest && System.IO.File.Exists(resolvedManifestPath))
        {
            var json = await System.IO.File.ReadAllTextAsync(resolvedManifestPath);
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
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out _))
            return Forbid();

        var snapshot = await db.ReportSnapshots
            .Where(s => s.ReportId == id)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();

        if (snapshot is null)
            return NotFound(new { error = "No snapshot available." });

        if (!PortalPathGuard.TryResolveSnapshot(portalConfig, snapshot.ManifestPath, out var resolvedManifestPath))
            return Forbid();

        if (!System.IO.File.Exists(resolvedManifestPath))
            return NotFound(new { error = "No snapshot available." });

        var json = await System.IO.File.ReadAllTextAsync(resolvedManifestPath);
        return Content(json, "application/json");
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

        string? existingJobId = jobService.GetActiveRefreshJobId(id);
        bool alreadyRunning   = existingJobId is not null;

        var jobId = alreadyRunning
            ? existingJobId!
            : jobService.EnqueueRefresh(id, CurrentUserId, resolvedScriptPath);

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

        var svc      = await GetOrRebuildSessionAsync(id, resolvedScriptPath);
        var manifest = await svc.SetParameterAsync(req.Name, req.Value ?? string.Empty, req.IsInteraction);
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

        var svc      = await GetOrRebuildSessionAsync(id, resolvedScriptPath);
        var updates  = (req.Params ?? Enumerable.Empty<ParameterUpdateRequest>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => (p.Name, p.Value));
        var manifest = await svc.SetParametersAsync(updates, req.IsInteraction);
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

        var svc      = await GetOrRebuildSessionAsync(id, resolvedScriptPath);
        var manifest = req.Direction?.ToUpperInvariant() == "UP"
            ? await svc.DrillUpAsync(req.VisualName, req.TargetDepth)
            : await svc.DrillInAsync(req.VisualName, req.ClickedValue ?? "");
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

        var svc      = await GetOrRebuildSessionAsync(id, resolvedScriptPath);
        var manifest = await svc.RefreshVisualsAsync(req.Visuals);
        return manifest is null ? NotFound() : Ok(manifest);
    }

    // ── Session helper ────────────────────────────────────────────────────────

    private async Task<ETL_SQL.ReportHosting.DashboardService> GetOrRebuildSessionAsync(
        int reportId, string scriptPath)
    {
        var svc = sessions.GetOrCreate(reportId, CurrentUserId, scriptPath);

        // If session was just created and there is a snapshot, prime it from disk
        // so the user doesn't wait for a full re-execution on parameter change.
        if (svc.IsStale())
        {
            var snapshot = await db.ReportSnapshots
                .Where(s => s.ReportId == reportId)
                .OrderByDescending(s => s.BuiltAt)
                .FirstOrDefaultAsync();

            if (snapshot is not null
                && PortalPathGuard.TryResolveSnapshot(portalConfig, snapshot.ManifestPath, out var resolvedManifestPath)
                && System.IO.File.Exists(resolvedManifestPath))
            {
                var store    = new SnapshotStore();
                var manifest = await store.LoadAsync(resolvedManifestPath);
                // DashboardService doesn't expose a direct "seed from manifest" API, so
                // if there's a saved snapshot we just let the first real interaction trigger
                // a rebuild — the session will be warm after the first call.
            }
        }

        return svc;
    }
}
