using System;
using System.Collections.Generic;
using System.Linq;

using ETL_SQL.Common;

namespace ETL_SQL.Data
{
    // Represents a row in our tabular data
    /// <summary>
    /// Represents a single row of tabular data in a <see cref="DataTable"/>.
    /// Contains a dictionary of column names to values.
    /// </summary>
    public class Row
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, object?> Columns { get; } = new System.Collections.Concurrent.ConcurrentDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        public object? this[string columnName]
        {
            get => Columns.TryGetValue(columnName, out var value) ? value : null;
            set => Columns[columnName] = value;
        }

        public bool HasColumn(string columnName) => Columns.ContainsKey(columnName);

        public Row Clone()
        {
            var row = new Row();
            foreach (var kvp in Columns)
            {
                row.Columns[kvp.Key] = kvp.Value;
            }
            return row;
        }
    }

    // Represents tabular data in memory
    /// <summary>
    /// Represents a set of tabular data in memory.
    /// Used for passing data between engine components and rendering results.
    /// </summary>
    public class DataTable
    {
        public List<Row> Rows { get; } = new List<Row>();
        public List<string> ColumnNames { get; } = new List<string>();
        public long ExecutionTimeMs { get; set; }
        public int TotalRowsMatched { get; set; }
        public int ResultSetIndex { get; set; }

        private readonly HashSet<string> _columnNameSet = new(StringComparer.OrdinalIgnoreCase);

        public void SetColumns(IEnumerable<string> columns)
        {
            ColumnNames.Clear();
            _columnNameSet.Clear();
            foreach (var col in columns)
            {
                if (_columnNameSet.Add(col))
                {
                    ColumnNames.Add(col);
                }
            }
        }

        public void AddColumn(string columnName)
        {
            if (_columnNameSet.Add(columnName))
            {
                ColumnNames.Add(columnName);
            }
        }

        public void RemoveColumn(string columnName)
        {
            if (_columnNameSet.Remove(columnName))
            {
                ColumnNames.RemoveAll(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void RenameColumn(string oldName, string newName)
        {
            if (_columnNameSet.Remove(oldName))
            {
                _columnNameSet.Add(newName);
                for (int i = 0; i < ColumnNames.Count; i++)
                {
                    if (ColumnNames[i].Equals(oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        ColumnNames[i] = newName;
                        break;
                    }
                }
            }
        }

        // A quick helper to create a table from a list of rows
        public void AddRow(Row row)
        {
            Rows.Add(row);
            
            // Ensure _columnNameSet is in sync with ColumnNames if it was modified externally
            if (_columnNameSet.Count == 0 && ColumnNames.Count > 0)
            {
                foreach (var col in ColumnNames) _columnNameSet.Add(col);
            }

            foreach (var key in row.Columns.Keys)
            {
                if (_columnNameSet.Add(key))
                {
                    ColumnNames.Add(key);
                }
            }
        }

        public DataTable Clone()
        {
            var dt = new DataTable();
            dt.ColumnNames.AddRange(ColumnNames);
            foreach (var row in Rows) dt.AddRow(row.Clone());
            return dt;
        }
    }
}
