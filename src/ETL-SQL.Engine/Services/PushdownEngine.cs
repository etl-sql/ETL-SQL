using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;

namespace ETL_SQL.Engine.Services
{
    public class PushdownEngine(ILogger logger)
    {
        private readonly ILogger _logger = logger;

        public bool IsPushdownPossible(SelectStatement stmt, IExecutionContext context, out string? connectionName)
        {
            connectionName = null;
            if (stmt.FromTable == null) return false;

            connectionName = stmt.FromTable.ConnectionName ?? stmt.FromTable.TableName;
            var targetConn = connectionName;
            bool allSameConn = (stmt.Joins == null || stmt.Joins.Count == 0) ||
                               stmt.Joins.All(j => (j.Table.ConnectionName ?? j.Table.TableName).Equals(targetConn, StringComparison.OrdinalIgnoreCase));

            if (!allSameConn) return false;
            if (!context.IsSqlPushdown(connectionName)) return false;

            // Check for local engines (aggregation, window functions, distinct, join, subqueries)
            var aggregateEngine = new AggregateEngine(context, _logger);
            var windowEngine = new WindowEngine(context, aggregateEngine, _logger);
            var subqueryAnalyzer = new SubqueryAnalyzer();

            bool hasSubqueries = stmt.Columns.Any(c => HasSubqueries(c.Expression, subqueryAnalyzer)) ||
                                 HasSubqueries(stmt.WhereClause, subqueryAnalyzer) ||
                                 HasSubqueries(stmt.HavingClause, subqueryAnalyzer) ||
                                 (stmt.Joins != null && stmt.Joins.Any(j => HasSubqueries(j.Condition, subqueryAnalyzer)));

            bool localEngineRequired = hasSubqueries ||
                                       stmt.Columns.Any(c => aggregateEngine.IsAggregate(c.Expression)) ||
                                       stmt.GroupBy != null ||
                                       stmt.Columns.Any(c => windowEngine.IsWindowFunction(c.Expression)) ||
                                       stmt.IsDistinct ||
                                       (stmt.Joins != null && stmt.Joins.Count > 0);

            return !localEngineRequired;
        }

        private bool HasSubqueries(Expression? expr, SubqueryAnalyzer analyzer)
        {
            if (expr == null) return false;
            if (expr is SubqueryExpression) return true;
            if (expr is ExistsExpression) return true;
            if (expr is InExpression inExp && inExp.Right is SubqueryExpression) return true;

            // Check nested expressions
            if (expr is BinaryExpression bin) return HasSubqueries(bin.Left, analyzer) || HasSubqueries(bin.Right, analyzer);
            if (expr is FunctionCallExpression f) return f.Arguments.Any(a => HasSubqueries(a, analyzer));
            if (expr is CaseExpression c) return c.WhenClauses.Any(w => HasSubqueries(w.Condition, analyzer) || HasSubqueries(w.Result, analyzer)) || HasSubqueries(c.ElseResult, analyzer);

            return false;
        }

        public async Task<DataTable> ExecutePushdown(SelectStatement stmt, string connectionName, IExecutionContext context)
        {
            _logger.Debug("Pushing down SELECT to remote connection: {ConnName}", connectionName);
            var conn = (IDatabaseSource)context.Connections[connectionName];
            var compiled = context.CompileQuery(stmt, conn.Dialect);
            var pushdownBatches = conn.ExecuteRawSql(compiled.Sql, compiled.Parameters.Values);

            var result = new DataTable();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool isFirst = true;
            long totalRows = 0;
            bool capped = false;

            await foreach (var batch in pushdownBatches)
            {
                if (result.ColumnNames.Count == 0) result.SetColumns(batch.ColumnNames);

                bool shouldStop = false;
                foreach (var r in batch.Rows)
                {
                    if (result.Rows.Count < context.MaxLastResultRows)
                    {
                        totalRows++;
                        await result.AddRowAsync(r);
                    }
                    else if (!context.RedirectOutput)
                    {
                        if (!capped)
                        {
                            capped = true;
                            result.IsCapped = true;
                            _logger.Debug("[SELECT] Result buffer reached {MaxLastResultRows} rows. Stopping consumption to prevent memory exhaustion.", context.MaxLastResultRows);
                        }
                        shouldStop = true;
                        break;
                    }
                    else
                    {
                        totalRows++;
                        if (!capped)
                        {
                            capped = true;
                            result.IsCapped = true;
                        }
                    }
                }

                if (!context.RedirectOutput)
                {
                    ResultFormatter.PrintBatch(batch, isFirst);
                    isFirst = false;
                }

                if (shouldStop) break;
            }

            sw.Stop();
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;
            result.TotalRowsMatched = (int)Math.Min(totalRows, int.MaxValue);
            context.Telemetry.RowsProcessed += totalRows;
            return result;
        }

        public async IAsyncEnumerable<DataTable> ExecuteStreamingPushdown(SelectStatement stmt, string connectionName, IExecutionContext context)
        {
            _logger.Debug("[SELECT] Pushing down query (possibly paged) to remote: {ConnName}", connectionName);
            var conn = (IDatabaseSource)context.Connections[connectionName];
            var compiled = context.CompileQuery(stmt, conn.Dialect);
            await foreach (var batch in conn.ExecuteRawSql(compiled.Sql, compiled.Parameters.Values))
            {
                yield return batch;
            }
        }
    }
}

