using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Engine.Services;
using Spectre.Console;

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
                var intoName = intoTable.ConnectionName ?? intoTable.TableName;
                if (context.VarContext.TryGetView(intoName, out _))
                    throw new ExecutionException($"View {intoName} is read-only and cannot be used as a SELECT INTO target.");

                var destination = await context.ResolveDataSourceAsync(intoTable);
                await destination.TruncateAsync();

                var batches = EvaluateQuery(statement, context);

                var targetCols = (await destination.GetColumnsAsync()).ToList();
                if (targetCols.Count > 0) batches = context.AlignColumns(batches, targetCols);
                if (forClause != null) batches = context.EvaluateForClause(batches, forClause);

                // Record Lineage (importing DB catalog metadata first, when enabled,
                // so source column comments inherit onto the derived columns).
                await context.EnsureCatalogMetadataImportedAsync(statement.GetSourceTables());
                new LineageManager(context.LineageTracker).RecordSelectIntoLineage(statement, intoTable, context);

                var boundBatches = context.InterceptProgress(batches);
                long totalRows = 0;
                async IAsyncEnumerable<DataTable> CountBatches(IAsyncEnumerable<DataTable> source)
                {
                    await foreach (var batch in source)
                    {
                        totalRows += batch.Rows.Count;
                        yield return batch;
                    }
                }

                await destination.WriteBatches(CountBatches(boundBatches), append: true);

                context.Variables["@@ROWCOUNT"] = totalRows;
                context.Logger.Info($"{totalRows} rows affected.");

                if (context.InteractiveMode)
                {
                    // Emit a friendly message for notebooks
                    context.OnMessage?.Invoke(new Diagnostic($"{totalRows} rows affected (INTO {intoTable.TableName})", 0, 0, DiagnosticSeverity.Info));
                }
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
            var metadataHelper = new QueryMetadataHelper(_logger);
            var streamingEngine = new StreamingQueryEngine(context, _logger);

            // 1. Handle Remote Pushdown (delegate to PushdownEngine)
            if (stmt.IntoTable == null && _pushdownEngine.IsPushdownPossible(stmt, context, out var connName))
            {
                await foreach (var batch in _pushdownEngine.ExecuteStreamingPushdown(stmt, connName!, context)) yield return batch;
                yield break;
            }

            // 2. Resolve Source
            _logger.Debug("[SELECT] Evaluating local engine for {TableName}", stmt.FromTable.TableName);
            var batches = context.ResolveAndApplyOperators(stmt.FromTable);

            // 3. Metadata Discovery & Column Expansion
            var enumerator = batches.GetAsyncEnumerator();
            DataTable? firstBatch = null;
            try { if (await enumerator.MoveNextAsync()) firstBatch = enumerator.Current; }
            catch { await enumerator.DisposeAsync(); throw; }

            var effectiveBatches = streamingEngine.ReplayBatches(firstBatch, enumerator);
            var (finalColumns, colNames) = await metadataHelper.ExpandColumns(stmt, firstBatch?.ColumnNames ?? new List<string>());


            // 4. Strategy Selection
            bool hasAgg = stmt.Columns.Any(c => aggregateEngine.IsAggregate(c.Expression)) || stmt.GroupBy != null;
            bool hasWindow = stmt.Columns.Any(c => windowEngine.IsWindowFunction(c.Expression));
            bool isComplex = hasAgg || hasWindow || (stmt.Joins != null && stmt.Joins.Count > 0) || stmt.OrderBy != null || stmt.Offset != null || stmt.LimitCount != null || stmt.IsDistinct || stmt.QualifyClause != null;

            if (!isComplex)
            {
                _logger.Debug("[SELECT] Execution Strategy: Fast Streaming");
                await foreach (var batch in streamingEngine.ExecuteStreamingSelect(stmt, effectiveBatches, finalColumns, colNames)) yield return batch;
            }
            else
            {
                _logger.Debug("[SELECT] Execution Strategy: Multi-Pass Pipeline");
                var executionEngine = new SelectExecutionEngine(context, _logger);
                await foreach (var batch in executionEngine.ExecuteHeavyPipeline(stmt, effectiveBatches, finalColumns, colNames)) yield return batch;
            }
        }
    }
}
