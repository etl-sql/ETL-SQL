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

            // Handle pushdown if applicable (only if no INTO clause and ALL tables are on the same pushable connection)
            if (statement is SelectStatement selPush && selPush.IntoTable == null)
            {
                var fromConn = selPush.FromTable.ConnectionName ?? selPush.FromTable.TableName;
                bool allSameConn = (selPush.Joins == null || selPush.Joins.Count == 0) || 
                                   selPush.Joins.All(j => (j.Table.ConnectionName ?? j.Table.TableName).Equals(fromConn, StringComparison.OrdinalIgnoreCase));

                if (allSameConn && context.IsSqlPushdown(fromConn))
                {
                    _logger.Debug("Pushing down SELECT to remote connection: {ConnName}", fromConn);
                    var conn = (IDatabaseSource)context.Connections[fromConn];
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
                                _logger.Debug("[SELECT] Result buffer capped at {MaxLastResultRows} rows to prevent memory exhaustion. All rows still counted and streamed to display.", MaxLastResultRows);
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
                            _logger.Debug("[SELECT] Result buffer capped at {MaxLastResultRows} rows to prevent memory exhaustion. All rows still counted and streamed to display.", MaxLastResultRows);
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
            if (query.Ctes != null && query.Ctes.Count > 0) await RegisterCtes(query.Ctes, context);

            if (query is SelectStatement select)
            {
                await foreach (var batch in EvaluateSelect(select, context)) yield return batch;
            }
            else if (query is SetOperationStatement setOp)
            {
                var setEngine = new SetOperationEngine(context, _logger);
                await foreach (var batch in setEngine.ApplySetOperation(setOp)) yield return batch;
            }
        }

        /// <summary>
        /// Evaluates a SELECT statement, choosing between streaming or heavy multi-pass execution.
        /// </summary>
        public async IAsyncEnumerable<DataTable> EvaluateSelect(SelectStatement stmt, IExecutionContext context)
        {
            // 1. Handle Remote Pushdown (No aggregation/sorting allowed in streaming pushdown here)
            if (stmt.IntoTable == null)
            {
                var fromConn = stmt.FromTable.ConnectionName ?? stmt.FromTable.TableName;
                bool allSameConn = (stmt.Joins == null || stmt.Joins.Count == 0) || 
                                   stmt.Joins.All(j => (j.Table.ConnectionName ?? j.Table.TableName).Equals(fromConn, StringComparison.OrdinalIgnoreCase));

                if (allSameConn && context.IsSqlPushdown(fromConn))
                {
                    _logger.Debug("[SELECT] Pushing down subquery to remote: {ConnName}", fromConn);
                    var conn = (IDatabaseSource)context.Connections[fromConn];
                    var sql = context.CompileQuery(stmt, conn.Dialect);
                    await foreach (var batch in conn.ExecuteRawSql(sql)) yield return batch;
                    yield break;
                }
            }

            // 2. Prepare Engines
            var aggregateEngine = new AggregateEngine(context, _logger);
            var windowEngine = new WindowEngine(context, aggregateEngine, _logger);
            
            _logger.Debug("[SELECT] Evaluating local engine for {TableName}", stmt.FromTable.TableName);
            var batches = context.ResolveAndApplyOperators(stmt.FromTable);

            // 3. Metadata Discovery & Column Expansion
            var enumerator = batches.GetAsyncEnumerator();
            DataTable? firstBatch = null;
            try { if (await enumerator.MoveNextAsync()) firstBatch = enumerator.Current; }
            catch { await enumerator.DisposeAsync(); throw; }

            var effectiveBatches = ReplayBatches(firstBatch, enumerator);
            var (finalColumns, colNames) = await ExpandColumns(stmt, firstBatch?.ColumnNames ?? new List<string>());

            // 4. Strategy Selection
            bool hasAgg = stmt.Columns.Any(c => aggregateEngine.IsAggregate(c.Expression)) || stmt.GroupBy != null;
            bool hasWindow = stmt.Columns.Any(c => windowEngine.IsWindowFunction(c.Expression));
            bool isComplex = hasAgg || hasWindow || (stmt.Joins != null && stmt.Joins.Count > 0) || stmt.OrderBy != null || stmt.Offset != null || stmt.IsDistinct;

            if (!isComplex)
            {
                _logger.Debug("[SELECT] Execution Strategy: Fast Streaming");
                await foreach (var batch in ExecuteStreamingSelect(stmt, effectiveBatches, finalColumns, colNames, context)) yield return batch;
            }
            else
            {
                _logger.Debug("[SELECT] Execution Strategy: Multi-Pass Pipeline");
                var executionEngine = new SelectExecutionEngine(context, _logger);
                await foreach (var batch in executionEngine.ExecuteHeavyPipeline(stmt, effectiveBatches, finalColumns, colNames)) yield return batch;
            }
        }

        private async Task<(List<SelectColumn> Columns, List<string> Names)> ExpandColumns(SelectStatement stmt, List<string> sourceColumns)
        {
            var final = new List<SelectColumn>();
            foreach (var col in stmt.Columns)
            {
                if (col.Expression is IdentifierExpression id && (id.Name == "*" || id.Name.EndsWith(".*")))
                {
                    foreach (var sc in sourceColumns) final.Add(new SelectColumn(new IdentifierExpression(sc), sc));
                }
                else final.Add(col);
            }
            var names = final.Select(c => c.Alias ?? (c.Expression is IdentifierExpression id ? id.Name.Split('.').Last() : $"Expr{final.IndexOf(c)}")).ToList();
            return (final, names);
        }

        private async IAsyncEnumerable<DataTable> ExecuteStreamingSelect(
            SelectStatement stmt, 
            IAsyncEnumerable<DataTable> batches, 
            List<SelectColumn> finalColumns, 
            List<string> colNames, 
            IExecutionContext context)
        {
            var resultBatch = new DataTable();
            resultBatch.SetColumns(colNames);

            bool yielded = false;
            await foreach (var batch in batches)
            {
                foreach (var row in batch.Rows)
                {
                    if (stmt.WhereClause != null && !await context.EvaluateCondition(stmt.WhereClause, row)) continue;
                    
                    var resRow = resultBatch.NewRow();
                    for (int i = 0; i < finalColumns.Count; i++)
                        resRow[i] = await context.EvaluateValue(finalColumns[i].Expression, row);
                    
                    await resultBatch.AddRowAsync(resRow);
                    if (resultBatch.Rows.Count >= context.BatchSize)
                    {
                        yield return resultBatch;
                        yielded = true;
                        resultBatch = new DataTable();
                        resultBatch.SetColumns(colNames);
                    }
                }
            }
            if (resultBatch.Rows.Count > 0 || !yielded) yield return resultBatch;
        }

        private async IAsyncEnumerable<DataTable> ReplayBatches(DataTable? first, IAsyncEnumerator<DataTable> e)
        {
            try
            {
                if (first != null) yield return first;
                while (await e.MoveNextAsync()) yield return e.Current;
            }
            finally { await e.DisposeAsync(); }
        }

        private async Task RegisterCtes(List<CteDefinition> ctes, IExecutionContext context)
        {
            foreach (var cte in ctes)
            {
                if (IsRecursive(cte, out var anchor, out var recursive, out var isDistinct))
                {
                    _logger.Debug("Evaluating RECURSIVE CTE: {CteName} ({UnionType})", cte.Name, isDistinct ? "UNION" : "UNION ALL");
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
