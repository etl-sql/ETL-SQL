using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

internal sealed class ColumnarSortSelectPlan
{
    private readonly SelectStatement _statement;
    private readonly string[] _sourceColumns;
    private readonly string[] _outputColumns;
    private readonly (string Column, bool Descending)[] _orderKeys;

    private ColumnarSortSelectPlan(
        SelectStatement statement,
        string[] sourceColumns,
        string[] outputColumns,
        (string Column, bool Descending)[] orderKeys)
    {
        _statement = statement;
        _sourceColumns = sourceColumns;
        _outputColumns = outputColumns;
        _orderKeys = orderKeys;
    }

    public static bool TryCreate(SelectStatement statement, out ColumnarSortSelectPlan? plan)
    {
        plan = null;
        if (statement.FromTable.TableOperators.Count != 0 || statement.Joins.Count != 0
            || statement.OrderBy is not { Count: > 0 } || statement.OrderByAll
            || statement.GroupBy != null || statement.GroupingSet != null || statement.HavingClause != null
            || statement.Offset != null || statement.LimitCount != null || statement.TopCount != null
            || statement.IsDistinct || statement.QualifyClause != null || statement.Sample != null
            || statement.IsTopPercent || statement.GroupByAll)
            return false;
        if (statement.Columns.Count == 0 || statement.Columns.Any(column => column.Expression is not IdentifierExpression))
            return false;
        if (statement.OrderBy.Any(order => order.Expression is not IdentifierExpression)) return false;
        var sourceColumns = statement.Columns.Cast<SelectColumn>()
            .Select(column => ((IdentifierExpression)column.Expression).Name.Split('.').Last()).ToArray();
        var outputColumns = statement.Columns.Select((column, index) => column.Alias ?? sourceColumns[index]).ToArray();
        if (outputColumns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputColumns.Length) return false;
        var orderKeys = statement.OrderBy.Select(order =>
            (((IdentifierExpression)order.Expression).Name.Split('.').Last(), order.Descending)).ToArray();
        plan = new ColumnarSortSelectPlan(statement, sourceColumns, outputColumns, orderKeys);
        return true;
    }

    public async Task<Execution?> TryOpenAsync(IExecutionContext context)
    {
        var dataSource = await context.ResolveDataSourceAsync(_statement.FromTable);
        if (dataSource is not IReplayableColumnarDataSource source
            || dataSource is not IEstimatedCardinalityDataSource estimate)
            return null;
        var estimatedBytes = checked(estimate.EstimatedRowCount * 64L);
        var operatorBudget = (long)context.OperatorMemoryGrantMB * 1024 * 1024;
        if (operatorBudget > 0 && estimatedBytes > operatorBudget) return null;

        var keys = _orderKeys.Select(key => new NativeSortKey(
            key.Column, key.Descending, NullsFirst: true,
            context.CaseSensitiveComparison ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)).ToArray();
        var batches = new List<ColumnBatch>();
        var runs = new List<NativeSortRun>();
        var lease = context.MemoryArbiter.AcquireLease();
        long retainedBytes = 0;
        try
        {
            await foreach (var batch in source.ReadColumnBatches(context.BatchSize, context.CancellationToken))
            {
                if (!HasColumns(batch, _sourceColumns) || !HasColumns(batch, keys.Select(key => key.ColumnName)))
                {
                    batch.Dispose();
                    Cleanup();
                    return null;
                }
                SelectionVector? selection = null;
                if (_statement.WhereClause != null && !ColumnarPredicateCompiler.TrySelect(
                    batch, _statement.WhereClause, out selection,
                    cancellationToken: context.CancellationToken,
                    caseSensitiveComparison: context.CaseSensitiveComparison))
                {
                    batch.Dispose();
                    Cleanup();
                    return null;
                }
                try
                {
                    var prospective = checked(retainedBytes + batch.Columns.Sum(column => column.AllocatedBytes));
                    if (lease.RegisterAndCheckSpill(prospective))
                    {
                        batch.Dispose();
                        Cleanup();
                        return null;
                    }
                    retainedBytes = prospective;
                    runs.Add(ColumnBatchSortKernels.CreateRun(
                        batch, keys, context.MemoryArbiter, selection, context.CancellationToken));
                    batches.Add(batch);
                }
                finally { selection?.Dispose(); }
            }
            return new Execution(context, batches, runs, keys, _sourceColumns, _outputColumns, lease);
        }
        catch (ExecutionException)
        {
            Cleanup();
            return null;
        }
        catch
        {
            Cleanup();
            throw;
        }

        void Cleanup()
        {
            foreach (var run in runs) run.Dispose();
            foreach (var batch in batches) batch.Dispose();
            runs.Clear();
            batches.Clear();
            lease.Dispose();
        }

        static bool HasColumns(ColumnBatch batch, IEnumerable<string> columns)
        {
            try { foreach (var column in columns) batch.Schema.GetOrdinal(column); return true; }
            catch (KeyNotFoundException) { return false; }
        }
    }

    internal sealed class Execution : IAsyncDisposable
    {
        private readonly IExecutionContext _context;
        private readonly List<ColumnBatch> _batches;
        private readonly List<NativeSortRun> _runs;
        private readonly NativeSortKey[] _keys;
        private readonly string[] _sourceColumns;
        private readonly string[] _outputColumns;
        private IMemoryGrantLease? _lease;

        public Execution(
            IExecutionContext context,
            List<ColumnBatch> batches,
            List<NativeSortRun> runs,
            NativeSortKey[] keys,
            string[] sourceColumns,
            string[] outputColumns,
            IMemoryGrantLease lease)
        {
            _context = context;
            _batches = batches;
            _runs = runs;
            _keys = keys;
            _sourceColumns = sourceColumns;
            _outputColumns = outputColumns;
            _lease = lease;
        }

        public async IAsyncEnumerable<DataTable> ExecuteAsync()
        {
            var queue = new PriorityQueue<Cursor, Cursor>(new CursorComparer(_batches, _runs, _keys));
            for (var batch = 0; batch < _runs.Count; batch++)
                if (_runs[batch].Count > 0) queue.Enqueue(new Cursor(batch, 0), new Cursor(batch, 0));
            if (queue.Count == 0)
            {
                var empty = new DataTable();
                empty.SetColumns(_outputColumns);
                yield return empty;
                yield break;
            }

            var output = NewBatch();
            while (queue.TryDequeue(out var cursor, out _))
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                var source = _batches[cursor.Batch];
                var sourceRow = _runs[cursor.Batch].Ordinals.Span[cursor.Position];
                var row = output.NewRow();
                for (var column = 0; column < _sourceColumns.Length; column++)
                {
                    var ordinal = source.Schema.GetOrdinal(_sourceColumns[column]);
                    var field = source.Schema.Fields[ordinal];
                    row[column] = ColumnBatchAdapter.RestoreEngineValue(
                        source.Columns[ordinal].GetBoxedValue(sourceRow), field.LogicalType);
                }
                output.Rows.Add(row);
                var next = cursor with { Position = cursor.Position + 1 };
                if (next.Position < _runs[next.Batch].Count) queue.Enqueue(next, next);
                if (output.Rows.Count >= _context.BatchSize)
                {
                    yield return output;
                    output = NewBatch();
                    await Task.Yield();
                }
            }
            if (output.Rows.Count > 0) yield return output;

            DataTable NewBatch()
            {
                var table = new DataTable();
                table.SetColumns(_outputColumns);
                return table;
            }
        }

        public ValueTask DisposeAsync()
        {
            foreach (var run in _runs) run.Dispose();
            foreach (var batch in _batches) batch.Dispose();
            _runs.Clear();
            _batches.Clear();
            _lease?.Dispose();
            _lease = null;
            return ValueTask.CompletedTask;
        }

        private readonly record struct Cursor(int Batch, int Position);

        private sealed class CursorComparer(
            IReadOnlyList<ColumnBatch> batches,
            IReadOnlyList<NativeSortRun> runs,
            IReadOnlyList<NativeSortKey> keys) : IComparer<Cursor>
        {
            public int Compare(Cursor left, Cursor right)
            {
                var leftRow = runs[left.Batch].Ordinals.Span[left.Position];
                var rightRow = runs[right.Batch].Ordinals.Span[right.Position];
                var order = ColumnBatchSortKernels.CompareRows(
                    batches[left.Batch], leftRow, batches[right.Batch], rightRow, keys);
                if (order != 0) return order;
                order = left.Batch.CompareTo(right.Batch);
                return order != 0 ? order : leftRow.CompareTo(rightRow);
            }
        }
    }
}
