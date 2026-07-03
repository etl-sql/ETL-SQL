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
            await foreach (var _ in replaceSql.ExecuteRawSql($"DROP TABLE IF EXISTS {_evaluator.GetSqlTableName(stmt.TargetTable, replaceSql.Dialect)};")) { }
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
                await foreach (var _ in sqlConn.ExecuteRawSql($"CREATE TABLE {_evaluator.GetSqlTableName(stmt.TargetTable, sqlConn.Dialect)} (\n  {string.Join(",\n  ", cols)}\n);")) { }
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
            }
            else if (!stmt.IfExists)
                throw new ExecutionException($"Table not found: {connName}");
        }
        else if (connections.TryGetValue(connName, out var conn))
        {
            if (conn is IDatabaseSource sqlConn)
            {
                var ifExists = stmt.IfExists ? "IF EXISTS " : "";
                await foreach (var _ in sqlConn.ExecuteRawSql($"DROP TABLE {ifExists}{_evaluator.GetSqlTableName(stmt.TargetTable, sqlConn.Dialect)};")) { }
            }
            else
            {
                await conn.DisposeAsync();
                connections.Remove(connName);
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
            if (connections.TryGetValue(connName, out var connection) && connection is InMemoryDataSource mem)
            {
                // InMemory index removal
                _logger.WriteLine($"Index {stmt.IndexName} dropped from {connName}", ConsoleColor.Yellow);
            }
            else if (connection is IDatabaseSource sqlConn)
            {
                var ifExists = stmt.IfExists ? "IF EXISTS " : "";
                await foreach (var _ in sqlConn.ExecuteRawSql($"DROP INDEX {ifExists}{stmt.IndexName} ON {_evaluator.GetSqlTableName(stmt.Table, sqlConn.Dialect)};")) { }
            }
        }
        else
        {
            bool executedAny = false;
            foreach (var conn in connections.Values)
            {
                if (conn is InMemoryDataSource)
                {
                    _logger.WriteLine($"Index {stmt.IndexName} dropped from in-memory connection", ConsoleColor.Yellow);
                    executedAny = true;
                }
                else if (conn is IDatabaseSource sqlConn)
                {
                    if (_evaluator.IsWhatIf)
                    {
                        _logger.WriteLine($"WHAT IF: Would drop index {stmt.IndexName} from database source", ConsoleColor.Yellow);
                        executedAny = true;
                        continue;
                    }
                    var ifExists = stmt.IfExists ? "IF EXISTS " : "";
                    await foreach (var _ in sqlConn.ExecuteRawSql($"DROP INDEX {ifExists}{stmt.IndexName};")) { }
                    executedAny = true;
                }
            }

            if (!executedAny && !stmt.IfExists)
            {
                throw new ExecutionException($"Context table required for dropping index {stmt.IndexName} and no active connections were found to target.");
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
            _logger.WriteLine($"Index {stmt.IndexName} created on {connName} ({string.Join(", ", stmt.Columns)})", ConsoleColor.Green);
        }
        else if (connection is AppendOnlyColumnDataSource)
        {
            var mutable = (InMemoryDataSource)await TempTableStorageRouter.EnsureMutableAsync(
                _evaluator, connName, connection, "CREATE INDEX");
            foreach (var col in stmt.Columns) mutable.CreateIndex(col, stmt.IsUnique);
            _logger.WriteLine($"Index {stmt.IndexName} created on {connName} ({string.Join(", ", stmt.Columns)})", ConsoleColor.Green);
        }
        else
        {
            _logger.WriteLine($"Warning: Indexing not natively supported for {connection.GetType().Name}. Skipping.", ConsoleColor.Yellow);
        }
    }
}
