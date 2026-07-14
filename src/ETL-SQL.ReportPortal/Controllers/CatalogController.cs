using System.Security.Claims;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Filters;
using ETL_SQL.ReportPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/catalog")]
[Authorize]
[RequirePortalModule("Reporting")]
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
        var pattern = LikePattern(q);

        var folders = await VisibleFoldersQuery()
            .AsNoTracking()
            .Where(f => EF.Functions.Like(f.Name, pattern, @"\")
                || EF.Functions.Like(f.Path, pattern, @"\"))
            .OrderBy(f => f.Path)
            .Take(limit)
            .ToListAsync();

        var remaining = limit - folders.Count;
        var reports = remaining <= 0
            ? new List<Report>()
            : await VisibleReportsQuery()
                .AsNoTracking()
                .Include(r => r.Folder)
                .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
                .Where(r => EF.Functions.Like(r.Name, pattern, @"\")
                    || (r.Description != null && EF.Functions.Like(r.Description, pattern, @"\"))
                    || EF.Functions.Like(r.Folder.Path, pattern, @"\")
                    || (r.Tags != null && EF.Functions.Like(r.Tags, pattern, @"\"))
                    || (r.Category != null && EF.Functions.Like(r.Category, pattern, @"\"))
                    || (r.Owner != null && EF.Functions.Like(r.Owner, pattern, @"\"))
                    || (r.Contact != null && EF.Functions.Like(r.Contact, pattern, @"\"))
                    || (r.Domain != null && EF.Functions.Like(r.Domain, pattern, @"\"))
                    || (r.Steward != null && EF.Functions.Like(r.Steward, pattern, @"\"))
                    || (r.Certification != null && EF.Functions.Like(r.Certification, pattern, @"\")))
                .OrderBy(r => r.Folder.Path)
                .ThenBy(r => r.Name)
                .Take(remaining)
                .ToListAsync();
        var favoriteIds = await GetFavoriteReportIdsAsync();

        var results = folders
            .Select(f => new CatalogSearchResultDto(
                "Folder", f.Id, f.Name, f.Path, f.Id, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null))
            .Concat(reports
                .Select(r => ToCatalogResult(r, favoriteIds)))
            .ToList();

        return Ok(results);
    }

    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);

        var reports = await VisibleReportsQuery()
            .AsNoTracking()
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => r.LastViewedAt != null)
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
        var userId = CurrentUserId;

        var reports = await (
                from favorite in db.ReportFavorites.AsNoTracking()
                join report in VisibleReportsQuery() on favorite.ReportId equals report.Id
                where favorite.UserId == userId
                orderby favorite.CreatedAt descending
                select report)
            .AsNoTracking()
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Take(limit)
            .ToListAsync();

        var favoriteIds = reports.Select(r => r.Id).ToHashSet();
        return Ok(reports.Select(r => ToCatalogResult(r, favoriteIds)).ToList());
    }

    [HttpGet("lineage/table")]
    public async Task<IActionResult> LineageForTable(
        [FromQuery] string name,
        [FromQuery] int limit = 100,
        [FromQuery] string? column = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            return BadRequest(new { error = "Table name is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForTableAsync(name, LineageScanLimit(limit, from, to));
        entries = FilterColumn(entries, column);
        return Ok(await ToLineageDtosAsync(FilterRunWindow(entries, from, to).Take(limit)));
    }

    [HttpGet("lineage/source")]
    public async Task<IActionResult> LineageForSource([FromQuery] string name, [FromQuery] int limit = 100, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            return BadRequest(new { error = "Source name is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForSourceAsync(name, LineageScanLimit(limit, from, to));
        return Ok(await ToLineageDtosAsync(FilterRunWindow(entries, from, to).Take(limit)));
    }

    [HttpGet("lineage/downstream")]
    public async Task<IActionResult> LineageDownstream([FromQuery] string table, [FromQuery] int limit = 50)
    {
        table = table?.Trim() ?? "";
        if (table.Length == 0)
            return BadRequest(new { error = "Table name is required." });

        limit = Math.Clamp(limit, 1, 200);
        var entries = await lineageCatalog.GetHistoryForSourceAsync(table, limit * 20);
        var reportIds = entries
            .Select(e => TryParseReportId(e.JobName))
            .Where(id => id.HasValue).Select(id => id!.Value)
            .ToHashSet();

        var reports = reportIds.Count == 0
            ? new Dictionary<int, Report>()
            : await VisibleReportsQuery()
                .AsNoTracking()
                .Include(r => r.Folder)
                .Where(r => reportIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id);

        var result = entries
            .GroupBy(e => TryParseReportId(e.JobName))
            .Select(g =>
            {
                var reportId = g.Key;
                reports.TryGetValue(reportId ?? -1, out var report);
                return new DownstreamReportDto(
                    report?.Id,
                    report?.Name,
                    report?.Folder.Path,
                    g.Count(),
                    g.Max(e => e.RunAt));
            })
            .Where(d => d.ReportId.HasValue)
            .OrderByDescending(d => d.LastSeen)
            .Take(limit)
            .ToList();

        return Ok(result);
    }

    [HttpGet("lineage/source-file")]
    public async Task<IActionResult> LineageForSourceFile([FromQuery] string path, [FromQuery] int limit = 100, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        path = path?.Trim() ?? "";
        if (path.Length == 0)
            return BadRequest(new { error = "Source file path is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForSourceFileAsync(path, LineageScanLimit(limit, from, to));
        return Ok(await ToLineageDtosAsync(FilterRunWindow(entries, from, to).Take(limit)));
    }

    [HttpGet("lineage/tag")]
    public async Task<IActionResult> LineageForTag([FromQuery] string key, [FromQuery] string? value = null, [FromQuery] int limit = 100, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        key = key?.Trim() ?? "";
        if (key.Length == 0)
            return BadRequest(new { error = "Tag key is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForTagAsync(key, string.IsNullOrWhiteSpace(value) ? null : value, LineageScanLimit(limit, from, to));
        return Ok(await ToLineageDtosAsync(FilterRunWindow(entries, from, to).Take(limit)));
    }

    [HttpGet("lineage/job")]
    public async Task<IActionResult> LineageForJob([FromQuery] string name, [FromQuery] int limit = 100, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            return BadRequest(new { error = "Job name is required." });

        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetHistoryForJobAsync(name, LineageScanLimit(limit, from, to));
        return Ok(await ToLineageDtosAsync(FilterRunWindow(entries, from, to).Take(limit)));
    }

    private IQueryable<Folder> VisibleFoldersQuery()
    {
        if (IsAdmin)
            return db.Folders;

        var userId = CurrentUserId;
        return db.Folders.Where(f => db.FolderAcls.Any(a =>
            a.FolderId == f.Id
            && a.Permission >= FolderPermission.Read
            && db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)));
    }

    private IQueryable<Report> VisibleReportsQuery()
    {
        if (IsAdmin)
            return db.Reports.Where(r => !r.IsDeleted);

        var userId = CurrentUserId;
        return db.Reports.Where(r => !r.IsDeleted && db.FolderAcls.Any(a =>
            a.FolderId == r.FolderId
            && a.Permission >= FolderPermission.Read
            && db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)));
    }

    private static string LikePattern(string query) =>
        $"%{query.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%";

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

        var reports = reportIds.Count == 0
            ? new Dictionary<int, Report>()
            : await VisibleReportsQuery()
                .AsNoTracking()
                .Include(r => r.Folder)
                .Where(r => reportIds.Contains(r.Id))
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
                    e.SourceColumns ?? [],
                    e.TransformationKind,
                    e.TransformationExpression,
                    e.FunctionsApplied,
                    e.DerivedFromDescriptions,
                    report?.Id,
                    report?.Name,
                    report?.Folder.Path);
            })
            .ToList();
    }

    private static int LineageScanLimit(int limit, DateTime? from, DateTime? to) =>
        from.HasValue || to.HasValue ? Math.Max(limit * 10, 500) : limit;

    private static IEnumerable<LineageHistoryEntry> FilterRunWindow(IEnumerable<LineageHistoryEntry> entries, DateTime? from, DateTime? to)
    {
        if (from.HasValue)
            entries = entries.Where(e => e.RunAt >= from.Value);
        if (to.HasValue)
            entries = entries.Where(e => e.RunAt <= to.Value);
        return entries;
    }

    private static IEnumerable<LineageHistoryEntry> FilterColumn(IEnumerable<LineageHistoryEntry> entries, string? column)
    {
        column = column?.Trim();
        return string.IsNullOrWhiteSpace(column)
            ? entries
            : entries.Where(e => string.Equals(e.TargetColumn, column, StringComparison.OrdinalIgnoreCase));
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
        var isStale = snapshot is not null && r.ScriptLastModified > snapshot.BuiltAt;
        var scriptChanged = false;

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
