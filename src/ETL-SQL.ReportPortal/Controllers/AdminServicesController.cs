using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Controllers;

/// <summary>Status and run history for the native admin background services.</summary>
[ApiController]
[Route("api/admin/services")]
[Authorize(Roles = "Admin")]
public class AdminServicesController(PortalDbContext db, PortalConfig config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var services = new (string Name, AdminServiceScheduleConfig Schedule)[]
        {
            ("failure-digest", config.AdminServices.FailureDigest),
            ("backup-report", config.AdminServices.BackupReport),
            ("capacity-report", config.AdminServices.CapacityReport),
        };

        var lastRuns = new List<AdminServiceRun>();
        foreach (var (name, _) in services)
        {
            var last = await db.AdminServiceRuns
                .AsNoTracking()
                .Where(r => r.ServiceName == name)
                .OrderByDescending(r => r.StartedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (last != null) lastRuns.Add(last);
        }

        return Ok(services.Select(s =>
        {
            var last = lastRuns.FirstOrDefault(r => r.ServiceName == s.Name);
            return new
            {
                name = s.Name,
                enabled = s.Schedule.Enabled,
                intervalHours = s.Schedule.IntervalHours,
                smtpAlias = s.Schedule.SmtpAlias,
                recipients = s.Schedule.Recipients,
                lastRun = last == null ? null : new
                {
                    last.Outcome,
                    last.StartedAtUtc,
                    last.CompletedAtUtc,
                    last.Attempts,
                    last.NodeName,
                    last.Detail
                }
            };
        }));
    }

    [HttpGet("{name}/history")]
    public async Task<IActionResult> History(string name, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var runs = await db.AdminServiceRuns
            .AsNoTracking()
            .Where(r => r.ServiceName == name)
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        return Ok(runs);
    }
}
