using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Handles window functions (OVER clause) including ranking, offsets, and windowed aggregates.
    /// </summary>
    public class WindowEngine
    {
        private readonly IExecutionContext _context;
        private readonly AggregateEngine _aggregateEngine;
        private readonly ILogger _logger;

        public WindowEngine(IExecutionContext context, AggregateEngine aggregateEngine, ILogger logger)
        {
            _context = context;
            _aggregateEngine = aggregateEngine;
            _logger = logger;
        }

        /// <summary>Calculates and appends window function results to the result set based on partitioning and ordering.</summary>
        public async Task<List<Row>> ApplyWindowFunctions(List<Row> allBufferedRows, SelectStatement stmt)
        {
            var windowCols = stmt.Columns.Where(c => c.Expression is FunctionCallExpression f && f.Window != null).ToList();
            if (windowCols.Count == 0) return allBufferedRows;

            foreach (var col in windowCols)
            {
                var f = (FunctionCallExpression)col.Expression;
                var name = f.FunctionName.ToUpperInvariant();
                var window = f.Window!;

                var partitionKeysOrder = new List<string>();
                var partitions = new Dictionary<string, List<Row>>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in allBufferedRows)
                {
                    var key = "";
                    if (window.PartitionBy != null && window.PartitionBy.Count > 0)
                    {
                        foreach (var p in window.PartitionBy)
                        {
                            var val = await _context.EvaluateValue(p, row);
                            key += (val?.ToString() ?? "NULL") + "|";
                        }
                    }
                    else key = "GLOBAL";

                    if (!partitions.ContainsKey(key)) { partitions[key] = new List<Row>(); partitionKeysOrder.Add(key); }
                    partitions[key].Add(row);
                }

                foreach (var pKey in partitionKeysOrder)
                {
                    var partitionRows = partitions[pKey];

                    // Pre-evaluate sort keys to avoid .Result in Sort comparator (deadlock risk).
                    List<object?[]>? sortKeys = null;
                    if (window.OrderBy != null && window.OrderBy.Count > 0)
                    {
                        var withKeys = new List<(Row row, object?[] keys)>(partitionRows.Count);
                        foreach (var row in partitionRows)
                        {
                            var keys = new object?[window.OrderBy.Count];
                            for (int k = 0; k < window.OrderBy.Count; k++)
                                keys[k] = await _context.EvaluateValue(window.OrderBy[k].Expression, row);
                            withKeys.Add((row, keys));
                        }

                        withKeys.Sort((a, b) =>
                        {
                            for (int k = 0; k < window.OrderBy.Count; k++)
                            {
                                int res = _context.CompareConstants(a.keys[k], b.keys[k]);
                                if (res != 0) return window.OrderBy[k].Descending ? -res : res;
                            }
                            return 0;
                        });

                        partitionRows = withKeys.Select(x => x.row).ToList();
                        sortKeys = withKeys.Select(x => x.keys).ToList();
                        partitions[pKey] = partitionRows;
                    }

                    int currentRank = 1;
                    int currentDenseRank = 1;

                    for (int i = 0; i < partitionRows.Count; i++)
                    {
                        if (i > 0 && sortKeys != null)
                        {
                            bool sameAsPrev = true;
                            for (int k = 0; k < sortKeys[i].Length; k++)
                            {
                                if (_context.CompareConstants(sortKeys[i][k], sortKeys[i - 1][k]) != 0)
                                {
                                    sameAsPrev = false;
                                    break;
                                }
                            }

                            if (!sameAsPrev)
                            {
                                currentDenseRank++;
                                currentRank = i + 1;
                            }
                        }

                        object? winVal = null;
                        switch (name)
                        {
                            case "ROW_NUMBER": winVal = (decimal)(i + 1); break;
                            case "RANK":
                                if (window.OrderBy == null || window.OrderBy.Count == 0)
                                    throw new ExecutionException("RANK() requires an ORDER BY clause in the OVER window.");
                                winVal = (decimal)currentRank;
                                break;
                            case "DENSE_RANK":
                                if (window.OrderBy == null || window.OrderBy.Count == 0)
                                    throw new ExecutionException("DENSE_RANK() requires an ORDER BY clause in the OVER window.");
                                winVal = (decimal)currentDenseRank;
                                break;
                            case "PERCENT_RANK":
                                if (window.OrderBy == null || window.OrderBy.Count == 0)
                                    throw new ExecutionException("PERCENT_RANK() requires an ORDER BY clause in the OVER window.");
                                winVal = partitionRows.Count <= 1 ? 0m
                                    : (decimal)(currentRank - 1) / (partitionRows.Count - 1);
                                break;
                            case "CUME_DIST":
                                if (window.OrderBy == null || window.OrderBy.Count == 0)
                                    throw new ExecutionException("CUME_DIST() requires an ORDER BY clause in the OVER window.");
                                {
                                    // Find the last row in the current peer group (same sort values)
                                    int peerEnd = i;
                                    while (peerEnd + 1 < partitionRows.Count && sortKeys != null)
                                    {
                                        bool samePeer = true;
                                        for (int sk = 0; sk < sortKeys[peerEnd].Length; sk++)
                                        {
                                            if (_context.CompareConstants(sortKeys[peerEnd][sk], sortKeys[peerEnd + 1][sk]) != 0)
                                            { samePeer = false; break; }
                                        }
                                        if (!samePeer) break;
                                        peerEnd++;
                                    }
                                    winVal = (decimal)(peerEnd + 1) / partitionRows.Count;
                                }
                                break;
                            case "NTH_VALUE":
                                if (f.Arguments.Count < 2) { winVal = null; break; }
                                {
                                    int nth = Convert.ToInt32(await _context.EvaluateValue(f.Arguments[1], partitionRows[i]));
                                    var nthFrame = partitionRows;
                                    if (window.Frame != null)
                                    {
                                        var nthRange = await ResolveFrameRange(i, partitionRows, window);
                                        nthFrame = partitionRows.GetRange(nthRange.Start, nthRange.End - nthRange.Start + 1);
                                    }
                                    winVal = (nth >= 1 && nth <= nthFrame.Count)
                                        ? await _context.EvaluateValue(f.Arguments[0], nthFrame[nth - 1])
                                        : null;
                                }
                                break;
                            case "LAG":
                                int lag = f.Arguments.Count >= 2 ? Convert.ToInt32(await _context.EvaluateValue(f.Arguments[1], partitionRows[i])) : 1;
                                winVal = (i - lag >= 0) ? await _context.EvaluateValue(f.Arguments[0], partitionRows[i - lag]) : null;
                                break;
                            case "LEAD":
                                int lead = f.Arguments.Count >= 2 ? Convert.ToInt32(await _context.EvaluateValue(f.Arguments[1], partitionRows[i])) : 1;
                                winVal = (i + lead < partitionRows.Count) ? await _context.EvaluateValue(f.Arguments[0], partitionRows[i + lead]) : null;
                                break;
                            case "FIRST_VALUE":
                                winVal = partitionRows.Count > 0 ? await _context.EvaluateValue(f.Arguments[0], partitionRows[0]) : null;
                                break;
                            case "LAST_VALUE":
                                winVal = partitionRows.Count > 0 ? await _context.EvaluateValue(f.Arguments[0], partitionRows[partitionRows.Count - 1]) : null;
                                break;
                            case "NTILE":
                                if (f.Arguments.Count == 0) { winVal = null; break; }
                                int nBuckets = Convert.ToInt32(await _context.EvaluateValue(f.Arguments[0], partitionRows[i]));
                                if (nBuckets <= 0) { winVal = null; break; }

                                long totalRowCount = partitionRows.Count;
                                long baseSize = totalRowCount / nBuckets;
                                long extraRows = totalRowCount % nBuckets;

                                if (baseSize == 0)
                                    winVal = (decimal)(i + 1);
                                else if (i < extraRows * (baseSize + 1))
                                    winVal = (decimal)(i / (baseSize + 1) + 1);
                                else
                                    winVal = (decimal)((i - extraRows * (baseSize + 1)) / baseSize + extraRows + 1);
                                break;
                            default:
                                // Window Aggregate
                                var frameRows = partitionRows;
                                if (window.Frame != null)
                                {
                                    var range = await ResolveFrameRange(i, partitionRows, window);
                                    frameRows = partitionRows.GetRange(range.Start, range.End - range.Start + 1);
                                }
                                winVal = await _aggregateEngine.EvaluateAggregate(f, frameRows);
                                break;
                        }
                        partitionRows[i][$"WINDOW_{f.ToSql().ToUpperInvariant()}"] = winVal;
                    }
                }
                allBufferedRows = partitionKeysOrder.SelectMany(k => partitions[k]).ToList();
            }
            return allBufferedRows;
        }

        public bool IsWindowFunction(Expression expr)
        {
            return expr is FunctionCallExpression f && f.Window != null;
        }

        private async Task<(int Start, int End)> ResolveFrameRange(int currentIndex, List<Row> partitionRows, WindowClause window)
        {
            var frame = window.Frame!;
            int start = 0;
            int end = partitionRows.Count - 1;

            if (frame.Type == WindowFrameType.ROWS)
            {
                start = await ResolveRowsBound(frame.StartBound, frame.StartValue, currentIndex, partitionRows.Count);
                end = frame.EndBound.HasValue
                    ? await ResolveRowsBound(frame.EndBound.Value, frame.EndValue, currentIndex, partitionRows.Count)
                    : currentIndex;
            }
            else // RANGE
            {
                if (frame.StartBound == WindowFrameBoundType.UNBOUNDED_PRECEDING &&
                    (!frame.EndBound.HasValue || frame.EndBound == WindowFrameBoundType.CURRENT_ROW))
                {
                    start = 0;
                    end = currentIndex;
                    while (end + 1 < partitionRows.Count && await ArePeers(partitionRows[currentIndex], partitionRows[end + 1], window.OrderBy))
                        end++;
                }
                else
                {
                    start = 0;
                    end = partitionRows.Count - 1;
                }
            }

            return (Math.Max(0, start), Math.Min(partitionRows.Count - 1, end));
        }

        private async Task<int> ResolveRowsBound(WindowFrameBoundType bound, Expression? value, int currentIndex, int rowCount)
        {
            switch (bound)
            {
                case WindowFrameBoundType.UNBOUNDED_PRECEDING: return 0;
                case WindowFrameBoundType.UNBOUNDED_FOLLOWING: return rowCount - 1;
                case WindowFrameBoundType.CURRENT_ROW: return currentIndex;
                case WindowFrameBoundType.PRECEDING:
                    int offsetP = Convert.ToInt32(await _context.EvaluateValue(value, new Row()));
                    return currentIndex - offsetP;
                case WindowFrameBoundType.FOLLOWING:
                    int offsetF = Convert.ToInt32(await _context.EvaluateValue(value, new Row()));
                    return currentIndex + offsetF;
                default: return currentIndex;
            }
        }

        private async Task<bool> ArePeers(Row a, Row b, List<OrderByClause> orderBy)
        {
            if (orderBy == null || orderBy.Count == 0) return true;
            foreach (var clause in orderBy)
            {
                var valA = await _context.EvaluateValue(clause.Expression, a);
                var valB = await _context.EvaluateValue(clause.Expression, b);
                if (_context.CompareConstants(valA, valB) != 0) return false;
            }
            return true;
        }
    }
}
