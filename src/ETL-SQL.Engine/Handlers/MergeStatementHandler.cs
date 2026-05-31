using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the execution of MERGE statements, supporting complex matching logic (MATCHED, NOT MATCHED BY TARGET, NOT MATCHED BY SOURCE).
    /// Supports remote SQL pushdown for MSSQL and in-memory heterogeneous merges.
    /// </summary>
    public class MergeStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(MergeStatement);
 
        public MergeStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the MERGE statement, choosing between SQL pushdown or in-memory evaluation.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (MergeStatement)statement;
            

            string targetConnName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            _logger.Debug("Merging into {TargetConnName}", targetConnName);
            if (context.VarContext.TryGetView(targetConnName, out _))
                throw new ExecutionException($"View {targetConnName} is read-only and cannot be used as a MERGE target.");
            context.LineageTracker.Record(targetConnName, stmt.GetSourceTables(), "MERGE", line: stmt.Line, column: stmt.Column);

            var targetSource = await context.ResolveDataSourceAsync(stmt.TargetTable);
            var sourceSource = await context.ResolveDataSourceAsync(stmt.SourceTable);

            if (targetSource == null) throw new ExecutionException($"Unknown target connection: {targetConnName}");
            if (sourceSource == null) throw new ExecutionException($"Unknown source connection: {stmt.SourceTable.ToSql()}");

            // Record column-level lineage for MERGE actions
            var sTable = stmt.SourceTable.Alias ?? stmt.SourceTable.TableName;
            foreach (var clause in stmt.MatchedClauses)
            {
                if (clause.ActionType == MergeActionType.UPDATE)
                {
                    foreach (var a in clause.UpdateAssignments!)
                    {
                        var srcTables = a.Value.GetSourceTables().Select(s => s.Equals("S", StringComparison.OrdinalIgnoreCase) || s.Equals(stmt.SourceTable.Alias, StringComparison.OrdinalIgnoreCase) ? sTable : s).ToList();
                        var srcCols = a.Value.GetSourceColumns();
                        var inherited = context.LineageTracker.InheritMetadata(srcTables, srcCols, out var derived);

                        context.LineageTracker.Record(
                            targetConnName, 
                            srcTables, 
                            "MERGE UPDATE", 
                            targetColumn: a.ColumnName, 
                            sourceColumns: srcCols,
                            metadata: inherited,
                            derivedFromDescriptions: derived,
                            line: a.Line,
                            column: a.Column);
                    }
                }
            }
            foreach (var clause in stmt.NotMatchedClauses.Where(c => c.Option == MergeSourceOrTarget.Target))
            {
                if (clause.ActionType == MergeActionType.INSERT)
                {
                    var targetCols = (await targetSource.GetColumnsAsync()).ToList();
                    var colNames = (clause.InsertColumns != null && clause.InsertColumns.Count > 0) ? clause.InsertColumns : targetCols;
                    for (int i = 0; i < colNames.Count && i < clause.InsertValues!.Count; i++)
                    {
                        var val = clause.InsertValues[i];
                        var srcTables = val.GetSourceTables().Select(s => s.Equals("S", StringComparison.OrdinalIgnoreCase) || s.Equals(stmt.SourceTable.Alias, StringComparison.OrdinalIgnoreCase) ? sTable : s).ToList();
                        var srcCols = val.GetSourceColumns();
                        var inherited = context.LineageTracker.InheritMetadata(srcTables, srcCols, out var derived);

                        context.LineageTracker.Record(
                            targetConnName, 
                            srcTables, 
                            "MERGE INSERT", 
                            targetColumn: colNames[i], 
                            sourceColumns: srcCols,
                            metadata: inherited,
                            derivedFromDescriptions: derived,
                            line: val.Line,
                            column: val.Column);
                    }
                }
            }

            // Strategy 1: SQL Pushdown (Same connection, MSSQL)
            if (targetSource is IDatabaseSource targetSql && sourceSource is IDatabaseSource sourceSql && 
                targetSql.Dialect == "MSSQL" && targetSql.Dialect == sourceSql.Dialect && 
                string.Equals(targetSql.Path, sourceSql.Path, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("Strategy: Remote SQL MERGE Pushdown (MSSQL)");
                var compiled = context.CompileQuery(stmt, targetSql.Dialect);
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would execute remote SQL pushdown merge on {targetConnName}:\n{compiled.ToEscapedSql(targetSql.Dialect)}", ConsoleColor.Yellow);
                }
                else
                {
                    await foreach (var batch in targetSql.ExecuteRawSql(compiled.Sql, compiled.Parameters.Values)) 
                    {
                        if (batch.RowsAffected >= 0) context.Telemetry.RowsProcessed += batch.RowsAffected;
                    }
                }
                return;
            }

            // Strategy 2: In-Memory / Heterogeneous Merge
            _logger.Debug("Strategy: In-Memory Engine MERGE");
            await PerformInMemoryMerge(stmt, targetSource, sourceSource, context);
        }

        /// <summary>Performs a row-by-row merge in memory when pushdown is not possible.</summary>
        /// <remarks>Optimized with O(S+T) hash-join for equality conditions.</remarks>
        private async Task PerformInMemoryMerge(MergeStatement stmt, IDataSource target, IDataSource source, IExecutionContext context)
        {
            var sourceRows = new List<Row>();
            await foreach (var batch in source.ReadBatches(context.BatchSize)) sourceRows.AddRange(batch.Rows);

            var targetRows = new List<Row>();
            await foreach (var batch in target.ReadBatches(context.BatchSize)) targetRows.AddRange(batch.Rows);
            if (context.IsWhatIf)
            {
                targetRows = targetRows.Select(row => row.Clone()).ToList();
            }

            var tAlias = stmt.TargetTable.Alias ?? "T";
            var sAlias = stmt.SourceTable.Alias ?? "S";

            // Optimization: Detect equality conditions for O(S+T) join
            var (isEquality, targetCols, sourceCols) = TryExtractEqualityJoin(stmt.OnCondition, tAlias, sAlias);
            
            int processedCount = 0;
            var matchedTargetRows = new HashSet<Row>();
            var rowsToDelete = new HashSet<Row>();
            var rowsToAdd = new List<Row>();
            var outputRows = new List<MergeOutputRow>();

            if (isEquality)
            {
                _logger.Debug("Optimizing merge with O(S+T) Hash Join on columns: [{Columns}]", string.Join(", ", targetCols!));
                var targetIndex = new Dictionary<CompositeKey, List<Row>>();
                foreach(var tr in targetRows)
                {
                    var key = new CompositeKey(targetCols!.Select(c => tr[c]).ToArray());
                    if (!targetIndex.TryGetValue(key, out var list)) targetIndex[key] = list = new List<Row>();
                    list.Add(tr);
                }

                foreach (var sRow in sourceRows)
                {
                    var sKey = new CompositeKey(sourceCols!.Select(c => sRow[c]).ToArray());
                    if (targetIndex.TryGetValue(sKey, out var tMatches))
                    {
                        foreach (var tRow in tMatches)
                        {
                            var combinedRow = CreateEvalRow(sRow, sAlias, tRow, tAlias);
                            if (await context.EvaluateCondition(stmt.OnCondition, combinedRow))
                            {
                                matchedTargetRows.Add(tRow);
                                await HandleMatched(stmt, combinedRow, tRow, rowsToDelete, outputRows, context, () => processedCount++);
                            }
                        }
                    }
                    else
                    {
                        await HandleNotMatched(stmt, sRow, sAlias, target, rowsToAdd, outputRows, context, () => processedCount++);
                    }
                }
            }
            else
            {
                // Fallback: O(S*T) nested loop join
                foreach (var sRow in sourceRows)
                {
                    bool rowMatched = false;
                    foreach (var tRow in targetRows)
                    {
                        var combinedRow = CreateEvalRow(sRow, sAlias, tRow, tAlias);
                        if (await context.EvaluateCondition(stmt.OnCondition, combinedRow))
                        {
                            rowMatched = true;
                            matchedTargetRows.Add(tRow);
                            await HandleMatched(stmt, combinedRow, tRow, rowsToDelete, outputRows, context, () => processedCount++);
                        }
                    }
                    if (!rowMatched)
                    {
                        await HandleNotMatched(stmt, sRow, sAlias, target, rowsToAdd, outputRows, context, () => processedCount++);
                    }
                }
            }

            // Process Target Rows (NOT MATCHED BY SOURCE)
            foreach (var tRow in targetRows)
            {
                if (!matchedTargetRows.Contains(tRow))
                {
                    var tEvalRow = CreateEvalRow(null, sAlias, tRow, tAlias);
                    foreach (var clause in stmt.NotMatchedClauses.Where(c => c.Option == MergeSourceOrTarget.Source))
                    {
                        if (clause.Condition == null || await context.EvaluateCondition(clause.Condition, tEvalRow))
                        {
                            if (clause.ActionType == MergeActionType.DELETE)
                            {
                                var oldRow = tRow.Clone();
                                rowsToDelete.Add(tRow);
                                outputRows.Add(new MergeOutputRow(null, oldRow, "DELETE"));
                            }
                            else if (clause.ActionType == MergeActionType.UPDATE)
                            {
                                var oldRow = tRow.Clone();
                                foreach (var a in clause.UpdateAssignments!) 
                                    tRow[a.ColumnName] = await context.EvaluateValue(a.Value, tEvalRow);
                                outputRows.Add(new MergeOutputRow(tRow.Clone(), oldRow, "UPDATE"));
                            }
                            processedCount++;
                            break;
                        }
                    }
                }
            }

            // Apply Mutations
            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would perform in-memory merge on {target.GetType().Name}. Actions: {processedCount}.", ConsoleColor.Yellow);
            }
            else
            {
                foreach (var r in rowsToDelete) targetRows.Remove(r);
                targetRows.AddRange(rowsToAdd);

                if (target is InMemoryDataSource mem)
                {
                    mem.Restore(new List<DataTable> { await CreateDataTable(targetRows, await target.GetColumnsAsync()) });
                }
                else
                {
                    _logger.Debug("Finalizing MERGE by overwriting {TargetType}", target.GetType().Name);
                    var finalBatch = await CreateDataTable(targetRows, await target.GetColumnsAsync());
                    await target.WriteBatches(new[] { finalBatch }.ToAsyncEnumerable());
                }
            }

            context.Telemetry.RowsProcessed += processedCount;

            if (!context.IsWhatIf && stmt.Output != null && outputRows.Count > 0)
            {
                await OutputClauseHelper.ProcessAsync(stmt.Output, context, outputRows.Select(r => (r.Deleted, r.Inserted, (string?)r.Action)));
            }
        }

        private async Task HandleMatched(MergeStatement stmt, Row combinedRow, Row tRow, HashSet<Row> rowsToDelete, List<MergeOutputRow> outputRows, IExecutionContext context, Action onAction)
        {
            foreach (var clause in stmt.MatchedClauses)
            {
                if (clause.Condition == null || await context.EvaluateCondition(clause.Condition, combinedRow))
                {
                    if (clause.ActionType == MergeActionType.UPDATE)
                    {
                        var oldRow = tRow.Clone();
                        foreach (var a in clause.UpdateAssignments!)
                            tRow[a.ColumnName] = await context.EvaluateValue(a.Value, combinedRow);
                        
                        outputRows.Add(new MergeOutputRow(tRow.Clone(), oldRow, "UPDATE"));
                    }
                    else if (clause.ActionType == MergeActionType.DELETE)
                    {
                        var oldRow = tRow.Clone();
                        rowsToDelete.Add(tRow);
                        outputRows.Add(new MergeOutputRow(null, oldRow, "DELETE"));
                    }
                    onAction();
                    break;
                }
            }
        }

        private async Task HandleNotMatched(MergeStatement stmt, Row sRow, string sAlias, IDataSource target, List<Row> rowsToAdd, List<MergeOutputRow> outputRows, IExecutionContext context, Action onAction)
        {
            var sEvalRow = CreateEvalRow(sRow, sAlias);
            foreach (var clause in stmt.NotMatchedClauses.Where(c => c.Option == MergeSourceOrTarget.Target))
            {
                if (clause.Condition == null || await context.EvaluateCondition(clause.Condition, sEvalRow))
                {
                    var newRow = new Row();
                    var targetCols = (await target.GetColumnsAsync()).ToList();
                    var colNames = (clause.InsertColumns != null && clause.InsertColumns.Count > 0) ? clause.InsertColumns : targetCols;
                    for (int i = 0; i < colNames.Count && i < clause.InsertValues!.Count; i++)
                        newRow[colNames[i]] = await context.EvaluateValue(clause.InsertValues[i], sEvalRow);
                    
                    rowsToAdd.Add(newRow);
                    outputRows.Add(new MergeOutputRow(newRow.Clone(), null, "INSERT"));
                    onAction();
                    break;
                }
            }
        }


        private record MergeOutputRow(Row? Inserted, Row? Deleted, string Action);

        private (bool isEquality, List<string>? targetCols, List<string>? sourceCols) TryExtractEqualityJoin(Expression onCondition, string tAlias, string sAlias)
        {
            var targetCols = new List<string>();
            var sourceCols = new List<string>();

            // Flatten AND conditions
            var parts = new List<Expression>();
            void Flatten(Expression e)
            {
                if (e is BinaryExpression b && b.Operator == TokenType.AND) { Flatten(b.Left); Flatten(b.Right); }
                else parts.Add(e);
            }
            Flatten(onCondition);

            foreach (var part in parts)
            {
                if (part is BinaryExpression b && b.Operator == TokenType.EQUALS)
                {
                    if (b.Left is IdentifierExpression idL && b.Right is IdentifierExpression idR)
                    {
                        var (isTL, colL) = AnalyzeIdentifier(idL, tAlias, sAlias);
                        var (isTR, colR) = AnalyzeIdentifier(idR, tAlias, sAlias);

                        if (isTL && !isTR && colL != null && colR != null) { targetCols.Add(colL); sourceCols.Add(colR); }
                        else if (!isTL && isTR && colL != null && colR != null) { targetCols.Add(colR); sourceCols.Add(colL); }
                        else return (false, null, null);
                    }
                    else return (false, null, null);
                }
                else return (false, null, null);
            }

            return targetCols.Count > 0 ? (true, targetCols, sourceCols) : (false, null, null);
        }

        private (bool isTarget, string? column) AnalyzeIdentifier(IdentifierExpression id, string tAlias, string sAlias)
        {
            var parts = id.Name.Split('.');
            if (parts.Length == 1) return (false, parts[0]); // Unqualified assumed source or ambiguous
            if (parts[0].Equals(tAlias, StringComparison.OrdinalIgnoreCase)) return (true, parts[1]);
            if (parts[0].Equals(sAlias, StringComparison.OrdinalIgnoreCase)) return (false, parts[1]);
            return (false, null);
        }

        /// <summary>Creates a composite row for evaluating merge conditions, with source and target aliases.</summary>
        private Row CreateEvalRow(Row? sRow, string sAlias, Row? tRow = null, string? tAlias = null)
        {
            var eval = new Row();
            if (sRow != null)
            {
                foreach (var kv in sRow.Columns)
                {
                    eval[kv.Key] = kv.Value;
                    eval[$"{sAlias}.{kv.Key}"] = kv.Value;
                }
            }
            if (tRow != null && tAlias != null)
            {
                foreach (var kv in tRow.Columns)
                {
                    // Target overwrites source for unqualified names if conflict, 
                    // but qualified names stay distinct.
                    eval[kv.Key] = kv.Value;
                    eval[$"{tAlias}.{kv.Key}"] = kv.Value;
                }
            }
            return eval;
        }

        /// <summary>Helper to create a DataTable from a list of rows and column names.</summary>
        private async Task<DataTable> CreateDataTable(List<Row> rows, IEnumerable<string> columns)
        {
            var dt = new DataTable();
            dt.SetColumns(columns);
            foreach (var r in rows) await dt.AddRowAsync(r);
            return dt;
        }
    }
}

