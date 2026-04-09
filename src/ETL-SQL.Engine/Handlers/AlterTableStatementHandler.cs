using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the ALTER TABLE statement, supporting ADD COLUMN, DROP COLUMN, and RENAME COLUMN.
    /// Delegates to memory sources or pushes down to SQL databases.
    /// </summary>
    public class AlterTableStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(AlterTableStatement);

        public AlterTableStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the ALTER TABLE statement, applying schema modifications.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (AlterTableStatement)statement;

            var destination = await context.ResolveDataSourceAsync(stmt.TargetTable);
            if (destination == null)
                throw new ExecutionException($"Unknown table/connection: {stmt.TargetTable.TableName} at Line {stmt.Line}");

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would execute ALTER TABLE {stmt.TargetTable.TableName} ({stmt.Action})", ConsoleColor.Yellow);
                return;
            }

            if (destination is InMemoryDataSource mem)
            {
                switch (stmt.Action)
                {
                    case AlterTableActionType.ADD:
                        mem.AddColumn(stmt.NewColumn!);
                        break;
                    case AlterTableActionType.DROP_COLUMN:
                        mem.DropColumn(stmt.ColumnToDelete!);
                        break;
                    case AlterTableActionType.RENAME_COLUMN:
                        mem.RenameColumn(stmt.OldColumnName!, stmt.NewColumnName!);
                        break;
                }
            }
            else if (destination is IDatabaseSource sqlDest)
            {
                // Push down to SQL database
                var sql = $"ALTER TABLE {context.GetSqlTableName(stmt.TargetTable)} ";
                switch (stmt.Action)
                {
                    case AlterTableActionType.ADD:
                        sql += $"ADD {stmt.NewColumn!.ColumnName} {stmt.NewColumn.DataType}";
                        if (stmt.NewColumn.DefaultExpression != null)
                            sql += $" DEFAULT {context.CompileExpression(stmt.NewColumn.DefaultExpression, sqlDest.Dialect)}";
                        break;
                    case AlterTableActionType.DROP_COLUMN:
                        sql += $"DROP COLUMN {stmt.ColumnToDelete}";
                        break;
                    case AlterTableActionType.RENAME_COLUMN:
                        // Dialect specific rename
                        if (sqlDest.Dialect.Contains("MSSQL") || sqlDest.Dialect.Contains("SQLSERVER"))
                            sql = $"EXEC sp_rename '{context.GetSqlTableName(stmt.TargetTable)}.{stmt.OldColumnName}', '{stmt.NewColumnName}', 'COLUMN'";
                        else
                            sql += $"RENAME COLUMN {stmt.OldColumnName} TO {stmt.NewColumnName}";
                        break;
                }
                await foreach (var _ in sqlDest.ExecuteRawSql(sql)) { }
            }
            else
            {
                throw new ExecutionException($"ALTER TABLE not supported for data source of type {destination.GetType().Name}");
            }
        }
    }
}
