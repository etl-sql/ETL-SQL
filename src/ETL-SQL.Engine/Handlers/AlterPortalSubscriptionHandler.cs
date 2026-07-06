using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles ALTER SUBSCRIPTION &lt;id&gt; SET ... — updates an existing Orchestrator subscription job.
/// Schedule, format, active state, and parameters can each be changed independently.
/// Parameters: null clause = leave unchanged; empty list = clear all; populated list = replace all.
/// </summary>
public class AlterPortalSubscriptionHandler(IJobHistoryStore store, ILogger logger) : IStatementHandler
{
    public Type SupportedStatementType => typeof(AlterPortalSubscriptionStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AlterPortalSubscriptionStatement)statement;

        // Find the matching SUB job by id suffix (naming convention: "SUB:{name}")
        var jobs = (await store.GetActiveJobsAsync()).ToList();
        var pattern = $"SUB-{stmt.SubscriptionId}:";
        var job = jobs.FirstOrDefault(j => j.Name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                   ?? jobs.FirstOrDefault(j => j.Name.Contains($":{stmt.SubscriptionId}:", StringComparison.OrdinalIgnoreCase));

        if (job is null)
        {
            logger.WriteLine(
                $"Subscription {stmt.SubscriptionId} not found in job store.",
                ConsoleColor.Yellow);
            return;
        }

        // Rebuild parameters from the existing script if needed
        var scriptPath = ResolveSubscriptionScriptPath(job.Script, context, requireWrite: stmt.Parameters is not null || stmt.NewFormat.HasValue);

        var existingParams = await ExtractParametersFromScriptAsync(scriptPath, context.CancellationToken);

        IReadOnlyList<SubscriptionParameter> finalParams = stmt.Parameters switch
        {
            null => existingParams,   // unchanged
            { Count: 0 } => Array.Empty<SubscriptionParameter>(), // clear
            IReadOnlyList<SubscriptionParameter> p => p          // replace
        };

        // Rewrite the script if parameters changed or format changed
        if (stmt.Parameters is not null || stmt.NewFormat.HasValue)
            await RewriteScriptAsync(scriptPath, finalParams, stmt.NewFormat, context.CancellationToken);

        var newSchedule = stmt.NewSchedule ?? job.Unit;
        var (interval, unit) = ParseScheduleUnit(stmt.NewSchedule, job.Interval, job.Unit);
        var isEnabled = stmt.SetActive ?? job.IsEnabled;

        var updated = new JobDefinition(
            Name: job.Name,
            Script: job.Script,
            Interval: interval,
            Unit: unit,
            AtTime: job.AtTime,
            LastRun: job.LastRun,
            NextRun: job.NextRun,
            IsEnabled: isEnabled,
            MaxRetries: job.MaxRetries,
            RetryDelaySeconds: job.RetryDelaySeconds);

        await store.SaveJobAsync(updated);

        var paramsMsg = stmt.Parameters switch
        {
            null => "parameters unchanged",
            { Count: 0 } => "parameters cleared",
            var p => $"parameters updated ({p.Count})"
        };
        logger.WriteLine(
            $"Subscription {stmt.SubscriptionId} updated. " +
            $"Schedule: {unit}. Active: {isEnabled}. {paramsMsg}.",
            ConsoleColor.Green);
    }

    private static string ResolveSubscriptionScriptPath(string scriptPath, IExecutionContext context, bool requireWrite)
    {
        return new FileSystemPolicyAuthorizer(context.SecurityService)
            .Authorize(context, context.ResolvePath(scriptPath),
                requireWrite ? FileSystemAccessKind.Write : FileSystemAccessKind.Read,
                validateFileType: false)
            .CanonicalPath;
    }

    private static async Task<IReadOnlyList<SubscriptionParameter>> ExtractParametersFromScriptAsync(string scriptPath, System.Threading.CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath)) return Array.Empty<SubscriptionParameter>();
        var result = new List<SubscriptionParameter>();
        foreach (var line in await File.ReadAllLinesAsync(scriptPath, cancellationToken))
        {
            var m = Regex.Match(line, @"^(?:SET|DECLARE)\s+(@\w+)(?:\s+STRING)?\s*=\s*'(.*)';\s*$", RegexOptions.IgnoreCase);
            if (m.Success) result.Add(new SubscriptionParameter(m.Groups[1].Value, m.Groups[2].Value));
        }
        return result;
    }

    private static async Task RewriteScriptAsync(string scriptPath, IReadOnlyList<SubscriptionParameter> parameters, PortalSubscriptionFormat? newFormat, System.Threading.CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath)) return;
        var lines = (await File.ReadAllLinesAsync(scriptPath, cancellationToken)).ToList();

        // Remove old SET / DECLARE @param lines
        lines.RemoveAll(l => Regex.IsMatch(l, @"^(?:SET|DECLARE)\s+@\w+(?:\s+STRING)?\s*=\s*'.*';\s*$", RegexOptions.IgnoreCase));

        // Find insertion point: after comment block
        int insertAt = 0;
        while (insertAt < lines.Count && lines[insertAt].StartsWith("--")) insertAt++;
        if (insertAt < lines.Count && lines[insertAt] == "") insertAt++;

        // Insert new parameter DECLARE statements
        var setLines = parameters.Select(p => $"DECLARE {p.Name} STRING = '{p.Value.Replace("'", "\\'")}';").ToList();
        if (setLines.Count > 0)
        {
            setLines.Add("");
            lines.InsertRange(insertAt, setLines);
        }

        // Update FORMAT if requested
        if (newFormat.HasValue)
        {
            var formatStr = newFormat.Value == PortalSubscriptionFormat.Csv ? "CSV" : "PDF";
            for (int i = 0; i < lines.Count; i++)
                lines[i] = Regex.Replace(lines[i], @"\bFORMAT\s+(PDF|CSV|BOTH)\b", $"FORMAT {formatStr}", RegexOptions.IgnoreCase);
        }

        await File.WriteAllLinesAsync(scriptPath, lines, cancellationToken);
    }

    private static (int interval, string unit) ParseScheduleUnit(string? newSchedule, int existingInterval, string existingUnit)
    {
        if (newSchedule is null) return (existingInterval, existingUnit);
        return newSchedule.ToUpperInvariant() switch
        {
            "HOURLY" => (1, "HOUR"),
            "DAILY" => (1, "DAY"),
            "WEEKLY" => (1, "WEEK"),
            "MONTHLY" => (1, "MONTH"),
            _ => (existingInterval, existingUnit)
        };
    }
}
