using System.Security.Claims;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public class FolderPermissionService(PortalDbContext db)
{
    public int GetUserId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public bool IsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin");

    public async Task<ISet<int>> GetUserGroupIdsAsync(ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var ids = await db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        return new HashSet<int>(ids);
    }

    public async Task<FolderPermission?> GetEffectivePermissionAsync(int folderId, ClaimsPrincipal user)
    {
        if (IsAdmin(user)) return FolderPermission.Manage;

        var groupIds = await GetUserGroupIdsAsync(user);
        return await GetEffectivePermissionAsync(folderId, groupIds);
    }

    public async Task<bool> HasPermissionAsync(int folderId, FolderPermission required, ClaimsPrincipal user)
    {
        if (IsAdmin(user)) return true;

        var effective = await GetEffectivePermissionAsync(folderId, user);
        return effective.HasValue && effective.Value >= required;
    }

    public async Task<FolderPermission?> GetEffectivePermissionAsync(int folderId, ISet<int> groupIds)
    {
        var perms = await db.FolderAcls
            .Where(a => a.FolderId == folderId && groupIds.Contains(a.GroupId))
            .Select(a => a.Permission)
            .ToListAsync();

        if (!perms.Any()) return null;
        return (FolderPermission)perms.Max(p => (int)p);
    }

    /// <summary>Resolves the effective permission for many folders with a single ACL query.
    /// Folders with no matching grant map to null.</summary>
    public async Task<IReadOnlyDictionary<int, FolderPermission?>> GetEffectivePermissionsAsync(
        IReadOnlyCollection<int> folderIds, ISet<int> groupIds)
    {
        var result = folderIds.Distinct().ToDictionary(id => id, _ => (FolderPermission?)null);
        if (result.Count == 0 || groupIds.Count == 0) return result;

        var ids = result.Keys.ToList();
        var rows = await db.FolderAcls
            .Where(a => ids.Contains(a.FolderId) && groupIds.Contains(a.GroupId))
            .GroupBy(a => a.FolderId)
            .Select(g => new { FolderId = g.Key, Max = g.Max(a => (int)a.Permission) })
            .ToListAsync();

        foreach (var row in rows)
            result[row.FolderId] = (FolderPermission)row.Max;
        return result;
    }
}
