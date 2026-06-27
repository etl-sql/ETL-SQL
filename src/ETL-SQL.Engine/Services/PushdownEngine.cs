using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;

namespace ETL_SQL.Engine.Services;
public class PushdownEngine(ILogger logger)
{
    private readonly ILogger _logger = logger;

    private static readonly HashSet<string> LocalOnlyFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "FILE_EXISTS", "DIRECTORY_EXISTS", "FILE_LIST", "DIRECTORY", "FILE_HASH", "FILE_SIZE", "FILE_MODIFIED",
        "REMOTE_FILE_LIST", "REMOTE_FILE_EXISTS", "PATH_COMBINE", "PATH_FILENAME", "PATH_EXTENSION", "PATH_DIRECTORY",
        "GET_JOB_STATE", "SET_JOB_STATE", "ENV", "CONNECTION_PROPERTY",
        "ERROR_NUMBER", "ERROR_MESSAGE", "ERROR_SEVERITY", "ERROR_STATE", "ERROR_LINE",
        "APPEND_TO_LIST", "ADD_TO_LIST", "REMOVE_FROM_LIST", "SORT_LIST",
        "GET_TAGS", "GET_TAG_VALUE", "HAS_TAG"
    };

    private bool ContainsLocalOnlyEngineFunctions(Expression? expr)
    {
        if (expr == null) return false;

        if (expr is FunctionCallExpression f)
        {
            if (LocalOnlyFunctions.Contains(f.FunctionName)) return true;
            if (f.Arguments.Any(ContainsLocalOnlyEngineFunctions)) return true;
            if (f.Filter != null && ContainsLocalOnlyEngineFunctions(f.Filter)) return true;
        }
        else if (expr is BinaryExpression bin)
        {
            return ContainsLocalOnlyEngineFunctions(bin.Left) || ContainsLocalOnlyEngineFunctions(bin.Right);
        }
        else if (expr is CaseExpression c)
        {
            if (c.InputExpression != null && ContainsLocalOnlyEngineFunctions(c.InputExpression)) return true;
            if (c.WhenClauses.Any(w => ContainsLocalOnlyEngineFunctions(w.Condition) || ContainsLocalOnlyEngineFunctions(w.Result))) return true;
            if (c.ElseResult != null && ContainsLocalOnlyEngineFunctions(c.ElseResult)) return true;
        }
        else if (expr is InExpression inExp)
        {
            if (ContainsLocalOnlyEngineFunctions(inExp.Left)) return true;
            if (inExp.Right != null && ContainsLocalOnlyEngineFunctions(inExp.Right)) return true;
            if (inExp.Subquery != null && ContainsLocalOnlyEngineFunctions(inExp.Subquery)) return true;
        }
        else if (expr is BetweenExpression bet)
        {
            return ContainsLocalOnlyEngineFunctions(bet.Left) ||
                   ContainsLocalOnlyEngineFunctions(bet.Start) ||
                   ContainsLocalOnlyEngineFunctions(bet.End);
        }
        else if (expr is LikeExpression like)
        {
            return ContainsLocalOnlyEngineFunctions(like.Left) ||
                   ContainsLocalOnlyEngineFunctions(like.Pattern) ||
                   (like.EscapeChar != null && ContainsLocalOnlyEngineFunctions(like.EscapeChar));
        }
        else if (expr is ExistsExpression ex)
        {
            return ContainsLocalOnlyEngineFunctions(ex.Subquery);
        }
        else if (expr is UnaryExpression un)
        {
            return ContainsLocalOnlyEngineFunctions(un.Expression);
        }
        else if (expr is IsNullExpression nullExpr)
        {
            return ContainsLocalOnlyEngineFunctions(nullExpr.Expression);
        }
        else if (expr is IsDistinctFromExpression idf)
        {
            return ContainsLocalOnlyEngineFunctions(idf.Left) || ContainsLocalOnlyEngineFunctions(idf.Right);
        }
        else if (expr is ListExpression list)
        {
            return list.Items.Any(ContainsLocalOnlyEngineFunctions);
        }
        else if (expr is SubqueryExpression sub)
        {
            return ContainsLocalOnlyEngineFunctions(sub.Query);
        }
        else if (expr is AtTimeZoneExpression tz)
        {
            return ContainsLocalOnlyEngineFunctions(tz.Left) || ContainsLocalOnlyEngineFunctions(tz.TimeZone);
        }
        else if (expr is MemberAccessExpression ma)
        {
            return ContainsLocalOnlyEngineFunctions(ma.Expression);
        }

        return false;
    }

    private bool ContainsLocalOnlyEngineFunctions(Statement? stmt)
    {
        if (stmt == null) return false;
        if (stmt is SelectStatement sel)
        {
            if (sel.Columns.Any(c => ContainsLocalOnlyEngineFunctions(c.Expression))) return true;
            if (ContainsLocalOnlyEngineFunctions(sel.WhereClause)) return true;
            if (sel.GroupBy != null && sel.GroupBy.Any(ContainsLocalOnlyEngineFunctions)) return true;
            if (ContainsLocalOnlyEngineFunctions(sel.HavingClause)) return true;
            if (sel.OrderBy != null && sel.OrderBy.Any(o => ContainsLocalOnlyEngineFunctions(o.Expression))) return true;
            if (ContainsLocalOnlyEngineFunctions(sel.LimitCount)) return true;
            if (ContainsLocalOnlyEngineFunctions(sel.Offset)) return true;
            if (ContainsLocalOnlyEngineFunctions(sel.TopCount)) return true;
            if (ContainsLocalOnlyEngineFunctions(sel.QualifyClause)) return true;

            if (sel.Joins != null)
            {
                foreach (var join in sel.Joins)
                {
                    if (ContainsLocalOnlyEngineFunctions(join.Condition)) return true;
                    if (join.Table?.Subquery != null && ContainsLocalOnlyEngineFunctions(join.Table.Subquery)) return true;
                }
            }
        }
        else if (stmt is SetOperationStatement setOp)
        {
            return ContainsLocalOnlyEngineFunctions(setOp.Left) || ContainsLocalOnlyEngineFunctions(setOp.Right);
        }
        return false;
    }

    public bool IsPushdownPossible(SelectStatement stmt, IExecutionContext context, out string? connectionName)
    {
        connectionName = null;
        if (stmt.FromTable == null || stmt.FromTable.ConnectionName == null) return false;
        if (stmt.GroupingSet != null) return false;

        connectionName = stmt.FromTable.ConnectionName;
        var targetConn = connectionName;
        bool allSameConn = (stmt.Joins == null || stmt.Joins.Count == 0) ||
                           stmt.Joins.All(j => (j.Table.ConnectionName ?? j.Table.TableName).Equals(targetConn, StringComparison.OrdinalIgnoreCase));

        if (!allSameConn) return false;
        if (!context.IsSqlPushdown(connectionName)) return false;

        // Check for window functions and subqueries which must be handled locally
        var aggregateEngine = new AggregateEngine(context, _logger);
        var windowEngine = new WindowEngine(context, aggregateEngine, _logger);
        var subqueryAnalyzer = new SubqueryAnalyzer();

        bool hasSubqueries = stmt.Columns.Any(c => HasSubqueries(c.Expression, subqueryAnalyzer)) ||
                             HasSubqueries(stmt.WhereClause, subqueryAnalyzer) ||
                             HasSubqueries(stmt.HavingClause, subqueryAnalyzer) ||
                             (stmt.Joins != null && stmt.Joins.Any(j => HasSubqueries(j.Condition, subqueryAnalyzer)));

        bool hasWindowFunctions = stmt.Columns.Any(c => windowEngine.IsWindowFunction(c.Expression));

        if (hasSubqueries || hasWindowFunctions) return false;

        if (ContainsLocalOnlyEngineFunctions(stmt)) return false;

        // Verify compilation
        try
        {
            if (context.Connections.TryGetValue(connectionName, out var ds) && ds is IDatabaseSource db)
            {
                context.CompileQuery(stmt, db.Dialect);
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
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

            DataTable? displayBatch = null;
            if (!context.RedirectOutput)
            {
                displayBatch = new DataTable();
                displayBatch.SetColumns(batch.ColumnNames);
            }

            bool shouldStop = false;
            foreach (var r in batch.Rows)
            {
                if (result.Rows.Count < context.MaxLastResultRows)
                {
                    totalRows++;
                    await result.AddRowAsync(r);
                    if (displayBatch != null) await displayBatch.AddRowAsync(r);
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

            if (!context.RedirectOutput && displayBatch != null && displayBatch.Rows.Count > 0)
            {
                ResultFormatter.PrintBatch(displayBatch, isFirst);
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

