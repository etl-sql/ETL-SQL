using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// P2.2 governance recovery certification for the fail-closed mutation policy (P1.12): when remote
/// audit delivery is required and the local outbox shows the collector is unreachable, an audited
/// mutation must be blocked at commit — but a single event during a brief outage, and all mutations
/// under the default (best-effort) posture, must still proceed. The audit row is staged into the
/// same unit of work as the mutation, so the interceptor blocking the save certifies the mutation
/// itself cannot commit. Recovery scenarios for expired/unavailable policy and secret-provider
/// failure are certified in the Core suite (OrganizationPolicyCacheTests, GovernanceRecoveryTests).
/// </summary>
[Trait("Category", "Portal")]
public sealed class GovernanceRecoveryCertificationTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "gov_recovery_" + Guid.NewGuid().ToString("N")[..8]);

    public GovernanceRecoveryCertificationTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    [Fact]
    public async Task UnavailableCollector_BacklogOverLimit_BlocksAuditedMutation()
    {
        var config = new PortalConfig
        {
            Audit =
            {
                RequireRemoteDelivery = true,
                FailClosedMaxPendingBacklog = 3,
                FailClosedMaxBacklogSeconds = 0
            }
        };
        var provider = await CreateProviderAsync("backlog.db", config);
        await SeedPendingBacklogAsync(provider, count: 3); // already at the limit

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = new AuditService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        audit.Stage(7, "DELETE_USER", "User", "7");

        var ex = await Assert.ThrowsAsync<AuditDeliveryUnavailableException>(() => db.SaveChangesAsync());
        Assert.Contains("backlog", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The mutation's unit of work was rejected wholesale: no new audit row landed.
        await using var verify = provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal(3, await verifyDb.AuditOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task UnavailableCollector_FailedDelivery_BlocksAuditedMutation()
    {
        var config = new PortalConfig { Audit = { RequireRemoteDelivery = true } };
        var provider = await CreateProviderAsync("failed.db", config);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.AuditOutboxMessages.Add(Row("Failed"));
            await db.SaveChangesAsync();
        }

        await using var work = provider.CreateAsyncScope();
        var workDb = work.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = new AuditService(workDb, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        audit.Stage(7, "RESET_PASSWORD", "User", "7");

        await Assert.ThrowsAsync<AuditDeliveryUnavailableException>(() => workDb.SaveChangesAsync());
    }

    [Fact]
    public async Task FirstEventDuringOutage_IsAllowed()
    {
        var config = new PortalConfig
        {
            Audit = { RequireRemoteDelivery = true, FailClosedMaxPendingBacklog = 1000, FailClosedMaxBacklogSeconds = 900 }
        };
        var provider = await CreateProviderAsync("first.db", config);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = new AuditService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        audit.Stage(7, "UPDATE_USER", "User", "7");

        // No prior backlog: a single staged event commits even with delivery required.
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.AuditOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task DefaultPosture_DoesNotBlock_EvenWithLargeBacklog()
    {
        var config = new PortalConfig { Audit = { RequireRemoteDelivery = false, FailClosedMaxPendingBacklog = 1 } };
        var provider = await CreateProviderAsync("default.db", config);
        await SeedPendingBacklogAsync(provider, count: 50);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var audit = new AuditService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        audit.Stage(7, "UPDATE_USER", "User", "7");

        // Local zero-trust default (best-effort forwarding) is unchanged by the new policy.
        await db.SaveChangesAsync();
        Assert.Equal(51, await db.AuditOutboxMessages.CountAsync());
    }

    private async Task<ServiceProvider> CreateProviderAsync(string dbName, PortalConfig config)
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditFailClosedInterceptor>();
        services.AddDbContext<PortalDbContext>((sp, opt) =>
            opt.UseSqlite($"Data Source={Path.Combine(_scratch, dbName)}")
               .AddInterceptors(sp.GetRequiredService<AuditFailClosedInterceptor>()));
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Database.MigrateAsync();
        return provider;
    }

    private static async Task SeedPendingBacklogAsync(ServiceProvider provider, int count)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        for (var i = 0; i < count; i++)
            db.AuditOutboxMessages.Add(Row("Pending"));
        await db.SaveChangesAsync();
    }

    private static AuditOutboxMessage Row(string status) => new()
    {
        Action = "SEED",
        PayloadJson = "{\"action\":\"SEED\"}",
        Status = status,
        OccurredAt = DateTime.UtcNow.AddMinutes(-1),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
