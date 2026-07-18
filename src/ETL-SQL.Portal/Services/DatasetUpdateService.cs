using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class DatasetUpdateService(
    PortalDbContext db,
    AuditService audit)
{
    public async Task<DatasetUpdateResult> UpdateAsync(
        Dataset dataset,
        UpdateDatasetRequest request,
        long expectedVersion,
        int currentUserId,
        bool canManage)
    {
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion))
            return DatasetUpdateResult.Conflict();

        if (request.AccessLevel is not null)
        {
            if (!Enum.TryParse<DatasetAccessLevel>(request.AccessLevel, ignoreCase: true, out var level))
                return DatasetUpdateResult.InvalidAccessLevel();

            if (level != dataset.AccessLevel && !canManage)
                return DatasetUpdateResult.Forbidden();

            dataset.AccessLevel = level;
        }

        if (request.Ttl is not null)
            dataset.Ttl = string.IsNullOrWhiteSpace(request.Ttl) ? null : request.Ttl;

        dataset.UpdatedAt = DateTime.UtcNow;
        audit.Stage(currentUserId, "UPDATE_DATASET", "Dataset", dataset.Id.ToString(), dataset.Name);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(dataset).ReloadAsync();
            return DatasetUpdateResult.Conflict();
        }

        return DatasetUpdateResult.Saved();
    }
}

public enum DatasetUpdateResultKind
{
    Saved,
    InvalidAccessLevel,
    Forbidden,
    Conflict
}

public sealed record DatasetUpdateResult(DatasetUpdateResultKind Kind)
{
    public static DatasetUpdateResult Saved() => new(DatasetUpdateResultKind.Saved);
    public static DatasetUpdateResult InvalidAccessLevel() => new(DatasetUpdateResultKind.InvalidAccessLevel);
    public static DatasetUpdateResult Forbidden() => new(DatasetUpdateResultKind.Forbidden);
    public static DatasetUpdateResult Conflict() => new(DatasetUpdateResultKind.Conflict);
}
