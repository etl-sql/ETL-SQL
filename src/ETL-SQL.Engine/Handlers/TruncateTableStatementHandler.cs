using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the TRUNCATE TABLE statement, efficiently removing all rows from a target table.
    /// </summary>
    public class TruncateTableStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(TruncateTableStatement);


        /// <summary>Executes the TRUNCATE TABLE statement on the resolved datasource.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            if (statement is not TruncateTableStatement truncateStmt)
                throw new ExecutionException("Invalid statement type for TruncateTableStatementHandler");

            var dataSource = await context.ResolveDataSourceAsync(truncateStmt.TargetTable);
            if (dataSource == null)
                throw new ExecutionException($"Table not found: {truncateStmt.TargetTable.TableName}");

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would truncate table {truncateStmt.TargetTable.TableName}", ConsoleColor.Yellow);
                return;
            }

            await dataSource.TruncateAsync();
        }
    }
}
