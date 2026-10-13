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
[Authorize]
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
    AuditService audit,
    DatasetTenantScope? tenantScope = null,
    PortalTenantCatalogScope? catalogScope = null) : ControllerBase
{
    private readonly DatasetTenantScope _tenantScope = tenantScope ?? new DatasetTenantScope(portalConfig);
    private PortalTenantCatalogScope CatalogScope => catalogScope ?? new PortalTenantCatalogScope(db, _tenantScope);
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
            studioAuthorization.Mode == StudioDeploymentMode.SourceControlled && sourceControl.IsEnabled,
            portalConfig.Studio.DraftRecovery,
            portalConfig.Studio.PrivateDrafts,
            Math.Max(1, portalConfig.DesignerLimits.MaxScriptCharacters)));
    }

    [HttpGet("reports")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<IReadOnlyList<StudioReportDto>>> GetReports(CancellationToken ct)
    {
        var reports = await CatalogScope.Documents
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

    [HttpGet("catalog")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioCatalogPageDto>> SearchCatalog(
        CancellationToken ct, [FromQuery] string? query = null, [FromQuery] int afterId = 0)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length > 160 || afterId < 0) return BadRequest(new { error = "Invalid catalog search or cursor." });
        var candidates = await CatalogScope.Documents.AsNoTracking().Include(report => report.Folder)
            .Where(report => !report.IsDeleted && report.Id > afterId
                && (query == "" || report.Name.Contains(query) || report.Folder.Path.Contains(query)))
            .OrderBy(report => report.Id).Take(51).ToListAsync(ct);
        var inspected = candidates.Take(50).ToList();
        var permissions = await folderPermissions.GetEffectiveReportPermissionsAsync(inspected, User, ct);
        var visible = inspected.Where(report => permissions[report.Id].AtLeast(FolderPermission.Author)).Select(ToDto).ToList();
        return Ok(new StudioCatalogPageDto(visible, candidates.Count > 50 ? inspected[^1].Id : null));
    }

    [HttpGet("reports/{id:int}/summary")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<StudioReportDto>> GetReportSummary(int id, CancellationToken ct)
    {
        var report = await CatalogScope.Documents.AsNoTracking().Include(report => report.Folder)
            .FirstOrDefaultAsync(report => report.Id == id && !report.IsDeleted, ct);
        if (report is null || !(await folderPermissions.GetEffectiveReportPermissionAsync(report, User)).AtLeast(FolderPermission.Author))
            return NotFound(new { error = "The document is unavailable or outside your catalog permissions. Refresh Studio Home." });
        return Ok(ToDto(report));
    }

    [HttpGet("folders")]
    [RequireStudioCapability(StudioCapabilities.ScriptSave,
        StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
    public async Task<ActionResult<IReadOnlyList<StudioFolderDto>>> GetFolders(CancellationToken ct)
    {
        var folders = await CatalogScope.Folders.AsNoTracking().OrderBy(folder => folder.Path).ToListAsync(ct);
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
        var kind = CatalogDocumentKind.Report;
        if (!string.IsNullOrWhiteSpace(request.Kind) && !Enum.TryParse(request.Kind, ignoreCase: true, out kind))
            return BadRequest(new { error = "Kind must be Report or Pipeline." });
        var (refusal, report) = await CreateCatalogDocumentAsync(
            request.Name, kind, request.FolderId, request.Description, request.ScriptText,
            "CREATE_STUDIO_REPORT", auditDetail: null, alsoInTransaction: null, ct);
        return refusal ?? CreatedAtAction(nameof(GetReports), new { }, ToDto(report!));
    }

    /// <summary>
    /// Creates one catalog document with every check a catalog create runs. A refusal has changed
    /// nothing. <paramref name="alsoInTransaction"/> commits or rolls back with the document, which
    /// is how publishing a private draft removes the draft only if the document exists.
    /// </summary>
    private async Task<(ActionResult? Refusal, Report? Report)> CreateCatalogDocumentAsync(
        string? requestedName, CatalogDocumentKind kind, int folderId, string? description, string? scriptText,
        string auditAction, string? auditDetail, Func<Task>? alsoInTransaction, CancellationToken ct)
    {
        var name = requestedName?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 160)
            return (BadRequest(new { error = "Report name must be between 1 and 160 characters." }), null);
        if (scriptText is null || scriptText.Length > Math.Max(1, portalConfig.DesignerLimits.MaxScriptCharacters))
            return (BadRequest(new { error = $"Script text exceeds the {portalConfig.DesignerLimits.MaxScriptCharacters} character limit." }), null);

        var folder = await CatalogScope.Folders.FirstOrDefaultAsync(value => value.Id == folderId, ct);
        if (folder is null)
            return (NotFound(new { error = "Folder not found." }), null);
        if (!await folderPermissions.HasPermissionAsync(folder.Id, FolderPermission.Manage, User))
            return (Forbid(), null);
        // Names are unique across kinds in a folder, so a pipeline and a report never read the same.
        if (await CatalogScope.Documents.AnyAsync(report =>
                report.FolderId == folder.Id && !report.IsDeleted && report.Name == name, ct))
            return (Conflict(new { error = $"A document named '{name}' already exists in {folder.Path}." }), null);

        sourceControl.ValidateScriptTextForCommit(scriptText);
        var extension = kind == CatalogDocumentKind.Pipeline ? ".etlsql" : ".rptsql";
        var scriptKey = $"studio/{folder.Id}/{Slugify(name)}-{Guid.NewGuid():N}{extension}";
        if (!PortalPathGuard.TryResolveScript(
                portalConfig, _tenantScope.TenantId, scriptKey, out var resolvedScriptPath))
            return (StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The Studio script storage root is unavailable." }), null);

        var now = DateTime.UtcNow;
        var report = new Report
        {
            TenantId = _tenantScope.TenantId,
            FolderId = folder.Id,
            Folder = folder,
            Kind = kind,
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            ScriptPath = resolvedScriptPath,
            ScriptLastModified = now,
            PublishedScriptHash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(scriptText))).ToLowerInvariant(),
            CreatedBy = CurrentUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await artifacts.WriteAllTextAsync(ArtifactArea.Scripts, scriptKey, scriptText,
            overwrite: false, ct: ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.Reports.Add(report);
            await db.SaveChangesAsync(ct);
            if (alsoInTransaction is not null) await alsoInTransaction();
            audit.Stage(CurrentUserId, auditAction, "Report", report.Id.ToString(),
                $"{folder.Path}/{name}; kind={kind}; mode={studioAuthorization.Mode}"
                + (auditDetail is null ? "" : $"; {auditDetail}"));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            await artifacts.DeleteAsync(ArtifactArea.Scripts, scriptKey, ct);
            throw;
        }

        return (null, report);
    }

    private static StudioReportDto ToDto(Report report) => new(
        report.Id,
        report.FolderId,
        report.Folder.Path,
        report.Name,
        report.Description,
        report.UpdatedAt,
        report.Version,
        report.Kind.ToString());

    private static string Slugify(string value)
    {
        var slug = UnsafeSlugCharacters().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "report" : slug[..Math.Min(slug.Length, 80)];
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeSlugCharacters();
}
