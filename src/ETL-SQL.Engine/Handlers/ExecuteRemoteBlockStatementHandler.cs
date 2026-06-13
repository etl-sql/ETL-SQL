using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the EXECUTE ON {connection} { ... } statement, pushing an entire block of statements to a remote connection for translation and execution.
    /// </summary>
    public class ExecuteRemoteBlockStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(ExecuteRemoteBlockStatement);

        public ExecuteRemoteBlockStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the remote block, translating each inner statement to the target dialect.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ExecuteRemoteBlockStatement)statement;


            var connNameObj = await context.EvaluateValue(stmt.ConnectionName, new Row());
            string connName = connNameObj?.ToString() ?? "";
            _logger.Debug("Executing remote block on {ConnName}", connName);

            if (!context.Connections.TryGetValue(connName, out var source))
            {
                var available = string.Join(", ", context.Connections.Keys);
                throw new System.Exception($"Connection '{connName}' not found in the current session. Available: [{available}]");
            }

            if (source is IPortalAdminConnection adminConn)
            {
                context.Log($"Executing admin block on {connName}...");
                foreach (var innerStmt in stmt.Body.Statements)
                {
                    if (context.IsWhatIf)
                    {
                        // Read-only validating dry-run: the connector reports a create/skip/conflict
                        // plan and may throw to fail closed on a missing reference or secret, without
                        // mutating. Falls back to a generic message when the statement is not plannable.
                        var plan = await adminConn.PlanAdminStatementAsync(innerStmt, context);
                        _logger.WriteLine(
                            plan ?? $"WHAT IF: Would execute portal admin statement {innerStmt.GetType().Name} on {connName}",
                            ConsoleColor.Yellow);
                        continue;
                    }
                    await adminConn.ExecuteAdminStatementAsync(innerStmt, context);
                }
            }
            else if (source is IDatabaseSource db)
            {
                context.Log($"Executing remote block on {connName}...");

                foreach (var innerStmt in stmt.Body.Statements)
                {
                    var compiled = context.CompileQuery(innerStmt, db.Dialect);
                    if (string.IsNullOrWhiteSpace(compiled.Sql)) continue;

                    if (context.IsWhatIf)
                    {
                        _logger.WriteLine($"WHAT IF: Would execute remote block SQL on {connName}:\n{compiled.ToEscapedSql(db.Dialect)}", ConsoleColor.Yellow);
                        continue;
                    }

                    context.LastResultSets.Clear();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var batches = db.ExecuteRawSql(compiled.Sql, compiled.Parameters.Values);

                    var currentKey = -1;
                    DataTable? currentSet = null;

                    await foreach (var batch in batches)
                    {
                        if (batch.ResultSetIndex != currentKey)
                        {
                            if (currentSet != null) context.LastResultSets.Add(currentSet);
                            currentSet = new DataTable { ResultSetIndex = batch.ResultSetIndex };
                            currentKey = batch.ResultSetIndex;
                        }

                        if (currentSet!.ColumnNames.Count == 0) currentSet.SetColumns(batch.ColumnNames);
                        foreach (var row in batch.Rows) await currentSet.AddRowAsync(row);
                    }
                    if (currentSet != null) context.LastResultSets.Add(currentSet);

                    sw.Stop();
                    if (context.LastResultSets.Count > 0)
                    {
                        context.LastResult = context.LastResultSets[^1];
                        foreach (var rs in context.LastResultSets)
                        {
                            rs.ExecutionTimeMs = sw.ElapsedMilliseconds / context.LastResultSets.Count; // Approximate
                            rs.TotalRowsMatched = rs.Rows.Count;
                        }
                    }
                }
            }
            else
            {
                throw new System.Exception($"Connection '{connName}' does not support remote block execution.");
            }

            await LoadIntoTableAsync(stmt, context);
        }

        private async Task LoadIntoTableAsync(ExecuteRemoteBlockStatement stmt, IExecutionContext context)
        {
            if (stmt.IntoTable == null || context.LastResultSets.Count == 0) return;

            var tableName = stmt.IntoTable.TableName;
            var lastResult = context.LastResultSets[^1];

            if (tableName.StartsWith("#") && !context.Connections.ContainsKey(tableName))
            {
                var mem = new InMemoryDataSource();
                mem.SetSchema(lastResult.ColumnNames.Select(c => new ColumnDefinition(c, "ANY", false)));
                context.Connections[tableName] = mem;
            }

            if (!context.Connections.TryGetValue(tableName, out var target))
                throw new System.Exception($"Target table '{tableName}' not found for INTO clause.");

            await target.TruncateAsync();

            async IAsyncEnumerable<DataTable> Batches()
            {
                yield return lastResult;
                await Task.CompletedTask;
            }

            await target.WriteBatches(Batches());
            _logger.WriteLine($"Loaded {lastResult.Rows.Count} rows into {tableName}.", ConsoleColor.Green);
        }
    }
}
