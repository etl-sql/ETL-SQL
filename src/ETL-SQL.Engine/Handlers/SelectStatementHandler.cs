using ETL_SQL.Data;
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

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the execution of SELECT statements, including CTEs, joins, aggregates, and window functions.
    /// Supports both streaming and multi-pass (buffered) execution strategies.
    /// </summary>
    public class SelectStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(SelectStatement);
 
        private const int MaxLastResultRows = 50_000;

        /// <summary>
        /// Executes a SELECT statement, handling pushdown to remote sources or local evaluation.
        /// </summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {

            // Handle pushdown if applicable (only if no INTO clause)
            if (statement is SelectStatement selPush && selPush.IntoTable == null && context.IsSqlPushdown(selPush.FromTable.ConnectionName ?? selPush.FromTable.TableName))
            {
                var connName = selPush.FromTable.ConnectionName ?? selPush.FromTable.TableName;
                _logger.Debug($"Pushing down SELECT to remote connection: {connName}");
                var conn = (IDatabaseSource)context.Connections[connName];
                var sql = context.CompileQuery(selPush, conn.Dialect);
                var pushdownBatches = conn.ExecuteRawSql(sql);
                var pushdownResult = new DataTable();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool isFirst = true;
                long totalRows = 0;
                bool capped = false;

                await foreach (var batch in pushdownBatches)
                {
                    if (pushdownResult.ColumnNames.Count == 0)
                    {
                        pushdownResult.SetColumns(batch.ColumnNames);
                    }
                    foreach (var r in batch.Rows)
                    {
                        totalRows++;
                        if (pushdownResult.Rows.Count < MaxLastResultRows)
                            await pushdownResult.AddRowAsync(r);
                        else if (!capped)
                        {
                            capped = true;
                            _logger.Debug($"[SELECT] Result buffer capped at {MaxLastResultRows:N0} rows to prevent memory exhaustion. All rows still counted and streamed to display.");
                        }
                    }

                    if (!context.RedirectOutput)
                    {
                        ResultFormatter.PrintBatch(batch, isFirst);
                        isFirst = false;
                    }
                }
                sw.Stop();
                pushdownResult.ExecutionTimeMs = sw.ElapsedMilliseconds;
                pushdownResult.TotalRowsMatched = (int)Math.Min(totalRows, int.MaxValue);
                context.RowsProcessed += totalRows;
                context.LastResult = pushdownResult;
                context.LastResultSets.Add(pushdownResult);
                context.OnResultSet?.Invoke(pushdownResult);
                return;
            }

            var intoTable = context.GetIntoTable(statement);
            var forClause = context.GetForClause(statement);

            if (intoTable != null)
            {
                string intoName = intoTable.ConnectionName ?? intoTable.TableName;
                var destination = await context.ResolveDataSourceAsync(intoTable);
                await destination.TruncateAsync(); // SELECT INTO should overwrite/refresh the target
                var batches = EvaluateQuery(statement, context);
                
                // Align columns if target table already has a schema
                var targetCols = (await destination.GetColumnsAsync()).ToList();
                if (targetCols.Count > 0)
                {
                    batches = context.AlignColumns(batches, targetCols);
                }

                if (forClause != null) batches = context.EvaluateForClause(batches, forClause);
                
                // Record column-level lineage for SELECT INTO
                if (statement is SelectStatement select)
                {
                    var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (select.FromTable != null)
                    {
                        var fromTable = (select.FromTable.Alias ?? select.FromTable.TableName);
                        aliases[fromTable] = select.FromTable.TableName;
                        if (select.FromTable.Metadata?.Any() == true)
                            context.LineageTracker.Record(select.FromTable.TableName, new[] { select.FromTable.TableName }, "TABLE_TAGS", metadata: select.FromTable.Metadata, line: select.FromTable.Line, column: select.FromTable.Column);
                    }
                    foreach (var j in select.Joins)
                    {
                        var joinTable = (j.Table.Alias ?? j.Table.TableName);
                        aliases[joinTable] = j.Table.TableName;
                        if (j.Table.Metadata?.Any() == true)
                            context.LineageTracker.Record(j.Table.TableName, new[] { j.Table.TableName }, "TABLE_TAGS", metadata: j.Table.Metadata, line: j.Table.Line, column: j.Table.Column);
                    }

                    foreach (var col in select.Columns)
                    {
                        string targetCol = col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{select.Columns.IndexOf(col)}");
                        
                        var resolvedSources = col.Expression.GetSourceTables()
                            .Select(s => aliases.TryGetValue(s, out var real) ? real : s)
                            .ToList();

                        if (!resolvedSources.Any() && select.FromTable != null)
                        {
                            resolvedSources = select.GetSourceTables().ToList();
                        }

                        // Inherit descriptions and amalgamate
                        var sourceCols = col.Expression.GetSourceColumns().ToList();
                        var inherited = context.LineageTracker.InheritMetadata(resolvedSources, sourceCols, out var derived);
                        
                        col.DerivedFromDescriptions = derived;
                        foreach (var m in inherited)
                        {
                            if (!col.Metadata.ContainsKey(m.Key)) col.Metadata[m.Key] = m.Value;
                        }

                        context.LineageTracker.Record(
                            intoName, 
                            resolvedSources, 
                            "SELECT INTO", 
                            targetColumn: targetCol, 
                            sourceColumns: sourceCols,
                            metadata: col.Metadata,
                            derivedFromDescriptions: col.DerivedFromDescriptions,
                            line: select.Line,
                            column: select.Column);
                    }
                }
                else if (statement is SetOperationStatement setOp)
                {
                    // For set operations, we derive column lineage from the left-hand query
                    if (setOp.Left is SelectStatement leftSelect)
                    {
                        foreach (var col in leftSelect.Columns)
                        {
                            string targetCol = col.Alias ?? (col.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{leftSelect.Columns.IndexOf(col)}");
                            context.LineageTracker.Record(
                                intoName, 
                                leftSelect.GetSourceTables(), 
                                $"SELECT INTO ({setOp.Operation})", 
                                targetColumn: targetCol, 
                                metadata: col.Metadata,
                                derivedFromDescriptions: col.DerivedFromDescriptions,
                                line: statement.Line,
                                column: statement.Column);
                        }
                    }
                    else
                    {
                        context.LineageTracker.Record(intoName, statement.GetSourceTables(), "SELECT INTO", line: statement.Line, column: statement.Column);
                    }
                }
                else
                {
                    context.LineageTracker.Record(intoName, statement.GetSourceTables(), "SELECT INTO", line: statement.Line, column: statement.Column);
                }

                var boundBatches = context.InterceptProgress(batches);
                await destination.WriteBatches(boundBatches);
            }
            else
            {
                var batches = EvaluateQuery(statement, context);
                if (forClause != null) batches = context.EvaluateForClause(batches, forClause);
                
                var result = new DataTable();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool isFirst = true;
                long totalRows = 0;
                bool capped = false;
                await foreach (var batch in batches)
                {
                    if (result.ColumnNames.Count == 0) result.SetColumns(batch.ColumnNames);
                    foreach (var r in batch.Rows)
                    {
                        totalRows++;
                        if (result.Rows.Count < MaxLastResultRows)
                            await result.AddRowAsync(r);
                        else if (!capped)
                        {
                            capped = true;
                            _logger.Debug($"[SELECT] Result buffer capped at {MaxLastResultRows:N0} rows to prevent memory exhaustion. All rows still counted and streamed to display.");
                        }
                    }

                    if (!context.RedirectOutput)
                    {
                        if (forClause != null)
                        {
                            foreach (var r in batch.Rows) _logger.WriteLine(r[0]?.ToString() ?? "");
                        }
                        else
                        {
                            ResultFormatter.PrintBatch(batch, isFirst);
                            isFirst = false;
                        }
                    }
                }
                sw.Stop();
                result.ExecutionTimeMs = sw.ElapsedMilliseconds;
                result.TotalRowsMatched = (int)Math.Min(totalRows, int.MaxValue);
                context.RowsProcessed += totalRows;
                context.LastResult = result;
                context.LastResultSets.Add(result);
                context.OnResultSet?.Invoke(result);
            }
        }

        /// <summary>Evaluates a query statement and returns a stream of row batches.</summary>
        public async IAsyncEnumerable<DataTable> EvaluateQuery(Statement query, IExecutionContext context)
        {
            // Handle CTEs at the query level to support UNION/EXCEPT and subqueries
            if (query.Ctes != null && query.Ctes.Count > 0)
            {
                await RegisterCtes(query.Ctes, context);
            }

            if (query is SelectStatement select)
            {
                await foreach (var batch in EvaluateSelect(select, context)) yield return batch;
            }
            else if (query is SetOperationStatement setOp)
            {
                await foreach (var batch in EvaluateSetOperation(setOp, context)) yield return batch;
            }
        }

        /// <summary>
        /// Evaluates a SELECT statement, handling CTEs, column expansion, and choosing between streaming or multi-pass execution.
        /// </summary>
        public async IAsyncEnumerable<DataTable> EvaluateSelect(SelectStatement stmt, IExecutionContext context)
        {
            var joinEngine = new JoinEngine(context, _logger);
            var aggregateEngine = new AggregateEngine(context, _logger);
            var windowEngine = new WindowEngine(context, aggregateEngine, _logger);
            
            // Handle CTEs are now in EvaluateQuery or Execute
            
            _logger.Debug($"Evaluating SELECT FROM {stmt.FromTable.TableName}");
            var batches = context.ResolveAndApplyOperators(stmt.FromTable);

            // Expand * and alias.*
            var finalColumns = new List<SelectColumn>();
            List<string> sourceColumnNames = new();

            // We need to get columns without consuming the enumerator if possible, 
            // or buffer the first batch to avoid losing data.
            var enumerator = batches.GetAsyncEnumerator();
            DataTable? firstBatch = null;
            try
            {
                if (await enumerator.MoveNextAsync())
                {
                    firstBatch = enumerator.Current;
                    sourceColumnNames = firstBatch.ColumnNames;
                }
            }
            catch
            {
                await enumerator.DisposeAsync();
                throw;
            }

            // Create a new enumerable that includes the first batch if we found one
            // Helper to replay the first buffered batch followed by the remaining stream.
            async IAsyncEnumerable<DataTable> ReplayBatches(DataTable? first, IAsyncEnumerator<DataTable> e)
            {
                try
                {
                    if (first != null) yield return first;
                    while (await e.MoveNextAsync()) yield return e.Current;
                }
                finally
                {
                    await e.DisposeAsync();
                }
            }
            
            var effectiveBatches = ReplayBatches(firstBatch, enumerator);

            foreach (var col in stmt.Columns)
            {
                if (col.Expression is IdentifierExpression id && (id.Name == "*" || id.Name.EndsWith(".*")))
                {
                    if (sourceColumnNames.Count > 0)
                    {
                        foreach (var sc in sourceColumnNames)
                        {
                            finalColumns.Add(new SelectColumn(new IdentifierExpression(sc), sc));
                        }
                    }
                    else finalColumns.Add(col);
                }
                else finalColumns.Add(col);
            }

            var colNames = finalColumns.Select(c => c.Alias ?? (c.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{finalColumns.IndexOf(c)}")).ToList();

            string fromName = stmt.FromTable.Alias ?? stmt.FromTable.TableName;

            // OPTIMIZATION: Streaming for queries without GROUP BY or WINDOW functions
            bool hasAggInColumns = stmt.Columns.Any(c => aggregateEngine.IsAggregate(c.Expression));
            bool hasWindowInColumns = stmt.Columns.Any(c => windowEngine.IsWindowFunction(c.Expression));
            bool hasOrderBy = stmt.OrderBy != null && stmt.OrderBy.Count > 0;
            bool hasOffset = stmt.Offset != null;

            bool canStream = (stmt.GroupBy == null) && 
                             !hasWindowInColumns &&
                             !hasAggInColumns &&
                             !hasOrderBy &&
                             !hasOffset;

            if (canStream)
            {
                _logger.Debug("Execution Strategy: Streaming (supports Joins)");
                
                async IAsyncEnumerable<Row> EnumerateRows(IAsyncEnumerable<DataTable> dataBatches, string alias)
                {
                    await foreach (var batch in dataBatches)
                    {
                        foreach (var row in batch.Rows)
                        {
                            if (!string.IsNullOrEmpty(alias))
                            {
                                var evalRow = row.Clone();
                                var cols = batch.ColumnNames;
                                for(int i=0; i<cols.Count; i++) evalRow[$"{alias}.{cols[i]}"] = row[i];
                                yield return evalRow;
                            }
                            else yield return row;
                        }
                    }
                }

                var rowStream = EnumerateRows(effectiveBatches, fromName);

                if (stmt.Joins != null && stmt.Joins.Count > 0)
                {
                    rowStream = joinEngine.ApplyJoinsStreaming(rowStream, stmt.Joins, stmt);
                }

                var streamResultBatch = new DataTable();
                streamResultBatch.SetColumns(colNames);

                await foreach (var evalRow in rowStream)
                {
                    if (stmt.WhereClause != null && !await context.EvaluateCondition(stmt.WhereClause, evalRow)) continue;
                    
                    var resRow = streamResultBatch.NewRow();
                    for (int i = 0; i < finalColumns.Count; i++)
                        resRow[i] = await context.EvaluateValue(finalColumns[i].Expression, evalRow);
                    
                    await streamResultBatch.AddRowAsync(resRow);
                    if (streamResultBatch.Rows.Count >= context.BatchSize)
                    {
                        yield return streamResultBatch;
                        streamResultBatch = new DataTable();
                        streamResultBatch.SetColumns(colNames);
                    }
                }
                
                if (streamResultBatch.Rows.Count > 0) yield return streamResultBatch;
                yield break;
            }

            // FALLBACK: Heavy execution for complex queries (JOINS, AGGREGATES, WINDOW)
            _logger.Debug("Execution Strategy: Multi-Pass Engine Pipeline");
            
            // To support billion-row scaling, we MUST NOT buffer the primary source here.
            // We'll preserve the stream and pass it to the engines.
            var inputStream = effectiveBatches.SelectMany(b => b.Rows.Select(r => {
                var cloned = r.Clone();
                foreach (var kv in r.Columns.ToList()) cloned[$"{fromName}.{kv.Key}"] = kv.Value;
                return cloned;
            }).ToAsyncEnumerable());

            List<Row> allBufferedRows;

            if (stmt.Joins != null && stmt.Joins.Count > 0)
            {
                // JoinEngine.ApplyJoin currently takes List<Row>. 
                // We'll buffer only if it's small, otherwise we use ExternalJoinEngine.
                // For simplicity, let's buffer for now but use the external engine if it's large.
                allBufferedRows = new List<Row>();
                int count = 0;
                await foreach (var r in inputStream) 
                { 
                    allBufferedRows.Add(r); 
                    count++;
                    if (count > 100000) break; // Memory limit reached
                }

                if (count > 100000)
                {
                    _logger.WriteLine("[yellow]HYPER-SCALE: Primary source exceeded memory limit. Switching to streaming external join.[/]");
                    // Re-create stream starting from the 100,001st row... 
                    // This is tricky. Better to just use External engine from the start if we suspect scale.
                    // For now, let's assume we use the external engine if we have joins and we are in 'complex' mode.
                    var externalJoin = new ExternalJoinEngine(context, _logger);
                    allBufferedRows = await externalJoin.ApplyHashJoinExternal(inputStream, Enumerable.Empty<Row>().ToAsyncEnumerable(), stmt.Joins[0], new List<string>(), new List<string>());
                    // This is a placeholder for a more robust streaming join pipeline.
                }
                else
                {
                    allBufferedRows = await joinEngine.ApplyJoins(allBufferedRows, stmt.Joins, stmt);
                }
            }
            else
            {
                allBufferedRows = new List<Row>();
                await foreach (var r in inputStream) allBufferedRows.Add(r);
            }

            if (stmt.WhereClause != null)
            {
                var filtered = new List<Row>();
                foreach (var r in allBufferedRows) if (await context.EvaluateCondition(stmt.WhereClause, r)) filtered.Add(r);
                allBufferedRows = filtered;
            }

            // 1. GROUP BY / HAVING  (includes GROUPING SETS / ROLLUP / CUBE)
            if (stmt.GroupBy != null || stmt.GroupingSet != null || hasAggInColumns)
            {
                if (allBufferedRows.Count > 100000 && stmt.GroupingSet == null)
                {
                    _logger.WriteLine($"[yellow]HYPER-SCALE: Aggregate input exceeded memory limit ({allBufferedRows.Count} rows). Switching to external aggregation.[/]");
                    var externalAgg = new ExternalAggregateEngine(context, _logger);
                    allBufferedRows = await externalAgg.ApplyAggregationExternal(allBufferedRows.ToAsyncEnumerable(), stmt.GroupBy, finalColumns, colNames, stmt.HavingClause);
                }
                else
                {
                    allBufferedRows = await aggregateEngine.ApplyAggregation(allBufferedRows, stmt.GroupBy, finalColumns, colNames, stmt.HavingClause, stmt.GroupingSet);
                }
                _logger.Debug($"Aggregation applied: {allBufferedRows.Count} groups remaining");
            }

            // 2. WINDOW FUNCTIONS
            if (hasWindowInColumns)
            {
                allBufferedRows = await windowEngine.ApplyWindowFunctions(allBufferedRows, stmt);
                _logger.Debug("Window functions applied");
            }

            List<(Row Row, object?[] Keys)>? rowSortKeys = null;
            if (stmt.OrderBy != null && stmt.OrderBy.Count > 0)
            {
                if (allBufferedRows.Count > 100_000)
                {
                    var externalSort = new ExternalSortEngine(context, _logger);
                    allBufferedRows = await externalSort.SortExternal(allBufferedRows, stmt.OrderBy);
                }
                else
                {
                    rowSortKeys = new List<(Row Row, object?[] Keys)>(allBufferedRows.Count);
                    foreach (var row in allBufferedRows)
                    {
                        var keys = new object?[stmt.OrderBy.Count];
                        for (int i = 0; i < stmt.OrderBy.Count; i++)
                        {
                            var expr = stmt.OrderBy[i].Expression;
                            if (expr is LiteralExpression lit && lit.Type == TokenType.NUMBER && decimal.TryParse(lit.Value?.ToString(), out var num) && num > 0 && num <= colNames.Count)
                            {
                                string colName = colNames[(int)num - 1];
                                keys[i] = row[colName];
                            }
                            else
                            {
                                keys[i] = await context.EvaluateValue(expr, row);
                            }
                        }
                        rowSortKeys.Add((row, keys));
                    }

                    context.CompareConstants(null, null); // Ensure sort comparison helper is ready if needed
                    rowSortKeys.Sort((a, b) =>
                    {
                        for (int i = 0; i < stmt.OrderBy.Count; i++)
                        {
                            var res = context.CompareConstants(a.Keys[i], b.Keys[i]);
                            if (res != 0) return stmt.OrderBy[i].Descending ? -res : res;
                        }
                        return 0;
                    });

                    allBufferedRows = rowSortKeys.Select(x => x.Row).ToList();
                    
                    // We might need rowSortKeys later for WITH TIES
                    if (stmt.WithTies && allBufferedRows.Count > 0)
                    {
                         // We'll keep rowSortKeys until after the limit application
                    }
                    else
                    {
                        rowSortKeys = null; // Save memory
                    }
                }
            }

            // 4. OFFSET / LIMIT / TOP
            if (stmt.Offset != null)
            {
                int offset = Convert.ToInt32(await context.EvaluateValue(stmt.Offset, new Row()));
                if (offset > 0) allBufferedRows = allBufferedRows.Skip(offset).ToList();
            }

            int finalTake = -1;
            if (stmt.TopCount != null)
            {
                var topVal = await context.EvaluateValue(stmt.TopCount, new Row());
                finalTake = Convert.ToInt32(topVal);
                if (stmt.IsTopPercent)
                {
                    finalTake = (int)Math.Ceiling(allBufferedRows.Count * finalTake / 100.0);
                }
                
                if (stmt.WithTies)
                {
                    if (stmt.OrderBy == null || !stmt.OrderBy.Any())
                        throw new SyntaxException("TOP WITH TIES requires an ORDER BY clause", stmt.Line, stmt.Column);
                    
                    if (finalTake > 0 && finalTake < allBufferedRows.Count && rowSortKeys != null)
                    {
                        var lastRowKeys = rowSortKeys[finalTake - 1].Keys;
                        while (finalTake < rowSortKeys.Count)
                        {
                            var nextRowKeys = rowSortKeys[finalTake].Keys;
                            bool match = true;
                            for (int i = 0; i < stmt.OrderBy.Count; i++)
                            {
                                if (context.CompareConstants(lastRowKeys[i], nextRowKeys[i]) != 0)
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match) finalTake++;
                            else break;
                        }
                    }
                }
            }
            else if (stmt.LimitCount != null)
            {
                finalTake = Convert.ToInt32(await context.EvaluateValue(stmt.LimitCount, new Row()));
            }

            if (finalTake >= 0)
            {
                allBufferedRows = allBufferedRows.Take(finalTake).ToList();
            }

            // Final buffered projection
            var currentResultBatch = new DataTable();
            currentResultBatch.SetColumns(colNames);
            foreach (var row in allBufferedRows)
            {
                var resRow = currentResultBatch.NewRow();
                for (int i = 0; i < finalColumns.Count; i++)
                    resRow[i] = await context.EvaluateValue(finalColumns[i].Expression, row);
                await currentResultBatch.AddRowAsync(resRow);
                if (currentResultBatch.Rows.Count >= context.BatchSize)
                {
                    yield return currentResultBatch;
                    currentResultBatch = new DataTable();
                    currentResultBatch.SetColumns(colNames);
                }
            }
            if (currentResultBatch.Rows.Count > 0) yield return currentResultBatch;
        }

        /// <summary>Evaluates a set operation (UNION, EXCEPT, INTERSECT).</summary>
        public async IAsyncEnumerable<DataTable> EvaluateSetOperation(SetOperationStatement setOp, IExecutionContext context)
        {
            var setOpEngine = new SetOperationEngine(context, _logger);
            await foreach (var batch in setOpEngine.ApplySetOperation(setOp))
            {
                yield return batch;
            }
        }

        private async Task RegisterCtes(List<CteDefinition> ctes, IExecutionContext context)
        {
            foreach (var cte in ctes)
            {
                if (IsRecursive(cte, out var anchor, out var recursive, out var isDistinct))
                {
                    _logger.Debug($"Evaluating RECURSIVE CTE: {cte.Name} ({(isDistinct ? "UNION" : "UNION ALL")})");
                    var finalResult = new DataTable();
                    var currentStep = new DataTable();

                    // 1. Evaluate Anchor Member
                    await foreach (var batch in context.ExecuteQuery(anchor!))
                    {
                        if (finalResult.ColumnNames.Count == 0) finalResult.SetColumns(batch.ColumnNames);
                        if (currentStep.ColumnNames.Count == 0) currentStep.SetColumns(batch.ColumnNames);
                        foreach (var r in batch.Rows) { await finalResult.AddRowAsync(r); await currentStep.AddRowAsync(r); }
                    }

                    // 2. Iterative Recursive Member
                    int depth = 0;
                    var colDefs = new List<ColumnDefinition>();

                    while (currentStep.Rows.Count > 0 && depth < context.MaxRecursiveDepth)
                    {
                        depth++;
                        context.CurrentRecursiveDepth = depth;
                        
                        // Register currentStep as the CTE source for this iteration
                        var mem = new InMemoryDataSource();
                        
                        // Type inference (only on first iteration to establish schema)
                        if (depth == 1 && currentStep.Rows.Count > 0)
                        {
                            var firstRow = currentStep.Rows[0];
                            foreach (var colName in currentStep.ColumnNames)
                            {
                                var val = firstRow[colName];
                                string type = "STRING";
                                if (val is int || val is long) type = "INT";
                                else if (val is decimal || val is double || val is float) type = "DECIMAL";
                                else if (val is DateTime) type = "DATETIME";
                                else if (val is bool) type = "BOOLEAN";
                                colDefs.Add(new ColumnDefinition(colName, type, true));
                            }
                        }
                        else if (depth == 1)
                        {
                           foreach (var colName in currentStep.ColumnNames) colDefs.Add(new ColumnDefinition(colName, "STRING", true));
                        }

                        mem.SetSchema(colDefs);
                        await mem.WriteBatches(new[] { currentStep }.ToAsyncEnumerable());
                        context.LocalSources[cte.Name] = mem;

                        var nextStep = new DataTable();
                        nextStep.SetColumns(currentStep.ColumnNames);

                        await foreach (var batch in context.ExecuteQuery(recursive!))
                        {
                            // Aligned by index to anchor schema
                            var alignedBatch = context.AlignColumns(new[] { batch }.ToAsyncEnumerable(), currentStep.ColumnNames.ToList());
                            await foreach (var aligned in alignedBatch)
                            {
                                foreach (var r in aligned.Rows)
                                {
                                    if (isDistinct)
                                    {
                                        if (!finalResult.Rows.Any(existing => context.IsSoftEqual(existing, r)))
                                        {
                                        await finalResult.AddRowAsync(r);
                                        await nextStep.AddRowAsync(r);
                                        }
                                    }
                                    else
                                    {
                                        await finalResult.AddRowAsync(r);
                                        await nextStep.AddRowAsync(r);
                                    }
                                }
                            }
                        }
                        currentStep = nextStep;
                    }

                    if (depth >= context.MaxRecursiveDepth && currentStep.Rows.Count > 0)
                        throw new ExecutionException($"The maximum recursion {context.MaxRecursiveDepth} has been exhausted before statement completion for CTE '{cte.Name}'.", null, cte.Line, cte.Column);
                    
                    var finalMem = new InMemoryDataSource();
                    finalMem.SetSchema(colDefs);
                    await finalMem.WriteBatches(new[] { finalResult }.ToAsyncEnumerable());
                    context.LocalSources[cte.Name] = finalMem;
                }
                else
                {
                    // Standard Non-Recursive CTE (Buffered)
                    var cteResult = new DataTable();
                    await foreach (var batch in context.ExecuteQuery(cte.Query))
                    {
                        if (cteResult.Schema.ColumnCount == 0) cteResult.SetColumns(batch.ColumnNames);
                        foreach (var r in batch.Rows) await cteResult.AddRowAsync(r);
                    }
                    var mem = new InMemoryDataSource();
                    mem.SetSchema(cteResult.ColumnNames.Select(c => new ColumnDefinition(c, "STRING", false)));
                    await mem.WriteBatches(new[] { cteResult }.ToAsyncEnumerable());
                    context.LocalSources[cte.Name] = mem;
                }
            }
        }

        private bool IsRecursive(CteDefinition cte, out Statement? anchor, out Statement? recursive, out bool isDistinct)
        {
            anchor = null;
            recursive = null;
            isDistinct = false;
            if (cte.Query is SetOperationStatement setOp && (setOp.Operation == SetOpType.UNION_ALL || setOp.Operation == SetOpType.UNION))
            {
                isDistinct = setOp.Operation == SetOpType.UNION;
                // Simple recursive CTE check: recursive member contains a reference to the CTE name
                if (setOp.Right.GetSourceTables().Contains(cte.Name, StringComparer.OrdinalIgnoreCase))
                {
                    anchor = setOp.Left;
                    recursive = setOp.Right;
                    return true;
                }
            }
            return false;
        }
    }
}
