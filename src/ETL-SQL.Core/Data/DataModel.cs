using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Data
{
    /// <summary>
    /// Represents the schema of a <see cref="DataTable"/>.
    /// Maps column names to indices for fast array-based access in <see cref="Row"/>.
    /// </summary>
    public class TableSchema
    {
        private readonly Dictionary<string, int> _columnToIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _columnNames = new();

        public IReadOnlyList<string> ColumnNames => _columnNames;

        public TableSchema() { }

        public TableSchema(IEnumerable<string> columns)
        {
            foreach (var col in columns) AddColumn(col);
        }

        public int AddColumn(string name)
        {
            if (_columnToIndex.TryGetValue(name, out var index)) return index;
            index = _columnNames.Count;
            _columnNames.Add(name);
            _columnToIndex[name] = index;
            return index;
        }

        public int GetIndex(string name) => _columnToIndex.TryGetValue(name, out var index) ? index : -1;

        public string GetName(int index) => index >= 0 && index < _columnNames.Count ? _columnNames[index] : string.Empty;

        public int ColumnCount => _columnNames.Count;

        public bool Contains(string name) => _columnToIndex.ContainsKey(name);

        public void RemoveColumn(string name)
        {
            if (!_columnToIndex.Remove(name, out var index)) return;
            _columnNames.RemoveAt(index);
            // Rebuild the index map because indices have shifted
            _columnToIndex.Clear();
            for (int i = 0; i < _columnNames.Count; i++) _columnToIndex[_columnNames[i]] = i;
        }

        public void RenameColumn(string oldName, string newName)
        {
            if (!_columnToIndex.Remove(oldName, out var index)) return;
            _columnNames[index] = newName;
            _columnToIndex[newName] = index;
        }
    }

    /// <summary>
    /// Represents a single row of tabular data.
    /// Optimized for performance by using array-based storage when a <see cref="TableSchema"/> is provided.
    /// </summary>
    public class Row
    {
        private object?[]? _values;
        private Dictionary<string, object?>? _dynamicColumns;
        private TableSchema? _schema;

        public Row() { }

        public Row(TableSchema schema)
        {
            _schema = schema;
            _values = new object?[schema.ColumnCount];
        }

        public Row(TableSchema schema, object?[] values)
        {
            _schema = schema;
            _values = values;
        }

        /// <summary>
        /// Gets or sets the value of a column by name.
        /// </summary>
        public object? this[string columnName]
        {
            get
            {
                if (_schema != null)
                {
                    int index = _schema.GetIndex(columnName);
                    if (index >= 0) return _values != null && index < _values.Length ? _values[index] : null;
                }
                return _dynamicColumns != null && _dynamicColumns.TryGetValue(columnName, out var value) ? value : null;
            }
            set
            {
                if (_schema != null)
                {
                    int index = _schema.GetIndex(columnName);
                    if (index >= 0)
                    {
                        EnsureValuesCapacity(index + 1);
                        _values![index] = value;
                        return;
                    }
                }
                
                _dynamicColumns ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                _dynamicColumns[columnName] = value;
            }
        }

        /// <summary>
        /// Gets or sets the value of a column by index (only for schema-bound rows).
        /// </summary>
        public object? this[int index]
        {
            get => _values != null && index >= 0 && index < _values.Length ? _values[index] : null;
            set
            {
                EnsureValuesCapacity(index + 1);
                _values![index] = value;
            }
        }

        private void EnsureValuesCapacity(int capacity)
        {
            if (_values == null)
            {
                _values = new object?[capacity];
            }
            else if (capacity > _values.Length)
            {
                Array.Resize(ref _values, Math.Max(capacity, _values.Length * 2));
            }
        }

        public bool HasColumn(string columnName)
        {
            if (_schema != null && _schema.Contains(columnName)) return true;
            return _dynamicColumns != null && _dynamicColumns.ContainsKey(columnName);
        }

        public Dictionary<string, object?> Columns
        {
            get
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (_schema != null && _values != null)
                {
                    for (int i = 0; i < Math.Min(_schema.ColumnCount, _values.Length); i++)
                    {
                        dict[_schema.GetName(i)] = _values[i];
                    }
                }
                if (_dynamicColumns != null)
                {
                    foreach (var kvp in _dynamicColumns) dict[kvp.Key] = kvp.Value;
                }
                return dict;
            }
        }

        public Row Clone()
        {
            if (_schema != null)
            {
                var newValues = _values != null ? (object?[])_values.Clone() : null;
                var row = new Row(_schema, newValues!);
                if (_dynamicColumns != null)
                {
                    foreach (var kvp in _dynamicColumns) row[kvp.Key] = kvp.Value;
                }
                return row;
            }
            else
            {
                var row = new Row();
                if (_dynamicColumns != null)
                {
                    foreach (var kvp in _dynamicColumns) row[kvp.Key] = kvp.Value;
                }
                return row;
            }
        }

        internal void SetSchema(TableSchema schema)
        {
            if (_schema == schema) return;
            
            var oldSchema = _schema;
            var oldValues = _values;
            _schema = schema;
            _values = new object?[schema.ColumnCount];

            // 1. Migrate values from old schema if it existed
            if (oldSchema != null && oldValues != null)
            {
                for (int i = 0; i < Math.Min(oldSchema.ColumnCount, oldValues.Length); i++)
                {
                    var name = oldSchema.GetName(i);
                    int newIdx = _schema.GetIndex(name);
                    if (newIdx >= 0) _values[newIdx] = oldValues[i];
                    else
                    {
                        // Move to dynamic columns if no longer in schema
                        _dynamicColumns ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        _dynamicColumns[name] = oldValues[i];
                    }
                }
            }

            // 2. Migrate values from dynamic columns that are now included in the schema
            if (_dynamicColumns != null)
            {
                var keysToRemove = new List<string>();
                foreach (var kvp in _dynamicColumns)
                {
                    int idx = _schema.GetIndex(kvp.Key);
                    if (idx >= 0)
                    {
                        _values[idx] = kvp.Value;
                        keysToRemove.Add(kvp.Key);
                    }
                }
                foreach (var k in keysToRemove) _dynamicColumns.Remove(k);
                if (_dynamicColumns.Count == 0) _dynamicColumns = null;
            }
        }
    }

    /// <summary>
    /// Represents a set of tabular data in memory.
    /// Optimized for batch processing with a shared <see cref="TableSchema"/>.
    /// </summary>
    public class DataTable
    {
        public List<Row> Rows { get; } = new();
        public TableSchema Schema { get; private set; } = new();
        
        public List<string> ColumnNames => Schema.ColumnNames.ToList();
        
        public long ExecutionTimeMs { get; set; }
        public int TotalRowsMatched { get; set; }
        public int ResultSetIndex { get; set; }

        public void SetColumns(IEnumerable<string> columns)
        {
            Schema = new TableSchema(columns);
        }

        public void AddColumn(string columnName)
        {
            Schema.AddColumn(columnName);
        }

        public void RemoveColumn(string columnName)
        {
            Schema.RemoveColumn(columnName);
        }

        public void RenameColumn(string oldName, string newName)
        {
            Schema.RenameColumn(oldName, newName);
        }

        public void AddRow(Row row)
        {
            row.SetSchema(Schema);
            Rows.Add(row);
        }

        public Row NewRow() => new(Schema);

        public DataTable Clone()
        {
            var dt = new DataTable { Schema = Schema };
            foreach (var row in Rows) dt.Rows.Add(row.Clone());
            return dt;
        }
    }
}
