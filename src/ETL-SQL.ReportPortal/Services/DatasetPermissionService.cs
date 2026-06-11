using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Central authority for dataset read and edit permissions.
/// </summary>
public sealed class DatasetPermissionService(
    PortalDbContext db,
    FolderPermissionService folderPermissions)
{
    public static bool CanView(DatasetPermission? permission) =>
        permission is not null;

    public static bool CanRefresh(DatasetPermission? permission) =>
        permission >= DatasetPermission.Refresh;

    public static bool CanEdit(DatasetPermission? permission) =>
        permission >= DatasetPermission.Editor;

    public static bool CanManage(DatasetPermission? permission) =>
        permission >= DatasetPermission.Owner;

    public async Task<ISet<int>> GetUserGroupIdsAsync(int userId)
    {
        return (await db.UserGroups
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.GroupId)
                .ToListAsync())
            .ToHashSet();
    }

    public async Task<DatasetPermission?> GetEffectivePermissionAsync(
        Dataset dataset,
        int? userId,
        bool isAdmin,
        ISet<int>? groupIds = null)
    {
        if (isAdmin)
            return DatasetPermission.Owner;

        if (userId is null)
            return null;

        if (dataset.CreatedBy == userId ||
            dataset.OwningReport?.CreatedBy == userId)
        {
            return DatasetPermission.Owner;
        }

        groupIds ??= await GetUserGroupIdsAsync(userId.Value);

        var aclPermission = dataset.Acls
            .Where(a => groupIds.Contains(a.GroupId))
            .Select(a => (DatasetPermission?)a.Permission)
            .Max();

        if (dataset.AccessLevel == DatasetAccessLevel.Public)
        {
            if (dataset.FolderId is int folderId &&
                await folderPermissions.GetEffectivePermissionAsync(folderId, groupIds) is null)
            {
                return null;
            }

            // Datasets without a folder retain the legacy authenticated-user fallback.
            return aclPermission ?? DatasetPermission.Viewer;
        }

        return aclPermission;
    }
}
