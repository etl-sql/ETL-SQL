using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Storage;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Handles the resolution of table references to physical or virtual data sources.
/// Manages temporary tables, subqueries, and table-level operators (PIVOT/UNPIVOT).
/// </summary>
public class DataSourceManager(ILogger logger, Evaluator evaluator, ExpressionEvaluator expressionEvaluator)
{
    private readonly ILogger _logger = logger;
    private readonly Evaluator _evaluator = evaluator;
    private readonly ExpressionEvaluator _expressionEvaluator = expressionEvaluator;
    private readonly Stack<string> _viewResolutionStack = new();

    /// <summary>
    /// Scans all active connections for #temp tables and prepares them for session persistence.
    /// Flushes all in-memory batches to the spill store before returning metadata.
    /// </summary>
    public async Task<IEnumerable<ETL_SQL.Core.Execution.SavedTempTable>> GetTempTablesToSave()
    {
        var result = new List<ETL_SQL.Core.Execution.SavedTempTable>();
        foreach (var kvp in _evaluator.Connections)
        {
            if (kvp.Value is InMemoryDataSource mem && kvp.Key.StartsWith("#"))
            {
                await mem.FlushToSpillAsync();
                var chunks = mem.GetSpillChunks().ToList();
                result.Add(new ETL_SQL.Core.Execution.SavedTempTable(kvp.Key, mem.Schema.Values.ToList(), chunks));
            }
        }
        return result;
    }

    /// <summary>
    /// Resolves a table reference to a functional IDataSource.
    /// Handles subqueries, function calls, dual table, and temporary tables.
    /// </summary>
    public async Task<IDataSource> ResolveDataSourceAsync(TableReference table, IDictionary<string, IDataSource> connections, TransactionManager transactionManager)
    {
        _evaluator.CancellationToken.ThrowIfCancellationRequested();

        if (table.Subquery != null)
        {
            return new StreamingSubqueryDataSource(_evaluator.ExecuteQuery(table.Subquery));
        }

        string name = table.ConnectionName ?? table.TableName;
        IDataSource? source = null;

        if (_evaluator.LocalSources.TryGetValue(name, out source))
        {
            // Found in local CTE scope
        }
        else if (connections.TryGetValue(name, out source))
        {
            // Found in global connections
        }
        else if (table.ConnectionName == null && (table.TableName.StartsWith("#") || table.TableName.StartsWith("&")))
        {
            var mem = new InMemoryDataSource();
            mem.Validator = _evaluator;

            // Configure spill-to-disk protection
            mem.MaxInMemoryBatches = _evaluator.MaxInMemoryBatches;
            mem.ExecutionContext = _evaluator;

            // [STABILIZATION] Always register in global connections to ensure persistence 
            // across statement boundaries within the session.
            connections[name] = mem;
            source = mem;
        }
        else if (table.ValuesRows != null)
        {
            return await BuildValuesDataSourceAsync(table);
        }
        else if (table.FunctionCall != null)
        {
            if (table.FunctionCall.FunctionName.Equals("LINEAGE", StringComparison.OrdinalIgnoreCase))
            {
                string? targetTbl = table.FunctionCall.Arguments.Count > 0 ? (await _expressionEvaluator.EvaluateInternal(table.FunctionCall.Arguments[0], new Row()))?.ToString() : null;
                string? targetCol = table.FunctionCall.Arguments.Count > 1 ? (await _expressionEvaluator.EvaluateInternal(table.FunctionCall.Arguments[1], new Row()))?.ToString() : null;
                return new LineageDataSource(_evaluator.LineageTracker, targetTbl, targetCol);
            }

            if (table.FunctionCall.FunctionName.Equals("JSON_TABLE", StringComparison.OrdinalIgnoreCase) &&
                table.FunctionCall.JsonTable != null)
            {
                return await BuildJsonTableDataSourceAsync(table.FunctionCall);
            }

            var result = await _expressionEvaluator.EvaluateInternal(table.FunctionCall, new Row());
            if (result is DataTable dt)
            {
                var mem = new InMemoryDataSource();
                mem.Validator = _evaluator;
                mem.ExecutionContext = _evaluator;
                mem.MaxInMemoryBatches = _evaluator.MaxInMemoryBatches;

                await mem.WriteBatches(new[] { dt }.ToAsyncEnumerable(), append: false, cancellationToken: _evaluator.CancellationToken);
                return mem;
            }
            else if (result is System.Collections.IEnumerable list && !(result is string))
            {
                var mem = new InMemoryDataSource();
                mem.Validator = _evaluator;
                mem.ExecutionContext = _evaluator;
                mem.MaxInMemoryBatches = _evaluator.MaxInMemoryBatches;

                var dtResult = new DataTable();
                dtResult.SetColumns(new[] { "Value" });
                foreach (var item in list) await dtResult.AddRowAsync(new Row { ["Value"] = item });
                await mem.WriteBatches(new[] { dtResult }.ToAsyncEnumerable(), append: false, cancellationToken: _evaluator.CancellationToken);
                return mem;
            }
            throw new ExecutionException($"Function {table.FunctionCall.FunctionName} did not return a table.");
        }
        else if (table.ConnectionName == null && _evaluator.TryGetView(name, out var view))
        {
            return new StreamingSubqueryDataSource(EvaluateViewBatches(name, view!.Query));
        }
        else if (name.Equals("LINEAGE", StringComparison.OrdinalIgnoreCase))
        {
            return new LineageDataSource(_evaluator.LineageTracker);
        }
        else if (name.Equals("LINEAGE_TAGS", StringComparison.OrdinalIgnoreCase))
        {
            return new LineageTagsDataSource(_evaluator.LineageTracker);
        }
        else if (name.Equals("DUAL", StringComparison.OrdinalIgnoreCase))

        {
            if (!connections.ContainsKey("DUAL"))
            {
                var dual = new InMemoryDataSource();
                dual.Validator = _evaluator;
                dual.ExecutionContext = _evaluator;
                dual.MaxInMemoryBatches = _evaluator.MaxInMemoryBatches;

                var dualTable = new DataTable();
                dualTable.SetColumns(new[] { "DUMMY" });
                await dualTable.AddRowAsync(new Row { ["DUMMY"] = "X" });
                await dual.WriteBatches(new[] { dualTable }.ToAsyncEnumerable(), append: false, cancellationToken: _evaluator.CancellationToken);
                connections["DUAL"] = dual;
            }
            source = connections["DUAL"];
        }
        else if (name.StartsWith("@"))
        {
            var val = _evaluator.GetVariable(name);
            if (val is System.Collections.IEnumerable list && !(val is string))
            {
                var mem = new InMemoryDataSource();
                mem.Validator = _evaluator;
                mem.ExecutionContext = _evaluator;
                mem.MaxInMemoryBatches = _evaluator.MaxInMemoryBatches;

                var dt = new DataTable();
                dt.SetColumns(new[] { "Value" });
                foreach (var item in list) await dt.AddRowAsync(new Row { ["Value"] = item });
                await mem.WriteBatches(new[] { dt }.ToAsyncEnumerable(), append: false, cancellationToken: _evaluator.CancellationToken);
                return mem;
            }

            // If it's used as a target in SELECT INTO, or if it's not a collection,
            // return a VariableDataSource so it can be written to and updated in the session.
            return new VariableDataSource(name, _evaluator);
        }


        if (source == null)
        {
            throw new ExecutionException($"Unknown source: {name} at Line {table.Line}");
        }

        _evaluator.CancellationToken.ThrowIfCancellationRequested();

        if (transactionManager.TranCount > 0) await transactionManager.EnlistDataSource(source, _evaluator.CancellationToken);

        if (table.ConnectionName != null) return source.WithTable(table.TableName);
        return source;
    }

    private async IAsyncEnumerable<DataTable> EvaluateViewBatches(string viewName, Statement query)
    {
        if (_viewResolutionStack.Any(v => v.Equals(viewName, StringComparison.OrdinalIgnoreCase)))
        {
            var chain = string.Join(" -> ", _viewResolutionStack.Reverse().Append(viewName));
            throw new ExecutionException($"Recursive view reference detected: {chain}");
        }

        _viewResolutionStack.Push(viewName);
        try
        {
            await foreach (var batch in _evaluator.ExecuteQuery(query).WithCancellation(_evaluator.CancellationToken))
                yield return batch;
        }
        finally
        {
            _viewResolutionStack.Pop();
        }
    }

    private async Task<IDataSource> BuildValuesDataSourceAsync(TableReference table)
    {
        _evaluator.CancellationToken.ThrowIfCancellationRequested();

        var rows = table.ValuesRows ?? new List<List<Expression>>();
        if (rows.Count == 0)
        {
            throw new ExecutionException("VALUES table constructor requires at least one row.");
        }

        int columnCount = rows[0].Count;
        if (columnCount == 0)
        {
            throw new ExecutionException("VALUES table constructor rows must contain at least one expression.");
        }

        foreach (var row in rows)
        {
            if (row.Count != columnCount)
            {
                throw new ExecutionException("All VALUES table constructor rows must have the same number of expressions.");
            }
        }

        var columnNames = table.ColumnAliases != null && table.ColumnAliases.Count > 0
            ? table.ColumnAliases
            : Enumerable.Range(1, columnCount).Select(i => $"column{i}").ToList();

        if (columnNames.Count != columnCount)
        {
            throw new ExecutionException("VALUES table constructor column alias count must match the row value count.");
        }

        var result = new DataTable();
        result.SetColumns(columnNames);
        foreach (var valueRow in rows)
        {
            var dataRow = new Row(result.Schema);
            for (int i = 0; i < valueRow.Count; i++)
            {
                _evaluator.CancellationToken.ThrowIfCancellationRequested();
                dataRow[columnNames[i]] = await _expressionEvaluator.EvaluateInternal(valueRow[i], new Row());
            }
            await result.AddRowAsync(dataRow);
        }

        var mem = new InMemoryDataSource();
        mem.Validator = _evaluator;
        mem.ExecutionContext = _evaluator;
        mem.MaxInMemoryBatches = _evaluator.MaxInMemoryBatches;
        await mem.WriteBatches(new[] { result }.ToAsyncEnumerable(), append: false, cancellationToken: _evaluator.CancellationToken);
        return mem;
    }

    private async Task<IDataSource> BuildJsonTableDataSourceAsync(FunctionCallExpression functionCall)
    {
        _evaluator.CancellationToken.ThrowIfCancellationRequested();

        string? json = functionCall.Arguments.Count > 0
            ? (await _expressionEvaluator.EvaluateInternal(functionCall.Arguments[0], new Row()))?.ToString()
            : null;
        string? rowPath = functionCall.Arguments.Count > 1
            ? (await _expressionEvaluator.EvaluateInternal(functionCall.Arguments[1], new Row()))?.ToString()
            : "$";

        var columns = new List<JsonFunctions.JsonTableColumn>();
        foreach (var column in functionCall.JsonTable?.Columns ?? new List<JsonTableColumnSpec>())
        {
            string? path = column.Path != null
                ? (await _expressionEvaluator.EvaluateInternal(column.Path, new Row()))?.ToString()
                : null;
            object? defaultOnEmpty = column.DefaultOnEmpty != null
                ? await _expressionEvaluator.EvaluateInternal(column.DefaultOnEmpty, new Row())
                : null;
            object? defaultOnError = column.DefaultOnError != null
                ? await _expressionEvaluator.EvaluateInternal(column.DefaultOnError, new Row())
                : null;

            columns.Add(new JsonFunctions.JsonTableColumn(
                column.Name,
                column.TypeName,
                path,
                column.ForOrdinality,
                column.Exists,
                defaultOnEmpty,
                defaultOnError));
        }

        var dt = await JsonFunctions.BuildJsonTableWithColumns(json, rowPath, columns);
        var mem = new InMemoryDataSource();
        mem.Validator = _evaluator;
        mem.ExecutionContext = _evaluator;
        mem.MaxInMemoryBatches = _evaluator.MaxInMemoryBatches;
        await mem.WriteBatches(new[] { dt }.ToAsyncEnumerable(), append: false, cancellationToken: _evaluator.CancellationToken);
        return mem;
    }

    /// <summary>Restores a temporary table from a session data store (SQLite/Chunks or Legacy JSON).</summary>
    public async Task<IDataSource> RestoreTempTable(TempTableInfo info, string password)
    {
        _evaluator.CancellationToken.ThrowIfCancellationRequested();

        var ds = new InMemoryDataSource();
        ds.Validator = _evaluator;
        ds.ExecutionContext = _evaluator;

        // Restore schema (full ColumnDefinitions and TableConstraints)
        if (info.Columns != null && info.Columns.Count > 0)
        {
            ds.SetSchema(info.Columns, MapToAst(info.Constraints));
        }

        // High-Performance Path: Restore from Persistent Spill Chunks
        if (info.SpillChunkNames != null && info.SpillChunkNames.Count > 0)
        {
            ds.Rehydrate(info.Columns ?? new(), info.SpillChunkNames);
            _logger.Debug("[SESSION] Rehydrated temp table {TableName} from {ChunkCount} spill chunks.", info.Name, info.SpillChunkNames.Count);
            return ds;
        }

        // Legacy/Snapshot Path: Restore from JSON file
        if (File.Exists(info.DataFilePath))
        {
            try
            {
                string encryptedJson = await File.ReadAllTextAsync(info.DataFilePath, _evaluator.CancellationToken);
                string plainJson = CryptoUtils.Unprotect(encryptedJson, password);
                var rows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(plainJson);

                if (rows != null && rows.Count > 0)
                {
                    var dt = new DataTable();
                    dt.SetColumns(ds.Schema.Keys, info.Constraints);

                    foreach (var rowDict in rows)
                    {
                        _evaluator.CancellationToken.ThrowIfCancellationRequested();
                        var row = dt.NewRow();
                        foreach (var kvp in rowDict)
                        {
                            row[kvp.Key] = JsonToClr(kvp.Value);
                        }
                        await dt.AddRowAsync(row);
                    }
                    await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable(), append: false, cancellationToken: _evaluator.CancellationToken);
                    _logger.Debug("[SESSION] Restored {RowCount} rows into temp table {TableName} via JSON snapshot", rows.Count, info.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"Warning: Failed to restore temp table {info.Name}: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        return ds;
    }

    private List<TableConstraint> MapToAst(IEnumerable<TableConstraintInfo> constraints)
    {
        var result = new List<TableConstraint>();
        foreach (var info in constraints)
        {
            TableConstraint? tc = null;
            switch (info.Type)
            {
                case ConstraintType.PrimaryKey:
                    tc = new TablePrimaryKeyConstraint(info.Columns);
                    break;
                case ConstraintType.Unique:
                    tc = new TableUniqueConstraint(info.Columns);
                    break;
                case ConstraintType.Check:
                    tc = new TableCheckConstraint(info.Expression!);
                    break;
                case ConstraintType.ForeignKey:
                    tc = new TableForeignKeyConstraint(info.Columns, info.ForeignKey!);
                    break;
            }
            if (tc != null)
            {
                tc.ConstraintName = info.Name;
                result.Add(tc);
            }
        }
        return result;
    }

    private Expression ParseExpression(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        var parser = new Parser(tokens, sql);
        return parser.ParseExpression();
    }

    private ForeignKeyReference ParseForeignKeyReference(string sql)
    {
        // The info.Expression for FK was Reference.ToSql() -> "REFERENCES Table(Col)"
        var fkSql = sql.StartsWith("REFERENCES ", StringComparison.OrdinalIgnoreCase) ? sql.Substring(11) : sql;
        var tokens = new Lexer(fkSql).Tokenize();
        var parser = new Parser(tokens, fkSql);
        var stmtParser = new StatementParser(parser);
        return stmtParser.ParseForeignKeyReference();
    }

    private object? JsonToClr(object? val)
    {
        if (val is JsonElement elem)
        {
            switch (elem.ValueKind)
            {
                case JsonValueKind.String: return elem.GetString();
                case JsonValueKind.Number:
                    if (elem.TryGetInt32(out int i)) return i;
                    if (elem.TryGetInt64(out long l)) return l;
                    return elem.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return null;
                default: return elem.ToString();
            }
        }
        return val;
    }

    /// <summary>
    /// Reads batches from a data source and applies high-level operators like PIVOT or UNPIVOT.
    /// </summary>
    public async IAsyncEnumerable<DataTable> ResolveAndApplyOperators(TableReference table, IDictionary<string, IDataSource> connections, TransactionManager transactionManager, int batchSize)
    {
        var source = await ResolveDataSourceAsync(table, connections, transactionManager);
        var batches = source.ReadBatches(batchSize, _evaluator.CancellationToken);

        if (table.TableOperators.Count == 0)
        {
            await foreach (var b in batches) yield return b;
            yield break;
        }

        if (table.TableOperators.Count == 1 && table.TableOperators[0] is UnpivotClause streamingUnpivot)
        {
            string streamingTableName = table.Alias ?? table.TableName;
            var streamingPivotEngine = new Engines.PivotEngine(_evaluator, _logger);
            await foreach (var b in BatchRows(
                streamingPivotEngine.ApplyUnpivotStream(PrefixRows(batches, streamingTableName), streamingUnpivot),
                batchSize))
                yield return b;
            yield break;
        }

        if (table.TableOperators.Count == 1 && table.TableOperators[0] is PivotClause streamingPivot)
        {
            string streamingTableName = table.Alias ?? table.TableName;
            var streamingPivotEngine = new Engines.PivotEngine(_evaluator, _logger);
            await foreach (var b in BatchRows(
                streamingPivotEngine.ApplyPivotStream(PrefixRows(batches, streamingTableName), streamingPivot),
                batchSize))
                yield return b;
            yield break;
        }

        // Chained table operators retain the compatibility path because each stage may change the next stage's schema.
        var allRows = new List<Row>();
        string tableName = table.Alias ?? table.TableName;
        var containsMatchRecognize = table.TableOperators.Any(op => op is MatchRecognizeClause);
        var warnedMatchRecognizeBuffering = false;
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
                if (containsMatchRecognize
                    && !warnedMatchRecognizeBuffering
                    && allRows.Count > _evaluator.JoinSpillThreshold)
                {
                    warnedMatchRecognizeBuffering = true;
                    _logger.Warning($"MATCH_RECOGNIZE input exceeded {_evaluator.JoinSpillThreshold:N0} rows and requires in-memory partition matching. Pre-filter the source or increase available memory.");
                }
            }
        }

        var pivotEngine = new Engines.PivotEngine(_evaluator, _logger);
        var matchRecognizeEngine = new Engines.MatchRecognizeEngine(_evaluator);
        foreach (var op in table.TableOperators)
        {
            if (op is PivotClause pivot) allRows = await pivotEngine.ApplyPivot(allRows, pivot);
            else if (op is DuckPivotClause duckPivot) allRows = await pivotEngine.ApplyDuckPivot(allRows, duckPivot);
            else if (op is UnpivotClause unpivot) allRows = await pivotEngine.ApplyUnpivot(allRows, unpivot);
            else if (op is MatchRecognizeClause matchRecognize) allRows = await matchRecognizeEngine.Apply(allRows, matchRecognize);
        }

        var resultTable = new DataTable();
        if (allRows.Count > 0) resultTable.SetColumns(allRows[0].Columns.Keys);
        foreach (var r in allRows) await resultTable.AddRowAsync(r);

        yield return resultTable;
    }

    private static async IAsyncEnumerable<Row> PrefixRows(IAsyncEnumerable<DataTable> batches, string tableName)
    {
        await foreach (var batch in batches)
        {
            foreach (var row in batch.Rows)
            {
                var r = row.Clone();
                foreach (var kv in row.Columns.ToList())
                {
                    if (!kv.Key.Contains("."))
                        r[$"{tableName}.{kv.Key}"] = kv.Value;
                }
                yield return r;
            }
        }
    }

    private static async IAsyncEnumerable<DataTable> BatchRows(IAsyncEnumerable<Row> rows, int batchSize)
    {
        DataTable? batch = null;
        bool yielded = false;
        await foreach (var row in rows)
        {
            if (batch == null)
            {
                batch = new DataTable();
                batch.SetColumns(row.Columns.Keys);
            }

            await batch.AddRowAsync(row);
            if (batch.Rows.Count >= batchSize)
            {
                yield return batch;
                yielded = true;
                batch = null;
            }
        }

        if (batch != null)
        {
            yield return batch;
            yielded = true;
        }
        else if (!yielded)
        {
            yield return new DataTable();
        }
    }
}
