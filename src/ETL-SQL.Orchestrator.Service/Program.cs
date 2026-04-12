using System;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.Directory;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors;
using ETL_SQL.Common;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;

// ── Serilog bootstrap logger (captures startup errors before host is ready) ──
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System",    LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/orchestrator-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] [{SessionId: [sid=]:l}]{Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("ETL-SQL Orchestrator Service starting up.");

    var builder = WebApplication.CreateBuilder(args);

    // ── Replace default logging with Serilog ──────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
           .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
           .WriteTo.File("logs/orchestrator-.log", rollingInterval: RollingInterval.Day));

    // ── Windows Service / systemd integration ────────────────────────────
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        builder.Host.UseWindowsService(opts => opts.ServiceName = "ETL-SQL Orchestrator");
    else
        builder.Host.UseSystemd();

    // ── Configuration ────────────────────────────────────────────────────
    builder.Configuration
        .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    var cfg = builder.Configuration;

    // ── Engine services (mirrors App/DependencyInjectionSetup) ───────────
    var loggerService = new LoggerService();
    loggerService.InitializeAppLogger(
        cfg["Logging:AppLog:Directory"] ?? "logs/orchestrator",
        int.TryParse(cfg["Logging:AppLog:RetentionDays"],   out var rd) ? rd : 30,
        int.TryParse(cfg["Logging:AppLog:FileSizeLimitMb"], out var sl) ? sl : 10);

#pragma warning disable CS0618
    ETL_SQL.Common.Logger.Instance = loggerService;
#pragma warning restore CS0618

    builder.Services.AddSingleton<LoggerService>(loggerService);
    builder.Services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
    builder.Services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);

    var fnRegistry = new ETL_SQL.Engine.Functions.FunctionRegistry();
    ETL_SQL.Engine.Functions.FileFunctions.Register(fnRegistry);
    ETL_SQL.Engine.Functions.StandardFunctions.Register(fnRegistry);
    ETL_SQL.Engine.Functions.JsonFunctions.Register(fnRegistry);
    ETL_SQL.Engine.Functions.XmlFunctions.Register(fnRegistry);
    builder.Services.AddSingleton<ETL_SQL.Core.Functions.IFunctionRegistry>(fnRegistry);

    builder.Services.AddSingleton<ILineageTracker, LineageTracker>();
    builder.Services.AddSingleton<IDockerManager, DockerContainerManager>();
    builder.Services.AddSingleton<SessionStateManager>();
    builder.Services.AddSingleton<ETL_SQL.Services.SecurityService>();

    // Connectors
    builder.Services.AddSingleton<IConnector, MockDbConnector>();
    builder.Services.AddSingleton<IConnector, SqlServerConnector>();
    builder.Services.AddSingleton<IConnector, OracleConnector>();
    builder.Services.AddSingleton<IConnector, PostgresConnector>();
    builder.Services.AddSingleton<IConnector, FlatFileConnector>();
    builder.Services.AddSingleton<IConnector, JsonConnector>();
    builder.Services.AddSingleton<IConnector, XmlConnector>();
    builder.Services.AddSingleton<IConnector, ExcelConnector>();
    builder.Services.AddSingleton<IConnector, DirectoryConnector>();
    builder.Services.AddSingleton<IConnector, ParquetConnector>();
    builder.Services.AddSingleton<IConnector, AvroConnector>();
    builder.Services.AddSingleton<IConnector, SmtpConnector>();
    builder.Services.AddSingleton<IConnector>(new FtpConnector(
        cfg["Connectors:Ftp:Host"] ?? "localhost",
        cfg["Connectors:Ftp:Username"] ?? "anonymous",
        cfg["Connectors:Ftp:Password"] ?? ""));
    builder.Services.AddSingleton<IConnector>(new SftpConnector(
        cfg["Connectors:Sftp:Host"] ?? "localhost",
        cfg["Connectors:Sftp:Username"] ?? "user",
        cfg["Connectors:Sftp:Password"] ?? "pass"));
    builder.Services.AddSingleton<IConnector>(new AzureBlobConnector(
        cfg["Connectors:AzureBlob:ConnectionString"] ?? "UseDevelopmentStorage=true",
        cfg["Connectors:AzureBlob:Container"]       ?? "test"));

    builder.Services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
    builder.Services.AddSingleton<CliContext>(new CliContext());
    builder.Services.AddTransient<ExecutionSession>();
    builder.Services.AddTransient<Evaluator>();
    builder.Services.AddTransient<IExecutionContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<IVariableContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<IQueryContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<ILineageContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<ISqlCompilerContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<ITransactionContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<IDockerContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<ILoggingContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<IEvaluationContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<IDataContext>(sp => sp.GetRequiredService<Evaluator>());
    builder.Services.AddTransient<IEngineContext>(sp => sp.GetRequiredService<Evaluator>());

    var handlerTypes = typeof(DeclareStatementHandler).Assembly.GetTypes()
        .Where(t => typeof(IStatementHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
    foreach (var type in handlerTypes)
    {
        builder.Services.AddTransient(typeof(IStatementHandler), type);
        builder.Services.AddTransient(type);
    }

    // Storage, Scheduling, Throttle, Executor
    builder.Services.AddSingleton<IJobHistoryStore, SQLiteJobHistoryStore>();
    builder.Services.Configure<ETL_SQL.Orchestrator.Execution.JobThrottleOptions>(cfg.GetSection("Jobs"));
    builder.Services.AddSingleton<ETL_SQL.Orchestrator.Execution.JobThrottle>();
    builder.Services.AddSingleton<SchedulerService>();

    // Phase 7: choose execution strategy via config
    // "Jobs:UseProcessSpawning": true  → spawn ETL-SQL.exe run as child processes (production)
    // "Jobs:UseProcessSpawning": false → run in-process via ScriptExecutorAdapter (dev / fallback)
    builder.Services.Configure<ETL_SQL.Orchestrator.Execution.ProcessJobExecutorOptions>(
        cfg.GetSection("Jobs"));

    builder.Services.AddSingleton<ETL_SQL.Orchestrator.Execution.ChildProcessTracker>();

    var useProcessSpawning = cfg.GetValue<bool>("Jobs:UseProcessSpawning");
    if (useProcessSpawning)
    {
        builder.Services.AddTransient<IScriptExecutor,
            ETL_SQL.Orchestrator.Execution.ProcessJobExecutor>();
        Log.Information("Job execution mode: process spawning (ETL-SQL.exe run)");
    }
    else
    {
        builder.Services.AddTransient<IScriptExecutor, ScriptExecutorAdapter>();
        Log.Information("Job execution mode: in-process (ScriptExecutorAdapter)");
    }

    // Hosted service (starts/stops SchedulerService with the host)
    builder.Services.AddHostedService<OrchestratorHostedService>();

    var app = builder.Build();

    // ── Orphan PID cleanup on startup ────────────────────────────────────
    var tracker = app.Services.GetRequiredService<ETL_SQL.Orchestrator.Execution.ChildProcessTracker>();
    tracker.CleanupOrphans();

    // ── HTTP API endpoints ───────────────────────────────────────────────
    app.MapJobApi();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException && ex is not HostAbortedException)
{
    Log.Fatal(ex, "ETL-SQL Orchestrator Service terminated unexpectedly.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
