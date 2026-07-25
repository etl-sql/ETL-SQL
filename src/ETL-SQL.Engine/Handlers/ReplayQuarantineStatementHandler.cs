using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles the v2 quarantine replay preflight. The full replay executor will reuse this manifest
/// resolution and released-row scan before taking a replay lease and resuming at the recorded label.
/// </summary>
public class ReplayQuarantineStatementHandler(ILogger logger) : IStatementHandler
{
    public Type SupportedStatementType => typeof(ReplayQuarantineStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ReplayQuarantineStatement)statement;
        if (string.IsNullOrWhiteSpace(context.JobName))
            throw new ExecutionException(
                "REPLAY QUARANTINE requires an orchestrator job name in the execution context.",
                null, stmt.Line, stmt.Column);

        var provider = context.JobMetrics;
        if (provider == null)
            throw new ExecutionException(
                "REPLAY QUARANTINE requires orchestrator quarantine manifest history.",
                null, stmt.Line, stmt.Column);

        var target = stmt.QuarantineTable.ToSql();
        var manifest = await provider.GetQuarantineReplayManifestAsync(
            context.JobName,
            stmt.QuarantineTable.TableName,
            context.CancellationToken);
        if (manifest == null)
            throw new ExecutionException(
                $"No quarantine replay manifest was found for job '{context.JobName}' and target '{target}'.",
                null, stmt.Line, stmt.Column);

        if (!manifest.IsReplayable)
            throw new ExecutionException(
                $"Quarantine target '{target}' is not replayable: {manifest.NonReplayableReason ?? "no reason recorded"}.",
                null, stmt.Line, stmt.Column);

        var releasedRows = await CountReleasedRowsAsync(context, stmt.QuarantineTable);
        var result = BuildResult(manifest, releasedRows);
        context.LastResult = result;
        context.LastResultSets.Add(result);
        context.OnResultSet?.Invoke(result);

        logger.Info(
            "REPLAY QUARANTINE preflight for '{Target}': {ReleasedRows} released row(s) ready for section '{SectionLabel}'.",
            target,
            releasedRows,
            manifest.SectionLabel);
    }

    private static async Task<long> CountReleasedRowsAsync(IExecutionContext context, TableReference table)
    {
        long count = 0;
        var source = await context.ResolveDataSourceAsync(table);
        await foreach (var batch in source.ReadBatches(context.EffectiveBatchSize, context.CancellationToken))
        {
            foreach (var row in batch.Rows)
            {
                if (string.Equals(
                    row[DataQualityColumns.Status]?.ToString(),
                    DataQualityColumns.ReleasedStatus,
                    StringComparison.OrdinalIgnoreCase))
                    count++;
            }
        }
        return count;
    }

    private static DataTable BuildResult(Core.Data.QuarantineReplayManifest manifest, long releasedRows)
    {
        var table = new DataTable();
        table.AddColumn("JobName");
        table.AddColumn("ScriptPath");
        table.AddColumn("SectionLabel");
        table.AddColumn("SourceTable");
        table.AddColumn("QuarantineTarget");
        table.AddColumn("ReleasedRows");
        table.AddColumn("InputSchemaFingerprint");
        table.AddColumn("Status");

        var row = new Row();
        row["JobName"] = manifest.JobName;
        row["ScriptPath"] = manifest.ScriptPath;
        row["SectionLabel"] = manifest.SectionLabel;
        row["SourceTable"] = manifest.SourceTable;
        row["QuarantineTarget"] = manifest.QuarantineTarget;
        row["ReleasedRows"] = releasedRows;
        row["InputSchemaFingerprint"] = manifest.InputSchemaFingerprint;
        row["Status"] = "ready";
        table.Rows.Add(row);
        return table;
    }
}
