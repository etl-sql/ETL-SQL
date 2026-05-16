using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/catalog")]
[Authorize]
public class CatalogController(PortalDbContext db) : ControllerBase
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

        var results = folders
            .Where(f => Matches(q, f.Name, f.Path))
            .Select(f => new CatalogSearchResultDto(
                "Folder", f.Id, f.Name, f.Path, f.Id, null, null, null, null, null,
                null, null, null, null, null))
            .Concat(reports
                .Where(r => Matches(q, r.Name, r.Description, r.Folder.Path, r.Tags, r.Category, r.Owner, r.Contact, r.Domain, r.Steward, r.Certification))
                .Select(r => new CatalogSearchResultDto(
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
                    r.Snapshots.OrderByDescending(s => s.BuiltAt).FirstOrDefault()?.BuiltAt,
                    r.LastViewedAt,
                    r.LastRefreshStatus,
                    r.LastRefreshError,
                    r.LastRefreshDurationMs)))
            .Take(limit)
            .ToList();

        return Ok(results);
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
}
