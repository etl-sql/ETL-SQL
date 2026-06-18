using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ETL_SQL.ReportPortal.Services.HealthChecks;

public class ExecutionCapacityHealthCheck(
    PortalConfig config,
    PortalNodeIdentity nodeIdentity,
    IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        int cap = config.Resources.MaxConcurrentReportExecutions;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        int smtpCount = await db.SmtpConnections.CountAsync(ct);
        int activeSubs = await db.Subscriptions.CountAsync(s => s.IsActive, ct);
        int activeExecutions = await db.PortalExecutionJobs.CountAsync(
            job => job.Status == "Pending" || job.Status == "Running", ct);

        var data = new Dictionary<string, object>
        {
            ["execution_cap"] = cap,
            ["active_executions"] = activeExecutions,
            ["portal_topology"] = "shared-state-ha",
            ["portal_node_id"] = nodeIdentity.NodeId,
            ["affinity_cookie"] = config.LoadBalancer.SessionAffinityCookieName,
            ["smtp_connections"] = smtpCount,
            ["active_subscriptions"] = activeSubs
        };

        return HealthCheckResult.Healthy(
            $"Execution cap: {cap}. SMTP connections: {smtpCount}. Active subscriptions: {activeSubs}.",
            data);
    }
}
