using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

internal sealed class ColumnarJoinSelectPlan
{
    private readonly SelectStatement _statement;
    private readonly JoinClause _join;
    private readonly ColumnarJoinKind _kind;
    private readonly string[] _leftKeys;
    private readonly string[] _rightKeys;
    private readonly Projection[] _projections;

    private ColumnarJoinSelectPlan(
        SelectStatement statement,
        JoinClause join,
        ColumnarJoinKind kind,
        string[] leftKeys,
        string[] rightKeys,
        Projection[] projections)
    {
        _statement = statement;
        _join = join;
        _kind = kind;
        _leftKeys = leftKeys;
        _rightKeys = rightKeys;
        _projections = projections;
    }

    public static bool TryCreate(SelectStatement statement, out ColumnarJoinSelectPlan? plan)
    {
        plan = null;
        if (statement.Joins is not { Count: 1 } || statement.FromTable.TableOperators.Count != 0
            || statement.Joins[0].Table.TableOperators.Count != 0 || statement.WhereClause != null
            || statement.GroupBy != null || statement.GroupingSet != null || statement.HavingClause != null
            || statement.OrderBy != null || statement.Offset != null || statement.LimitCount != null
            || statement.TopCount != null || statement.IsDistinct || statement.QualifyClause != null
            || statement.Sample != null || statement.IsTopPercent || statement.GroupByAll || statement.OrderByAll)
            return false;
        var join = statement.Joins[0];
        if (!TryMapKind(join.JoinType, out var kind)) return false;
        var leftAlias = statement.FromTable.Alias ?? statement.FromTable.TableName;
        var rightAlias = join.Table.Alias ?? join.Table.TableName;
        var leftKeys = new List<string>();
        var rightKeys = new List<string>();
        if (!TryExtractKeys(join.Condition, leftAlias, rightAlias, leftKeys, rightKeys)) return false;

        var projections = new List<Projection>(statement.Columns.Count);
        for (var index = 0; index < statement.Columns.Count; index++)
        {
            var column = statement.Columns[index];
            if (column.Expression is not IdentifierExpression identifier || !identifier.Name.Contains('.')) return false;
            var parts = identifier.Name.Split('.');
            if (parts.Length != 2) return false;
            var side = parts[0].Equals(leftAlias, StringComparison.OrdinalIgnoreCase)
                ? JoinSide.Left
                : parts[0].Equals(rightAlias, StringComparison.OrdinalIgnoreCase)
                    ? JoinSide.Right
                    : JoinSide.Unknown;
            if (side == JoinSide.Unknown || side == JoinSide.Right
                && kind is ColumnarJoinKind.LeftSemi or ColumnarJoinKind.LeftAnti)
                return false;
            projections.Add(new Projection(side, parts[1], column.Alias ?? parts[1], index));
        }
        if (projections.Count == 0
            || projections.Select(projection => projection.OutputName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != projections.Count)
            return false;
        plan = new ColumnarJoinSelectPlan(
            statement, join, kind, leftKeys.ToArray(), rightKeys.ToArray(), projections.ToArray());
        return true;
    }

    public async Task<Execution?> TryOpenAsync(IExecutionContext context)
    {
        var leftSource = await context.ResolveDataSourceAsync(_statement.FromTable);
        var rightSource = await context.ResolveDataSourceAsync(_join.Table);
        if (leftSource is not IReplayableColumnarDataSource left
            || rightSource is not IReplayableColumnarDataSource right
            || leftSource is not IEstimatedCardinalityDataSource leftEstimate
            || rightSource is not IEstimatedCardinalityDataSource rightEstimate)
            return null;

        var estimatedBytes = checked((leftEstimate.EstimatedRowCount + rightEstimate.EstimatedRowCount) * 64L);
        var operatorBudget = (long)context.EffectiveOperatorMemoryGrantMB * 1024 * 1024;
        if (operatorBudget > 0 && estimatedBytes > operatorBudget) return null;
        if (rightEstimate.EstimatedRowCount == 0 && _kind == ColumnarJoinKind.LeftOuter
            && _projections.Any(projection => projection.Side == JoinSide.Right))
            return null;

        var rightBatches = new List<ColumnBatch>();
        var lease = context.MemoryArbiter.AcquireLease();
        long retainedBytes = 0;
        try
        {
            await foreach (var batch in right.ReadColumnBatches(context.EffectiveBatchSize, context.CancellationToken))
            {
                var prospective = checked(retainedBytes + batch.Columns.Sum(column => column.AllocatedBytes));
                if (lease.RegisterAndCheckSpill(prospective))
                {
                    batch.Dispose();
                    foreach (var retained in rightBatches) retained.Dispose();
                    lease.Dispose();
                    return null;
                }
                retainedBytes = prospective;
                rightBatches.Add(batch);
            }
            return new Execution(this, context, left, rightBatches, lease);
        }
        catch
        {
            foreach (var batch in rightBatches) batch.Dispose();
            lease.Dispose();
            throw;
        }
    }

    private static bool TryExtractKeys(
        Expression expression,
        string leftAlias,
        string rightAlias,
        List<string> leftKeys,
        List<string> rightKeys)
    {
        if (expression is BinaryExpression { Operator: Core.Parser.TokenType.AND } and)
            return TryExtractKeys(and.Left, leftAlias, rightAlias, leftKeys, rightKeys)
                && TryExtractKeys(and.Right, leftAlias, rightAlias, leftKeys, rightKeys);
        if (expression is not BinaryExpression { Operator: Core.Parser.TokenType.EQUALS } equality
            || equality.Left is not IdentifierExpression leftIdentifier
            || equality.Right is not IdentifierExpression rightIdentifier)
            return false;
        if (TryOrient(leftIdentifier.Name, rightIdentifier.Name, leftAlias, rightAlias, out var left, out var right)
            || TryOrient(rightIdentifier.Name, leftIdentifier.Name, leftAlias, rightAlias, out left, out right))
        {
            leftKeys.Add(left);
            rightKeys.Add(right);
            return true;
        }
        return false;
    }

    private static bool TryOrient(
        string candidateLeft,
        string candidateRight,
        string leftAlias,
        string rightAlias,
        out string left,
        out string right)
    {
        left = right = string.Empty;
        if (!candidateLeft.StartsWith(leftAlias + ".", StringComparison.OrdinalIgnoreCase)
            || !candidateRight.StartsWith(rightAlias + ".", StringComparison.OrdinalIgnoreCase)) return false;
        left = candidateLeft[(candidateLeft.IndexOf('.') + 1)..];
        right = candidateRight[(candidateRight.IndexOf('.') + 1)..];
        return true;
    }

    private static bool TryMapKind(string joinType, out ColumnarJoinKind kind)
    {
        if (joinType.Contains("SEMI", StringComparison.OrdinalIgnoreCase)) kind = ColumnarJoinKind.LeftSemi;
        else if (joinType.Contains("ANTI", StringComparison.OrdinalIgnoreCase)) kind = ColumnarJoinKind.LeftAnti;
        else if (joinType.Contains("LEFT", StringComparison.OrdinalIgnoreCase)
            || joinType.Contains("OUTER", StringComparison.OrdinalIgnoreCase)) kind = ColumnarJoinKind.LeftOuter;
        else if (joinType.Contains("INNER", StringComparison.OrdinalIgnoreCase)) kind = ColumnarJoinKind.Inner;
        else { kind = default; return false; }
        return true;
    }

    internal sealed class Execution : IAsyncDisposable
    {
        private readonly ColumnarJoinSelectPlan _plan;
        private readonly IExecutionContext _context;
        private readonly IReplayableColumnarDataSource _left;
        private readonly List<ColumnBatch> _rightBatches;
        private IMemoryGrantLease? _lease;

        public Execution(
            ColumnarJoinSelectPlan plan,
            IExecutionContext context,
            IReplayableColumnarDataSource left,
            List<ColumnBatch> rightBatches,
            IMemoryGrantLease lease)
        {
            _plan = plan;
            _context = context;
            _left = left;
            _rightBatches = rightBatches;
            _lease = lease;
        }

        public async IAsyncEnumerable<DataTable> ExecuteAsync()
        {
            var yielded = false;
            await foreach (var leftBatch in _left.ReadColumnBatches(_context.EffectiveBatchSize, _context.CancellationToken))
                using (leftBatch)
                {
                    var matched = new bool[leftBatch.RowCount];
                    foreach (var rightBatch in _rightBatches)
                    {
                        using var pairs = ColumnBatchJoinKernels.JoinAuto(
                            leftBatch, _plan._leftKeys, rightBatch, _plan._rightKeys, ColumnarJoinKind.Inner,
                            _context.MemoryArbiter, cancellationToken: _context.CancellationToken);
                        foreach (var row in pairs.LeftRows.Span) matched[row] = true;
                        if (_plan._kind is ColumnarJoinKind.Inner or ColumnarJoinKind.LeftOuter && pairs.Count > 0)
                        {
                            yielded = true;
                            yield return Project(leftBatch, rightBatch, pairs);
                        }
                    }

                    if (_plan._kind == ColumnarJoinKind.Inner) continue;
                    var selected = Enumerable.Range(0, matched.Length)
                        .Where(row => _plan._kind switch
                        {
                            ColumnarJoinKind.LeftOuter => !matched[row],
                            ColumnarJoinKind.LeftSemi => matched[row],
                            ColumnarJoinKind.LeftAnti => !matched[row],
                            _ => false
                        }).ToArray();
                    if (selected.Length == 0) continue;
                    var rightRows = Enumerable.Repeat(-1, selected.Length).ToArray();
                    using var selectedPairs = ColumnBatchJoinKernels.CreateOrdinalPairs(
                        selected, rightRows, _context.MemoryArbiter);
                    yielded = true;
                    yield return Project(leftBatch, null, selectedPairs);
                }
            if (!yielded)
            {
                var empty = new DataTable();
                empty.SetColumns(_plan._projections.Select(projection => projection.OutputName));
                yield return empty;
            }
        }

        private DataTable Project(ColumnBatch left, ColumnBatch? right, NativeJoinPairs pairs)
        {
            var leftProjections = _plan._projections.Where(item => item.Side == JoinSide.Left).ToArray();
            var rightProjections = _plan._projections.Where(item => item.Side == JoinSide.Right).ToArray();
            if (right == null && rightProjections.Length > 0)
            {
                // Use any retained right schema for unmatched outer rows. If the right input is empty,
                // planner execution cannot infer physical payload types and must have been rejected.
                right = _rightBatches.FirstOrDefault()
                    ?? throw new InvalidOperationException("Native outer join cannot project an empty right schema.");
            }
            var temporaryNames = leftProjections.Select(item => $"__p{item.Position}")
                .Concat(rightProjections.Select(item => $"__p{item.Position}")).ToArray();
            using var projected = ColumnBatchJoinKernels.ProjectPayloads(
                left, right ?? left, pairs,
                leftProjections.Select(item => item.SourceColumn).ToArray(),
                rightProjections.Select(item => item.SourceColumn).ToArray(), temporaryNames,
                _context.CancellationToken);
            var orderedTemporary = _plan._projections.OrderBy(item => item.Position)
                .Select(item => $"__p{item.Position}").ToArray();
            var orderedNames = _plan._projections.OrderBy(item => item.Position)
                .Select(item => item.OutputName).ToArray();
            using var ordered = ColumnBatchAdapter.Compact(
                projected, orderedTemporary, outputColumns: orderedNames,
                cancellationToken: _context.CancellationToken);
            return ColumnBatchAdapter.ToDataTable(ordered);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var batch in _rightBatches) batch.Dispose();
            _rightBatches.Clear();
            _lease?.Dispose();
            _lease = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record Projection(JoinSide Side, string SourceColumn, string OutputName, int Position);
    private enum JoinSide { Unknown, Left, Right }
}
