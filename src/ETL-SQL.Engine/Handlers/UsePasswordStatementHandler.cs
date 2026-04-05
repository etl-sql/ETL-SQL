using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the USE PASSWORD = '...' statement, setting the script-level password for encryption/decryption.
    /// </summary>
    public class UsePasswordStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(UsePasswordStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (UsePasswordStatement)statement;
            context.ScriptPassword = stmt.Password;

            if (context.IsVerbose)
            {
                var masked = stmt.ToSql(true);
                context.Log($"Script password set: {masked}");
            }

            return Task.CompletedTask;
        }
    }
}
