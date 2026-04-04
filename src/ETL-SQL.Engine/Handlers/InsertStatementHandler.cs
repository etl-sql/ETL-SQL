using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the execution of INSERT statements, including INSERT INTO SELECT, INSERT VALUES, and OUTPUT clauses.
    /// Supports remote pushdown for SQL sources and buffered batch transfers.
    /// </summary>
    public class InsertStatementHandler : IStatementHandler
    {
        private readonly ExecutePushdownStatementHandler _pushdownHandler;

        public InsertStatementHandler(ExecutePushdownStatementHandler pushdownHandler)
        {
            _pushdownHandler = pushdownHandler;
        }

        public Type SupportedStatementType => typeof(InsertStatement);

        /// <summary>Executes the INSERT statement against the target data source.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (InsertStatement)statement;
            

            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            if (stmt.TargetTable.ConnectionName == null && stmt.TargetTable.TableName.StartsWith("#") && !context.Connections.ContainsKey(connName))
            {
                context.Connections[connName] = new InMemoryDataSource();
            }

            Logger.Verbose($"Inserting into {connName}");
            
            if (stmt.SelectQuery != null && stmt.SelectQuery is SelectStatement select)
            {
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (select.FromTable != null) aliases[select.FromTable.Alias ?? select.FromTable.TableName] = select.FromTable.TableName;
                foreach (var j in select.Joins) aliases[j.Table.Alias ?? j.Table.TableName] = j.Table.TableName;

                var targetCols = stmt.Columns;
                for (int i = 0; i < select.Columns.Count; i++)
                {
                    var sourceCol = select.Columns[i];
                    string? targetCol = (targetCols != null && i < targetCols.Count) ? targetCols[i] : (sourceCol.Alias ?? (sourceCol.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : null));
                    
                    var resolvedSources = sourceCol.Expression.GetSourceTables()
                        .Select(s => aliases.TryGetValue(s, out var real) ? real : s)
                        .ToList();

                    if (!resolvedSources.Any() && select.FromTable != null)
                    {
                        resolvedSources = select.GetSourceTables().ToList();
                    }

                    var sourceCols = sourceCol.Expression.GetSourceColumns().ToList();
                    var inherited = context.LineageTracker.InheritMetadata(resolvedSources, sourceCols, out var derived);
                    
                    // Merge existing metadata tags from the SelectColumn (e.g. from /* @d: ... */)
                    foreach (var m in sourceCol.Metadata) inherited[m.Key] = m.Value;

                    context.LineageTracker.Record(
                        connName, 
                        resolvedSources, 
                        "INSERT", 
                        targetColumn: targetCol,
                        sourceColumns: sourceCols,
                        metadata: inherited,
                        derivedFromDescriptions: derived ?? sourceCol.DerivedFromDescriptions,
                        line: stmt.Line,
                        column: stmt.Column);
                }
            }
            else
            {
                context.LineageTracker.Record(connName, stmt.GetSourceTables(), "INSERT", line: stmt.Line, column: stmt.Column);
            }

            var destination = await context.ResolveDataSourceAsync(stmt.TargetTable);
            if (destination == null)
                 throw new ExecutionException($"Unknown connection: {connName} at Line {stmt.Line}");
            Logger.Verbose($"Destination resolved as {destination.GetType().Name}");

            if (destination is IDatabaseSource sqlDest)
            {
                if (stmt.SelectQuery != null && stmt.SelectQuery is SelectStatement sel && (sel.FromTable.ConnectionName ?? sel.FromTable.TableName).Equals(connName, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Verbose("Strategy: Remote SQL Pushdown (Insert from Select)");
                    var sql = $"INSERT INTO {context.GetSqlTableName(stmt.TargetTable)}\n{context.CompileQuery(stmt.SelectQuery, sqlDest.Dialect)}";
                    await foreach(var _ in sqlDest.ExecuteRawSql(sql)){}
                }
                else if (stmt.SelectQuery != null && stmt.SelectQuery is ExecutePushdownStatement pushdown)
                {
                    Logger.Verbose("Strategy: Remote SQL Pushdown (Insert from EXECUTE)");
                    // Handle as a batch transfer since the source is native SQL on potentially different connection
                    await PerformBatchTransfer(stmt, destination, context);
                }
                else if (stmt.Values != null)
                {
                    Logger.Verbose($"Strategy: Remote SQL Values ({stmt.Values.Count} rows)");
                    var rowStrings = stmt.Values.Select(row => "(" + string.Join(", ", row.Select(v => context.CompileExpression(v, sqlDest.Dialect))) + ")");
                    var colList = stmt.Columns != null ? "(" + string.Join(", ", stmt.Columns) + ") " : "";
                    var sql = $"INSERT INTO {context.GetSqlTableName(stmt.TargetTable)} {colList}VALUES {string.Join(", ", rowStrings)}";
                    await foreach(var _ in sqlDest.ExecuteRawSql(sql)){}
                }
                else
                {
                    await PerformBatchTransfer(stmt, destination, context);
                }
            }
            else
            {
                await PerformBatchTransfer(stmt, destination, context);
            }
        }

        /// <summary>Performs a row-by-row or batch-based insertion when pushdown is not possible.</summary>
        private async Task PerformBatchTransfer(InsertStatement stmt, IDataSource destination, IExecutionContext context)
        {
            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            if (stmt.SelectQuery != null)
            {
                Logger.Verbose("Strategy: Batch Transfer from SELECT/EXECUTE");
                IAsyncEnumerable<DataTable> batches;
                
                if (stmt.SelectQuery is ExecutePushdownStatement pushdown)
                {
                    await _pushdownHandler.Execute(pushdown, (Evaluator)context);
                    // Results are in context.LastResultSets
                    batches = ((Evaluator)context).LastResultSets.ToAsyncEnumerable();
                }
                else
                {
                    batches = context.ExecuteQuery(stmt.SelectQuery);
                }

                var forClause = context.GetForClause(stmt.SelectQuery);
                if (forClause != null) batches = context.EvaluateForClause(batches, forClause);

                var targetCols = stmt.Columns ?? (forClause == null ? (await destination.GetColumnsAsync()).ToList() : new List<string>());
                if (targetCols.Count > 0)
                {
                    batches = context.AlignColumns(batches, targetCols);
                }

                var boundBatches = context.InterceptProgress(batches);
                int count = 0;
                var allInsertedRows = new List<Row>();
                await foreach (var batch in boundBatches)
                {
                    await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable());
                    count += batch.Rows.Count;
                    if (stmt.Output != null) allInsertedRows.AddRange(batch.Rows);
                }
                context.RowsProcessed += count;

                if (stmt.Output != null && allInsertedRows.Count > 0)
                {
                    await ProcessOutputClause(stmt.Output, allInsertedRows, context);
                }

                if (context.IsVerbose) Logger.WriteLine($"Finished inserting {count} rows into {connName}");
            }
            else if (stmt.Values != null)
            {
                var colNames = new List<string>();
                if (stmt.Columns != null)
                {
                    colNames.AddRange(stmt.Columns);
                }
                else
                {
                    colNames.AddRange(await destination.GetColumnsAsync());
                    if (colNames.Count == 0 && stmt.Values.Count > 0)
                    {
                        for (int i = 0; i < stmt.Values[0].Count; i++) colNames.Add($"Col{i + 1}");
                    }
                }

                var batch = new DataTable();
                batch.SetColumns(colNames);

                var schema = (destination as InMemoryDataSource)?.Schema;

                foreach (var rowExprs in stmt.Values)
                {
                    var row = new Row();
                    for (int i = 0; i < colNames.Count && i < rowExprs.Count; i++)
                        row[colNames[i]] = await context.EvaluateValue(rowExprs[i], new Row());

                    // Apply defaults for missing columns
                    if (schema != null)
                    {
                        foreach (var colDef in schema.Values)
                        {
                            if (!row.Columns.ContainsKey(colDef.ColumnName) && colDef.DefaultExpression != null)
                            {
                                row[colDef.ColumnName] = await context.EvaluateValue(colDef.DefaultExpression, new Row());
                            }
                        }
                    }

                    batch.AddRow(row);
                }
                await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable());
                context.RowsProcessed += stmt.Values.Count;

                if (stmt.Output != null && batch.Rows.Count > 0)
                {
                    await ProcessOutputClause(stmt.Output, batch.Rows, context);
                }

                if (context.IsVerbose) Logger.WriteLine($"Finished inserting {stmt.Values.Count} rows into {connName}");
            }
        }

        /// <summary>Processes the OUTPUT clause of an INSERT statement, evaluating expressions against inserted rows.</summary>
        private async Task ProcessOutputClause(OutputClause output, List<Row> insertedRows, IExecutionContext context)
        {
            var outputTable = new DataTable();
            var outputRows = new List<Row>();

            foreach (var insertedRow in insertedRows)
            {
                var contextRow = new Row();
                foreach (var col in insertedRow.Columns)
                {
                    contextRow.Columns[$"INSERTED.{col.Key}"] = col.Value;
                    if (!contextRow.Columns.ContainsKey(col.Key)) contextRow.Columns[col.Key] = col.Value;
                }

                var outputRow = new Row();
                foreach (var outCol in output.Columns)
                {
                    var val = await context.EvaluateValue(outCol.Expression, contextRow);
                    outputRow.Columns[outCol.Alias ?? outCol.ToSql()] = val;
                }
                outputRows.Add(outputRow);
            }

            if (outputRows.Count > 0)
            {
                outputTable.SetColumns(outputRows[0].Columns.Keys);
                foreach (var r in outputRows) outputTable.AddRow(r);

                if (output.IntoTable != null)
                {
                    var intoDest = await context.ResolveDataSourceAsync(output.IntoTable);
                    await intoDest.WriteBatches(new[] { outputTable }.ToAsyncEnumerable());
                }
                else
                {
                    context.LastResult = outputTable;
                }
            }
        }
    }
}
