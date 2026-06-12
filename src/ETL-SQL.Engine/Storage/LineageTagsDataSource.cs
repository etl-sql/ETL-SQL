using System;
using System.Collections.Generic;
using System.Linq;
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
            "TargetTable", "TargetColumn", "Operation",
            "TagName", "TagValue", "Scope",
            "Line", "SourceFile"
        };

        public string Path => "LINEAGE_TAGS";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "LINEAGE_TAGS";

        public LineageTagsDataSource(ILineageTracker tracker)
        {
            _tracker = tracker;
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            var rows = new List<Row>();

            foreach (var entry in _tracker.GetFullLineage())
            {
                if (entry.Metadata == null || entry.Metadata.Count == 0) continue;

                string scope = entry.TargetColumn != null ? "column" : "table";

                foreach (var kv in entry.Metadata)
                {
                    var row = new Row();
                    row["TargetTable"] = entry.TargetTable;
                    row["TargetColumn"] = entry.TargetColumn;
                    row["Operation"] = entry.Operation;
                    row["TagName"] = kv.Key;
                    row["TagValue"] = kv.Value;
                    row["Scope"] = scope;
                    row["Line"] = entry.Line;
                    row["SourceFile"] = entry.SourceFile;
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
            => throw new NotSupportedException("LINEAGE_TAGS is read-only.");

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)_columns);

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
