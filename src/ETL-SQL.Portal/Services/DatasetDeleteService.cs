using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class DatasetDeleteService(
    PortalDbContext db,
    IDatasetRegistry registry,
    AuditService audit)
{
    public async Task<DatasetDeleteResult> DeleteAsync(
        Dataset dataset,
        long expectedVersion,
        int currentUserId)
    {
        if (!OptimisticConcurrency.Prepare(db, dataset, expectedVersion))
            return DatasetDeleteResult.Conflict(dataset);

        audit.Stage(currentUserId, "DELETE_DATASET", "Dataset", dataset.Id.ToString(), dataset.Name);
        try
        {
            await registry.Delete(dataset.Name);
            return DatasetDeleteResult.Deleted();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.Datasets
                .Include(d => d.OwningReport)
                .Include(d => d.Acls)
                .Include(d => d.UserAcls)
                .FirstOrDefaultAsync(d => d.Id == dataset.Id);
            return current is null
                ? DatasetDeleteResult.NotFound()
                : DatasetDeleteResult.Conflict(current);
        }
    }
}

public enum DatasetDeleteResultKind
{
    Deleted,
    NotFound,
    Conflict
}

public sealed record DatasetDeleteResult(DatasetDeleteResultKind Kind, Dataset? Current)
{
    public static DatasetDeleteResult Deleted() => new(DatasetDeleteResultKind.Deleted, null);
    public static DatasetDeleteResult NotFound() => new(DatasetDeleteResultKind.NotFound, null);
    public static DatasetDeleteResult Conflict(Dataset current) => new(DatasetDeleteResultKind.Conflict, current);
}
