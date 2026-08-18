using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Platform Admin Control Plane API (P2 maturity).
/// 
/// Dedicated endpoint for fleet observability, tenant operational capacity, and platform audit receipts.
/// Enforces Platform Identity Isolation:
/// - Authenticates exclusively via the platform management key (X-Portal-Platform-Key).
/// - Completely decoupled from tenant JWT / RBAC; tenant principals cannot reach this endpoint.
/// - Responses contain strictly operational metadata and counts (zero tenant script bodies or data rows).
/// </summary>
[ApiController]
[Route("api/platform/control-plane")]
[AllowAnonymous]
public sealed class ControlPlaneDashboardController(
    ControlPlaneDashboardService service,
    PortalConfig config) : ControllerBase
{
    private bool IsAuthorized()
    {
        if (!config.SharedTenancy.Enabled
            || string.IsNullOrWhiteSpace(config.SharedTenancy.LifecycleManagementKey))
            return false;

        return SharedTenantLifecycleController.ManagementKeyAccepted(
            Request, config.SharedTenancy.LifecycleManagementKey);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetFleetOverview(CancellationToken ct)
    {
        if (!config.SharedTenancy.Enabled) return NotFound();
        if (!IsAuthorized()) return Unauthorized(new { error = "Platform management credentials required." });

        var overview = await service.GetFleetOverviewAsync(ct);
        return Ok(overview);
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(CancellationToken ct)
    {
        if (!config.SharedTenancy.Enabled) return NotFound();
        if (!IsAuthorized()) return Unauthorized(new { error = "Platform management credentials required." });

        var tenants = await service.GetTenantInventoryAsync(ct);
        return Ok(tenants);
    }

    [HttpGet("audit")]
    public async Task<IActionResult> GetAuditTrail([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!config.SharedTenancy.Enabled) return NotFound();
        if (!IsAuthorized()) return Unauthorized(new { error = "Platform management credentials required." });

        var audit = await service.GetPlatformAuditTrailAsync(limit, ct);
        return Ok(audit);
    }

    [HttpGet("tenants/{tenantId}/health")]
    public async Task<IActionResult> GetTenantHealth(string tenantId, CancellationToken ct)
    {
        if (!config.SharedTenancy.Enabled) return NotFound();
        if (!IsAuthorized()) return Unauthorized(new { error = "Platform management credentials required." });

        var health = await service.GetTenantHealthAsync(tenantId, ct);
        if (health is null) return NotFound(new { error = $"Tenant '{tenantId}' was not found in the shared fleet." });

        return Ok(health);
    }
}
