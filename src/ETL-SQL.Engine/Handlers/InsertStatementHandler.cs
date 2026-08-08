using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Handlers;
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
        // Lineage target must be the fully-qualified table name ("hospital.dbo.Patient") so the
        // tracker can chain it to the final SELECT whose source column is recorded as
        // "hospital.dbo.Patient.*". connName is kept as-is for connection lookup and guards.
        string lineageTarget = stmt.TargetTable.FullyQualifiedName;

        if (context.VarContext.TryGetView(connName, out _))
            throw new ExecutionException($"View {connName} is read-only and cannot be used as an INSERT target.");

        if (stmt.TargetTable.ConnectionName == null && stmt.TargetTable.TableName.StartsWith("#") && !context.Connections.ContainsKey(connName))
        {
            context.Connections[connName] = new InMemoryDataSource();
        }

        _logger.Debug("Inserting into {ConnName}", connName);

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

                // Classify the expression so the lineage hover shows the transformation
                // kind (Cast, FunctionCall, etc.) and expression text — matching what the
                // static LineageAnalyzer records for SELECT INTO.
                var kind = LineageAnalyzer.ClassifyExpression(sourceCol.Expression);
                var exprSql = kind != TransformationKind.PassThrough ? sourceCol.Expression.ToSql() : null;
                var fns = LineageAnalyzer.CollectFunctions(sourceCol.Expression);

                context.LineageTracker.Record(
                    lineageTarget,
                    resolvedSources,
                    "INSERT",
                    targetColumn: targetCol,
                    sourceColumns: sourceCols,
                    metadata: inherited,
                    derivedFromDescriptions: derived ?? sourceCol.DerivedFromDescriptions,
                    line: stmt.Line,
                    column: stmt.Column,
                    transformationKind: kind,
                    transformationExpression: exprSql,
                    functionsApplied: fns.Count > 0 ? fns : null);
            }
        }
        else
        {
            context.LineageTracker.Record(lineageTarget, stmt.GetSourceTables(), "INSERT", line: stmt.Line, column: stmt.Column);
        }

        var destination = await context.ResolveDataSourceAsync(stmt.TargetTable);
        if (destination == null)
            throw new ExecutionException($"Unknown connection: {connName} at Line {stmt.Line}");
        _logger.Debug("Destination resolved as {DestinationType}", destination.GetType().Name);
        await GuardDataQualityEvidenceInsertAsync(stmt, connName, destination, context);

        if (destination is AppendOnlyColumnDataSource && stmt.IsReplace && !context.IsWhatIf)
            destination = await TempTableStorageRouter.EnsureMutableAsync(context, connName, destination, "INSERT OR REPLACE");

        if (destination is InMemoryDataSource memSource)
        {
            memSource.ReplaceOnConflict = stmt.IsReplace;
        }

        if (destination is IDatabaseSource sqlDest && sqlDest.SupportsSqlPushdown)
        {
            if (stmt.SelectQuery != null && stmt.SelectQuery is SelectStatement sel && sel.FromTable != null && (sel.FromTable.ConnectionName ?? sel.FromTable.TableName).Equals(connName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("Strategy: Remote SQL Pushdown (Insert from Select)");
                var compiledSelect = context.CompileQuery(stmt.SelectQuery, sqlDest.Dialect);
                var sql = $"INSERT INTO {context.GetSqlTableName(stmt.TargetTable, sqlDest.Dialect)}\n{compiledSelect.Sql}";
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would execute remote SQL pushdown insert on {connName}:\n{compiledSelect.ToEscapedSql(sqlDest.Dialect)}", ConsoleColor.Yellow);
                }
                else
                {
                    await foreach (var batch in sqlDest.ExecuteRawSql(sql, compiledSelect.Parameters.Values))
                    {
                        if (batch.RowsAffected >= 0) context.Telemetry.RowsProcessed += batch.RowsAffected;
                    }
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
                _logger.Debug("Strategy: Remote SQL Values ({RowCount} rows)", stmt.Values.Count);
                var allParams = new List<object?>();
                var rowStrings = new List<string>();
                foreach (var row in stmt.Values)
                {
                    var placeholders = new List<string>();
                    foreach (var v in row)
                    {
                        var compiled = context.CompileExpression(v, sqlDest.Dialect);
                        if (compiled.Parameters.Count == 0)
                        {
                            // Pure SQL fragment (NULL, constant function, etc.) — no injection risk
                            placeholders.Add(compiled.Sql);
                        }
                        else
                        {
                            // Remap local @p0, @p1 → globally sequential @p{n}
                            // Map local parameter names to their new global names
                            var localToGlobal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var kv in compiled.Parameters)
                            {
                                localToGlobal[kv.Key] = $"@p{allParams.Count}";
                                allParams.Add(kv.Value);
                            }

                            // Single-pass replacement using Regex to avoid collisions
                            var remapped = System.Text.RegularExpressions.Regex.Replace(compiled.Sql, @"@p\d+", m =>
                            {
                                return localToGlobal.TryGetValue(m.Value, out var globalName) ? globalName : m.Value;
                            });
                            placeholders.Add(remapped);
                        }
                    }
                    rowStrings.Add("(" + string.Join(", ", placeholders) + ")");
                }
                var colList = stmt.Columns != null ? "(" + string.Join(", ", stmt.Columns) + ") " : "";
                var sql = $"INSERT INTO {context.GetSqlTableName(stmt.TargetTable, sqlDest.Dialect)} {colList}VALUES {string.Join(", ", rowStrings)}";

                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would insert {stmt.Values.Count} rows into {connName} using parameterized SQL.", ConsoleColor.Yellow);
                }
                else
                {
                    await foreach (var batch in sqlDest.ExecuteRawSql(sql, allParams.Count > 0 ? allParams : null))
                    {
                        if (batch.RowsAffected >= 0) context.Telemetry.RowsProcessed += batch.RowsAffected;
                    }
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

            var targetCols = stmt.Columns ?? (forClause == null ? (await destination.GetColumnsAsync(context.CancellationToken)).ToList() : stmt.Columns ?? new List<string>());
            if (targetCols.Count > 0)
            {
                batches = context.AlignColumns(batches, targetCols);
            }

            var boundBatches = context.InterceptProgress(batches);
            int count = 0;
            var allInsertedRows = new List<Row>();
            async IAsyncEnumerable<DataTable> CountBatches(IAsyncEnumerable<DataTable> source)
            {
                await foreach (var batch in source)
                {
                    count += batch.Rows.Count;
                    if (stmt.Output != null) allInsertedRows.AddRange(batch.Rows);
                    yield return batch;
                }
            }

            if (context.IsWhatIf)
            {
                await foreach (var _ in CountBatches(boundBatches)) { }
            }
            else
            {
                // Security Hardening: Block writing data into script files
                if (!string.IsNullOrEmpty(destination.Path))
                {
                    context.SecurityService.ValidateWriteAccess(destination.Path);
                }

                await destination.WriteBatches(context.Buffer(CountBatches(boundBatches)), append: true);
            }

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would insert {count} rows into {connName} via batch transfer.", ConsoleColor.Yellow);
            }

            if (stmt.Output != null && allInsertedRows.Count > 0)
            {
                await ProcessOutputClause(stmt.Output, allInsertedRows, context);
            }

            if (context.IsVerbose) _logger.WriteLine($"Finished inserting {count} rows into {connName}");
        }
        else if (stmt.Values != null)
        {
            var destinationCols = (await destination.GetColumnsAsync(context.CancellationToken)).ToList();
            if (destinationCols.Count == 0 && stmt.Values.Count > 0)
            {
                if (stmt.Columns != null) destinationCols.AddRange(stmt.Columns);
                else
                {
                    for (int i = 0; i < stmt.Values[0].Count; i++) destinationCols.Add($"Col{i + 1}");
                }
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
                await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable(), append: true);
            }
            context.IncrementOperationCount(OperationType.EngineInternal, count: stmt.Values.Count);
            context.Telemetry.RowsProcessed += stmt.Values.Count;

            if (stmt.Output != null && batch.Rows.Count > 0)
            {
                await ProcessOutputClause(stmt.Output, batch.Rows, context);
            }

            if (context.IsVerbose) _logger.WriteLine($"Finished inserting {stmt.Values.Count} rows into {connName}");
        }
    }

    /// <summary>
    /// Rejects an INSERT that writes engine-owned <c>__dq_*</c> evidence columns.
    /// Quarantine and warn rows are written by the engine's capture path, not by scripts; a
    /// hand-authored row carrying <c>__dq_status = 'released'</c> would be picked up by
    /// <c>REPLAY QUARANTINE</c> and injected into the production target as if it had been
    /// validated and remediated.
    /// </summary>
    private static async Task GuardDataQualityEvidenceInsertAsync(
        InsertStatement stmt,
        string connName,
        IDataSource destination,
        IExecutionContext context)
    {
        var columns = stmt.Columns
            ?? (await destination.GetColumnsAsync(context.CancellationToken)).ToList();

        foreach (var column in columns)
        {
            if (!ETL_SQL.Core.Quality.DataQualityColumns.IsDataQualityColumn(column)) continue;
            throw new ExecutionException(
                $"Cannot INSERT into data-quality evidence column '{column}' on '{connName}'. "
                + "These columns are written by the engine's quarantine capture; a hand-authored "
                + "row would be replayed into the target as if it had been validated.");
        }
    }

    /// <summary>Processes the OUTPUT clause of an INSERT statement, evaluating expressions against inserted rows.</summary>
    private async Task ProcessOutputClause(OutputClause output, List<Row> insertedRows, IExecutionContext context)
    {
        if (output != null)
        {
            await OutputClauseHelper.ProcessAsync(output, context, insertedRows.Select(r => ((Row?)null, (Row?)r, (string?)null)));
        }
    }
}

