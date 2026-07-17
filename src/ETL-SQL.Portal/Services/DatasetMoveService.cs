using System.Security.Claims;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class DatasetMoveService(
    PortalDbContext db,
    FolderPermissionService folderPermissions,
    AuditService audit,
    SessionCache sessions)
{
    public async Task<DatasetMoveResult> MoveAsync(
        Dataset dataset,
        Folder destination,
        long expectedVersion,
        ClaimsPrincipal user,
        int currentUserId,
        bool isAdmin)
    {
        if (dataset.FolderId == destination.Id)
            return DatasetMoveResult.Unchanged();

        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion))
            return DatasetMoveResult.Conflict();

        if (dataset.FolderId is int sourceFolderId)
        {
            if (!await folderPermissions.HasPermissionAsync(sourceFolderId, FolderPermission.Manage, user))
                return DatasetMoveResult.Forbidden();
        }
        else if (!isAdmin)
        {
            return DatasetMoveResult.Forbidden();
        }

        if (!await folderPermissions.HasPermissionAsync(destination.Id, FolderPermission.Manage, user))
            return DatasetMoveResult.Forbidden();

        var sourcePath = dataset.FolderPath;
        dataset.FolderId = destination.Id;
        dataset.FolderPath = destination.Path;
        dataset.UpdatedAt = DateTime.UtcNow;
        audit.Stage(
            currentUserId,
            "MOVE_DATASET",
            "Dataset",
            dataset.Id.ToString(),
            $"{dataset.Name}: {sourcePath} -> {destination.Path}");

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(dataset).ReloadAsync();
            return DatasetMoveResult.Conflict();
        }

        if (dataset.OwningReportId is int reportId)
            await sessions.InvalidateReportAsync(reportId);

        return DatasetMoveResult.Moved();
    }
}

public enum DatasetMoveResultKind
{
    Unchanged,
    Forbidden,
    Conflict,
    Moved
}

public sealed record DatasetMoveResult(DatasetMoveResultKind Kind)
{
    public static DatasetMoveResult Unchanged() => new(DatasetMoveResultKind.Unchanged);
    public static DatasetMoveResult Forbidden() => new(DatasetMoveResultKind.Forbidden);
    public static DatasetMoveResult Conflict() => new(DatasetMoveResultKind.Conflict);
    public static DatasetMoveResult Moved() => new(DatasetMoveResultKind.Moved);
}
