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
/// Handles ALTER JOB definition changes. Schedule and notification links are separate statements.
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
        await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Job,
            stmt.JobName, existing.Id, existing.TenantId,
            OrchestratorObjectPermission.Manage, existing.CreatedBy);

        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would alter job '{stmt.JobName}'.", ConsoleColor.Yellow);
            return;
        }

        var targetPath = stmt.TargetPath ?? existing.TargetPath;
        var newScript = existing.Script;
        var scriptHash = existing.ScriptHash;
        if (stmt.TargetPath is not null && existing.JobType == JobTargetKind.Script)
        {
            targetPath = await PinBundlePathAsync(stmt.TargetPath, context);
            newScript = new RunScriptStatement(
                new LiteralExpression(targetPath, TokenType.STRING_LITERAL),
                []).ToSql();
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(newScript));
            scriptHash = "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var updated = existing with
        {
            Script = newScript,
            TargetPath = targetPath,
            MaxRetries = stmt.MaxRetries ?? existing.MaxRetries,
            RetryDelaySeconds = stmt.RetryDelaySeconds ?? existing.RetryDelaySeconds,
            DisplayName = stmt.Metadata.DisplayName ?? existing.DisplayName,
            Description = stmt.Metadata.Description ?? existing.Description,
            Options = CatalogStatementSupport.SerializeOptions(stmt.Metadata.Options) ?? existing.Options,
            ScriptHash = scriptHash,
            HashPolicy = existing.JobType == JobTargetKind.Script ? context.ScriptHashPolicy : existing.HashPolicy,
            ModifiedBy = CatalogStatementSupport.ActingIdentity(context)
        };

        await _store.SaveJobAsync(updated);
        context.Log($"Job '{stmt.JobName}' altered successfully.", ConsoleColor.Green);
    }

    private static async Task<string> PinBundlePathAsync(string path, IExecutionContext context)
    {
        if (!BundleUri.TryParse(path, out var uri) || uri == null || uri.Version.HasValue)
            return path;

        var store = context.ServiceProvider.GetService<IBundleStore>();
        if (store == null) return path;
        var latest = await store.GetLatestVersionAsync(uri.BundleName);
        if (latest == null) return path;
        var pinned = uri.ToPinnedString(latest.Version);
        context.Log($"Resolved {path} to {pinned} for scheduled job stability.", ConsoleColor.Cyan);
        return pinned;
    }
}
