using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/studio")]
[Authorize(Roles = "Admin,Publisher")]
[RequirePortalModule("Reporting")]
[RequirePortalModule("Designer")]
[RequireStudioCapability(StudioCapabilities.StudioAccess,
    StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
public sealed partial class StudioController(
    PortalDbContext db,
    FolderPermissionService folderPermissions,
    StudioAuthorizationService studioAuthorization,
    IArtifactStorage artifacts,
    PortalConfig portalConfig,
    PortalScriptSourceControlService sourceControl,
    AuditService audit) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// What this caller may do in Studio. Reachable by any authenticated user, overriding the
    /// class-level role and capability requirements.
    ///
    /// <para>This is a <b>probe</b>: the shell calls it on every page load to decide whether to
    /// offer Studio at all. Gating it by role made the answer for everyone else an error rather
    /// than "nothing" — a console error on every sign-in for three of the five roles, and a
    /// capability check that cannot be asked without already having the capability.</para>
    /// </summary>
    // AllowAnonymous, then an explicit authentication check. An action-level [Authorize] does NOT
    // override the class-level [Authorize(Roles = "Admin,Publisher")] -- both apply -- which is why
    // the first attempt at this fix left the endpoint answering 403 to everyone else. AllowAnonymous
    // is the only attribute that takes the action out of that policy, so the authentication
    // requirement is restated here rather than inherited.
    [HttpGet("session")]
    [AllowAnonymous]
    [AllowStudioCapabilityBypass]
    public ActionResult<StudioSessionDto> GetSession()
    {
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();

        return Ok(new StudioSessionDto(
            studioAuthorization.Mode.ToString(),
            studioAuthorization.EffectiveCapabilities(User),
            studioAuthorization.Mode == StudioDeploymentMode.SourceControlled && sourceControl.IsEnabled));
    }

    [HttpGet("reports")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<IReadOnlyList<StudioReportDto>>> GetReports(CancellationToken ct)
    {
        var reports = await db.Reports
            .AsNoTracking()
            .Include(report => report.Folder)
            .Where(report => !report.IsDeleted)
            .OrderBy(report => report.Folder.Path)
            .ThenBy(report => report.Name)
            .ToListAsync(ct);

        var visible = new List<StudioReportDto>();
        foreach (var report in reports)
        {
            var permission = await folderPermissions.GetEffectiveReportPermissionAsync(report, User);
            if (!permission.AtLeast(FolderPermission.Author))
                continue;
            visible.Add(ToDto(report));
        }

        return Ok(visible);
    }

    [HttpGet("folders")]
    [RequireStudioCapability(StudioCapabilities.ScriptSave,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<IReadOnlyList<StudioFolderDto>>> GetFolders(CancellationToken ct)
    {
        var folders = await db.Folders.AsNoTracking().OrderBy(folder => folder.Path).ToListAsync(ct);
        var writable = new List<StudioFolderDto>();
        foreach (var folder in folders)
        {
            if (await folderPermissions.HasPermissionAsync(folder.Id, FolderPermission.Manage, User))
                writable.Add(new StudioFolderDto(folder.Id, folder.Path, folder.Name));
        }
        return Ok(writable);
    }

    [HttpPost("reports")]
    [RequireStudioCapability(StudioCapabilities.ScriptSave,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    [RequireStudioCapability(StudioCapabilities.ReportPublish,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioReportDto>> CreateReport(
        [FromBody] CreateStudioReportRequest request,
        CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 160)
            return BadRequest(new { error = "Report name must be between 1 and 160 characters." });
        if (request.ScriptText is null || request.ScriptText.Length > Math.Max(1, portalConfig.DesignerLimits.MaxScriptCharacters))
            return BadRequest(new { error = $"Script text exceeds the {portalConfig.DesignerLimits.MaxScriptCharacters} character limit." });

        var folder = await db.Folders.FirstOrDefaultAsync(value => value.Id == request.FolderId, ct);
        if (folder is null)
            return NotFound(new { error = "Folder not found." });
        if (!await folderPermissions.HasPermissionAsync(folder.Id, FolderPermission.Manage, User))
            return Forbid();
        if (await db.Reports.AnyAsync(report =>
                report.FolderId == folder.Id && !report.IsDeleted && report.Name == name, ct))
            return Conflict(new { error = $"A report named '{name}' already exists in {folder.Path}." });

        sourceControl.ValidateScriptTextForCommit(request.ScriptText);
        var scriptKey = $"studio/{folder.Id}/{Slugify(name)}-{Guid.NewGuid():N}.rptsql";
        if (!PortalPathGuard.TryResolveScript(portalConfig, scriptKey, out var resolvedScriptPath))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The Studio script storage root is unavailable." });

        var now = DateTime.UtcNow;
        var report = new Report
        {
            FolderId = folder.Id,
            Folder = folder,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ScriptPath = resolvedScriptPath,
            ScriptLastModified = now,
            PublishedScriptHash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(request.ScriptText))).ToLowerInvariant(),
            CreatedBy = CurrentUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await artifacts.WriteAllTextAsync(ArtifactArea.Scripts, scriptKey, request.ScriptText,
            overwrite: false, ct: ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.Reports.Add(report);
            await db.SaveChangesAsync(ct);
            audit.Stage(CurrentUserId, "CREATE_STUDIO_REPORT", "Report", report.Id.ToString(),
                $"{folder.Path}/{name}; mode={studioAuthorization.Mode}");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            await artifacts.DeleteAsync(ArtifactArea.Scripts, scriptKey, ct);
            throw;
        }

        return CreatedAtAction(nameof(GetReports), new { }, ToDto(report));
    }

    private static StudioReportDto ToDto(Report report) => new(
        report.Id,
        report.FolderId,
        report.Folder.Path,
        report.Name,
        report.Description,
        report.UpdatedAt,
        report.Version);

    private static string Slugify(string value)
    {
        var slug = UnsafeSlugCharacters().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "report" : slug[..Math.Min(slug.Length, 80)];
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeSlugCharacters();
}
