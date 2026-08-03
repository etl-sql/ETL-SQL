using System.Security.Claims;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public class FolderPermissionService(PortalDbContext db)
{
    private readonly Dictionary<int, ISet<int>> _cachedUserGroupIdsByUser = [];

    public int GetUserId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public bool IsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin");

    public async Task<ISet<int>> GetUserGroupIdsAsync(ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        if (_cachedUserGroupIdsByUser.TryGetValue(userId, out var cached))
            return cached;

        var ids = await db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        var groupIds = new HashSet<int>(ids);
        _cachedUserGroupIdsByUser[userId] = groupIds;
        return groupIds;
    }

    public async Task<FolderPermission?> GetEffectivePermissionAsync(int folderId, ClaimsPrincipal user)
    {
        if (IsAdmin(user)) return FolderPermission.Manage;

        var groupIds = await GetUserGroupIdsAsync(user);
        return await GetEffectivePermissionAsync(folderId, groupIds, GetUserId(user));
    }

    public async Task<bool> HasPermissionAsync(int folderId, FolderPermission required, ClaimsPrincipal user)
    {
        if (IsAdmin(user)) return true;

        var effective = await GetEffectivePermissionAsync(folderId, user);
        return effective.HasValue && effective.Value >= required;
    }

    public async Task<FolderPermission?> GetEffectiveReportPermissionAsync(Report report, ClaimsPrincipal user) =>
        await GetEffectiveReportPermissionAsync(
            report, GetUserId(user), await GetUserGroupIdsAsync(user), IsAdmin(user));

    /// <summary>
    /// Resolves a report permission for an identity given directly rather than as a principal, so a
    /// caller reasoning about <em>someone else's</em> access — the access simulator — gets its answer
    /// from this method instead of reimplementing it. A second copy of the authorship rule would
    /// drift from this one, and a diagnostic that disagrees with the enforcement it describes is
    /// worse than no diagnostic.
    /// </summary>
    public async Task<FolderPermission?> GetEffectiveReportPermissionAsync(
        Report report, int userId, ISet<int> groupIds, bool isAdmin)
    {
        if (isAdmin) return FolderPermission.Manage;

        var folderPerm = await GetEffectivePermissionAsync(report.FolderId, groupIds, userId);

        var reportPerms = await db.ReportAcls
            .Where(a => a.ReportId == report.Id && ((a.UserId.HasValue && a.UserId == userId) || (a.GroupId.HasValue && groupIds.Contains(a.GroupId.Value))))
            .Select(a => a.Permission)
            .ToListAsync();

        FolderPermission? directPerm = reportPerms.Count > 0 ? (FolderPermission)reportPerms.Max(p => (int)p) : null;

        // Report authorship upgrades an existing grant to Manage — it never substitutes for one.
        // Treating authorship as standing permission would mean revoking a user's group membership
        // (or removing them from the directory) leaves them full access to every report they ever
        // created, so deprovisioning would not actually deprovision.
        if (report.CreatedBy == userId && (folderPerm.HasValue || directPerm.HasValue))
            return FolderPermission.Manage;

        if (!folderPerm.HasValue) return directPerm;
        if (!directPerm.HasValue) return folderPerm;
        return (FolderPermission)Math.Max((int)folderPerm.Value, (int)directPerm.Value);
    }

    /// <summary>
    /// Names the grants an identity holds on a report, for explaining an answer rather than
    /// enforcing one. The permission itself always comes from
    /// <see cref="GetEffectiveReportPermissionAsync(Report, int, ISet{int}, bool)"/>.
    /// </summary>
    public async Task<IReadOnlyList<string>> DescribeReportGrantsAsync(
        Report report, int userId, ISet<int> groupIds, bool isAdmin)
    {
        var sources = new List<string>();
        if (isAdmin) sources.Add("Administrator role");

        var folderPerm = await GetEffectivePermissionAsync(report.FolderId, groupIds, userId);
        if (folderPerm is not null) sources.Add($"Folder ACL ({folderPerm})");

        var reportPerms = await db.ReportAcls
            .Where(a => a.ReportId == report.Id && ((a.UserId.HasValue && a.UserId == userId) || (a.GroupId.HasValue && groupIds.Contains(a.GroupId.Value))))
            .Select(a => a.Permission)
            .ToListAsync();
        if (reportPerms.Count > 0)
            sources.Add($"Report ACL ({(FolderPermission)reportPerms.Max(p => (int)p)})");

        if (report.CreatedBy == userId)
        {
            sources.Add(folderPerm is not null || reportPerms.Count > 0
                ? "Authorship (upgrades the grant above to Manage)"
                : "Authorship (no effect — it upgrades a grant, it does not substitute for one)");
        }

        return sources.Count == 0 ? ["No grant"] : sources;
    }

    public async Task<FolderPermission?> GetEffectivePermissionAsync(
        int folderId, ISet<int> groupIds, int? userId = null)
    {
        // Folder ownership implies Manage: the creator (or a transfer target) administers the
        // folder without an explicit ACL grant, matching OwnerId's role as the ownership
        // fallback for system-published datasets.
        if (userId is not null
            && await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == userId))
            return FolderPermission.Manage;

        var perms = await db.FolderAcls
            .Where(a => a.FolderId == folderId && groupIds.Contains(a.GroupId))
            .Select(a => a.Permission)
            .ToListAsync();

        if (!perms.Any()) return null;
        return (FolderPermission)perms.Max(p => (int)p);
    }

    /// <summary>Resolves the effective permission for many folders with a single ACL query
    /// (plus one ownership query when the caller is known).
    /// Folders with no matching grant map to null.</summary>
    public async Task<IReadOnlyDictionary<int, FolderPermission?>> GetEffectivePermissionsAsync(
        IReadOnlyCollection<int> folderIds, ISet<int> groupIds, int? userId = null)
    {
        var result = folderIds.Distinct().ToDictionary(id => id, _ => (FolderPermission?)null);
        if (result.Count == 0) return result;

        var ids = result.Keys.ToList();
        if (groupIds.Count > 0)
        {
            var rows = await db.FolderAcls
                .Where(a => ids.Contains(a.FolderId) && groupIds.Contains(a.GroupId))
                .GroupBy(a => a.FolderId)
                .Select(g => new { FolderId = g.Key, Max = g.Max(a => (int)a.Permission) })
                .ToListAsync();

            foreach (var row in rows)
                result[row.FolderId] = (FolderPermission)row.Max;
        }

        if (userId is not null)
        {
            var owned = await db.Folders
                .Where(f => ids.Contains(f.Id) && f.OwnerId == userId)
                .Select(f => f.Id)
                .ToListAsync();
            foreach (var folderId in owned)
                result[folderId] = FolderPermission.Manage;
        }

        return result;
    }
}
