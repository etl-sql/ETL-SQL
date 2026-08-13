using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/catalog")]
[Authorize]
[RequirePortalModule("Reporting")]
public class CatalogController(
    PortalDbContext db,
    PortalTenantLineageCatalog lineageCatalog,
    DatasetTenantScope tenantScope,
    PortalTenantCatalogScope catalogScope) : ControllerBase
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
        var queryTokens = q.Split([' ', '_', '-', '/', '\\', '.', ','], StringSplitOptions.RemoveEmptyEntries);

        var folders = await VisibleFoldersQuery()
            .AsNoTracking()
            .Where(f => EF.Functions.Like(f.Name, pattern, @"\")
                || EF.Functions.Like(f.Path, pattern, @"\"))
            .OrderBy(f => f.Path)
            .Take(limit)
            .ToListAsync();

        var remaining = limit - folders.Count;
        List<(Report Report, CatalogMatch Match)> reports;

        if (remaining > 0)
        {
            var likeReports = await VisibleReportsQuery()
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
                .Take(remaining * 2)
                .ToListAsync();

            var fallbackCandidates = likeReports.Count == 0
                ? await VisibleReportsQuery()
                    .AsNoTracking()
                    .Include(r => r.Folder)
                    .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
                    .OrderByDescending(r => r.UpdatedAt)
                    .Take(500)
                    .ToListAsync()
                : [];

            var allCandidates = likeReports
                .Concat(fallbackCandidates)
                .DistinctBy(r => r.Id)
                .ToList();
            var lineageTerms = await BuildReportLineageTermsAsync(allCandidates.Select(r => r.Id).ToHashSet());

            reports = allCandidates
                .Select(r => (Report: r, Match: ComputeCatalogMatch(r, queryTokens, lineageTerms.TryGetValue(r.Id, out var terms) ? terms : [])))
                .Where(x => x.Match.Score > 0.05)
                .OrderByDescending(x => x.Match.Score)
                .ThenBy(x => x.Report.Name)
                .Take(remaining)
                .ToList();
        }
        else
        {
            reports = new List<(Report Report, CatalogMatch Match)>();
        }

        var favoriteIds = await GetFavoriteReportIdsAsync();

        var results = folders
            .Select(f => new CatalogSearchResultDto(
                "Folder", f.Id, f.Name, f.Path, f.Id, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, "matched folder", 1.0))
            .Concat(reports.Select(r => ToCatalogResult(r.Report, favoriteIds, r.Match.Reason, r.Match.Score)))
            .ToList();

        return Ok(results);
    }

    [HttpGet("consumer-home")]
    public async Task<IActionResult> ConsumerHome([FromQuery] int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 30);
        var favoriteIds = await GetFavoriteReportIdsAsync();

        var favReports = await (
                from favorite in catalogScope.ReportFavorites.AsNoTracking()
                join report in VisibleReportsQuery() on favorite.ReportId equals report.Id
                where favorite.UserId == CurrentUserId
                orderby favorite.CreatedAt descending
                select report)
            .AsNoTracking()
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Take(limit)
            .ToListAsync();

        var recentReportIds = await db.AuditLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantScope.TenantId
                && log.UserId == CurrentUserId
                && log.Action == "VIEW_SNAPSHOT"
                && log.ResourceType == "Report"
                && log.ResourceId != null)
            .GroupBy(log => log.ResourceId!)
            .Select(g => new { ResourceId = g.Key, LastViewedAt = g.Max(log => log.Timestamp) })
            .OrderByDescending(x => x.LastViewedAt)
            .Take(limit * 4)
            .ToListAsync();
        var recentIds = recentReportIds
            .Select(x => int.TryParse(x.ResourceId, out var reportId) ? reportId : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var recentReports = recentIds.Count == 0
            ? []
            : await VisibleReportsQuery()
            .AsNoTracking()
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => recentIds.Contains(r.Id))
            .ToListAsync();
        var recentOrder = recentIds.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
        recentReports = recentReports
            .OrderBy(r => recentOrder.TryGetValue(r.Id, out var idx) ? idx : int.MaxValue)
            .Take(limit)
            .ToList();

        var featuredReports = await VisibleReportsQuery()
            .AsNoTracking()
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => r.Certification != null || r.Steward != null)
            .OrderByDescending(r => r.UpdatedAt)
            .Take(limit)
            .ToListAsync();

        var popularReportIds = await db.AuditLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantScope.TenantId
                && log.Action == "VIEW_SNAPSHOT"
                && log.ResourceType == "Report"
                && log.ResourceId != null)
            .GroupBy(log => log.ResourceId!)
            .Select(g => new { ResourceId = g.Key, ViewCount = g.Count(), LastViewedAt = g.Max(log => log.Timestamp) })
            .OrderByDescending(x => x.ViewCount)
            .ThenByDescending(x => x.LastViewedAt)
            .Take(limit * 4)
            .ToListAsync();
        var popularIds = popularReportIds
            .Select(x => int.TryParse(x.ResourceId, out var reportId) ? reportId : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var popularReports = popularIds.Count == 0
            ? await VisibleReportsQuery()
                .AsNoTracking()
                .Include(r => r.Folder)
                .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
                .OrderByDescending(r => r.LastRefreshCompletedAt ?? r.UpdatedAt)
                .Take(limit)
                .ToListAsync()
            : await VisibleReportsQuery()
            .AsNoTracking()
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => popularIds.Contains(r.Id))
            .ToListAsync();
        if (popularIds.Count > 0)
        {
            var popularOrder = popularIds.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
            popularReports = popularReports
                .OrderBy(r => popularOrder.TryGetValue(r.Id, out var idx) ? idx : int.MaxValue)
                .Take(limit)
                .ToList();
        }

        var favDtos = favReports.Select(r => ToCatalogResult(r, favoriteIds)).ToList();
        var recentDtos = recentReports.Select(r => ToCatalogResult(r, favoriteIds)).ToList();
        var featuredDtos = featuredReports.Select(r => ToCatalogResult(r, favoriteIds)).ToList();
        var popularDtos = popularReports.Select(r => ToCatalogResult(r, favoriteIds)).ToList();

        return Ok(new ConsumerHomeDto(favDtos, recentDtos, featuredDtos, popularDtos));
    }

    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);

        var recentReportIds = await db.AuditLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantScope.TenantId
                && log.UserId == CurrentUserId
                && log.Action == "VIEW_SNAPSHOT"
                && log.ResourceType == "Report"
                && log.ResourceId != null)
            .GroupBy(log => log.ResourceId!)
            .Select(g => new { ResourceId = g.Key, LastViewedAt = g.Max(log => log.Timestamp) })
            .OrderByDescending(x => x.LastViewedAt)
            .Take(limit * 4)
            .ToListAsync();
        var recentIds = recentReportIds
            .Select(x => int.TryParse(x.ResourceId, out var reportId) ? reportId : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var reports = recentIds.Count == 0
            ? []
            : await VisibleReportsQuery()
            .AsNoTracking()
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => recentIds.Contains(r.Id))
            .ToListAsync();
        var recentOrder = recentIds.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
        reports = reports
            .OrderBy(r => recentOrder.TryGetValue(r.Id, out var idx) ? idx : int.MaxValue)
            .ThenBy(r => r.Name)
            .Take(limit)
            .ToList();
        var favoriteIds = await GetFavoriteReportIdsAsync();

        return Ok(reports.Select(r => ToCatalogResult(r, favoriteIds)).ToList());
    }

    [HttpGet("favorites")]
    public async Task<IActionResult> Favorites([FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        var userId = CurrentUserId;

        var reports = await (
                from favorite in catalogScope.ReportFavorites.AsNoTracking()
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

    [HttpGet("stewardship")]
    public async Task<IActionResult> Stewardship(
        [FromQuery] string view = "all",
        [FromQuery] string? q = null,
        [FromQuery] string? steward = null,
        [FromQuery] string? domain = null,
        [FromQuery] int staleAfterDays = 30,
        [FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        staleAfterDays = Math.Clamp(staleAfterDays, 1, 3660);
        view = ETL_SQL.Portal.Services.StewardshipProjection.NormalizeView(view);

        var scanLimit = Math.Max(limit * 20, 1000);
        var latestTargets = (await lineageCatalog.GetRecentLineageAsync(scanLimit))
            .GroupBy(
                e => $"{e.TargetTable}\u001f{e.TargetColumn ?? string.Empty}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var allItems = latestTargets
            .Select(e => ETL_SQL.Portal.Services.StewardshipProjection.ToAsset(e, staleAfterDays))
            .ToList();

        var normalizedQuery = q?.Trim();
        var normalizedSteward = steward?.Trim();
        var normalizedDomain = domain?.Trim();

        var filtered = allItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
            filtered = filtered.Where(i => ETL_SQL.Portal.Services.StewardshipProjection.MatchesQuery(i, normalizedQuery));
        if (!string.IsNullOrWhiteSpace(normalizedSteward))
            filtered = filtered.Where(i => string.Equals(i.Steward, normalizedSteward, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(normalizedDomain))
            filtered = filtered.Where(i => string.Equals(i.Domain, normalizedDomain, StringComparison.OrdinalIgnoreCase));

        filtered = view switch
        {
            "sensitive" => filtered.Where(i => i.IsSensitive || i.IsRestricted),
            "missing" => filtered.Where(i => i.MissingTags.Count > 0),
            "stale" => filtered.Where(i => i.IsStale),
            "queue" => filtered.Where(i => !string.IsNullOrWhiteSpace(i.Steward) && (i.MissingTags.Count > 0 || i.IsStale || i.IsSensitive || i.IsRestricted)),
            _ => filtered
        };

        var queueScope = allItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(normalizedSteward))
            queueScope = queueScope.Where(i => string.Equals(i.Steward, normalizedSteward, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(normalizedDomain))
            queueScope = queueScope.Where(i => string.Equals(i.Domain, normalizedDomain, StringComparison.OrdinalIgnoreCase));

        var result = new StewardshipCatalogDto(
            new StewardshipSummaryDto(
                allItems.Count,
                allItems.Count(i => i.IsSensitive || i.IsRestricted),
                allItems.Count(i => i.MissingTags.Count > 0),
                allItems.Count(i => i.IsStale),
                queueScope.Count(i => !string.IsNullOrWhiteSpace(i.Steward) && (i.MissingTags.Count > 0 || i.IsStale || i.IsSensitive || i.IsRestricted))),
            BuildFacet(allItems.Select(i => i.Steward)),
            BuildFacet(allItems.Select(i => i.Domain)),
            BuildFacet(allItems.Select(i => i.Classification)),
            BuildFacet(allItems.Select(i => i.Quality)),
            filtered
                .OrderByDescending(i => i.IsRestricted)
                .ThenByDescending(i => i.IsSensitive)
                .ThenByDescending(i => i.MissingTags.Count)
                .ThenByDescending(i => i.IsStale)
                .ThenByDescending(i => i.RunAt)
                .Take(limit)
                .ToList());

        return Ok(result);
    }

    [HttpGet("protected-data")]
    public async Task<IActionResult> ProtectedData([FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetRecentLineageAsync(Math.Max(limit * 20, 1000));
        return Ok(LineageProtectedData.FromHistory(entries).Take(limit).ToList());
    }

    [HttpGet("protected-data/suggestions")]
    public async Task<IActionResult> ProtectedDataSuggestions([FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        var entries = await lineageCatalog.GetRecentLineageAsync(Math.Max(limit * 20, 1000));
        return Ok(LineageProtectedData.SuggestFromHistory(entries).Take(limit).ToList());
    }

    [HttpGet("impact")]
    public async Task<IActionResult> Impact(
        [FromServices] LineageImpactService impact,
        [FromQuery] string kind,
        [FromQuery] string name,
        [FromQuery] string? column = null,
        [FromQuery] string direction = "downstream",
        [FromQuery] int depth = 4,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            return BadRequest(new { error = "Impact target name is required." });

        return Ok(await impact.AnalyzeAsync(
            kind,
            name,
            column,
            direction,
            depth,
            limit,
            IsAdmin,
            CurrentUserId,
            cancellationToken));
    }

    private IQueryable<Folder> VisibleFoldersQuery()
    {
        if (IsAdmin)
            return catalogScope.Folders;

        var userId = CurrentUserId;
        return catalogScope.Folders.Where(f => catalogScope.FolderAcls.Any(a =>
            a.FolderId == f.Id
            && a.Permission >= FolderPermission.Read
            && db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)));
    }

    private IQueryable<Report> VisibleReportsQuery()
    {
        if (IsAdmin)
            return catalogScope.Reports.Where(r => !r.IsDeleted);

        var userId = CurrentUserId;
        // Mirrors FolderPermissionService.GetEffectiveReportPermissionAsync: authorship is not a
        // visibility grant on its own, otherwise a user removed from every group would keep seeing
        // the reports they authored in search, favourites, and recents while being unable to open
        // them. A creator who still holds folder ownership or any ACL is covered by the clauses
        // below.
        return catalogScope.Reports.Where(r => !r.IsDeleted && (
            catalogScope.Folders.Any(f => f.Id == r.FolderId && f.OwnerId == userId)
            || catalogScope.FolderAcls.Any(a =>
                a.FolderId == r.FolderId
                && a.Permission >= FolderPermission.Read
                && db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId))
            || catalogScope.ReportAcls.Any(a =>
                a.ReportId == r.Id
                && a.Permission >= FolderPermission.Read
                && ((a.UserId.HasValue && a.UserId == userId)
                    || (a.GroupId.HasValue && db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId))))));
    }

    private static string LikePattern(string query) =>
        $"%{query.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%";

    private static string CombinePath(string folderPath, string reportName) =>
        folderPath.EndsWith('/') ? folderPath + reportName : $"{folderPath}/{reportName}";

    private async Task<HashSet<int>> GetFavoriteReportIdsAsync() =>
        await catalogScope.ReportFavorites
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

    private static IReadOnlyList<StewardshipFacetDto> BuildFacet(IEnumerable<string?> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StewardshipFacetDto(g.Key, g.Count()))
            .OrderBy(f => f.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool HasTruthyTag(IReadOnlyDictionary<string, string> tags, string key) =>
        tags.TryGetValue(key, out var value) && value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool HasTagValue(IReadOnlyDictionary<string, string> tags, string key, string expected) =>
        tags.TryGetValue(key, out var value) && value.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string? GetTag(IReadOnlyDictionary<string, string> tags, string key) =>
        tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

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

    private static CatalogSearchResultDto ToCatalogResult(Report r, ISet<int> favoriteIds, string? matchReason = null, double? score = null)
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
            favoriteIds.Contains(r.Id),
            matchReason,
            score);
    }

    private async Task<Dictionary<int, IReadOnlyList<string>>> BuildReportLineageTermsAsync(ISet<int> reportIds)
    {
        if (reportIds.Count == 0)
            return [];

        var entries = await lineageCatalog.GetRecentLineageAsync(Math.Max(1000, reportIds.Count * 20));
        return entries
            .Select(e => new { ReportId = TryParseReportId(e.JobName), Entry = e })
            .Where(x => x.ReportId.HasValue && reportIds.Contains(x.ReportId.Value))
            .GroupBy(x => x.ReportId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.SelectMany(x => new[]
                    {
                        x.Entry.TargetTable,
                        x.Entry.TargetColumn,
                        x.Entry.TransformationKind,
                        x.Entry.TransformationExpression,
                        x.Entry.DerivedFromDescriptions
                    }
                    .Concat(x.Entry.SourceTables)
                    .Concat(x.Entry.SourceColumns ?? [])
                    .Concat(x.Entry.FunctionsApplied ?? [])
                    .Concat(x.Entry.Tags.SelectMany(tag => new[] { tag.Key, tag.Value })))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private sealed record CatalogMatch(double Score, string Reason);

    private static CatalogMatch ComputeCatalogMatch(Report r, string[] tokens, IReadOnlyList<string> lineageTerms)
    {
        if (tokens.Length == 0) return new CatalogMatch(0, "no match");

        var weightedTargets = new (string? Text, double Weight, string Reason)[]
        {
            (r.Name, 1.00, "matched title"),
            (r.Description, 0.78, "matched description"),
            (r.Tags, 0.74, "matched tag"),
            (r.Category, 0.68, "matched category"),
            (r.Owner, 0.64, "matched owner"),
            (r.Contact, 0.62, "matched contact"),
            (r.Domain, 0.60, "matched domain"),
            (r.Steward, 0.60, "matched steward"),
            (r.Certification, 0.58, "matched certification"),
            (r.Folder?.Path, 0.45, "matched folder")
        }.Concat(lineageTerms.Select(term => ((string?)term, 0.52, "matched metric or column"))).ToArray();

        double totalScore = 0;
        string bestReason = "matched report";
        foreach (var token in tokens)
        {
            double bestTokenScore = 0;
            string? tokenReason = null;
            foreach (var target in weightedTargets)
            {
                if (string.IsNullOrWhiteSpace(target.Item1)) continue;
                var words = target.Item1.Split([' ', '_', '-', '/', '\\', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                {
                    double candidate = 0;
                    if (target.Item1.Equals(token, StringComparison.OrdinalIgnoreCase))
                        candidate = 1.25;
                    else if (word.Equals(token, StringComparison.OrdinalIgnoreCase))
                        candidate = 1.0;
                    else if (word.Contains(token, StringComparison.OrdinalIgnoreCase) || token.Contains(word, StringComparison.OrdinalIgnoreCase))
                        candidate = 0.85;
                    else if (token.Length >= 3 && word.Length >= 3)
                    {
                        int dist = ComputeLevenshtein(token.ToLowerInvariant(), word.ToLowerInvariant());
                        if (dist <= 2)
                        {
                            double sim = 1.0 - ((double)dist / Math.Max(token.Length, word.Length));
                            candidate = sim * 0.75;
                        }
                    }

                    candidate *= target.Item2;
                    if (candidate > bestTokenScore)
                    {
                        bestTokenScore = candidate;
                        tokenReason = target.Item3;
                    }
                }
            }
            if (bestTokenScore > 0 && tokenReason is not null)
                bestReason = tokenReason;
            totalScore += bestTokenScore;
        }

        return new CatalogMatch(totalScore / tokens.Length, bestReason);
    }

    private static int ComputeLevenshtein(string a, string b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        if (n > m)
        {
            (a, b) = (b, a);
            (n, m) = (m, n);
        }

        Span<int> v0 = n + 1 <= 256 ? stackalloc int[n + 1] : new int[n + 1];
        Span<int> v1 = n + 1 <= 256 ? stackalloc int[n + 1] : new int[n + 1];

        for (int i = 0; i <= n; i++) v0[i] = i;

        for (int j = 1; j <= m; j++)
        {
            v1[0] = j;
            for (int i = 1; i <= n; i++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                v1[i] = Math.Min(Math.Min(v1[i - 1] + 1, v0[i] + 1), v0[i - 1] + cost);
            }
            v1.CopyTo(v0);
        }

        return v0[n];
    }
}
