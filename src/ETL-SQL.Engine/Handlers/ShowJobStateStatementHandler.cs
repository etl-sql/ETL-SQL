using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles SHOW JOB STATE [jobName] [INTO #t] — enumerates the saved job-state key/value store
/// (watermarks written by SET_JOB_STATE, e.g. incremental-load positions or backup markers).
/// This is the administrator's cross-job read surface: GET_JOB_STATE only reads the caller's own
/// context and requires knowing the key, while this lists every key for any orchestrator job.
/// CLI-run scripts persist their state to a local .etlstate file instead, which this does not show.
/// </summary>
public class ShowJobStateStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowJobStateStatement);
    private readonly IJobHistoryStore _store;

    public ShowJobStateStatementHandler(IJobHistoryStore store)
    {
        _store = store;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowJobStateStatement)statement;

        var filter = JobId.None;
        if (!string.IsNullOrWhiteSpace(stmt.JobName))
        {
            var job = await _store.GetJobAsync(CatalogStatementSupport.ActingTenant(context), stmt.JobName)
                ?? throw new ETL_SQL.Core.Common.Exceptions.ExecutionException(
                    $"Job '{stmt.JobName}' does not exist.", null, stmt.Line, stmt.Column);
            filter = job.Id;
        }

        var entries = await _store.GetJobStatesAsync(filter);

        var table = new DataTable();
        table.AddColumn("JobName");
        table.AddColumn("StateKey");
        table.AddColumn("StateValue");
        table.AddColumn("UpdatedAt");

        foreach (var e in entries)
        {
            var row = new Row();
            row["JobName"] = e.JobName;
            row["StateKey"] = e.StateKey;
            row["StateValue"] = e.StateValue;
            row["UpdatedAt"] = e.UpdatedAt;
            await table.AddRowAsync(row);
        }

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
            {
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            }
            var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
        }
        else
        {
            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }
}
