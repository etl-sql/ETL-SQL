using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using ETL_SQL.Reporting;

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

    private ReportDto ToDto(Report r, ReportSnapshot? snap, bool isFavorite = false)
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
            scriptChanged);
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

        var validation = await ValidateResolvedScriptAsync(resolved);
        if (!validation.IsValid)
            return BadRequest(validation);

        var scriptMetadata = new Dictionary<string, string>(validation.Metadata, StringComparer.OrdinalIgnoreCase);

        var report = new Report
        {
            FolderId            = req.FolderId,
            Name                = req.Name,
            Description         = FirstNonBlank(req.Description, GetMetadata(scriptMetadata, "description", "d")),
            Owner               = FirstNonBlank(req.Owner, GetMetadata(scriptMetadata, "owner")),
            Contact             = FirstNonBlank(req.Contact, GetMetadata(scriptMetadata, "contact")),
            Tags                = FirstNonBlank(req.Tags, GetMetadata(scriptMetadata, "tags")),
            Category            = FirstNonBlank(req.Category, GetMetadata(scriptMetadata, "category")),
            Domain              = FirstNonBlank(req.Domain, GetMetadata(scriptMetadata, "domain")),
            Steward             = FirstNonBlank(req.Steward, GetMetadata(scriptMetadata, "steward")),
            Certification       = FirstNonBlank(req.Certification, GetMetadata(scriptMetadata, "certification", "trusted")),
            MetadataJson        = SerializeMetadata(scriptMetadata),
            ScriptPath          = resolved,
            ScriptLastModified  = validation.LastModified ?? DateTime.UtcNow,
            PublishedScriptHash = validation.Hash,
            CreatedBy           = CurrentUserId,
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "PUBLISH_REPORT", "Report", report.Id.ToString(), report.Name);

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

        var validation = await ValidateResolvedScriptAsync(resolved);
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

        var manifestDatasets = await ReadManifestDatasetsAsync(snapshot);

        var registeredDatasets = await db.Datasets
            .Where(d => d.OwningReportId == id)
            .OrderBy(d => d.FolderPath)
            .ThenBy(d => d.Name)
            .ToListAsync();

        var datasetDtos = registeredDatasets
            .Select(d => new ReportDependencyDatasetDto(
                d.Id,
                d.Name,
                d.FolderPath,
                d.AccessLevel.ToString(),
                d.RowCount,
                d.LastRefresh,
                d.RefreshInterval,
                BuildSourceDtos(ParseSourceTables(d.SourceQuery), "DatasetSource")))
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
        foreach (var source in await ReadScriptSourceTablesAsync(report.ScriptPath))
            sourceNames.Add(source);
        foreach (var source in registeredDatasets.SelectMany(d => ParseSourceTables(d.SourceQuery)))
            sourceNames.Add(source);

        var dto = new ReportDependencyDto(
            new ReportDependencyReportDto(report.Id, report.Name, report.Folder?.Path ?? "", report.ScriptPath),
            snapshot is null ? null : new ReportDependencySnapshotDto(snapshot.Id, snapshot.ManifestPath, snapshot.BuiltAt),
            manifestDatasets,
            datasetDtos,
            jobs,
            BuildSourceDtos(sourceNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), "ScriptSource"));

        return Ok(dto);
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

        var currentHash = await ReadCurrentScriptHashAsync(report.ScriptPath);
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

        if (req.Name is not null)        report.Name        = req.Name;
        if (req.Description is not null) report.Description = req.Description;
        if (req.Owner is not null)         report.Owner         = req.Owner;
        if (req.Contact is not null)       report.Contact       = req.Contact;
        if (req.Tags is not null)          report.Tags          = req.Tags;
        if (req.Category is not null)      report.Category      = req.Category;
        if (req.Domain is not null)        report.Domain        = req.Domain;
        if (req.Steward is not null)       report.Steward       = req.Steward;
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

            var validation = await ValidateResolvedScriptAsync(resolved);
            if (!validation.IsValid)
                return BadRequest(validation);

            report.ScriptPath = resolved;
            report.PublishedScriptHash = validation.Hash;
            report.ScriptLastModified  = validation.LastModified ?? DateTime.UtcNow;
            var scriptMetadata = new Dictionary<string, string>(validation.Metadata, StringComparer.OrdinalIgnoreCase);
            report.MetadataJson = SerializeMetadata(scriptMetadata);
            report.Description   = FirstNonBlank(req.Description, GetMetadata(scriptMetadata, "description", "d"), report.Description);
            report.Owner         = FirstNonBlank(req.Owner, GetMetadata(scriptMetadata, "owner"), report.Owner);
            report.Contact       = FirstNonBlank(req.Contact, GetMetadata(scriptMetadata, "contact"), report.Contact);
            report.Tags          = FirstNonBlank(req.Tags, GetMetadata(scriptMetadata, "tags"), report.Tags);
            report.Category      = FirstNonBlank(req.Category, GetMetadata(scriptMetadata, "category"), report.Category);
            report.Domain        = FirstNonBlank(req.Domain, GetMetadata(scriptMetadata, "domain"), report.Domain);
            report.Steward       = FirstNonBlank(req.Steward, GetMetadata(scriptMetadata, "steward"), report.Steward);
            report.Certification = FirstNonBlank(req.Certification, GetMetadata(scriptMetadata, "certification", "trusted"), report.Certification);
        }

        report.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_REPORT", "Report", id.ToString());

        var isFavorite = await db.ReportFavorites.AnyAsync(f => f.UserId == CurrentUserId && f.ReportId == report.Id);
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
            await db.SaveChangesAsync();
            await audit.LogAsync(CurrentUserId, "FAVORITE_REPORT", "Report", id.ToString(), report.Name);
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
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UNFAVORITE_REPORT", "Report", id.ToString());
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

        if (req?.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return BadRequest(new { error = "Share link expiration must be in the future." });

        var link = new ReportShareLink
        {
            ReportId = id,
            CreatedBy = CurrentUserId,
            Token = await GenerateUniqueShareTokenAsync(),
            ExpiresAt = req?.ExpiresAt
        };
        db.ReportShareLinks.Add(link);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_REPORT_SHARE_LINK", "Report", id.ToString(), report.Name);

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
            await db.SaveChangesAsync();
            await audit.LogAsync(CurrentUserId, "REVOKE_REPORT_SHARE_LINK", "Report", id.ToString(), token);
        }

        return NoContent();
    }

    // ── GET /api/share/{token} ──────────────────────────────────────────────

    [HttpGet("share/{token}")]
    public async Task<IActionResult> ResolveShareLink(string token)
    {
        var link = await db.ReportShareLinks
            .Include(l => l.Report).ThenInclude(r => r.Folder)
            .FirstOrDefaultAsync(l => l.Token == token);
        if (link is null || link.Report.IsDeleted) return NotFound();
        if (link.RevokedAt is not null) return NotFound();
        if (link.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow) return NotFound();

        var perm = await GetEffectivePermissionAsync(link.Report.FolderId);
        if (perm is null) return Forbid();

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
        if (req?.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return BadRequest(new { error = "Embed token expiration must be in the future." });

        var token = new ReportEmbedToken
        {
            ReportId = id,
            CreatedBy = CurrentUserId,
            Name = string.IsNullOrWhiteSpace(req?.Name) ? "Embed token" : req!.Name!,
            Token = await GenerateUniqueEmbedTokenAsync(),
            ExpiresAt = req?.ExpiresAt
        };
        db.ReportEmbedTokens.Add(token);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_REPORT_EMBED_TOKEN", "Report", id.ToString(), report.Name);
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
            await db.SaveChangesAsync();
            await audit.LogAsync(CurrentUserId, "REVOKE_REPORT_EMBED_TOKEN", "Report", id.ToString(), token);
        }
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("embed/{token}")]
    public async Task<IActionResult> ResolveEmbedToken(string token)
    {
        var embed = await db.ReportEmbedTokens.Include(t => t.Report).ThenInclude(r => r.Folder).FirstOrDefaultAsync(t => t.Token == token);
        if (embed is null || embed.Report.IsDeleted) return NotFound();
        if (embed.RevokedAt is not null) return NotFound();
        if (embed.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow) return NotFound();
        return Ok(new ReportShareResolutionDto(embed.ReportId, embed.Report.Name, embed.Report.Folder.Path, $"/reports/{embed.ReportId}", embed.ExpiresAt));
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
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_SAVED_REPORT_VIEW", "Report", id.ToString(), req.Name);
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
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_SAVED_REPORT_VIEW", "Report", id.ToString(), view.Name);
        return Ok(ToSavedViewDto(view));
    }

    [HttpDelete("reports/{id:int}/saved-views/{viewId:int}")]
    public async Task<IActionResult> DeleteSavedView(int id, int viewId)
    {
        var view = await db.SavedReportViews.FirstOrDefaultAsync(v => v.Id == viewId && v.ReportId == id && v.UserId == CurrentUserId);
        if (view is null) return NoContent();
        db.SavedReportViews.Remove(view);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_SAVED_REPORT_VIEW", "Report", id.ToString(), view.Name);
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
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_REPORT_ALERT", "Report", id.ToString(), req.Name);
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
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_REPORT_ALERT", "Report", id.ToString(), alert.Name);
        return Ok(ToAlertDto(alert));
    }

    [HttpDelete("reports/{id:int}/alerts/{alertId:int}")]
    public async Task<IActionResult> DeleteAlert(int id, int alertId)
    {
        var alert = await db.ReportAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.ReportId == id);
        if (alert is null) return NoContent();
        if (!IsAdmin && alert.OwnerId != CurrentUserId) return Forbid();
        db.ReportAlerts.Remove(alert);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_REPORT_ALERT", "Report", id.ToString(), alert.Name);
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

    private static async Task<Dictionary<string, string>> ReadScriptMetadataAsync(string scriptPath)
    {
        var scriptText = await System.IO.File.ReadAllTextAsync(scriptPath);
        var tokens = new Lexer(scriptText).Tokenize();
        var script = new CoreParser(tokens, scriptText).Parse();
        return new Dictionary<string, string>(script.Metadata, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<ReportScriptValidationDto> ValidateResolvedScriptAsync(string resolvedScriptPath)
    {
        if (!System.IO.File.Exists(resolvedScriptPath))
        {
            return new ReportScriptValidationDto(
                false,
                resolvedScriptPath,
                null,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<ReportParameterDto>(),
                ["Script file not found."]);
        }

        if (!resolvedScriptPath.EndsWith(".rptsql", StringComparison.OrdinalIgnoreCase))
        {
            return new ReportScriptValidationDto(
                false,
                resolvedScriptPath,
                null,
                System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<ReportParameterDto>(),
                ["Only .rptsql files may be published as reports."]);
        }

        var scriptText = await System.IO.File.ReadAllTextAsync(resolvedScriptPath);
        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();
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
            var hash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(await System.IO.File.ReadAllBytesAsync(resolvedScriptPath))).ToLowerInvariant();

            return new ReportScriptValidationDto(
                true,
                resolvedScriptPath,
                hash,
                System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath),
                new Dictionary<string, string>(script.Metadata, StringComparer.OrdinalIgnoreCase),
                parameters,
                Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new ReportScriptValidationDto(
                false,
                resolvedScriptPath,
                null,
                System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<ReportParameterDto>(),
                [ex.Message]);
        }
    }

    private async Task<IReadOnlyList<ReportDependencyManifestDatasetDto>> ReadManifestDatasetsAsync(ReportSnapshot? snapshot)
    {
        if (snapshot is null) return Array.Empty<ReportDependencyManifestDatasetDto>();
        if (!PortalPathGuard.TryResolveSnapshot(portalConfig, snapshot.ManifestPath, out var resolvedManifestPath))
            return Array.Empty<ReportDependencyManifestDatasetDto>();
        if (!System.IO.File.Exists(resolvedManifestPath))
            return Array.Empty<ReportDependencyManifestDatasetDto>();

        try
        {
            await using var stream = System.IO.File.OpenRead(resolvedManifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<ReportManifest>(stream);
            if (manifest is null) return Array.Empty<ReportDependencyManifestDatasetDto>();

            return manifest.Datasets
                .Select(d => new ReportDependencyManifestDatasetDto(
                    d.TempTableName,
                    d.RefreshInterval,
                    d.Ttl,
                    d.LastRefresh,
                    d.RowCount))
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<ReportDependencyManifestDatasetDto>();
        }
    }

    private async Task<IReadOnlyList<string>> ReadScriptSourceTablesAsync(string scriptPath)
    {
        if (!PortalPathGuard.TryResolveScript(portalConfig, scriptPath, out var resolvedScriptPath))
            return Array.Empty<string>();
        if (!System.IO.File.Exists(resolvedScriptPath))
            return Array.Empty<string>();

        return ParseSourceTables(await System.IO.File.ReadAllTextAsync(resolvedScriptPath));
    }

    private async Task<string?> ReadCurrentScriptHashAsync(string scriptPath)
    {
        if (!PortalPathGuard.TryResolveScript(portalConfig, scriptPath, out var resolvedScriptPath))
            return null;
        if (!System.IO.File.Exists(resolvedScriptPath))
            return null;

        var bytes = await System.IO.File.ReadAllBytesAsync(resolvedScriptPath);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static IReadOnlyList<string> ParseSourceTables(string? scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText)) return Array.Empty<string>();
        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();
            return script.Statements
                .SelectMany(s => s.GetSourceTables())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<ReportDependencySourceDto> BuildSourceDtos(IEnumerable<string> sources, string kind) =>
        sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s =>
            {
                var parts = s.Split('.', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var connection = parts.Length == 2 && !parts[0].StartsWith("#", StringComparison.Ordinal) ? parts[0] : null;
                var objectName = parts.Length == 2 ? parts[1] : s;
                return new ReportDependencySourceDto(s, connection, objectName, kind);
            })
            .ToList();

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
