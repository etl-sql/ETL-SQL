using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Manages DDL operations including creating and dropping tables, indexes, procedures, and functions.
/// Orchestrates schema changes across various data sources.
/// </summary>
public class SchemaManager(ILogger logger, Evaluator evaluator, VariableScopeManager variableScopeManager)
{
    private readonly ILogger _logger = logger;
    private readonly Evaluator _evaluator = evaluator;
    private readonly VariableScopeManager _variableScopeManager = variableScopeManager;

    // Index name -> the in-memory table it is on. An in-memory table keeps its indexes by column,
    // not by name, so without this DROP INDEX and REINDEX could not tell a real index from a typo.
    private readonly Dictionary<string, string> _memoryIndexes = new(StringComparer.OrdinalIgnoreCase);

    private void ForgetIndexesOf(string table)
    {
        foreach (var index in _memoryIndexes.Where(e => e.Value.Equals(table, StringComparison.OrdinalIgnoreCase)).Select(e => e.Key).ToList())
            _memoryIndexes.Remove(index);
    }

    /// <summary>
    /// Executes <c>REINDEX name</c>. In-memory indexes are kept current on every write, so there
    /// is nothing to rebuild; the statement only has to fail on a name that is not an index, a
    /// table or a collation.
    /// </summary>
    public void EvaluateReindex(ReindexStatement stmt, IDictionary<string, IDataSource> connections)
    {
        if (_memoryIndexes.ContainsKey(stmt.Name)
            || connections.ContainsKey(stmt.Name)
            || stmt.Name.Equals("BINARY", StringComparison.OrdinalIgnoreCase)
            || stmt.Name.Equals("NOCASE", StringComparison.OrdinalIgnoreCase)
            || stmt.Name.Equals("RTRIM", StringComparison.OrdinalIgnoreCase))
            return;

        throw new ExecutionException($"Unknown index or table '{stmt.Name}' at Line {stmt.Line}. REINDEX takes the name of an index, a table or a collation.");
    }

    /// <summary>Executes a CREATE TABLE statement.</summary>
    public async Task EvaluateCreateTable(CreateTableStatement stmt, IDictionary<string, IDataSource> connections)
    {
        string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
        bool isTemp = stmt.TargetTable.TableName.StartsWith("#") && stmt.TargetTable.ConnectionName == null;

        if (_evaluator.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would create table {connName}", ConsoleColor.Yellow);
            return;
        }

        // CREATE OR REPLACE TABLE: drop any existing relational table first. (Temp/in-memory tables are
        // replaced by the overwrite below, so they need no explicit drop.)
        if (stmt.OrReplace && !isTemp && connections.TryGetValue(connName, out var toReplace) && toReplace is IDatabaseSource replaceSql)
        {
            await foreach (var _ in replaceSql.ExecuteRawSql($"DROP TABLE IF EXISTS {_evaluator.GetSqlTableName(stmt.TargetTable, replaceSql.Dialect)};", null, _evaluator.CancellationToken)) { }
        }

        if (isTemp || !connections.ContainsKey(connName))
        {
            if (isTemp && !connections.ContainsKey(connName))
                LiveObjectLimits.EnsureTempTableCapacity(_evaluator);

            if (isTemp && _evaluator.UseColumnarTempTables && !_evaluator.IsPersistentSession && IsColumnarEligible(stmt))
            {
                connections[connName] = new AppendOnlyColumnDataSource(
                    stmt.Columns,
                    segmentRowCapacity: _evaluator.BatchSize,
                    memoryArbiter: ((IExecutionContext)_evaluator).MemoryArbiter,
                    tableConstraints: stmt.TableConstraints);
                return;
            }
            var mem = new InMemoryDataSource();
            mem.ExecutionContext = _evaluator;
            mem.Validator = _evaluator;
            mem.SetSchema(stmt.Columns, stmt.TableConstraints);
            connections[connName] = mem;
        }
        else
        {
            if (connections.TryGetValue(connName, out var conn) && conn is IDatabaseSource sqlConn)
            {
                var cols = stmt.Columns.Select(c => $"{c.ColumnName} {c.DataType}{(c.IsIdentity ? " IDENTITY" : "")}{(c.DefaultExpression != null ? $" DEFAULT {c.DefaultExpression.ToSql()}" : "")}");
                await foreach (var _ in sqlConn.ExecuteRawSql($"CREATE TABLE {_evaluator.GetSqlTableName(stmt.TargetTable, sqlConn.Dialect)} (\n  {string.Join(",\n  ", cols)}\n);", null, _evaluator.CancellationToken)) { }
            }
        }
    }

    private static bool IsColumnarEligible(CreateTableStatement statement)
    {
        if (statement.Columns.Any(column => column.IsIdentity
            || column.DefaultExpression != null
            || column.CheckConstraint != null
            || column.ForeignKey != null))
            return false;
        if (statement.TableConstraints.Any(constraint => constraint is TableCheckConstraint or TableForeignKeyConstraint))
            return false;
        try
        {
            foreach (var column in statement.Columns) ColumnBatchAdapter.GetPhysicalType(column.DataType);
            return statement.Columns.Count > 0;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Executes a DROP TABLE statement.</summary>
    public async Task EvaluateDropTable(DropTableStatement stmt, IDictionary<string, IDataSource> connections)
    {
        string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;

        if (_evaluator.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would drop table {connName} (IfExists: {stmt.IfExists})", ConsoleColor.Yellow);
            return;
        }

        if (connName.StartsWith("#") && stmt.TargetTable.ConnectionName == null)
        {
            if (connections.TryGetValue(connName, out var src))
            {
                await src.DisposeAsync();
                connections.Remove(connName);
                ForgetIndexesOf(connName);
            }
            else if (!stmt.IfExists)
                throw new ExecutionException($"Table not found: {connName}");
        }
        else if (connections.TryGetValue(connName, out var conn))
        {
            if (conn is IDatabaseSource sqlConn)
            {
                var ifExists = stmt.IfExists ? "IF EXISTS " : "";
                await foreach (var _ in sqlConn.ExecuteRawSql($"DROP TABLE {ifExists}{_evaluator.GetSqlTableName(stmt.TargetTable, sqlConn.Dialect)};", null, _evaluator.CancellationToken)) { }
            }
            else
            {
                await conn.DisposeAsync();
                connections.Remove(connName);
                ForgetIndexesOf(connName);
            }
        }
        else if (!stmt.IfExists)
        {
            throw new ExecutionException($"Table or connection not found: {connName}");
        }
    }

    /// <summary>Executes a DROP CONNECTION statement.</summary>
    public async Task EvaluateDropConnection(DropConnectionStatement stmt, IDictionary<string, IDataSource> connections)
    {
        if (_evaluator.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would drop connection {stmt.ConnectionName}", ConsoleColor.Yellow);
            return;
        }

        if (connections.TryGetValue(stmt.ConnectionName, out var ds))
        {
            await ds.DisposeAsync();
            connections.Remove(stmt.ConnectionName);
            _logger.WriteLine($"Connection {stmt.ConnectionName} dropped.", ConsoleColor.Yellow);
        }
        else if (!stmt.IfExists)
        {
            throw new ExecutionException($"Connection not found: {stmt.ConnectionName}");
        }
    }

    /// <summary>Executes a DROP PROCEDURE statement.</summary>
    public void EvaluateDropProcedure(DropProcedureStatement stmt)
    {
        if (_evaluator.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would drop procedure {stmt.ProcedureName}", ConsoleColor.Yellow);
            return;
        }

        if (!_variableScopeManager.RemoveProcedure(stmt.ProcedureName) && !stmt.IfExists)
            throw new ExecutionException($"Procedure not found: {stmt.ProcedureName}");
    }

    /// <summary>Executes a DROP FUNCTION statement.</summary>
    public void EvaluateDropFunction(DropFunctionStatement stmt)
    {
        if (_evaluator.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would drop function {stmt.FunctionName}", ConsoleColor.Yellow);
            return;
        }

        if (!_variableScopeManager.RemoveFunction(stmt.FunctionName) && !stmt.IfExists)
            throw new ExecutionException($"Function not found: {stmt.FunctionName}");
    }

    /// <summary>Executes a DROP INDEX statement.</summary>
    public async Task EvaluateDropIndex(DropIndexStatement stmt, IDictionary<string, IDataSource> connections)
    {
        if (stmt.Table != null)
        {
            string connName = stmt.Table.ConnectionName ?? stmt.Table.TableName;
            if (_evaluator.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would drop index {stmt.IndexName} from {connName}", ConsoleColor.Yellow);
                return;
            }
            if (connections.TryGetValue(connName, out var connection) && connection is InMemoryDataSource or AppendOnlyColumnDataSource)
            {
                if (_memoryIndexes.TryGetValue(stmt.IndexName, out var owner) && owner.Equals(connName, StringComparison.OrdinalIgnoreCase))
                {
                    _memoryIndexes.Remove(stmt.IndexName);
                    _logger.WriteLine($"Index {stmt.IndexName} dropped from {connName}", ConsoleColor.Yellow);
                }
                else if (!stmt.IfExists)
                {
                    throw new ExecutionException($"Unknown index '{stmt.IndexName}' on {connName} at Line {stmt.Line}.");
                }
            }
            else if (connection is IDatabaseSource sqlConn)
            {
                var ifExists = stmt.IfExists ? "IF EXISTS " : "";
                await foreach (var _ in sqlConn.ExecuteRawSql($"DROP INDEX {ifExists}{stmt.IndexName} ON {_evaluator.GetSqlTableName(stmt.Table, sqlConn.Dialect)};", null, _evaluator.CancellationToken)) { }
            }
        }
        else
        {
            if (_memoryIndexes.TryGetValue(stmt.IndexName, out var owner))
            {
                if (_evaluator.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would drop index {stmt.IndexName} from {owner}", ConsoleColor.Yellow);
                    return;
                }
                _memoryIndexes.Remove(stmt.IndexName);
                _logger.WriteLine($"Index {stmt.IndexName} dropped from {owner}", ConsoleColor.Yellow);
                return;
            }

            // Not an in-memory index: a database connection may own it. A source with no SQL
            // engine behind it (MOCKDB) holds no indexes and accepts any statement, so it is not asked.
            bool executedAny = false;
            foreach (var conn in connections.Values)
            {
                if (conn is IDatabaseSource { SupportsSqlPushdown: true } sqlConn)
                {
                    if (_evaluator.IsWhatIf)
                    {
                        _logger.WriteLine($"WHAT IF: Would drop index {stmt.IndexName} from database source", ConsoleColor.Yellow);
                        executedAny = true;
                        continue;
                    }
                    var ifExists = stmt.IfExists ? "IF EXISTS " : "";
                    await foreach (var _ in sqlConn.ExecuteRawSql($"DROP INDEX {ifExists}{stmt.IndexName};", null, _evaluator.CancellationToken)) { }
                    executedAny = true;
                }
            }

            if (!executedAny && !stmt.IfExists)
            {
                throw new ExecutionException($"Unknown index '{stmt.IndexName}' at Line {stmt.Line}. No in-memory table has an index of that name, and no database connection is open to ask.");
            }
        }
    }

    /// <summary>Executes a CREATE INDEX statement.</summary>
    public async Task EvaluateCreateIndex(CreateIndexStatement stmt, IDictionary<string, IDataSource> connections)
    {
        string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
        if (!connections.TryGetValue(connName, out var connection)) throw new ExecutionException($"Unknown connection: {connName}");

        if (_evaluator.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would create index {stmt.IndexName} on {connName} ({string.Join(", ", stmt.Columns)})", ConsoleColor.Yellow);
            return;
        }

        if (connection is InMemoryDataSource mem)
        {
            foreach (var col in stmt.Columns) mem.CreateIndex(col, stmt.IsUnique);
            _memoryIndexes[stmt.IndexName] = connName;
            _logger.WriteLine($"Index {stmt.IndexName} created on {connName} ({string.Join(", ", stmt.Columns)})", ConsoleColor.Green);
        }
        else if (connection is AppendOnlyColumnDataSource)
        {
            var mutable = (InMemoryDataSource)await TempTableStorageRouter.EnsureMutableAsync(
                _evaluator, connName, connection, "CREATE INDEX");
            foreach (var col in stmt.Columns) mutable.CreateIndex(col, stmt.IsUnique);
            _memoryIndexes[stmt.IndexName] = connName;
            _logger.WriteLine($"Index {stmt.IndexName} created on {connName} ({string.Join(", ", stmt.Columns)})", ConsoleColor.Green);
        }
        else
        {
            _logger.WriteLine($"Warning: Indexing not natively supported for {connection.GetType().Name}. Skipping.", ConsoleColor.Yellow);
        }
    }
}
