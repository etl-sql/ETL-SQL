using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SET SHOW_SECRETS ON/OFF statement (alias: SET SHOW_PASSWORD), toggling secret visibility in output.
    /// </summary>
    public class SetShowPasswordStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(SetShowPasswordStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetShowPasswordStatement)statement;
            context.ShowPassword = stmt.Enabled;

            if (stmt.Enabled)
            {
                context.Log("Warning: SHOW_SECRETS is ON. Sensitive values may appear in results, logs, diagnostics, and exported output.", ConsoleColor.Yellow);
            }
            else if (context.IsVerbose)
            {
                context.Log("SHOW_SECRETS set to OFF");
            }

            return Task.CompletedTask;
        }
    }
}
