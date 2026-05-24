using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/catalog")]
[Authorize]
public class CatalogController(PortalDbContext db, ILineageCatalogStore lineageCatalog) : ControllerBase
{
    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 50)
    {
        q = q?.Trim() ?? "";
        if (q.Length == 0)
            return BadRequest(new { error = "Search query is required." });

        limit = Math.Clamp(limit, 1, 100);
        var visibleFolderIds = await GetVisibleFolderIdsAsync();

        var folders = await db.Folders
            .Where(f => visibleFolderIds.Contains(f.Id))
            .OrderBy(f => f.Path)
            .ToListAsync();

        var reports = await db.Reports
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => !r.IsDeleted && visibleFolderIds.Contains(r.FolderId))
            .OrderBy(r => r.Folder.Path)
            .ThenBy(r => r.Name)
            .ToListAsync();
        var favoriteIds = await GetFavoriteReportIdsAsync();

        var results = folders
            .Where(f => Matches(q, f.Name, f.Path))
            .Select(f => new CatalogSearchResultDto(
                "Folder", f.Id, f.Name, f.Path, f.Id, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null))
            .Concat(reports
                .Where(r => Matches(q, r.Name, r.Description, r.Folder.Path, r.Tags, r.Category, r.Owner, r.Contact, r.Domain, r.Steward, r.Certification))
                .Select(r => ToCatalogResult(r, favoriteIds)))
            .Take(limit)
            .ToList();

        return Ok(results);
    }

    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        var visibleFolderIds = await GetVisibleFolderIdsAsync();

        var reports = await db.Reports
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => !r.IsDeleted && r.LastViewedAt != null && visibleFolderIds.Contains(r.FolderId))
            .OrderByDescending(r => r.LastViewedAt)
            .ThenBy(r => r.Name)
            .Take(limit)
            .ToListAsync();
        var favoriteIds = await GetFavoriteReportIdsAsync();

        return Ok(reports.Select(r => ToCatalogResult(r, favoriteIds)).ToList());
    }

    [HttpGet("favorites")]
    public async Task<IActionResult> Favorites([FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        var visibleFolderIds = await GetVisibleFolderIdsAsync();

        var reports = await db.ReportFavorites
            .Include(f => f.Report).ThenInclude(r => r.Folder)
            .Include(f => f.Report).ThenInclude(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(f => f.UserId == CurrentUserId && !f.Report.IsDeleted && visibleFolderIds.Contains(f.Report.FolderId))
            .OrderByDescending(f => f.CreatedAt)
            .Take(limit)
            .Select(f => f.Report)
            .ToListAsync();

        var favoriteIds = reports.Select(r => r.Id).ToHashSet();
        return Ok(reports.Select(r => ToCatalogResult(r, favoriteIds)).ToList());
    }

    [HttpGet("lineage/table")]
    public async Task<IActionResult> LineageForTable([FromQuery] string name, [FromQuery] int limit = 100)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            return BadRequest(new { error = "Table name is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForTableAsync(name, limit);
        return Ok(await ToLineageDtosAsync(entries));
    }

    [HttpGet("lineage/source")]
    public async Task<IActionResult> LineageForSource([FromQuery] string name, [FromQuery] int limit = 100)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            return BadRequest(new { error = "Source name is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForSourceAsync(name, limit);
        return Ok(await ToLineageDtosAsync(entries));
    }

    [HttpGet("lineage/source-file")]
    public async Task<IActionResult> LineageForSourceFile([FromQuery] string path, [FromQuery] int limit = 100)
    {
        path = path?.Trim() ?? "";
        if (path.Length == 0)
            return BadRequest(new { error = "Source file path is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForSourceFileAsync(path, limit);
        return Ok(await ToLineageDtosAsync(entries));
    }

    [HttpGet("lineage/tag")]
    public async Task<IActionResult> LineageForTag([FromQuery] string key, [FromQuery] string? value = null, [FromQuery] int limit = 100)
    {
        key = key?.Trim() ?? "";
        if (key.Length == 0)
            return BadRequest(new { error = "Tag key is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForTagAsync(key, string.IsNullOrWhiteSpace(value) ? null : value, limit);
        return Ok(await ToLineageDtosAsync(entries));
    }

    [HttpGet("lineage/job")]
    public async Task<IActionResult> LineageForJob([FromQuery] string name, [FromQuery] int limit = 100)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            return BadRequest(new { error = "Job name is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForJobAsync(name, limit);
        return Ok(await ToLineageDtosAsync(entries));
    }

    private async Task<HashSet<int>> GetVisibleFolderIdsAsync()
    {
        if (IsAdmin)
            return await db.Folders.Select(f => f.Id).ToHashSetAsync();

        var groupIds = await db.UserGroups
            .Where(ug => ug.UserId == CurrentUserId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        return await db.FolderAcls
            .Where(a => groupIds.Contains(a.GroupId) && a.Permission >= FolderPermission.Read)
            .Select(a => a.FolderId)
            .ToHashSetAsync();
    }

    private static bool Matches(string query, params string?[] values) =>
        values.Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

    private static string CombinePath(string folderPath, string reportName) =>
        folderPath.EndsWith('/') ? folderPath + reportName : $"{folderPath}/{reportName}";

    private async Task<HashSet<int>> GetFavoriteReportIdsAsync() =>
        await db.ReportFavorites
            .Where(f => f.UserId == CurrentUserId)
            .Select(f => f.ReportId)
            .ToHashSetAsync();

    private async Task<IReadOnlyList<CatalogLineageHistoryDto>> ToLineageDtosAsync(IEnumerable<LineageHistoryEntry> entries)
    {
        var entryList = entries.ToList();
        var reportIds = entryList
            .Select(e => TryParseReportId(e.JobName))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var visibleFolderIds = await GetVisibleFolderIdsAsync();
        var reports = reportIds.Count == 0
            ? new Dictionary<int, Report>()
            : await db.Reports
                .Include(r => r.Folder)
                .Where(r => !r.IsDeleted && reportIds.Contains(r.Id) && visibleFolderIds.Contains(r.FolderId))
                .ToDictionaryAsync(r => r.Id);

        return entryList
            .Select(e =>
            {
                var reportId = TryParseReportId(e.JobName);
                reports.TryGetValue(reportId ?? -1, out var report);
                return new CatalogLineageHistoryDto(
                    e.Id,
                    e.RunAt,
                    e.JobName,
                    e.ScriptPath,
                    e.TargetTable,
                    e.TargetColumn,
                    e.SourceTables,
                    e.Operation,
                    e.Tags,
                    e.SourceFile,
                    e.Line,
                    report?.Id,
                    report?.Name,
                    report?.Folder.Path);
            })
            .ToList();
    }

    private static int? TryParseReportId(string? jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName) || !jobName.StartsWith("report:", StringComparison.OrdinalIgnoreCase))
            return null;

        var nextColon = jobName.IndexOf(':', "report:".Length);
        var idText = nextColon < 0
            ? jobName["report:".Length..]
            : jobName["report:".Length..nextColon];
        return int.TryParse(idText, out var id) ? id : null;
    }

    private static CatalogSearchResultDto ToCatalogResult(Report r, ISet<int> favoriteIds)
    {
        var snapshot = r.Snapshots.OrderByDescending(s => s.BuiltAt).FirstOrDefault();
        var isStale = snapshot is not null
            && System.IO.File.Exists(r.ScriptPath)
            && System.IO.File.GetLastWriteTimeUtc(r.ScriptPath) > snapshot.BuiltAt;

        var scriptChanged = false;
        if (!string.IsNullOrWhiteSpace(r.PublishedScriptHash) && System.IO.File.Exists(r.ScriptPath))
        {
            var currentHash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(System.IO.File.ReadAllBytes(r.ScriptPath))).ToLowerInvariant();
            scriptChanged = !string.Equals(currentHash, r.PublishedScriptHash, StringComparison.OrdinalIgnoreCase);
        }

        return new CatalogSearchResultDto(
            "Report",
            r.Id,
            r.Name,
            CombinePath(r.Folder.Path, r.Name),
            r.FolderId,
            r.Description,
            r.Tags,
            r.Category,
            r.Owner,
            r.Certification,
            snapshot?.BuiltAt,
            r.LastViewedAt,
            r.LastRefreshStatus,
            r.LastRefreshError,
            r.LastRefreshDurationMs,
            snapshot is not null,
            isStale,
            scriptChanged,
            favoriteIds.Contains(r.Id));
    }
}
