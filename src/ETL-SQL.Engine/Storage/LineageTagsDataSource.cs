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
    /// Virtual data source that exposes lineage metadata as flat tag rows — one row per tag per lineage entry.
    /// Eliminates the need for JSON_VALUE gymnastics on the Metadata column of the LINEAGE table.
    /// Columns: TargetTable, TargetColumn, Operation, TagName, TagValue, Scope, Line, SourceFile.
    /// </summary>
    public class LineageTagsDataSource : IDataSource
    {
        private readonly ILineageTracker _tracker;
        private readonly List<string> _columns = new()
        {
            "target_table", "target_column", "operation",
            "tag_name", "tag_value", "scope",
            "line", "source_file"
        };

        public string Path { get; }
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => Path.Equals("LINEAGE_TAGS", StringComparison.OrdinalIgnoreCase)
            ? "LINEAGE_TAGS"
            : "ENG";

        public LineageTagsDataSource(ILineageTracker tracker, string path = "LINEAGE_TAGS")
        {
            _tracker = tracker;
            Path = path;
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ReadBatches(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = new List<Row>();

            foreach (var kv in _tracker.GlobalMetadata)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new Row();
                row["target_table"] = null;
                row["target_column"] = null;
                row["operation"] = "SCRIPT_TAGS";
                row["tag_name"] = kv.Key;
                row["tag_value"] = kv.Value;
                row["scope"] = "script";
                row["line"] = null;
                row["source_file"] = null;
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

            foreach (var entry in _tracker.GetFullLineage())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Metadata == null || entry.Metadata.Count == 0) continue;

                string scope = entry.TargetColumn != null ? "column" : "table";

                foreach (var kv in entry.Metadata)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = new Row();
                    row["target_table"] = entry.TargetTable;
                    row["target_column"] = entry.TargetColumn;
                    row["operation"] = entry.Operation;
                    row["tag_name"] = kv.Key;
                    row["tag_value"] = kv.Value;
                    row["scope"] = scope;
                    row["line"] = entry.Line;
                    row["source_file"] = entry.SourceFile;
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
            => throw new NotSupportedException($"{Path} is read-only.");

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)_columns);

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
