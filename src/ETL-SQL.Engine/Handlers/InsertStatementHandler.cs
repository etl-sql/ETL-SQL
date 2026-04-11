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
    public class InsertStatementHandler(ILogger logger, ExecutePushdownStatementHandler pushdownHandler) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        private readonly ExecutePushdownStatementHandler _pushdownHandler = pushdownHandler;


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

            _logger.Debug($"Inserting into {connName}");
            
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
            _logger.Debug($"Destination resolved as {destination.GetType().Name}");

            if (destination is IDatabaseSource sqlDest)
            {
                if (stmt.SelectQuery != null && stmt.SelectQuery is SelectStatement sel && (sel.FromTable.ConnectionName ?? sel.FromTable.TableName).Equals(connName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("Strategy: Remote SQL Pushdown (Insert from Select)");
                    var sql = $"INSERT INTO {context.GetSqlTableName(stmt.TargetTable)}\n{context.CompileQuery(stmt.SelectQuery, sqlDest.Dialect)}";
                    if (context.IsWhatIf)
                    {
                        _logger.WriteLine($"WHAT IF: Would execute remote SQL pushdown insert on {connName}:\n{sql}", ConsoleColor.Yellow);
                    }
                    else
                    {
                        await foreach (var _ in sqlDest.ExecuteRawSql(sql)) { }
                    }
                }
                else if (stmt.SelectQuery != null && stmt.SelectQuery is ExecutePushdownStatement pushdown)
                {
                    _logger.Debug("Strategy: Remote SQL Pushdown (Insert from EXECUTE)");
                    // Handle as a batch transfer since the source is native SQL on potentially different connection
                    await PerformBatchTransfer(stmt, destination, context);
                }
                else if (stmt.Values != null)
                {
                    _logger.Debug($"Strategy: Remote SQL Values ({stmt.Values.Count} rows)");
                    var rowStrings = stmt.Values.Select(row => "(" + string.Join(", ", row.Select(v => context.CompileExpression(v, sqlDest.Dialect))) + ")");
                    var colList = stmt.Columns != null ? "(" + string.Join(", ", stmt.Columns) + ") " : "";
                    var sql = $"INSERT INTO {context.GetSqlTableName(stmt.TargetTable)} {colList}VALUES {string.Join(", ", rowStrings)}";
                    
                    if (context.IsWhatIf)
                    {
                        _logger.WriteLine($"WHAT IF: Would insert {stmt.Values.Count} rows into {connName} using raw SQL.", ConsoleColor.Yellow);
                    }
                    else
                    {
                        await foreach (var _ in sqlDest.ExecuteRawSql(sql)) { }
                    }
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
                _logger.Debug("Strategy: Batch Transfer from SELECT/EXECUTE");
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
                    if (context.IsWhatIf)
                    {
                        // Dry run: don't write
                    }
                    else
                    {
                        // Security Hardening: Block writing data into script files
                        if (!string.IsNullOrEmpty(destination.Path))
                        {
                            context.SecurityService.ValidateWriteAccess(destination.Path);
                        }
                        
                        await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable());
                    }
                    count += batch.Rows.Count;
                    if (stmt.Output != null) allInsertedRows.AddRange(batch.Rows);
                }
                
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would insert {count} rows into {connName} via batch transfer.", ConsoleColor.Yellow);
                }
                context.RowsProcessed += count;

                if (stmt.Output != null && allInsertedRows.Count > 0)
                {
                    await ProcessOutputClause(stmt.Output, allInsertedRows, context);
                }

                if (context.IsVerbose) _logger.WriteLine($"Finished inserting {count} rows into {connName}");
            }
            else if (stmt.Values != null)
            {
                var destinationCols = (await destination.GetColumnsAsync()).ToList();
                if (destinationCols.Count == 0 && stmt.Values.Count > 0)
                {
                    for (int i = 0; i < stmt.Values[0].Count; i++) destinationCols.Add($"Col{i + 1}");
                }

                var batch = new DataTable();
                batch.SetColumns(destinationCols);

                var schema = (destination as InMemoryDataSource)?.Schema;

                foreach (var rowExprs in stmt.Values)
                {
                    var row = batch.NewRow();
                    
                    // Map provided values to the correct row slots
                    for (int i = 0; i < rowExprs.Count; i++)
                    {
                        string? colName = stmt.Columns != null ? stmt.Columns[i] : (i < destinationCols.Count ? destinationCols[i] : null);
                        if (colName != null)
                        {
                            row[colName] = await context.EvaluateValue(rowExprs[i], new Row());
                        }
                    }

                    // Apply defaults for missing columns
                    if (schema != null)
                    {
                        foreach (var colDef in schema.Values)
                        {
                            bool isProvided = false;
                            if (stmt.Columns != null)
                                isProvided = stmt.Columns.Any(c => string.Equals(c, colDef.ColumnName, StringComparison.OrdinalIgnoreCase));
                            else
                            {
                                int destIdx = destinationCols.FindIndex(c => string.Equals(c, colDef.ColumnName, StringComparison.OrdinalIgnoreCase));
                                isProvided = destIdx >= 0 && destIdx < rowExprs.Count;
                            }

                            if (!isProvided && colDef.DefaultExpression != null)
                            {
                                row[colDef.ColumnName] = await context.EvaluateValue(colDef.DefaultExpression, new Row());
                            }
                        }
                    }

                    await batch.AddRowAsync(row);
                }

                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would insert {stmt.Values.Count} rows into {connName} via memory batch.", ConsoleColor.Yellow);
                }
                else
                {
                    await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable());
                }
                context.RowsProcessed += stmt.Values.Count;

                if (stmt.Output != null && batch.Rows.Count > 0)
                {
                    await ProcessOutputClause(stmt.Output, batch.Rows, context);
                }

                if (context.IsVerbose) _logger.WriteLine($"Finished inserting {stmt.Values.Count} rows into {connName}");
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
                contextRow["INSERTED"] = insertedRow;
                foreach (var col in insertedRow.Columns)
                {
                    if (!contextRow.HasColumn(col.Key)) contextRow[col.Key] = col.Value;
                }

                var outputRow = new Row();
                foreach (var outCol in output.Columns)
                {
                    var val = await context.EvaluateValue(outCol.Expression, contextRow);
                    outputRow[outCol.Alias ?? outCol.ToSql()] = val;
                }
                outputRows.Add(outputRow);
            }

            if (outputRows.Count > 0)
            {
                outputTable.SetColumns(outputRows[0].Columns.Keys);
                foreach (var r in outputRows) await outputTable.AddRowAsync(r);

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
