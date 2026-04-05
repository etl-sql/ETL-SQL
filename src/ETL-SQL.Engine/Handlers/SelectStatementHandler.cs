using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.Common;
using ETL_SQL.Engine.Engines;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the execution of SELECT statements, including CTEs, joins, aggregates, and window functions.
    /// Supports both streaming and multi-pass (buffered) execution strategies.
    /// </summary>
    public class SelectStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(SelectStatement);
        private const int MaxLastResultRows = 50_000;
        private JoinEngine? _joinEngine;
        private AggregateEngine? _aggregateEngine;
        private WindowEngine? _windowEngine;
        private SetOperationEngine? _setOpEngine;
        private PivotEngine? _pivotEngine;

        /// <summary>
        /// Executes a SELECT statement, handling pushdown to remote sources or local evaluation.
        /// </summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            _joinEngine ??= new JoinEngine(context);
            _aggregateEngine ??= new AggregateEngine(context);
            _windowEngine ??= new WindowEngine(context, _aggregateEngine);

            // Handle pushdown if applicable (only if no INTO clause)
            if (statement is SelectStatement selPush && selPush.IntoTable == null && context.IsSqlPushdown(selPush.FromTable.ConnectionName ?? selPush.FromTable.TableName))
            {
                var connName = selPush.FromTable.ConnectionName ?? selPush.FromTable.TableName;
                Logger.Verbose($"Pushing down SELECT to remote connection: {connName}");
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
                            pushdownResult.AddRow(r);
                        else if (!capped)
                        {
                            capped = true;
                            Logger.Verbose($"[SELECT] Result buffer capped at {MaxLastResultRows:N0} rows to prevent memory exhaustion. All rows still counted and streamed to display.");
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
                            result.AddRow(r);
                        else if (!capped)
                        {
                            capped = true;
                            Logger.Verbose($"[SELECT] Result buffer capped at {MaxLastResultRows:N0} rows to prevent memory exhaustion. All rows still counted and streamed to display.");
                        }
                    }

                    if (!context.RedirectOutput)
                    {
                        if (forClause != null)
                        {
                            foreach (var r in batch.Rows) Logger.WriteLine(r[0]?.ToString() ?? "");
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
            _joinEngine ??= new JoinEngine(context);
            _aggregateEngine ??= new AggregateEngine(context);
            _windowEngine ??= new WindowEngine(context, _aggregateEngine);
            _pivotEngine ??= new PivotEngine(context);
            
            // Handle CTEs are now in EvaluateQuery or Execute
            
            Logger.Verbose($"Evaluating SELECT FROM {stmt.FromTable.TableName}");
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
            bool hasAggInColumns = stmt.Columns.Any(c => _aggregateEngine.IsAggregate(c.Expression));
            bool hasWindowInColumns = stmt.Columns.Any(c => _windowEngine.IsWindowFunction(c.Expression));
            bool hasOrderBy = stmt.OrderBy != null && stmt.OrderBy.Count > 0;
            bool hasOffset = stmt.Offset != null;

            bool canStream = (stmt.GroupBy == null) && 
                             !hasWindowInColumns &&
                             !hasAggInColumns &&
                             !hasOrderBy &&
                             !hasOffset;

            if (canStream)
            {
                Logger.Verbose("Execution Strategy: Streaming (supports Joins)");
                
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
                    rowStream = _joinEngine!.ApplyJoinsStreaming(rowStream, stmt.Joins, stmt);
                }

                var streamResultBatch = new DataTable();
                streamResultBatch.SetColumns(colNames);

                await foreach (var evalRow in rowStream)
                {
                    if (stmt.WhereClause != null && !await context.EvaluateCondition(stmt.WhereClause, evalRow)) continue;
                    
                    var resRow = streamResultBatch.NewRow();
                    for (int i = 0; i < finalColumns.Count; i++)
                        resRow[i] = await context.EvaluateValue(finalColumns[i].Expression, evalRow);
                    
                    streamResultBatch.AddRow(resRow);
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
            Logger.Verbose("Execution Strategy: Multi-Pass Engine Pipeline");
            
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
                    Logger.WriteLine("[yellow]HYPER-SCALE: Primary source exceeded memory limit. Switching to streaming external join.[/]");
                    // Re-create stream starting from the 100,001st row... 
                    // This is tricky. Better to just use External engine from the start if we suspect scale.
                    // For now, let's assume we use the external engine if we have joins and we are in 'complex' mode.
                    var externalJoin = new ExternalJoinEngine(context);
                    allBufferedRows = await externalJoin.ApplyHashJoinExternal(inputStream, Enumerable.Empty<Row>().ToAsyncEnumerable(), stmt.Joins[0], new List<string>(), new List<string>());
                    // This is a placeholder for a more robust streaming join pipeline.
                }
                else
                {
                    allBufferedRows = await _joinEngine!.ApplyJoins(allBufferedRows, stmt.Joins, stmt);
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

            // 1. GROUP BY / HAVING
            if (stmt.GroupBy != null || hasAggInColumns)
            {
                if (allBufferedRows.Count > 100000)
                {
                    Logger.WriteLine($"[yellow]HYPER-SCALE: Aggregate input exceeded memory limit ({allBufferedRows.Count} rows). Switching to external aggregation.[/]");
                    var externalAgg = new ExternalAggregateEngine(context);
                    allBufferedRows = await externalAgg.ApplyAggregationExternal(allBufferedRows.ToAsyncEnumerable(), stmt.GroupBy, finalColumns, colNames, stmt.HavingClause);
                }
                else
                {
                    allBufferedRows = await _aggregateEngine!.ApplyAggregation(allBufferedRows, stmt.GroupBy, finalColumns, colNames, stmt.HavingClause);
                }
                Logger.Verbose($"Aggregation applied: {allBufferedRows.Count} groups remaining");
            }

            // 2. WINDOW FUNCTIONS
            if (hasWindowInColumns)
            {
                allBufferedRows = await _windowEngine!.ApplyWindowFunctions(allBufferedRows, stmt);
                Logger.Verbose("Window functions applied");
            }

            // 3. Global ORDER BY
            if (stmt.OrderBy != null && stmt.OrderBy.Count > 0)
            {
                if (allBufferedRows.Count > 100000)
                    Logger.WriteLine($"[yellow]HYPER-SCALE: ORDER BY input has {allBufferedRows.Count:N0} rows. In-memory sort — consider adding a LIMIT or pushing ORDER BY to the source query.[/]");

                var rowSortKeys = new List<(Row Row, object?[] Keys)>(allBufferedRows.Count);
                foreach (var row in allBufferedRows)
                {
                    var keys = new object?[stmt.OrderBy.Count];
                    for (int i = 0; i < stmt.OrderBy.Count; i++)
                    {
                        keys[i] = await context.EvaluateValue(stmt.OrderBy[i].Expression, row);
                    }
                    rowSortKeys.Add((row, keys));
                }

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
            }

            // 4. OFFSET / LIMIT
            if (stmt.Offset != null)
            {
                int offset = Convert.ToInt32(await context.EvaluateValue(stmt.Offset, new Row()));
                if (offset > 0) allBufferedRows = allBufferedRows.Skip(offset).ToList();
            }

            if (stmt.LimitCount != null)
            {
                int limit = Convert.ToInt32(await context.EvaluateValue(stmt.LimitCount, new Row()));
                if (limit >= 0) allBufferedRows = allBufferedRows.Take(limit).ToList();
            }

            // Final buffered projection
            var currentResultBatch = new DataTable();
            currentResultBatch.SetColumns(colNames);
            foreach (var row in allBufferedRows)
            {
                var resRow = currentResultBatch.NewRow();
                for (int i = 0; i < finalColumns.Count; i++)
                    resRow[i] = await context.EvaluateValue(finalColumns[i].Expression, row);
                currentResultBatch.AddRow(resRow);
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
            _setOpEngine ??= new SetOperationEngine(context);
            await foreach (var batch in _setOpEngine.ApplySetOperation(setOp))
            {
                yield return batch;
            }
        }

        private async Task RegisterCtes(List<CteDefinition> ctes, IExecutionContext context)
        {
            foreach (var cte in ctes)
            {
                if (IsRecursive(cte, out var anchor, out var recursive))
                {
                    Logger.Verbose($"Evaluating RECURSIVE CTE: {cte.Name}");
                    var finalResult = new DataTable();
                    var currentStep = new DataTable();

                    // 1. Evaluate Anchor
                    await foreach (var batch in EvaluateQuery(anchor!, context))
                    {
                        if (finalResult.ColumnNames.Count == 0) finalResult.ColumnNames.AddRange(batch.ColumnNames);
                        if (currentStep.ColumnNames.Count == 0) currentStep.ColumnNames.AddRange(batch.ColumnNames);
                        foreach (var r in batch.Rows) { finalResult.AddRow(r); currentStep.AddRow(r); }
                    }

                    // 2. Iterative Recursive Member
                    int depth = 0;
                    const int MAX_RECURSION = 100;
                    while (currentStep.Rows.Count > 0 && depth < MAX_RECURSION)
                    {
                        depth++;
                        context.MaxRecursiveDepth = Math.Max(context.MaxRecursiveDepth, depth);
                        Logger.Verbose($"[DIAG-RECURSION] Level {depth} for CTE {cte.Name}. Internal rows in current step: {currentStep.Rows.Count}");
                        var nextStep = new DataTable();
                        // Temporarily register currentStep as the CTE source
                        var mem = new InMemoryDataSource();
                        mem.SetSchema(currentStep.ColumnNames.Select(c => new ColumnDefinition(c, "STRING", false)));
                        await mem.WriteBatches(new[] { currentStep }.ToAsyncEnumerable());
                        context.Connections[cte.Name] = mem;

                        await foreach (var batch in EvaluateQuery(recursive!, context))
                        {
                            if (nextStep.ColumnNames.Count == 0) nextStep.ColumnNames.AddRange(batch.ColumnNames);
                            foreach (var r in batch.Rows) { finalResult.AddRow(r); nextStep.AddRow(r); }
                        }
                        currentStep = nextStep;
                    }
                    
                    if (finalResult.Schema.ColumnCount == 0 && finalResult.Rows.Count > 0)
                    {
                        finalResult.SetColumns(finalResult.Rows[0].Columns.Keys);
                    }
                    var finalMem = new InMemoryDataSource();
                    finalMem.SetSchema(finalResult.ColumnNames.Select(c => new ColumnDefinition(c, "STRING", false)));
                    await finalMem.WriteBatches(new[] { finalResult }.ToAsyncEnumerable());
                    context.Connections[cte.Name] = finalMem;
                }
                else
                {
                    // Standard Non-Recursive CTE (Buffered)
                    var cteResult = new DataTable();
                    await foreach (var batch in EvaluateQuery(cte.Query, context))
                    {
                        if (cteResult.Schema.ColumnCount == 0) cteResult.SetColumns(batch.ColumnNames);
                        foreach (var r in batch.Rows) cteResult.AddRow(r);
                    }
                    var mem = new InMemoryDataSource();
                    mem.SetSchema(cteResult.ColumnNames.Select(c => new ColumnDefinition(c, "STRING", false)));
                    await mem.WriteBatches(new[] { cteResult }.ToAsyncEnumerable());
                    context.Connections[cte.Name] = mem;
                }
            }
        }

        private bool IsRecursive(CteDefinition cte, out Statement? anchor, out Statement? recursive)
        {
            anchor = null;
            recursive = null;
            if (cte.Query is SetOperationStatement setOp && (setOp.Operation == SetOpType.UNION_ALL || setOp.Operation == SetOpType.UNION))
            {
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
