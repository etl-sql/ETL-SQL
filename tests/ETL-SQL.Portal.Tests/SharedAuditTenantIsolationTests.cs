using System.Text.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Smoke.Security")]
public sealed class SharedAuditTenantIsolationTests
{
    [Fact]
    public async Task EqualResourcesEventsDiagnosticsAndFailClosedStateRemainTenantPartitioned()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var config = new PortalConfig
        {
            SharedTenancy = new SharedTenancyConfig { Enabled = true },
            Audit =
            {
                RequireRemoteDelivery = true,
                FailClosedMaxPendingBacklog = 100,
                FailClosedMaxBacklogSeconds = 0
            }
        };
        var alphaScope = Scope(config, "tenant-alpha");
        var betaScope = Scope(config, "tenant-beta");
        var context = new HttpContextAccessor();

        await new AuditService(db, context, alphaScope)
            .LogAsync(7, "EQUAL_ACTION", "Report", "42", "alpha");
        await new AuditService(db, context, betaScope)
            .LogAsync(7, "EQUAL_ACTION", "Report", "42", "beta");

        var alphaLog = await db.AuditLogs.SingleAsync(row => row.TenantId == "tenant-alpha");
        var betaLog = await db.AuditLogs.SingleAsync(row => row.TenantId == "tenant-beta");
        Assert.Equal("42", alphaLog.ResourceId);
        Assert.Equal("42", betaLog.ResourceId);
        var alphaOutbox = await db.AuditOutboxMessages
            .SingleAsync(row => row.TenantId == "tenant-alpha");
        using (var payload = JsonDocument.Parse(alphaOutbox.PayloadJson))
            Assert.Equal("tenant-alpha", payload.RootElement.GetProperty("tenantId").GetString());

        const string collision = "same-event-id";
        db.AuditOutboxMessages.AddRange(
            Row("tenant-alpha", collision, "Delivered"),
            Row("tenant-beta", collision, "Failed"));
        await db.SaveChangesAsync();

        await AuditDeliveryGate.EnsureDeliverableAsync(
            db, config.Audit, TimeProvider.System, "tenant-alpha");
        await Assert.ThrowsAsync<AuditDeliveryUnavailableException>(() =>
            AuditDeliveryGate.EnsureDeliverableAsync(
                db, config.Audit, TimeProvider.System, "tenant-beta"));

        var alphaHealth = await new AuditCollectorHealthService(
            db, config, TimeProvider.System, alphaScope).BuildAsync();
        var betaHealth = await new AuditCollectorHealthService(
            db, config, TimeProvider.System, betaScope).BuildAsync();
        Assert.Equal(0, alphaHealth.Failed);
        Assert.Equal(1, betaHealth.Failed);
        Assert.False(alphaHealth.FailClosed.Tripped);
        Assert.True(betaHealth.FailClosed.Tripped);
    }

    [Fact]
    public async Task SharedAuditScopeRejectsMissingVerifiedTenantContext()
    {
        var config = new PortalConfig
        {
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        };
        Assert.Throws<UnauthorizedAccessException>(() => new DatasetTenantScope(config));
        Assert.Throws<UnauthorizedAccessException>(() => new DatasetTenantScope(
            config, TenantContext.FromHostConfiguration("tenant-alpha")));
        await Task.CompletedTask;
    }

    private static DatasetTenantScope Scope(PortalConfig config, string tenant) =>
        new(config, TenantContext.FromVerifiedCredential(tenant));

    private static AuditOutboxMessage Row(string tenantId, string eventId, string status) => new()
    {
        TenantId = tenantId,
        EventId = eventId,
        Action = "COLLISION",
        PayloadJson = "{}",
        Status = status,
        OccurredAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
