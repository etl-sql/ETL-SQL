using System.Security.Claims;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/admin/workload-identity/approvals")]
[Authorize(Roles = "Admin")]
public sealed class WorkloadIdentityApprovalsController(
    PortalDbContext db,
    PortalConfig config,
    RequestTenantContextAccessor tenantAccessor,
    IWorkloadIdentityApprovalService approvals,
    AuditService audit) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateWorkloadApprovalRequest request, CancellationToken ct)
    {
        var tenantId = config.SharedTenancy.Enabled
            ? tenantAccessor.RequireCurrent().Tenant.Value
            : string.IsNullOrWhiteSpace(config.TenantId) ? "portal-host" : config.TenantId;
        var binding = config.Identity.WorkloadIdentity.Bindings.SingleOrDefault(value =>
            value.Enabled && value.TenantId == tenantId
            && value.Id == request.BindingId && value.Resource == request.Resource
            && value.Operations.Contains(request.Operation, StringComparer.Ordinal));
        if (binding is null || !binding.RequireApproval) return NotFound();
        var account = await db.ServiceAccounts.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ClientId == binding.ServiceAccountClientId && value.TenantId == binding.TenantId, ct);
        if (account is null) return NotFound();
        var approverId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (account.OwnerUserId == approverId) return Forbid();

        var token = approvals.Issue(binding, approverId);
        await audit.LogAsync(approverId, "WORKLOAD_IDENTITY_APPROVED", "ServiceAccount", account.Id,
            $"Binding={binding.Id}; Resource={binding.Resource}; Operation={request.Operation}");
        return Ok(new WorkloadApprovalResponse(token, WorkloadIdentityApprovalService.LifetimeSeconds, binding.Id));
    }
}
