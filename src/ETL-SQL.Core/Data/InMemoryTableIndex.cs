using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Data;
/// <summary>
/// Encapsulates the indexing logic for an InMemoryDataSource.
/// Manages multiple named indexes (single or composite columns) and uniqueness constraints.
/// </summary>
public class InMemoryTableIndex
{
    private readonly Dictionary<string, Dictionary<object, List<Row>>> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _indexColumnMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _uniqueColumns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<object>> _uniqueKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _uniqueKeyBytes = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _indexColumnMap.Count;
    public int UniqueIndexCount => _uniqueColumns.Count;
    public int NonUniqueIndexCount => Count - UniqueIndexCount;
    public long EstimatedUniqueKeyBytes => _uniqueKeyBytes.Values.Sum();
    public IEnumerable<string> Keys => _indexColumnMap.Keys;

    /// <summary>The column list of every defined index, for callers that must rebuild them all.</summary>
    public IEnumerable<List<string>> IndexedColumnSets => _indexColumnMap.Values.Select(cols => cols.ToList()).ToList();

    public void Clear()
    {
        _indexes.Clear();
        _indexColumnMap.Clear();
        _uniqueColumns.Clear();
        _uniqueKeys.Clear();
        _uniqueKeyBytes.Clear();
    }

    /// <summary>Clears indexed data while retaining index definitions.</summary>
    public void ClearData(bool preserveUniqueKeys = false)
    {
        _indexes.Clear();
        foreach (var indexKey in _indexColumnMap.Keys)
            if (!_uniqueColumns.Contains(indexKey))
                _indexes[indexKey] = new Dictionary<object, List<Row>>();
        if (!preserveUniqueKeys)
        {
            foreach (var keys in _uniqueKeys.Values) keys.Clear();
            foreach (var indexKey in _uniqueKeyBytes.Keys.ToList()) _uniqueKeyBytes[indexKey] = 0;
        }
    }

    public void Remove(string indexKey)
    {
        _indexes.Remove(indexKey);
        _uniqueColumns.Remove(indexKey);
        _uniqueKeys.Remove(indexKey);
        _uniqueKeyBytes.Remove(indexKey);
        _indexColumnMap.Remove(indexKey);
    }

    public bool HasIndex(string indexKey) => _indexColumnMap.ContainsKey(indexKey);

    public void AddIndexDefinition(string indexKey, List<string> columns, bool isUnique)
    {
        _indexColumnMap[indexKey] = columns;
        if (isUnique)
        {
            _uniqueColumns.Add(indexKey);
            _uniqueKeys.TryAdd(indexKey, new HashSet<object>());
            _uniqueKeyBytes.TryAdd(indexKey, 0);
        }
    }

    public void SetIndex(string indexKey, Dictionary<object, List<Row>> index, List<string> columns)
    {
        _indexes[indexKey] = index;
        _indexColumnMap[indexKey] = columns;
    }

    public bool TryGetIndex(string indexKey, out Dictionary<object, List<Row>>? index)
    {
        return _indexes.TryGetValue(indexKey, out index);
    }

    public bool TryGetColumns(string indexKey, out List<string>? columns)
    {
        return _indexColumnMap.TryGetValue(indexKey, out columns);
    }

    public bool IsUnique(string indexKey) => _uniqueColumns.Contains(indexKey);

    public List<Row>? Lookup(string indexKey, object? value)
    {
        if (value == null) return new List<Row>();
        if (_indexes.TryGetValue(indexKey, out var index))
        {
            if (index.TryGetValue(value, out var rows)) return rows;
            return new List<Row>();
        }
        return null;
    }

    public bool ContainsKey(string indexKey, object key)
    {
        if (_uniqueKeys.TryGetValue(indexKey, out var unique)) return unique.Contains(key);
        return _indexes.TryGetValue(indexKey, out var index) && index.ContainsKey(key);
    }

    public string GetIndexKey(IEnumerable<string> columns) => string.Join(",", columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

    public object? GetRowKey(IEnumerable<string> columns, Row row) => RowKey(SortColumns(columns), row);

    private static List<string> SortColumns(IEnumerable<string> columns) =>
        columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The key of one row for columns already in index order. Callers that key a whole batch sort
    /// once and call this per row. A NULL in any key column, as C# null or DBNull, means no key:
    /// NULLs never collide in a unique index.
    /// </summary>
    private static object? RowKey(List<string> sortedCols, Row row)
    {
        if (sortedCols.Count == 1)
        {
            var single = row[sortedCols[0]];
            return single == DBNull.Value ? null : single;
        }

        var values = new object?[sortedCols.Count];
        for (int i = 0; i < sortedCols.Count; i++)
        {
            values[i] = row[sortedCols[i]];
            if (values[i] == null || values[i] == DBNull.Value) return null;
        }
        return new CompositeKey(values);
    }

    public void UpdateIndexWithBatch(IEnumerable<string> columns, DataTable batch)
    {
        var cols = columns.ToList();
        var indexKey = GetIndexKey(cols);
        var isUnique = _uniqueColumns.Contains(indexKey);
        var sorted = SortColumns(cols);

        if (isUnique)
        {
            var keys = _uniqueKeys[indexKey];
            foreach (var row in batch.Rows)
            {
                var value = RowKey(sorted, row);
                if (value != null)
                {
                    if (!keys.Add(value))
                        throw new ExecutionException($"Unique index violation on columns ({string.Join(",", cols)}) for value {value}");
                    _uniqueKeyBytes[indexKey] += EstimateKeyBytes(value);
                }
            }
            return;
        }

        if (!_indexes.TryGetValue(indexKey, out var index)) return;

        foreach (var row in batch.Rows)
        {
            var val = RowKey(sorted, row);
            if (val == null) continue;

            if (!index.TryGetValue(val, out var rows))
            {
                rows = new List<Row>();
                index[val] = rows;
            }
            rows.Add(row);
        }
    }

    public void RemoveUniqueKeysForBatch(DataTable batch)
    {
        foreach (var indexKey in _uniqueColumns)
        {
            if (!_indexColumnMap.TryGetValue(indexKey, out var columns) ||
                !_uniqueKeys.TryGetValue(indexKey, out var keys)) continue;
            var sorted = SortColumns(columns);
            foreach (var row in batch.Rows)
            {
                var value = RowKey(sorted, row);
                if (value != null && keys.Remove(value))
                    _uniqueKeyBytes[indexKey] -= EstimateKeyBytes(value);
            }
        }
    }
    public bool IsDuplicate(IEnumerable<string> columns, Row row, IEnumerable<DataTable> batches)
    {
        var cols = columns.ToList();
        var indexKey = GetIndexKey(cols);
        var val = GetRowKey(cols, row);
        if (val != null && _uniqueKeys.TryGetValue(indexKey, out var uniqueKeys))
            return uniqueKeys.Contains(val);
        if (_indexes.TryGetValue(indexKey, out var index))
        {
            if (val != null) return index.ContainsKey(val);
        }

        foreach (var batch in batches)
        {
            foreach (var r in batch.Rows)
            {
                if (r == row) continue;
                bool allMatch = true;
                foreach (var col in cols)
                {
                    if (!object.Equals(r[col], row[col])) { allMatch = false; break; }
                }
                if (allMatch) return true;
            }
        }
        return false;
    }
    public void RebuildIndex(IEnumerable<string> columns, IEnumerable<DataTable> batches)
    {
        var cols = columns.ToList();
        var indexKey = GetIndexKey(cols);
        _indexColumnMap[indexKey] = cols;
        if (_uniqueColumns.Contains(indexKey))
        {
            _uniqueKeys[indexKey] = new HashSet<object>();
            _uniqueKeyBytes[indexKey] = 0;
        }
        else
            _indexes[indexKey] = new Dictionary<object, List<Row>>();
        foreach (var batch in batches)
        {
            UpdateIndexWithBatch(cols, batch);
        }
    }
    /// <summary>
    /// Renames a column in every index that uses it and returns the column sets of those indexes.
    /// Only the definitions move. A composite key is built in sorted column order, so a rename can
    /// reorder it; the caller rebuilds each returned index from its rows once they carry the new name.
    /// </summary>
    public List<List<string>> RenameColumn(string oldColumn, string newColumn)
    {
        var renamed = new List<List<string>>();
        foreach (var (oldKey, columns) in _indexColumnMap.ToList())
        {
            if (!columns.Contains(oldColumn, StringComparer.OrdinalIgnoreCase)) continue;

            var newColumns = columns
                .Select(c => c.Equals(oldColumn, StringComparison.OrdinalIgnoreCase) ? newColumn : c)
                .ToList();
            var isUnique = _uniqueColumns.Contains(oldKey);
            Remove(oldKey);
            AddIndexDefinition(GetIndexKey(newColumns), newColumns, isUnique);
            renamed.Add(newColumns);
        }
        return renamed;
    }
    private static long EstimateKeyBytes(object key) => key switch
    {
        CompositeKey composite => 32L + composite.EstimateHeapBytes(),
        _ => 32L + Row.EstimateValueBytes(key)
    };
}
