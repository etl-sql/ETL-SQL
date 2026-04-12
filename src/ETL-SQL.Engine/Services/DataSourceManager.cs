using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Storage;
using System.Text.Json;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Handles the resolution of table references to physical or virtual data sources.
    /// Manages temporary tables, subqueries, and table-level operators (PIVOT/UNPIVOT).
    /// </summary>
    public class DataSourceManager(ILogger logger, Evaluator evaluator, ExpressionEvaluator expressionEvaluator)
    {
        private readonly ILogger _logger = logger;
        private readonly Evaluator _evaluator = evaluator;
        private readonly ExpressionEvaluator _expressionEvaluator = expressionEvaluator;

        /// <summary>
        /// Resolves a table reference to a functional IDataSource.
        /// Handles subqueries, function calls, dual table, and temporary tables.
        /// </summary>
        public async Task<IDataSource> ResolveDataSourceAsync(TableReference table, IDictionary<string, IDataSource> connections, TransactionManager transactionManager)
        {
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
                    await dualTable.AddRowAsync(new Row { ["DUMMY"] = "X" });
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
                foreach (var item in list) await dt.AddRowAsync(new Row { ["Val"] = item });
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

        /// <summary>Restores a temporary table from a session data file.</summary>
        public async Task<IDataSource> RestoreTempTable(TempTableInfo info, string password)
        {
            var ds = new InMemoryDataSource();
            ds.Validator = _evaluator;
            
            // Restore schema (full ColumnDefinitions and TableConstraints)
            if (info.Columns != null && info.Columns.Count > 0)
            {
                ds.SetSchema(info.Columns, MapToAst(info.Constraints));
            }
            else if (info.Columns != null && info.Columns.Count == 0) // Fallback for names if definitions are missing
            {
                // This shouldn't happen with the new logic, but handled for safety
            }
            
            if (File.Exists(info.DataFilePath))
            {
                try
                {
                    string encryptedJson = await File.ReadAllTextAsync(info.DataFilePath);
                    string plainJson = CryptoUtils.Unprotect(encryptedJson, password);
                    var rows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(plainJson);
                    
                    if (rows != null && rows.Count > 0)
                    {
                        var dt = new DataTable(); 
                        dt.SetColumns(ds.Schema.Keys, info.Constraints); 

                        foreach (var rowDict in rows)
                        {
                            var row = dt.NewRow();
                            foreach (var kvp in rowDict)
                            {
                                row[kvp.Key] = JsonToClr(kvp.Value);
                            }
                            await dt.AddRowAsync(row);
                        }
                        await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable());
                        _logger.Debug("[SESSION] Restored {RowCount} rows into temp table {TableName}", rows.Count, info.Name);
                    }
                    else
                    {
                        _logger.Debug("[SESSION] Data file for {TableName} found but contained 0 rows.", info.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.WriteLine($"Warning: Failed to restore temp table {info.Name}: {ex.Message}", ConsoleColor.Yellow);
                }
            }
            else
            {
                _logger.Debug("[SESSION] No data file found for temp table {TableName} at {DataFilePath}", info.Name, info.DataFilePath);
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

            var pivotEngine = new Engines.PivotEngine(_evaluator, _logger);
            foreach (var op in table.TableOperators)
            {
                if (op is PivotClause pivot) allRows = await pivotEngine.ApplyPivot(allRows, pivot);
                else if (op is UnpivotClause unpivot) allRows = await pivotEngine.ApplyUnpivot(allRows, unpivot);
            }

            var resultTable = new DataTable();
            if (allRows.Count > 0) resultTable.SetColumns(allRows[0].Columns.Keys);
            foreach (var r in allRows) await resultTable.AddRowAsync(r);
            
            yield return resultTable;
        }
    }
}
