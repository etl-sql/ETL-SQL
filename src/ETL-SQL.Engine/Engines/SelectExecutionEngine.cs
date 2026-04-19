using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Encapsulates the multi-pass execution pipeline for complex SELECT statements
    /// involving JOINS, AGGREGATES, WINDOW FUNCTIONS, and SORTING.
    /// </summary>
    public class SelectExecutionEngine
    {
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;
        private readonly JoinEngine _joinEngine;
        private readonly AggregateEngine _aggregateEngine;
        private readonly WindowEngine _windowEngine;
        private readonly ExternalWindowEngine _externalWindowEngine;

        public SelectExecutionEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
            _aggregateEngine = new AggregateEngine(context, logger);
            _joinEngine = new JoinEngine(context, logger);
            _windowEngine = new WindowEngine(context, _aggregateEngine, logger);
            _externalWindowEngine = new ExternalWindowEngine(context, _windowEngine, logger);
        }

        public async IAsyncEnumerable<DataTable> ExecuteHeavyPipeline(
            SelectStatement stmt, 
            IAsyncEnumerable<DataTable> sourceBatches,
            List<SelectColumn> finalColumns,
            List<string> colNames)
        {
            string fromName = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
            bool hasAggInColumns = stmt.Columns.Any(c => _aggregateEngine.IsAggregate(c.Expression));
            bool hasWindowInColumns = stmt.Columns.Any(c => _windowEngine.IsWindowFunction(c.Expression));

            _logger.Debug("[PIPELINE] Initializing Multi-Pass Engine Pipeline for {TableName}", fromName);

            var inputStream = sourceBatches.SelectMany(b => b.Rows.Select(r => {
                var cloned = r.Clone();
                foreach (var kv in r.Columns.ToList())
                {
                    // Only qualify if not already qualified
                    if (!kv.Key.Contains("."))
                        cloned[$"{fromName}.{kv.Key}"] = kv.Value;
                }
                return cloned;
            }).ToAsyncEnumerable());

            List<Row> allRows;
            bool whereApplied = false;

            // Optimization for streaming aggregates
            bool streamAggregate = (stmt.Joins == null || stmt.Joins.Count == 0)
                && !hasWindowInColumns
                && (stmt.GroupBy != null || stmt.GroupingSet != null || hasAggInColumns);

            if (stmt.Joins != null && stmt.Joins.Count > 0)
            {
                allRows = new List<Row>();
                var inputEnumerator = inputStream.GetAsyncEnumerator();
                try
                {
                    int count = 0;
                    while (await inputEnumerator.MoveNextAsync())
                    {
                        allRows.Add(inputEnumerator.Current);
                        count++;
                        if (count > 100000) break;
                    }

                    if (count > 100000)
                    {
                        _logger.WriteLine("[yellow]HYPER-SCALE: Switching to streaming external join.[/]");
                        var externalJoin = new ExternalJoinEngine(_context, _logger);
                        var hashKeysLeft = new List<string>();
                        var hashKeysRight = new List<string>();
                        var join = stmt.Joins[0];
                        _joinEngine.TryExtractEqualityKeys(join.Condition, fromName, join.Table.Alias ?? join.Table.TableName, hashKeysLeft, hashKeysRight);
                        
                        allRows = await externalJoin.ApplyHashJoinExternal(
                            PrependRows(allRows, ContinueStream(inputEnumerator)), 
                            _joinEngine.GetJoinRowsAsyncEnumerable(join), 
                            join, hashKeysLeft, hashKeysRight);
                    }
                    else
                    {
                        allRows = await _joinEngine.ApplyJoins(allRows, stmt.Joins, stmt);
                    }
                }
                finally { await inputEnumerator.DisposeAsync(); }
            }
            else if (streamAggregate)
            {
                IAsyncEnumerable<Row> aggInput = inputStream;
                if (stmt.WhereClause != null)
                {
                    aggInput = WhereStream(inputStream, stmt.WhereClause, _context);
                    whereApplied = true;
                }
                var externalAgg = new ExternalAggregateEngine(_context, _logger);
                allRows = await externalAgg.ApplyAggregationExternal(aggInput, stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet).ToListAsync();
            }
            else
            {
                allRows = new List<Row>();
                await foreach (var r in inputStream) allRows.Add(r);
            }

            // 1. WHERE
            if (!whereApplied && stmt.WhereClause != null)
            {
                var filtered = new List<Row>();
                foreach (var r in allRows) if (await _context.EvaluateCondition(stmt.WhereClause, r)) filtered.Add(r);
                allRows = filtered;
            }

            // 2. GROUP BY
            if (!streamAggregate && (stmt.GroupBy != null || stmt.GroupingSet != null || hasAggInColumns))
            {
                if (allRows.Count > 100000)
                {
                    var externalAgg = new ExternalAggregateEngine(_context, _logger);
                    allRows = await externalAgg.ApplyAggregationExternal(allRows.ToAsyncEnumerable(), stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet).ToListAsync();
                }
                else
                {
                    allRows = await _aggregateEngine.ApplyAggregation(allRows, stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
                }
            }

            // 3. WINDOW FUNCTIONS
            if (hasWindowInColumns)
            {
                if (allRows.Count >= _context.WindowSpillThreshold)
                {
                    _logger.WriteLine($"[yellow]HYPER-SCALE: Switching to ExternalWindowEngine (Row count {allRows.Count} >= threshold {_context.WindowSpillThreshold}).[/]");
                    var stream = ConvertToAsyncEnumerable(allRows);
                    var windowStream = _externalWindowEngine.ApplyWindowFunctionsExternal(stream, stmt);
                    
                    // Note: For now we still materialize here to maintain compatibility with the sort/limit logic.
                    // True end-to-end streaming will be a future refinement.
                    allRows = await windowStream.ToListAsync();
                }
                else
                {
                    allRows = await _windowEngine.ApplyWindowFunctions(allRows, stmt);
                }
            }

            // 4. ORDER BY
            if (stmt.OrderBy != null && stmt.OrderBy.Count > 0)
            {
                if (allRows.Count > 100000)
                {
                    var externalSort = new ExternalSortEngine(_context, _logger);
                    allRows = await externalSort.SortExternal(allRows, stmt.OrderBy);
                }
                else
                {
                    allRows = await SortInMemory(allRows, stmt.OrderBy, colNames, finalColumns);
                }
            }

            // 5. OFFSET / LIMIT
            allRows = await ApplyLimits(allRows, stmt);

            // Final Projection & Batching
            var seenRows = stmt.IsDistinct ? new HashSet<string>() : null;
            var batch = new DataTable();
            batch.SetColumns(colNames);
            bool yielded = false;
            foreach (var row in allRows)
            {
                var resRow = batch.NewRow();
                for (int i = 0; i < finalColumns.Count; i++)
                {
                    var col = finalColumns[i];
                    // If the column (by alias or exact expression match) is already in the row, use it.
                    // This is essential after Aggregation or Window functions.
                    if (col.Alias != null && row.HasColumn(col.Alias))
                    {
                        resRow[i] = row[col.Alias];
                    }
                    else if (row.HasColumn(col.Expression.ToSql()))
                    {
                        resRow[i] = row[col.Expression.ToSql()];
                    }
                    else if (col.Expression is FunctionCallExpression fce && fce.Window != null && row.HasColumn($"WINDOW_{fce.ToSql().ToUpperInvariant()}"))
                    {
                        resRow[i] = row[$"WINDOW_{fce.ToSql().ToUpperInvariant()}"];
                    }
                    else
                    {
                        resRow[i] = await _context.EvaluateValue(col.Expression, row);
                    }
                }

                if (seenRows != null)
                {
                    // Fix DISTINCT collapse: Use a unique sentinel for NULL to distinguish it from empty string
                    var key = string.Join("\0", colNames.Select(c => 
                    {
                        var val = resRow[c];
                        if (val == null || val == DBNull.Value) return "__NULL__";
                        var s = val.ToString() ?? "";
                        return s == "__NULL__" ? "__[NULL]__" : s; // Escape literal "__NULL__"
                    }));
                    if (!seenRows.Add(key)) continue;
                }

                await batch.AddRowAsync(resRow);
                if (batch.Rows.Count >= _context.BatchSize)
                {
                    yield return batch;
                    yielded = true;
                    batch = new DataTable();
                    batch.SetColumns(colNames);
                }
            }
            if (batch.Rows.Count > 0 || !yielded) yield return batch;
        }

        private async Task<List<Row>> SortInMemory(List<Row> rows, List<OrderByClause> orderBy, List<string> colNames, List<SelectColumn> finalColumns)
        {
            var rowSortKeys = new List<(Row Row, object?[] Keys)>(rows.Count);
            foreach (var row in rows)
            {
                var keys = new object?[orderBy.Count];
                for (int i = 0; i < orderBy.Count; i++)
                {
                    var expr = orderBy[i].Expression;
                    if (expr is LiteralExpression lit && lit.Type == TokenType.NUMBER && decimal.TryParse(lit.Value?.ToString(), out var num) && num > 0 && num <= colNames.Count)
                    {
                        keys[i] = row[colNames[(int)num - 1]];
                    }
                    Row? evalRow = null;
                    if (expr is IdentifierExpression id && colNames.Contains(id.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        keys[i] = row[id.Name];
                    }
                    else if (expr is IdentifierExpression idAlias && finalColumns.FirstOrDefault(c => string.Equals(c.Alias, idAlias.Name, StringComparison.OrdinalIgnoreCase)) is SelectColumn col)
                    {
                        evalRow = row;
                        keys[i] = await _context.EvaluateValue(col.Expression, evalRow);
                    }
                    else
                    {
                        evalRow = row;
                        keys[i] = await _context.EvaluateValue(expr, evalRow);
                    }
                }
                rowSortKeys.Add((row, keys));
            }

            rowSortKeys.Sort((a, b) => {
                for (int i = 0; i < orderBy.Count; i++)
                {
                    var res = _context.CompareConstants(a.Keys[i], b.Keys[i]);
                    if (res != 0) return orderBy[i].Descending ? -res : res;
                }
                return 0;
            });
            return rowSortKeys.Select(x => x.Row).ToList();
        }

        private async Task<List<Row>> ApplyLimits(List<Row> rows, SelectStatement stmt)
        {
            if (stmt.Offset != null)
            {
                int offset = Convert.ToInt32(await _context.EvaluateValue(stmt.Offset, new Row()));
                if (offset < 0) throw new ExecutionException("OFFSET must be a non-negative integer.");
                if (offset > 0) rows = rows.Skip(offset).ToList();
            }

            int take = -1;
            if (stmt.TopCount != null)
            {
                take = Convert.ToInt32(await _context.EvaluateValue(stmt.TopCount, new Row()));
                if (stmt.IsTopPercent) take = (int)Math.Ceiling(rows.Count * take / 100.0);
            }
            else if (stmt.LimitCount != null)
            {
                take = Convert.ToInt32(await _context.EvaluateValue(stmt.LimitCount, new Row()));
            }

            if (take >= 0) rows = rows.Take(take).ToList();
            return rows;
        }

        private static async IAsyncEnumerable<Row> PrependRows(IEnumerable<Row> buffered, IAsyncEnumerable<Row> remaining)
        {
            foreach (var r in buffered) yield return r;
            await foreach (var r in remaining) yield return r;
        }

        private static async IAsyncEnumerable<Row> ContinueStream(IAsyncEnumerator<Row> e)
        {
            while (await e.MoveNextAsync()) yield return e.Current;
        }

        private static async IAsyncEnumerable<Row> WhereStream(IAsyncEnumerable<Row> source, Expression clause, IExecutionContext context)
        {
            await foreach (var r in source) if (await context.EvaluateCondition(clause, r)) yield return r;
        }

        private async IAsyncEnumerable<Row> ConvertToAsyncEnumerable(List<Row> rows)
        {
            foreach (var r in rows) yield return r;
            await Task.CompletedTask;
        }
    }
}
