using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;
using ETL_SQL.Gateway;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record ControlPlaneFleetOverview(
    int TotalTenants,
    int ActiveTenants,
    int ProvisioningTenants,
    int MaintenanceTenants,
    int QuarantinedTenants,
    int DeletingTenants,
    int ActiveExecutions,
    int QueuedExecutions,
    int ConnectedGateways,
    int UniqueGatewayTenants,
    int AuditOutboxPending,
    int AuditOutboxFailed,
    string Environment,
    DateTimeOffset CapturedAtUtc);

public sealed record ControlPlaneTenantDto(
    string TenantId,
    string State,
    string ActiveRelease,
    int MaxConcurrentJobs,
    int MaxStorageMb,
    int MaxReportSessions,
    long FenceEpoch,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? DeletedAtUtc,
    int ActiveExecutions,
    int QueuedExecutions,
    int ConnectedGateways,
    double QuotaUtilizationPercentage);

public sealed record PlatformAuditReceiptDto(
    string OperationId,
    string TenantId,
    string Kind,
    string Status,
    string Phase,
    string PlatformOperator,
    string AuthorizationReference,
    string Reason,
    DateTime AuthorizationExpiresUtc,
    string? TargetRelease,
    int? TargetMaxConcurrentJobs,
    int? TargetMaxStorageMb,
    int? TargetMaxReportSessions,
    string ReceiptHash);

public sealed record ControlPlaneTenantHealthDto(
    string TenantId,
    string State,
    string ActiveRelease,
    int ActiveExecutions,
    int QueuedExecutions,
    int MaxConcurrentJobs,
    int MaxStorageMb,
    int MaxReportSessions,
    int ConnectedGateways,
    bool IsAtCapacity,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Service for the SaaS Control Plane Dashboard (Platform Admin UI).
/// 
/// Enforces Platform Identity Isolation:
/// - Summarizes aggregate fleet health, worker capacity, and tenant operational quotas.
/// - Strictly prevents data peeking: responses contain ZERO customer scripts, row contents,
///   query parameters, or credentials.
/// </summary>
public sealed class ControlPlaneDashboardService(
    PortalDbContext db,
    ExecutionJobService? executions = null,
    GatewaySessionRegistry? gatewayRegistry = null)
{
    public async Task<ControlPlaneFleetOverview> GetFleetOverviewAsync(CancellationToken ct = default)
    {
        var tenants = await db.SharedTenantLifecycles.AsNoTracking().ToListAsync(ct);

        var total = tenants.Count;
        var active = tenants.Count(t => t.State.Equals("Active", StringComparison.OrdinalIgnoreCase));
        var provisioning = tenants.Count(t => t.State.Equals("Provisioning", StringComparison.OrdinalIgnoreCase));
        var maintenance = tenants.Count(t => t.State.Equals("Maintenance", StringComparison.OrdinalIgnoreCase));
        var quarantined = tenants.Count(t => t.State.Equals("Quarantined", StringComparison.OrdinalIgnoreCase));
        var deleting = tenants.Count(t => t.State.Equals("Deleting", StringComparison.OrdinalIgnoreCase) || t.State.Equals("Deleted", StringComparison.OrdinalIgnoreCase));

        int activeExec = 0;
        int queuedExec = 0;
        if (executions is not null)
        {
            var (q, r) = executions.GetWorkloadCounts(null);
            queuedExec = q;
            activeExec = r;
        }

        var connectedGateways = 0;
        var uniqueGatewayTenants = 0;
        if (gatewayRegistry is not null)
        {
            var sessions = gatewayRegistry.ListActive();
            connectedGateways = sessions.Count;
            uniqueGatewayTenants = sessions.Select(s => s.TenantId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }

        var outboxPending = await db.AuditOutboxMessages.CountAsync(x => x.Status == "Pending", ct);
        var outboxFailed = await db.AuditOutboxMessages.CountAsync(x => x.Status == "Failed", ct);

        var env = Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "production-saas";

        return new ControlPlaneFleetOverview(
            TotalTenants: total,
            ActiveTenants: active,
            ProvisioningTenants: provisioning,
            MaintenanceTenants: maintenance,
            QuarantinedTenants: quarantined,
            DeletingTenants: deleting,
            ActiveExecutions: activeExec,
            QueuedExecutions: queuedExec,
            ConnectedGateways: connectedGateways,
            UniqueGatewayTenants: uniqueGatewayTenants,
            AuditOutboxPending: outboxPending,
            AuditOutboxFailed: outboxFailed,
            Environment: env,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<ControlPlaneTenantDto>> GetTenantInventoryAsync(CancellationToken ct = default)
    {
        var tenants = await db.SharedTenantLifecycles.AsNoTracking()
            .OrderBy(t => t.TenantId)
            .ToListAsync(ct);

        var result = new List<ControlPlaneTenantDto>(tenants.Count);
        foreach (var tenant in tenants)
        {
            int activeExec = 0;
            int queuedExec = 0;
            if (executions is not null)
            {
                var (q, r) = executions.GetWorkloadCounts(tenant.TenantId);
                queuedExec = q;
                activeExec = r;
            }

            int connectedGateways = 0;
            if (gatewayRegistry is not null)
            {
                connectedGateways = gatewayRegistry.ListActive(tenant.TenantId).Count;
            }

            double quotaPct = 0;
            if (tenant.MaxConcurrentJobs > 0)
            {
                quotaPct = Math.Round((double)(activeExec + queuedExec) / tenant.MaxConcurrentJobs * 100.0, 1);
            }

            result.Add(new ControlPlaneTenantDto(
                TenantId: tenant.TenantId,
                State: tenant.State,
                ActiveRelease: tenant.ActiveRelease,
                MaxConcurrentJobs: tenant.MaxConcurrentJobs,
                MaxStorageMb: tenant.MaxStorageMb,
                MaxReportSessions: tenant.MaxReportSessions,
                FenceEpoch: tenant.FenceEpoch,
                CreatedAtUtc: tenant.CreatedAtUtc,
                UpdatedAtUtc: tenant.UpdatedAtUtc,
                DeletedAtUtc: tenant.DeletedAtUtc,
                ActiveExecutions: activeExec,
                QueuedExecutions: queuedExec,
                ConnectedGateways: connectedGateways,
                QuotaUtilizationPercentage: quotaPct));
        }

        return result;
    }

    public async Task<IReadOnlyList<PlatformAuditReceiptDto>> GetPlatformAuditTrailAsync(int limit = 100, CancellationToken ct = default)
    {
        var operations = await db.SharedTenantLifecycleOperations.AsNoTracking()
            .OrderByDescending(o => o.AuthorizationExpiresUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

        return operations.Select(o =>
        {
            var raw = $"{o.OperationId}:{o.TenantId}:{o.Kind}:{o.Status}:{o.PlatformOperator}:{o.AuthorizationReference}:{o.Reason}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];

            return new PlatformAuditReceiptDto(
                OperationId: o.OperationId,
                TenantId: o.TenantId,
                Kind: o.Kind,
                Status: o.Status,
                Phase: o.Phase,
                PlatformOperator: o.PlatformOperator,
                AuthorizationReference: o.AuthorizationReference,
                Reason: o.Reason,
                AuthorizationExpiresUtc: o.AuthorizationExpiresUtc,
                TargetRelease: o.TargetRelease,
                TargetMaxConcurrentJobs: o.TargetMaxConcurrentJobs,
                TargetMaxStorageMb: o.TargetMaxStorageMb,
                TargetMaxReportSessions: o.TargetMaxReportSessions,
                ReceiptHash: hash);
        }).ToList();
    }

    public async Task<ControlPlaneTenantHealthDto?> GetTenantHealthAsync(string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) return null;

        var tenant = await db.SharedTenantLifecycles.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

        if (tenant is null) return null;

        int activeExec = 0;
        int queuedExec = 0;
        if (executions is not null)
        {
            var (q, r) = executions.GetWorkloadCounts(tenant.TenantId);
            queuedExec = q;
            activeExec = r;
        }

        int connectedGateways = 0;
        if (gatewayRegistry is not null)
        {
            connectedGateways = gatewayRegistry.ListActive(tenant.TenantId).Count;
        }

        bool isAtCapacity = tenant.MaxConcurrentJobs > 0 && (activeExec + queuedExec) >= tenant.MaxConcurrentJobs;

        return new ControlPlaneTenantHealthDto(
            TenantId: tenant.TenantId,
            State: tenant.State,
            ActiveRelease: tenant.ActiveRelease,
            ActiveExecutions: activeExec,
            QueuedExecutions: queuedExec,
            MaxConcurrentJobs: tenant.MaxConcurrentJobs,
            MaxStorageMb: tenant.MaxStorageMb,
            MaxReportSessions: tenant.MaxReportSessions,
            ConnectedGateways: connectedGateways,
            IsAtCapacity: isAtCapacity,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }
}
