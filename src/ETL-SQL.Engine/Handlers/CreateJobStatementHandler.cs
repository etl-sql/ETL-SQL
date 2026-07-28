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
/// Handles the CREATE JOB statement, scheduling an ETL-SQL script for automated execution.
/// </summary>
public class CreateJobStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreateJobStatement);
    private readonly IJobHistoryStore _store;
    private readonly IJobCatalogStore? _catalog;

    public CreateJobStatementHandler(IJobHistoryStore store, IJobCatalogStore? catalog = null)
    {
        _store = store;
        _catalog = catalog ?? store as IJobCatalogStore;
    }

    /// <summary>Executes the CREATE JOB statement, registering the job in the persistent job store.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateJobStatement)statement;
        if (stmt.JobName.StartsWith("sub_", StringComparison.OrdinalIgnoreCase))
            throw new ExecutionException(
                "Job names beginning with 'sub_' are reserved for subscription-generated objects. " +
                "Choose a stable script-facing name and use DISPLAY_NAME for the operator-facing label.",
                null, stmt.Line, stmt.Column);

        var existing = await _store.GetJobAsync(stmt.JobName);
        if (existing is not null && stmt.Mode == ObjectCreationMode.Create)
            throw new ExecutionException(
                $"Job '{stmt.JobName}' already exists. Use CREATE OR ALTER JOB to update it, " +
                $"CREATE OR REPLACE JOB to redefine it, or DROP JOB {stmt.JobName} first.",
                null, stmt.Line, stmt.Column);

        if (context.IsWhatIf)
        {
            var action = existing is null ? "create" : stmt.Mode == ObjectCreationMode.CreateOrReplace ? "replace" : "alter";
            context.Log($"WHAT IF: Would {action} {stmt.TargetKind.ToString().ToUpperInvariant()} job " +
                        $"'{stmt.JobName}' targeting '{stmt.TargetPath}'.", ConsoleColor.Yellow);
            return;
        }

        var targetPath = stmt.TargetKind == JobTargetKind.Script
            ? await PinBundlePathAsync(stmt.TargetPath, context)
            : stmt.TargetPath;
        var scriptText = stmt.TargetKind == JobTargetKind.Script
            ? new RunScriptStatement(
                new LiteralExpression(targetPath, TokenType.STRING_LITERAL),
                []).ToSql()
            : string.Empty;
        string? scriptHash = null;
        if (stmt.TargetKind == JobTargetKind.Script)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(scriptText));
            scriptHash = "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var patches = existing is not null && stmt.Mode == ObjectCreationMode.CreateOrAlter;
        var identity = CatalogStatementSupport.ActingIdentity(context);
        var job = new JobDefinition(
            stmt.JobName,
            scriptText,
            // Legacy interval columns remain physically present during the normalized-store rollout,
            // but this grammar never drives them. Jobs fire only through JobSchedules links.
            1,
            "HOUR",
            null,
            existing?.LastRun,
            null,
            existing?.IsEnabled ?? true,
            stmt.MaxRetries ?? (patches ? existing!.MaxRetries : 0),
            stmt.RetryDelaySeconds ?? (patches ? existing!.RetryDelaySeconds : 30),
            ScriptHash: scriptHash,
            HashPolicy: stmt.TargetKind == JobTargetKind.Script ? context.ScriptHashPolicy : "Off",
            Version: existing?.Version ?? 1,
            JobType: stmt.TargetKind,
            TargetPath: targetPath,
            DisplayName: stmt.Metadata.DisplayName ?? (patches ? existing!.DisplayName : stmt.JobName),
            Description: stmt.Metadata.Description ?? (patches ? existing!.Description : null),
            Options: CatalogStatementSupport.SerializeOptions(stmt.Metadata.Options)
                     ?? (patches ? existing!.Options : null),
            CreatedBy: existing?.CreatedBy ?? identity,
            ModifiedBy: identity);

        if (existing is not null
            && stmt.Mode == ObjectCreationMode.CreateOrReplace
            && _catalog is null)
            throw new ExecutionException(
                "CREATE OR REPLACE JOB cannot reset links because the configured job store does not expose the scheduler catalog.",
                null, stmt.Line, stmt.Column);

        await _store.SaveJobAsync(job);

        if (existing is not null && stmt.Mode == ObjectCreationMode.CreateOrReplace)
            await ResetAttachmentsAsync(stmt.JobName);

        CatalogStatementSupport.AuditMutation(
            context,
            existing is null ? "CREATE_JOB" : stmt.Mode == ObjectCreationMode.CreateOrReplace ? "REPLACE_JOB" : "ALTER_JOB",
            $"JOB:{stmt.JobName}",
            $"Job '{stmt.JobName}' {(existing is null ? "created" : "updated")} as {stmt.TargetKind.ToString().ToUpperInvariant()} '{targetPath}'.");
        context.Log($"Job '{stmt.JobName}' {(existing is null ? "created" : "updated")} as " +
                    $"{stmt.TargetKind.ToString().ToUpperInvariant()} '{targetPath}'.", ConsoleColor.Green);
    }

    private async Task ResetAttachmentsAsync(string jobName)
    {
        if (_catalog is null) return;
        foreach (var link in await _catalog.GetJobSchedulesAsync(jobName))
            await _catalog.RemoveJobScheduleAsync(jobName, link.ScheduleName);
        foreach (var link in await _catalog.GetJobNotificationsAsync(jobName))
            await _catalog.RemoveJobNotificationAsync(jobName, link.NotificationName, link.Trigger);
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
