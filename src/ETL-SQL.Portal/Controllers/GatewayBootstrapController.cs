using ETL_SQL.Core.Governance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ETL_SQL.Portal.Controllers;

/// <summary>One-time, token-authenticated bootstrap endpoint used by an on-premises Gateway.</summary>
[ApiController]
[Route("api/gateway/enrollment")]
[AllowAnonymous]
[EnableRateLimiting("anonymous-token")]
public sealed class GatewayBootstrapController(IGatewayEnrollmentStore enrollmentStore) : ControllerBase
{
    public sealed record ConsumeEnrollmentRequest(
        string TenantId,
        string OneTimeToken,
        string WorkloadPublicKeyThumbprint);

    [HttpPost("consume")]
    public async Task<IActionResult> Consume(
        [FromBody] ConsumeEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.TenantId)
            || string.IsNullOrWhiteSpace(request.OneTimeToken)
            || string.IsNullOrWhiteSpace(request.WorkloadPublicKeyThumbprint))
        {
            return BadRequest(new { error = "The enrollment token is not valid." });
        }

        try
        {
            var enrollment = await enrollmentStore.ConsumeAsync(
                request.TenantId.Trim(),
                request.OneTimeToken,
                request.WorkloadPublicKeyThumbprint.Trim(),
                cancellationToken);
            return Ok(new
            {
                tenantId = enrollment.TenantId,
                gatewayId = enrollment.GatewayId,
                workloadPublicKeyThumbprint = enrollment.WorkloadPublicKeyThumbprint
            });
        }
        catch (GatewayEnrollmentException)
        {
            // Unknown, expired, revoked, consumed, and cross-tenant presentations are deliberately
            // indistinguishable. Never echo the token or reveal which refusal occurred.
            return Unauthorized(new { error = "The enrollment token is not valid." });
        }
    }
}
