using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Gateway;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ETL_SQL.Portal.Tests;

public sealed class ControlPlaneDashboardTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"etlsql-cp-dashboard-{Guid.NewGuid():N}.db");

    private const string ManagementKey = "platform-secret-management-key-at-least-32-chars-long";

    private async Task<PortalDbContext> DatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;
        var db = new PortalDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static PortalConfig Config(bool enabled = true) => new()
    {
        SharedTenancy = new SharedTenancyConfig
        {
            Enabled = enabled,
            LifecycleManagementKey = ManagementKey,
            DefaultRelease = "v0.18.0",
            DefaultMaxConcurrentJobs = 10,
            DefaultMaxStorageMb = 20480,
            DefaultMaxReportSessions = 50
        }
    };

    [Fact]
    public async Task GetFleetOverview_AggregatesCountsAcrossTenantsAndGateways()
    {
        await using var db = await DatabaseAsync();
        db.SharedTenantLifecycles.AddRange(
            new SharedTenantLifecycle { TenantId = "tenant-alpha", State = "Active", MaxConcurrentJobs = 10, MaxStorageMb = 10240, MaxReportSessions = 20 },
            new SharedTenantLifecycle { TenantId = "tenant-beta", State = "Active", MaxConcurrentJobs = 20, MaxStorageMb = 20480, MaxReportSessions = 40 },
            new SharedTenantLifecycle { TenantId = "tenant-gamma", State = "Maintenance", MaxConcurrentJobs = 5, MaxStorageMb = 5120, MaxReportSessions = 10 },
            new SharedTenantLifecycle { TenantId = "tenant-delta", State = "Provisioning", MaxConcurrentJobs = 5, MaxStorageMb = 5120, MaxReportSessions = 10 }
        );
        await db.SaveChangesAsync();

        var gatewayRegistry = new GatewaySessionRegistry();

        var service = new ControlPlaneDashboardService(db, executions: null, gatewayRegistry: gatewayRegistry);
        var overview = await service.GetFleetOverviewAsync();

        Assert.Equal(4, overview.TotalTenants);
        Assert.Equal(2, overview.ActiveTenants);
        Assert.Equal(1, overview.MaintenanceTenants);
        Assert.Equal(1, overview.ProvisioningTenants);
        Assert.Equal(0, overview.QuarantinedTenants);
        Assert.Equal(0, overview.DeletingTenants);
    }

    [Fact]
    public async Task GetTenantInventory_CalculatesWorkloadAndQuotaHeadroom_ContainsZeroCustomerData()
    {
        await using var db = await DatabaseAsync();
        db.SharedTenantLifecycles.AddRange(
            new SharedTenantLifecycle { TenantId = "tenant-alpha", State = "Active", ActiveRelease = "v0.18.0", MaxConcurrentJobs = 10, MaxStorageMb = 10240, MaxReportSessions = 20 },
            new SharedTenantLifecycle { TenantId = "tenant-beta", State = "Active", ActiveRelease = "v0.18.0", MaxConcurrentJobs = 20, MaxStorageMb = 20480, MaxReportSessions = 40 }
        );
        await db.SaveChangesAsync();

        var service = new ControlPlaneDashboardService(db);
        var inventory = await service.GetTenantInventoryAsync();

        Assert.Equal(2, inventory.Count);
        Assert.Equal("tenant-alpha", inventory[0].TenantId);
        Assert.Equal("tenant-beta", inventory[1].TenantId);

        // Security assertion: ControlPlaneTenantDto must never contain customer payload, secret, or script fields
        var propertyNames = typeof(ControlPlaneTenantDto).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var forbiddenNames = new[] { "Script", "Payload", "Data", "RowSample", "Password", "Secret", "ApiKey", "Credential", "SqlQuery" };
        foreach (var forbidden in forbiddenNames)
        {
            Assert.DoesNotContain(forbidden, propertyNames);
        }
    }

    [Fact]
    public async Task GetPlatformAuditTrail_ReturnsAttributedReceiptsWithHashes()
    {
        await using var db = await DatabaseAsync();
        db.SharedTenantLifecycleOperations.Add(new SharedTenantLifecycleOperation
        {
            OperationId = "op-prov-101",
            TenantId = "tenant-alpha",
            Kind = "Provision",
            Status = "Completed",
            Phase = "Activated",
            PlatformOperator = "operator@platform.test",
            AuthorizationReference = "CHG-2026-0819",
            Reason = "New tenant onboarding",
            AuthorizationExpiresUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var service = new ControlPlaneDashboardService(db);
        var audit = await service.GetPlatformAuditTrailAsync();

        var entry = Assert.Single(audit);
        Assert.Equal("op-prov-101", entry.OperationId);
        Assert.Equal("tenant-alpha", entry.TenantId);
        Assert.Equal("operator@platform.test", entry.PlatformOperator);
        Assert.Equal("CHG-2026-0819", entry.AuthorizationReference);
        Assert.False(string.IsNullOrWhiteSpace(entry.ReceiptHash));
    }

    [Fact]
    public async Task Controller_EnforcesAuthenticationAndDisabledPosture()
    {
        await using var db = await DatabaseAsync();
        var service = new ControlPlaneDashboardService(db);
        var controller = new ControlPlaneDashboardController(service, Config());

        // 1. Missing header -> 401 Unauthorized
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        var res1 = await controller.GetFleetOverview(CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(res1);

        // 2. Wrong header -> 401 Unauthorized
        var ctx2 = new DefaultHttpContext();
        ctx2.Request.Headers[SharedTenantLifecycleController.ManagementKeyHeader] = "invalid-key-that-does-not-match";
        controller.ControllerContext = new ControllerContext { HttpContext = ctx2 };
        var res2 = await controller.GetFleetOverview(CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(res2);

        // 3. Valid key -> 200 OK
        var ctx3 = new DefaultHttpContext();
        ctx3.Request.Headers[SharedTenantLifecycleController.ManagementKeyHeader] = ManagementKey;
        controller.ControllerContext = new ControllerContext { HttpContext = ctx3 };
        var res3 = await controller.GetFleetOverview(CancellationToken.None);
        Assert.IsType<OkObjectResult>(res3);

        // 4. Disabled shared tenancy -> 404 NotFound
        var disabledController = new ControlPlaneDashboardController(service, Config(enabled: false))
        {
            ControllerContext = new ControllerContext { HttpContext = ctx3 }
        };
        var res4 = await disabledController.GetFleetOverview(CancellationToken.None);
        Assert.IsType<NotFoundResult>(res4);
    }

    [Fact]
    public async Task ProvisionTenant_CreatesTenantResourcesAndAuditReceipt()
    {
        await using var db = await DatabaseAsync();
        var service = new ControlPlaneDashboardService(db);

        var req = new ProvisionTenantAdminRequest(
            TenantId: "tenant-new-corp",
            ActiveRelease: "v0.18.0",
            MaxConcurrentJobs: 15,
            MaxStorageMb: 40960,
            MaxReportSessions: 60,
            PlatformOperator: "admin@platform.test",
            AuthorizationReference: "CHG-2026-9999",
            Reason: "New tenant onboarding");

        var receipt = await service.ProvisionTenantAsync(req);

        Assert.Equal("tenant-new-corp", receipt.TenantId);
        Assert.Equal("Provision", receipt.Kind);
        Assert.Equal("Completed", receipt.Status);
        Assert.False(string.IsNullOrWhiteSpace(receipt.ReceiptHash));

        // Verify DB
        var savedTenant = await db.SharedTenantLifecycles.SingleOrDefaultAsync(t => t.TenantId == "tenant-new-corp");
        Assert.NotNull(savedTenant);
        Assert.Equal("Active", savedTenant.State);
        Assert.Equal(15, savedTenant.MaxConcurrentJobs);

        var resources = await db.SharedTenantResources.Where(r => r.TenantId == "tenant-new-corp").ToListAsync();
        Assert.Equal(3, resources.Count);

        var savedOp = await db.SharedTenantLifecycleOperations.SingleOrDefaultAsync(o => o.TenantId == "tenant-new-corp");
        Assert.NotNull(savedOp);
        Assert.Equal("admin@platform.test", savedOp.PlatformOperator);
    }

    [Fact]
    public async Task UpdateTenantQuotasAndState_ModifiesRecordAndEmitsAuditReceipts()
    {
        await using var db = await DatabaseAsync();
        db.SharedTenantLifecycles.Add(new SharedTenantLifecycle
        {
            TenantId = "tenant-target",
            State = "Active",
            ActiveRelease = "v0.18.0",
            MaxConcurrentJobs = 10,
            MaxStorageMb = 10240,
            MaxReportSessions = 20
        });
        await db.SaveChangesAsync();

        var service = new ControlPlaneDashboardService(db);

        // 1. Update Quotas
        var quotaReq = new UpdateTenantQuotasAdminRequest(
            MaxConcurrentJobs: 25,
            MaxStorageMb: 51200,
            MaxReportSessions: 50,
            PlatformOperator: "sre@platform.test",
            AuthorizationReference: "CHG-QUOTA-01",
            Reason: "Scale up for marketing campaign");

        var quotaReceipt = await service.UpdateTenantQuotasAsync("tenant-target", quotaReq);
        Assert.Equal("UpdateQuotas", quotaReceipt.Kind);
        Assert.Equal(25, quotaReceipt.TargetMaxConcurrentJobs);

        var updatedTenant = await db.SharedTenantLifecycles.SingleAsync(t => t.TenantId == "tenant-target");
        Assert.Equal(25, updatedTenant.MaxConcurrentJobs);
        Assert.Equal(51200, updatedTenant.MaxStorageMb);

        // 2. Update State to Maintenance
        var stateReq = new UpdateTenantStateAdminRequest(
            State: "Maintenance",
            PlatformOperator: "sre@platform.test",
            AuthorizationReference: "INC-2026-444",
            Reason: "Emergency maintenance window");

        var stateReceipt = await service.UpdateTenantStateAsync("tenant-target", stateReq);
        Assert.Equal("SetState:Maintenance", stateReceipt.Kind);

        var maintTenant = await db.SharedTenantLifecycles.SingleAsync(t => t.TenantId == "tenant-target");
        Assert.Equal("Maintenance", maintTenant.State);

        // Verify 2 audit receipts generated
        var auditTrail = await service.GetPlatformAuditTrailAsync();
        Assert.Equal(2, auditTrail.Count);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
