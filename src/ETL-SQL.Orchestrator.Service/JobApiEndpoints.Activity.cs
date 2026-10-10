using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Service;

public static partial class JobApiEndpoints
{
    private static void MapScheduledJobActivity(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/scheduled-jobs/activity", async (
            HttpContext ctx, IJobHistoryStore store, OrchestratorObjectAuthorizationService authorization,
            IConfiguration cfg, TimeProvider clock, ILogger<OrchestratorHostedService> logger,
            string filter = "all", string status = "all", string? search = null,
            int limit = 25, int offset = 0) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (!TryResolveRequestTenant(ctx, cfg, out var tenantId))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (filter is not ("all" or "active" or "queued" or "completedToday" or "failedToday")
                || status is not ("all" or "enabled" or "disabled") || limit is < 1 or > 1000 || offset < 0)
                return Results.BadRequest(new { Error = "Invalid activity filter or pagination." });
            if (store is not IScheduledJobActivityStore activityStore)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            try
            {
                var asOfUtc = clock.GetUtcNow();
                var snapshot = await activityStore.GetScheduledJobActivityAsync(tenantId, asOfUtc, ctx.RequestAborted);
                var caller = RequestCaller(ctx);
                var visible = new List<ScheduledJobActivity>();
                foreach (var activity in snapshot)
                {
                    var job = activity.Job;
                    if (await authorization.CanAsync(caller, OrchestratorObjectKind.Job, job.Id, job.TenantId,
                            OrchestratorObjectPermission.Read, job.CreatedBy, ctx.RequestAborted))
                        visible.Add(activity);
                }

                // Counts and membership share the authorized snapshot. Authorization precedes
                // pagination, so hidden rows neither disclose counts nor create holes in a page.
                var counts = new ScheduledJobActivityCounts(
                    visible.Count(a => a.RunState == ScheduledJobRunState.Running),
                    visible.Count(a => a.RunState == ScheduledJobRunState.Queued),
                    visible.Count(a => a.CompletedToday), visible.Count(a => a.FailedToday));
                var query = (search ?? "").Trim();
                var matching = visible.Where(a => MatchesActivityFilter(a, filter)
                    && (status == "all" || a.Job.IsEnabled == (status == "enabled"))
                    && MatchesActivitySearch(a.Job, query)).ToList();
                var dayStart = new DateTimeOffset(asOfUtc.UtcDateTime.Date, TimeSpan.Zero);
                return Results.Ok(new ScheduledJobActivityPage(
                    asOfUtc, dayStart, counts, matching.Count, offset, limit,
                    matching.Skip(offset).Take(limit).ToArray()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduled job activity could not be read.");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("listScheduledJobActivity");
    }

    private static bool MatchesActivityFilter(ScheduledJobActivity activity, string filter) => filter switch
    {
        "active" => activity.RunState == ScheduledJobRunState.Running,
        "queued" => activity.RunState == ScheduledJobRunState.Queued,
        "completedToday" => activity.CompletedToday,
        "failedToday" => activity.FailedToday,
        _ => true
    };

    private static bool MatchesActivitySearch(JobDefinition job, string query) =>
        query.Length == 0 || new[]
        {
            job.Name, job.DisplayName, job.Description, job.TargetPath ?? job.Script,
            $"{job.Interval} {job.Unit} {job.AtTime}", job.CreatedBy
        }.Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
}
