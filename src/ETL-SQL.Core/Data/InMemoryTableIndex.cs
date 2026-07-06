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

    public object? GetRowKey(IEnumerable<string> columns, Row row)
    {
        var sortedCols = columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        if (sortedCols.Count == 1) return row[sortedCols[0]];

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

        if (isUnique)
        {
            var keys = _uniqueKeys[indexKey];
            foreach (var row in batch.Rows)
            {
                var value = GetRowKey(cols, row);
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
            var val = GetRowKey(cols, row);
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
            foreach (var row in batch.Rows)
            {
                var value = GetRowKey(columns, row);
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
    public void RenameIndex(string oldName, string newName)
    {
        if (_indexes.TryGetValue(oldName, out var index))
        {
            _indexes.Remove(oldName);
            _indexes[newName] = index;
        }
        if (_indexColumnMap.TryGetValue(oldName, out var columns))
        {
            _indexColumnMap.Remove(oldName);
            _indexColumnMap[newName] = columns;
        }
        if (_uniqueColumns.Contains(oldName))
        {
            _uniqueColumns.Remove(oldName);
            _uniqueColumns.Add(newName);
        }
        if (_uniqueKeys.TryGetValue(oldName, out var keys))
        {
            _uniqueKeys.Remove(oldName);
            _uniqueKeys[newName] = keys;
        }
        if (_uniqueKeyBytes.TryGetValue(oldName, out var keyBytes))
        {
            _uniqueKeyBytes.Remove(oldName);
            _uniqueKeyBytes[newName] = keyBytes;
        }
    }

    private static long EstimateKeyBytes(object key) => key switch
    {
        CompositeKey composite => 32L + composite.EstimateHeapBytes(),
        _ => 32L + Row.EstimateValueBytes(key)
    };
}
