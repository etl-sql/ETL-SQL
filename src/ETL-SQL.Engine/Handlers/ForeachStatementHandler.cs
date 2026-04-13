using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the FOREACH loop statement, iterating over collections, table rows, or JSON arrays.
    /// </summary>
    public class ForeachStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ForeachStatement);

        /// <summary>Executes the FOREACH statement, resolving the collection and iterating through its elements via streaming.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ForeachStatement)statement;
            var iterVarName = stmt.VariableName.StartsWith("@") ? stmt.VariableName : "@" + stmt.VariableName;
            if (!context.ContainsVariable(iterVarName))
            {
                context.DeclareVariable(iterVarName, null);
            }

            // 1. Try Paged Pushdown (Optimized Paging for Remote SQL)
            if (await TryPagedPushdown(stmt, context, iterVarName)) return;

            // 2. Fallback: Full Streaming Iteration
            await foreach (var row in context.EvaluateStream(stmt.ListExpression, new Row()))
            {
                // Optimization: If the row has exactly one column and its name is "Value", 
                // it's likely a scalar wrapper from a LIST or scalar expression. 
                // Unwrap it to provide the scalar value directly to the user.
                // Otherwise, keep it as a Row to allow member access (e.g., @row.ColumnName).
                // Only unwrap if it's a single column named "Value" (standard for simple collections/scalars)
                // Otherwise keep the row to allow member access (e.g. @row.Val)
                bool shouldUnwrap = row.Schema != null && 
                                   row.Schema.ColumnCount == 1 && 
                                   row.Schema.ColumnNames[0].Equals("Value", StringComparison.OrdinalIgnoreCase);

                object? val = shouldUnwrap ? row[0] : row;
                context.SetVariable(iterVarName, val);
                
                try
                {
                    await context.EvaluateStatement(stmt.Body);
                }
                catch (BreakException)
                {
                    break;
                }
                catch (ContinueException)
                {
                    continue;
                }
            }
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

            int pageSize = context.ForeachPageSize;
            int offset = 0;
            bool hasMore = true;

            while (hasMore)
            {
                // Create a paged version of the query
                // Using record 'with' expression for immutability-aware cloning
                var pagedQuery = sel with { Offset = new LiteralExpression(offset, TokenType.NUMBER), LimitCount = new LiteralExpression(pageSize, TokenType.NUMBER) };
                
                int rowsInPage = 0;
                await foreach (var batch in context.ExecuteQuery(pagedQuery))
                {
                    foreach (var row in batch.Rows)
                    {
                        rowsInPage++;
                        context.SetVariable(iterVarName, (row.Schema?.ColumnCount ?? 0) == 1 ? row[0] : row);
                        
                        try
                        {
                            await context.EvaluateStatement(stmt.Body);
                        }
                        catch (BreakException) { hasMore = false; goto finish; }
                        catch (ContinueException) { continue; }
                    }
                }

                if (rowsInPage < pageSize) break;
                offset += pageSize;
            }

            finish:
            return true;
        }
    }
}
