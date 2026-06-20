using ETL_SQL.Core.Storage;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ETL_SQL.ReportPortal.Controllers;

/// <summary>
/// Read-only fleet health surface (P2.2). Returns only aggregate operational counts for this
/// environment, gated to the scoped <c>FleetReader</c> role (and Admin). This is the ONLY thing a
/// fleet aggregator credential may reach — it cannot read report data, run scripts, mutate state, or
/// access secrets/keys (see the fleet trust boundary in Departmental_Isolation.md).
/// </summary>
[ApiController]
[Route("api/fleet")]
[Authorize(Roles = "FleetReader,Admin")]
public sealed class FleetStatusController(
    HealthCheckService health,
    ExecutionJobService executions,
    PortalDbContext db,
    IArtifactStorage artifacts) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var report = await health.CheckHealthAsync(ct);
        var (queued, running) = executions.GetWorkloadCounts();

        var failedRefreshes = await db.Reports.CountAsync(r => r.LastRefreshStatus == "Failed", ct);
        var outboxPending = await db.AuditOutboxMessages.CountAsync(x => x.Status == "Pending", ct);
        var outboxFailed = await db.AuditOutboxMessages.CountAsync(x => x.Status == "Failed", ct);

        var storage = await ProbeStorageAsync(ct);

        return Ok(new FleetEnvironmentStatus(
            Environment: Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default",
            Status: report.Status.ToString(),
            QueueDepth: queued,
            ActiveExecutions: running,
            FailedRefreshes: failedRefreshes,
            AuditOutboxPending: outboxPending,
            AuditOutboxFailed: outboxFailed,
            Storage: storage,
            CapturedAtUtc: DateTime.UtcNow));
    }

    private async Task<string> ProbeStorageAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await foreach (var _ in artifacts
                .EnumerateAsync(ArtifactArea.Snapshots, prefix: null, recursive: false, timeout.Token)
                .WithCancellation(timeout.Token))
            {
                break;
            }
            return "ok";
        }
        catch
        {
            return "unavailable";
        }
    }
}
