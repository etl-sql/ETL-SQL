using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api")]
[Authorize]
[RequirePortalModule("Reporting")]
public class ReportsController : ControllerBase
{
    private static readonly TimeSpan DefaultAnonymousAccessLifetime = TimeSpan.FromDays(7);
    private readonly PortalDbContext db;
    private readonly AuditService audit;
    private readonly PortalConfig portalConfig;
    private readonly ILineageCatalogStore lineageCatalog;
    private readonly FolderPermissionService folderPermissions;
    private readonly ReportScriptInspectionService scriptInspection;
    private readonly ReportScriptSaveService scriptSave;
    private readonly PortalScriptSourceControlService sourceControl;
    private readonly IDatasetRegistry datasetRegistry;
    private readonly ETL_SQL.Core.Storage.IArtifactStorage artifacts;
    private readonly ReportStructureService reportStructure;

    public ReportsController(PortalDbContext db, AuditService audit, PortalConfig portalConfig, ILineageCatalogStore lineageCatalog, FolderPermissionService folderPermissions, ReportScriptInspectionService scriptInspection, ReportScriptSaveService scriptSave, PortalScriptSourceControlService sourceControl, IDatasetRegistry datasetRegistry, ETL_SQL.Core.Storage.IArtifactStorage artifacts, ReportStructureService reportStructure)
    {
        this.db = db;
        this.audit = audit;
        this.portalConfig = portalConfig;
        this.lineageCatalog = lineageCatalog;
        this.folderPermissions = folderPermissions;
        this.scriptInspection = scriptInspection;
        this.scriptSave = scriptSave;
        this.sourceControl = sourceControl;
        this.datasetRegistry = datasetRegistry;
        this.artifacts = artifacts;
        this.reportStructure = reportStructure;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin => User.IsInRole("Admin");

    private Task<FolderPermission?> GetEffectivePermissionAsync(int folderId) =>
        folderPermissions.GetEffectivePermissionAsync(folderId, User);

    /// <summary>
    /// Converts a stored <c>ScriptPath</c> — which may be absolute (older publish rows) or relative
    /// (uploads) — into a Scripts-area-relative key for <see cref="ETL_SQL.Core.Storage.IArtifactStorage"/>,
    /// or null if it escapes the configured script root. Reuses the existing within-root guard for the
    /// security check, so no catalog data migration is needed to route script I/O through the seam.
    /// </summary>
    private string? ToScriptKey(string? scriptPath) => PortalPathGuard.ToScriptKey(portalConfig, scriptPath);

    private async Task<bool> CreatorCanResolveAsync(
        int creatorId,
        int folderId,
        CancellationToken ct = default)
    {
        var creator = await db.Users.FirstOrDefaultAsync(user => user.Id == creatorId, ct);
        if (creator is null || !creator.IsActive)
            return false;

        var isAdmin = await db.UserRoles
            .Join(db.Roles, userRole => userRole.RoleId, role => role.Id,
                (userRole, role) => new { userRole.UserId, role.Name })
            .AnyAsync(value => value.UserId == creatorId && value.Name == "Admin", ct);
        if (isAdmin)
            return true;

        var groupIds = await db.UserGroups
            .Where(membership => membership.UserId == creatorId)
            .Select(membership => membership.GroupId)
            .ToListAsync(ct);
        var permission = await folderPermissions.GetEffectivePermissionAsync(
            folderId, new HashSet<int>(groupIds));
        return permission >= FolderPermission.Read;
    }

    private ReportDto ToDto(Report r, ReportSnapshot? snap, bool isFavorite = false)
    {
        var isStale = snap is not null && r.ScriptLastModified > snap.BuiltAt;
        var scriptChanged = false;

        return new ReportDto(
            r.Id, r.FolderId, r.Folder?.Path ?? "",
            r.Name, r.Description,
            r.Owner, r.Contact, r.Tags, r.Category, r.Domain, r.Steward, r.Certification,
            DeserializeMetadata(r.MetadataJson),
            r.ScriptPath,
            r.ScriptLastModified,
            snap is not null,
            snap?.BuiltAt,
            r.LastViewedAt,
            r.LastRefreshStartedAt,
            r.LastRefreshCompletedAt,
            r.LastRefreshStatus,
            r.LastRefreshError,
            r.LastRefreshDurationMs,
            isFavorite,
            isStale,
            scriptChanged,
            r.Version);
    }

    private async Task<string> GenerateUniqueShareTokenAsync()
    {
        while (true)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            if (!await db.ReportShareLinks.AnyAsync(l => l.Token == token))
                return token;
        }
    }

    private ReportShareLinkDto ToShareLinkDto(ReportShareLink link)
    {
        var report = link.Report;
        var folderPath = report.Folder?.Path ?? "";
        return new ReportShareLinkDto(
            link.Id,
            link.ReportId,
            report.Name,
            folderPath,
            link.Token,
            $"{Request.Scheme}://{Request.Host}/api/share/{link.Token}",
            link.CreatedBy,
            link.CreatedAt,
            link.ExpiresAt,
            link.RevokedAt);
    }

    private async Task<string> GenerateUniqueEmbedTokenAsync()
    {
        while (true)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (!await db.ReportEmbedTokens.AnyAsync(t => t.Token == token))
                return token;
        }
    }

    private ReportEmbedTokenDto ToEmbedTokenDto(ReportEmbedToken token)
    {
        var report = token.Report;
        return new ReportEmbedTokenDto(
            token.Id,
            token.ReportId,
            report.Name,
            token.Name,
            token.Token,
            $"{Request.Scheme}://{Request.Host}/api/embed/{token.Token}",
            token.CreatedBy,
            token.CreatedAt,
            token.ExpiresAt,
            token.RevokedAt);
    }

    private static SavedReportViewDto ToSavedViewDto(SavedReportView view) =>
        new(
            view.Id,
            view.ReportId,
            view.Name,
            DeserializeDictionary(view.ParametersJson),
            DeserializeDictionary(view.FiltersJson),
            view.IsDefault,
            view.CreatedAt,
            view.UpdatedAt);

    private static ReportAlertDto ToAlertDto(ReportAlert alert) =>
        new(
            alert.Id,
            alert.ReportId,
            alert.Name,
            alert.VisualName,
            alert.Operator,
            alert.Threshold,
            alert.Recipient,
            alert.SmtpAlias,
            alert.IsActive,
            alert.CreatedAt,
            alert.UpdatedAt,
            alert.LastCheckedAt,
            alert.LastTriggeredAt);

    private async Task ClearDefaultSavedViewsAsync(int reportId)
    {
        var defaults = await db.SavedReportViews
            .Where(v => v.ReportId == reportId && v.UserId == CurrentUserId && v.IsDefault)
            .ToListAsync();
        foreach (var view in defaults)
            view.IsDefault = false;
    }

    private static bool IsSupportedAlertOperator(string op) =>
        op is ">" or ">=" or "<" or "<=" or "=" or "!=";

    private static string? SerializeDictionary(Dictionary<string, string>? values) =>
        values is null || values.Count == 0
            ? null
            : JsonSerializer.Serialize(values.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value));

    private static Dictionary<string, string>? DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
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
        var reportIds = reports.Select(r => r.Id).ToList();
        var favoriteIds = await db.ReportFavorites
            .Where(f => f.UserId == CurrentUserId && reportIds.Contains(f.ReportId))
            .Select(f => f.ReportId)
            .ToHashSetAsync();

        return Ok(reports.Select(r => ToDto(r, r.Snapshots.FirstOrDefault(), favoriteIds.Contains(r.Id))));
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

        var validation = await scriptInspection.ValidateResolvedScriptAsync(resolved);
        if (!validation.IsValid)
            return BadRequest(validation);

        var scriptMetadata = new Dictionary<string, string>(validation.Metadata, StringComparer.OrdinalIgnoreCase);

        var report = new Report
        {
            FolderId = req.FolderId,
            Name = req.Name,
            Description = FirstNonBlank(req.Description, GetMetadata(scriptMetadata, "description", "d")),
            Owner = FirstNonBlank(req.Owner, GetMetadata(scriptMetadata, "owner")),
            Contact = FirstNonBlank(req.Contact, GetMetadata(scriptMetadata, "contact")),
            Tags = FirstNonBlank(req.Tags, GetMetadata(scriptMetadata, "tags")),
            Category = FirstNonBlank(req.Category, GetMetadata(scriptMetadata, "category")),
            Domain = FirstNonBlank(req.Domain, GetMetadata(scriptMetadata, "domain")),
            Steward = FirstNonBlank(req.Steward, GetMetadata(scriptMetadata, "steward")),
            Certification = FirstNonBlank(req.Certification, GetMetadata(scriptMetadata, "certification", "trusted")),
            MetadataJson = SerializeMetadata(scriptMetadata),
            ScriptPath = resolved,
            ScriptLastModified = validation.LastModified ?? DateTime.UtcNow,
            PublishedScriptHash = validation.Hash,
            CreatedBy = CurrentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        audit.Stage(CurrentUserId, "PUBLISH_REPORT", "Report", report.Id.ToString(), report.Name);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return CreatedAtAction(nameof(GetById), new { id = report.Id }, ToDto(report, null));
    }

    // ── POST /api/reports/validate ───────────────────────────────────────────

    [HttpPost("reports/validate")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> ValidateScript([FromBody] ValidateReportScriptRequest req)
    {
        if (!PortalPathGuard.TryResolveScript(portalConfig, req.ScriptPath, out var resolved))
            return BadRequest(new ReportScriptValidationDto(
                false,
                req.ScriptPath,
                null,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<ReportParameterDto>(),
                ["Script path must be within the configured ScriptRootPath"]));

        var validation = await scriptInspection.ValidateResolvedScriptAsync(resolved);
        return validation.IsValid ? Ok(validation) : BadRequest(validation);
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

        var isFavorite = await db.ReportFavorites.AnyAsync(f => f.UserId == CurrentUserId && f.ReportId == report.Id);
        OptimisticConcurrency.SetETag(Response, report.Version);
        return Ok(ToDto(report, report.Snapshots.FirstOrDefault(), isFavorite));
    }

    // ── GET /api/reports/{id}/dependencies ───────────────────────────────────

    [HttpGet("reports/{id:int}/dependencies")]
    public async Task<IActionResult> GetDependencies(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        var snapshot = await db.ReportSnapshots
            .Where(s => s.ReportId == id)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();

        var manifestDatasets = await scriptInspection.ReadManifestDatasetsAsync(snapshot);

        List<int> datasetGroupIds = IsAdmin
            ? []
            : await db.UserGroups
                .Where(ug => ug.UserId == CurrentUserId)
                .Select(ug => ug.GroupId)
                .ToListAsync();

        var registeredDatasets = (await db.Datasets
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .Where(d => d.OwningReportId == id)
            .OrderBy(d => d.FolderPath)
            .ThenBy(d => d.Name)
            .ToListAsync())
            .Where(d => CanReadDataset(d, datasetGroupIds))
            .ToList();

        var datasetDtos = registeredDatasets
            .Select(d => new ReportDependencyDatasetDto(
                d.Id,
                d.Name,
                d.FolderPath,
                d.AccessLevel.ToString(),
                d.RowCount,
                d.LastRefresh,
                d.RefreshInterval,
                scriptInspection.BuildSourceDtos(scriptInspection.ParseSourceTables(d.SourceQuery), "DatasetSource")))
            .ToList();

        var jobs = await db.DatasetJobs
            .Where(j => j.ReportId == id)
            .OrderBy(j => j.OrchestratorJobName)
            .Select(j => new ReportDependencyRefreshJobDto(
                j.Id,
                j.OrchestratorJobName,
                j.RefreshInterval,
                j.LastRefreshedAt))
            .ToListAsync();

        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in await scriptInspection.ReadScriptSourceTablesAsync(report.ScriptPath))
            sourceNames.Add(source);
        foreach (var source in registeredDatasets.SelectMany(d => scriptInspection.ParseSourceTables(d.SourceQuery)))
            sourceNames.Add(source);
        var lineageEntries = await scriptInspection.ReadScriptLineageAsync(report.ScriptPath);

        var dto = new ReportDependencyDto(
            new ReportDependencyReportDto(report.Id, report.Name, report.Folder?.Path ?? "", report.ScriptPath),
            snapshot is null ? null : new ReportDependencySnapshotDto(snapshot.Id, snapshot.ManifestPath, snapshot.BuiltAt),
            manifestDatasets,
            datasetDtos,
            jobs,
            scriptInspection.BuildSourceDtos(sourceNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), "ScriptSource"),
            lineageEntries);

        return Ok(dto);
    }

    // ── GET /api/reports/{id}/structure ─────────────────────────────────────

    [HttpGet("reports/{id:int}/structure")]
    public async Task<IActionResult> GetStructure(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        var scriptKey = ToScriptKey(report.ScriptPath);
        if (scriptKey is null)
            return Forbid();

        if (!await artifacts.ExistsAsync(ETL_SQL.Core.Storage.ArtifactArea.Scripts, scriptKey))
            return Ok(new DagDto([], []));

        var scriptText = await artifacts.ReadAllTextAsync(ETL_SQL.Core.Storage.ArtifactArea.Scripts, scriptKey);

        try
        {
            return Ok(await reportStructure.BuildAsync(scriptText, id));
        }
        catch (ReportStructureParseException ex)
        {
            return UnprocessableEntity(new { Error = $"Could not parse report script: {ex.Message}" });
        }
    }

    // ── GET /api/reports/{id}/history ────────────────────────────────────────

    [HttpGet("reports/{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        var snapshots = await db.ReportSnapshots
            .Where(s => s.ReportId == id)
            .OrderByDescending(s => s.BuiltAt)
            .Select(s => new ReportHistorySnapshotDto(
                s.Id,
                s.BuiltAt,
                s.BuiltBy,
                s.ManifestPath,
                s.ScriptHashAtRunTime,
                s.HashMatched,
                s.ParametersJson))
            .ToListAsync();

        var resourceId = id.ToString();
        var changes = await db.AuditLogs
            .Where(a => a.ResourceType == "Report" && a.ResourceId == resourceId)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new ReportHistoryChangeDto(
                a.Id,
                a.Action,
                a.Timestamp,
                a.UserId,
                a.Detail))
            .ToListAsync();

        var currentHash = await scriptInspection.ReadCurrentScriptHashAsync(report.ScriptPath);
        var scriptChanged = currentHash is not null
            && report.PublishedScriptHash is not null
            && !string.Equals(currentHash, report.PublishedScriptHash, StringComparison.OrdinalIgnoreCase);

        return Ok(new ReportHistoryDto(
            new ReportDependencyReportDto(report.Id, report.Name, report.Folder?.Path ?? "", report.ScriptPath),
            report.PublishedScriptHash,
            currentHash,
            scriptChanged,
            snapshots,
            changes));
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
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, report, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToDto(report, null));

        if (req.Name is not null) report.Name = req.Name;
        if (req.Description is not null) report.Description = req.Description;
        if (req.Owner is not null) report.Owner = req.Owner;
        if (req.Contact is not null) report.Contact = req.Contact;
        if (req.Tags is not null) report.Tags = req.Tags;
        if (req.Category is not null) report.Category = req.Category;
        if (req.Domain is not null) report.Domain = req.Domain;
        if (req.Steward is not null) report.Steward = req.Steward;
        if (req.Certification is not null) report.Certification = req.Certification;
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

            var validation = await scriptInspection.ValidateResolvedScriptAsync(resolved);
            if (!validation.IsValid)
                return BadRequest(validation);

            report.ScriptPath = resolved;
            report.PublishedScriptHash = validation.Hash;
            report.ScriptLastModified = validation.LastModified ?? DateTime.UtcNow;
            var scriptMetadata = new Dictionary<string, string>(validation.Metadata, StringComparer.OrdinalIgnoreCase);
            report.MetadataJson = SerializeMetadata(scriptMetadata);
            report.Description = FirstNonBlank(req.Description, GetMetadata(scriptMetadata, "description", "d"), report.Description);
            report.Owner = FirstNonBlank(req.Owner, GetMetadata(scriptMetadata, "owner"), report.Owner);
            report.Contact = FirstNonBlank(req.Contact, GetMetadata(scriptMetadata, "contact"), report.Contact);
            report.Tags = FirstNonBlank(req.Tags, GetMetadata(scriptMetadata, "tags"), report.Tags);
            report.Category = FirstNonBlank(req.Category, GetMetadata(scriptMetadata, "category"), report.Category);
            report.Domain = FirstNonBlank(req.Domain, GetMetadata(scriptMetadata, "domain"), report.Domain);
            report.Steward = FirstNonBlank(req.Steward, GetMetadata(scriptMetadata, "steward"), report.Steward);
            report.Certification = FirstNonBlank(req.Certification, GetMetadata(scriptMetadata, "certification", "trusted"), report.Certification);
        }

        report.UpdatedAt = DateTime.UtcNow;
        audit.Stage(CurrentUserId, "UPDATE_REPORT", "Report", id.ToString());
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(report).ReloadAsync();
            return OptimisticConcurrency.Conflict(this, ToDto(report, null));
        }

        var isFavorite = await db.ReportFavorites.AnyAsync(f => f.UserId == CurrentUserId && f.ReportId == report.Id);
        OptimisticConcurrency.SetETag(Response, report.Version);
        return Ok(ToDto(report, null, isFavorite));
    }

    // ── POST /api/reports/{id}/favorite ──────────────────────────────────────

    [HttpPost("reports/{id:int}/favorite")]
    public async Task<IActionResult> AddFavorite(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        var exists = await db.ReportFavorites.AnyAsync(f => f.UserId == CurrentUserId && f.ReportId == id);
        if (!exists)
        {
            db.ReportFavorites.Add(new ReportFavorite { UserId = CurrentUserId, ReportId = id });
            audit.Stage(CurrentUserId, "FAVORITE_REPORT", "Report", id.ToString(), report.Name);
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    // ── DELETE /api/reports/{id}/favorite ────────────────────────────────────

    [HttpDelete("reports/{id:int}/favorite")]
    public async Task<IActionResult> RemoveFavorite(int id)
    {
        var favorite = await db.ReportFavorites
            .FirstOrDefaultAsync(f => f.UserId == CurrentUserId && f.ReportId == id);
        if (favorite is null) return NoContent();

        db.ReportFavorites.Remove(favorite);
        audit.Stage(CurrentUserId, "UNFAVORITE_REPORT", "Report", id.ToString());
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── POST /api/reports/{id}/share-links ──────────────────────────────────

    [HttpPost("reports/{id:int}/share-links")]
    public async Task<IActionResult> CreateShareLink(int id, [FromBody] CreateReportShareLinkRequest? req)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Execute) return Forbid();

        var expiresAt = req?.ExpiresAt ?? DateTime.UtcNow.Add(DefaultAnonymousAccessLifetime);
        if (expiresAt <= DateTime.UtcNow)
            return BadRequest(new { error = "Share link expiration must be in the future." });

        var link = new ReportShareLink
        {
            ReportId = id,
            CreatedBy = CurrentUserId,
            Token = await GenerateUniqueShareTokenAsync(),
            ExpiresAt = expiresAt
        };
        db.ReportShareLinks.Add(link);
        audit.Stage(CurrentUserId, "CREATE_REPORT_SHARE_LINK", "Report", id.ToString(), report.Name);
        await db.SaveChangesAsync();

        link.Report = report;
        return CreatedAtAction(nameof(ResolveShareLink), new { token = link.Token }, ToShareLinkDto(link));
    }

    // ── GET /api/reports/{id}/share-links ───────────────────────────────────

    [HttpGet("reports/{id:int}/share-links")]
    public async Task<IActionResult> GetShareLinks(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || (perm < FolderPermission.Manage && !IsAdmin)) return Forbid();

        var links = await db.ReportShareLinks
            .Include(l => l.Report).ThenInclude(r => r.Folder)
            .Where(l => l.ReportId == id)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        return Ok(links.Select(ToShareLinkDto));
    }

    // ── DELETE /api/reports/{id}/share-links/{token} ────────────────────────

    [HttpDelete("reports/{id:int}/share-links/{token}")]
    public async Task<IActionResult> RevokeShareLink(int id, string token)
    {
        var link = await db.ReportShareLinks
            .Include(l => l.Report)
            .FirstOrDefaultAsync(l => l.ReportId == id && l.Token == token);
        if (link is null) return NoContent();

        var perm = await GetEffectivePermissionAsync(link.Report.FolderId);
        if (perm is null || (perm < FolderPermission.Manage && link.CreatedBy != CurrentUserId)) return Forbid();

        if (link.RevokedAt is null)
        {
            link.RevokedAt = DateTime.UtcNow;
            // Staged so the revocation and its audit row share one commit (P1.6).
            audit.Stage(CurrentUserId, "REVOKE_REPORT_SHARE_LINK", "Report", id.ToString(), token);
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    // ── GET /api/share/{token} ──────────────────────────────────────────────

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-token")]
    [HttpGet("share/{token}")]
    public async Task<IActionResult> ResolveShareLink(string token)
    {
        // COMPAT_BREAK: 0.11
        var link = await db.ReportShareLinks
            .Include(l => l.Report).ThenInclude(r => r.Folder)
            .FirstOrDefaultAsync(l => l.Token == token);
        if (link is null || link.Report.IsDeleted) return NotFound();
        if (link.RevokedAt is not null) return NotFound();
        if (link.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow) return NotFound();

        if (!await CreatorCanResolveAsync(link.CreatedBy, link.Report.FolderId))
            return NotFound();

        await audit.LogAsync(null, "ANONYMOUS_SHARE_LINK_VIEW", "Report",
            link.ReportId.ToString(), $"creator={link.CreatedBy}");
        return Ok(new ReportShareResolutionDto(
            link.ReportId,
            link.Report.Name,
            link.Report.Folder.Path,
            $"/reports/{link.ReportId}",
            link.ExpiresAt));
    }

    // ── Embed tokens ────────────────────────────────────────────────────────

    [HttpPost("reports/{id:int}/embed-tokens")]
    public async Task<IActionResult> CreateEmbedToken(int id, [FromBody] CreateReportEmbedTokenRequest? req)
    {
        var report = await db.Reports.Include(r => r.Folder).FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();
        var expiresAt = req?.ExpiresAt ?? DateTime.UtcNow.Add(DefaultAnonymousAccessLifetime);
        if (expiresAt <= DateTime.UtcNow)
            return BadRequest(new { error = "Embed token expiration must be in the future." });

        var token = new ReportEmbedToken
        {
            ReportId = id,
            CreatedBy = CurrentUserId,
            Name = string.IsNullOrWhiteSpace(req?.Name) ? "Embed token" : req!.Name!,
            Token = await GenerateUniqueEmbedTokenAsync(),
            ExpiresAt = expiresAt
        };
        db.ReportEmbedTokens.Add(token);
        audit.Stage(CurrentUserId, "CREATE_REPORT_EMBED_TOKEN", "Report", id.ToString(), report.Name);
        await db.SaveChangesAsync();
        token.Report = report;
        return CreatedAtAction(nameof(ResolveEmbedToken), new { token = token.Token }, ToEmbedTokenDto(token));
    }

    [HttpGet("reports/{id:int}/embed-tokens")]
    public async Task<IActionResult> GetEmbedTokens(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        var tokens = await db.ReportEmbedTokens
            .Include(t => t.Report).ThenInclude(r => r.Folder)
            .Where(t => t.ReportId == id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        return Ok(tokens.Select(ToEmbedTokenDto));
    }

    [HttpDelete("reports/{id:int}/embed-tokens/{token}")]
    public async Task<IActionResult> RevokeEmbedToken(int id, string token)
    {
        var embed = await db.ReportEmbedTokens.Include(t => t.Report).FirstOrDefaultAsync(t => t.ReportId == id && t.Token == token);
        if (embed is null) return NoContent();
        var perm = await GetEffectivePermissionAsync(embed.Report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();
        if (embed.RevokedAt is null)
        {
            embed.RevokedAt = DateTime.UtcNow;
            audit.Stage(CurrentUserId, "REVOKE_REPORT_EMBED_TOKEN", "Report", id.ToString(), token);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("anonymous-token")]
    [HttpGet("embed/{token}")]
    public async Task<IActionResult> ResolveEmbedToken(string token)
    {
        var embed = await db.ReportEmbedTokens.Include(t => t.Report).ThenInclude(r => r.Folder).FirstOrDefaultAsync(t => t.Token == token);
        if (embed is null || embed.Report.IsDeleted) return NotFound();
        if (embed.RevokedAt is not null) return NotFound();
        if (embed.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow) return NotFound();
        if (!await CreatorCanResolveAsync(embed.CreatedBy, embed.Report.FolderId))
            return NotFound();
        await audit.LogAsync(null, "ANONYMOUS_EMBED_TOKEN_VIEW", "Report",
            embed.ReportId.ToString(), $"creator={embed.CreatedBy}");
        return Ok(new ReportShareResolutionDto(embed.ReportId, embed.Report.Name, embed.Report.Folder.Path, $"/reports/{embed.ReportId}", embed.ExpiresAt));
    }

    [HttpGet("admin/anonymous-report-access")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAnonymousReportAccessInventory()
    {
        var now = DateTime.UtcNow;
        var shares = await db.ReportShareLinks
            .Include(link => link.Report).ThenInclude(report => report.Folder)
            .Include(link => link.Creator)
            .ToListAsync();
        var embeds = await db.ReportEmbedTokens
            .Include(token => token.Report).ThenInclude(report => report.Folder)
            .Include(token => token.Creator)
            .ToListAsync();

        static string Status(
            DateTime? revokedAt,
            DateTime? expiresAt,
            bool creatorActive,
            bool reportDeleted,
            bool creatorAuthorized,
            DateTime now) =>
            revokedAt is not null ? "Revoked"
            : expiresAt is not null && expiresAt <= now ? "Expired"
            : !creatorActive ? "CreatorDisabled"
            : reportDeleted ? "ReportDeleted"
            : !creatorAuthorized ? "PermissionLost"
            : "Active";

        var items = new List<AnonymousReportAccessDto>();
        foreach (var link in shares)
        {
            var creatorAuthorized = await CreatorCanResolveAsync(link.CreatedBy, link.Report.FolderId);
            items.Add(new AnonymousReportAccessDto(
                "ShareLink", link.Id, link.ReportId, link.Report.Name, link.Report.Folder.Path,
                null, link.CreatedBy, link.Creator.UserName, link.Creator.IsActive, link.CreatedAt,
                link.ExpiresAt, link.RevokedAt,
                Status(link.RevokedAt, link.ExpiresAt, link.Creator.IsActive,
                    link.Report.IsDeleted, creatorAuthorized, now)));
        }
        foreach (var token in embeds)
        {
            var creatorAuthorized = await CreatorCanResolveAsync(token.CreatedBy, token.Report.FolderId);
            items.Add(new AnonymousReportAccessDto(
                "EmbedToken", token.Id, token.ReportId, token.Report.Name, token.Report.Folder.Path,
                token.Name, token.CreatedBy, token.Creator.UserName, token.Creator.IsActive, token.CreatedAt,
                token.ExpiresAt, token.RevokedAt,
                Status(token.RevokedAt, token.ExpiresAt, token.Creator.IsActive,
                    token.Report.IsDeleted, creatorAuthorized, now)));
        }

        return Ok(items.OrderByDescending(item => item.CreatedAt));
    }

    // ── Saved parameter/filter views ────────────────────────────────────────

    [HttpGet("reports/{id:int}/saved-views")]
    public async Task<IActionResult> GetSavedViews(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();
        var views = await db.SavedReportViews.Where(v => v.ReportId == id && v.UserId == CurrentUserId).OrderBy(v => v.Name).ToListAsync();
        return Ok(views.Select(ToSavedViewDto));
    }

    [HttpPost("reports/{id:int}/saved-views")]
    public async Task<IActionResult> CreateSavedView(int id, [FromBody] CreateSavedReportViewRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Saved view name is required." });
        if (req.IsDefault) await ClearDefaultSavedViewsAsync(id);

        var view = new SavedReportView
        {
            ReportId = id,
            UserId = CurrentUserId,
            Name = req.Name,
            ParametersJson = SerializeDictionary(req.Parameters),
            FiltersJson = SerializeDictionary(req.Filters),
            IsDefault = req.IsDefault
        };
        db.SavedReportViews.Add(view);
        audit.Stage(CurrentUserId, "CREATE_SAVED_REPORT_VIEW", "Report", id.ToString(), req.Name);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetSavedViews), new { id }, ToSavedViewDto(view));
    }

    [HttpPut("reports/{id:int}/saved-views/{viewId:int}")]
    public async Task<IActionResult> UpdateSavedView(int id, int viewId, [FromBody] UpdateSavedReportViewRequest req)
    {
        var view = await db.SavedReportViews.FirstOrDefaultAsync(v => v.Id == viewId && v.ReportId == id && v.UserId == CurrentUserId);
        if (view is null) return NotFound();
        if (req.Name is not null) view.Name = req.Name;
        if (req.Parameters is not null) view.ParametersJson = SerializeDictionary(req.Parameters);
        if (req.Filters is not null) view.FiltersJson = SerializeDictionary(req.Filters);
        if (req.IsDefault.HasValue)
        {
            if (req.IsDefault.Value) await ClearDefaultSavedViewsAsync(id);
            view.IsDefault = req.IsDefault.Value;
        }
        view.UpdatedAt = DateTime.UtcNow;
        audit.Stage(CurrentUserId, "UPDATE_SAVED_REPORT_VIEW", "Report", id.ToString(), view.Name);
        await db.SaveChangesAsync();
        return Ok(ToSavedViewDto(view));
    }

    [HttpDelete("reports/{id:int}/saved-views/{viewId:int}")]
    public async Task<IActionResult> DeleteSavedView(int id, int viewId)
    {
        var view = await db.SavedReportViews.FirstOrDefaultAsync(v => v.Id == viewId && v.ReportId == id && v.UserId == CurrentUserId);
        if (view is null) return NoContent();
        db.SavedReportViews.Remove(view);
        audit.Stage(CurrentUserId, "DELETE_SAVED_REPORT_VIEW", "Report", id.ToString(), view.Name);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Alerts ───────────────────────────────────────────────────────────────

    [HttpGet("reports/{id:int}/alerts")]
    public async Task<IActionResult> GetAlerts(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();
        var alerts = await db.ReportAlerts.Where(a => a.ReportId == id && (IsAdmin || a.OwnerId == CurrentUserId)).OrderBy(a => a.Name).ToListAsync();
        return Ok(alerts.Select(ToAlertDto));
    }

    [HttpPost("reports/{id:int}/alerts")]
    public async Task<IActionResult> CreateAlert(int id, [FromBody] CreateReportAlertRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Execute) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.VisualName))
            return BadRequest(new { error = "Alert name and visualName are required." });
        if (!IsSupportedAlertOperator(req.Operator)) return BadRequest(new { error = "Unsupported alert operator." });

        var alert = new ReportAlert
        {
            ReportId = id,
            OwnerId = CurrentUserId,
            Name = req.Name,
            VisualName = req.VisualName,
            Operator = req.Operator,
            Threshold = req.Threshold,
            Recipient = req.Recipient,
            SmtpAlias = req.SmtpAlias
        };
        db.ReportAlerts.Add(alert);
        audit.Stage(CurrentUserId, "CREATE_REPORT_ALERT", "Report", id.ToString(), req.Name);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAlerts), new { id }, ToAlertDto(alert));
    }

    [HttpPut("reports/{id:int}/alerts/{alertId:int}")]
    public async Task<IActionResult> UpdateAlert(int id, int alertId, [FromBody] UpdateReportAlertRequest req)
    {
        var alert = await db.ReportAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.ReportId == id);
        if (alert is null) return NotFound();
        if (!IsAdmin && alert.OwnerId != CurrentUserId) return Forbid();
        if (req.Name is not null) alert.Name = req.Name;
        if (req.VisualName is not null) alert.VisualName = req.VisualName;
        if (req.Operator is not null)
        {
            if (!IsSupportedAlertOperator(req.Operator)) return BadRequest(new { error = "Unsupported alert operator." });
            alert.Operator = req.Operator;
        }
        if (req.Threshold.HasValue) alert.Threshold = req.Threshold.Value;
        if (req.Recipient is not null) alert.Recipient = req.Recipient;
        if (req.SmtpAlias is not null) alert.SmtpAlias = req.SmtpAlias;
        if (req.IsActive.HasValue) alert.IsActive = req.IsActive.Value;
        alert.UpdatedAt = DateTime.UtcNow;
        audit.Stage(CurrentUserId, "UPDATE_REPORT_ALERT", "Report", id.ToString(), alert.Name);
        await db.SaveChangesAsync();
        return Ok(ToAlertDto(alert));
    }

    [HttpDelete("reports/{id:int}/alerts/{alertId:int}")]
    public async Task<IActionResult> DeleteAlert(int id, int alertId)
    {
        var alert = await db.ReportAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.ReportId == id);
        if (alert is null) return NoContent();
        if (!IsAdmin && alert.OwnerId != CurrentUserId) return Forbid();
        db.ReportAlerts.Remove(alert);
        audit.Stage(CurrentUserId, "DELETE_REPORT_ALERT", "Report", id.ToString(), alert.Name);
        await db.SaveChangesAsync();
        return NoContent();
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

        var scriptKey = ToScriptKey(report.ScriptPath);
        if (scriptKey is null)
            return Forbid();

        if (!await artifacts.ExistsAsync(ETL_SQL.Core.Storage.ArtifactArea.Scripts, scriptKey))
            return Ok(Array.Empty<ReportParameterDto>());

        var scriptText = await artifacts.ReadAllTextAsync(ETL_SQL.Core.Storage.ArtifactArea.Scripts, scriptKey);
        var tokens = new Lexer(scriptText).Tokenize();
        var parser = new CoreParser(tokens, scriptText);
        var script = parser.Parse();

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

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? GetMetadata(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }

    private static string? SerializeMetadata(IReadOnlyDictionary<string, string> metadata) =>
        metadata.Count == 0
            ? null
            : JsonSerializer.Serialize(metadata
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value));

    private static IReadOnlyDictionary<string, string> DeserializeMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
            return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool CanReadDataset(Dataset dataset, IReadOnlyCollection<int> groupIds)
    {
        if (IsAdmin) return true;
        if (dataset.AccessLevel == DatasetAccessLevel.Public) return true;
        if (dataset.OwningReport?.CreatedBy == CurrentUserId) return true;

        return dataset.Acls.Any(a =>
            groupIds.Contains(a.GroupId)
            && a.Permission is DatasetPermission.Viewer
                or DatasetPermission.Refresh
                or DatasetPermission.Editor
                or DatasetPermission.Owner);
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
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        if (expectedVersion is null)
            return OptimisticConcurrency.MissingVersion(this);
        if (!OptimisticConcurrency.Prepare(db, report, expectedVersion.Value))
            return OptimisticConcurrency.Conflict(this, ToDto(report, null));

        bool hasActive = report.Subscriptions.Any();
        if (hasActive && !cascade)
            return Conflict(new { error = "Report has active subscriptions. Use ?cascade=true." });

        if (cascade)
            foreach (var sub in report.Subscriptions)
                sub.IsActive = false;

        var datasetNames = await db.Datasets
            .Where(d => d.OwningReportId == report.Id)
            .Select(d => d.Name)
            .ToListAsync();
        foreach (var datasetName in datasetNames)
            await datasetRegistry.Delete(datasetName);

        report.IsDeleted = true;
        audit.Stage(CurrentUserId, "DELETE_REPORT", "Report", id.ToString(), report.Name);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(report).ReloadAsync();
            return OptimisticConcurrency.Conflict(this, ToDto(report, null));
        }
        return NoContent();
    }

    // ── GET /api/reports/{id}/script-content ─────────────────────────────────

    [HttpGet("reports/{id:int}/script-content")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> GetScriptContent(int id)
    {
        var report = await db.Reports.Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        var scriptKey = ToScriptKey(report.ScriptPath);
        if (scriptKey is null)
            return Forbid();

        var text = await artifacts.ExistsAsync(ETL_SQL.Core.Storage.ArtifactArea.Scripts, scriptKey)
            ? await artifacts.ReadAllTextAsync(ETL_SQL.Core.Storage.ArtifactArea.Scripts, scriptKey)
            : string.Empty;
        OptimisticConcurrency.SetETag(Response, report.Version);
        return Ok(new ScriptContentResponse(text, report.Version, await sourceControl.GetCurrentRevisionAsync(), sourceControl.IsEnabled));
    }

    // ── PUT /api/reports/{id}/script-content ──────────────────────────────────

    [HttpPut("reports/{id:int}/script-content")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> SaveScriptContent(int id, [FromBody] ScriptContentRequest req)
    {
        var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
        var result = await scriptSave.SaveAsync(id, req.ScriptText, expectedVersion, User, CurrentUserId, req.BaseRevision);
        return result.Status switch
        {
            ReportScriptSaveStatus.Saved => SavedScriptResponse(result),
            ReportScriptSaveStatus.NotFound => NotFound(),
            ReportScriptSaveStatus.Forbidden => Forbid(),
            ReportScriptSaveStatus.MissingVersion => OptimisticConcurrency.MissingVersion(this),
            ReportScriptSaveStatus.Conflict => OptimisticConcurrency.Conflict(this, ToDto(result.Current!, null)),
            _ => StatusCode(500, new { error = "Unknown save status." })
        };
    }

    // ── POST /api/reports/{id}/script-source/commit ─────────────────────────

    [HttpPost("reports/{id:int}/script-source/commit")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> CommitScriptSource(int id, CancellationToken cancellationToken)
    {
        var report = await db.Reports.Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        var scriptKey = ToScriptKey(report.ScriptPath);
        if (scriptKey is null)
            return Forbid();

        var result = await sourceControl.CommitScriptAsync(scriptKey, User, cancellationToken);
        var detail = result.Revision is null
            ? report.Name
            : $"{report.Name}; sourceRevision={result.Revision}; committed={result.Committed}";
        audit.Stage(CurrentUserId, "COMMIT_REPORT_SCRIPT", "Report", id.ToString(), detail);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new ScriptSourceControlResponse(result.Revision, result.Committed));
    }

    private IActionResult SavedScriptResponse(ReportScriptSaveResult result)
    {
        OptimisticConcurrency.SetETag(Response, result.Version!.Value);
        return Ok(new SaveDesignerResponse(result.Version.Value, result.SourceRevision));
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

        if (string.IsNullOrWhiteSpace(portalConfig.ScriptRootPath))
            return StatusCode(503, new { error = "ScriptRootPath is not configured on the portal." });

        // Filename-only key (separators already rejected above); written through the guarded Scripts area.
        await artifacts.WriteAllBytesAsync(ETL_SQL.Core.Storage.ArtifactArea.Scripts, req.Filename, content);

        return Ok(new UploadScriptResponse(req.Filename));
    }

    // ── GET /api/reports/available-scripts ───────────────────────────────────

    [HttpGet("reports/available-scripts")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> GetAvailableScripts()
    {
        var files = new List<string>();
        await foreach (var info in artifacts.EnumerateAsync(ETL_SQL.Core.Storage.ArtifactArea.Scripts, recursive: true))
            if (info.Path.EndsWith(".rptsql", StringComparison.OrdinalIgnoreCase))
                files.Add(info.Path);
        files.Sort(StringComparer.Ordinal);
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

        // Read through the artifact-storage seam (Maps area), which enforces the path-traversal guardrail
        // (ArtifactPath.Normalize rejects '..'/absolute) so a shared/SMB-backed map root works uniformly.
        try
        {
            if (!await artifacts.ExistsAsync(ETL_SQL.Core.Storage.ArtifactArea.Maps, path))
                return NotFound(new { error = "Map file not found." });

            var json = await artifacts.ReadAllTextAsync(ETL_SQL.Core.Storage.ArtifactArea.Maps, path);
            return Content(json, "application/geo+json");
        }
        catch (ArgumentException)
        {
            // Path escaped the area root (traversal) — same denial as the previous guard.
            return Forbid();
        }
    }

}
