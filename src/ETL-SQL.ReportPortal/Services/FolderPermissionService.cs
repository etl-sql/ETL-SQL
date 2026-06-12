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
}
