using ETL_SQL.ReportPortal.Data;

namespace ETL_SQL.ReportPortal.Services;

public class AuditService(PortalDbContext db)
{
    public async Task LogAsync(int? userId, string action,
        string? resourceType = null, string? resourceId = null, string? detail = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Timestamp = DateTime.UtcNow,
            Detail = detail
        });
        await db.SaveChangesAsync();
    }
}
