using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Orchestrator.Service;

public static partial class JobApiEndpoints
{
    private static void MapJobToolApproval(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/scheduled-jobs/{name}/tool-approval", async (
            string name, SavedJobToolApprovalRequest request, HttpContext ctx, IConfiguration cfg, IJobHistoryStore jobs) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            var caller = RequestCaller(ctx);
            if (ProjectToolPrincipal(caller) is not { WithinExecutionScope: true }
                || !OrchestratorObjectAuthorizationService.WithinScopeCeiling(caller, OrchestratorObjectPermission.Manage)
                || !TryResolveRequestTenant(ctx, cfg, out var tenant) || tenant is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (request.Approved is not { } approved)
                return Results.BadRequest(new { Error = "Approved is required." });

            var version = ReadExpectedVersion(ctx);
            if (version is null)
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);

            var job = await jobs.GetJobAsync(tenant, Uri.UnescapeDataString(name));
            if (job is null) return Results.NotFound();
            if (!string.Equals(job.CreatedBy, caller.PrincipalKey, StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (job.JobType != JobTargetKind.Script || job.TargetPath is not null)
                return Results.BadRequest(new { Error = "Tool approval requires a saved inline script." });
            if (job.Version != version || jobs is not IJobToolApprovalStore approvals
                || !await approvals.SetToolApprovalAsync(job, approved, ctx.RequestAborted))
                return Results.Conflict(new { Error = "The saved job changed before approval was recorded." });

            var action = approved ? "JOB_TOOL_APPROVAL_SET" : "JOB_TOOL_APPROVAL_REVOKED";
            EmitObjectMutationAudit(caller, $"JOB:{job.Name}", action, $"Saved job version {job.Version}.", ctx.TraceIdentifier);
            return Results.NoContent();
        }).WithName("setScheduledJobToolApproval");
    }
}
