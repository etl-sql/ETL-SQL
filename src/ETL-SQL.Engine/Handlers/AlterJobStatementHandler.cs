using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the ALTER JOB statement: modifies an existing job's schedule and/or script body.
/// Throws <see cref="ExecutionException"/> if the named job does not exist.
/// </summary>
public class AlterJobStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(AlterJobStatement);
    private readonly IJobHistoryStore _store;

    public AlterJobStatementHandler(IJobHistoryStore store)
    {
        _store = store;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AlterJobStatement)statement;

        // Require the job to already exist — ALTER is not CREATE.
        var existing = await _store.GetJobAsync(stmt.JobName)
            ?? throw new ExecutionException($"ALTER JOB failed: job '{stmt.JobName}' not found. Use CREATE JOB to create it.");

        // Build the updated definition, applying only what was specified.
        string newScript = stmt.Script != null
            ? await PinBundlePathsAsync(stmt.Script.ToSql(), context)
            : existing.Script;

        int newInterval = stmt.Schedule?.Interval ?? existing.Interval;
        string newUnit = stmt.Schedule?.Unit ?? existing.Unit;
        string? newAtTime = stmt.Schedule != null ? stmt.Schedule.AtTime : existing.AtTime;

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(newScript));
        var scriptHash = "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant();

        var updated = existing with
        {
            Script = newScript,
            Interval = newInterval,
            Unit = newUnit,
            AtTime = newAtTime,
            ScriptHash = scriptHash,
            HashPolicy = context.ScriptHashPolicy
        };

        await _store.SaveJobAsync(updated);
        context.Log($"Job '{stmt.JobName}' altered successfully.", ConsoleColor.Green);
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
