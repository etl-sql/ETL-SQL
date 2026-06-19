using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public sealed class AuditRedactionTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "audit_redaction_" + Guid.NewGuid().ToString("N")[..8]);

    public AuditRedactionTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    [Fact]
    public async Task AuditService_RedactsSecretsBeforePersistingRows()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_scratch, "portal.db")}")
            .Options;
        await using var db = new PortalDbContext(options);
        await db.Database.MigrateAsync();

        var audit = new AuditService(db, new HttpContextAccessor());
        await audit.LogAsync(
            userId: 7,
            action: "TEST_SECRET_REDCTION",
            resourceType: "Connection",
            resourceId: "SECRET:prod/reporting",
            detail: "PASSWORD=cleartext; token=raw-token; Authorization: Bearer bearer-token");

        var row = await db.AuditLogs.SingleAsync(a => a.Action == "TEST_SECRET_REDCTION");

        Assert.DoesNotContain("prod/reporting", row.ResourceId);
        Assert.DoesNotContain("cleartext", row.Detail);
        Assert.DoesNotContain("raw-token", row.Detail);
        Assert.DoesNotContain("bearer-token", row.Detail);
        Assert.Contains("********", row.Detail);
    }
}
