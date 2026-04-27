using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.Common;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the execution of SELECT statements, including CTEs, joins, aggregates, and window functions.
    /// Supports both streaming and multi-pass (buffered) execution strategies.
    /// </summary>
    public class SelectStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        private readonly CteManager _cteManager = new(logger);
        private readonly PushdownEngine _pushdownEngine = new(logger);
        private readonly ResultProcessor _resultProcessor = new(logger);

        public Type SupportedStatementType => typeof(SelectStatement);

 

        /// <summary>
        /// Executes a SELECT statement, handling pushdown to remote sources or local evaluation.
        /// </summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            _logger.Info($"[SELECT] Executing SelectStatement. Session: {context.SessionId}");

            // 1. Handle Pushdown (Optimization: Push simple queries to DB)
            if (statement is SelectStatement selPush && selPush.IntoTable == null)
            {
                if (_pushdownEngine.IsPushdownPossible(selPush, context, out var connName))
                {
                    var result = await _pushdownEngine.ExecutePushdown(selPush, connName!, context);
                    context.LastResult = result;
                    context.LastResultSets.Add(result);
                    context.OnResultSet?.Invoke(result);
                    return;
                }
            }

            var intoTable = context.GetIntoTable(statement);
            var forClause = context.GetForClause(statement);

            // 2. Handle SELECT INTO (Extract -> Stage -> Load)
            if (intoTable != null)
            {
                var destination = await context.ResolveDataSourceAsync(intoTable);
                await destination.TruncateAsync();

                var batches = EvaluateQuery(statement, context);
                
                var targetCols = (await destination.GetColumnsAsync()).ToList();
                if (targetCols.Count > 0) batches = context.AlignColumns(batches, targetCols);
                if (forClause != null) batches = context.EvaluateForClause(batches, forClause);
                
                // Record Lineage
                new LineageManager(context.LineageTracker).RecordSelectIntoLineage(statement, intoTable, context);

                var boundBatches = context.InterceptProgress(batches);
                await destination.WriteBatches(boundBatches);
            }
            // 3. Handle Standard SELECT (Extract -> Display)
            else
            {
                var batches = EvaluateQuery(statement, context);
                if (forClause != null) batches = context.EvaluateForClause(batches, forClause);
                await _resultProcessor.ProcessStream(batches, context, forClause != null);
            }
        }


        /// <summary>Evaluates a query statement and returns a stream of row batches.</summary>
        public async IAsyncEnumerable<DataTable> EvaluateQuery(Statement query, IExecutionContext context)
        {
            if (query.Ctes != null && query.Ctes.Count > 0) await _cteManager.RegisterCtes(query.Ctes, context);

            if (query is SelectStatement select)
            {
                await foreach (var batch in EvaluateSelect(select, context)) yield return batch;
            }
            else if (query is SetOperationStatement setOp)
            {
                var setEngine = new SetOperationEngine(context, _logger);
                await foreach (var batch in setEngine.ApplySetOperation(setOp)) yield return batch;
            }
        }


        /// <summary>
        /// Evaluates a SELECT statement, choosing between streaming or heavy multi-pass execution.
        /// </summary>
        public async IAsyncEnumerable<DataTable> EvaluateSelect(SelectStatement stmt, IExecutionContext context)
        {
            var aggregateEngine = new AggregateEngine(context, _logger);
            var windowEngine = new WindowEngine(context, aggregateEngine, _logger);

            // 1. Handle Remote Pushdown (No aggregation/sorting allowed in streaming pushdown here)
            if (stmt.IntoTable == null)
            {
                var fromConn = stmt.FromTable.ConnectionName ?? stmt.FromTable.TableName;
                bool allSameConn = (stmt.Joins == null || stmt.Joins.Count == 0) || 
                                   stmt.Joins.All(j => (j.Table.ConnectionName ?? j.Table.TableName).Equals(fromConn, StringComparison.OrdinalIgnoreCase));

                if (allSameConn && context.IsSqlPushdown(fromConn))
                {
                    // Check for local engines (aggregation, window functions, distinct, join)
                    // If none of those are present, we can push down the entire query including OFFSET/LIMIT.
                    // If those are present, we must fall through to the Heavy Pipeline.
                    bool localEngineRequired = stmt.Columns.Any(c => aggregateEngine.IsAggregate(c.Expression)) || 
                                               stmt.GroupBy != null || 
                                               stmt.Columns.Any(c => windowEngine.IsWindowFunction(c.Expression)) ||
                                               stmt.IsDistinct ||
                                               (stmt.Joins != null && stmt.Joins.Count > 0);

                    if (!localEngineRequired)
                    {
                        _logger.Debug("[SELECT] Pushing down query (possibly paged) to remote: {ConnName}", fromConn);
                        var conn = (IDatabaseSource)context.Connections[fromConn];
                        var compiled = context.CompileQuery(stmt, conn.Dialect);
                        await foreach (var batch in conn.ExecuteRawSql(compiled.Sql, compiled.Parameters.Values)) yield return batch;
                        yield break;
                    }
                }
            }

            // 2. Prepare Engines (Engines already prepared above)
            
            _logger.Debug("[SELECT] Evaluating local engine for {TableName}", stmt.FromTable.TableName);
            var batches = context.ResolveAndApplyOperators(stmt.FromTable);

            // 3. Metadata Discovery & Column Expansion
            var enumerator = batches.GetAsyncEnumerator();
            DataTable? firstBatch = null;
            try { if (await enumerator.MoveNextAsync()) firstBatch = enumerator.Current; }
            catch { await enumerator.DisposeAsync(); throw; }

            var effectiveBatches = ReplayBatches(firstBatch, enumerator);
            var (finalColumns, colNames) = await ExpandColumns(stmt, firstBatch?.ColumnNames ?? new List<string>());

            // 4. Strategy Selection
            bool hasAgg = stmt.Columns.Any(c => aggregateEngine.IsAggregate(c.Expression)) || stmt.GroupBy != null;
            bool hasWindow = stmt.Columns.Any(c => windowEngine.IsWindowFunction(c.Expression));
            bool isComplex = hasAgg || hasWindow || (stmt.Joins != null && stmt.Joins.Count > 0) || stmt.OrderBy != null || stmt.Offset != null || stmt.LimitCount != null || stmt.IsDistinct;

            if (!isComplex)
            {
                _logger.Debug("[SELECT] Execution Strategy: Fast Streaming");
                await foreach (var batch in ExecuteStreamingSelect(stmt, effectiveBatches, finalColumns, colNames, context)) yield return batch;
            }
            else
            {
                _logger.Debug("[SELECT] Execution Strategy: Multi-Pass Pipeline");
                var executionEngine = new SelectExecutionEngine(context, _logger);
                await foreach (var batch in executionEngine.ExecuteHeavyPipeline(stmt, effectiveBatches, finalColumns, colNames)) yield return batch;
            }
        }

        private async Task<(List<SelectColumn> Columns, List<string> Names)> ExpandColumns(SelectStatement stmt, List<string> sourceColumns)
        {
            var final = new List<SelectColumn>();
            foreach (var col in stmt.Columns)
            {
                if (col.Expression is IdentifierExpression id && (id.Name == "*" || id.Name.EndsWith(".*")))
                {
                    foreach (var sc in sourceColumns) final.Add(new SelectColumn(new IdentifierExpression(sc), sc));
                }
                else final.Add(col);
            }
            var names = final.Select(c => c.Alias ?? (c.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{final.IndexOf(c)}")).ToList();
            return (final, names);
        }

        private async IAsyncEnumerable<DataTable> ExecuteStreamingSelect(
            SelectStatement stmt, 
            IAsyncEnumerable<DataTable> batches, 
            List<SelectColumn> finalColumns, 
            List<string> colNames, 
            IExecutionContext context)
        {
            var resultBatch = new DataTable();
            resultBatch.SetColumns(colNames);

            bool yielded = false;
            int rowsYielded = 0;
            int rowsSkipped = 0;
            int offset = 0;
            if (stmt.Offset != null)
            {
                var offVal = await context.EvaluateValue(stmt.Offset, new Row());
                offset = Convert.ToInt32(offVal);
            }
            int? limit = null;
            if (stmt.LimitCount != null)
            {
                var limVal = await context.EvaluateValue(stmt.LimitCount, new Row());
                limit = Convert.ToInt32(limVal);
            }

            string fromName = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
            await foreach (var batch in batches)
            {
                foreach (var row in batch.Rows)
                {
                    // Qualify row for evaluation context (especially for correlated subqueries)
                    var evalRow = row;
                    if (!string.IsNullOrEmpty(fromName))
                    {
                        evalRow = row.Clone();
                        foreach (var kv in row.Columns)
                        {
                            if (!kv.Key.Contains(".")) evalRow[$"{fromName}.{kv.Key}"] = kv.Value;
                        }
                    }

                    if (stmt.WhereClause != null && !await context.EvaluateCondition(stmt.WhereClause, evalRow)) continue;
                    
                    if (rowsSkipped < offset)
                    {
                        rowsSkipped++;
                        continue;
                    }

                    if (limit.HasValue && rowsYielded >= limit.Value) goto done;

                    var resRow = resultBatch.NewRow();
                    for (int i = 0; i < finalColumns.Count; i++)
                        resRow[i] = await context.EvaluateValue(finalColumns[i].Expression, evalRow);
                    
                    await resultBatch.AddRowAsync(resRow);
                    rowsYielded++;

                    if (resultBatch.Rows.Count >= context.BatchSize)
                    {
                        yield return resultBatch;
                        yielded = true;
                        resultBatch = new DataTable();
                        resultBatch.SetColumns(colNames);
                    }
                }
                if (limit.HasValue && rowsYielded >= limit.Value) break;
            }
            done:
            if (resultBatch.Rows.Count > 0 || !yielded) yield return resultBatch;
        }

        private async IAsyncEnumerable<DataTable> ReplayBatches(DataTable? first, IAsyncEnumerator<DataTable> e)
        {
            try
            {
                if (first != null) yield return first;
                while (await e.MoveNextAsync()) yield return e.Current;
            }
            finally { await e.DisposeAsync(); }
        }
    }
}
