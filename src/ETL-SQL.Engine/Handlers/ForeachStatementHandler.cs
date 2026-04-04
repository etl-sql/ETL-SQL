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
        /// <summary>Executes the FOREACH statement, resolving the collection and iterating through its elements.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ForeachStatement)statement;
            
            var collection = await context.EvaluateValue(stmt.ListExpression, new Row());
            
            // Handle table names resolved as strings or identifiers
            if (collection == null || collection is string)
            {
                var tableName = collection?.ToString() ?? (stmt.ListExpression as IdentifierExpression)?.Name;
                if (tableName != null)
                {
                    var dataSource = await context.ResolveDataSourceAsync(new TableReference(tableName));
                    if (dataSource != null)
                    {
                        var rows = new List<Row>();
                        await foreach (var batch in dataSource.ReadBatches())
                        {
                            rows.AddRange(batch.Rows);
                        }
                        collection = rows;
                    }
                }
            }

            if (collection is DataTable dt)
            {
                collection = dt.Rows;
            }

            var iterVarName = stmt.VariableName.StartsWith("@") ? stmt.VariableName : "@" + stmt.VariableName;

            if (collection is IEnumerable list && collection is not string)
            {
                if (!context.ContainsVariable(iterVarName))
                {
                    context.DeclareVariable(iterVarName, null);
                }

                foreach (var item in list)
                {
                    context.SetVariable(iterVarName, item);
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
            else if (collection is System.Text.Json.JsonElement json && json.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                if (!context.ContainsVariable(iterVarName))
                {
                    context.DeclareVariable(iterVarName, null);
                }

                foreach (var item in json.EnumerateArray())
                {
                    context.SetVariable(iterVarName, item);
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
        }
    }
}
