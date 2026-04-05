using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SET SHOW_PASSWORD ON/OFF statement, toggling password visibility in output.
    /// </summary>
    public class SetShowPasswordStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(SetShowPasswordStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetShowPasswordStatement)statement;
            context.ShowPassword = stmt.Enabled;

            if (context.IsVerbose)
            {
                context.Log($"SHOW_PASSWORD set to {(stmt.Enabled ? "ON" : "OFF")}");
            }

            return Task.CompletedTask;
        }
    }
}
