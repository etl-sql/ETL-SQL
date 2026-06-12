using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;

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
            var scriptText = await PinBundlePathsAsync(stmt.Script.ToSql(), context);
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(scriptText));
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
                ScriptHash: scriptHash,
                HashPolicy: context.ScriptHashPolicy
            );

            await _store.SaveJobAsync(job);
            context.Log($"Job '{stmt.JobName}' created/updated successfully.", ConsoleColor.Green);
        }

        private static async Task<string> PinBundlePathsAsync(string scriptText, IExecutionContext context)
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new Parser(tokens, scriptText).Parse();
            if (script.Statements.Count != 1 || script.Statements[0] is not RunScriptStatement run)
                return scriptText;
            if (run.PathExpression is not LiteralExpression lit || lit.Value is not string path)
                return scriptText;
            if (!BundleUri.TryParse(path, out var uri) || uri == null || uri.Version.HasValue)
                return scriptText;

            var store = context.ServiceProvider.GetService<IBundleStore>();
            if (store == null) return scriptText;
            var latest = await store.GetLatestVersionAsync(uri.BundleName);
            if (latest == null) return scriptText;
            var pinned = uri.ToPinnedString(latest.Version);
            context.Log($"Resolved {path} to {pinned} for scheduled job stability.", ConsoleColor.Cyan);
            return new RunScriptStatement(new LiteralExpression(pinned, TokenType.STRING_LITERAL), run.Parameters).ToSql();
        }
    }
}
