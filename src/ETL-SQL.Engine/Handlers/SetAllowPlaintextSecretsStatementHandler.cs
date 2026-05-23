using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles SET ALLOW_PLAINTEXT_SECRETS ON/OFF for unsafe local development saves.
    /// </summary>
    public class SetAllowPlaintextSecretsStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(SetAllowPlaintextSecretsStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetAllowPlaintextSecretsStatement)statement;
            context.AllowPlaintextSecrets = stmt.Enabled;

            if (stmt.Enabled)
            {
                context.Log("Warning: ALLOW_PLAINTEXT_SECRETS is ON. Plaintext secrets may be saved to disk and should not be committed to source control.", ConsoleColor.Yellow);
            }
            else if (context.IsVerbose)
            {
                context.Log("ALLOW_PLAINTEXT_SECRETS set to OFF");
            }

            return Task.CompletedTask;
        }
    }
}
