using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly PortalDbContext db;
    private readonly AuditService audit;
    private readonly PortalConfig portalConfig;

    public ReportsController(PortalDbContext db, AuditService audit, PortalConfig portalConfig)
    {
        this.db = db;
        this.audit = audit;
        this.portalConfig = portalConfig;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin => User.IsInRole("Admin");

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

    private ReportDto ToDto(Report r, ReportSnapshot? snap)
    {
        bool isStale = false;
        if (snap is not null
            && PortalPathGuard.TryResolveScript(portalConfig, r.ScriptPath, out var resolvedScriptPath)
            && System.IO.File.Exists(resolvedScriptPath))
            isStale = System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath) > snap.BuiltAt;

        bool scriptChanged = false;
        if (r.PublishedScriptHash is not null
            && PortalPathGuard.TryResolveScript(portalConfig, r.ScriptPath, out resolvedScriptPath)
            && System.IO.File.Exists(resolvedScriptPath))
        {
            var currentHash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(System.IO.File.ReadAllBytes(resolvedScriptPath))).ToLowerInvariant();
            scriptChanged = !string.Equals(currentHash, r.PublishedScriptHash, StringComparison.OrdinalIgnoreCase);
        }

        return new ReportDto(
            r.Id, r.FolderId, r.Folder?.Path ?? "",
            r.Name, r.Description, r.ScriptPath,
            r.ScriptLastModified,
            snap is not null,
            snap?.BuiltAt,
            isStale,
            scriptChanged);
    }

    // ── GET /api/folders/{id}/reports ─────────────────────────────────────────

    [HttpGet("folders/{folderId:int}/reports")]
    public async Task<IActionResult> GetByFolder(int folderId)
    {
        var perm = await GetEffectivePermissionAsync(folderId);
        if (perm is null) return Forbid();

        var reports = await db.Reports
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => r.FolderId == folderId && !r.IsDeleted)
            .ToListAsync();

        return Ok(reports.Select(r => ToDto(r, r.Snapshots.FirstOrDefault())));
    }

    // ── POST /api/reports ─────────────────────────────────────────────────────

    [HttpPost("reports")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> Publish([FromBody] PublishReportRequest req)
    {
        var perm = await GetEffectivePermissionAsync(req.FolderId);
        if (perm is null || perm < FolderPermission.Manage)
            return Forbid();

        if (!await db.Folders.AnyAsync(f => f.Id == req.FolderId))
            return NotFound("Folder not found");

        if (!PortalPathGuard.TryResolveScript(portalConfig, req.ScriptPath, out var resolved))
            return BadRequest(new { error = "Script path must be within the configured ScriptRootPath" });

        var lastModified = System.IO.File.Exists(resolved)
            ? System.IO.File.GetLastWriteTimeUtc(resolved)
            : DateTime.UtcNow;

        string? publishedHash = null;
        if (System.IO.File.Exists(resolved))
            publishedHash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(System.IO.File.ReadAllBytes(resolved))).ToLowerInvariant();

        var report = new Report
        {
            FolderId            = req.FolderId,
            Name                = req.Name,
            Description         = req.Description,
            ScriptPath          = resolved,
            ScriptLastModified  = lastModified,
            PublishedScriptHash = publishedHash,
            CreatedBy           = CurrentUserId,
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "PUBLISH_REPORT", "Report", report.Id.ToString(), report.Name);

        return CreatedAtAction(nameof(GetById), new { id = report.Id }, ToDto(report, null));
    }

    // ── GET /api/reports/{id} ─────────────────────────────────────────────────

    [HttpGet("reports/{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        return Ok(ToDto(report, report.Snapshots.FirstOrDefault()));
    }

    // ── PUT /api/reports/{id} ─────────────────────────────────────────────────

    [HttpPut("reports/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReportRequest req)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        if (req.Name is not null)        report.Name        = req.Name;
        if (req.Description is not null) report.Description = req.Description;
        if (req.FolderId.HasValue)
        {
            var targetPerm = await GetEffectivePermissionAsync(req.FolderId.Value);
            if (targetPerm is null || targetPerm < FolderPermission.Manage)
                return Forbid();
            report.FolderId = req.FolderId.Value;
        }

        if (req.ScriptPath is not null)
        {
            var scriptRoot = portalConfig.ScriptRootPath;
            if (string.IsNullOrWhiteSpace(scriptRoot))
                return BadRequest(new { error = "ScriptRootPath is not configured." });

            if (!PortalPathGuard.TryResolveScript(portalConfig, req.ScriptPath, out var resolved))
                return BadRequest(new { error = "Script path must be within the configured ScriptRootPath" });

            if (!System.IO.File.Exists(resolved))
                return BadRequest(new { error = $"Script file not found: {req.ScriptPath}" });

            report.ScriptPath = resolved;
            var bytes = await System.IO.File.ReadAllBytesAsync(resolved);
            report.PublishedScriptHash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            report.ScriptLastModified  = System.IO.File.GetLastWriteTimeUtc(resolved);
        }

        report.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_REPORT", "Report", id.ToString());

        return Ok(ToDto(report, null));
    }

    // ── GET /api/reports/{id}/parameters ─────────────────────────────────────

    /// <summary>
    /// Parses the report script and returns metadata for all INPUT-declared parameters.
    /// No script execution occurs. Used by the subscription UI to build parameter forms.
    /// </summary>
    [HttpGet("reports/{id:int}/parameters")]
    public async Task<IActionResult> GetParameters(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        if (!System.IO.File.Exists(resolvedScriptPath))
            return Ok(Array.Empty<ReportParameterDto>());

        var scriptText = await System.IO.File.ReadAllTextAsync(resolvedScriptPath);
        var tokens     = new Lexer(scriptText).Tokenize();
        var parser     = new CoreParser(tokens, scriptText);
        var script     = parser.Parse();

        var parameters = script.Statements
            .OfType<DeclareStatement>()
            .Where(d => d.IsInput)
            .Select(d => new ReportParameterDto(
                d.VariableName,
                d.DataType,
                d.InitialValue is LiteralExpression lit ? lit.Value?.ToString() : null,
                d.InitialValue is null,
                d.Description))
            .ToList();

        return Ok(parameters);
    }

    // ── DELETE /api/reports/{id} ──────────────────────────────────────────────

    [HttpDelete("reports/{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool cascade = false)
    {
        var report = await db.Reports
            .Include(r => r.Subscriptions.Where(s => s.IsActive))
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        bool hasActive = report.Subscriptions.Any();
        if (hasActive && !cascade)
            return Conflict(new { error = "Report has active subscriptions. Use ?cascade=true." });

        if (cascade)
            foreach (var sub in report.Subscriptions)
                sub.IsActive = false;

        report.IsDeleted = true;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_REPORT", "Report", id.ToString(), report.Name);
        return NoContent();
    }

    // ── POST /api/scripts/upload ──────────────────────────────────────────────

    [HttpPost("scripts/upload")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> UploadScript([FromBody] UploadScriptRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Filename))
            return BadRequest(new { error = "Filename is required." });

        // Reject any path separators — filename only, no subdirectory traversal.
        if (req.Filename.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || req.Filename.Contains('/') || req.Filename.Contains('\\'))
            return BadRequest(new { error = "Filename must not contain path separators." });

        if (!req.Filename.EndsWith(".rptsql", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .rptsql files may be uploaded." });

        byte[] content;
        try { content = Convert.FromBase64String(req.ContentBase64); }
        catch { return BadRequest(new { error = "ContentBase64 is not valid base64." }); }

        var root = portalConfig.ScriptRootPath;
        if (string.IsNullOrWhiteSpace(root))
            return StatusCode(503, new { error = "ScriptRootPath is not configured on the portal." });

        Directory.CreateDirectory(root);
        var destination = System.IO.Path.Combine(root, req.Filename);

        await System.IO.File.WriteAllBytesAsync(destination, content);

        var relativePath = System.IO.Path.GetRelativePath(root, destination).Replace('\\', '/');
        return Ok(new UploadScriptResponse(relativePath));
    }

    // ── GET /api/reports/available-scripts ───────────────────────────────────

    [HttpGet("reports/available-scripts")]
    [Authorize(Roles = "Admin,Publisher")]
    public IActionResult GetAvailableScripts()
    {
        var root = portalConfig.ScriptRootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) 
            return Ok(Array.Empty<string>());

        var files = Directory.GetFiles(root, "*.rptsql", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(f => f)
            .ToList();

        return Ok(files);
    }

    // ── GET /api/maps/custom ─────────────────────────────────────────────────

    [HttpGet("maps/custom")]
    public async Task<IActionResult> GetCustomMap([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Path is required." });

        if (Path.IsPathRooted(path))
            return BadRequest(new { error = "Map path must be relative." });

        var ext = Path.GetExtension(path);
        if (!ext.Equals(".geojson", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only .json and .geojson map files are supported." });
        }

        if (!PortalPathGuard.TryResolveMap(portalConfig, path, out var resolved))
            return Forbid();

        if (!System.IO.File.Exists(resolved))
            return NotFound(new { error = "Map file not found." });

        var json = await System.IO.File.ReadAllTextAsync(resolved);
        return Content(json, "application/geo+json");
    }

}
