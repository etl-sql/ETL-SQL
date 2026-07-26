using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

/// <summary>
/// Writes captured rows to a quarantine or warn target. The captured row is the
/// <b>pre-projection input row</b> — every input column the statement saw — not the projected
/// output row: that is what makes v2 replay possible (re-feed the row through the statement) and
/// it points stewards at the cause (the source value) rather than the symptom. Rows are augmented
/// with the <see cref="DataQualityColumns"/> set and written in batches with
/// <c>WriteBatches(append: true)</c>.
/// </summary>
internal sealed class QuarantineWriter(
    IExecutionContext context,
    string target,
    string status,
    bool includeTargetWritten)
{
    private const int BatchSize = 500;

    private readonly List<Row> _pending = [];
    private readonly string _runId = context.SessionId ?? Guid.NewGuid().ToString("N");
    private readonly string _captureScope = BuildCaptureScope(context);
    private List<string>? _columns;

    /// <summary>Total rows handed to the target so far.</summary>
    public long RowsWritten { get; private set; }

    public async Task WriteAsync(Row input, ColumnQualityValidator.RowFailure failure, CancellationToken cancellationToken)
    {
        _columns ??= BuildColumnList(input);

        var captured = new DataTable();
        captured.SetColumns(_columns);
        var row = captured.NewRow();

        foreach (var (name, value) in input.Columns)
        {
            if (DataQualityColumns.IsDataQualityColumn(name)) continue; // never re-capture engine columns
            row[name] = value;
        }

        row[DataQualityColumns.Rule] = failure.Rule.Text;
        row[DataQualityColumns.Column] = failure.ColumnName;
        row[DataQualityColumns.Value] = failure.IsPii ? DataQualityReport.PiiMask : failure.Value;
        row[DataQualityColumns.Reason] = failure.Reason;
        row[DataQualityColumns.Timestamp] = DateTime.UtcNow;
        row[DataQualityColumns.RunId] = _runId;
        row[DataQualityColumns.CaptureScope] = _captureScope;
        row[DataQualityColumns.Status] = status;
        row[DataQualityColumns.RowId] = ComputeRowId(input);
        row[DataQualityColumns.OriginRowId] = null; // reserved for v2 replay linkage
        if (includeTargetWritten) row[DataQualityColumns.TargetWritten] = 1m;

        _pending.Add(row);
        if (_pending.Count >= BatchSize) await DrainAsync(cancellationToken);
    }

    /// <summary>Writes any buffered rows and applies retention pruning when configured.</summary>
    public async Task FlushAsync(RetentionInterval? retention, CancellationToken cancellationToken)
    {
        await DrainAsync(cancellationToken);
        if (retention != null) await PruneAsync(retention, cancellationToken);
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0) return;

        var batch = new DataTable();
        batch.SetColumns(_columns!);
        foreach (var row in _pending) await batch.AddRowAsync(row);
        _pending.Clear();

        var destination = await ResolveTargetAsync();
        await destination.WriteBatches(ToAsyncEnumerable(batch), append: true, cancellationToken);
        RowsWritten += batch.Rows.Count;
    }

    /// <summary>
    /// Deletes captured rows older than the retention window. Only rows this engine wrote are
    /// considered (matched on the <c>__dq_ts</c> column); targets that cannot be read back are
    /// skipped with a warning rather than failing the run.
    /// </summary>
    private async Task PruneAsync(RetentionInterval retention, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - retention.ToTimeSpan();
        try
        {
            var destination = await ResolveTargetAsync();
            if (destination is not InMemoryDataSource memory)
            {
                if (destination is IDataQualityRetentionPruner pruner)
                {
                    int pruned = await pruner.PruneDataQualityRowsAsync(
                        DataQualityColumns.Timestamp,
                        cutoff,
                        DataQualityColumns.CaptureScope,
                        _captureScope,
                        cancellationToken);
                    if (pruned > 0)
                        context.Logger.Info(
                            "Data-quality retention pruned {Count} row(s) older than {Retention} from '{Target}'.",
                            pruned,
                            retention.ToString(),
                            target);
                    return;
                }

                context.Logger.Debug(
                    "Data-quality retention ({Retention}) on '{Target}': connector '{Connector}' does not support " +
                    "bounded data-quality retention pruning.", retention.ToString(), target, destination.ConnectorType);
                return;
            }

            // A 'released' row is a steward's pending fix awaiting replay — ageing it out would
            // discard that work silently, so only terminal dispositions prune.
            int removed = memory.RemoveRows(row =>
                row[DataQualityColumns.Timestamp] is DateTime ts
                && ts < cutoff
                && string.Equals(
                    row[DataQualityColumns.CaptureScope]?.ToString(),
                    _captureScope,
                    StringComparison.OrdinalIgnoreCase)
                && IsTerminalDisposition(row[DataQualityColumns.Status]?.ToString()));
            if (removed < 0)
                context.Logger.Debug(
                    "Data-quality retention on '{Target}' did not run: the table has spilled to disk.", target);
            else if (removed > 0)
                context.Logger.Info("Data-quality retention pruned {Count} row(s) older than {Retention} from '{Target}'.",
                    removed, retention.ToString(), target);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Logger.Warning("Data-quality retention pruning on '{Target}' did not run: {Message}", target, ex.Message);
        }
    }

    private async Task<IDataSource> ResolveTargetAsync()
    {
        // #temp targets auto-create on first write, matching INSERT INTO #name behavior.
        if (target.StartsWith('#') && !context.Connections.ContainsKey(target))
            context.Connections[target] = new InMemoryDataSource();

        var destination = await context.ResolveDataSourceAsync(new TableReference(target));
        return destination
            ?? throw new ExecutionException($"Data-quality target '{target}' could not be resolved.");
    }

    private List<string> BuildColumnList(Row input)
    {
        var columns = input.Columns.Keys
            .Where(name => !DataQualityColumns.IsDataQualityColumn(name))
            .ToList();

        columns.Add(DataQualityColumns.Rule);
        columns.Add(DataQualityColumns.Column);
        columns.Add(DataQualityColumns.Value);
        columns.Add(DataQualityColumns.Reason);
        columns.Add(DataQualityColumns.Timestamp);
        columns.Add(DataQualityColumns.RunId);
        columns.Add(DataQualityColumns.CaptureScope);
        columns.Add(DataQualityColumns.Status);
        columns.Add(DataQualityColumns.RowId);
        columns.Add(DataQualityColumns.OriginRowId);
        if (includeTargetWritten) columns.Add(DataQualityColumns.TargetWritten);
        return columns;
    }

    private static string BuildCaptureScope(IExecutionContext executionContext)
    {
        if (!string.IsNullOrWhiteSpace(executionContext.JobName))
            return $"job:{executionContext.JobName.Trim()}";
        if (!string.IsNullOrWhiteSpace(executionContext.CurrentScriptPath))
            return $"script:{executionContext.CurrentScriptPath.Trim()}";
        return $"session:{executionContext.SessionId ?? "interactive"}";
    }

    private static bool IsTerminalDisposition(string? status) =>
        string.Equals(status, DataQualityColumns.WarnedStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, DataQualityColumns.ReplayedStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, DataQualityColumns.DiscardedStatus, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deterministic hash of the captured row's content plus the run id — the stable identity
    /// replay-once semantics key on. Column names are included so two rows with the same values
    /// under different schemas do not collide.
    /// </summary>
    private string ComputeRowId(Row input)
    {
        var builder = new StringBuilder(_runId).Append('\u001f');
        foreach (var (name, value) in input.Columns.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (DataQualityColumns.IsDataQualityColumn(name)) continue;
            builder.Append(name).Append('=')
                .Append(value switch
                {
                    null or DBNull => "\0",
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString()
                })
                .Append('\u001e');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static async IAsyncEnumerable<DataTable> ToAsyncEnumerable(DataTable table)
    {
        yield return table;
        await Task.CompletedTask;
    }
}
