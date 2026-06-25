using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Execution;
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
    private readonly IExecutionContext _context;
    private readonly WindowEngine _inMemoryEngine;
    private readonly ExternalSortEngine _sortEngine;
    private readonly ILogger _logger;
    private readonly IBufferManager? _bufferManager;
    public int PartitionCount => Math.Max(1, _context.ExternalHashPartitions);

    public ExternalWindowEngine(IExecutionContext context, WindowEngine inMemoryEngine, ILogger logger)
    {
        _context = context;
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

    public async IAsyncEnumerable<Row> ApplyWindowFunctionsExternal(IAsyncEnumerable<Row> inputStream, SelectStatement stmt)
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

                currentStream = ProcessWindowGroup(currentStream, group, stmt);

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

    private async IAsyncEnumerable<Row> ProcessWindowGroup(IAsyncEnumerable<Row> stream, WindowGroup group, SelectStatement stmt)
    {
        _logger.WriteLine($"[blue]   - Group: {group.Columns.Count} cols, PARTITION BY ({(group.Signature.PartitionBy?.Count ?? 0)} expressions)[/]");

        var partitionInfos = await PartitionStream(stream, group.Signature.PartitionBy);
        try
        {
            foreach (var info in partitionInfos)
            {
                bool useDeepSpill = info.RowCount > _context.WindowSpillThreshold;

                if (useDeepSpill && IsDeepSpillCompatible(group))
                {
                    _logger.WriteLine($"[magenta]     * DEEP-SPILL: Partition has {info.RowCount:N0} rows (threshold: {_context.WindowSpillThreshold:N0}). Processing via streaming.[/]");
                    await foreach (var row in ProcessBucketDeepSpill(info.Name, group, stmt))
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
            new[] { "ROW_NUMBER", "RANK", "DENSE_RANK" }.Contains(f.FunctionName.ToUpperInvariant()));
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
                object? winVal = name_func switch
                {
                    "ROW_NUMBER" => (decimal)rowNumber,
                    "RANK" => (decimal)currentRank,
                    "DENSE_RANK" => (decimal)currentDenseRank,
                    _ => null
                };
                row[$"WINDOW_{f.ToSql().ToUpperInvariant()}"] = winVal;
            }

            yield return row;
            prevRow = row;
            prevPartitionKeys = currentPartitionKeys;
            prevSortKeys = currentSortKeys;
        }
    }

    private async Task<PartitionInfo[]> PartitionStream(IAsyncEnumerable<Row> stream, List<Expression>? partitionBy)
    {
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
            await foreach (var row in stream)
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
        }

        return names.Select((p, i) => new PartitionInfo(p, counts[i])).ToArray();
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

