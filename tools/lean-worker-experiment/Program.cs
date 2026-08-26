using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Profiling;
using ETL_SQL.Orchestrator;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Worker;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            if (args.Length == 1 && args[0] is "--version" or "-v")
            {
                Console.WriteLine(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(Program).Assembly.GetName().Version?.ToString());
                return 0;
            }

            var manifest = await WorkerProfileManifest.LoadAsync(CancellationToken.None);
            SecurityEventRuntime.ConfigureLocalOutboxFactory(new SqliteSecurityEventOutboxFactory());
            await EnterprisePolicyRuntime.InitializeFromMachineAsync();

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            await using var services = BuildServiceProvider();
            if (args.Length == 1 && args[0] == "profile-probe")
                return EmitProfileProbe(manifest, services);
            if (args.Length > 0 && args[0] == "runner")
                return await RunProtocolAsync(services, cancellation.Token);
            if (args.Length > 1 && args[0] == "run")
                return await RunOnceAsync(services, args, cancellation.Token);

            await Console.Error.WriteLineAsync("Usage: etl-sql-worker run <script.etlsql> [--json] [--session <id>] [--resume] [--log <directory>]");
            return 2;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Worker execution was cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(SecretRedactor.Redact(ex.Message));
            return 1;
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddEnterprisePolicy()
            .Build();
        ETL_SQL.Core.Metadata.SnippetLibrary.Initialize(configuration["Snippets:UserSnippetsPath"]);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var logger = new LoggerService();
        logger.InitializeAppLogger(
            configuration["Logging:AppLog:Directory"] ?? "logs/app",
            int.TryParse(configuration["Logging:AppLog:RetentionDays"], out var retention) ? retention : 30,
            int.TryParse(configuration["Logging:AppLog:FileSizeLimitMb"], out var size) ? size : 10);
        services.AddSingleton(logger);
        services.AddSingleton<ETL_SQL.Common.ILogger>(logger);
        services.AddSingleton<ILoggerService>(logger);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });
        services.AddEtlSqlEngine(configuration);
        return services.BuildServiceProvider();
    }

    private static int EmitProfileProbe(WorkerProfileManifest manifest, IServiceProvider services)
    {
        _ = services.GetRequiredService<ETL_SQL.Engine.Evaluator>();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            manifest.Profile,
            workingSetBytes = process.WorkingSet64,
            peakWorkingSetBytes = process.PeakWorkingSet64,
            loadedAssemblyCount = assemblies.Length,
            loadedAssemblies = assemblies
        }, JsonOptions));
        return 0;
    }

    private static async Task<int> RunOnceAsync(ServiceProvider services, string[] args, CancellationToken cancellationToken)
    {
        var scriptPath = Path.GetFullPath(args[1]);
        var ctx = new CliContext
        {
            Command = "run",
            ScriptFile = new FileInfo(scriptPath),
            IsJsonMode = args.Contains("--json", StringComparer.Ordinal),
            IsSilentMode = args.Contains("--json", StringComparer.Ordinal),
            SessionId = ReadOption(args, "--session") ?? Guid.NewGuid().ToString("N"),
            Resume = args.Contains("--resume", StringComparer.Ordinal),
            BatchSize = int.TryParse(ReadOption(args, "--batch-size"), out var batch) ? batch : 10_000
        };

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Worker script file not found.", scriptPath);
        ConfigureLogger(services, ctx.IsJsonMode, ReadOption(args, "--log"), ctx.ScriptFile.Name);

        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime.TotalSeconds;
        await using var session = new ExecutionSession(services, ctx, services.GetRequiredService<ETL_SQL.Common.ILogger>());
        var result = await session.ExecuteAsync(
            await File.ReadAllTextAsync(scriptPath, cancellationToken),
            jobName: ctx.ScriptFile.Name,
            resume: ctx.Resume,
            cancellationToken: cancellationToken);
        process.Refresh();

        if (ctx.IsJsonMode)
        {
            var evaluator = session.LastEvaluator;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "done",
                exitCode = result.Success ? 0 : 1,
                success = result.Success,
                rowsProcessed = result.RowsProcessed,
                error = result.Success ? null : string.Join("; ", result.Diagnostics.Select(item => item.Message)),
                peakMemoryBytes = process.PeakWorkingSet64,
                cpuTimeSeconds = Math.Max(0, process.TotalProcessorTime.TotalSeconds - cpuStart),
                sessionId = ctx.SessionId,
                rowsQuarantined = result.RowsQuarantined,
                rowsWarned = result.RowsWarned,
                dataQualityFailures = result.DataQualityFailures,
                dataQualityColumnMetrics = result.DataQualityColumnMetrics,
                dataQualityRuleFailures = result.DataQualityRuleFailures,
                statementMetrics = StatementMetricsPayload.FromRun(evaluator?.Telemetry.ProfileMetrics, !result.Success)
            }, JsonOptions));
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<int> RunProtocolAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        ConfigureLogger(services, true, null, null);
        Console.WriteLine(JsonSerializer.Serialize(new { type = "ready", protocol = 1 }, JsonOptions));
        string? line;
        while ((line = await Console.In.ReadLineAsync(cancellationToken)) is not null)
        {
            var request = JsonSerializer.Deserialize<RunnerRequest>(line, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidDataException("Runner request is invalid.");
            var requestArgs = new List<string> { "run", request.ScriptFile, "--json", "--session", request.SessionId ?? Guid.NewGuid().ToString("N") };
            if (request.Resume) requestArgs.Add("--resume");
            await RunOnceAsync(services, requestArgs.ToArray(), cancellationToken);
        }
        return 0;
    }

    private static void ConfigureLogger(ServiceProvider services, bool json, string? logPath, string? scriptName)
    {
        var logger = services.GetRequiredService<LoggerService>();
        logger.SuppressConsole = json;
        logger.IsSilent = json;
        logger.IsJsonMode = json;
        if (!string.IsNullOrWhiteSpace(logPath) && !string.IsNullOrWhiteSpace(scriptName))
            logger.InitializeScriptLogger(scriptName, Path.GetFullPath(logPath), 30, 10);
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed record RunnerRequest(string Id, string ScriptFile, string? SessionId, bool Resume = false);
}
