using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE JOB statement, scheduling an ETL-SQL script for automated execution.
    /// </summary>
    public class CreateJobStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(CreateJobStatement);
        private readonly IJobHistoryStore _store;

        public CreateJobStatementHandler(IJobHistoryStore store)
        {
            _store = store;
        }

        /// <summary>Executes the CREATE JOB statement, registering the job in the persistent job store.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateJobStatement)statement;
            var scriptText = stmt.Script.ToSql();
            var hashBytes  = SHA256.HashData(Encoding.UTF8.GetBytes(scriptText));
            var scriptHash = "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant();

            var job = new JobDefinition(
                stmt.JobName,
                scriptText,
                stmt.Schedule.Interval,
                stmt.Schedule.Unit,
                stmt.Schedule.AtTime,
                null,
                null,
                true,
                stmt.MaxRetries,
                stmt.RetryDelaySeconds,
                ScriptHash:  scriptHash,
                HashPolicy:  context.ScriptHashPolicy
            );

            await _store.SaveJobAsync(job);
            context.Log($"Job '{stmt.JobName}' created/updated successfully.", ConsoleColor.Green);
        }
    }
}
