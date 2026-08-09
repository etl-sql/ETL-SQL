using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Profiling;
using ETL_SQL.Core.Quality;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.App
{
    internal static class WarmJobRunner
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static async Task<int> RunAsync(IServiceProvider services)
        {
            if (services.GetService<ILogger>() is LoggerService loggerService)
            {
                loggerService.SuppressConsole = true;
                loggerService.IsSilent = true;
                loggerService.IsJsonMode = true;
            }

            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { type = "ready", protocol = 1 }));
            await Console.Out.FlushAsync();

            string? line;
            while ((line = await Console.In.ReadLineAsync()) != null)
            {
                WarmRunnerRequest? request = null;
                try
                {
                    request = JsonSerializer.Deserialize<WarmRunnerRequest>(line, JsonOptions);
                    if (request == null || string.IsNullOrWhiteSpace(request.Id))
                        throw new InvalidOperationException("Runner request is missing an id.");

                    var result = await ExecuteRequestAsync(services, request);
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
                    await Console.Out.FlushAsync();
                }
                catch (Exception ex)
                {
                    var id = request?.Id ?? string.Empty;
                    var response = new WarmRunnerResponse(
                        "result",
                        id,
                        false,
                        0,
                        SecretRedactor.Redact(ex.Message),
                        Process.GetCurrentProcess().PeakWorkingSet64,
                        0,
                        request?.SessionId,
                        0,
                        0,
                        null,
                        null,
                        null,
                        null);
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
                    await Console.Out.FlushAsync();
                }
            }

            return 0;
        }

        internal static async Task<WarmRunnerResponse> ExecuteRequestAsync(IServiceProvider services, WarmRunnerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ScriptFile) || !File.Exists(request.ScriptFile))
                throw new FileNotFoundException("Runner script file not found.", request.ScriptFile);

            var scriptText = await File.ReadAllTextAsync(request.ScriptFile);
            var process = Process.GetCurrentProcess();
            var startCpu = process.TotalProcessorTime.TotalSeconds;

            var ctx = new CliContext
            {
                Command = "run",
                BatchSize = request.BatchSize > 0 ? request.BatchSize : 10000,
                IsJsonMode = true,
                IsSilentMode = true,
                SessionId = string.IsNullOrWhiteSpace(request.SessionId)
                    ? Guid.NewGuid().ToString("N")
                    : request.SessionId!
            };

            var logger = services.GetRequiredService<ILogger>();
            await using var session = new ExecutionSession(services, ctx, logger);
            var execution = await session.ExecuteAsync(
                scriptText,
                jobName: request.JobName,
                queueWaitMs: request.QueueWaitMs,
                variableOverrides: request.VariableOverrides,
                resume: request.Resume);

            process.Refresh();
            var cpuSeconds = Math.Max(0, process.TotalProcessorTime.TotalSeconds - startCpu);
            var error = execution.Success
                ? null
                : string.Join("; ", execution.Diagnostics.Select(d => d.Message));
            var configuration = services.GetService<IConfiguration>();
            var statementMetrics = StatementMetricsPayload.FromRun(
                session.LastEvaluator?.Telemetry.ProfileMetrics,
                runFailed: !execution.Success,
                maxStatements: configuration?.GetValue<int>(
                    "Orchestrator:MaxStatementsPerRun", StatementMetricsPayload.DefaultMaxStatements)
                    ?? StatementMetricsPayload.DefaultMaxStatements,
                maxTextLength: configuration?.GetValue<int>(
                    "Orchestrator:MaxStatementTextLength", StatementTextNormalizer.DefaultMaxLength)
                    ?? StatementTextNormalizer.DefaultMaxLength);

            return new WarmRunnerResponse(
                "result",
                request.Id,
                execution.Success,
                execution.RowsProcessed,
                string.IsNullOrWhiteSpace(error) ? null : SecretRedactor.Redact(error),
                process.PeakWorkingSet64,
                cpuSeconds,
                ctx.SessionId,
                execution.RowsQuarantined,
                execution.RowsWarned,
                execution.DataQualityFailures,
                execution.DataQualityColumnMetrics,
                execution.DataQualityRuleFailures,
                statementMetrics);
        }
    }

    internal sealed record WarmRunnerRequest(
        string Id,
        string? ScriptFile,
        string? SessionId,
        string? JobName,
        long QueueWaitMs,
        int BatchSize,
        IReadOnlyDictionary<string, string>? VariableOverrides = null,
        bool Resume = false);

    internal sealed record WarmRunnerResponse(
        string Type,
        string Id,
        bool Success,
        long RowsProcessed,
        string? ErrorMessage,
        long PeakMemoryBytes,
        double CpuTimeSeconds,
        string? SessionId,
        long RowsQuarantined,
        long RowsWarned,
        string? DataQualityFailures,
        IReadOnlyList<DataQualityColumnMetric>? DataQualityColumnMetrics,
        IReadOnlyList<DataQualityRuleFailureMetric>? DataQualityRuleFailures,
        IReadOnlyList<StatementMetricsPayload>? StatementMetrics);
}
