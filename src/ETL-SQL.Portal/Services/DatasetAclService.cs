using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class DatasetAclService(
    PortalDbContext db,
    AuditService audit,
    SecuritySessionService securitySessions)
{
    public async Task<DatasetAclMutationResult> GrantAsync(
        Dataset dataset,
        int groupId,
        string permission,
        long expectedVersion,
        int currentUserId)
    {
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion))
            return DatasetAclMutationResult.Conflict();

        if (!Enum.TryParse<DatasetPermission>(permission, ignoreCase: true, out var granted))
            return DatasetAclMutationResult.InvalidPermission();

        if (!await db.Groups.AnyAsync(g => g.Id == groupId))
            return DatasetAclMutationResult.GroupNotFound();

        var existing = await db.DatasetAcls
            .FirstOrDefaultAsync(a => a.DatasetId == dataset.Id && a.GroupId == groupId);

        if (existing is null)
            db.DatasetAcls.Add(new DatasetAcl { DatasetId = dataset.Id, GroupId = groupId, Permission = granted });
        else
            existing.Permission = granted;

        audit.Stage(currentUserId, "GRANT_DATASET_PERMISSION", "Dataset", dataset.Id.ToString(),
            $"{dataset.Name} -> group {groupId} as {granted}");
        return await SaveAndInvalidateAsync(dataset, groupId);
    }

    public async Task<DatasetAclMutationResult> RevokeAsync(
        Dataset dataset,
        int groupId,
        long expectedVersion,
        int currentUserId)
    {
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion))
            return DatasetAclMutationResult.Conflict();

        var acl = await db.DatasetAcls
            .FirstOrDefaultAsync(a => a.DatasetId == dataset.Id && a.GroupId == groupId);
        if (acl is null)
            return DatasetAclMutationResult.AclNotFound();

        db.DatasetAcls.Remove(acl);
        audit.Stage(currentUserId, "REVOKE_DATASET_PERMISSION", "Dataset", dataset.Id.ToString(),
            $"{dataset.Name} -> group {groupId}");
        return await SaveAndInvalidateAsync(dataset, groupId);
    }

    /// <summary>
    /// Revokes a grant made directly to a user — including the Owner grant a dataset's creator
    /// receives at registration. Without this an administrator could see that grant but not remove
    /// it, which would leave "authorship is revocable" true only in the database.
    /// </summary>
    public async Task<DatasetAclMutationResult> RevokeUserAsync(
        Dataset dataset,
        int userId,
        long expectedVersion,
        int currentUserId)
    {
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion))
            return DatasetAclMutationResult.Conflict();

        var acl = await db.DatasetUserAcls
            .FirstOrDefaultAsync(a => a.DatasetId == dataset.Id && a.UserId == userId);
        if (acl is null)
            return DatasetAclMutationResult.AclNotFound();

        db.DatasetUserAcls.Remove(acl);
        audit.Stage(currentUserId, "REVOKE_DATASET_PERMISSION", "Dataset", dataset.Id.ToString(),
            $"{dataset.Name} -> user {userId}");

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(dataset).ReloadAsync();
            return DatasetAclMutationResult.Conflict();
        }

        // The revoked user's own sessions must stop carrying the old answer.
        await securitySessions.InvalidateUserAsync(userId);
        return DatasetAclMutationResult.Saved();
    }

    private async Task<DatasetAclMutationResult> SaveAndInvalidateAsync(Dataset dataset, int groupId)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(dataset).ReloadAsync();
            return DatasetAclMutationResult.Conflict();
        }

        await securitySessions.InvalidateGroupMembersAsync(groupId);
        return DatasetAclMutationResult.Saved();
    }
}

public enum DatasetAclMutationResultKind
{
    Saved,
    InvalidPermission,
    GroupNotFound,
    AclNotFound,
    Conflict
}

public sealed record DatasetAclMutationResult(DatasetAclMutationResultKind Kind)
{
    public static DatasetAclMutationResult Saved() => new(DatasetAclMutationResultKind.Saved);
    public static DatasetAclMutationResult InvalidPermission() => new(DatasetAclMutationResultKind.InvalidPermission);
    public static DatasetAclMutationResult GroupNotFound() => new(DatasetAclMutationResultKind.GroupNotFound);
    public static DatasetAclMutationResult AclNotFound() => new(DatasetAclMutationResultKind.AclNotFound);
    public static DatasetAclMutationResult Conflict() => new(DatasetAclMutationResultKind.Conflict);
}
