using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
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
public sealed class AppendOnlyColumnDataSource : ITransactionalDataSource, IReplayableColumnarDataSource, IColumnarDataSink, IEstimatedCardinalityDataSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ColumnBatch> _segments = new();
    private readonly List<byte[]?> _segmentTombstones = new();
    private readonly Dictionary<string, ColumnDefinition> _logicalSchema;
    private readonly string[] _columnNames;
    private DataTable _head;
    private readonly IMemoryGrantArbiter _memoryArbiter;
    private IMemoryGrantLease _headMemoryLease;
    private IMemoryGrantLease _constraintMemoryLease;
    private IMemoryGrantLease _tombstoneMemoryLease;
    private readonly List<IUniqueConstraint> _uniqueConstraints;
    private readonly Stack<TransactionSnapshot> _transactionSnapshots = new();
    private bool _disposed;
    private long _rowCount;
    private long _headEstimatedBytes;
    private long _allocatedSegmentBytes;
    private long _constraintEstimatedBytes;
    private long _tombstoneBytes;
    private long _compactionCount;

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
        TableConstraints = (tableConstraints ?? Enumerable.Empty<TableConstraint>()).ToArray();
        _memoryArbiter = memoryArbiter ?? UnlimitedMemoryGrantArbiter.Instance;
        _uniqueConstraints = definitions
            .Select((column, ordinal) => (column, ordinal))
            .Where(item => item.column.IsPrimaryKey || item.column.IsUnique)
            .Select(item => new UniqueColumnConstraint(item.column, item.ordinal))
            .Cast<IUniqueConstraint>()
            .ToList();
        foreach (var constraint in TableConstraints)
        {
            if (constraint is TablePrimaryKeyConstraint primaryKey)
                _uniqueConstraints.Add(CreateCompositeConstraint(primaryKey.Columns, isPrimaryKey: true, primaryKey.ConstraintName));
            else if (constraint is TableUniqueConstraint unique)
                _uniqueConstraints.Add(CreateCompositeConstraint(unique.Columns, isPrimaryKey: false, unique.ConstraintName));
        }
        _headMemoryLease = _memoryArbiter.AcquireLease();
        _constraintMemoryLease = _memoryArbiter.AcquireLease();
        _tombstoneMemoryLease = _memoryArbiter.AcquireLease();
        _head = CreateHead();
    }

    public int SegmentRowCapacity { get; }
    public int SegmentCount { get { lock (_segments) return _segments.Count; } }
    public int MutableHeadRows => _head.Rows.Count;
    public long EstimatedRowCount => Interlocked.Read(ref _rowCount);
    public long AllocatedSegmentBytes => Interlocked.Read(ref _allocatedSegmentBytes);
    public long MemoryUsageBytes => AllocatedSegmentBytes + Interlocked.Read(ref _headEstimatedBytes)
        + Interlocked.Read(ref _constraintEstimatedBytes) + Interlocked.Read(ref _tombstoneBytes);
    public IReadOnlyDictionary<string, ColumnDefinition> LogicalSchema => _logicalSchema;
    public IReadOnlyList<TableConstraint> TableConstraints { get; }
    public long CompactionCount => Interlocked.Read(ref _compactionCount);
    public long TombstonedRowCount
    {
        get
        {
            lock (_segments) return _segmentTombstones.Sum(CountTombstones);
        }
    }

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
        byte[]?[] tombstones;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            FreezeHead();
            lock (_segments)
            {
                retained = _segments.Select(segment => segment.Retain()).ToArray();
                tombstones = _segmentTombstones.Select(bitmap => bitmap?.ToArray()).ToArray();
            }
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
                var tombstone = tombstones[next];
                if (tombstone == null)
                {
                    yield return batch;
                    continue;
                }
                using (batch)
                using (var selection = CreateLiveSelection(batch.RowCount, tombstone))
                    yield return ColumnBatchAdapter.Compact(
                        batch, _columnNames, selection, cancellationToken, _columnNames);
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

    /// <summary>
    /// Marks rows selected by a native predicate as deleted. Returns <c>null</c> without mutation when
    /// the predicate cannot be bound to native buffers, allowing the caller to use row compatibility.
    /// </summary>
    public async Task<long?> DeleteWhereAsync(
        Expression? predicate,
        bool caseSensitiveComparison,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            FreezeHead();
            var selections = new SelectionVector?[_segments.Count];
            try
            {
                for (var index = 0; index < _segments.Count; index++)
                {
                    if (predicate == null)
                        selections[index] = SelectionVector.FromIndices(Enumerable.Range(0, _segments[index].RowCount));
                    else if (!ColumnarPredicateCompiler.TrySelect(
                        _segments[index], predicate, out selections[index],
                        cancellationToken: cancellationToken,
                        caseSensitiveComparison: caseSensitiveComparison))
                        return null;
                }

                var original = _segmentTombstones.Select(bitmap => bitmap?.ToArray()).ToArray();
                var originalCount = _rowCount;
                var addedBitmapBytes = 0L;
                for (var index = 0; index < selections.Length; index++)
                    if (_segmentTombstones[index] == null && selections[index]!.Count > 0)
                        addedBitmapBytes = checked(addedBitmapBytes + (_segments[index].RowCount + 7L) / 8L);
                var prospectiveBytes = checked(_tombstoneBytes + addedBitmapBytes);
                if (_tombstoneMemoryLease.RegisterAndCheckSpill(prospectiveBytes))
                    throw MemoryGrantExceeded(prospectiveBytes);

                long deleted = 0;
                try
                {
                    for (var index = 0; index < selections.Length; index++)
                    {
                        var selection = selections[index]!;
                        if (selection.Count == 0) continue;
                        var bitmap = _segmentTombstones[index] ??=
                            new byte[(_segments[index].RowCount + 7) / 8];
                        foreach (var row in selection.Indices.Span)
                        {
                            var mask = (byte)(1 << (row & 7));
                            ref var slot = ref bitmap[row >> 3];
                            if ((slot & mask) != 0) continue;
                            slot |= mask;
                            deleted++;
                        }
                    }
                    _tombstoneBytes = prospectiveBytes;
                    _rowCount -= deleted;
                    RebuildConstraintsFromLiveRows(cancellationToken);
                    TryCompactTombstones(cancellationToken);
                    return deleted;
                }
                catch
                {
                    _segmentTombstones.Clear();
                    _segmentTombstones.AddRange(original);
                    _rowCount = originalCount;
                    RebaseTombstoneGrant(original.Sum(bitmap => (long)(bitmap?.LongLength ?? 0)));
                    RebuildConstraintsFromLiveRows(cancellationToken);
                    throw;
                }
            }
            finally
            {
                foreach (var selection in selections) selection?.Dispose();
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Replaces rows selected by a native predicate using tombstones plus one appended delta segment.
    /// Only selected rows cross the row expression boundary. Returns <c>null</c> without mutation when
    /// the predicate cannot be bound to native buffers.
    /// </summary>
    public async Task<long?> UpdateWhereAsync(
        Expression? predicate,
        bool caseSensitiveComparison,
        Func<Row, ValueTask<Row>> transform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            FreezeHead();
            var selections = new SelectionVector?[_segments.Count];
            try
            {
                for (var index = 0; index < _segments.Count; index++)
                {
                    if (predicate == null)
                        selections[index] = SelectionVector.FromIndices(Enumerable.Range(0, _segments[index].RowCount));
                    else if (!ColumnarPredicateCompiler.TrySelect(
                        _segments[index], predicate, out selections[index],
                        cancellationToken: cancellationToken,
                        caseSensitiveComparison: caseSensitiveComparison))
                        return null;
                }

                var replacements = CreateHead();
                for (var segmentIndex = 0; segmentIndex < selections.Length; segmentIndex++)
                {
                    var segment = _segments[segmentIndex];
                    var tombstone = _segmentTombstones[segmentIndex];
                    foreach (var sourceRow in selections[segmentIndex]!.Indices.ToArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (tombstone != null && (tombstone[sourceRow >> 3] & (1 << (sourceRow & 7))) != 0)
                            continue;
                        var row = replacements.NewRow();
                        for (var column = 0; column < _columnNames.Length; column++)
                        {
                            var field = segment.Schema.Fields[column];
                            row[column] = ColumnBatchAdapter.RestoreEngineValue(
                                segment.Columns[column].GetBoxedValue(sourceRow), field.LogicalType);
                        }
                        replacements.Rows.Add(NormalizeRow(await transform(row)));
                    }
                }
                if (replacements.Rows.Count == 0) return 0;

                var originalTombstones = _segmentTombstones.Select(bitmap => bitmap?.ToArray()).ToArray();
                var originalCount = _rowCount;
                var originalSegmentCount = _segments.Count;
                var addedBitmapBytes = 0L;
                for (var index = 0; index < selections.Length; index++)
                    if (_segmentTombstones[index] == null && selections[index]!.Count > 0)
                        addedBitmapBytes = checked(addedBitmapBytes + (_segments[index].RowCount + 7L) / 8L);
                var prospectiveBytes = checked(_tombstoneBytes + addedBitmapBytes);
                if (_tombstoneMemoryLease.RegisterAndCheckSpill(prospectiveBytes))
                    throw MemoryGrantExceeded(prospectiveBytes);

                ColumnBatch? delta = null;
                try
                {
                    long updated = 0;
                    for (var index = 0; index < selections.Length; index++)
                    {
                        var bitmap = _segmentTombstones[index] ??=
                            new byte[(_segments[index].RowCount + 7) / 8];
                        foreach (var row in selections[index]!.Indices.Span)
                        {
                            var mask = (byte)(1 << (row & 7));
                            ref var slot = ref bitmap[row >> 3];
                            if ((slot & mask) != 0) continue;
                            slot |= mask;
                            updated++;
                        }
                    }
                    _tombstoneBytes = prospectiveBytes;
                    _rowCount -= updated;
                    RebuildConstraintsFromLiveRows(cancellationToken);

                    delta = ColumnBatchAdapter.FromDataTable(replacements, _logicalSchema);
                    ValidateNativeNullability(delta);
                    var stagedKeys = StageKeys(delta);
                    CommitKeys(stagedKeys);
                    try { AcceptSegment(delta); }
                    catch { RollbackKeys(stagedKeys); throw; }
                    delta = null; // ownership transferred
                    _rowCount += updated;
                    TryCompactTombstones(cancellationToken);
                    return updated;
                }
                catch
                {
                    delta?.Dispose();
                    while (_segments.Count > originalSegmentCount)
                    {
                        var last = _segments.Count - 1;
                        var appended = _segments[last];
                        _segments.RemoveAt(last);
                        _segmentTombstones.RemoveAt(last);
                        appended.Dispose();
                    }
                    _segmentTombstones.Clear();
                    _segmentTombstones.AddRange(originalTombstones);
                    _rowCount = originalCount;
                    RebaseTombstoneGrant(originalTombstones.Sum(bitmap => (long)(bitmap?.LongLength ?? 0)));
                    RebuildConstraintsFromLiveRows(cancellationToken);
                    throw;
                }
            }
            finally
            {
                foreach (var selection in selections) selection?.Dispose();
            }
        }
        finally { _gate.Release(); }
    }

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

    public async Task BeginTransactionAsync()
    {
        await BeginTransactionAsync(CancellationToken.None);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            FreezeHead();
            ColumnBatch[] retained;
            lock (_segments) retained = _segments.Select(segment => segment.Retain()).ToArray();
            byte[]?[] tombstones;
            lock (_segments) tombstones = _segmentTombstones.Select(bitmap => bitmap?.ToArray()).ToArray();
            _transactionSnapshots.Push(new TransactionSnapshot(retained, tombstones, _rowCount));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CommitAsync()
    {
        await CommitAsync(CancellationToken.None);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (_transactionSnapshots.Count == 0)
                throw new InvalidOperationException("No append-store transaction is active.");
            _transactionSnapshots.Pop().Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RollbackAsync()
    {
        await RollbackAsync(CancellationToken.None);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (_transactionSnapshots.Count == 0)
                throw new InvalidOperationException("No append-store transaction is active.");
            var snapshot = _transactionSnapshots.Pop();
            ClearCore();
            var (restored, tombstones) = snapshot.TakeSegments();
            try
            {
                lock (_segments)
                {
                    _segments.AddRange(restored);
                    _segmentTombstones.AddRange(tombstones);
                }
                RebaseTombstoneGrant(tombstones.Sum(bitmap => (long)(bitmap?.LongLength ?? 0)));
                Interlocked.Exchange(ref _rowCount, snapshot.RowCount);
                RebuildConstraintsFromLiveRows(cancellationToken);
            }
            catch
            {
                foreach (var segment in restored) segment.Dispose();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            while (_transactionSnapshots.Count > 0) _transactionSnapshots.Pop().Dispose();
            ClearCore();
            _headMemoryLease.Dispose();
            _constraintMemoryLease.Dispose();
            _tombstoneMemoryLease.Dispose();
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
            _segmentTombstones.Clear();
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
        RebaseTombstoneGrant(0);
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

    private void RebuildConstraintsFromLiveRows(CancellationToken cancellationToken)
    {
        foreach (var constraint in _uniqueConstraints) constraint.Clear();
        _constraintEstimatedBytes = 0;
        _constraintMemoryLease.Dispose();
        _constraintMemoryLease = _memoryArbiter.AcquireLease();
        for (var index = 0; index < _segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tombstone = _segmentTombstones[index];
            if (tombstone == null)
            {
                CommitKeys(StageKeys(_segments[index]));
                continue;
            }
            using var selection = CreateLiveSelection(_segments[index].RowCount, tombstone);
            if (selection.Count == 0) continue;
            using var live = ColumnBatchAdapter.Compact(
                _segments[index], _columnNames, selection, cancellationToken, _columnNames);
            CommitKeys(StageKeys(live));
        }
    }

    private void RebaseTombstoneGrant(long bytes)
    {
        _tombstoneMemoryLease.Dispose();
        _tombstoneMemoryLease = _memoryArbiter.AcquireLease();
        if (bytes > 0 && _tombstoneMemoryLease.RegisterAndCheckSpill(bytes))
            throw MemoryGrantExceeded(bytes);
        _tombstoneBytes = bytes;
    }

    private bool TryCompactTombstones(CancellationToken cancellationToken)
    {
        if (_transactionSnapshots.Count > 0) return false;
        long physicalRows = 0;
        long tombstonedRows = 0;
        for (var index = 0; index < _segments.Count; index++)
        {
            physicalRows += _segments[index].RowCount;
            tombstonedRows += CountTombstones(_segmentTombstones[index]);
        }
        if (tombstonedRows == 0 || tombstonedRows * 4 < physicalRows) return false;

        var replacements = new List<(int Index, ColumnBatch? Batch)>();
        try
        {
            for (var index = 0; index < _segments.Count; index++)
            {
                var tombstone = _segmentTombstones[index];
                if (tombstone == null) continue;
                cancellationToken.ThrowIfCancellationRequested();
                using var selection = CreateLiveSelection(_segments[index].RowCount, tombstone);
                var compacted = selection.Count == 0
                    ? null
                    : ColumnBatchAdapter.Compact(
                        _segments[index], _columnNames, selection, cancellationToken, _columnNames);
                replacements.Add((index, compacted));
            }

            var accepted = 0;
            try
            {
                foreach (var replacement in replacements)
                {
                    if (replacement.Batch == null) continue;
                    AcceptSegment(replacement.Batch);
                    accepted++;
                }
            }
            catch (ExecutionException)
            {
                while (accepted > 0)
                {
                    var last = _segments.Count - 1;
                    _segments.RemoveAt(last);
                    _segmentTombstones.RemoveAt(last);
                    accepted--;
                }
                foreach (var replacement in replacements)
                    replacement.Batch?.Dispose();
                return false;
            }

            foreach (var replacement in replacements.OrderByDescending(item => item.Index))
            {
                var old = _segments[replacement.Index];
                _segments.RemoveAt(replacement.Index);
                _segmentTombstones.RemoveAt(replacement.Index);
                old.Dispose();
            }
            RebaseTombstoneGrant(_segmentTombstones.Sum(bitmap => (long)(bitmap?.LongLength ?? 0)));
            Interlocked.Increment(ref _compactionCount);
            return true;
        }
        catch
        {
            foreach (var replacement in replacements)
                if (replacement.Batch != null && !_segments.Contains(replacement.Batch))
                    replacement.Batch.Dispose();
            throw;
        }
    }

    private static long CountTombstones(byte[]? bitmap)
    {
        if (bitmap == null) return 0;
        long count = 0;
        foreach (var value in bitmap) count += BitOperations.PopCount((uint)value);
        return count;
    }

    private static SelectionVector CreateLiveSelection(int rowCount, byte[] tombstones)
    {
        var rows = new List<int>(rowCount);
        for (var row = 0; row < rowCount; row++)
            if ((tombstones[row >> 3] & (1 << (row & 7))) == 0) rows.Add(row);
        return SelectionVector.FromIndices(rows);
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
        lock (_segments)
        {
            _segments.Add(segment);
            _segmentTombstones.Add(null);
        }
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

    private sealed class TransactionSnapshot : IDisposable
    {
        private ColumnBatch[]? _segments;
        private byte[]?[]? _tombstones;

        public TransactionSnapshot(ColumnBatch[] segments, byte[]?[] tombstones, long rowCount)
        {
            _segments = segments;
            _tombstones = tombstones;
            RowCount = rowCount;
        }

        public long RowCount { get; }

        public (ColumnBatch[] Segments, byte[]?[] Tombstones) TakeSegments()
        {
            var segments = _segments ?? throw new ObjectDisposedException(nameof(TransactionSnapshot));
            var tombstones = _tombstones ?? throw new ObjectDisposedException(nameof(TransactionSnapshot));
            _segments = null;
            _tombstones = null;
            return (segments, tombstones);
        }

        public void Dispose()
        {
            var segments = Interlocked.Exchange(ref _segments, null);
            Interlocked.Exchange(ref _tombstones, null);
            if (segments != null) foreach (var segment in segments) segment.Dispose();
        }
    }
}
