using System;
using System.Collections.Generic;
using System.Linq;
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
            "Timestamp", "Operation", "TargetTable", "TargetColumn",
            "SourceTables", "SourceColumns", "Description", "Metadata",
            "DerivedFromDescriptions", "SourceFile", "Line", "Column",
            "TransformationKind", "TransformationExpression", "FunctionsApplied"
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

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            IEnumerable<LineageEntry> entries;
            if (!string.IsNullOrEmpty(_targetTable))
            {
                entries = _tracker.GetAncestors(_targetTable, _targetColumn);
            }
            else
            {
                entries = _tracker.GetFullLineage();
            }

            var rows = new List<Row>();
            foreach (var entry in entries)
            {
                var row = new Row();
                row["Timestamp"] = entry.Timestamp;
                row["Operation"] = entry.Operation;
                row["TargetTable"] = entry.TargetTable;
                row["TargetColumn"] = entry.TargetColumn;
                row["SourceTables"] = string.Join(", ", entry.SourceTables);
                row["SourceColumns"] = string.Join(", ", entry.SourceColumns);
                row["Description"] = entry.Description;
                row["Metadata"] = System.Text.Json.JsonSerializer.Serialize(entry.Metadata);
                row["DerivedFromDescriptions"] = entry.DerivedFromDescriptions;
                row["SourceFile"] = entry.SourceFile;
                row["Line"] = entry.Line;
                row["Column"] = entry.Column;
                row["TransformationKind"] = entry.TransformationKind == ETL_SQL.Core.TransformationKind.Unknown ? null : entry.TransformationKind.ToString();
                row["TransformationExpression"] = entry.TransformationExpression;
                row["FunctionsApplied"] = entry.FunctionsApplied != null ? string.Join(", ", entry.FunctionsApplied) : null;
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("Lineage data is read-only.");

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)_columns);

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public IDataSource WithTable(string tableName) => this;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
