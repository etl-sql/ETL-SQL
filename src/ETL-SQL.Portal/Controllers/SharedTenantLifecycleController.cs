using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

/// <summary>
/// Platform-only Shared tenant lifecycle endpoint. It is intentionally outside tenant JWT/RBAC:
/// the platform management credential authenticates the host caller, while a live signed policy
/// independently authorizes exactly one tenant operation and attributes the human operator.
/// </summary>
[ApiController]
[Route("api/platform/shared-tenants")]
[AllowAnonymous]
public sealed class SharedTenantLifecycleController(
    SharedTenantLifecycleService lifecycle,
    PortalConfig config) : ControllerBase
{
    public const string ManagementKeyHeader = "X-Portal-Platform-Key";
    public sealed record LifecycleRequest(string? TenantId, bool Execute = false);

    [HttpPost("{kind}")]
    public async Task<IActionResult> Apply(
        string kind,
        [FromBody] LifecycleRequest request,
        CancellationToken cancellationToken)
    {
        if (!config.SharedTenancy.Enabled
            || string.IsNullOrWhiteSpace(config.SharedTenancy.LifecycleManagementKey))
            return NotFound();
        if (!ManagementKeyAccepted(Request, config.SharedTenancy.LifecycleManagementKey))
            return Unauthorized();
        if (!Enum.TryParse<SharedTenantLifecycleKind>(kind, true, out var operationKind))
            return BadRequest(new { error = "Lifecycle kind must be provision, upgrade, or delete." });

        try
        {
            var now = DateTimeOffset.UtcNow;
            var authority = SharedTenantLifecycleService.ResolveAuthority(
                operationKind, request.TenantId, EnterprisePolicyRuntime.Current, config, now);
            var result = await lifecycle.ApplyAsync(
                authority, request.Execute, now, cancellationToken);
            return result.Status is "Completed" or "Preflight"
                ? Ok(result)
                : Accepted(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidDataException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    internal static bool ManagementKeyAccepted(HttpRequest request, string configured)
    {
        if (configured.Length < 32
            || !request.Headers.TryGetValue(ManagementKeyHeader, out var supplied)
            || supplied.Count != 1)
            return false;
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()));
        return CryptographicOperations.FixedTimeEquals(configuredHash, suppliedHash);
    }
}
