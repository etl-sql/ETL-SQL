using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles CREATE SUBSCRIPTION — registers a portal subscription as an Orchestrator job.
    /// Parameters are serialized to JSON and injected as SET statements at the top of the generated script.
    /// </summary>
    public class CreatePortalSubscriptionHandler(IJobHistoryStore store, ILogger logger, IConfiguration? config = null) : IStatementHandler
    {
        public Type SupportedStatementType => typeof(CreatePortalSubscriptionStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreatePortalSubscriptionStatement)statement;

            var name = stmt.Name ?? Path.GetFileNameWithoutExtension(stmt.ReportPath);
            var jobName = $"SUB:{name}";

            var (interval, unit) = ParseScheduleOrRefresh(stmt.Schedule, stmt.OnRefresh);
            var scriptPath = GenerateSubscriptionScript(stmt, context);
            var job = new JobDefinition(
                Name: jobName,
                Script: scriptPath,
                Interval: interval,
                Unit: unit,
                AtTime: null,
                LastRun: null,
                NextRun: null,
                IsEnabled: stmt.IsActive,
                MaxRetries: 3,
                RetryDelaySeconds: config?.GetValue<int>("Portal:SubscriptionRetryDelaySeconds") ?? 60);

            await store.SaveJobAsync(job);

            var paramsJson = stmt.Parameters.Count > 0
                ? JsonSerializer.Serialize(BuildParamDict(stmt.Parameters))
                : null;

            logger.WriteLine(
                $"Subscription '{name}' created. Schedule: {stmt.Schedule ?? "ON REFRESH"}. " +
                $"Format: {stmt.Format}. Parameters: {paramsJson ?? "none"}.",
                ConsoleColor.Green);
        }

        private string GenerateSubscriptionScript(CreatePortalSubscriptionStatement stmt, IExecutionContext context)
        {
            var sessionRoot = context.SessionRoot;
            var subDir = Path.Combine(sessionRoot, "subscriptions");
            Directory.CreateDirectory(subDir);

            var safeName = SanitizeName(stmt.Name ?? Path.GetFileNameWithoutExtension(stmt.ReportPath));
            var scriptPath = Path.Combine(subDir, $"sub_{safeName}.etlsql");

            var sb = new StringBuilder();
            sb.AppendLine($"-- Subscription: {stmt.Name ?? stmt.ReportPath}");
            sb.AppendLine($"-- Report: {stmt.ReportPath}");
            sb.AppendLine($"-- Recipient: {stmt.Recipient}");
            sb.AppendLine($"-- Schedule: {stmt.Schedule ?? "ON REFRESH"}");
            sb.AppendLine();

            foreach (var p in stmt.Parameters)
                sb.AppendLine($"SET {p.Name} = '{p.Value.Replace("'", "\\'")}';");

            if (stmt.Parameters.Count > 0)
                sb.AppendLine();

            var formatStr = stmt.Format switch
            {
                PortalSubscriptionFormat.Csv => "CSV",
                PortalSubscriptionFormat.Both => "PDF",
                _ => "PDF"
            };
            sb.AppendLine($"EXPORT REPORT '{stmt.ReportPath}' FORMAT {formatStr};");
            sb.AppendLine();
            sb.AppendLine($"SEND EMAIL");
            sb.AppendLine($"    TO      '{Esc(stmt.Recipient)}'");
            sb.AppendLine($"    SUBJECT 'Subscription: {Esc(stmt.ReportPath)}'");
            sb.AppendLine($"    BODY    'Your scheduled report is ready.'");
            sb.AppendLine($"    AT {stmt.SmtpAlias};");

            File.WriteAllText(scriptPath, sb.ToString());
            return scriptPath;
        }

        private static (int interval, string unit) ParseScheduleOrRefresh(string? schedule, bool onRefresh)
        {
            if (onRefresh) return (0, "REFRESH");
            return schedule?.ToUpperInvariant() switch
            {
                "HOURLY" => (1, "HOUR"),
                "DAILY" => (1, "DAY"),
                "WEEKLY" => (1, "WEEK"),
                "MONTHLY" => (1, "MONTH"),
                _ => (1, "DAY")
            };
        }

        private static Dictionary<string, string> BuildParamDict(IReadOnlyList<SubscriptionParameter> parameters)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parameters) dict[p.Name] = p.Value;
            return dict;
        }

        private static string SanitizeName(string name) =>
            new string(System.Array.ConvertAll(name.ToCharArray(),
                c => char.IsLetterOrDigit(c) ? c : '_'));

        private static string Esc(string s) => s.Replace("'", "\\'");
    }
}
