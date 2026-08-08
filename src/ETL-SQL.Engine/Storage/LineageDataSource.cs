using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Storage
{
    /// <summary>
    /// A virtual data source that exposes lineage tracking data as a queryable table.
    /// Supports filtering by table and column.
    /// </summary>
    public class LineageDataSource : IDataSource
    {
        private readonly ILineageTracker _tracker;
        private readonly string? _targetTable;
        private readonly string? _targetColumn;
        private readonly List<string> _columns = new()
        {
            "step", "timestamp", "operation", "target_table", "target_physical", "target_column",
            "source_tables", "source_physical", "source_columns", "description", "metadata",
            "derived_from_descriptions", "source_file", "line", "column",
            "transformation_kind", "transformation_expression", "functions_applied"
        };

        public string Path => "LINEAGE";

        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "LINEAGE";

        public LineageDataSource(ILineageTracker tracker, string? targetTable = null, string? targetColumn = null)
        {
            _tracker = tracker;
            _targetTable = targetTable;
            _targetColumn = targetColumn;
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ReadBatches(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<LineageEntry> entries;
            if (!string.IsNullOrEmpty(_targetTable))
            {
                entries = _tracker.GetAncestors(_targetTable, _targetColumn);
            }
            else
            {
                entries = _tracker.GetFullLineage();
            }

            // One movement can be observed twice — static analysis records it at parse time and the
            // engine records it again as it executes, at a different source position. Both are
            // legitimate entries (hover locates the cursor by position), but as a chain to walk
            // they are the same hop, so collapse them here and keep the better-described one.
            var deduped = entries
                .GroupBy(e => (
                    Target: e.TargetTable.ToLowerInvariant(),
                    Column: e.TargetColumn?.ToLowerInvariant(),
                    Operation: e.Operation.ToLowerInvariant(),
                    Sources: string.Join("|", e.SourceTables).ToLowerInvariant(),
                    SourceColumns: string.Join("|", e.SourceColumns).ToLowerInvariant()))
                .Select(g => g
                    .OrderByDescending(e => e.TargetTablePhysical != null || e.SourceTablesPhysical.Any(p => p != null))
                    .ThenByDescending(e => e.TransformationKind != ETL_SQL.Core.TransformationKind.Unknown)
                    .ThenByDescending(e => e.Metadata.Count)
                    .First())
                .ToList();

            // Order origin-first so the result reads as a walkable chain rather than as the order
            // the entries happened to be recorded in.
            var steps = ETL_SQL.Core.LineageTracker.ComputeChainSteps(deduped);
            var ordered = deduped
                .OrderBy(e => steps.TryGetValue(e, out var s) ? s : 1)
                .ThenBy(e => e.Timestamp)
                .ToList();

            var rows = new List<Row>();
            foreach (var entry in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new Row();
                row["step"] = (decimal)(steps.TryGetValue(entry, out var step) ? step : 1);
                row["timestamp"] = entry.Timestamp;
                row["operation"] = entry.Operation;
                row["target_table"] = entry.TargetTable;
                row["target_physical"] = entry.TargetTablePhysical;
                row["target_column"] = entry.TargetColumn;
                row["source_tables"] = string.Join(", ", entry.SourceTables);
                row["source_physical"] = entry.SourceTablesPhysical.Any(p => p != null)
                    ? string.Join(", ", Enumerable.Range(0, entry.SourceTables.Count).Select(entry.SourceTableDisplay))
                    : null;
                row["source_columns"] = string.Join(", ", entry.SourceColumns);
                row["description"] = entry.Description;
                row["metadata"] = System.Text.Json.JsonSerializer.Serialize(entry.Metadata);
                row["derived_from_descriptions"] = entry.DerivedFromDescriptions;
                row["source_file"] = entry.SourceFile;
                row["line"] = entry.Line;
                row["column"] = entry.Column;
                row["transformation_kind"] = entry.TransformationKind == ETL_SQL.Core.TransformationKind.Unknown ? null : entry.TransformationKind.ToString();
                row["transformation_expression"] = entry.TransformationExpression;
                row["functions_applied"] = entry.FunctionsApplied != null ? string.Join(", ", entry.FunctionsApplied) : null;
                rows.Add(row);

                if (rows.Count >= batchSize)
                {
                    var dt = new DataTable();
                    dt.SetColumns(_columns);
                    foreach (var r in rows) await dt.AddRowAsync(r);
                    yield return dt;
                    rows = new List<Row>();
                }
            }

            if (rows.Count > 0)
            {
                var dt = new DataTable();
                dt.SetColumns(_columns);
                foreach (var r in rows) await dt.AddRowAsync(r);
                yield return dt;
            }

            await Task.CompletedTask;
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Lineage data is read-only.");

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)_columns);

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public IDataSource WithTable(string tableName) => this;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
