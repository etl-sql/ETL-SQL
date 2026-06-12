using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles SET SCRIPT_HASH_POLICY = 'Warn'|'Block' — controls behaviour on hash mismatch for the current session.
    /// </summary>
    public class SetScriptHashPolicyHandler(ILogger logger) : IStatementHandler
    {
        public Type SupportedStatementType => typeof(SetScriptHashPolicyStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetScriptHashPolicyStatement)statement;
            var policy = stmt.Policy;
            if (!policy.Equals("Warn", StringComparison.OrdinalIgnoreCase) &&
                !policy.Equals("Block", StringComparison.OrdinalIgnoreCase))
                throw new ExecutionException(
                    $"Invalid SCRIPT_HASH_POLICY value: '{policy}'. Valid values: Warn, Block.");

            context.ScriptHashPolicy = policy.Equals("Block", StringComparison.OrdinalIgnoreCase) ? "Block" : "Warn";
            logger.WriteLine($"Script hash policy set to {context.ScriptHashPolicy}.", ConsoleColor.Cyan);
            return Task.CompletedTask;
        }
    }
}
