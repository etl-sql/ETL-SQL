using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Storage;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Handles the resolution of table references to physical or virtual data sources.
    /// Manages temporary tables, subqueries, and table-level operators (PIVOT/UNPIVOT).
    /// </summary>
    public class DataSourceManager
    {
        private readonly Evaluator _evaluator;
        private readonly ExpressionEvaluator _expressionEvaluator;

        public DataSourceManager(Evaluator evaluator, ExpressionEvaluator expressionEvaluator)
        {
            _evaluator = evaluator;
            _expressionEvaluator = expressionEvaluator;
        }

        /// <summary>
        /// Resolves a table reference to a functional IDataSource.
        /// Handles subqueries, function calls, dual table, and temporary tables.
        /// </summary>
        public async Task<IDataSource> ResolveDataSourceAsync(TableReference table, IDictionary<string, IDataSource> connections, TransactionManager transactionManager)
        {
            string name = table.ConnectionName ?? table.TableName;
            IDataSource? source = null;

            if (connections.TryGetValue(name, out source))
            {
                // Found in connections
            }
            else if (name.StartsWith("#") && table.ConnectionName == null)
            {
                var mem = new InMemoryDataSource();
                mem.Validator = _evaluator;
                connections[name] = mem;
                source = mem;
            }
            else if (table.Subquery != null)
            {
                return new StreamingSubqueryDataSource(_evaluator.ExecuteQuery(table.Subquery));
            }
            else if (table.FunctionCall != null)
            {
                if (table.FunctionCall.FunctionName.Equals("LINEAGE", StringComparison.OrdinalIgnoreCase))
                {
                    string? targetTbl = table.FunctionCall.Arguments.Count > 0 ? (await _expressionEvaluator.EvaluateInternal(table.FunctionCall.Arguments[0], new Row()))?.ToString() : null;
                    string? targetCol = table.FunctionCall.Arguments.Count > 1 ? (await _expressionEvaluator.EvaluateInternal(table.FunctionCall.Arguments[1], new Row()))?.ToString() : null;
                    return new LineageDataSource(_evaluator.LineageTracker, targetTbl, targetCol);
                }

                var result = await _expressionEvaluator.EvaluateInternal(table.FunctionCall, new Row());
                if (result is DataTable dt)
                {
                    var mem = new InMemoryDataSource();
                    await mem.WriteBatches(new[] { dt }.ToAsyncEnumerable());
                    return mem;
                }
                throw new ExecutionException($"Function {table.FunctionCall.FunctionName} did not return a table.");
            }
            else if (name.Equals("DUAL", StringComparison.OrdinalIgnoreCase))
            {
                if (!connections.ContainsKey("DUAL"))
                {
                    var dual = new InMemoryDataSource();
                    var dualTable = new DataTable();
                    dualTable.SetColumns(new[] { "DUMMY" });
                    dualTable.AddRow(new Row { ["DUMMY"] = "X" });
                    await dual.WriteBatches(new[] { dualTable }.ToAsyncEnumerable());
                    connections["DUAL"] = dual;
                }
                source = connections["DUAL"];
            }
            else if (name.StartsWith("@") && _evaluator.GetVariable(name) is System.Collections.IEnumerable list)
            {
                var mem = new InMemoryDataSource();
                var dt = new DataTable();
                dt.SetColumns(new[] { "Val" });
                foreach (var item in list) dt.AddRow(new Row { ["Val"] = item });
                await mem.WriteBatches(new[] { dt }.ToAsyncEnumerable());
                return mem;
            }

            if (source == null)
            {
                throw new ExecutionException($"Unknown source: {name} at Line {table.Line}");
            }

            if (transactionManager.TranCount > 0) await transactionManager.EnlistDataSource(source);

            if (table.ConnectionName != null) return source.WithTable(table.TableName);
            return source;
        }

        /// <summary>
        /// Reads batches from a data source and applies high-level operators like PIVOT or UNPIVOT.
        /// </summary>
        public async IAsyncEnumerable<DataTable> ResolveAndApplyOperators(TableReference table, IDictionary<string, IDataSource> connections, TransactionManager transactionManager, int batchSize)
        {
            var source = await ResolveDataSourceAsync(table, connections, transactionManager);
            var batches = source.ReadBatches(batchSize);
            
            if (table.TableOperators.Count == 0)
            {
                await foreach (var b in batches) yield return b;
                yield break;
            }

            // PIVOT/UNPIVOT currently requires buffering all data from the source to perform the transformation correctly
            var allRows = new List<Row>();
            string tableName = table.Alias ?? table.TableName;
            await foreach (var batch in batches)
            {
                foreach (var row in batch.Rows)
                {
                    var r = row.Clone();
                    // Prefix columns if not already prefixed
                    foreach (var kv in row.Columns.ToList())
                    {
                        if (!kv.Key.Contains(".")) r[$"{tableName}.{kv.Key}"] = kv.Value;
                    }
                    allRows.Add(r);
                }
            }

            var pivotEngine = new Engines.PivotEngine(_evaluator);
            foreach (var op in table.TableOperators)
            {
                if (op is PivotClause pivot) allRows = await pivotEngine.ApplyPivot(allRows, pivot);
                else if (op is UnpivotClause unpivot) allRows = await pivotEngine.ApplyUnpivot(allRows, unpivot);
            }

            var resultTable = new DataTable();
            if (allRows.Count > 0) resultTable.SetColumns(allRows[0].Columns.Keys);
            foreach (var r in allRows) resultTable.AddRow(r);
            
            yield return resultTable;
        }
    }
}
