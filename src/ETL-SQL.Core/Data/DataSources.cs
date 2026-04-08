using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;

using System.Threading;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Data
{
    public struct CompositeKey : IEquatable<CompositeKey>
    {
        private readonly object?[] _values;
        private readonly int _hashCode;

        public CompositeKey(object?[] values)
        {
            _values = values;
            var hash = new HashCode();
            foreach (var v in values) hash.Add(v);
            _hashCode = hash.ToHashCode();
        }

        public bool Equals(CompositeKey other)
        {
            if (_values.Length != other._values.Length) return false;
            for (int i = 0; i < _values.Length; i++)
            {
                if (!object.Equals(_values[i], other._values[i])) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is CompositeKey other && Equals(other);
        public override int GetHashCode() => _hashCode;
    }

    /// <summary>
    /// Defines methods for validating row-level constraints (CHECK, FOREIGN KEY).
    /// </summary>
    public interface IDataValidator
    {
        /// <summary>Validates a check constraint expression against a row.</summary>
        Task<bool> ValidateCheckConstraint(Expression expression, Row row);
        /// <summary>Validates that a foreign key reference exists in the target table.</summary>
        Task<bool> ValidateForeignKey(ForeignKeyReference reference, List<string> sourceColumns, Row row);
    }

    public interface ITransactionalDataSource : IDataSource
    {
        Task BeginTransactionAsync();
        Task CommitAsync();
        Task RollbackAsync();
    }

    /// <summary>
    /// Base interface for all data sources (Files, SQL Databases, In-Memory).
    /// </summary>
    public interface IDataSource : IAsyncDisposable
    {
        /// <summary>Streams the data source content in batches.</summary>
        IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000);
        /// <summary>Writes batches of data into the data source.</summary>
        Task WriteBatches(IAsyncEnumerable<DataTable> batches);
        /// <summary>Removes all data from the data source.</summary>
        Task TruncateAsync() => throw new NotSupportedException($"TRUNCATE is not supported for {GetType().Name}");
        /// <summary>Returns the list of column names in the data source.</summary>
        Task<IEnumerable<string>> GetColumnsAsync();
        /// <summary>Creates a state snapshot of the data source for transaction support.</summary>
        object? Snapshot();
        /// <summary>Restores the data source to a previous state snapshot.</summary>
        void Restore(object? snapshot);
        /// <summary>Returns a new data source instance scoped to a specific table.</summary>
        IDataSource WithTable(string tableName);
        /// <summary>The physical or logical path to the data source.</summary>
        string Path { get; }
        /// <summary>The options used to create this data source.</summary>
        Dictionary<string, string>? Options { get; }
        /// <summary>The type name of the connector that created this data source (e.g., MSSQL, FLATFILE).</summary>
        string ConnectorType { get; }
        /// <summary>Returns the list of tables in the data source (for multi-table sources).</summary>
        Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
    }

    public interface IDatabaseSource : IDataSource
    {
        Task<string> GetVersionAsync();
        HashSet<string> GetSupportedFunctions();
        IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null);
        string ConnectionString { get; }
        string Dialect { get; }
        Task<IEnumerable<string>> GetViewsAsync();
        Task<IEnumerable<string>> GetColumnsAsync(string tableName);
        /// <summary>
        /// True when this connector can execute arbitrary SQL natively (SQL Server, Postgres, etc.).
        /// False for file-based connectors (FlatFile, JSON, XML) that only support full-table reads.
        /// </summary>
        bool SupportsSqlPushdown { get; }
    }

    /// <summary>
    /// Represents an in-memory data store with indexing and constraint validation support.
    /// Used for temporary tables, MOCKDB, and intermediate query results.
    /// </summary>
    public class InMemoryDataSource : IDataSource
    {
        private readonly List<DataTable> _batches = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        public string Path => "";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "INMEMORY";
        private readonly List<string> _columnOrder = new();
        public Dictionary<string, ColumnDefinition> Schema { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<object, List<Row>>> _indexes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _indexColumnMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _uniqueColumns = new(StringComparer.OrdinalIgnoreCase);
        public List<TableConstraint> TableConstraints { get; private set; } = new();
        public IDataValidator? Validator { get; set; }

        public async Task ValidateRow(Row row)
        {
            foreach (var kv in Schema)
            {
                var col = kv.Value;
                var val = row[col.ColumnName];

                // 1. NOT NULL
                if (!col.IsNullable && (val == null || val == DBNull.Value))
                    throw new ExecutionException($"Column {col.ColumnName} does not allow nulls.");

                // 2. Column-level CHECK
                if (col.CheckConstraint != null && Validator != null)
                {
                    if (!await Validator.ValidateCheckConstraint(col.CheckConstraint, row))
                        throw new ExecutionException($"Check constraint violation on column {col.ColumnName}");
                }

                // 3. Column-level FK
                if (col.ForeignKey != null && Validator != null)
                {
                    if (!await Validator.ValidateForeignKey(col.ForeignKey, new List<string> { col.ColumnName }, row))
                        throw new ExecutionException($"Foreign key violation on column {col.ColumnName} (value: {val})");
                }

                // 4. Column-level Unique
                if (col.IsUnique)
                {
                    if (IsDuplicate(new List<string> { col.ColumnName }, row))
                        throw new ExecutionException($"Unique constraint violation on column {col.ColumnName} (value: {val})");
                }
            }

            // 5. Table-level Constraints
            foreach (var tc in TableConstraints)
            {
                if (tc is TableCheckConstraint c && Validator != null)
                {
                    if (!await Validator.ValidateCheckConstraint(c.Expression, row))
                        throw new ExecutionException($"Check constraint violation: {tc.ConstraintName ?? "unnamed"}");
                }
                else if (tc is TableForeignKeyConstraint fk && Validator != null)
                {
                    if (!await Validator.ValidateForeignKey(fk.Reference, fk.Columns, row))
                    {
                        var vals = string.Join(", ", fk.Columns.Select(col => row[col]?.ToString() ?? "NULL"));
                        throw new ExecutionException($"Foreign key violation: {tc.ConstraintName ?? "unnamed"} (values: {vals})");
                    }
                }
                else if (tc is TablePrimaryKeyConstraint pk)
                {
                    foreach (var colName in pk.Columns)
                    {
                        var val = row[colName];
                        if (val == null || val == DBNull.Value)
                            throw new ExecutionException($"Primary key column {colName} cannot be null.");
                    }
                    if (IsDuplicate(pk.Columns, row))
                        throw new ExecutionException($"Primary key violation: {tc.ConstraintName ?? "unnamed"}");
                }
                else if (tc is TableUniqueConstraint uk)
                {
                    if (IsDuplicate(uk.Columns, row))
                        throw new ExecutionException($"Unique constraint violation: {tc.ConstraintName ?? "unnamed"}");
                }
            }
        }

        private bool IsDuplicate(List<string> columns, Row row)
        {
            var indexKey = GetIndexKey(columns);
            if (_indexes.TryGetValue(indexKey, out var index))
            {
                var val = GetRowKey(columns, row);
                if (val != null) return index.ContainsKey(val);
            }

            foreach (var batch in _batches)
            {
                foreach (var r in batch.Rows)
                {
                    if (r == row) continue;
                    bool allMatch = true;
                    foreach (var col in columns)
                    {
                        if (!object.Equals(r[col], row[col])) { allMatch = false; break; }
                    }
                    if (allMatch) return true;
                }
            }
            return false;
        }

        private string GetIndexKey(IEnumerable<string> columns) => string.Join(",", columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

        private object? GetRowKey(IEnumerable<string> columns, Row row)
        {
            var sortedCols = columns.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            if (sortedCols.Count == 1) return row[sortedCols[0]];
            
            var values = new object?[sortedCols.Count];
            for (int i = 0; i < sortedCols.Count; i++)
            {
                values[i] = row[sortedCols[i]];
                if (values[i] == null || values[i] == DBNull.Value) return null; // Composite unique keys usually ignore rows with any NULLs depending on dialect, but here we treat it as "cannot be indexed" for uniqueness if any part is null if we want to follow strict SQL. But for PK it's definitely null.
            }
            return new CompositeKey(values);
        }

        public void SetSchema(IEnumerable<ColumnDefinition> columns, IEnumerable<TableConstraint>? tableConstraints = null)
        {
            Schema.Clear();
            _columnOrder.Clear();
            _indexes.Clear();
            _indexColumnMap.Clear();
            _uniqueColumns.Clear();
            TableConstraints.Clear();

            foreach (var col in columns)
            {
                Schema[col.ColumnName] = col;
                _columnOrder.Add(col.ColumnName);
                if (col.IsPrimaryKey)
                {
                    col.IsNullable = false;
                    _uniqueColumns.Add(col.ColumnName);
                    CreateIndex(col.ColumnName, true);
                }
                else if (col.IsUnique)
                {
                    _uniqueColumns.Add(col.ColumnName);
                    CreateIndex(col.ColumnName, true);
                }
            }

            if (tableConstraints != null)
            {
                TableConstraints.AddRange(tableConstraints);
                foreach (var tc in TableConstraints)
                {
                    if (tc is TablePrimaryKeyConstraint pk)
                    {
                        foreach (var colName in pk.Columns)
                        {
                            if (Schema.TryGetValue(colName, out var col)) col.IsNullable = false;
                        }
                        CreateIndex(pk.Columns, true);
                    }
                    else if (tc is TableUniqueConstraint uk)
                    {
                        CreateIndex(uk.Columns, true);
                    }
                }
            }
        }

        public void AddColumn(ColumnDefinition col)
        {
            if (Schema.ContainsKey(col.ColumnName))
                throw new ExecutionException($"Column {col.ColumnName} already exists.");
            Schema[col.ColumnName] = col;
            _columnOrder.Add(col.ColumnName);
            
            foreach (var batch in _batches)
            {
                batch.AddColumn(col.ColumnName);
                // Note: The rows themselves already handle missing keys as null, 
                // but we could explicitly add them here if we wanted to evaluate defaults for existing data.
            }
        }

        public void DropColumn(string columnName)
        {
            if (!Schema.Remove(columnName))
                throw new ExecutionException($"Column {columnName} not found.");
            _columnOrder.RemoveAll(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            _indexes.Remove(columnName);
            _uniqueColumns.Remove(columnName);

            foreach (var batch in _batches)
            {
                batch.RemoveColumn(columnName);
                // In a high-perf scenario, we don't necessarily need to clear the underlying array storage immediately.
                // It just becomes inaccessible via the schema.
            }
        }

        public void RenameColumn(string oldName, string newName)
        {
            if (!Schema.TryGetValue(oldName, out var colDef))
                throw new ExecutionException($"Column {oldName} not found.");
            if (Schema.ContainsKey(newName))
                throw new ExecutionException($"Column {newName} already exists.");

            var newColDef = new ColumnDefinition(newName, colDef.DataType, colDef.IsIdentity, colDef.DefaultExpression);
            Schema.Remove(oldName);
            Schema[newName] = newColDef;

            for (int i = 0; i < _columnOrder.Count; i++)
            {
                if (_columnOrder[i].Equals(oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _columnOrder[i] = newName;
                    break;
                }
            }

            if (_indexes.TryGetValue(oldName, out var index))
            {
                _indexes.Remove(oldName);
                _indexes[newName] = index;
            }
            if (_uniqueColumns.Contains(oldName))
            {
                _uniqueColumns.Remove(oldName);
                _uniqueColumns.Add(newName);
            }

            foreach (var batch in _batches)
            {
                batch.RenameColumn(oldName, newName);
                // The new schema indices will map to the same slots in the row array.
            }
        }
 
        public async Task TruncateAsync()
        {
            await _lock.WaitAsync();
            try
            {
                _batches.Clear();
                // Clear existing index data while preserving the index definitions
                foreach (var index in _indexes.Values) index.Clear();
            }
            finally
            {
                _lock.Release();
            }
        }

        public void CreateIndex(string columnName, bool isUnique = false)
        {
            CreateIndex(new[] { columnName }, isUnique);
        }

        public void CreateIndex(IEnumerable<string> columns, bool isUnique = false)
        {
            var cols = columns.ToList();
            var indexKey = GetIndexKey(cols);
            _indexColumnMap[indexKey] = cols;
            if (isUnique) _uniqueColumns.Add(indexKey);
            RebuildIndex(cols);
        }

        private void RebuildIndex(string columnName) => RebuildIndex(new[] { columnName });

        private void RebuildIndex(IEnumerable<string> columns)
        {
            var cols = columns.ToList();
            var indexKey = GetIndexKey(cols);
            var index = new Dictionary<object, List<Row>>();
            _indexes[indexKey] = index;
            _indexColumnMap[indexKey] = cols;
            foreach (var batch in _batches)
            {
                UpdateIndexWithBatch(cols, batch);
            }
        }

        private void UpdateIndexWithBatch(string indexKey, DataTable batch)
        {
            if (!_indexColumnMap.TryGetValue(indexKey, out var columns)) return;
            UpdateIndexWithBatch(columns, batch);
        }

        private void UpdateIndexWithBatch(IEnumerable<string> columns, DataTable batch)
        {
            var cols = columns.ToList();
            var indexKey = GetIndexKey(cols);
            if (!_indexes.TryGetValue(indexKey, out var index)) return;
            var isUnique = _uniqueColumns.Contains(indexKey);

            foreach (var row in batch.Rows)
            {
                var val = GetRowKey(cols, row);
                if (val == null) continue;

                if (isUnique && index.ContainsKey(val))
                    throw new ExecutionException($"Unique index violation on columns ({string.Join(",", cols)}) for value {val}");

                if (!index.TryGetValue(val, out var rows))
                {
                    rows = new List<Row>();
                    index[val] = rows;
                }
                rows.Add(row);
            }
        }

        public List<Row>? Lookup(string columnName, object? value)
        {
            if (value == null) return new List<Row>(); 
            if (_indexes.TryGetValue(columnName, out var index))
            {
                if (index.TryGetValue(value, out var rows)) return rows;
                return new List<Row>(); 
            }
            return null; 
        }

        public bool HasIndex(string columnName) => _indexes.ContainsKey(columnName);

        public IDataSource WithTable(string tableName) => this;
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            await _lock.WaitAsync();
            List<DataTable> copy;
            try { copy = _batches.ToList(); }
            finally { _lock.Release(); }
            
            foreach (var batch in copy)
            {
                yield return batch;
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            await foreach (var b in batches)
            {
                await _lock.WaitAsync();
                try
                {
                    foreach (var row in b.Rows) await ValidateRow(row);
                    _batches.Add(b);
                    if (_indexes.Count > 0)
                    {
                        foreach (var col in _indexes.Keys.ToList())
                        {
                            UpdateIndexWithBatch(col, b);
                        }
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
        }

        public async Task<List<Row>> DeleteRows(Func<Row, Task<bool>> predicate)
        {
            await _lock.WaitAsync();
            try
            {
                var deleted = new List<Row>();
                foreach (var batch in _batches)
                {
                    for (int i = batch.Rows.Count - 1; i >= 0; i--)
                    {
                        var row = batch.Rows[i];
                        if (await predicate(row))
                        {
                            batch.Rows.RemoveAt(i);
                            deleted.Add(row);
                        }
                    }
                }
                if (deleted.Count > 0 && _indexes.Count > 0)
                {
                    // Simplest to rebuild indexes for now if rows were deleted
                    foreach(var col in _indexes.Keys.ToList()) RebuildIndex(col);
                }
                return deleted;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<List<(Row Before, Row After)>> UpdateRows(Func<Row, Task<bool>> predicate, Func<Row, Task> updateAction)
        {
            await _lock.WaitAsync();
            try
            {
                var updated = new List<(Row Before, Row After)>();
                foreach (var batch in _batches)
                {
                    foreach (var row in batch.Rows)
                    {
                        if (await predicate(row))
                        {
                            var before = row.Clone();
                            await updateAction(row);
                            updated.Add((before, row));
                        }
                    }
                }
                if (updated.Count > 0 && _indexes.Count > 0)
                {
                    foreach(var col in _indexes.Keys.ToList()) RebuildIndex(col);
                }
                return updated;
            }
            finally
            {
                _lock.Release();
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(_columnOrder.Count > 0 ? (IEnumerable<string>)_columnOrder : (_batches.Any() ? _batches.First().ColumnNames : Enumerable.Empty<string>()));

        public object? Snapshot()
        {
            return _batches.Select(b => b.Clone()).ToList();
        }

        public void Restore(object? snapshot)
        {
            if (snapshot is List<DataTable> s)
            {
                _batches.Clear();
                _batches.AddRange(s);
                if (_indexes.Count > 0)
                {
                    foreach (var col in _indexes.Keys.ToList()) RebuildIndex(col);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _batches.Clear();
            _indexes.Clear();
            _indexColumnMap.Clear();
            await Task.CompletedTask;
        }
    }

    public class StreamingSubqueryDataSource : IDataSource
    {
        private IAsyncEnumerator<DataTable>? _enumerator;
        private List<string>? _columns;
        private DataTable? _firstBatch;
        public string Path => "";
        public Dictionary<string, string>? Options => null;

        public StreamingSubqueryDataSource(IAsyncEnumerable<DataTable> batches)
        {
            _enumerator = batches.GetAsyncEnumerator();
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (_firstBatch != null)
            {
                yield return _firstBatch;
                _firstBatch = null;
            }
            while (_enumerator != null && await _enumerator.MoveNextAsync())
            {
                if (_columns == null) _columns = _enumerator.Current.ColumnNames.ToList();
                yield return _enumerator.Current;
            }
            if (_enumerator != null)
            {
                await _enumerator.DisposeAsync();
                _enumerator = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_enumerator != null)
            {
                await _enumerator.DisposeAsync();
                _enumerator = null;
            }
        }

        public IDataSource WithTable(string tableName) => this;
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => throw new NotSupportedException();
        public string ConnectorType => "STREAMING";
 
        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (_columns != null) return _columns;
            if (_firstBatch == null && _enumerator != null)
            {
                if (await _enumerator.MoveNextAsync())
                {
                    _firstBatch = _enumerator.Current;
                    _columns = _firstBatch.ColumnNames.ToList();
                }
            }
            return _columns ?? Enumerable.Empty<string>();
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
    }
}
