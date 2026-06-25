using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Data;
/// <summary>
/// Type of table constraint.
/// </summary>
public enum ConstraintType { PrimaryKey, Unique, Check, ForeignKey }

/// <summary>
/// Represents a constraint on a table.
/// </summary>
public class TableConstraintInfo
{
    public string? Name { get; set; }
    public ConstraintType Type { get; set; }
    public List<string> Columns { get; set; } = new();
    public ETL_SQL.Core.Expression? Expression { get; set; }
    public ETL_SQL.Core.ForeignKeyReference? ForeignKey { get; set; }
}

/// <summary>
/// Represents the schema of a <see cref="DataTable"/>.
/// Maps column names to indices for fast array-based access in <see cref="Row"/>.
/// </summary>
public class TableSchema
{
    private readonly Dictionary<string, int> _columnToIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _columnNames = new();
    public List<TableConstraintInfo> Constraints { get; } = new();

    public TableSchema(IEnumerable<string> columns, IEnumerable<TableConstraintInfo>? constraints = null)
    {
        foreach (var col in columns) AddColumn(col);
        if (constraints != null) Constraints.AddRange(constraints);
    }

    public TableSchema() { }

    public IReadOnlyList<string> ColumnNames => _columnNames;

    public int AddColumn(string name)
    {
        int index = _columnNames.Count;
        _columnNames.Add(name);
        _columnToIndex.TryAdd(name, index);
        return index;
    }

    public int GetIndex(string name) => _columnToIndex.TryGetValue(name, out var index) ? index : -1;

    public string GetName(int index) => index >= 0 && index < _columnNames.Count ? _columnNames[index] : string.Empty;

    public int ColumnCount => _columnNames.Count;

    public bool Contains(string name) => _columnToIndex.ContainsKey(name);

    public void RemoveColumn(string name)
    {
        if (!_columnToIndex.Remove(name, out var removedAt)) return;
        _columnNames.RemoveAt(removedAt);
        // Only update indices for columns that came after the removed one — avoids full clear+rebuild.
        foreach (var key in _columnToIndex.Keys.ToList())
        {
            if (_columnToIndex[key] > removedAt)
                _columnToIndex[key]--;
        }
    }

    /// <summary>
    /// Removes multiple columns in one pass, rebuilding the index a single time.
    /// Prefer this over repeated <see cref="RemoveColumn"/> calls when dropping many columns.
    /// </summary>
    public void RemoveColumns(IReadOnlyCollection<string> names)
    {
        if (names.Count == 0) return;
        var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var n in nameSet) _columnToIndex.Remove(n);
        _columnNames.RemoveAll(n => nameSet.Contains(n));
        for (int i = 0; i < _columnNames.Count; i++) _columnToIndex[_columnNames[i]] = i;
    }

    /// <summary>
    /// Adds a secondary name for an existing column slot without allocating a new value position.
    /// The alias is visible to <see cref="GetIndex"/> but not to <see cref="ColumnCount"/>,
    /// <see cref="GetName"/>, or row iteration. Used to expose qualified names (e.g. <c>t6.d6</c>)
    /// alongside bare canonical names so lookups succeed without duplicating values.
    /// </summary>
    public void AddAlias(string aliasName, int canonicalIndex)
    {
        if (canonicalIndex >= 0 && canonicalIndex < _columnNames.Count)
            _columnToIndex.TryAdd(aliasName, canonicalIndex);
    }

    /// <summary>
    /// Copies alias entries (names that map to a different canonical name at the same index)
    /// into <paramref name="target"/>, remapping by canonical name. Call after canonical columns
    /// are added to the target so qualified lookups continue to resolve in combined rows.
    /// </summary>
    public void CopyAliasesTo(TableSchema target)
    {
        foreach (var kvp in _columnToIndex)
        {
            string name = kvp.Key;
            int idx = kvp.Value;
            if (!string.Equals(_columnNames[idx], name, StringComparison.OrdinalIgnoreCase))
            {
                int targetIdx = target.GetIndex(_columnNames[idx]);
                if (targetIdx >= 0) target.AddAlias(name, targetIdx);
            }
        }
    }

    /// <summary>
    /// Returns all alias names registered for a canonical column, i.e., names that resolve
    /// to the same slot but are not the primary canonical name stored in ColumnNames.
    /// </summary>
    public IEnumerable<string> EnumerateAliasesOf(string canonicalName)
    {
        if (!_columnToIndex.TryGetValue(canonicalName, out var idx)) yield break;
        foreach (var kvp in _columnToIndex)
            if (kvp.Value == idx && !string.Equals(kvp.Key, canonicalName, StringComparison.OrdinalIgnoreCase))
                yield return kvp.Key;
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
    private TableSchema? _schema;
    private object?[]? _values;
    private Dictionary<string, object?>? _dynamicColumns;
    public TableSchema? Schema => _schema;

    /// <summary>
    /// A shared empty row with no schema or values. Use when an expression evaluator
    /// requires a row context but no column bindings are needed (e.g., evaluating
    /// literal start/end/step values in FOR loops, WHILE conditions, etc.).
    /// </summary>
    public static readonly Row Empty = new();

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

    public object? this[int index]
    {
        get => _values != null && index >= 0 && index < _values.Length ? _values[index] : null;
        set
        {
            EnsureValuesCapacity(index + 1);
            _values![index] = value;
        }
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

    /// <summary>
    /// Gets the value of a column by name without allocating a dictionary.
    /// Returns <see langword="false"/> if the column does not exist.
    /// </summary>
    public bool TryGetValue(string columnName, out object? value)
    {
        if (_schema != null)
        {
            int index = _schema.GetIndex(columnName);
            if (index >= 0)
            {
                value = _values != null && index < _values.Length ? _values[index] : null;
                return true;
            }
        }
        if (_dynamicColumns != null && _dynamicColumns.TryGetValue(columnName, out value))
            return true;
        value = null;
        return false;
    }

    /// <summary>
    /// Returns an enumerable of all column names (both schema-defined and dynamic).
    /// Optimized to avoid creating a full dictionary copy.
    /// </summary>
    public IEnumerable<string> GetColumnNames()
    {
        if (_schema != null)
        {
            foreach (var name in _schema.ColumnNames) yield return name;
        }

        if (_dynamicColumns != null)
        {
            foreach (var name in _dynamicColumns.Keys) yield return name;
        }
    }

    /// <summary>
    /// Invokes <paramref name="action"/> for each column name/value pair without allocating a dictionary.
    /// Prefer this over <see cref="Columns"/> in hot-path code (joins, projections).
    /// </summary>
    public void ForEachColumn(Action<string, object?> action)
    {
        if (_schema != null && _values != null)
        {
            int count = Math.Min(_schema.ColumnCount, _values.Length);
            for (int i = 0; i < count; i++)
                action(_schema.GetName(i), _values[i]);
        }
        if (_dynamicColumns != null)
        {
            foreach (var kvp in _dynamicColumns) action(kvp.Key, kvp.Value);
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
            var oldOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Min(oldSchema.ColumnCount, oldValues.Length); i++)
            {
                var name = oldSchema.GetName(i);
                if (!oldOccurrences.TryGetValue(name, out int occurrence))
                {
                    occurrence = 0;
                }
                oldOccurrences[name] = occurrence + 1;

                int newIdx = FindOccurrenceIndex(_schema, name, occurrence);
                if (newIdx >= 0)
                {
                    _values[newIdx] = oldValues[i];
                }
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

    private static int FindOccurrenceIndex(TableSchema schema, string columnName, int occurrenceIndex)
    {
        int currentOccurrence = 0;
        for (int i = 0; i < schema.ColumnCount; i++)
        {
            if (string.Equals(schema.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                if (currentOccurrence == occurrenceIndex)
                {
                    return i;
                }
                currentOccurrence++;
            }
        }
        return -1;
    }

    public Row Clone()
    {
        Row row;
        if (_schema != null)
        {
            var newValues = _values != null ? (object?[])_values.Clone() : null;
            row = new Row(_schema, newValues!);
        }
        else
        {
            row = new Row();
        }

        if (_dynamicColumns != null)
        {
            foreach (var kvp in _dynamicColumns) row[kvp.Key] = kvp.Value;
        }
        return row;
    }

    public override string ToString()
    {
        return string.Join(", ", Columns.Select(kv => $"{kv.Key}: {kv.Value ?? "NULL"}"));
    }
}

/// <summary>
/// Represents a set of tabular data in memory.
/// Optimized for batch processing with a shared <see cref="TableSchema"/>.
/// </summary>
public class DataTable
{
    public TableSchema Schema { get; set; } = new(Enumerable.Empty<string>());
    public List<Row> Rows { get; } = new();

    public List<string> ColumnNames => Schema.ColumnNames.ToList();

    public long ExecutionTimeMs { get; set; }
    public int RowsAffected { get; set; } = -1; // -1 means not applicable/unknown
    public int TotalRowsMatched { get; set; }
    public int ResultSetIndex { get; set; }
    public bool IsCapped { get; set; }

    public IDataValidator? Validator { get; set; }

    private readonly Dictionary<TableConstraintInfo, HashSet<object>> _constraintCaches = new();

    public void SetColumns(IEnumerable<string> columns, IEnumerable<TableConstraintInfo>? constraints = null)
    {
        Schema = new TableSchema(columns, constraints);
        _constraintCaches.Clear();
    }

    public void Clear()
    {
        Rows.Clear();
        _constraintCaches.Clear();
    }

    public void AddColumn(string columnName)
    {
        Schema.AddColumn(columnName);
    }

    public void RemoveColumn(string columnName)
    {
        Schema.RemoveColumn(columnName);
    }

    public void RemoveColumns(IReadOnlyCollection<string> columnNames)
    {
        Schema.RemoveColumns(columnNames);
    }

    public void RenameColumn(string oldName, string newName)
    {
        Schema.RenameColumn(oldName, newName);
    }

    public async System.Threading.Tasks.Task AddRowAsync(Row row)
    {
        PrepareRowForAdd(row);

        foreach (var constraint in Schema.Constraints)
        {
            if (constraint.Type == ConstraintType.Check && Validator != null && constraint.Expression != null)
            {
                if (!await Validator.ValidateCheckConstraint(constraint.Expression, row))
                {
                    throw new Core.Common.Exceptions.ExecutionException($"Check constraint violation: {constraint.Name ?? "unnamed"}");
                }
            }
            else if (constraint.Type == ConstraintType.ForeignKey && Validator != null && constraint.ForeignKey != null)
            {
                if (!await Validator.ValidateForeignKey(constraint.ForeignKey, constraint.Columns, row))
                {
                    var vals = string.Join(", ", constraint.Columns.Select(c => row[c]?.ToString() ?? "NULL"));
                    throw new Core.Common.Exceptions.ExecutionException($"Foreign key violation: {constraint.Name ?? "unnamed"} (values: {vals})");
                }
            }
        }

        ValidateSynchronousConstraints(row);
        IncrementRowsAdded();
        Rows.Add(row);
    }

    [Obsolete("Use AddRowAsync to ensure all constraints (CHECK, FOREIGN KEY) are validated.")]
    public void AddRow(Row row)
    {
        PrepareRowForAdd(row);

        if (Schema.Constraints.Any(RequiresAsyncValidation))
        {
            throw new Core.Common.Exceptions.ExecutionException(
                "DataTable.AddRow cannot validate CHECK or FOREIGN KEY constraints synchronously. Use AddRowAsync.");
        }

        ValidateSynchronousConstraints(row);
        IncrementRowsAdded();
        Rows.Add(row);
    }

    private void PrepareRowForAdd(Row row)
    {
        if (row.Schema == null) row.SetSchema(Schema);
        else if (row.Schema != Schema) row.SetSchema(Schema);

    }

    private static void IncrementRowsAdded()
    {
        ETL_SQL.Core.Common.ExecutionNode.Current.Value?.IncrementRows();
    }

    private void ValidateSynchronousConstraints(Row row)
    {
        foreach (var constraint in Schema.Constraints)
        {
            if (constraint.Type != ConstraintType.PrimaryKey && constraint.Type != ConstraintType.Unique)
                continue;

            if (!_constraintCaches.TryGetValue(constraint, out var cache))
            {
                cache = new HashSet<object>(new RowEqualityComparer(constraint.Columns, Schema));
                _constraintCaches[constraint] = cache;
                foreach (var r in Rows) cache.Add(r);
            }

            if (!cache.Add(row))
            {
                var vals = string.Join(", ", constraint.Columns.Select(c => row[c]?.ToString() ?? "NULL"));
                throw new Core.Common.Exceptions.ExecutionException($"Unique constraint violation: {constraint.Name ?? "unnamed"} (values: {vals})");
            }
        }
    }

    private bool RequiresAsyncValidation(TableConstraintInfo constraint) =>
        Validator != null
        && ((constraint.Type == ConstraintType.Check && constraint.Expression != null)
            || (constraint.Type == ConstraintType.ForeignKey && constraint.ForeignKey != null));

    private class RowEqualityComparer : IEqualityComparer<object>
    {
        private readonly List<int> _colIndices;
        public RowEqualityComparer(List<string> columns, TableSchema schema)
        {
            _colIndices = columns.Select(c => schema.GetIndex(c)).ToList();
        }

        public new bool Equals(object? x, object? y)
        {
            if (x is not Row rx || y is not Row ry) return object.Equals(x, y);
            foreach (var idx in _colIndices)
            {
                if (!object.Equals(rx[idx], ry[idx])) return false;
            }
            return true;
        }

        public int GetHashCode(object obj)
        {
            if (obj is not Row r) return obj.GetHashCode();
            var hash = new HashCode();
            foreach (var idx in _colIndices) hash.Add(r[idx]);
            return hash.ToHashCode();
        }
    }


    public Row NewRow() => new(Schema);

    public DataTable Clone()
    {
        var dt = new DataTable { Schema = Schema };
        foreach (var row in Rows) dt.Rows.Add(row.Clone());
        return dt;
    }
}
