using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the KILL JOB statement by invoking the IJobManager.
/// </summary>
public class KillJobStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;

    public Type SupportedStatementType => typeof(KillJobStatement);

    public KillJobStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (KillJobStatement)statement;

        var jobIdObj = await context.EvaluationContext.EvaluateValue(stmt.JobIdExpr, new Row());
        if (jobIdObj == null || !long.TryParse(jobIdObj.ToString(), out long historyId))
        {
            throw new ExecutionException($"KILL JOB requires a valid numeric HistoryId. Got: {jobIdObj ?? "NULL"}");
        }

        var historyStore = context.ServiceProvider.GetService<IJobHistoryStore>();
        string? killedJobName = null;
        if (historyStore is not null && await historyStore.GetHistoryEntryAsync(historyId) is { } history)
        {
            var job = await historyStore.GetJobAsync(CatalogStatementSupport.ActingTenant(context), history.JobName);
            if (job is not null)
            {
                await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Job,
                    job.Name, job.Id, job.TenantId,
                    OrchestratorObjectPermission.Manage, job.CreatedBy);
                killedJobName = job.Name;
            }
        }

        _logger.Info("Attempting to kill job with HistoryId: {HistoryId}", historyId);

        var jobManager = context.ServiceProvider.GetService<IJobManager>();
        if (jobManager == null)
        {
            _logger.Warning("No IJobManager registered in the current context. KILL JOB ignored.");
            return;
        }

        bool killed = jobManager.KillJob(historyId);
        if (killed)
        {
            _logger.Info("Successfully sent cancellation request for job {HistoryId}.", historyId);
            // Targeted at the run, matching the HTTP resume audit, because a KILL names a run rather
            // than a definition. The job name rides in the reason when the run resolved to one; a
            // history id that resolves to nothing is still worth recording as an attempt that landed.
            CatalogStatementSupport.AuditMutation(
                context,
                "KILL_JOB",
                $"JOB_RUN:{historyId}",
                killedJobName is null
                    ? $"Run {historyId} cancelled."
                    : $"Run {historyId} of job '{killedJobName}' cancelled.");
        }
        else
        {
            _logger.Warning("Job {HistoryId} was not found or is not currently running.", historyId);
        }
    }
}
