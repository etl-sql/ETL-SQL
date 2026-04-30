using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Execution;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Spill;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Data;

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
        /// <summary>Writes batches of data into the data source. If append is true, existing data is preserved.</summary>
        Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false);
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
        /// <summary>Returns the options used to create this data source, with sensitive values masked.</summary>
        IReadOnlyDictionary<string, string> GetConfig()
        {
            var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Options != null)
            {
                foreach (var kv in Options)
                {
                    bool isSensitive = kv.Key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
                                      kv.Key.Contains("CONNECTIONSTRING", StringComparison.OrdinalIgnoreCase) ||
                                      kv.Key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                                      kv.Key.Contains("APIKEY", StringComparison.OrdinalIgnoreCase) ||
                                      kv.Key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
                                      kv.Key.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase) ||
                                      kv.Key.Contains("PRIVATEKEY", StringComparison.OrdinalIgnoreCase) ||
                                      kv.Value.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase);

                    config[kv.Key] = isSensitive ? "********" : kv.Value;
                }
            }
            return config;
        }

        /// <summary>Checks if a row with matching column values exists in the data source.</summary>
        Task<bool> ExistsAsync(List<string> columns, List<object?> values) => Task.FromResult(false);
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
    public class InMemoryDataSource : IDataSource, ISpillable
    {
        private readonly List<DataTable> _batches = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        public string Path => "";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "INMEMORY";
        private readonly List<string> _columnOrder = new();
        public Dictionary<string, ColumnDefinition> Schema { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly InMemoryTableIndex _index = new();
        public List<TableConstraint> TableConstraints { get; private set; } = new();
        public IDataValidator? Validator { get; set; }

        public int MaxInMemoryBatches { get; set; } = LanguageMetadata.DefaultMaxInMemoryBatches;
        
        private readonly List<string> _spillChunkNames = new();
        public int SpillChunkCount => _spillChunkNames.Count;
        public long SpillTotalBytes { get; private set; } = 0;
        private long _totalRowCount = 0;
        private IExecutionContext? _executionContext;
        public IExecutionContext? ExecutionContext 
        { 
            get => _executionContext;
            set
            {
                if (_executionContext != null)
                {
                    _executionContext.ServiceProvider.GetService<IBufferManager>()?.UnregisterSpillable(this);
                }
                _executionContext = value;
                if (_executionContext != null)
                {
                    _executionContext.ServiceProvider.GetService<IBufferManager>()?.RegisterSpillable(this);
                }
            }
        }

        public long MemoryUsageBytes
        {
            get
            {
                // Simple estimation: 256 bytes per row (overhead + pointers)
                // Plus index overhead
                long batchBytes = _batches.Sum(b => (long)b.Rows.Count * 256);
                long indexBytes = _index.Count * 128L; // Simplified
                // Spilled chunks are ON DISK, so they don't count towards CURRENT RAM USAGE.
                // This is critical for BufferManager to know how much RAM is actually reclaimable.
                return batchBytes + indexBytes;
            }
        }

        public string SpillToken => "InMemoryDataSource_" + (string.IsNullOrEmpty(Path) ? GetHashCode().ToString("X") : Path);

        public async Task<bool> SpillAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_batches.Count == 0 && _index.Count == 0) return false;
                if (ExecutionContext == null) return false;

                // Move all batches to spill store
                foreach (var batch in _batches)
                {
                    var chunkName = $"{Guid.NewGuid():N}.spill";
                    await using (var writer = await ExecutionContext.SpillStore.CreateWriterAsync(chunkName))
                    {
                        await writer.WriteRowsAsync(batch.Rows);
                        SpillTotalBytes += writer.BytesWritten;
                    }
                    _spillChunkNames.Add(chunkName);
                }

                _batches.Clear();
                _index.Clear();
                
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task ValidateRow(Row row)
        {
            foreach (var kv in Schema)
            {
                var col = kv.Value;
                var val = row[col.ColumnName];

                // 0. Type Coercion
                if (val != null && val != DBNull.Value)
                {
                    row[col.ColumnName] = val = TypeConverter.Cast(val, col.DataType);
                }

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
                    if (_index.IsDuplicate(new List<string> { col.ColumnName }, row, _batches))
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
                    if (_index.IsDuplicate(pk.Columns, row, _batches))
                        throw new ExecutionException($"Primary key violation: {tc.ConstraintName ?? "unnamed"}");
                }
                else if (tc is TableUniqueConstraint uk)
                {
                    if (_index.IsDuplicate(uk.Columns, row, _batches))
                        throw new ExecutionException($"Unique constraint violation: {tc.ConstraintName ?? "unnamed"}");
                }
            }
        }

        public void SetSchema(IEnumerable<ColumnDefinition> columns, IEnumerable<TableConstraint>? tableConstraints = null)
        {
            Schema.Clear();
            _columnOrder.Clear();
            _index.Clear();
            TableConstraints.Clear();

            foreach (var col in columns)
            {
                Schema[col.ColumnName] = col;
                _columnOrder.Add(col.ColumnName);
                if (col.IsPrimaryKey)
                {
                    col.IsNullable = false;
                    CreateIndex(col.ColumnName, true);
                }
                else if (col.IsUnique)
                {
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
            _index.Remove(columnName);

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

            _index.RenameIndex(oldName, newName);

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
                _totalRowCount = 0;
                // Clear existing index data while preserving the index definitions
                _index.Clear();

                if (ExecutionContext != null)
                {
                    foreach (var chunk in _spillChunkNames)
                    {
                        ExecutionContext.SpillStore.DeleteChunk(chunk);
                    }
                }
                _spillChunkNames.Clear();
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
            var indexKey = _index.GetIndexKey(cols);
            _index.AddIndexDefinition(indexKey, cols, isUnique);
            _index.RebuildIndex(cols, _batches);
        }

        public List<Row>? Lookup(string columnName, object? value)
        {
            return _index.Lookup(columnName, value);
        }

        public bool HasIndex(string columnName) => _index.HasIndex(columnName);

        public IDataSource WithTable(string tableName) => this;
        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            // 1. Yield from disk spill first (if any)
            List<string> chunks;
            List<DataTable> memoryCopy;
            await _lock.WaitAsync();
            try
            {
                chunks = _spillChunkNames.ToList();
                memoryCopy = _batches.ToList();
            }
            finally { _lock.Release(); }

            if (ExecutionContext != null)
            {
                foreach (var spillName in chunks)
                {
                    if (ExecutionContext.SpillStore == null)
                        throw new ExecutionException("Spill-to-disk operation failed: IExecutionContext.SpillStore is null but spilled data exists.");

                    await using var reader = await ExecutionContext.SpillStore.CreateReaderAsync(spillName);
                    var batch = new DataTable();
                    batch.SetColumns(_columnOrder);
                    
                    await foreach (var row in reader.AsEnumerableAsync())
                    {
                        await batch.AddRowAsync(row);
                        if (batch.Rows.Count >= batchSize)
                        {
                            yield return batch;
                            batch = new DataTable();
                            batch.SetColumns(_columnOrder);
                        }
                    }

                    if (batch.Rows.Count > 0)
                    {
                        yield return batch;
                    }
                }
            }

            // 2. Yield from memory buffer
            foreach (var b in memoryCopy) yield return b;
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            if (!append) await TruncateAsync();
            await foreach (var b in batches)
            {
                if (_columnOrder.Count == 0)
                {
                    _columnOrder.AddRange(b.ColumnNames);
                    foreach (var col in _columnOrder)
                    {
                        if (!Schema.ContainsKey(col))
                            Schema[col] = new ColumnDefinition(col, "UNKNOWN", false);
                    }
                }
                
                await _lock.WaitAsync();
                try
                {
                    foreach (var row in b.Rows) await ValidateRow(row);

                    long threshold = ExecutionContext?.TempTableSpillThresholdRows ?? LanguageMetadata.DefaultTempTableSpillThresholdRows;
                    
                    if (_totalRowCount + b.Rows.Count > threshold)
                    {
                        if (ExecutionContext != null)
                        {
                            var chunkName = $"{Guid.NewGuid():N}.tmp";
                            await using (var writer = await ExecutionContext.SpillStore.CreateWriterAsync(chunkName))
                            {
                                await writer.WriteRowsAsync(b.Rows);
                            }
                            _spillChunkNames.Add(chunkName);
                            _totalRowCount += b.Rows.Count;

                            if (ExecutionContext.Telemetry.IsProfiling)
                                ExecutionContext.LoggingContext.Logger.Debug("Temp table threshold reached ({Threshold} rows). Spilled batch to chunk: {ChunkName}", threshold, chunkName);
                            
                            continue;
                        }
                    }

                    _batches.Add(b);
                    _totalRowCount += b.Rows.Count;
                    
                    if (_index.Count > 0)
                    {
                        foreach (var col in _index.Keys.ToList())
                        {
                            if (_index.TryGetColumns(col, out var cols))
                                _index.UpdateIndexWithBatch(cols!, b);
                        }
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
        }

        public async Task<bool> ExistsAsync(List<string> columns, List<object?> values)
        {
            var key = new CompositeKey(values.ToArray());
            var indexName = string.Join(",", columns);

            await _lock.WaitAsync();
            try
            {
                if (_index.TryGetIndex(indexName, out var index))
                {
                    return index!.ContainsKey(key);
                }

                // If no index, fallback to linear scan
                foreach (var b in _batches)
                {
                    foreach (var r in b.Rows)
                    {
                        bool match = true;
                        for (int i = 0; i < columns.Count; i++)
                        {
                            if (!IsSoftEqual(r[columns[i]], values[i])) { match = false; break; }
                        }
                        if (match) return true;
                    }
                }
                return false;
            }
            finally { _lock.Release(); }
        }

        private bool IsSoftEqual(object? a, object? b)
        {
            if (a == null || a == DBNull.Value) return b == null || b == DBNull.Value;
            if (b == null || b == DBNull.Value) return false;
            if (a.Equals(b)) return true;
            return a.ToString() == b.ToString();
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
                if (deleted.Count > 0 && _index.Count > 0)
                {
                    // Simplest to rebuild indexes for now if rows were deleted
                    foreach(var col in _index.Keys.ToList()) 
                    {
                        if (_index.TryGetColumns(col, out var cols))
                        {
                            _index.RebuildIndex(cols!, _batches);
                        }
                    }
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
                    for (int i = 0; i < batch.Rows.Count; i++)
                    {
                        var row = batch.Rows[i];
                        if (await predicate(row))
                        {
                            var before = row.Clone();
                            var after = row.Clone();
                            
                            // Perform update on the clone to ensure atomicity
                            await updateAction(after);
                            
                            // Swap the row in the batch
                            batch.Rows[i] = after;
                            updated.Add((before, after));
                        }
                    }
                }
                if (updated.Count > 0 && _index.Count > 0)
                {
                    foreach(var col in _index.Keys.ToList()) 
                    {
                        if (_index.TryGetColumns(col, out var cols))
                        {
                            _index.RebuildIndex(cols!, _batches);
                        }
                    }
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
                if (_index.Count > 0)
                {
                    foreach (var col in _index.Keys.ToList()) 
                    {
                        if (_index.TryGetColumns(col, out var cols))
                        {
                            _index.RebuildIndex(cols!, _batches);
                        }
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _batches.Clear();
            _index.Clear();

            if (ExecutionContext != null && !ExecutionContext.IsPersistentSession)
            {
                foreach (var chunk in _spillChunkNames)
                {
                    ExecutionContext.SpillStore.DeleteChunk(chunk);
                }
            }
            _spillChunkNames.Clear();
        }

        public void Rehydrate(IEnumerable<ColumnDefinition> schema, IEnumerable<string> chunks)
        {
            SetSchema(schema);
            _spillChunkNames.Clear();
            _spillChunkNames.AddRange(chunks);
            _totalRowCount = 0; // Will be recalculatable from chunks if needed, but for now we assume recovered
        }

        public IEnumerable<string> GetSpillChunks() => _spillChunkNames;

        public async Task FlushToSpillAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_batches.Count == 0 || ExecutionContext?.SpillStore == null) return;

                foreach (var batch in _batches)
                {
                    var chunkName = $"{Guid.NewGuid():N}.tmp";
                    await using (var writer = await ExecutionContext.SpillStore.CreateWriterAsync(chunkName))
                    {
                        await writer.WriteRowsAsync(batch.Rows);
                    }
                    _spillChunkNames.Add(chunkName);
                }
                _batches.Clear();
                _index.Clear();
            }
            finally { _lock.Release(); }
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
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException();
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
