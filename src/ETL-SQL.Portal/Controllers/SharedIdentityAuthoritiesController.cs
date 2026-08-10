using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

/// <summary>Tenant-admin surface for Shared host/domain/issuer routing. Bindings never expose a
/// client-secret reference or value.</summary>
[ApiController]
[Route("api/admin/identity/authorities")]
[Authorize(Roles = "Admin")]
public sealed class SharedIdentityAuthoritiesController(
    PortalConfig config,
    IServiceProvider services,
    AuditService audit) : ControllerBase
{
    private SharedIdentityAuthorityService AuthorityService =>
        services.GetRequiredService<SharedIdentityAuthorityService>();

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!config.SharedTenancy.Enabled) return NotFound();
        return Ok(await AuthorityService.ListAsync(ct));
    }

    [HttpPut("{authorityId}")]
    public async Task<IActionResult> Set(
        string authorityId,
        [FromBody] SharedIdentityAuthorityDefinition definition,
        CancellationToken ct)
    {
        if (!config.SharedTenancy.Enabled) return NotFound();
        try
        {
            var binding = await AuthorityService.SetAsync(authorityId, definition, ct);
            await audit.LogAsync(null, "SET_SHARED_IDENTITY_AUTHORITY", "IdentityAuthority", authorityId,
                $"Host={binding.PortalHost}; Issuer={binding.Issuer}; Enabled={definition.Enabled}");
            return Ok(binding);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{authorityId}/disable")]
    public async Task<IActionResult> Disable(string authorityId, CancellationToken ct)
    {
        if (!config.SharedTenancy.Enabled) return NotFound();
        try
        {
            await AuthorityService.DisableAsync(authorityId, ct);
            await audit.LogAsync(null, "DISABLE_SHARED_IDENTITY_AUTHORITY", "IdentityAuthority", authorityId);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
