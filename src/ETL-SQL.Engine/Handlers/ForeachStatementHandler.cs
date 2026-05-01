using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Analysis;
using ETL_SQL.Core.Execution;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the FOREACH loop statement, iterating over collections, table rows, or JSON arrays.
    /// Supports optimized streaming for read-only sources.
    /// </summary>
    public class ForeachStatementHandler(IBufferManager bufferManager) : IStatementHandler
    {
        private readonly IBufferManager _bufferManager = bufferManager;
        public Type SupportedStatementType => typeof(ForeachStatement);

        /// <summary>Executes the FOREACH statement, resolving the collection and iterating through its elements.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ForeachStatement)statement;
            var iterVarName = stmt.VariableName.StartsWith("@") ? stmt.VariableName : "@" + stmt.VariableName;
            if (!context.VarContext.ContainsVariable(iterVarName))
            {
                context.VarContext.DeclareVariable(iterVarName, null);
            }

            // 1. Safety Check: If the loop body modifies the source table, we MUST use Paged Re-execution.
            // Also use paging if explicitly requested via ForeachPageSize (and ORBER BY is present).
            if (await ShouldUseSafePagedPath(stmt, context) || context.ForeachPageSize > 0)
            {
                if (await TryPagedPushdown(stmt, context, iterVarName)) return;
            }
            else
            {
                // 2. Fast Path: Streaming Iteration (Single cursor)
                if (await TryStreamingIteration(stmt, context, iterVarName)) return;
            }

            // 3. Fallback: Full In-Memory Streaming Iteration (for non-pushdown sources or collections)
            await foreach (var row in context.EvaluateStream(stmt.ListExpression, new Row()))
            {
                context.Telemetry.FetchStatus = 0;
                ProcessRow(row, iterVarName, stmt, context);
                
                try
                {
                    await context.EvaluateStatement(stmt.Body);
                }
                catch (BreakException) { context.Telemetry.FetchStatus = -1; break; }
                catch (ContinueException) { continue; }
            }
            context.Telemetry.FetchStatus = -1;
        }

        private async Task<bool> ShouldUseSafePagedPath(ForeachStatement stmt, IExecutionContext context)
        {
            var subq = stmt.ListExpression as SubqueryExpression;
            if (subq == null) return false;

            var sel = subq.Query as SelectStatement;
            if (sel == null) return false;

            var targetTable = sel.FromTable?.TableName;
            var targetConn = sel.FromTable?.ConnectionName;

            var detector = new DmlDetector(targetTable, targetConn);
            detector.Analyze(stmt.Body);

            if (detector.IsDmlDetected || detector.HasOpaqueCalls)
            {
                context.Log($"Side effects or DML detected in FOREACH body. Using SAFE Paged Re-execution path.");
                return true;
            }

            return false;
        }

        private async Task<bool> TryStreamingIteration(ForeachStatement stmt, IExecutionContext context, string iterVarName)
        {
            var subq = stmt.ListExpression as SubqueryExpression;
            if (subq == null) return false;

            var sel = subq.Query as SelectStatement;
            if (sel == null || sel.IntoTable != null) return false;

            // Resolve data source
            var ds = await context.ResolveDataSourceAsync(sel.FromTable);
            if (ds is not IDatabaseSource db || !db.SupportsSqlPushdown) return false;

            // Request a streaming cursor slot from the BufferManager
            using (await _bufferManager.AcquireCursorAsync(context.SessionId ?? "DEFAULT", owner: this))
            {
                context.Log($"Starting FAST-PATH Streaming FOREACH for {sel.FromTable.TableName} on {sel.FromTable.ConnectionName}");
                
                var compiled = context.CompileQuery(sel, db.Dialect);
                await foreach (var batch in db.ExecuteRawSql(compiled.Sql, compiled.Parameters.Values))
                {
                    foreach (var row in batch.Rows)
                    {
                        context.Telemetry.FetchStatus = 0;
                        ProcessRow(row, iterVarName, stmt, context);
                        
                        try
                        {
                            await context.EvaluateStatement(stmt.Body);
                        }
                        catch (BreakException) { context.Telemetry.FetchStatus = -1; return true; }
                        catch (ContinueException) { continue; }
                    }
                }
                context.Telemetry.FetchStatus = -1;
            }

            return true;
        }

        private void ProcessRow(Row row, string iterVarName, ForeachStatement stmt, IExecutionContext context)
        {
            // Optimization: If the row has exactly one column and its name is "Value", unwrap it
            bool shouldUnwrap = row.Schema != null && 
                               row.Schema.ColumnCount == 1 && 
                               row.Schema.ColumnNames[0].Equals("Value", StringComparison.OrdinalIgnoreCase);

            object? val = shouldUnwrap ? row[0] : row;
            context.VarContext.SetVariable(iterVarName, val);
        }

        private async Task<bool> TryPagedPushdown(ForeachStatement stmt, IExecutionContext context, string iterVarName)
        {
            var subq = stmt.ListExpression as SubqueryExpression;
            if (subq == null) return false;

            var sel = subq.Query as SelectStatement;
            if (sel == null || sel.IntoTable != null) return false;

            // Paging requires an ORDER BY for stability
            if (sel.OrderBy == null || sel.OrderBy.Count == 0) return false;

            // Resolve data source to check if it supports SQL pushdown
            var ds = await context.ResolveDataSourceAsync(sel.FromTable);
            if (ds is not IDatabaseSource db || !db.SupportsSqlPushdown) return false;

            int pageSize = context.ForeachPageSize > 0 ? context.ForeachPageSize : 10000;
            int offset = 0;
            bool hasMore = true;

            context.Log($"Starting SAFE-PATH Paged FOREACH for {sel.FromTable.TableName} on {sel.FromTable.ConnectionName}");

            while (hasMore)
            {
                // We manually deep-clone the parts we need via 'with' expression
                var pagedQuery = sel with 
                { 
                    Offset = new LiteralExpression((decimal)offset, TokenType.NUMBER),
                    LimitCount = new LiteralExpression((decimal)pageSize, TokenType.NUMBER)
                };
                
                int rowsInPage = 0;
                var compiled = context.CompileQuery(pagedQuery, db.Dialect);
                await foreach (var batch in db.ExecuteRawSql(compiled.Sql, compiled.Parameters.Values))
                {
                    foreach (var row in batch.Rows)
                    {
                        rowsInPage++;
                        context.Telemetry.FetchStatus = 0;
                        ProcessRow(row, iterVarName, stmt, context);
                        
                        try
                        {
                            await context.EvaluateStatement(stmt.Body);
                        }
                        catch (BreakException) { context.Telemetry.FetchStatus = -1; hasMore = false; goto finish; }
                        catch (ContinueException) { continue; }
                    }
                }
                context.Telemetry.FetchStatus = rowsInPage < pageSize ? -1 : 0;

                if (rowsInPage < pageSize) break;
                offset += pageSize;
            }

            finish:
            return true;
        }
    }
}

