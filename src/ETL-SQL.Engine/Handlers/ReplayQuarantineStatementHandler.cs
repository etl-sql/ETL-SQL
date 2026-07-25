using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
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
        if (context is not Evaluator evaluator)
            throw new ExecutionException(
                "REPLAY QUARANTINE requires the ETL-SQL engine evaluator replay context.",
                null, stmt.Line, stmt.Column);

        if (evaluator.QuarantineReplayDepth > 0)
        {
            logger.Debug("Skipping nested REPLAY QUARANTINE while a quarantine replay is already active.");
            return;
        }

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

        if (string.IsNullOrWhiteSpace(manifest.SectionLabel))
            throw new ExecutionException(
                $"Quarantine target '{target}' is not replayable: no section label was recorded.",
                null, stmt.Line, stmt.Column);

        var leaseOwner = BuildLeaseOwner(evaluator);
        var leaseAcquired = await provider.TryAcquireQuarantineReplayLeaseAsync(
            context.JobName!,
            manifest.QuarantineTarget,
            leaseOwner,
            TimeSpan.FromMinutes(30),
            context.CancellationToken);
        if (!leaseAcquired)
            throw new ExecutionException(
                $"Quarantine target '{target}' is already being replayed by another owner.",
                null, stmt.Line, stmt.Column);

        long releasedRows = 0;
        try
        {
            var replayScript = ResolveReplayScript(evaluator, manifest.SectionLabel!, stmt);
            var replaySourceResult = await BuildReplaySourceAsync(context, stmt.QuarantineTable, manifest);
            releasedRows = replaySourceResult.ReleasedRows;
            if (releasedRows > 0)
            {
                await ReplayReleasedRowsAsync(evaluator, replayScript, manifest, replaySourceResult.Source);
                await MarkReleasedRowsReplayedAsync(evaluator, stmt);
            }
        }
        finally
        {
            await provider.ReleaseQuarantineReplayLeaseAsync(
                context.JobName!,
                manifest.QuarantineTarget,
                leaseOwner,
                context.CancellationToken);
        }

        var result = BuildResult(manifest, releasedRows, releasedRows > 0 ? "replayed" : "ready");
        context.LastResult = result;
        context.LastResultSets.Add(result);
        context.OnResultSet?.Invoke(result);

        logger.Info(
            "REPLAY QUARANTINE for '{Target}': {ReleasedRows} released row(s) processed for section '{SectionLabel}'.",
            target,
            releasedRows,
            manifest.SectionLabel);
    }

    private static Script ResolveReplayScript(Evaluator evaluator, string sectionLabel, ReplayQuarantineStatement stmt)
    {
        static bool HasLabel(Script? script, string label) =>
            script?.Statements.Any(s => s is SectionLabelStatement section
                && section.LabelName.Equals(label, StringComparison.OrdinalIgnoreCase)) == true;

        if (HasLabel(evaluator.CurrentScript, sectionLabel))
            return evaluator.CurrentScript!;
        if (HasLabel(evaluator.LastReplayCandidateScript, sectionLabel))
            return evaluator.LastReplayCandidateScript!;

        throw new ExecutionException(
            $"Cannot replay quarantine: checkpoint label '{sectionLabel}' is not defined in the active script.",
            null, stmt.Line, stmt.Column);
    }

    private static string BuildLeaseOwner(Evaluator evaluator) =>
        $"{Environment.MachineName}:{Environment.ProcessId}:{evaluator.SessionId ?? "session"}:{Guid.NewGuid():N}";

    private static async Task ReplayReleasedRowsAsync(
        Evaluator evaluator,
        Script replayScript,
        Core.Data.QuarantineReplayManifest manifest,
        IDataSource replaySource)
    {
        var oldIsResuming = evaluator.IsResuming;
        var oldResumeLabel = evaluator.ResumeLabel;
        var oldSectionLabel = evaluator.CurrentSectionLabel;
        var sourceKey = manifest.SourceTable;

        evaluator.ReplaySourceOverrides[sourceKey] = replaySource;
        evaluator.QuarantineReplayDepth++;
        evaluator.IsResuming = true;
        evaluator.ResumeLabel = manifest.SectionLabel;

        try
        {
            await evaluator.Evaluate(replayScript, evaluator.CancellationToken);
        }
        finally
        {
            evaluator.ReplaySourceOverrides.Remove(sourceKey);
            evaluator.QuarantineReplayDepth--;
            evaluator.IsResuming = oldIsResuming;
            evaluator.ResumeLabel = oldResumeLabel;
            evaluator.CurrentSectionLabel = oldSectionLabel;
        }
    }

    private static Task MarkReleasedRowsReplayedAsync(Evaluator evaluator, ReplayQuarantineStatement stmt)
    {
        var update = new UpdateStatement(
            stmt.QuarantineTable,
            [
                new Assignment(
                    DataQualityColumns.Status,
                    new LiteralExpression(DataQualityColumns.ReplayedStatus, TokenType.STRING))
            ],
            new BinaryExpression(
                new IdentifierExpression(DataQualityColumns.Status),
                TokenType.EQUALS,
                new LiteralExpression(DataQualityColumns.ReleasedStatus, TokenType.STRING)))
        {
            Line = stmt.Line,
            Column = stmt.Column
        };

        return evaluator.EvaluateStatement(update, evaluator.CancellationToken);
    }

    private static async Task<(IDataSource Source, long ReleasedRows)> BuildReplaySourceAsync(
        IExecutionContext context,
        TableReference table,
        Core.Data.QuarantineReplayManifest manifest)
    {
        long count = 0;
        var source = await context.ResolveDataSourceAsync(table);
        var replayBatch = new DataTable();
        replayBatch.SetColumns(manifest.InputColumns);

        await foreach (var batch in source.ReadBatches(context.EffectiveBatchSize, context.CancellationToken))
        {
            foreach (var row in batch.Rows)
            {
                if (!string.Equals(
                        row[DataQualityColumns.Status]?.ToString(),
                        DataQualityColumns.ReleasedStatus,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var replayRow = new Row();
                foreach (var column in manifest.InputColumns)
                    replayRow[column] = row[column];
                await replayBatch.AddRowAsync(replayRow);
                count++;
            }
        }

        var replaySource = new InMemoryDataSource
        {
            Validator = context as IDataValidator,
            ExecutionContext = context
        };
        await replaySource.WriteBatches(SingleBatch(replayBatch, context.CancellationToken), append: false, context.CancellationToken);
        return (replaySource, count);
    }

    private static async IAsyncEnumerable<DataTable> SingleBatch(
        DataTable table,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return table;
        await Task.CompletedTask;
    }

    private static DataTable BuildResult(Core.Data.QuarantineReplayManifest manifest, long releasedRows, string status)
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
        row["Status"] = status;
        table.Rows.Add(row);
        return table;
    }
}
