using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Spill;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Data;

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
    public long EstimateHeapBytes()
    {
        long bytes = 32L + (long)_values.Length * IntPtr.Size;
        foreach (var value in _values) bytes += Row.EstimateValueBytes(value);
        return bytes;
    }
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
    Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return BeginTransactionAsync();
    }

    Task CommitAsync();
    Task CommitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CommitAsync();
    }

    Task RollbackAsync();
    Task RollbackAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RollbackAsync();
    }
}

/// <summary>
/// Base interface for all data sources (Files, SQL Databases, In-Memory).
/// </summary>
public interface IDataSource : IAsyncDisposable
{
    /// <summary>Streams the data source content in batches.</summary>
    IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000);
    /// <summary>Streams the data source content in batches and observes cancellation during enumeration.</summary>
    async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var batch in ReadBatches(batchSize).WithCancellation(cancellationToken))
            yield return batch;
    }
    /// <summary>Writes batches of data into the data source. If append is true, existing data is preserved.</summary>
    Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false);
    /// <summary>Writes batches of data and observes cancellation while consuming the source stream.</summary>
    Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
        WriteBatches(ApplyCancellation(batches, cancellationToken), append);
    /// <summary>Removes all data from the data source.</summary>
    Task TruncateAsync() => throw new NotSupportedException($"TRUNCATE is not supported for {GetType().Name}");
    /// <summary>Returns the list of column names in the data source.</summary>
    Task<IEnumerable<string>> GetColumnsAsync();
    /// <summary>Returns the list of column names and observes cancellation before schema resolution.</summary>
    Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetColumnsAsync();
    }
    /// <summary>Creates a state snapshot of the data source for transaction support.</summary>
    object? Snapshot();
    /// <summary>Restores the data source to a previous state snapshot.</summary>
    void Restore(object? snapshot);
    /// <summary>Returns a new data source instance scoped to a specific table.</summary>
    IDataSource WithTable(string tableName);
    /// <summary>The physical or logical path to the data source.</summary>
    string Path { get; }
    /// <summary>Returns a catalog metadata provider for this connection, or <c>null</c> if not supported.</summary>
    ICatalogMetadataProvider? GetCatalogProvider() => null;
    /// <summary>The options used to create this data source.</summary>
    Dictionary<string, string>? Options { get; }
    /// <summary>The type name of the connector that created this data source (e.g., MSSQL, FLATFILE).</summary>
    string ConnectorType { get; }
    /// <summary>Returns the list of tables in the data source (for multi-table sources).</summary>
    Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
    /// <summary>Returns table names and observes cancellation before schema resolution.</summary>
    Task<IEnumerable<string>> GetTablesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetTablesAsync();
    }
    /// <summary>Returns the options used to create this data source, with sensitive values masked.</summary>
    IReadOnlyDictionary<string, string> GetConfig()
    {
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Options != null)
        {
            foreach (var kv in Options)
            {
                config[kv.Key] = SecretRedactor.IsSensitiveKey(kv.Key)
                    || kv.Key.Contains("CONNECTIONSTRING", StringComparison.OrdinalIgnoreCase)
                    || SecretRedactor.LooksSensitiveValue(kv.Value)
                    ? SecretRedactor.Mask
                    : SecretRedactor.Redact(kv.Value) ?? string.Empty;
            }
        }
        return config;
    }

    /// <summary>Checks if a row with matching column values exists in the data source.</summary>
    Task<bool> ExistsAsync(List<string> columns, List<object?> values) => Task.FromResult(false);
    Task<bool> ExistsAsync(List<string> columns, List<object?> values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExistsAsync(columns, values);
    }

    private static async IAsyncEnumerable<DataTable> ApplyCancellation(
        IAsyncEnumerable<DataTable> batches,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var batch in batches.WithCancellation(cancellationToken))
            yield return batch;
    }
}

/// <summary>
/// Optional capability for data sources that can prune engine-owned data-quality capture rows
/// with a bounded connector-side delete.
/// </summary>
public interface IDataQualityRetentionPruner
{
    /// <summary>
    /// Deletes data-quality capture rows older than <paramref name="cutoffUtc"/>.
    /// Implementations MUST NOT delete rows whose disposition is still in flight — a
    /// <c>released</c> row is a steward's pending fix waiting to be replayed, and pruning it
    /// silently discards that work. Only terminal dispositions age out.
    /// </summary>
    Task<int> PruneDataQualityRowsAsync(
        string timestampColumn,
        DateTime cutoffUtc,
        string scopeColumn,
        string scopeValue,
        CancellationToken cancellationToken);
}

/// <summary>Optional row-count estimate used to choose bounded operators before consuming a source.</summary>
public interface IEstimatedCardinalityDataSource
{
    long EstimatedRowCount { get; }
}

/// <summary>
/// Optional exact-count capability for sources that can validate their physical backing store
/// without reconstructing every row. Implementations must consume all persisted data and verify
/// that the physical count agrees with their logical row count before returning.
/// </summary>
public interface IValidatedRowCountDataSource
{
    Task<long> CountRowsValidatedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional fast-path contract for sources that can expose native typed column buffers. Existing
/// <see cref="IDataSource.ReadBatches"/> remains the compatibility path for row-based consumers.
/// </summary>
public interface IColumnarDataSource
{
    /// <summary>
    /// Returns retained native batches. The consumer owns each returned reference and must dispose it.
    /// <paramref name="batchSize"/> is a preferred size; immutable stored segments may be larger.
    /// </summary>
    IAsyncEnumerable<ColumnBatch> ReadColumnBatches(
        int batchSize = 10000,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A native source whose column-batch enumeration can be restarted after a planner probe or
/// memory-pressure rejection without changing the logical result.
/// </summary>
public interface IReplayableColumnarDataSource : IColumnarDataSource;

/// <summary>Optional native append contract paired with <see cref="IColumnarDataSource"/>.</summary>
public interface IColumnarDataSink
{
    /// <summary>
    /// Appends native batches without a row conversion. Ownership of each successfully accepted
    /// batch transfers to the sink.
    /// </summary>
    Task WriteColumnBatches(
        IAsyncEnumerable<ColumnBatch> batches,
        bool append = false,
        CancellationToken cancellationToken = default);
}

public interface IDatabaseSource : IDataSource
{
    Task<string> GetVersionAsync();
    HashSet<string> GetSupportedFunctions();
    IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null);
    async IAsyncEnumerable<DataTable> ExecuteRawSql(
        string sql,
        IEnumerable<object?>? parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var batch in ExecuteRawSql(sql, parameters).WithCancellation(cancellationToken))
            yield return batch;
    }
    string ConnectionString { get; }
    string Dialect { get; }
    Task<IEnumerable<string>> GetViewsAsync();
    Task<IEnumerable<string>> GetViewsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetViewsAsync();
    }

    Task<IEnumerable<string>> GetColumnsAsync(string tableName);
    Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetColumnsAsync(tableName);
    }
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
/// <summary>
/// Parsed <c>INT(n)</c> / <c>INT(n,+)</c> / <c>INT(n,-)</c> digit-and-sign constraint.
/// </summary>
/// <remarks>
/// The declared type is schema-level and identical for every row, but the original implementation
/// ran <c>Regex.Match</c> against it once per column per row. On a 1M-row scan that is millions of
/// matches computing the same answer, and it cost roughly 14% of elapsed time on the certification
/// suite's streaming scenarios. Parsing is now done once per distinct type string and cached.
/// </remarks>
internal readonly record struct IntegerConstraint(int Digits, string Sign)
{
    private static readonly System.Text.RegularExpressions.Regex Pattern = new(
        @"^(\w+)\((\d+)(?:,([+-]))?\)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Compiled
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IntegerConstraint?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the constraint for a declared type, or null when it is not a sized integer.</summary>
    internal static IntegerConstraint? For(string? dataType)
    {
        if (string.IsNullOrEmpty(dataType)) return null;
        // A declared type has very low cardinality, so the cache stays tiny regardless of row count.
        return Cache.GetOrAdd(dataType!, static type =>
        {
            var match = Pattern.Match(type);
            if (!match.Success) return null;

            var name = match.Groups[1].Value;
            bool isInteger =
                name.Equals("INT", StringComparison.OrdinalIgnoreCase)
                || name.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)
                || name.Equals("BIGINT", StringComparison.OrdinalIgnoreCase)
                || name.Equals("SMALLINT", StringComparison.OrdinalIgnoreCase)
                || name.Equals("TINYINT", StringComparison.OrdinalIgnoreCase);

            if (!isInteger || !int.TryParse(match.Groups[2].Value, out var digits)) return null;
            return new IntegerConstraint(digits, match.Groups[3].Value);
        });
    }
}

public class InMemoryDataSource : IDataSource, ISpillable, IEstimatedCardinalityDataSource, IValidatedRowCountDataSource
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
    public bool ReplaceOnConflict { get; set; } = false;

    private readonly ConcurrentQueue<string> _spillChunkNames = new();
    public int SpillChunkCount => _spillChunkNames.Count;
    /// <summary>
    /// Approximate uncompressed payload target for a sequential spill extent. Logical input
    /// batches are appended to the same extent until this target is reached.
    /// </summary>
    public long SpillExtentTargetBytes { get; set; } = 128L * 1024 * 1024;
    public long SpillTotalBytes { get; private set; } = 0;
    private long _totalRowCount = 0;
    private long _residentEstimatedBytes;
    private IMemoryGrantLease? _memoryLease;
    public long EstimatedRowCount => _totalRowCount;
    private IExecutionContext? _executionContext;
    public IExecutionContext? ExecutionContext
    {
        get => _executionContext;
        set
        {
            IMemoryGrantLease? candidateLease = value == null
                ? null
                : (value.MemoryArbiter ?? MemoryGrantArbiter.Shared).AcquireLease();
            if (_residentEstimatedBytes > 0 &&
                candidateLease?.RegisterAndCheckSpill(_residentEstimatedBytes) == true)
            {
                candidateLease.Dispose();
                throw new ExecutionException(
                    $"In-memory table requires {_residentEstimatedBytes:N0} bytes, exceeding the process memory grant.");
            }

            if (_executionContext != null)
            {
                try
                {
                    _executionContext.ServiceProvider.GetService<IBufferManager>()?.UnregisterSpillable(this);
                }
                catch (ObjectDisposedException) { /* ignore during shutdown */ }
            }
            _memoryLease?.Dispose();
            _executionContext = value;
            _memoryLease = candidateLease;
            if (_executionContext != null)
            {
                try
                {
                    _executionContext.ServiceProvider.GetService<IBufferManager>()?.RegisterSpillable(this);
                }
                catch (ObjectDisposedException) { /* ignore during shutdown */ }
            }
        }

    }

    public long MemoryUsageBytes
    {
        get
        {
            long indexBytes = _index.Count * 128L; // Simplified
            // Spilled chunks are ON DISK, so they don't count towards CURRENT RAM USAGE.
            // This is critical for BufferManager to know how much RAM is actually reclaimable.
            return Interlocked.Read(ref _residentEstimatedBytes) + indexBytes;
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

            // Move resident batches into bounded sequential extents. Pressure-driven spilling used
            // to create one file per logical batch, amplifying filesystem and Arrow setup costs.
            if (_batches.Count > 0)
                SpillTotalBytes += await SpillResidentBatchesToExtentsAsync("spill");

            _batches.Clear();
            _index.ClearData(preserveUniqueKeys: true);
            ResetMemoryReservation(_index.EstimatedUniqueKeyBytes);

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private int? GetMaxLen(string dataType)
    {
        if (string.IsNullOrEmpty(dataType)) return null;
        int idx = dataType.IndexOf('(');
        if (idx == -1) return null;
        int endIdx = dataType.IndexOf(')', idx);
        if (endIdx == -1) return null;
        string lenStr = dataType.Substring(idx + 1, endIdx - idx - 1).Trim();
        if (int.TryParse(lenStr, out int result)) return result;
        return null;
    }

    public Task ValidateRow(Row row) => ValidateRow(row, _batches);

    public Task ValidateRow(Row row, IEnumerable<DataTable> activeBatches)
    {
        if (Schema.Count == 0 && TableConstraints.Count == 0)
            return Task.CompletedTask;
        return ValidateRowCore(row, activeBatches);
    }

    private async Task ValidateRowCore(Row row, IEnumerable<DataTable> activeBatches)
    {
        // Lazy: this runs once per ingested row and almost always finds nothing — an eager list was
        // ~0.4 GB per 10M rows in the Gate F round-trip profile.
        List<string>? errors = null;

        foreach (var kv in Schema)
        {
            var col = kv.Value;
            var val = row[col.ColumnName];

            // 0. Type Coercion
            if (val != null && val != DBNull.Value)
            {
                try
                {
                    row[col.ColumnName] = val = TypeConverter.Cast(val, col.DataType);
                }
                catch (Exception ex)
                {
                    if (ExecutionContext?.SkipError == true)
                    {
                        row[col.ColumnName] = val = null;
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Column '{col.ColumnName}' (value '{val}') cannot be converted to target type {col.DataType}. Detail: {ex.Message}");
                    }
                }
            }

            // 0b. String Truncation Check
            if (val is string strVal)
            {
                var maxLen = GetMaxLen(col.DataType);
                if (maxLen.HasValue && strVal.Length > maxLen.Value)
                {
                    if (ExecutionContext?.TruncateString == true)
                    {
                        row[col.ColumnName] = val = strVal.Substring(0, maxLen.Value);
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Column '{col.ColumnName}' is trying to insert a string with length {strVal.Length} into a {maxLen.Value} character column (Value: '{strVal}')");
                    }
                }
            }

            // 0c. Integer Precision & Sign Constraint Check (e.g. INT(5,+), INT(5,-), INT(5))
            if (val != null && val != DBNull.Value)
            {
                var constraint = IntegerConstraint.For(col.DataType);
                if (constraint.HasValue)
                {
                    int declaredDigits = constraint.Value.Digits;
                    string signChar = constraint.Value.Sign;
                    if (decimal.TryParse(val.ToString(), out var numVal))
                    {
                        if (signChar == "+" && numVal < 0)
                        {
                            if (ExecutionContext?.SkipError == true)
                            {
                                throw new RowSkipException($"Column '{col.ColumnName}' (value '{val}') violates positive-only constraint INT({declaredDigits},+).");
                            }
                            else
                            {
                                (errors ??= new List<string>()).Add($"Column '{col.ColumnName}' (value '{val}') violates positive-only constraint INT({declaredDigits},+).");
                            }
                        }
                        else if (signChar == "-" && numVal > 0)
                        {
                            if (ExecutionContext?.SkipError == true)
                            {
                                throw new RowSkipException($"Column '{col.ColumnName}' (value '{val}') violates negative-only constraint INT({declaredDigits},-).");
                            }
                            else
                            {
                                (errors ??= new List<string>()).Add($"Column '{col.ColumnName}' (value '{val}') violates negative-only constraint INT({declaredDigits},-).");
                            }
                        }
                        else
                        {
                            var absStr = Math.Abs(Math.Truncate(numVal)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (absStr.Length > declaredDigits)
                            {
                                if (ExecutionContext?.SkipError == true)
                                {
                                    throw new RowSkipException($"Column '{col.ColumnName}' value '{val}' exceeds declared digit limit of {declaredDigits}.");
                                }
                                else
                                {
                                    long maxVal = declaredDigits <= 18 ? (long)Math.Pow(10, declaredDigits) - 1 : long.MaxValue;
                                    (errors ??= new List<string>()).Add($"Column '{col.ColumnName}' value '{val}' exceeds declared digit limit of {declaredDigits} (max {declaredDigits} digits, range -{maxVal} to {maxVal}).");
                                }
                            }
                        }
                    }
                }
            }

            // 1. NOT NULL
            if (!col.IsNullable && (val == null || val == DBNull.Value))
            {
                if (ExecutionContext?.SkipError == true)
                {
                    throw new RowSkipException($"Column '{col.ColumnName}' does not allow nulls.");
                }
                else
                {
                    (errors ??= new List<string>()).Add($"Column '{col.ColumnName}' does not allow nulls.");
                }
            }

            // 2. Column-level CHECK
            if (col.CheckConstraint != null && Validator != null)
            {
                if (!await Validator.ValidateCheckConstraint(col.CheckConstraint, row))
                {
                    if (ExecutionContext?.SkipError == true)
                    {
                        throw new RowSkipException($"Check constraint violation on column {col.ColumnName}");
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Check constraint violation on column {col.ColumnName}");
                    }
                }
            }

            // 3. Column-level FK
            if (col.ForeignKey != null && Validator != null)
            {
                if (!await Validator.ValidateForeignKey(col.ForeignKey, new List<string> { col.ColumnName }, row))
                {
                    if (ExecutionContext?.SkipError == true)
                    {
                        throw new RowSkipException($"Foreign key violation on column {col.ColumnName} (value: {val})");
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Foreign key violation on column {col.ColumnName} (value: {val})");
                    }
                }
            }

            // 4. Column-level Unique
            if (col.IsUnique || col.IsPrimaryKey)
            {
                if (_index.IsDuplicate(new List<string> { col.ColumnName }, row, activeBatches))
                {
                    if (ExecutionContext?.SkipError == true)
                    {
                        throw new RowSkipException($"Unique constraint violation on column {col.ColumnName} (value: {val})");
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Unique constraint violation on column {col.ColumnName} (value: {val})");
                    }
                }
            }
        }

        // 5. Table-level Constraints
        foreach (var tc in TableConstraints)
        {
            if (tc is TableCheckConstraint c && Validator != null)
            {
                if (!await Validator.ValidateCheckConstraint(c.Expression, row))
                {
                    if (ExecutionContext?.SkipError == true)
                    {
                        throw new RowSkipException($"Check constraint violation: {tc.ConstraintName ?? "unnamed"}");
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Check constraint violation: {tc.ConstraintName ?? "unnamed"}");
                    }
                }
            }
            else if (tc is TableForeignKeyConstraint fk && Validator != null)
            {
                if (!await Validator.ValidateForeignKey(fk.Reference, fk.Columns, row))
                {
                    var vals = string.Join(", ", fk.Columns.Select(col => row[col]?.ToString() ?? "NULL"));
                    if (ExecutionContext?.SkipError == true)
                    {
                        throw new RowSkipException($"Foreign key violation: {tc.ConstraintName ?? "unnamed"} (values: {vals})");
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Foreign key violation: {tc.ConstraintName ?? "unnamed"} (values: {vals})");
                    }
                }
            }
            else if (tc is TablePrimaryKeyConstraint pk)
            {
                bool hasNull = false;
                foreach (var colName in pk.Columns)
                {
                    var val = row[colName];
                    if (val == null || val == DBNull.Value)
                    {
                        hasNull = true;
                        if (ExecutionContext?.SkipError == true)
                        {
                            throw new RowSkipException($"Primary key column {colName} cannot be null.");
                        }
                        else
                        {
                            (errors ??= new List<string>()).Add($"Primary key column {colName} cannot be null.");
                        }
                    }
                }
                if (!hasNull && _index.IsDuplicate(pk.Columns, row, activeBatches))
                {
                    if (ExecutionContext?.SkipError == true)
                    {
                        throw new RowSkipException($"Primary key violation: {tc.ConstraintName ?? "unnamed"}");
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Primary key violation: {tc.ConstraintName ?? "unnamed"}");
                    }
                }
            }
            else if (tc is TableUniqueConstraint uk)
            {
                if (_index.IsDuplicate(uk.Columns, row, activeBatches))
                {
                    if (ExecutionContext?.SkipError == true)
                    {
                        throw new RowSkipException($"Unique constraint violation: {tc.ConstraintName ?? "unnamed"}");
                    }
                    else
                    {
                        (errors ??= new List<string>()).Add($"Unique constraint violation: {tc.ConstraintName ?? "unnamed"}");
                    }
                }
            }
        }

        if (errors is { Count: > 0 })
        {
            throw new ExecutionException(string.Join(Environment.NewLine, errors));
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
        RecalculateResidentMemoryReservation();
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
        RecalculateResidentMemoryReservation();
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
            _index.ClearData();
            ResetMemoryReservation();

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

    /// <summary>
    /// Removes resident rows matching <paramref name="predicate"/> and returns how many were
    /// removed. Used by data-quality retention pruning on engine-managed targets. Returns
    /// <c>-1</c> when the table has spilled to disk — pruning a partially spilled table would
    /// silently prune only the resident part, so the caller is told it did not run.
    /// </summary>
    public int RemoveRows(Predicate<Row> predicate)
    {
        _lock.Wait();
        try
        {
            if (!_spillChunkNames.IsEmpty) return -1;

            int removed = 0;
            foreach (var batch in _batches)
            {
                removed += batch.Rows.RemoveAll(predicate);
            }
            if (removed == 0) return 0;

            _batches.RemoveAll(b => b.Rows.Count == 0);
            _totalRowCount -= removed;
            _index.ClearData(preserveUniqueKeys: false);
            foreach (var indexedColumns in _index.IndexedColumnSets)
                _index.RebuildIndex(indexedColumns, _batches);
            RecalculateResidentMemoryReservation();
            return removed;
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
        RecalculateResidentMemoryReservation();
    }

    public List<Row>? Lookup(string columnName, object? value)
    {
        return _index.Lookup(columnName, value);
    }

    public bool HasIndex(string columnName) => _index.HasIndex(columnName);

    public IDataSource WithTable(string tableName) => this;

    public async Task<long> CountRowsValidatedAsync(CancellationToken cancellationToken = default)
    {
        List<string> chunks;
        List<DataTable> memoryCopy;
        long expectedCount;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            chunks = _spillChunkNames.ToList();
            memoryCopy = _batches.ToList();
            expectedCount = _totalRowCount;
        }
        finally { _lock.Release(); }

        long physicalCount = memoryCopy.Sum(batch => (long)batch.Rows.Count);
        if (chunks.Count > 0 && ExecutionContext?.SpillStore == null)
            throw new ExecutionException("Spill-to-disk operation failed: IExecutionContext.SpillStore is null but spilled data exists.");

        foreach (var spillName in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var reader = await ExecutionContext!.SpillStore.CreateReaderAsync(spillName);
            if (reader is IColumnarSpillReader columnarReader)
            {
                await foreach (var batch in columnarReader.AsColumnBatchesAsync().WithCancellation(cancellationToken))
                {
                    using (batch) physicalCount += batch.RowCount;
                }
            }
            else
            {
                await foreach (var _ in reader.AsEnumerableAsync().WithCancellation(cancellationToken))
                    physicalCount++;
            }
        }

        if (physicalCount != expectedCount)
            throw new ExecutionException(
                $"Temp-table physical row-count validation failed: expected {expectedCount:N0}, read {physicalCount:N0}.");

        return physicalCount;
    }

    public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
        ReadBatches(batchSize, ExecutionContext?.CancellationToken ?? CancellationToken.None);

    public async IAsyncEnumerable<DataTable> ReadBatches(
        int batchSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 1. Yield from disk spill first (if any)
        List<string> chunks;
        List<DataTable> memoryCopy;
        await _lock.WaitAsync(cancellationToken);
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
                cancellationToken.ThrowIfCancellationRequested();
                if (ExecutionContext.SpillStore == null)
                    throw new ExecutionException("Spill-to-disk operation failed: IExecutionContext.SpillStore is null but spilled data exists.");

                await using var reader = await ExecutionContext.SpillStore.CreateReaderAsync(spillName);
                var batch = new DataTable();
                batch.SetColumns(_columnOrder);

                await foreach (var row in reader.AsEnumerableAsync().WithCancellation(cancellationToken))
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
        foreach (var b in memoryCopy)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return b;
        }
    }

    public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        => await WriteBatchesCore(batches, append, takeOwnership: false, ExecutionContext?.CancellationToken ?? CancellationToken.None);

    public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        => await WriteBatchesCore(batches, append, takeOwnership: false, cancellationToken);

    /// <summary>
    /// Writes engine-owned batches without cloning each row. The caller must not read or mutate a
    /// yielded batch after ownership transfers to this data source.
    /// </summary>
    public async Task WriteOwnedBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        => await WriteBatchesCore(batches, append, takeOwnership: true, ExecutionContext?.CancellationToken ?? CancellationToken.None);

    private async Task WriteBatchesCore(
        IAsyncEnumerable<DataTable> batches,
        bool append,
        bool takeOwnership,
        CancellationToken cancellationToken)
    {
        if (!append) await TruncateAsync();
        ISpillWriter? extentWriter = null;
        string? extentName = null;
        long extentEstimatedBytes = 0;
        Task? pendingSpillWrite = null;
        var currentExtentIndexedBatches = new List<DataTable>();

        try
        {
            await foreach (var b in batches.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_columnOrder.Count == 0)
                {
                    _columnOrder.AddRange(b.ColumnNames);
                    foreach (var col in _columnOrder)
                    {
                        if (!Schema.ContainsKey(col))
                            Schema[col] = new ColumnDefinition(col, "UNKNOWN", false);
                    }
                }

                IMemoryGrantLease? batchLease = null;
                await _lock.WaitAsync();
                try
                {
                    if (ReplaceOnConflict)
                    {
                        var uniqueKeys = new List<List<string>>();
                        foreach (var kv in Schema)
                        {
                            if (kv.Value.IsUnique || kv.Value.IsPrimaryKey)
                            {
                                uniqueKeys.Add(new List<string> { kv.Key });
                            }
                        }
                        foreach (var tc in TableConstraints)
                        {
                            if (tc is TablePrimaryKeyConstraint pk)
                            {
                                uniqueKeys.Add(pk.Columns);
                            }
                            else if (tc is TableUniqueConstraint uk)
                            {
                                uniqueKeys.Add(uk.Columns);
                            }
                        }

                        var rowsToDelete = new List<Row>();
                        foreach (var newRow in b.Rows)
                        {
                            foreach (var keyCols in uniqueKeys)
                            {
                                foreach (var batch in _batches)
                                {
                                    foreach (var existingRow in batch.Rows)
                                    {
                                        bool allMatch = true;
                                        foreach (var col in keyCols)
                                        {
                                            var existVal = existingRow[col];
                                            var newVal = newRow[col];
                                            if (existVal == null || existVal == DBNull.Value || newVal == null || newVal == DBNull.Value)
                                            {
                                                allMatch = false;
                                                break;
                                            }
                                            if (!IsSoftEqual(existVal, newVal))
                                            {
                                                allMatch = false;
                                                break;
                                            }
                                        }
                                        if (allMatch && !rowsToDelete.Contains(existingRow))
                                        {
                                            rowsToDelete.Add(existingRow);
                                        }
                                    }
                                }
                            }
                        }

                        if (rowsToDelete.Count > 0)
                        {
                            foreach (var batch in _batches)
                            {
                                foreach (var r in rowsToDelete)
                                {
                                    if (batch.Rows.Remove(r))
                                    {
                                        _totalRowCount--;
                                    }
                                }
                            }
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

                    var processedBatch = new DataTable();
                    processedBatch.SetColumns(_columnOrder);

                    // Loop-invariant and lazy: hoisted so the wrapper array + Concat iterator are not
                    // allocated per row (~880 MB per 10M rows in the Gate F round-trip profile). The
                    // deferred sequence still observes rows added to processedBatch when a constraint
                    // check actually enumerates it.
                    var activeBatches = _batches.Concat(new[] { processedBatch });
                    foreach (var row in b.Rows)
                    {
                        try
                        {
                            var rowClone = takeOwnership ? row : row.Clone();
                            await ValidateRow(rowClone, activeBatches);
                            await processedBatch.AddRowAsync(rowClone);
                        }
                        catch (RowSkipException)
                        {
                            // Skip this row
                        }
                    }

                    if (processedBatch.Rows.Count == 0) continue;

                    long threshold = ExecutionContext?.TempTableSpillThresholdRows ?? LanguageMetadata.DefaultTempTableSpillThresholdRows;
                    long processedBatchBytes = EstimateResidentMemoryBytes(processedBatch) +
                        EstimateIndexGrowthBytes(processedBatch.Rows.Count);
                    bool rowThresholdExceeded = _totalRowCount + processedBatch.Rows.Count > threshold;

                    // Account the producer slot independently from the resident table and the pending
                    // writer slot. If the writer currently owns the available headroom, wait for it and
                    // retry before admitting another batch. This is the pipeline's memory backpressure.
                    var pipelineArbiter = ExecutionContext?.MemoryArbiter ?? MemoryGrantArbiter.Shared;
                    batchLease = ExecutionContext == null ? null : pipelineArbiter.AcquireLease();
                    bool batchReserved = batchLease?.RegisterAndCheckSpill(processedBatchBytes) != true;
                    if (!batchReserved && pendingSpillWrite != null)
                    {
                        await AwaitPendingSpillAsync();
                        batchReserved = batchLease?.RegisterAndCheckSpill(processedBatchBytes) != true;
                    }

                    bool bytePressure = !batchReserved;
                    if (!rowThresholdExceeded && !bytePressure)
                    {
                        // Transfer accounting from the transient producer slot to retained table memory.
                        batchLease?.Dispose();
                        batchLease = null;
                        bytePressure = _memoryLease?.RegisterAndCheckSpill(
                            _residentEstimatedBytes + processedBatchBytes) == true;
                        if (bytePressure)
                        {
                            // Best effort reservation for the immediate writer slot. If the resident
                            // table already consumes the grant this may still be rejected; the batch is
                            // then synchronously handed to spill rather than retained.
                            batchLease = ExecutionContext == null ? null : pipelineArbiter.AcquireLease();
                            batchReserved = batchLease?.RegisterAndCheckSpill(processedBatchBytes) != true;
                            if (!batchReserved)
                            {
                                batchLease?.Dispose();
                                batchLease = null;
                            }
                        }
                    }

                    if (bytePressure || rowThresholdExceeded)
                    {
                        var executionContext = ExecutionContext;
                        if (executionContext != null)
                        {
                            // Keep at most one batch in the writer while the producer validates the
                            // next batch. Awaiting here bounds the pipeline to two logical batches:
                            // one being encoded/written and one being produced.
                            await AwaitPendingSpillAsync();

                            UpdateIndexesWithBatch(processedBatch);
                            if (_index.Count > 0)
                                currentExtentIndexedBatches.Add(processedBatch);
                            var estimatedBytes = EstimateSpillPayloadBytes(processedBatch);
                            pendingSpillWrite = WriteSpillBatchAsync(processedBatch, estimatedBytes, batchLease);
                            batchLease = null; // ownership transfers to the pending writer task
                            if (executionContext.EffectivePipelineDepth <= 0)
                                await AwaitPendingSpillAsync();
                            _totalRowCount += processedBatch.Rows.Count;

                            // A row-threshold spill may occur after the arbiter accepted the
                            // prospective resident size. Rebase to the bytes that actually remain
                            // resident so the process-wide reservation does not retain phantom RAM.
                            RecalculateResidentMemoryReservation();

                            if (executionContext.Telemetry.IsProfiling)
                                executionContext.LoggingContext.Logger.Debug(
                                    "Temp table spill triggered by {Reason} (row threshold {Threshold}). Appended batch to extent: {ExtentName}",
                                    bytePressure ? "memory grant" : "row threshold", threshold, extentName);

                            continue;
                        }
                    }

                    batchLease?.Dispose();

                    _batches.Add(processedBatch);
                    _residentEstimatedBytes += processedBatchBytes;
                    _totalRowCount += processedBatch.Rows.Count;

                    UpdateIndexesWithBatch(processedBatch);
                }
                finally
                {
                    batchLease?.Dispose();
                    _lock.Release();
                }
            }

            await AwaitPendingSpillAsync();

            if (extentWriter != null)
            {
                await CompleteExtentAsync();
            }
        }
        catch
        {
            // Observe an in-flight spill write before disposing the extent writer: the pending
            // task writes through extentWriter, so disposing/deleting underneath it races the
            // live write (and leaves its spill accounting nondeterministic). Its own failure is
            // secondary to the original exception.
            if (pendingSpillWrite != null)
            {
                try { await AwaitPendingSpillAsync(); }
                catch { /* preserve the original exception */ }
            }
            if (extentWriter != null)
            {
                try
                {
                    await extentWriter.DisposeAsync();
                }
                catch
                {
                    // Suppress secondary exceptions during cleanup to preserve the original exception
                }
                if (ExecutionContext != null && extentName != null)
                    ExecutionContext.SpillStore.DeleteChunk(extentName);
            }
            if (currentExtentIndexedBatches.Count > 0)
            {
                await _lock.WaitAsync();
                try
                {
                    foreach (var batch in currentExtentIndexedBatches)
                        _index.RemoveUniqueKeysForBatch(batch);
                    RecalculateResidentMemoryReservation();
                }
                finally { _lock.Release(); }
            }
            throw;
        }

        async Task CompleteExtentAsync()
        {
            var writer = extentWriter!;
            var name = extentName!;
            await writer.DisposeAsync();
            SpillTotalBytes += writer.BytesWritten;
            _spillChunkNames.Enqueue(name);
            extentWriter = null;
            extentName = null;
            extentEstimatedBytes = 0;
            currentExtentIndexedBatches.Clear();
        }

        async Task AwaitPendingSpillAsync()
        {
            if (pendingSpillWrite == null) return;
            var pending = pendingSpillWrite;
            pendingSpillWrite = null;
            await pending;
        }

        async Task WriteSpillBatchAsync(
            DataTable batch,
            long estimatedBytes,
            IMemoryGrantLease? pipelineLease)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (extentWriter == null)
                {
                    extentName = $"{Guid.NewGuid():N}.tmp";
                    extentWriter = await ExecutionContext!.SpillStore.CreateWriterAsync(extentName);
                    extentEstimatedBytes = 0;
                }

                if (extentWriter is IColumnarSpillWriter columnarWriter)
                {
                    using var columnBatch = ColumnBatchAdapter.FromDataTable(batch);
                    await columnarWriter.WriteBatchAsync(columnBatch);
                }
                else
                {
                    await extentWriter.WriteRowsAsync(batch.Rows);
                }
                extentEstimatedBytes += estimatedBytes;
                cancellationToken.ThrowIfCancellationRequested();

                if (extentEstimatedBytes >= Math.Max(1, SpillExtentTargetBytes))
                    await CompleteExtentAsync();
            }
            finally
            {
                pipelineLease?.Dispose();
            }
        }
    }

    private static long EstimateSpillPayloadBytes(DataTable batch)
    {
        // Arrow currently normalizes most fixed-width values to eight bytes and maintains null
        // metadata. Use a conservative per-cell allowance; include variable-width payload so a
        // string-heavy extent does not grow without bound. Exact physical bytes depend on optional
        // encryption/compression and are reported separately by the spill writer.
        long bytes = 0;
        foreach (var row in batch.Rows)
            bytes += row.EstimateHeapBytes();
        return bytes;
    }

    private long EstimateResidentMemoryBytes(DataTable batch)
    {
        // Row object + slot array + boxed/reference slots. Variable-width payload is added below.
        // The estimate is intentionally conservative because it is a governor input, not a storage
        // size report. Indexed tables include additional key/reference overhead per resident row.
        long bytes = 0;
        foreach (var row in batch.Rows)
            bytes += row.EstimateHeapBytes();
        return bytes;
    }

    private long EstimateIndexGrowthBytes(int rowCount)
        => (long)rowCount * (_index.UniqueIndexCount * 64L + _index.NonUniqueIndexCount * 16L);

    private void UpdateIndexesWithBatch(DataTable batch)
    {
        foreach (var indexKey in _index.Keys.ToList())
            if (_index.TryGetColumns(indexKey, out var columns))
                _index.UpdateIndexWithBatch(columns!, batch);
    }

    private void ResetMemoryReservation(long retainedBytes = 0)
    {
        _memoryLease?.Dispose();
        _memoryLease = _executionContext == null
            ? null
            : (_executionContext.MemoryArbiter ?? MemoryGrantArbiter.Shared).AcquireLease();
        Interlocked.Exchange(ref _residentEstimatedBytes, retainedBytes);
        if (retainedBytes > 0 && _memoryLease?.RegisterAndCheckSpill(retainedBytes) == true)
            throw new ExecutionException(
                $"Unique-key storage requires {retainedBytes:N0} bytes, exceeding the process memory grant.");
    }

    private void RebaseMemoryReservation()
    {
        _memoryLease?.Dispose();
        _memoryLease = _executionContext == null
            ? null
            : (_executionContext.MemoryArbiter ?? MemoryGrantArbiter.Shared).AcquireLease();
        if (_residentEstimatedBytes > 0)
        {
            if (_memoryLease?.RegisterAndCheckSpill(_residentEstimatedBytes) == true)
                throw new ExecutionException(
                    $"In-memory table requires {_residentEstimatedBytes:N0} bytes, exceeding the process memory grant.");
        }
    }

    private void RecalculateResidentMemoryReservation()
    {
        var residentRows = _batches.Sum(batch => (long)batch.Rows.Count);
        var bytes = _batches.Sum(EstimateResidentMemoryBytes) +
            _index.EstimatedUniqueKeyBytes +
            residentRows * _index.NonUniqueIndexCount * 16L;
        Interlocked.Exchange(ref _residentEstimatedBytes, bytes);
        RebaseMemoryReservation();
    }

    private async Task<long> SpillResidentBatchesToExtentsAsync(string extension)
    {
        if (ExecutionContext == null) throw new InvalidOperationException("A spill context is required.");
        var completedNames = new List<string>();
        ISpillWriter? writer = null;
        string? currentName = null;
        long extentBytes = 0;
        long totalBytes = 0;

        try
        {
            foreach (var batch in _batches)
            {
                if (writer == null)
                {
                    currentName = $"{Guid.NewGuid():N}.{extension}";
                    writer = await ExecutionContext.SpillStore.CreateWriterAsync(currentName);
                    extentBytes = 0;
                }

                if (writer is IColumnarSpillWriter columnarWriter)
                {
                    using var columnBatch = ColumnBatchAdapter.FromDataTable(batch);
                    await columnarWriter.WriteBatchAsync(columnBatch);
                }
                else
                {
                    await writer.WriteRowsAsync(batch.Rows);
                }
                extentBytes += EstimateSpillPayloadBytes(batch);
                if (extentBytes >= Math.Max(1, SpillExtentTargetBytes))
                    await CompleteCurrentAsync();
            }

            if (writer != null) await CompleteCurrentAsync();
            foreach (var name in completedNames) _spillChunkNames.Enqueue(name);
            return totalBytes;
        }
        catch
        {
            if (writer != null) await writer.DisposeAsync();
            if (currentName != null) ExecutionContext.SpillStore.DeleteChunk(currentName);
            foreach (var name in completedNames) ExecutionContext.SpillStore.DeleteChunk(name);
            throw;
        }

        async Task CompleteCurrentAsync()
        {
            var completedWriter = writer!;
            var completedName = currentName!;
            await completedWriter.DisposeAsync();
            totalBytes += completedWriter.BytesWritten;
            completedNames.Add(completedName);
            writer = null;
            currentName = null;
            extentBytes = 0;
        }
    }

    public async Task<bool> ExistsAsync(List<string> columns, List<object?> values)
    {
        var key = new CompositeKey(values.ToArray());
        var indexName = string.Join(",", columns);

        await _lock.WaitAsync();
        try
        {
            if (_index.HasIndex(indexName)) return _index.ContainsKey(indexName, key);

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
                        _totalRowCount--;
                    }
                }
            }
            if (deleted.Count > 0 && _index.Count > 0)
            {
                // Simplest to rebuild indexes for now if rows were deleted
                foreach (var col in _index.Keys.ToList())
                {
                    if (_index.TryGetColumns(col, out var cols))
                    {
                        _index.RebuildIndex(cols!, _batches);
                    }
                }
            }
            if (deleted.Count > 0) RecalculateResidentMemoryReservation();
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
                foreach (var col in _index.Keys.ToList())
                {
                    if (_index.TryGetColumns(col, out var cols))
                    {
                        _index.RebuildIndex(cols!, _batches);
                    }
                }
            }
            if (updated.Count > 0) RecalculateResidentMemoryReservation();
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
            var spilledRows = Math.Max(0, _totalRowCount - _batches.Sum(batch => (long)batch.Rows.Count));
            _batches.Clear();
            _batches.AddRange(s);
            _totalRowCount = spilledRows + _batches.Sum(batch => (long)batch.Rows.Count);
            ResetMemoryReservation();
            _residentEstimatedBytes = _batches.Sum(EstimateResidentMemoryBytes);
            RebaseMemoryReservation();
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
        ResetMemoryReservation();
        _memoryLease?.Dispose();
        _memoryLease = null;

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
        foreach (var chunk in chunks)
            _spillChunkNames.Enqueue(chunk);
        _totalRowCount = 0; // Will be recalculatable from chunks if needed, but for now we assume recovered
    }

    public IEnumerable<string> GetSpillChunks() => _spillChunkNames.ToArray();

    public async Task FlushToSpillAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_batches.Count == 0 || ExecutionContext?.SpillStore == null) return;

            SpillTotalBytes += await SpillResidentBatchesToExtentsAsync("tmp");
            _batches.Clear();
            _index.ClearData(preserveUniqueKeys: true);
            ResetMemoryReservation(_index.EstimatedUniqueKeyBytes);
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
