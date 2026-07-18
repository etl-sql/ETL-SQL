using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

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

        FolderPermission? folderPermission = null;
        if (dataset.AccessLevel == DatasetAccessLevel.Public && dataset.FolderId is int folderId)
            folderPermission = await folderPermissions.GetEffectivePermissionAsync(folderId, groupIds, userId);

        return Evaluate(dataset, userId.Value, groupIds, folderPermission);
    }

    /// <summary>
    /// Resolves the caller's permission for every dataset with two queries total (the
    /// caller's group ids and one folder-ACL aggregate), instead of one or two per dataset.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, DatasetPermission?>> GetEffectivePermissionsAsync(
        IReadOnlyCollection<Dataset> datasets,
        int? userId,
        bool isAdmin)
    {
        var result = new Dictionary<int, DatasetPermission?>(datasets.Count);

        if (isAdmin)
        {
            foreach (var dataset in datasets)
                result[dataset.Id] = DatasetPermission.Owner;
            return result;
        }

        if (userId is null)
        {
            foreach (var dataset in datasets)
                result[dataset.Id] = null;
            return result;
        }

        var groupIds = await GetUserGroupIdsAsync(userId.Value);

        var folderIds = datasets
            .Where(d => d.AccessLevel == DatasetAccessLevel.Public && d.FolderId is not null)
            .Select(d => d.FolderId!.Value)
            .Distinct()
            .ToList();
        var folderPermissionMap = await folderPermissions.GetEffectivePermissionsAsync(folderIds, groupIds, userId);

        foreach (var dataset in datasets)
        {
            FolderPermission? folderPermission = null;
            if (dataset.FolderId is int folderId)
                folderPermissionMap.TryGetValue(folderId, out folderPermission);
            result[dataset.Id] = Evaluate(dataset, userId.Value, groupIds, folderPermission);
        }

        return result;
    }

    private static DatasetPermission? Evaluate(
        Dataset dataset,
        int userId,
        ISet<int> groupIds,
        FolderPermission? folderPermission)
    {
        if (dataset.CreatedBy == userId ||
            dataset.OwningReport?.CreatedBy == userId)
        {
            return DatasetPermission.Owner;
        }

        var aclPermission = dataset.Acls
            .Where(a => groupIds.Contains(a.GroupId))
            .Select(a => (DatasetPermission?)a.Permission)
            .Max();

        if (dataset.AccessLevel == DatasetAccessLevel.Public)
        {
            if (dataset.FolderId is not null && folderPermission is null)
                return null;

            // Datasets without a folder retain the legacy authenticated-user fallback.
            return aclPermission ?? DatasetPermission.Viewer;
        }

        return aclPermission;
    }
}
