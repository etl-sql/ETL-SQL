using System.Security.Claims;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/admin/gateway-operations/ambiguous-writes")]
[Authorize(Roles = "Admin,OrchestratorManager")]
public sealed class GatewayAmbiguousWritesController(
    GatewayAmbiguousWriteService cases,
    RequestTenantContextAccessor tenantAccessor,
    AuditService audit,
    PortalConfig config) : ControllerBase
{
    public sealed record CaseNoteRequest(long Version, string? Note = null);
    public sealed record AssignCaseRequest(long Version, string Owner, string? Note = null);
    public sealed record EvidenceRequest(long Version, string? Note, string? EvidenceReference);
    public sealed record ResolveCaseRequest(
        long Version, string Resolution, string? Note, string? EvidenceReference);

    private string TenantId => tenantAccessor.Current?.Tenant.Value
        ?? (string.IsNullOrWhiteSpace(config.TenantId) ? "default" : config.TenantId);
    private string Actor => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "operator";
    private int? UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeResolved, CancellationToken cancellationToken) =>
        Ok(await cases.ListAsync(TenantId, includeResolved, cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var item = await cases.GetAsync(TenantId, id, cancellationToken);
        return item is null ? NotFound(new { error = $"Ambiguous-write case {id} was not found." }) : Ok(item);
    }

    [HttpPost("{id:long}/acknowledge")]
    public Task<IActionResult> Acknowledge(long id, [FromBody] CaseNoteRequest request,
        CancellationToken cancellationToken) =>
        MutateAsync(id, "GATEWAY_AMBIGUOUS_WRITE_ACKNOWLEDGED", () =>
            cases.AcknowledgeAsync(TenantId, id, request.Version, Actor, request.Note, cancellationToken));

    [HttpPost("{id:long}/assign")]
    public Task<IActionResult> Assign(long id, [FromBody] AssignCaseRequest request,
        CancellationToken cancellationToken) =>
        MutateAsync(id, "GATEWAY_AMBIGUOUS_WRITE_ASSIGNED", () =>
            cases.AssignAsync(TenantId, id, request.Version, Actor, request.Owner, request.Note,
                cancellationToken));

    [HttpPost("{id:long}/evidence")]
    public Task<IActionResult> AddEvidence(long id, [FromBody] EvidenceRequest request,
        CancellationToken cancellationToken) =>
        MutateAsync(id, "GATEWAY_AMBIGUOUS_WRITE_EVIDENCE_ADDED", () =>
            cases.AddEvidenceAsync(TenantId, id, request.Version, Actor, request.Note,
                request.EvidenceReference, cancellationToken));

    [HttpPost("{id:long}/resolve")]
    public Task<IActionResult> Resolve(long id, [FromBody] ResolveCaseRequest request,
        CancellationToken cancellationToken) =>
        MutateAsync(id, "GATEWAY_AMBIGUOUS_WRITE_RESOLVED", () =>
            cases.ResolveAsync(TenantId, id, request.Version, Actor, request.Resolution, request.Note,
                request.EvidenceReference, cancellationToken));

    private async Task<IActionResult> MutateAsync(
        long id,
        string action,
        Func<Task<GatewayAmbiguousWriteCaseDto>> mutate)
    {
        try
        {
            var item = await mutate();
            await audit.LogAsync(UserId, action, "GatewayAmbiguousWrite", item.OperationId,
                $"CaseId={id}; State={item.State}; Resolution={item.Resolution ?? "(none)"}");
            return Ok(item);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
