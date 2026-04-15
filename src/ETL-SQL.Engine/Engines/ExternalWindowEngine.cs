using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Engine.Engines
{
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
        private readonly string _tempDir;
        private int PartitionCount => _context.ExternalHashPartitions;

        public ExternalWindowEngine(IExecutionContext context, WindowEngine inMemoryEngine, ILogger logger)
        {
            _context = context;
            _inMemoryEngine = inMemoryEngine;
            _sortEngine = new ExternalSortEngine(context, logger);
            _logger = logger;
            _tempDir = Path.Combine(Path.GetTempPath(), "ETL-SQL", "WinSpill", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        private record WindowSignature(List<Expression>? PartitionBy, List<OrderByClause>? OrderBy)
        {
            public virtual bool Equals(WindowSignature? other)
            {
                if (other == null) return false;
                
                string thisPartition = PartitionBy == null ? "" : string.Join(",", PartitionBy.Select(e => e.ToSql()));
                string otherPartition = other.PartitionBy == null ? "" : string.Join(",", other.PartitionBy.Select(e => e.ToSql()));
                if (thisPartition != otherPartition) return false;

                string thisOrder = OrderBy == null ? "" : string.Join(",", OrderBy.Select(o => o.ToSql()));
                string otherOrder = other.OrderBy == null ? "" : string.Join(",", other.OrderBy.Select(o => o.ToSql()));
                if (thisOrder != otherOrder) return false;

                return true;
            }

            public override int GetHashCode()
            {
                int hash = 17;
                if (PartitionBy != null) foreach (var p in PartitionBy) hash = hash * 31 + p.ToSql().GetHashCode();
                if (OrderBy != null) foreach (var o in OrderBy) hash = hash * 31 + o.ToSql().GetHashCode();
                return hash;
            }
        }

        private class WindowGroup
        {
            public WindowSignature Signature { get; }
            public List<SelectColumn> Columns { get; } = new();
            public WindowGroup(WindowSignature sig) => Signature = sig;
        }

        public async IAsyncEnumerable<Row> ApplyWindowFunctionsExternal(IAsyncEnumerable<Row> inputStream, SelectStatement stmt)
        {
            var windowCols = stmt.Columns.Where(c => c.Expression is FunctionCallExpression f && f.Window != null).ToList();
            if (windowCols.Count == 0)
            {
                await foreach (var row in inputStream) yield return row;
                yield break;
            }

            var groups = new List<WindowGroup>();
            foreach (var col in windowCols)
            {
                var f = (FunctionCallExpression)col.Expression;
                var sig = new WindowSignature(f.Window!.PartitionBy, f.Window.OrderBy);
                var group = groups.FirstOrDefault(g => g.Signature.Equals(sig));
                if (group == null)
                {
                    group = new WindowGroup(sig);
                    groups.Add(group);
                }
                group.Columns.Add(col);
            }

            _logger.WriteLine($"[yellow]HYPER-SCALE: Processing {windowCols.Count} window functions across {groups.Count} signature groups.[/]");

            try
            {
                IAsyncEnumerable<Row> currentStream = inputStream;

                for (int i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    bool isLastGroup = (i == groups.Count - 1);

                    currentStream = ProcessWindowGroup(currentStream, group, stmt);

                    if (!isLastGroup)
                    {
                        var intermediatePath = Path.Combine(_tempDir, $"inter_pass_{i}.tmp");
                        await SpillStreamToDisk(currentStream, intermediatePath);
                        currentStream = ReadPartitionStream(intermediatePath);
                    }
                }

                await foreach (var row in currentStream)
                {
                    yield return row;
                }
            }
            finally
            {
                CleanupTempDir();
            }
        }

        private async IAsyncEnumerable<Row> ProcessWindowGroup(IAsyncEnumerable<Row> stream, WindowGroup group, SelectStatement stmt)
        {
            _logger.WriteLine($"[blue]   - Group: {group.Columns.Count} cols, PARTITION BY ({(group.Signature.PartitionBy?.Count ?? 0)} expressions)[/]");

            var partitionInfos = await PartitionStream(stream, group.Signature.PartitionBy);

            foreach (var info in partitionInfos)
            {
                if (!File.Exists(info.Path)) continue;

                bool useDeepSpill = info.RowCount > _context.WindowSpillThreshold;
                
                if (useDeepSpill && IsDeepSpillCompatible(group))
                {
                    _logger.WriteLine($"[magenta]     * DEEP-SPILL: Partition has {info.RowCount:N0} rows (threshold: {_context.WindowSpillThreshold:N0}). Processing via streaming.[/]");
                    await foreach (var row in ProcessBucketDeepSpill(info.Path, group, stmt))
                    {
                        yield return row;
                    }
                }
                else
                {
                    var bucketRows = ReadPartitionStream(info.Path);
                    if (group.Signature.OrderBy != null && group.Signature.OrderBy.Count > 0)
                    {
                        bucketRows = _sortEngine.SortStreamAsync(bucketRows, group.Signature.OrderBy);
                    }

                    var rows = await bucketRows.ToListAsync();
                    if (rows.Count > 0)
                    {
                        var groupStmt = new SelectStatement(group.Columns, null, stmt.FromTable, new List<JoinClause>(), null, null, null, group.Signature.OrderBy);
                        var processedRows = await _inMemoryEngine.ApplyWindowFunctions(rows, groupStmt);
                        foreach (var row in processedRows) yield return row;
                    }
                }

                File.Delete(info.Path);
            }
        }

        private record PartitionInfo(string Path, long RowCount);

        private bool IsDeepSpillCompatible(WindowGroup group)
        {
            return group.Columns.All(c => 
                c.Expression is FunctionCallExpression f && 
                new[] { "ROW_NUMBER", "RANK", "DENSE_RANK" }.Contains(f.FunctionName.ToUpperInvariant()));
        }

        private async IAsyncEnumerable<Row> ProcessBucketDeepSpill(string path, WindowGroup group, SelectStatement stmt)
        {
            var bucketRows = ReadPartitionStream(path);
            
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
                    var name = f.FunctionName.ToUpperInvariant();
                    object? winVal = name switch
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
            var paths = new string[PartitionCount];
            var counts = new long[PartitionCount];
            var writers = new StreamWriter[PartitionCount];

            for (int i = 0; i < PartitionCount; i++)
            {
                paths[i] = Path.Combine(_tempDir, $"win_part_{Guid.NewGuid():N}.tmp");
                writers[i] = new StreamWriter(paths[i]);
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

                    var json = JsonSerializer.Serialize(row.Columns);
                    _context.TotalSpilledBytes += System.Text.Encoding.UTF8.GetByteCount(json) + 1;
                    await writers[pIdx].WriteLineAsync(json);
                    counts[pIdx]++;
                }
            }
            finally
            {
                int usedCount = 0;
                foreach (var w in writers)
                {
                    if (w.BaseStream.Length > 0) usedCount++;
                    w.Flush();
                    w.Dispose();
                }
                _context.PartitionsCount += usedCount;
            }

            return paths.Select((p, i) => new PartitionInfo(p, counts[i])).ToArray();
        }

        private async Task SpillStreamToDisk(IAsyncEnumerable<Row> stream, string path)
        {
            using var writer = new StreamWriter(path);
            await foreach (var row in stream)
            {
                var json = JsonSerializer.Serialize(row.Columns);
                await writer.WriteLineAsync(json);
            }
        }

        private async IAsyncEnumerable<Row> ReadPartitionStream(string path)
        {
            using var reader = new StreamReader(path);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var cols = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
                if (cols != null)
                {
                    var row = new Row();
                    foreach (var kvp in cols) row[kvp.Key] = JsonElementToValue(kvp.Value);
                    yield return row;
                }
            }
        }

        private static object? JsonElementToValue(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
                JsonValueKind.String => decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (object?)d : (DateTime.TryParse(element.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (object?)element.GetString()),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };

        private void CleanupTempDir()
        {
            if (Directory.Exists(_tempDir))
                try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
