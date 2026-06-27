using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;
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
        var windowFunctionCalls = stmt.Columns
            .Where(c => ContainsWindowFunction(c.Expression))
            .SelectMany(c => CollectWindowCalls(c.Expression))
            .Concat(CollectWindowCalls(stmt.QualifyClause))
            .GroupBy(f => f.ToSql().ToUpperInvariant())
            .Select(g => g.First())
            .ToList();
        if (windowFunctionCalls.Count == 0) return allBufferedRows;

        foreach (var f in windowFunctionCalls)
        {
            var name = f.FunctionName.ToUpperInvariant();
            var window = f.Window!;

            var partitionKeysOrder = new List<CompoundKey>();
            var partitions = new Dictionary<CompoundKey, List<Row>>();
            foreach (var row in allBufferedRows)
            {
                CompoundKey key;
                if (window.PartitionBy != null && window.PartitionBy.Count > 0)
                {
                    var partitionValues = new object?[window.PartitionBy.Count];
                    for (int i = 0; i < window.PartitionBy.Count; i++)
                    {
                        partitionValues[i] = await _context.EvaluateValue(window.PartitionBy[i], row);
                    }
                    key = new CompoundKey(partitionValues);
                }
                else key = new CompoundKey("GLOBAL");

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
                                    nthFrame = await ResolveFrameRows(i, partitionRows, window);
                                }
                                winVal = (nth >= 1 && nth <= nthFrame.Count)
                                    ? await _context.EvaluateValue(f.Arguments[0], nthFrame[nth - 1])
                                    : null;
                            }
                            break;
                        case "LAG":
                            {
                                int lag = f.Arguments.Count >= 2 ? Convert.ToInt32(await _context.EvaluateValue(f.Arguments[1], partitionRows[i])) : 1;
                                if (i - lag >= 0)
                                    winVal = await _context.EvaluateValue(f.Arguments[0], partitionRows[i - lag]);
                                else
                                    winVal = f.Arguments.Count >= 3 ? await _context.EvaluateValue(f.Arguments[2], partitionRows[i]) : null;
                                break;
                            }
                        case "LEAD":
                            {
                                int lead = f.Arguments.Count >= 2 ? Convert.ToInt32(await _context.EvaluateValue(f.Arguments[1], partitionRows[i])) : 1;
                                if (i + lead < partitionRows.Count)
                                    winVal = await _context.EvaluateValue(f.Arguments[0], partitionRows[i + lead]);
                                else
                                    winVal = f.Arguments.Count >= 3 ? await _context.EvaluateValue(f.Arguments[2], partitionRows[i]) : null;
                                break;
                            }
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
                                frameRows = await ResolveFrameRows(i, partitionRows, window);
                            }

                            if (f.Filter != null)
                            {
                                var filtered = new List<Row>();
                                foreach (var r in frameRows)
                                {
                                    if (await _context.EvaluateCondition(f.Filter, r)) filtered.Add(r);
                                }
                                frameRows = filtered;
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

    /// <summary>
    /// Returns true if the expression itself is a window function call OR contains one anywhere
    /// in its sub-expression tree (e.g. Revenue - LAG(...) OVER (...) is a BinaryExpression
    /// that *contains* a window function and must still trigger window pre-computation).
    /// </summary>
    public bool IsWindowFunction(Expression expr) => ContainsWindowFunction(expr);

    public static bool ContainsWindowFunction(Expression? expr)
    {
        if (expr == null) return false;
        if (expr is FunctionCallExpression fc)
        {
            if (fc.Window != null) return true;
            return fc.Arguments.Any(ContainsWindowFunction) || ContainsWindowFunction(fc.Filter);
        }
        if (expr is BinaryExpression b) return ContainsWindowFunction(b.Left) || ContainsWindowFunction(b.Right);
        if (expr is UnaryExpression u) return ContainsWindowFunction(u.Expression);
        if (expr is CaseExpression c)
            return c.WhenClauses.Any(w => ContainsWindowFunction(w.Condition) || ContainsWindowFunction(w.Result))
                || ContainsWindowFunction(c.ElseResult);
        if (expr is IsNullExpression isn) return ContainsWindowFunction(isn.Expression);
        if (expr is IsDistinctFromExpression idf) return ContainsWindowFunction(idf.Left) || ContainsWindowFunction(idf.Right);
        if (expr is InExpression inx) return ContainsWindowFunction(inx.Left) || ContainsWindowFunction(inx.Right);
        if (expr is LikeExpression lk) return ContainsWindowFunction(lk.Left) || ContainsWindowFunction(lk.Pattern);
        return false;
    }

    public static List<FunctionCallExpression> CollectWindowCalls(Expression? expr)
    {
        var result = new List<FunctionCallExpression>();
        CollectWindowCallsInner(expr, result);
        return result;
    }

    private static void CollectWindowCallsInner(Expression? expr, List<FunctionCallExpression> result)
    {
        if (expr == null) return;
        if (expr is FunctionCallExpression fc)
        {
            if (fc.Window != null) { result.Add(fc); return; }
            foreach (var arg in fc.Arguments) CollectWindowCallsInner(arg, result);
            CollectWindowCallsInner(fc.Filter, result);
            return;
        }
        if (expr is BinaryExpression b) { CollectWindowCallsInner(b.Left, result); CollectWindowCallsInner(b.Right, result); return; }
        if (expr is UnaryExpression u) { CollectWindowCallsInner(u.Expression, result); return; }
        if (expr is CaseExpression c)
        {
            foreach (var w in c.WhenClauses) { CollectWindowCallsInner(w.Condition, result); CollectWindowCallsInner(w.Result, result); }
            CollectWindowCallsInner(c.ElseResult, result);
            return;
        }
        if (expr is IsNullExpression isn) { CollectWindowCallsInner(isn.Expression, result); return; }
        if (expr is IsDistinctFromExpression idf) { CollectWindowCallsInner(idf.Left, result); CollectWindowCallsInner(idf.Right, result); return; }
        if (expr is InExpression inx) { CollectWindowCallsInner(inx.Left, result); CollectWindowCallsInner(inx.Right, result); return; }
        if (expr is LikeExpression lk) { CollectWindowCallsInner(lk.Left, result); CollectWindowCallsInner(lk.Pattern, result); return; }
    }

    private async Task<List<Row>> ResolveFrameRows(int currentIndex, List<Row> partitionRows, WindowClause window)
    {
        var range = await ResolveFrameRange(currentIndex, partitionRows, window);
        if (range.End < range.Start) return new List<Row>();

        var frameRows = partitionRows.GetRange(range.Start, range.End - range.Start + 1);
        var exclusion = window.Frame?.Exclusion ?? WindowFrameExclusion.NoOthers;
        if (exclusion == WindowFrameExclusion.NoOthers) return frameRows;

        var currentRow = partitionRows[currentIndex];
        var filtered = new List<Row>(frameRows.Count);
        foreach (var row in frameRows)
        {
            bool isCurrent = ReferenceEquals(row, currentRow);
            bool isPeer = await ArePeers(currentRow, row, window.OrderBy);
            if (exclusion == WindowFrameExclusion.CurrentRow && isCurrent) continue;
            if (exclusion == WindowFrameExclusion.Group && isPeer) continue;
            if (exclusion == WindowFrameExclusion.Ties && isPeer && !isCurrent) continue;
            filtered.Add(row);
        }
        return filtered;
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
        else if (frame.Type == WindowFrameType.GROUPS)
        {
            var groups = await BuildPeerGroups(partitionRows, window.OrderBy);
            int currentGroup = groups.FindIndex(g => currentIndex >= g.Start && currentIndex <= g.End);
            start = await ResolveGroupBound(frame.StartBound, frame.StartValue, currentGroup, groups.Count, startBound: true);
            end = frame.EndBound.HasValue
                ? await ResolveGroupBound(frame.EndBound.Value, frame.EndValue, currentGroup, groups.Count, startBound: false)
                : currentGroup;
            start = groups[Math.Max(0, Math.Min(groups.Count - 1, start))].Start;
            end = groups[Math.Max(0, Math.Min(groups.Count - 1, end))].End;
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

    private async Task<int> ResolveGroupBound(WindowFrameBoundType bound, Expression? value, int currentGroup, int groupCount, bool startBound)
    {
        switch (bound)
        {
            case WindowFrameBoundType.UNBOUNDED_PRECEDING: return 0;
            case WindowFrameBoundType.UNBOUNDED_FOLLOWING: return groupCount - 1;
            case WindowFrameBoundType.CURRENT_ROW: return currentGroup;
            case WindowFrameBoundType.PRECEDING:
                int offsetP = Convert.ToInt32(await _context.EvaluateValue(value, new Row()));
                return currentGroup - offsetP;
            case WindowFrameBoundType.FOLLOWING:
                int offsetF = Convert.ToInt32(await _context.EvaluateValue(value, new Row()));
                return currentGroup + offsetF;
            default: return startBound ? 0 : currentGroup;
        }
    }

    private async Task<List<(int Start, int End)>> BuildPeerGroups(List<Row> partitionRows, List<OrderByClause> orderBy)
    {
        var groups = new List<(int Start, int End)>();
        if (partitionRows.Count == 0) return groups;

        int start = 0;
        for (int i = 1; i < partitionRows.Count; i++)
        {
            if (!await ArePeers(partitionRows[start], partitionRows[i], orderBy))
            {
                groups.Add((start, i - 1));
                start = i;
            }
        }
        groups.Add((start, partitionRows.Count - 1));
        return groups;
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
