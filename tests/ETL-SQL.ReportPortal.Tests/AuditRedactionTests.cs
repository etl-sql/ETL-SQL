using System.Text.Json.Nodes;
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
            action: "TEST_SECRET_REDACTION",
            resourceType: "Connection",
            resourceId: "SECRET:prod/reporting",
            detail: "PASSWORD=cleartext; token=raw-token; Authorization: Bearer bearer-token");

        var row = await db.AuditLogs.SingleAsync(a => a.Action == "TEST_SECRET_REDACTION");
        var outbox = await db.AuditOutboxMessages.SingleAsync(a => a.Action == "TEST_SECRET_REDACTION");
        var payload = JsonNode.Parse(outbox.PayloadJson)!.AsObject();

        Assert.DoesNotContain("prod/reporting", row.ResourceId);
        Assert.DoesNotContain("cleartext", row.Detail);
        Assert.DoesNotContain("raw-token", row.Detail);
        Assert.DoesNotContain("bearer-token", row.Detail);
        Assert.Contains("********", row.Detail);

        Assert.Equal(row.Id, outbox.AuditLogId);
        Assert.Equal("Pending", outbox.Status);
        Assert.Equal(row.CorrelationId, outbox.CorrelationId);
        Assert.DoesNotContain("prod/reporting", outbox.ResourceId);
        Assert.DoesNotContain("cleartext", outbox.PayloadJson);
        Assert.DoesNotContain("raw-token", outbox.PayloadJson);
        Assert.DoesNotContain("bearer-token", outbox.PayloadJson);
        Assert.Equal("TEST_SECRET_REDACTION", payload["action"]!.GetValue<string>());
        Assert.Equal(outbox.ResourceId, payload["resourceId"]!.GetValue<string>());
    }
}
