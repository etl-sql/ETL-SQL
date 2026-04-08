using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
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
        public Type SupportedStatementType => typeof(MergeStatement);

        /// <summary>Executes the MERGE statement, choosing between SQL pushdown or in-memory evaluation.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (MergeStatement)statement;
            

            string targetConnName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            Logger.Verbose($"Merging into {targetConnName}");
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
                Logger.Verbose("Strategy: Remote SQL MERGE Pushdown (MSSQL)");
                var sql = context.CompileQuery(stmt, targetSql.Dialect);
                if (context.IsWhatIf)
                {
                    Logger.WriteLine($"WHAT IF: Would execute remote SQL pushdown merge on {targetConnName}:\n{sql}", ConsoleColor.Yellow);
                }
                else
                {
                    await foreach (var _ in targetSql.ExecuteRawSql(sql)) { }
                }
                return;
            }

            // Strategy 2: In-Memory / Heterogeneous Merge
            Logger.Verbose("Strategy: In-Memory Engine MERGE");
            await PerformInMemoryMerge(stmt, targetSource, sourceSource, context);
        }

        /// <summary>Performs a row-by-row merge in memory when pushdown is not possible.</summary>
        private async Task PerformInMemoryMerge(MergeStatement stmt, IDataSource target, IDataSource source, IExecutionContext context)
        {
            var sourceRows = new List<Row>();
            await foreach (var batch in source.ReadBatches(context.BatchSize)) sourceRows.AddRange(batch.Rows);

            var targetRows = new List<Row>();
            await foreach (var batch in target.ReadBatches(context.BatchSize)) targetRows.AddRange(batch.Rows);

            int processedCount = 0;
            var matchedTargetRows = new HashSet<Row>();
            var rowsToDelete = new HashSet<Row>();
            var rowsToAdd = new List<Row>();

            var tAlias = stmt.TargetTable.Alias ?? "T";
            var sAlias = stmt.SourceTable.Alias ?? "S";

            // Process Source Rows (MATCHED / NOT MATCHED BY TARGET)
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

                        foreach (var clause in stmt.MatchedClauses)
                        {
                            if (clause.Condition == null || await context.EvaluateCondition(clause.Condition, combinedRow))
                            {
                                if (clause.ActionType == MergeActionType.UPDATE)
                                {
                                    foreach (var a in clause.UpdateAssignments!) 
                                        tRow[a.ColumnName] = await context.EvaluateValue(a.Value, combinedRow);
                                }
                                else if (clause.ActionType == MergeActionType.DELETE)
                                {
                                    rowsToDelete.Add(tRow);
                                }
                                processedCount++;
                                break;
                            }
                        }
                    }
                }

                if (!rowMatched)
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
                            processedCount++;
                            break;
                        }
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
                                rowsToDelete.Add(tRow);
                            }
                            else if (clause.ActionType == MergeActionType.UPDATE)
                            {
                                foreach (var a in clause.UpdateAssignments!) 
                                    tRow[a.ColumnName] = await context.EvaluateValue(a.Value, tEvalRow);
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
                Logger.WriteLine($"WHAT IF: Would perform in-memory merge on {target.GetType().Name}. Actions: {processedCount}.", ConsoleColor.Yellow);
            }
            else
            {
                foreach (var r in rowsToDelete) targetRows.Remove(r);
                targetRows.AddRange(rowsToAdd);

                if (target is InMemoryDataSource mem)
                {
                    mem.Restore(new List<DataTable> { CreateDataTable(targetRows, await target.GetColumnsAsync()) });
                }
                else
                {
                    Logger.Verbose($"Finalizing MERGE by overwriting {target.GetType().Name}");
                    var finalBatch = CreateDataTable(targetRows, await target.GetColumnsAsync());
                    await target.WriteBatches(new[] { finalBatch }.ToAsyncEnumerable());
                }
            }

            context.RowsProcessed = processedCount;
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
        private DataTable CreateDataTable(List<Row> rows, IEnumerable<string> columns)
        {
            var dt = new DataTable();
            dt.SetColumns(columns);
            foreach (var r in rows) dt.AddRow(r);
            return dt;
        }
    }
}
