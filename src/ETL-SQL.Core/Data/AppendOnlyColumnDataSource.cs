using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Data;

/// <summary>
/// Append-only segmented native store. A small row head accepts compatibility writes and freezes into
/// immutable column batches; native readers retain those batches without reconstructing rows.
/// </summary>
public sealed class AppendOnlyColumnDataSource : IDataSource, IColumnarDataSource, IColumnarDataSink
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ColumnBatch> _segments = new();
    private readonly Dictionary<string, ColumnDefinition> _logicalSchema;
    private readonly string[] _columnNames;
    private DataTable _head;
    private readonly IMemoryGrantArbiter _memoryArbiter;
    private IMemoryGrantLease _headMemoryLease;
    private IMemoryGrantLease _constraintMemoryLease;
    private readonly List<IUniqueConstraint> _uniqueConstraints;
    private bool _disposed;
    private long _rowCount;
    private long _headEstimatedBytes;
    private long _allocatedSegmentBytes;
    private long _constraintEstimatedBytes;

    public AppendOnlyColumnDataSource(
        IEnumerable<ColumnDefinition> schema,
        int segmentRowCapacity = 10_000,
        IMemoryGrantArbiter? memoryArbiter = null,
        IEnumerable<TableConstraint>? tableConstraints = null)
    {
        if (segmentRowCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(segmentRowCapacity));
        SegmentRowCapacity = segmentRowCapacity;
        var definitions = schema?.ToArray() ?? throw new ArgumentNullException(nameof(schema));
        if (definitions.Length == 0) throw new ArgumentException("A column store requires at least one column.", nameof(schema));
        _logicalSchema = definitions.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        _columnNames = definitions.Select(column => column.ColumnName).ToArray();
        _memoryArbiter = memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance;
        _uniqueConstraints = definitions
            .Select((column, ordinal) => (column, ordinal))
            .Where(item => item.column.IsPrimaryKey || item.column.IsUnique)
            .Select(item => new UniqueColumnConstraint(item.column, item.ordinal))
            .Cast<IUniqueConstraint>()
            .ToList();
        foreach (var constraint in tableConstraints ?? Enumerable.Empty<TableConstraint>())
        {
            if (constraint is TablePrimaryKeyConstraint primaryKey)
                _uniqueConstraints.Add(CreateCompositeConstraint(primaryKey.Columns, isPrimaryKey: true, primaryKey.ConstraintName));
            else if (constraint is TableUniqueConstraint unique)
                _uniqueConstraints.Add(CreateCompositeConstraint(unique.Columns, isPrimaryKey: false, unique.ConstraintName));
        }
        _headMemoryLease = _memoryArbiter.AcquireLease();
        _constraintMemoryLease = _memoryArbiter.AcquireLease();
        _head = CreateHead();
    }

    public int SegmentRowCapacity { get; }
    public int SegmentCount { get { lock (_segments) return _segments.Count; } }
    public int MutableHeadRows => _head.Rows.Count;
    public long EstimatedRowCount => Interlocked.Read(ref _rowCount);
    public long AllocatedSegmentBytes => Interlocked.Read(ref _allocatedSegmentBytes);
    public long MemoryUsageBytes => AllocatedSegmentBytes + Interlocked.Read(ref _headEstimatedBytes) + Interlocked.Read(ref _constraintEstimatedBytes);

    public string Path => string.Empty;
    public Dictionary<string, string>? Options => null;
    public string ConnectorType => "COLUMNAR_MEMORY";

    public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync();
        try
        {
            ThrowIfDisposed();
            if (!append) ClearCore();
            await foreach (var batch in batches)
            {
                ValidateColumns(batch);
                foreach (var row in batch.Rows)
                {
                    var snapshot = NormalizeRow(row);
                    var rowBytes = snapshot.EstimateHeapBytes();
                    EnsureHeadMemoryGrant(_headEstimatedBytes + rowBytes);
                    var stagedKeys = StageKeys(snapshot);
                    try
                    {
                        CommitKeys(stagedKeys);
                    }
                    catch
                    {
                        RebaseHeadMemoryGrant(_headEstimatedBytes);
                        throw;
                    }
                    // The row path is a compatibility boundary; snapshot rows that remain in the
                    // mutable head so caller mutation cannot change stored data. Native writers use
                    // WriteColumnBatches and transfer pooled segments without this clone.
                    try
                    {
                        _head.Rows.Add(snapshot);
                    }
                    catch
                    {
                        RollbackKeys(stagedKeys);
                        RebaseHeadMemoryGrant(_headEstimatedBytes);
                        throw;
                    }
                    _rowCount++;
                    _headEstimatedBytes += rowBytes;
                    if (_head.Rows.Count >= SegmentRowCapacity)
                        FreezeHead();
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteColumnBatches(
        IAsyncEnumerable<ColumnBatch> batches,
        bool append = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!append) ClearCore();
            FreezeHead();
            await foreach (var batch in batches.WithCancellation(cancellationToken))
            {
                ValidateSchema(batch.Schema);
                ValidateNativeNullability(batch);
                var stagedKeys = StageKeys(batch);
                CommitKeys(stagedKeys);
                try
                {
                    AcceptSegment(batch); // ownership transfers after validation and reservation
                }
                catch
                {
                    RollbackKeys(stagedKeys);
                    throw;
                }
                _rowCount += batch.RowCount;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async IAsyncEnumerable<ColumnBatch> ReadColumnBatches(
        int batchSize = 10_000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ColumnBatch[] retained;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            FreezeHead();
            lock (_segments)
                retained = _segments.Select(segment => segment.Retain()).ToArray();
        }
        finally
        {
            _gate.Release();
        }

        var next = 0;
        try
        {
            for (; next < retained.Length; next++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = retained[next];
                retained[next] = null!; // ownership transfers to the consumer
                yield return batch;
            }
        }
        finally
        {
            for (; next < retained.Length; next++) retained[next]?.Dispose();
        }
    }

    public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10_000)
    {
        await foreach (var batch in ReadColumnBatches(batchSize))
        {
            using (batch)
                yield return ColumnBatchAdapter.ToDataTable(batch);
        }
    }

    public Task<IEnumerable<string>> GetColumnsAsync()
        => Task.FromResult<IEnumerable<string>>(_columnNames.ToArray());

    public async Task TruncateAsync()
    {
        ThrowIfDisposed();
        await _gate.WaitAsync();
        try
        {
            ThrowIfDisposed();
            ClearCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public IDataSource WithTable(string tableName) => this;
    public object? Snapshot() => throw new NotSupportedException("Column-store snapshots require spill-backed segment manifests.");
    public void Restore(object? snapshot) => throw new NotSupportedException("Column-store snapshots require spill-backed segment manifests.");

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            ClearCore();
            _headMemoryLease.Dispose();
            _constraintMemoryLease.Dispose();
            _disposed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void FreezeHead()
    {
        if (_head.Rows.Count == 0) return;
        var segment = ColumnBatchAdapter.FromDataTable(_head, _logicalSchema);
        try
        {
            AcceptSegment(segment); // head rows were already constraint-checked
        }
        catch
        {
            segment.Dispose();
            throw;
        }
        _head = CreateHead();
        _headEstimatedBytes = 0;
        _headMemoryLease.Dispose();
        _headMemoryLease = _memoryArbiter.AcquireLease();
    }

    private void ClearCore()
    {
        lock (_segments)
        {
            foreach (var segment in _segments) segment.Dispose();
            _segments.Clear();
        }
        _head = CreateHead();
        Interlocked.Exchange(ref _rowCount, 0);
        Interlocked.Exchange(ref _headEstimatedBytes, 0);
        _headMemoryLease.Dispose();
        _headMemoryLease = _memoryArbiter.AcquireLease();
        foreach (var constraint in _uniqueConstraints) constraint.Clear();
        _constraintEstimatedBytes = 0;
        _constraintMemoryLease.Dispose();
        _constraintMemoryLease = _memoryArbiter.AcquireLease();
    }

    private DataTable CreateHead()
    {
        var table = new DataTable();
        table.SetColumns(_columnNames);
        return table;
    }

    private void ValidateColumns(DataTable batch)
    {
        if (!batch.ColumnNames.SequenceEqual(_columnNames, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Input batch columns do not match the column-store schema.", nameof(batch));
    }

    private void ValidateSchema(ColumnBatchSchema schema)
    {
        if (schema.Count != _columnNames.Length)
            throw new ArgumentException("Native batch columns do not match the column-store schema.", nameof(schema));
        for (var i = 0; i < schema.Count; i++)
        {
            var field = schema.Fields[i];
            if (!field.Name.Equals(_columnNames[i], StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Native batch columns do not match the column-store schema.", nameof(schema));
            var definition = _logicalSchema[_columnNames[i]];
            var expectedPhysicalType = ColumnBatchAdapter.GetPhysicalType(definition.DataType);
            if (field.ElementType != expectedPhysicalType)
                throw new ArgumentException(
                    $"Native column '{field.Name}' has physical type {field.ElementType.Name}; expected {expectedPhysicalType.Name} for {definition.DataType}.",
                    nameof(schema));
            if (!field.LogicalType.Equals(definition.DataType, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"Native column '{field.Name}' has logical type {field.LogicalType}; expected {definition.DataType}.",
                    nameof(schema));
        }
    }

    private Row NormalizeRow(Row source)
    {
        var normalized = source.Clone();
        for (var ordinal = 0; ordinal < _columnNames.Length; ordinal++)
        {
            var definition = _logicalSchema[_columnNames[ordinal]];
            var value = normalized[ordinal];
            if (value == null || value == DBNull.Value)
            {
                if (!definition.IsNullable || definition.IsPrimaryKey)
                    throw new ExecutionException($"Column '{definition.ColumnName}' does not allow NULL.");
                normalized[ordinal] = null;
                continue;
            }
            normalized[ordinal] = TypeConverter.Cast(value, definition.DataType);
        }
        return normalized;
    }

    private void ValidateNativeNullability(ColumnBatch batch)
    {
        for (var ordinal = 0; ordinal < _columnNames.Length; ordinal++)
        {
            var definition = _logicalSchema[_columnNames[ordinal]];
            if (definition.IsNullable && !definition.IsPrimaryKey) continue;
            var column = batch.Columns[ordinal];
            for (var row = 0; row < batch.RowCount; row++)
                if (column.IsNull(row))
                    throw new ExecutionException($"Column '{definition.ColumnName}' does not allow NULL.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AppendOnlyColumnDataSource));
    }

    private void EnsureHeadMemoryGrant(long prospectiveBytes)
    {
        if (_headMemoryLease.RegisterAndCheckSpill(prospectiveBytes))
            throw MemoryGrantExceeded(prospectiveBytes);
    }

    private void RebaseHeadMemoryGrant(long bytes)
    {
        _headMemoryLease.Dispose();
        _headMemoryLease = _memoryArbiter.AcquireLease();
        if (bytes > 0 && _headMemoryLease.RegisterAndCheckSpill(bytes))
            throw MemoryGrantExceeded(bytes);
    }

    private List<StagedKeys> StageKeys(Row row)
        => _uniqueConstraints.Select(constraint => constraint.Stage(row)).ToList();

    private List<StagedKeys> StageKeys(ColumnBatch batch)
        => _uniqueConstraints.Select(constraint => constraint.Stage(batch)).ToList();

    private void CommitKeys(List<StagedKeys> staged)
    {
        var addedBytes = staged.Sum(item => item.EstimatedBytes);
        if (_constraintMemoryLease.RegisterAndCheckSpill(checked(_constraintEstimatedBytes + addedBytes)))
        {
            RebaseConstraintMemoryGrant();
            throw MemoryGrantExceeded(checked(_constraintEstimatedBytes + addedBytes));
        }
        foreach (var item in staged) item.Commit();
        _constraintEstimatedBytes += addedBytes;
    }

    private void RollbackKeys(List<StagedKeys> staged)
    {
        foreach (var item in staged) item.Rollback();
        _constraintEstimatedBytes -= staged.Sum(item => item.EstimatedBytes);
        RebaseConstraintMemoryGrant();
    }

    private void RebaseConstraintMemoryGrant()
    {
        _constraintMemoryLease.Dispose();
        _constraintMemoryLease = _memoryArbiter.AcquireLease();
        if (_constraintEstimatedBytes > 0 && _constraintMemoryLease.RegisterAndCheckSpill(_constraintEstimatedBytes))
            throw MemoryGrantExceeded(_constraintEstimatedBytes);
    }

    private void AcceptSegment(ColumnBatch segment)
    {
        var bytes = segment.AllocatedBytes;
        var lease = _memoryArbiter.AcquireLease();
        if (lease.RegisterAndCheckSpill(bytes))
        {
            lease.Dispose();
            throw MemoryGrantExceeded(bytes);
        }

        try
        {
            segment.OnFinalRelease(() =>
            {
                Interlocked.Add(ref _allocatedSegmentBytes, -bytes);
                lease.Dispose();
            });
        }
        catch
        {
            lease.Dispose();
            throw;
        }
        lock (_segments) _segments.Add(segment);
        Interlocked.Add(ref _allocatedSegmentBytes, bytes);
    }

    private ExecutionException MemoryGrantExceeded(long requestedBytes) => new(
        $"Append-only column storage requires {requestedBytes:N0} bytes, exceeding the process memory grant. " +
        "Segment-native spill is not available yet; increase Engine:TotalMemoryGrantMB or reduce the input batch size.");

    private IUniqueConstraint CreateCompositeConstraint(IReadOnlyList<string> columns, bool isPrimaryKey, string? name)
    {
        if (columns.Count == 0) throw new ArgumentException("A table uniqueness constraint requires at least one column.", nameof(columns));
        var definitions = new ColumnDefinition[columns.Count];
        var ordinals = new int[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            if (!_logicalSchema.TryGetValue(columns[i], out var definition))
                throw new ArgumentException($"Constraint column '{columns[i]}' is not present in the column-store schema.", nameof(columns));
            definitions[i] = definition;
            ordinals[i] = Array.FindIndex(_columnNames, column => column.Equals(columns[i], StringComparison.OrdinalIgnoreCase));
        }
        return new CompositeUniqueConstraint(definitions, ordinals, isPrimaryKey, name);
    }

    private sealed class StagedKeys
    {
        private readonly IUniqueConstraint _owner;
        private readonly HashSet<object> _keys;
        private bool _committed;

        public StagedKeys(IUniqueConstraint owner, HashSet<object> keys, long estimatedBytes)
        {
            _owner = owner;
            _keys = keys;
            EstimatedBytes = estimatedBytes;
        }

        public long EstimatedBytes { get; }
        public void Commit() { foreach (var key in _keys) _owner.Add(key); _committed = true; }
        public void Rollback() { if (_committed) foreach (var key in _keys) _owner.Remove(key); }
    }

    private interface IUniqueConstraint
    {
        StagedKeys Stage(Row row);
        StagedKeys Stage(ColumnBatch batch);
        void Add(object key);
        void Remove(object key);
        void Clear();
    }

    private sealed class UniqueColumnConstraint : IUniqueConstraint
    {
        private readonly ColumnDefinition _definition;
        private readonly int _ordinal;
        private readonly IKeySet _keys;

        public UniqueColumnConstraint(ColumnDefinition definition, int ordinal)
        {
            _definition = definition;
            _ordinal = ordinal;
            _keys = CreateKeySet(ColumnBatchAdapter.GetPhysicalType(definition.DataType));
        }

        public StagedKeys Stage(Row row) => StageValues(new[] { row[_ordinal] });

        public StagedKeys Stage(ColumnBatch batch)
        {
            var column = batch.Columns[_ordinal];
            return StageValues(Enumerable.Range(0, batch.RowCount).Select(column.GetBoxedValue));
        }

        public void Add(object key) => _keys.Add(key);
        public void Remove(object key) => _keys.Remove(key);
        public void Clear() => _keys.Clear();

        private StagedKeys StageValues(IEnumerable<object?> values)
        {
            var additions = new HashSet<object>();
            long bytes = 0;
            foreach (var raw in values)
            {
                if (raw == null || raw == DBNull.Value)
                {
                    if (_definition.IsPrimaryKey)
                        throw new ExecutionException($"Primary key column '{_definition.ColumnName}' cannot contain NULL.");
                    continue; // SQL UNIQUE permits multiple NULL values.
                }

                var key = Normalize(raw);
                if (_keys.Contains(key) || !additions.Add(key))
                    throw new ExecutionException($"Duplicate value violates the unique constraint on column '{_definition.ColumnName}'.");
                bytes = checked(bytes + 32 + Row.EstimateValueBytes(key));
            }
            return new StagedKeys(this, additions, bytes);
        }

        internal static object Normalize(object value, ColumnDefinition definition)
        {
            var converted = TypeConverter.Cast(value, definition.DataType) ?? value;
            return ColumnBatchAdapter.GetPhysicalType(definition.DataType) switch
            {
                var type when type == typeof(byte) => Convert.ToByte(converted is bool flag ? (flag ? 1 : 0) : converted),
                var type when type == typeof(short) => Convert.ToInt16(converted),
                var type when type == typeof(int) => Convert.ToInt32(converted),
                var type when type == typeof(long) => Convert.ToInt64(converted),
                var type when type == typeof(float) => Convert.ToSingle(converted),
                var type when type == typeof(double) => Convert.ToDouble(converted),
                var type when type == typeof(decimal) => Convert.ToDecimal(converted),
                var type when type == typeof(DateTime) => Convert.ToDateTime(converted),
                var type when type == typeof(TimeSpan) => converted is TimeSpan span ? span : TimeSpan.Parse(converted.ToString()!),
                var type when type == typeof(Guid) => converted is Guid guid ? guid : Guid.Parse(converted.ToString()!),
                _ => converted.ToString() ?? string.Empty
            };
        }

        private object Normalize(object value) => Normalize(value, _definition);

        private static IKeySet CreateKeySet(Type type)
        {
            if (type == typeof(byte)) return new KeySet<byte>();
            if (type == typeof(short)) return new KeySet<short>();
            if (type == typeof(int)) return new KeySet<int>();
            if (type == typeof(long)) return new KeySet<long>();
            if (type == typeof(float)) return new KeySet<float>();
            if (type == typeof(double)) return new KeySet<double>();
            if (type == typeof(decimal)) return new KeySet<decimal>();
            if (type == typeof(DateTime)) return new KeySet<DateTime>();
            if (type == typeof(TimeSpan)) return new KeySet<TimeSpan>();
            if (type == typeof(Guid)) return new KeySet<Guid>();
            return new KeySet<string>();
        }
    }

    private sealed class CompositeUniqueConstraint : IUniqueConstraint
    {
        private readonly ColumnDefinition[] _definitions;
        private readonly int[] _ordinals;
        private readonly bool _isPrimaryKey;
        private readonly string _displayName;
        private readonly HashSet<PackedCompositeKey> _keys = new();

        public CompositeUniqueConstraint(ColumnDefinition[] definitions, int[] ordinals, bool isPrimaryKey, string? name)
        {
            _definitions = definitions;
            _ordinals = ordinals;
            _isPrimaryKey = isPrimaryKey;
            _displayName = name ?? string.Join(", ", definitions.Select(definition => definition.ColumnName));
        }

        public StagedKeys Stage(Row row)
            => StageRows(1, (_, ordinal) => row[ordinal]);

        public StagedKeys Stage(ColumnBatch batch)
            => StageRows(batch.RowCount, (row, ordinal) => batch.Columns[ordinal].GetBoxedValue(row));

        public void Add(object key) => _keys.Add((PackedCompositeKey)key);
        public void Remove(object key) => _keys.Remove((PackedCompositeKey)key);
        public void Clear() => _keys.Clear();

        private StagedKeys StageRows(int rowCount, Func<int, int, object?> getValue)
        {
            var additions = new HashSet<object>();
            long bytes = 0;
            for (var row = 0; row < rowCount; row++)
            {
                var values = new object?[_ordinals.Length];
                var hasNull = false;
                for (var column = 0; column < _ordinals.Length; column++)
                {
                    var raw = getValue(row, _ordinals[column]);
                    if (raw == null || raw == DBNull.Value)
                    {
                        hasNull = true;
                        if (_isPrimaryKey)
                            throw new ExecutionException($"Primary key constraint '{_displayName}' cannot contain NULL.");
                        break;
                    }
                    values[column] = UniqueColumnConstraint.Normalize(raw, _definitions[column]);
                }
                if (hasNull) continue; // SQL UNIQUE treats rows containing NULL as non-comparable.

                var key = PackedCompositeKey.Create(values);
                if (_keys.Contains(key) || !additions.Add(key))
                    throw new ExecutionException($"Duplicate value violates constraint '{_displayName}'.");
                bytes = checked(bytes + 32 + key.EstimatedBytes);
            }
            return new StagedKeys(this, additions, bytes);
        }
    }

    private readonly struct PackedCompositeKey : IEquatable<PackedCompositeKey>
    {
        private readonly byte[] _bytes;
        private readonly int _hashCode;

        private PackedCompositeKey(byte[] bytes)
        {
            _bytes = bytes;
            var hash = new HashCode();
            hash.AddBytes(bytes);
            _hashCode = hash.ToHashCode();
        }

        public long EstimatedBytes => 24L + _bytes.LongLength;

        public static PackedCompositeKey Create(IReadOnlyList<object?> values)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                foreach (var value in values)
                {
                    switch (value)
                    {
                        case byte item: writer.Write(item); break;
                        case short item: writer.Write(item); break;
                        case int item: writer.Write(item); break;
                        case long item: writer.Write(item); break;
                        case float item: writer.Write(item); break;
                        case double item: writer.Write(item); break;
                        case decimal item:
                            foreach (var part in decimal.GetBits(item)) writer.Write(part);
                            break;
                        case DateTime item: writer.Write(item.ToBinary()); break;
                        case TimeSpan item: writer.Write(item.Ticks); break;
                        case Guid item: writer.Write(item.ToByteArray()); break;
                        case string item: writer.Write(item); break;
                        default: throw new InvalidOperationException($"Unsupported composite-key type '{value?.GetType().Name}'.");
                    }
                }
            }
            return new PackedCompositeKey(stream.ToArray());
        }

        public bool Equals(PackedCompositeKey other)
            => _hashCode == other._hashCode && _bytes.AsSpan().SequenceEqual(other._bytes);
        public override bool Equals(object? obj) => obj is PackedCompositeKey other && Equals(other);
        public override int GetHashCode() => _hashCode;
    }

    private interface IKeySet
    {
        bool Contains(object value);
        void Add(object value);
        void Remove(object value);
        void Clear();
    }

    private sealed class KeySet<T> : IKeySet where T : notnull
    {
        private readonly HashSet<T> _values = new();
        public bool Contains(object value) => _values.Contains((T)value);
        public void Add(object value) => _values.Add((T)value);
        public void Remove(object value) => _values.Remove((T)value);
        public void Clear() => _values.Clear();
    }
}
