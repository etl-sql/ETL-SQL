using System.Security.Claims;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public class FolderPermissionService(PortalDbContext db)
{
    private ISet<int>? _cachedUserGroupIds;

    public int GetUserId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public bool IsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin");

    public async Task<ISet<int>> GetUserGroupIdsAsync(ClaimsPrincipal user)
    {
        if (_cachedUserGroupIds is not null) return _cachedUserGroupIds;

        var userId = GetUserId(user);
        var ids = await db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        _cachedUserGroupIds = new HashSet<int>(ids);
        return _cachedUserGroupIds;
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
