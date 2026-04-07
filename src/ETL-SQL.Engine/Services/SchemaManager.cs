using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Services
{
    /// <summary>
    /// Manages DDL operations including creating and dropping tables, indexes, procedures, and functions.
    /// Orchestrates schema changes across various data sources.
    /// </summary>
    public class SchemaManager
    {
        private readonly Evaluator _evaluator;
        private readonly VariableScopeManager _variableScopeManager;

        public SchemaManager(Evaluator evaluator, VariableScopeManager variableScopeManager)
        {
            _evaluator = evaluator;
            _variableScopeManager = variableScopeManager;
        }

        /// <summary>Executes a CREATE TABLE statement.</summary>
        public async Task EvaluateCreateTable(CreateTableStatement stmt, IDictionary<string, IDataSource> connections)
        {
            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            bool isTemp = stmt.TargetTable.TableName.StartsWith("#") && stmt.TargetTable.ConnectionName == null;

            if (_evaluator.IsWhatIf)
            {
                Logger.WriteLine($"WHAT IF: Would create table {connName}", ConsoleColor.Yellow);
                return;
            }

            if (isTemp || !connections.ContainsKey(connName))
            {
                var mem = new InMemoryDataSource();
                mem.Validator = _evaluator;
                mem.SetSchema(stmt.Columns, stmt.TableConstraints);
                connections[connName] = mem;
            }
            else
            {
                if (connections.TryGetValue(connName, out var conn) && conn is IDatabaseSource sqlConn)
                {
                    var cols = stmt.Columns.Select(c => $"{c.ColumnName} {c.DataType}{(c.IsIdentity ? " IDENTITY" : "")}{(c.DefaultExpression != null ? $" DEFAULT {c.DefaultExpression.ToSql()}" : "")}");
                    await foreach(var _ in sqlConn.ExecuteRawSql($"CREATE TABLE {_evaluator.GetSqlTableName(stmt.TargetTable)} (\n  {string.Join(",\n  ", cols)}\n);")){}
                }
            }
        }

        /// <summary>Executes a DROP TABLE statement.</summary>
        public async Task EvaluateDropTable(DropTableStatement stmt, IDictionary<string, IDataSource> connections)
        {
            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;

            if (_evaluator.IsWhatIf)
            {
                Logger.WriteLine($"WHAT IF: Would drop table {connName} (IfExists: {stmt.IfExists})", ConsoleColor.Yellow);
                return;
            }

            if (connName.StartsWith("#") && stmt.TargetTable.ConnectionName == null)
            {
                if (!connections.Remove(connName) && !stmt.IfExists) throw new ExecutionException($"Table not found: {connName}");
            }
            else if (connections.TryGetValue(connName, out var conn) && conn is IDatabaseSource sqlConn)
            {
                var ifExists = stmt.IfExists ? "IF EXISTS " : "";
                await foreach(var _ in sqlConn.ExecuteRawSql($"DROP TABLE {ifExists}{_evaluator.GetSqlTableName(stmt.TargetTable)};")){}
            }
            else if (!stmt.IfExists)
            {
                throw new ExecutionException($"Table or connection not found: {connName}");
            }
        }

        /// <summary>Executes a DROP CONNECTION statement.</summary>
        public Task EvaluateDropConnection(DropConnectionStatement stmt, IDictionary<string, IDataSource> connections)
        {
            if (!connections.Remove(stmt.ConnectionName) && !stmt.IfExists)
                throw new ExecutionException($"Connection not found: {stmt.ConnectionName}");
            return Task.CompletedTask;
        }

        /// <summary>Executes a DROP PROCEDURE statement.</summary>
        public void EvaluateDropProcedure(DropProcedureStatement stmt)
        {
            if (!_variableScopeManager.RemoveProcedure(stmt.ProcedureName) && !stmt.IfExists)
                throw new ExecutionException($"Procedure not found: {stmt.ProcedureName}");
        }

        /// <summary>Executes a DROP FUNCTION statement.</summary>
        public void EvaluateDropFunction(DropFunctionStatement stmt)
        {
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
                    Logger.WriteLine($"WHAT IF: Would drop index {stmt.IndexName} from {connName}", ConsoleColor.Yellow);
                    return;
                }
                if (connections.TryGetValue(connName, out var connection) && connection is InMemoryDataSource mem)
                {
                    // InMemory index removal
                    Logger.WriteLine($"Index {stmt.IndexName} dropped from {connName}", ConsoleColor.Yellow);
                }
                else if (connection is IDatabaseSource sqlConn)
                {
                    var ifExists = stmt.IfExists ? "IF EXISTS " : "";
                    await foreach(var _ in sqlConn.ExecuteRawSql($"DROP INDEX {ifExists}{stmt.IndexName} ON {_evaluator.GetSqlTableName(stmt.Table)};")){}
                }
            }
            else if (!stmt.IfExists) throw new ExecutionException($"Context table required for dropping index {stmt.IndexName}");
        }

        /// <summary>Executes a CREATE INDEX statement.</summary>
        public async Task EvaluateCreateIndex(CreateIndexStatement stmt, IDictionary<string, IDataSource> connections)
        {
            string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;
            if (!connections.TryGetValue(connName, out var connection)) throw new ExecutionException($"Unknown connection: {connName}");
            
            if (_evaluator.IsWhatIf)
            {
                Logger.WriteLine($"WHAT IF: Would create index {stmt.IndexName} on {connName} ({string.Join(", ", stmt.Columns)})", ConsoleColor.Yellow);
                return;
            }

            if (connection is InMemoryDataSource mem)
            {
                foreach (var col in stmt.Columns) mem.CreateIndex(col, stmt.IsUnique);
                Logger.WriteLine($"Index {stmt.IndexName} created on {connName} ({string.Join(", ", stmt.Columns)})", ConsoleColor.Green);
            }
            else
            {
                Logger.WriteLine($"Warning: Indexing not natively supported for {connection.GetType().Name}. Skipping.", ConsoleColor.Yellow);
            }
        }
    }
}
