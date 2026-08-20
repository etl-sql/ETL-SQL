using System.Security.Claims;
using System.Security.Cryptography;
using ETL_SQL.Core.Governance;
using ETL_SQL.Gateway;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Administrative lifecycle for Gateway enrollments (SaaS Tenant Isolation §11.3).
///
/// <para>Allows tenant administrators to issue one-time enrollment tokens, inspect enrollment states,
/// list live cluster nodes, and revoke Gateways. The enrollment secret token is returned only once at creation time and never
/// stored in plaintext or logged.</para>
/// </summary>
[ApiController]
[Route("api/admin/gateways")]
[Authorize(Roles = "Admin")]
[RequirePortalModule("ConnectionCatalog")]
public sealed class GatewayEnrollmentController(
    IGatewayEnrollmentStore enrollmentStore,
    AuditService audit,
    RequestTenantContextAccessor tenantAccessor,
    GatewaySessionRegistry? gatewayRegistry = null) : ControllerBase
{
    public sealed record IssueEnrollmentRequest(
        string GatewayId,
        int? ExpirationMinutes = null);

    public sealed record GatewayEnrollmentDto(
        string EnrollmentId,
        string GatewayId,
        GatewayEnrollmentState State,
        DateTimeOffset CreatedUtc,
        DateTimeOffset ExpiresUtc,
        DateTimeOffset? ConsumedUtc,
        string? WorkloadPublicKeyThumbprint,
        bool IsOnline = false,
        int ActiveNodes = 0,
        int TotalNodes = 0,
        IReadOnlyList<GatewaySessionInfo>? Nodes = null);

    private string CurrentTenantId =>
        tenantAccessor.Current?.Tenant.Value ?? "default";

    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var enrollments = await enrollmentStore.ListByTenantAsync(CurrentTenantId, ct);
        var activeClusters = gatewayRegistry?.ListActiveClusters(CurrentTenantId) ?? [];
        var clusterMap = activeClusters.ToDictionary(c => c.GatewayId, StringComparer.OrdinalIgnoreCase);

        var list = enrollments.Select(e =>
        {
            clusterMap.TryGetValue(e.GatewayId, out var cluster);
            var isOnline = cluster is { ActiveNodes: > 0 };
            return new GatewayEnrollmentDto(
                e.EnrollmentId,
                e.GatewayId,
                e.State,
                e.CreatedUtc,
                e.ExpiresUtc,
                e.ConsumedUtc,
                e.WorkloadPublicKeyThumbprint,
                IsOnline: isOnline,
                ActiveNodes: cluster?.ActiveNodes ?? 0,
                TotalNodes: cluster?.TotalNodes ?? 0,
                Nodes: cluster?.Nodes ?? []);
        }).ToList();

        return Ok(list);
    }

    [HttpPost("enroll")]
    public async Task<IActionResult> Issue([FromBody] IssueEnrollmentRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.GatewayId))
            return BadRequest(new { error = "GatewayId is required." });

        var tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        var oneTimeToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        var duration = TimeSpan.FromMinutes(request.ExpirationMinutes is > 0 and <= 1440 ? request.ExpirationMinutes.Value : 60);
        var expiresUtc = DateTimeOffset.UtcNow.Add(duration);

        try
        {
            var enrollment = await enrollmentStore.IssueAsync(
                CurrentTenantId,
                request.GatewayId.Trim(),
                oneTimeToken,
                expiresUtc,
                ct);

            await audit.LogAsync(
                userId: CurrentUserId,
                action: "Gateway.EnrollmentIssued",
                resourceType: "Gateway",
                resourceId: request.GatewayId.Trim(),
                detail: $"Issued one-time enrollment for Gateway '{request.GatewayId.Trim()}'. Expires at {expiresUtc:u}.");

            return Ok(new
            {
                enrollmentId = enrollment.EnrollmentId,
                gatewayId = enrollment.GatewayId,
                tenantId = enrollment.TenantId,
                oneTimeToken = oneTimeToken, // Only returned once upon generation
                expiresUtc = enrollment.ExpiresUtc
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{gatewayId}")]
    public async Task<IActionResult> Get(string gatewayId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gatewayId))
            return BadRequest(new { error = "GatewayId is required." });

        var enrollment = await enrollmentStore.FindByGatewayAsync(CurrentTenantId, gatewayId.Trim(), ct);
        if (enrollment is null)
            return NotFound(new { error = $"Gateway '{gatewayId}' has no enrollment record." });

        return Ok(new GatewayEnrollmentDto(
            enrollment.EnrollmentId,
            enrollment.GatewayId,
            enrollment.State,
            enrollment.CreatedUtc,
            enrollment.ExpiresUtc,
            enrollment.ConsumedUtc,
            enrollment.WorkloadPublicKeyThumbprint));
    }

    [HttpPost("{gatewayId}/revoke")]
    public async Task<IActionResult> Revoke(string gatewayId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gatewayId))
            return BadRequest(new { error = "GatewayId is required." });

        await enrollmentStore.RevokeAsync(CurrentTenantId, gatewayId.Trim(), ct);

        await audit.LogAsync(
            userId: CurrentUserId,
            action: "Gateway.Revoked",
            resourceType: "Gateway",
            resourceId: gatewayId.Trim(),
            detail: $"Revoked Gateway '{gatewayId.Trim()}'.");

        return Ok(new { success = true, gatewayId = gatewayId.Trim() });
    }
}
