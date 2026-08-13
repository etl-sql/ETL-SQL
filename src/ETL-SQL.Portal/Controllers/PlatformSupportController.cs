using System.Text;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Capability-only Managed Dedicated support surface. It deliberately does not accept a Portal
/// user or platform-superuser role: the tenant-issued capability is the complete, narrow authority.
/// </summary>
[ApiController]
[Route("api/platform/support-bundle")]
[AllowAnonymous]
public sealed class PlatformSupportController(
    PortalSupportBundleService bundle,
    SupportAccessApprovalService approvals,
    AuditService audit) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("anonymous-token")]
    public async Task<IActionResult> Download(CancellationToken ct)
    {
        var capability = Request.Headers[SupportAccessApprovalService.HeaderName].SingleOrDefault();
        if (string.IsNullOrWhiteSpace(capability))
            return Unauthorized(new { error = "tenant_support_approval_required" });

        var content = await bundle.BuildAsync(ct);
        SupportAccessApprovalService.Approval approval;
        try
        {
            approval = approvals.Validate(capability, content.ContentHash);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or InvalidOperationException)
        {
            await audit.LogAsync(null, "PLATFORM_SUPPORT_ACCESS_REFUSED", "SupportBundle", null,
                ex.Message, actorType: "PlatformOperator", actorId: "unverified");
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "support_capability_invalid", message = ex.Message });
        }

        await audit.LogAsync(null, "PLATFORM_SUPPORT_BUNDLE_DOWNLOADED", "SupportBundle",
            approval.CapabilityId,
            $"tenant={approval.TenantId}; content={approval.ContentHash}; purpose={approval.Purpose}; approvedBy={approval.ApprovedBy}; expires={approval.ExpiresUtc:O}",
            actorType: "PlatformOperator", actorId: approval.PlatformActor,
            effectiveScopes: "support.bundle.read");

        var json = System.Text.Json.JsonSerializer.Serialize(content,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json",
            $"etl-sql-dedicated-support-{DateTime.UtcNow:yyyyMMdd_HHmm}.json");
    }
}
