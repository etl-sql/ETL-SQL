using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Engines;
/// <summary>
/// High-scale window function processor that spills partitions to disk when data exceeds memory thresholds.
/// Supports multi-pass grouping for incompatible window signatures (different PARTITION BY).
/// </summary>
public class ExternalWindowEngine
{
    private const int MaxFanOutSampleRows = 4096;
    private const long MaxFanOutSampleBytes = 16L * 1024 * 1024;

    private readonly IExecutionContext _context;
    private readonly WindowEngine _inMemoryEngine;
    private readonly ExternalSortEngine _sortEngine;
    private readonly ILogger _logger;
    private readonly IBufferManager? _bufferManager;
    private int _partitionCount;
    public int PartitionCount => _partitionCount;
    internal long ColumnarWindowScanRows { get; private set; }

    public ExternalWindowEngine(IExecutionContext context, WindowEngine inMemoryEngine, ILogger logger)
    {
        _context = context;
        _partitionCount = Math.Max(1, context.ExternalHashPartitions);
        _inMemoryEngine = inMemoryEngine;
        _sortEngine = new ExternalSortEngine(context, logger);
        _logger = logger;
        _bufferManager = _context.ServiceProvider?.GetService<IBufferManager>();
    }

    private record WindowSignature
    {
        public List<Expression>? PartitionBy { get; }
        public List<OrderByClause>? OrderBy { get; }
        public string PartitionKey { get; }
        public string OrderKey { get; }
        private readonly int _cachedHash;

        public WindowSignature(List<Expression>? partitionBy, List<OrderByClause>? orderBy)
        {
            PartitionBy = partitionBy;
            OrderBy = orderBy;
            PartitionKey = partitionBy == null ? "" : string.Join(",", partitionBy.Select(e => e.ToSql()));
            OrderKey = orderBy == null ? "" : string.Join(",", orderBy.Select(o => o.ToSql()));
            _cachedHash = HashCode.Combine(PartitionKey, OrderKey);
        }

        public virtual bool Equals(WindowSignature? other)
        {
            if (other == null) return false;
            return PartitionKey == other.PartitionKey && OrderKey == other.OrderKey;
        }

        public override int GetHashCode() => _cachedHash;
    }

    private class WindowGroup
    {
        public WindowSignature Signature { get; }
        public List<SelectColumn> Columns { get; } = new();
        public WindowGroup(WindowSignature sig) => Signature = sig;
    }

    public async IAsyncEnumerable<Row> ApplyWindowFunctionsExternal(IAsyncEnumerable<Row> inputStream, SelectStatement stmt, long? knownRowCount = null, long? knownInputBytes = null)
    {
        using var cursor = _bufferManager != null ? await _bufferManager.AcquireCursorAsync(_context.SessionId ?? "DEFAULT", owner: this) : null;
        var allWindowCalls = stmt.Columns
            .Where(c => WindowEngine.ContainsWindowFunction(c.Expression))
            .SelectMany(c => WindowEngine.CollectWindowCalls(c.Expression))
            .GroupBy(f => f.ToSql().ToUpperInvariant())
            .Select(g => g.First())
            .ToList();
        if (allWindowCalls.Count == 0)
        {
            await foreach (var row in inputStream) yield return row;
            yield break;
        }

        var groups = new List<WindowGroup>();
        foreach (var f in allWindowCalls)
        {
            var sig = new WindowSignature(f.Window!.PartitionBy, f.Window.OrderBy);
            var group = groups.FirstOrDefault(g => g.Signature.Equals(sig));
            if (group == null)
            {
                group = new WindowGroup(sig);
                groups.Add(group);
            }
            group.Columns.Add(new SelectColumn(f));
        }

        _logger.WriteLine($"[yellow]HYPER-SCALE: Processing {allWindowCalls.Count} window functions across {groups.Count} signature groups.[/]");
        _context.Telemetry.PartitionsCount = groups.Count;

        IAsyncEnumerable<Row> currentStream = inputStream;
        var intermediates = new List<string>();

        try
        {
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                bool isLastGroup = (i == groups.Count - 1);

                currentStream = ProcessWindowGroup(
                    currentStream, group, stmt,
                    knownRowCount: i == 0 ? knownRowCount : null,
                    knownInputBytes: i == 0 ? knownInputBytes : null);

                if (!isLastGroup)
                {
                    var prefix = Guid.NewGuid().ToString("N");
                    var intermediateName = $"inter_pass_{prefix}_{i}.tmp";
                    intermediates.Add(intermediateName);
                    await SpillStreamToDisk(currentStream, intermediateName);
                    currentStream = ReadPartitionStream(intermediateName);
                }
            }

            await foreach (var row in currentStream)
            {
                yield return row;
            }
        }
        finally
        {
            foreach (var intermediateName in intermediates)
            {
                try
                {
                    _context.SpillStore.DeleteChunk(intermediateName);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error cleaning up intermediate window pass {intermediateName}: {ex.Message}");
                }
            }
        }
    }

    private async IAsyncEnumerable<Row> ProcessWindowGroup(
        IAsyncEnumerable<Row> stream,
        WindowGroup group,
        SelectStatement stmt,
        long? knownRowCount,
        long? knownInputBytes)
    {
        _logger.WriteLine($"[blue]   - Group: {group.Columns.Count} cols, PARTITION BY ({(group.Signature.PartitionBy?.Count ?? 0)} expressions)[/]");

        var partitionInfos = await PartitionStream(
            stream, group.Signature.PartitionBy, knownRowCount, knownInputBytes);
        try
        {
            foreach (var info in partitionInfos)
            {
                bool useDeepSpill = info.RowCount > _context.WindowSpillThreshold;

                if (useDeepSpill && IsOrderedPartitionValueReplayCompatible(group))
                {
                    _logger.WriteLine($"[magenta]     * ORDERED-VALUE-SPILL: Partition has {info.RowCount:N0} rows (threshold: {_context.WindowSpillThreshold:N0}). Processing via sorted value replay.[/]");
                    await foreach (var row in ProcessBucketOrderedValueReplay(info.Name, group))
                    {
                        yield return row;
                    }
                }
                else if (useDeepSpill && IsDistributionReplayCompatible(group))
                {
                    _logger.WriteLine($"[magenta]     * DISTRIBUTION-SPILL: Partition has {info.RowCount:N0} rows (threshold: {_context.WindowSpillThreshold:N0}). Processing via sorted cardinality replay.[/]");
                    await foreach (var row in ProcessBucketDistributionReplay(info.Name, group))
                    {
                        yield return row;
                    }
                }
                else if (useDeepSpill && IsLeadSpillCompatible(group))
                {
                    _logger.WriteLine($"[magenta]     * LEAD-SPILL: Partition has {info.RowCount:N0} rows (threshold: {_context.WindowSpillThreshold:N0}). Processing via bounded lookahead.[/]");
                    await foreach (var row in ProcessBucketLeadSpill(info.Name, group))
                    {
                        yield return row;
                    }
                }
                else if (useDeepSpill && IsDeepSpillCompatible(group))
                {
                    _logger.WriteLine($"[magenta]     * DEEP-SPILL: Partition has {info.RowCount:N0} rows (threshold: {_context.WindowSpillThreshold:N0}). Processing via streaming.[/]");
                    await foreach (var row in ProcessBucketDeepSpill(info.Name, group, stmt))
                    {
                        yield return row;
                    }
                }
                else if (useDeepSpill && IsPartitionReplayCompatible(group))
                {
                    _logger.WriteLine($"[magenta]     * PARTITION-REPLAY-SPILL: Partition has {info.RowCount:N0} rows (threshold: {_context.WindowSpillThreshold:N0}). Processing via two-pass streaming state.[/]");
                    await foreach (var row in ProcessBucketPartitionReplaySpill(info.Name, group))
                    {
                        yield return row;
                    }
                }
                else
                {
                    var bucketRows = await ReadPartitionStream(info.Name).ToListAsync();
                    if (bucketRows.Count > 0)
                    {
                        var groupStmt = new SelectStatement(group.Columns, null, stmt.FromTable, new List<JoinClause>(), null, null, null, group.Signature.OrderBy);
                        var processedRows = await _inMemoryEngine.ApplyWindowFunctions(bucketRows, groupStmt);
                        foreach (var row in processedRows) yield return row;
                    }
                }
            }
        }
        finally
        {
            foreach (var info in partitionInfos)
            {
                try
                {
                    _context.SpillStore.DeleteChunk(info.Name);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error cleaning up external window partition {info.Name}: {ex.Message}");
                }
            }
        }
    }

    private record PartitionInfo(string Name, long RowCount);

    private bool IsDeepSpillCompatible(WindowGroup group)
    {
        return group.Columns.All(c =>
            c.Expression is FunctionCallExpression f &&
            (new[] { "ROW_NUMBER", "RANK", "DENSE_RANK" }.Contains(f.FunctionName.ToUpperInvariant())
                || IsRunningAggregateCompatible(f)
                || IsSlidingAggregateCompatible(f)
                || IsBoundedValueCompatible(f)));
    }

    private static bool IsLeadSpillCompatible(WindowGroup group)
    {
        return group.Signature.OrderBy is { Count: > 0 }
            && group.Columns.All(c =>
                c.Expression is FunctionCallExpression f
                && f.FunctionName.Equals("LEAD", StringComparison.OrdinalIgnoreCase)
                && f.Arguments.Count is >= 1 and <= 3
                && TryGetOffset(f, out _));
    }

    private static bool IsDistributionReplayCompatible(WindowGroup group)
    {
        return group.Signature.OrderBy is { Count: > 0 }
            && group.Columns.All(c =>
            {
                if (c.Expression is not FunctionCallExpression f) return false;
                return f.FunctionName.ToUpperInvariant() switch
                {
                    "PERCENT_RANK" => f.Arguments.Count == 0,
                    "CUME_DIST" => f.Arguments.Count == 0,
                    "NTILE" => f.Arguments.Count == 1 && TryGetPositiveLiteral(f.Arguments[0], out _),
                    _ => false
                };
            });
    }

    private static bool IsOrderedPartitionValueReplayCompatible(WindowGroup group)
    {
        return group.Signature.OrderBy is { Count: > 0 }
            && group.Columns.All(c =>
                c.Expression is FunctionCallExpression f
                && f.FunctionName.ToUpperInvariant() is "FIRST_VALUE" or "LAST_VALUE"
                && f.Arguments.Count == 1
                && f.Filter == null
                && f.Window?.Frame == null);
    }

    private static bool IsBoundedValueCompatible(FunctionCallExpression f)
    {
        return f.FunctionName.ToUpperInvariant() switch
        {
            "FIRST_VALUE" => f.Arguments.Count == 1,
            "LAG" => f.Arguments.Count is >= 1 and <= 3 && TryGetLagOffset(f, out _),
            "NTH_VALUE" => IsCumulativeRowsFrame(f.Window?.Frame)
                && f.Arguments.Count == 2
                && TryGetPositiveLiteral(f.Arguments[1], out _),
            _ => false
        };
    }

    private static bool IsCumulativeRowsFrame(WindowFrame? frame)
    {
        return frame != null
            && frame.Type == WindowFrameType.ROWS
            && frame.StartBound == WindowFrameBoundType.UNBOUNDED_PRECEDING
            && frame.EndBound is null or WindowFrameBoundType.CURRENT_ROW
            && frame.Exclusion == WindowFrameExclusion.NoOthers;
    }

    private static bool TryGetPositiveLiteral(Expression expression, out int value)
    {
        value = 0;
        if (expression is not LiteralExpression literal) return false;
        try
        {
            value = Convert.ToInt32(literal.Value);
            return value > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetNonNegativeLiteral(Expression? expression, out int value)
    {
        value = 0;
        if (expression is not LiteralExpression literal) return false;
        try
        {
            value = Convert.ToInt32(literal.Value);
            return value >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetLagOffset(FunctionCallExpression f, out int offset)
        => TryGetOffset(f, out offset);

    private static bool TryGetOffset(FunctionCallExpression f, out int offset)
    {
        offset = 1;
        if (f.Arguments.Count < 2) return true;
        if (f.Arguments[1] is not LiteralExpression literal) return false;
        try
        {
            offset = Convert.ToInt32(literal.Value);
            return offset >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRunningAggregateCompatible(FunctionCallExpression f)
    {
        if (f.Window?.Frame is not WindowFrame frame) return false;
        if (f.IsDistinct
            || frame.Type != WindowFrameType.ROWS
            || frame.StartBound != WindowFrameBoundType.UNBOUNDED_PRECEDING
            || frame.EndBound is not null and not WindowFrameBoundType.CURRENT_ROW
            || frame.Exclusion != WindowFrameExclusion.NoOthers)
            return false;

        return f.FunctionName.ToUpperInvariant() switch
        {
            "COUNT" => f.Arguments.Count <= 1,
            "SUM" or "AVG" or "MIN" or "MAX" => f.Arguments.Count == 1,
            _ => false
        };
    }

    private static bool IsSlidingAggregateCompatible(FunctionCallExpression f)
    {
        if (f.Window?.Frame is not WindowFrame frame) return false;
        if (f.IsDistinct
            || frame.Type != WindowFrameType.ROWS
            || frame.StartBound != WindowFrameBoundType.PRECEDING
            || !TryGetNonNegativeLiteral(frame.StartValue, out _)
            || frame.EndBound is not null and not WindowFrameBoundType.CURRENT_ROW
            || frame.Exclusion != WindowFrameExclusion.NoOthers)
            return false;

        return f.FunctionName.ToUpperInvariant() switch
        {
            "COUNT" => f.Arguments.Count <= 1,
            "SUM" or "AVG" or "MIN" or "MAX" => f.Arguments.Count == 1,
            _ => false
        };
    }

    private static bool IsPartitionReplayCompatible(WindowGroup group)
    {
        return group.Columns.All(c =>
        {
            if (c.Expression is not FunctionCallExpression f || f.Window == null) return false;
            if (f.IsDistinct || f.Window.Frame != null || f.Window.OrderBy.Count > 0) return false;

            var name = f.FunctionName.ToUpperInvariant();
            return name switch
            {
                "COUNT" => f.Arguments.Count <= 1,
                "SUM" or "AVG" or "MIN" or "MAX" => f.Arguments.Count == 1,
                "FIRST_VALUE" or "LAST_VALUE" => f.Arguments.Count == 1 && f.Filter == null,
                _ => false
            };
        });
    }

    private async IAsyncEnumerable<Row> ProcessBucketPartitionReplaySpill(string name, WindowGroup group)
    {
        var results = await TryScanPartitionReplayBatches(name, group);
        if (results == null)
            results = await ScanPartitionReplayRows(name, group);

        await foreach (var row in ReadPartitionStream(name))
        {
            foreach (var (key, value) in results)
                row[key] = value;
            yield return row;
        }
    }

    private async Task<Dictionary<string, object?>?> TryScanPartitionReplayBatches(
        string name,
        WindowGroup group)
    {
        var functions = group.Columns.Select(column => (FunctionCallExpression)column.Expression).ToList();
        if (functions.Any(function => function.Filter != null || !TryGetBatchArgument(function, out _)))
            return null;
        var accumulators = group.Columns
            .Where(c => IsPartitionAggregate((FunctionCallExpression)c.Expression))
            .Select(c => (Function: (FunctionCallExpression)c.Expression, Accumulator: new StreamingWindowAggregate()))
            .ToList();
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        long scannedRows = 0;
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        if (reader is not IColumnarSpillReader columnarReader) return null;
        await foreach (var batch in columnarReader.AsColumnBatchesAsync())
        {
            using (batch)
            {
                int? Ordinal(FunctionCallExpression function)
                {
                    if (!TryGetBatchArgument(function, out var argument) || argument == null) return null;
                    return batch.Schema.GetOrdinal(argument);
                }
                var aggregateOrdinals = accumulators.Select(item => Ordinal(item.Function)).ToArray();
                var valueFunctions = functions
                    .Where(function => function.FunctionName.Equals("FIRST_VALUE", StringComparison.OrdinalIgnoreCase)
                        || function.FunctionName.Equals("LAST_VALUE", StringComparison.OrdinalIgnoreCase))
                    .Select(function => (Function: function, Ordinal: Ordinal(function)!.Value))
                    .ToArray();
                for (var rowIndex = 0; rowIndex < batch.RowCount; rowIndex++)
                {
                    for (var i = 0; i < accumulators.Count; i++)
                    {
                        var (function, accumulator) = accumulators[i];
                        var value = aggregateOrdinals[i] is { } ordinal
                            ? RowPacker.ReadBatchValue(batch, ordinal, rowIndex)
                            : null;
                        accumulator.Add(function.FunctionName, value, _context, IsCountStar(function));
                    }
                    foreach (var (function, ordinal) in valueFunctions)
                    {
                        var key = $"WINDOW_{function.ToSql().ToUpperInvariant()}";
                        var value = RowPacker.ReadBatchValue(batch, ordinal, rowIndex);
                        if (function.FunctionName.Equals("LAST_VALUE", StringComparison.OrdinalIgnoreCase)
                            || !values.ContainsKey(key))
                            values[key] = value;
                    }
                }
                scannedRows += batch.RowCount;
            }
        }
        foreach (var (function, accumulator) in accumulators)
            values[$"WINDOW_{function.ToSql().ToUpperInvariant()}"] = accumulator.GetValue(function.FunctionName);
        ColumnarWindowScanRows += scannedRows;
        return values;
    }

    private static bool TryGetBatchArgument(FunctionCallExpression function, out string? argument)
    {
        argument = null;
        if (IsCountStar(function)) return true;
        if (function.Arguments.Count != 1 || function.Arguments[0] is not IdentifierExpression identifier)
            return false;
        argument = identifier.Name.Split('.').Last();
        return argument != "*";
    }

    private async Task<Dictionary<string, object?>> ScanPartitionReplayRows(string name, WindowGroup group)
    {
        var accumulators = group.Columns
            .Where(c => IsPartitionAggregate((FunctionCallExpression)c.Expression))
            .Select(c => (Function: (FunctionCallExpression)c.Expression, Accumulator: new StreamingWindowAggregate()))
            .ToList();
        Row? firstRow = null;
        Row? lastRow = null;

        await foreach (var row in ReadPartitionStream(name))
        {
            firstRow ??= row;
            lastRow = row;
            foreach (var (f, acc) in accumulators)
            {
                if (f.Filter != null && !await _context.EvaluateCondition(f.Filter, row))
                    continue;

                object? value = null;
                if (f.Arguments.Count > 0)
                    value = await _context.EvaluateValue(f.Arguments[0], row);

                acc.Add(f.FunctionName, value, _context, IsCountStar(f));
            }
        }

        var results = accumulators.ToDictionary(
            x => $"WINDOW_{x.Function.ToSql().ToUpperInvariant()}",
            x => x.Accumulator.GetValue(x.Function.FunctionName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var column in group.Columns)
        {
            var f = (FunctionCallExpression)column.Expression;
            var sourceRow = f.FunctionName.Equals("FIRST_VALUE", StringComparison.OrdinalIgnoreCase)
                ? firstRow
                : f.FunctionName.Equals("LAST_VALUE", StringComparison.OrdinalIgnoreCase)
                    ? lastRow
                    : null;
            if (sourceRow != null)
            {
                results[$"WINDOW_{f.ToSql().ToUpperInvariant()}"] =
                    await _context.EvaluateValue(f.Arguments[0], sourceRow);
            }
        }

        return results;
    }

    private static bool IsPartitionAggregate(FunctionCallExpression f)
    {
        return f.FunctionName.ToUpperInvariant() is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX";
    }

    private static bool IsCountStar(FunctionCallExpression f)
        => f.FunctionName.Equals("COUNT", StringComparison.OrdinalIgnoreCase)
            && (f.Arguments.Count == 0
                || f.Arguments[0] is IdentifierExpression id && id.Name == "*");

    private sealed class StreamingWindowAggregate
    {
        private long _count;
        private long _nonNullCount;
        private decimal _sum;
        private object? _min;
        private object? _max;
        private bool _allIntegers = true;

        public void Add(string functionName, object? value, IExecutionContext context, bool countStar)
        {
            var name = functionName.ToUpperInvariant();
            if (name == "COUNT")
            {
                if (countStar || value is not null and not DBNull)
                    _count++;
                return;
            }

            if (value is null or DBNull) return;

            _nonNullCount++;
            switch (name)
            {
                case "SUM":
                case "AVG":
                    _sum += Convert.ToDecimal(value);
                    _allIntegers &= IsIntegerValue(value);
                    break;
                case "MIN":
                    if (_min == null || context.CompareConstants(value, _min) < 0)
                        _min = value;
                    break;
                case "MAX":
                    if (_max == null || context.CompareConstants(value, _max) > 0)
                        _max = value;
                    break;
            }
        }

        public object? GetValue(string functionName)
        {
            return functionName.ToUpperInvariant() switch
            {
                "COUNT" => (decimal)_count,
                "SUM" => _nonNullCount == 0 ? null : _sum,
                "AVG" => GetAverage(),
                "MIN" => _min,
                "MAX" => _max,
                _ => null
            };
        }

        private object? GetAverage()
        {
            if (_nonNullCount == 0) return null;
            var avg = _sum / _nonNullCount;
            return _allIntegers ? Math.Truncate(avg) : avg;
        }

        public static bool IsIntegerValue(object value)
        {
            return value is sbyte or byte or short or ushort or int or uint or long or ulong
                or System.Numerics.BigInteger;
        }
    }

    private sealed class SlidingWindowAggregate
    {
        private readonly Queue<(long Sequence, bool Included, object? Value)> _values = new();
        private readonly LinkedList<(long Sequence, object Value)> _minimums = new();
        private readonly LinkedList<(long Sequence, object Value)> _maximums = new();
        private long _sequence;
        private long _count;
        private long _nonNullCount;
        private long _nonIntegerCount;
        private decimal _sum;

        public void Add(string functionName, object? value, bool included, bool countStar, int windowSize, IExecutionContext context)
        {
            var sequence = _sequence++;
            _values.Enqueue((sequence, included, value));
            Apply(functionName, value, included, countStar, 1);
            if (included && value is not null and not DBNull)
            {
                if (functionName.Equals("MIN", StringComparison.OrdinalIgnoreCase))
                {
                    while (_minimums.Last != null && context.CompareConstants(_minimums.Last.Value.Value, value) >= 0)
                        _minimums.RemoveLast();
                    _minimums.AddLast((sequence, value));
                }
                else if (functionName.Equals("MAX", StringComparison.OrdinalIgnoreCase))
                {
                    while (_maximums.Last != null && context.CompareConstants(_maximums.Last.Value.Value, value) <= 0)
                        _maximums.RemoveLast();
                    _maximums.AddLast((sequence, value));
                }
            }
            if (_values.Count > windowSize)
            {
                var removed = _values.Dequeue();
                Apply(functionName, removed.Value, removed.Included, countStar, -1);
                if (_minimums.First?.Value.Sequence == removed.Sequence)
                    _minimums.RemoveFirst();
                if (_maximums.First?.Value.Sequence == removed.Sequence)
                    _maximums.RemoveFirst();
            }
        }

        public object? GetValue(string functionName)
        {
            return functionName.ToUpperInvariant() switch
            {
                "COUNT" => (decimal)_count,
                "SUM" => _nonNullCount == 0 ? null : _sum,
                "AVG" => GetAverage(),
                "MIN" => _minimums.First?.Value.Value,
                "MAX" => _maximums.First?.Value.Value,
                _ => null
            };
        }

        private void Apply(string functionName, object? value, bool included, bool countStar, int direction)
        {
            if (!included) return;
            if (functionName.Equals("COUNT", StringComparison.OrdinalIgnoreCase))
            {
                if (countStar || value is not null and not DBNull)
                    _count += direction;
                return;
            }
            if (value is null or DBNull) return;
            _nonNullCount += direction;
            if (!StreamingWindowAggregate.IsIntegerValue(value))
                _nonIntegerCount += direction;
            _sum += direction * Convert.ToDecimal(value);
        }

        private object? GetAverage()
        {
            if (_nonNullCount == 0) return null;
            var average = _sum / _nonNullCount;
            return _nonIntegerCount == 0 ? Math.Truncate(average) : average;
        }
    }

    private async IAsyncEnumerable<Row> ProcessBucketDeepSpill(string name, WindowGroup group, SelectStatement stmt)
    {
        var bucketRows = ReadPartitionStream(name);

        var sortCriteria = new List<OrderByClause>();
        if (group.Signature.PartitionBy != null)
        {
            foreach (var p in group.Signature.PartitionBy)
                sortCriteria.Add(new OrderByClause(p, false));
        }
        if (group.Signature.OrderBy != null)
        {
            sortCriteria.AddRange(group.Signature.OrderBy);
        }

        if (sortCriteria.Count > 0)
        {
            bucketRows = _sortEngine.SortStreamAsync(bucketRows, sortCriteria);
        }

        int rowNumber = 0;
        int currentRank = 1;
        int currentDenseRank = 1;
        Row? prevRow = null;
        object?[]? prevPartitionKeys = null;
        object?[]? prevSortKeys = null;
        var runningAggregates = new Dictionary<string, StreamingWindowAggregate>(StringComparer.OrdinalIgnoreCase);
        var slidingAggregates = new Dictionary<string, SlidingWindowAggregate>(StringComparer.OrdinalIgnoreCase);
        var maxLagOffset = group.Columns
            .Select(c => (FunctionCallExpression)c.Expression)
            .Where(f => f.FunctionName.Equals("LAG", StringComparison.OrdinalIgnoreCase))
            .Select(f => TryGetLagOffset(f, out var offset) ? offset : 0)
            .DefaultIfEmpty(0)
            .Max();
        var lagHistory = new Queue<Row>(Math.Max(1, maxLagOffset));
        Row? firstPartitionRow = null;
        var nthRows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);

        await foreach (var row in bucketRows)
        {
            object?[] currentPartitionKeys = new object?[group.Signature.PartitionBy?.Count ?? 0];
            if (group.Signature.PartitionBy != null)
            {
                for (int k = 0; k < group.Signature.PartitionBy.Count; k++)
                    currentPartitionKeys[k] = await _context.EvaluateValue(group.Signature.PartitionBy[k], row);
            }

            object?[] currentSortKeys = new object?[group.Signature.OrderBy?.Count ?? 0];
            if (group.Signature.OrderBy != null)
            {
                for (int k = 0; k < group.Signature.OrderBy.Count; k++)
                    currentSortKeys[k] = await _context.EvaluateValue(group.Signature.OrderBy[k].Expression, row);
            }

            bool partitionChanged = false;
            if (prevRow != null && group.Signature.PartitionBy != null && prevPartitionKeys != null)
            {
                for (int k = 0; k < group.Signature.PartitionBy.Count; k++)
                {
                    if (_context.CompareConstants(currentPartitionKeys[k], prevPartitionKeys[k]) != 0)
                    {
                        partitionChanged = true;
                        break;
                    }
                }
            }

            if (partitionChanged || prevRow == null)
            {
                rowNumber = 1;
                currentRank = 1;
                currentDenseRank = 1;
                runningAggregates.Clear();
                slidingAggregates.Clear();
                lagHistory.Clear();
                firstPartitionRow = row;
                nthRows.Clear();
            }
            else
            {
                rowNumber++;
                if (group.Signature.OrderBy != null && group.Signature.OrderBy.Count > 0)
                {
                    bool samePeer = true;
                    for (int k = 0; k < group.Signature.OrderBy.Count; k++)
                    {
                        if (prevSortKeys != null && _context.CompareConstants(currentSortKeys[k], prevSortKeys[k]) != 0)
                        {
                            samePeer = false;
                            break;
                        }
                    }

                    if (!samePeer)
                    {
                        currentDenseRank++;
                        currentRank = rowNumber;
                    }
                }
            }

            foreach (var col in group.Columns)
            {
                var f = (FunctionCallExpression)col.Expression;
                var name_func = f.FunctionName.ToUpperInvariant();
                object? winVal;
                if (IsRunningAggregateCompatible(f))
                {
                    var key = f.ToSql().ToUpperInvariant();
                    if (!runningAggregates.TryGetValue(key, out var accumulator))
                    {
                        accumulator = new StreamingWindowAggregate();
                        runningAggregates[key] = accumulator;
                    }

                    if (f.Filter == null || await _context.EvaluateCondition(f.Filter, row))
                    {
                        object? value = null;
                        if (f.Arguments.Count > 0)
                            value = await _context.EvaluateValue(f.Arguments[0], row);
                        accumulator.Add(f.FunctionName, value, _context, IsCountStar(f));
                    }

                    winVal = accumulator.GetValue(f.FunctionName);
                }
                else if (IsSlidingAggregateCompatible(f))
                {
                    var key = f.ToSql().ToUpperInvariant();
                    if (!slidingAggregates.TryGetValue(key, out var accumulator))
                    {
                        accumulator = new SlidingWindowAggregate();
                        slidingAggregates[key] = accumulator;
                    }
                    TryGetNonNegativeLiteral(f.Window!.Frame!.StartValue, out var preceding);
                    var included = f.Filter == null || await _context.EvaluateCondition(f.Filter, row);
                    object? value = null;
                    if (f.Arguments.Count > 0)
                        value = await _context.EvaluateValue(f.Arguments[0], row);
                    accumulator.Add(f.FunctionName, value, included, IsCountStar(f), preceding + 1, _context);
                    winVal = accumulator.GetValue(f.FunctionName);
                }
                else if (name_func == "FIRST_VALUE")
                {
                    winVal = firstPartitionRow != null
                        ? await _context.EvaluateValue(f.Arguments[0], firstPartitionRow)
                        : null;
                }
                else if (name_func == "LAG")
                {
                    TryGetLagOffset(f, out var offset);
                    if (offset == 0)
                    {
                        winVal = await _context.EvaluateValue(f.Arguments[0], row);
                    }
                    else if (lagHistory.Count >= offset)
                    {
                        var lagRow = lagHistory.ElementAt(lagHistory.Count - offset);
                        winVal = await _context.EvaluateValue(f.Arguments[0], lagRow);
                    }
                    else
                    {
                        winVal = f.Arguments.Count >= 3
                            ? await _context.EvaluateValue(f.Arguments[2], row)
                            : null;
                    }
                }
                else if (name_func == "NTH_VALUE")
                {
                    TryGetPositiveLiteral(f.Arguments[1], out var nth);
                    var key = f.ToSql().ToUpperInvariant();
                    if (rowNumber == nth)
                        nthRows[key] = row;
                    winVal = nthRows.TryGetValue(key, out var nthRow)
                        ? await _context.EvaluateValue(f.Arguments[0], nthRow)
                        : null;
                }
                else
                {
                    winVal = name_func switch
                    {
                        "ROW_NUMBER" => (decimal)rowNumber,
                        "RANK" => (decimal)currentRank,
                        "DENSE_RANK" => (decimal)currentDenseRank,
                        _ => null
                    };
                }
                row[$"WINDOW_{f.ToSql().ToUpperInvariant()}"] = winVal;
            }

            yield return row;
            if (maxLagOffset > 0)
            {
                lagHistory.Enqueue(row);
                while (lagHistory.Count > maxLagOffset)
                    lagHistory.Dequeue();
            }
            prevRow = row;
            prevPartitionKeys = currentPartitionKeys;
            prevSortKeys = currentSortKeys;
        }
    }

    private async IAsyncEnumerable<Row> ProcessBucketLeadSpill(string name, WindowGroup group)
    {
        var sortCriteria = new List<OrderByClause>();
        if (group.Signature.PartitionBy != null)
        {
            foreach (var expression in group.Signature.PartitionBy)
                sortCriteria.Add(new OrderByClause(expression, false));
        }
        sortCriteria.AddRange(group.Signature.OrderBy!);

        var rows = _sortEngine.SortStreamAsync(ReadPartitionStream(name), sortCriteria);
        var maxOffset = group.Columns
            .Select(c => (FunctionCallExpression)c.Expression)
            .Select(f => TryGetOffset(f, out var offset) ? offset : 0)
            .Max();
        var pending = new Queue<Row>(maxOffset + 1);
        object?[]? partitionKeys = null;

        await foreach (var row in rows)
        {
            var currentKeys = await EvaluatePartitionKeys(group.Signature.PartitionBy, row);
            if (partitionKeys != null && !PartitionKeysEqual(partitionKeys, currentKeys))
            {
                while (pending.Count > 0)
                    yield return await CompleteLeadRow(pending, group, useDefaults: true);
            }

            partitionKeys = currentKeys;
            pending.Enqueue(row);
            if (pending.Count > maxOffset)
                yield return await CompleteLeadRow(pending, group, useDefaults: false);
        }

        while (pending.Count > 0)
            yield return await CompleteLeadRow(pending, group, useDefaults: true);
    }

    private async IAsyncEnumerable<Row> ProcessBucketDistributionReplay(string name, WindowGroup group)
    {
        var sortedName = $"win_dist_{Guid.NewGuid():N}.tmp";
        var annotatedName = $"win_cume_{Guid.NewGuid():N}.tmp";
        var sortCriteria = new List<OrderByClause>();
        if (group.Signature.PartitionBy != null)
        {
            foreach (var expression in group.Signature.PartitionBy)
                sortCriteria.Add(new OrderByClause(expression, false));
        }
        sortCriteria.AddRange(group.Signature.OrderBy!);

        var partitionCounts = new List<long>();
        var hasCumeDist = group.Columns.Any(c =>
            ((FunctionCallExpression)c.Expression).FunctionName.Equals("CUME_DIST", StringComparison.OrdinalIgnoreCase));
        object?[]? previousPartitionKeys = null;
        try
        {
            await using (var writer = await _context.SpillStore.CreateWriterAsync(sortedName))
            {
                await foreach (var row in _sortEngine.SortStreamAsync(ReadPartitionStream(name), sortCriteria))
                {
                    var keys = await EvaluatePartitionKeys(group.Signature.PartitionBy, row);
                    if (previousPartitionKeys == null || !PartitionKeysEqual(previousPartitionKeys, keys))
                        partitionCounts.Add(0);
                    partitionCounts[^1]++;
                    previousPartitionKeys = keys;
                    await writer.WriteRowAsync(row);
                }
            }

            IAsyncEnumerable<Row> replayRows = ReadPartitionStream(sortedName);
            if (hasCumeDist)
            {
                var reverseCriteria = new List<OrderByClause>();
                if (group.Signature.PartitionBy != null)
                {
                    foreach (var expression in group.Signature.PartitionBy)
                        reverseCriteria.Add(new OrderByClause(expression, false));
                }
                reverseCriteria.AddRange(group.Signature.OrderBy!.Select(c =>
                    new OrderByClause(c.Expression, !c.Descending)));

                var reversePartitionIndex = -1;
                long rowsSeen = 0;
                decimal currentCumeDist = 0m;
                previousPartitionKeys = null;
                object?[]? previousReverseSortKeys = null;
                await using (var writer = await _context.SpillStore.CreateWriterAsync(annotatedName))
                {
                    await foreach (var row in _sortEngine.SortStreamAsync(ReadPartitionStream(sortedName), reverseCriteria))
                    {
                        var partitionKeys = await EvaluatePartitionKeys(group.Signature.PartitionBy, row);
                        var sortKeys = await EvaluateOrderKeys(group.Signature.OrderBy!, row);
                        if (previousPartitionKeys == null || !PartitionKeysEqual(previousPartitionKeys, partitionKeys))
                        {
                            reversePartitionIndex++;
                            rowsSeen = 0;
                            currentCumeDist = 0m;
                            previousReverseSortKeys = null;
                        }

                        var newPeer = previousReverseSortKeys == null
                            || !PartitionKeysEqual(previousReverseSortKeys, sortKeys);
                        var partitionCount = partitionCounts[reversePartitionIndex];
                        if (newPeer)
                            currentCumeDist = (decimal)(partitionCount - rowsSeen) / partitionCount;
                        foreach (var column in group.Columns)
                        {
                            var f = (FunctionCallExpression)column.Expression;
                            if (f.FunctionName.Equals("CUME_DIST", StringComparison.OrdinalIgnoreCase))
                                row[$"WINDOW_{f.ToSql().ToUpperInvariant()}"] = currentCumeDist;
                        }

                        await writer.WriteRowAsync(row);
                        rowsSeen++;
                        previousPartitionKeys = partitionKeys;
                        previousReverseSortKeys = sortKeys;
                    }
                }
                replayRows = _sortEngine.SortStreamAsync(ReadPartitionStream(annotatedName), sortCriteria);
            }

            var partitionIndex = -1;
            long rowIndex = 0;
            long rank = 1;
            previousPartitionKeys = null;
            object?[]? previousSortKeys = null;
            await foreach (var row in replayRows)
            {
                var partitionKeys = await EvaluatePartitionKeys(group.Signature.PartitionBy, row);
                var sortKeys = await EvaluateOrderKeys(group.Signature.OrderBy!, row);
                if (previousPartitionKeys == null || !PartitionKeysEqual(previousPartitionKeys, partitionKeys))
                {
                    partitionIndex++;
                    rowIndex = 0;
                    rank = 1;
                    previousSortKeys = null;
                }
                else if (previousSortKeys != null && !PartitionKeysEqual(previousSortKeys, sortKeys))
                {
                    rank = rowIndex + 1;
                }

                var partitionCount = partitionCounts[partitionIndex];
                foreach (var column in group.Columns)
                {
                    var f = (FunctionCallExpression)column.Expression;
                    object? value;
                    if (f.FunctionName.Equals("PERCENT_RANK", StringComparison.OrdinalIgnoreCase))
                    {
                        value = partitionCount <= 1 ? 0m : (decimal)(rank - 1) / (partitionCount - 1);
                    }
                    else if (f.FunctionName.Equals("CUME_DIST", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    else
                    {
                        TryGetPositiveLiteral(f.Arguments[0], out var bucketCount);
                        var baseSize = partitionCount / bucketCount;
                        var extraRows = partitionCount % bucketCount;
                        value = baseSize == 0
                            ? (decimal)(rowIndex + 1)
                            : rowIndex < extraRows * (baseSize + 1)
                                ? (decimal)(rowIndex / (baseSize + 1) + 1)
                                : (decimal)((rowIndex - extraRows * (baseSize + 1)) / baseSize + extraRows + 1);
                    }
                    row[$"WINDOW_{f.ToSql().ToUpperInvariant()}"] = value;
                }

                yield return row;
                rowIndex++;
                previousPartitionKeys = partitionKeys;
                previousSortKeys = sortKeys;
            }
        }
        finally
        {
            try
            {
                _context.SpillStore.DeleteChunk(sortedName);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error cleaning up distribution window replay {sortedName}: {ex.Message}");
            }
            if (hasCumeDist)
            {
                try
                {
                    _context.SpillStore.DeleteChunk(annotatedName);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Error cleaning up cumulative distribution replay {annotatedName}: {ex.Message}");
                }
            }
        }
    }

    private async IAsyncEnumerable<Row> ProcessBucketOrderedValueReplay(string name, WindowGroup group)
    {
        var sortedName = $"win_value_{Guid.NewGuid():N}.tmp";
        var sortCriteria = new List<OrderByClause>();
        if (group.Signature.PartitionBy != null)
        {
            foreach (var expression in group.Signature.PartitionBy)
                sortCriteria.Add(new OrderByClause(expression, false));
        }
        sortCriteria.AddRange(group.Signature.OrderBy!);

        var partitionResults = new List<Dictionary<string, object?>>();
        object?[]? previousPartitionKeys = null;
        try
        {
            await using (var writer = await _context.SpillStore.CreateWriterAsync(sortedName))
            {
                await foreach (var row in _sortEngine.SortStreamAsync(ReadPartitionStream(name), sortCriteria))
                {
                    var keys = await EvaluatePartitionKeys(group.Signature.PartitionBy, row);
                    var newPartition = previousPartitionKeys == null || !PartitionKeysEqual(previousPartitionKeys, keys);
                    if (newPartition)
                        partitionResults.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

                    var results = partitionResults[^1];
                    foreach (var column in group.Columns)
                    {
                        var f = (FunctionCallExpression)column.Expression;
                        var resultKey = $"WINDOW_{f.ToSql().ToUpperInvariant()}";
                        if (newPartition || f.FunctionName.Equals("LAST_VALUE", StringComparison.OrdinalIgnoreCase))
                            results[resultKey] = await _context.EvaluateValue(f.Arguments[0], row);
                    }

                    previousPartitionKeys = keys;
                    await writer.WriteRowAsync(row);
                }
            }

            var partitionIndex = -1;
            previousPartitionKeys = null;
            await foreach (var row in ReadPartitionStream(sortedName))
            {
                var keys = await EvaluatePartitionKeys(group.Signature.PartitionBy, row);
                if (previousPartitionKeys == null || !PartitionKeysEqual(previousPartitionKeys, keys))
                    partitionIndex++;
                foreach (var (resultKey, value) in partitionResults[partitionIndex])
                    row[resultKey] = value;
                previousPartitionKeys = keys;
                yield return row;
            }
        }
        finally
        {
            try
            {
                _context.SpillStore.DeleteChunk(sortedName);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error cleaning up ordered value window replay {sortedName}: {ex.Message}");
            }
        }
    }

    private async Task<Row> CompleteLeadRow(Queue<Row> pending, WindowGroup group, bool useDefaults)
    {
        var row = pending.Peek();
        foreach (var column in group.Columns)
        {
            var f = (FunctionCallExpression)column.Expression;
            TryGetOffset(f, out var offset);
            object? value;
            if (!useDefaults || offset < pending.Count)
            {
                var source = offset == 0 ? row : pending.ElementAt(offset);
                value = await _context.EvaluateValue(f.Arguments[0], source);
            }
            else
            {
                value = f.Arguments.Count >= 3
                    ? await _context.EvaluateValue(f.Arguments[2], row)
                    : null;
            }
            row[$"WINDOW_{f.ToSql().ToUpperInvariant()}"] = value;
        }
        pending.Dequeue();
        return row;
    }

    private async Task<object?[]> EvaluatePartitionKeys(List<Expression>? expressions, Row row)
    {
        var keys = new object?[expressions?.Count ?? 0];
        if (expressions == null) return keys;
        for (var i = 0; i < expressions.Count; i++)
            keys[i] = await _context.EvaluateValue(expressions[i], row);
        return keys;
    }

    private async Task<object?[]> EvaluateOrderKeys(List<OrderByClause> clauses, Row row)
    {
        var keys = new object?[clauses.Count];
        for (var i = 0; i < clauses.Count; i++)
            keys[i] = await _context.EvaluateValue(clauses[i].Expression, row);
        return keys;
    }

    private bool PartitionKeysEqual(object?[] left, object?[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            if (_context.CompareConstants(left[i], right[i]) != 0)
                return false;
        }
        return true;
    }

    private async Task<PartitionInfo[]> PartitionStream(
        IAsyncEnumerable<Row> stream,
        List<Expression>? partitionBy,
        long? knownRowCount,
        long? knownInputBytes)
    {
        await using var enumerator = stream.GetAsyncEnumerator(_context.CancellationToken);
        var sample = new List<Row>(MaxFanOutSampleRows);
        long sampledBytes = 0;
        while (sample.Count < MaxFanOutSampleRows
            && sampledBytes < MaxFanOutSampleBytes
            && await enumerator.MoveNextAsync())
        {
            var row = enumerator.Current;
            sample.Add(row);
            sampledBytes = checked(sampledBytes + row.EstimateHeapBytes());
        }
        await ConfigurePartitionCount(sample, partitionBy, knownRowCount, knownInputBytes);

        var names = new string[PartitionCount];
        var counts = new long[PartitionCount];
        var writers = new ETL_SQL.Core.Spill.ISpillWriter[PartitionCount];

        for (int i = 0; i < PartitionCount; i++)
        {
            names[i] = $"win_part_{Guid.NewGuid():N}.tmp";
            writers[i] = await _context.SpillStore.CreateWriterAsync(names[i]);
        }

        try
        {
            await foreach (var row in ReplaySample(sample, enumerator))
            {
                int pIdx = 0;
                if (partitionBy != null && partitionBy.Count > 0)
                {
                    int hash = 17;
                    foreach (var expr in partitionBy)
                    {
                        var val = await _context.EvaluateValue(expr, row);
                        hash = hash * 31 + (val?.GetHashCode() ?? 0);
                    }
                    pIdx = Math.Abs(hash % PartitionCount);
                }

                await writers[pIdx].WriteRowAsync(row);
                counts[pIdx]++;
            }
        }
        finally
        {
            int usedCount = 0;
            foreach (var w in writers)
            {
                if (w != null)
                {
                    usedCount++;
                    await w.DisposeAsync();
                }
            }
            _context.Telemetry.PartitionsCount += usedCount;
            _context.Telemetry.PartitionPassCount++;
        }

        return names.Select((p, i) => new PartitionInfo(p, counts[i])).ToArray();
    }

    private async Task ConfigurePartitionCount(
        IReadOnlyList<Row> sample,
        List<Expression>? partitionBy,
        long? knownRowCount,
        long? knownInputBytes)
    {
        if (sample.Count == 0 || partitionBy == null || partitionBy.Count == 0) return;
        long inputBytes = 0;
        long keyBytes = 0;
        var frequencies = new Dictionary<CompoundKey, int>();
        foreach (var row in sample)
        {
            inputBytes = checked(inputBytes + row.EstimateHeapBytes());
            var values = new object?[partitionBy.Count];
            for (var i = 0; i < partitionBy.Count; i++)
                values[i] = CompoundKey.NormalizeValue(await _context.EvaluateValue(partitionBy[i], row));
            var key = new CompoundKey(values);
            keyBytes = checked(keyBytes + RowMemory.EstimateKeyBytes(key));
            frequencies[key] = frequencies.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var budget = MemoryGovernor.Ceiling(_context);
        if (budget <= 0) budget = Math.Max(1L, (long)_context.EffectiveOperatorMemoryGrantMB * 1024 * 1024);
        var hotFraction = frequencies.Values.Max() / (double)sample.Count;
        var hasExactTotal = knownRowCount >= 0 && knownInputBytes >= 0;
        var plannedRows = hasExactTotal ? knownRowCount!.Value : sample.Count;
        var plannedBytes = hasExactTotal ? knownInputBytes!.Value : inputBytes;
        var estimatedDistinct = hasExactTotal
            ? Math.Min(plannedRows, (long)Math.Ceiling(frequencies.Count * (plannedRows / (double)sample.Count)))
            : frequencies.Count;
        var plan = HashPartitionSizing.Calculate(
            plannedBytes,
            plannedRows,
            (int)Math.Min(int.MaxValue, keyBytes / sample.Count),
            budget,
            estimatedDistinctKeys: (int)Math.Min(int.MaxValue, estimatedDistinct),
            largestKeyFraction: hotFraction,
            minimumPartitions: hasExactTotal ? 1 : _partitionCount,
            maximumPartitions: Math.Max(1024, _partitionCount));
        _partitionCount = hasExactTotal ? plan.PartitionCount : Math.Max(_partitionCount, plan.PartitionCount);
        _logger.Debug(
            "External window sampled {SampleRows} rows ({SampleBytes} bytes) and selected fan-out {FanOut}; estimated passes={Passes}, hotKey={HotKey}.",
            sample.Count, inputBytes, _partitionCount, plan.EstimatedPartitionPasses, plan.HasUnsplittableHotKey);
    }

    private static async IAsyncEnumerable<Row> ReplaySample(
        IReadOnlyList<Row> sample,
        IAsyncEnumerator<Row> remainder)
    {
        foreach (var row in sample) yield return row;
        while (await remainder.MoveNextAsync()) yield return remainder.Current;
    }

    private async Task SpillStreamToDisk(IAsyncEnumerable<Row> stream, string name)
    {
        await using var writer = await _context.SpillStore.CreateWriterAsync(name);
        await foreach (var row in stream)
        {
            await writer.WriteRowAsync(row);
        }
    }

    private async IAsyncEnumerable<Row> ReadPartitionStream(string name)
    {
        await using var reader = await _context.SpillStore.CreateReaderAsync(name);
        await foreach (var row in reader.AsEnumerableAsync())
        {
            var unwrapped = new Row();
            row.ForEachColumn((k, v) => unwrapped[k] = v is JsonElement je ? SpillSerializationHelper.UnwrapJsonElement(je) : v);
            yield return unwrapped;
        }
    }

}

