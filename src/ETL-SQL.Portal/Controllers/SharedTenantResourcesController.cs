using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/shared/resources/{kind}")]
[Authorize]
public sealed class SharedTenantResourcesController(
    SharedTenantResourceRegistry registry,
    AuditService audit) : ControllerBase
{
    public sealed record RegisterRequest(string? LogicalId);

    [HttpGet]
    public async Task<IActionResult> List(string kind, CancellationToken ct)
    {
        try { return Ok(await registry.ListAsync(kind, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Find(string kind, long id, CancellationToken ct)
    {
        try
        {
            var value = await registry.FindAsync(kind, id, ct);
            return value is null ? NotFound() : Ok(value);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    [HttpGet("by-scope")]
    public async Task<IActionResult> FindScoped(string kind, [FromQuery] string scopedId, CancellationToken ct)
    {
        try
        {
            var value = await registry.FindScopedAsync(kind, scopedId, ct);
            return value is null ? NotFound() : Ok(value);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (UnauthorizedAccessException) { return NotFound(); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Register(string kind, [FromBody] RegisterRequest request, CancellationToken ct)
    {
        try
        {
            var value = await registry.RegisterAsync(kind, request.LogicalId ?? string.Empty, ct);
            await audit.LogAsync(null, "REGISTER_SHARED_TENANT_RESOURCE", kind, value.LogicalId,
                $"scopedId={value.ScopedId}");
            return Ok(value);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string kind, long id, CancellationToken ct)
    {
        try
        {
            if (!await registry.DeleteAsync(kind, id, ct)) return NotFound();
            await audit.LogAsync(null, "DELETE_SHARED_TENANT_RESOURCE", kind, id.ToString());
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException) { return NotFound(); }
    }
}
