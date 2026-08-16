using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Directory;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

#if WINDOWS
// Running as a Windows Service, the working directory defaults to System32, which sends relative
// paths (Serilog file logs, SQLite job store) there. Anchor it to the executable folder before the
// bootstrap logger opens its file sink so all logs land in the install folder.
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    System.IO.Directory.SetCurrentDirectory(System.AppContext.BaseDirectory);
}
#endif

// ── Serilog bootstrap logger (captures startup errors before host is ready) ──
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/orchestrator-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] [{SessionId: [sid=]:l}]{Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("ETL-SQL Orchestrator Service starting up.");

    ETL_SQL.Core.Governance.SecurityEventRuntime.ConfigureLocalOutboxFactory(
        new ETL_SQL.Core.Governance.SqliteSecurityEventOutboxFactory());
    await ETL_SQL.Core.Governance.EnterprisePolicyRuntime.InitializeFromMachineAsync();
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
#if WINDOWS
    builder.Host.UseWindowsService(opts => opts.ServiceName = "ETL-SQL Orchestrator");
#else
    builder.Host.UseSystemd();
#endif

    // ── Configuration ────────────────────────────────────────────────────
    builder.Configuration
        .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddSecureConfiguration()
        .AddEnterprisePolicy();

    var cfg = builder.Configuration;

    // ── Engine services (centralized in Orchestrator extension) ─────────
    var loggerService = new LoggerService();
    loggerService.InitializeAppLogger(
        cfg["Logging:AppLog:Directory"] ?? "logs/orchestrator",
        int.TryParse(cfg["Logging:AppLog:RetentionDays"], out var rd) ? rd : 30,
        int.TryParse(cfg["Logging:AppLog:FileSizeLimitMb"], out var sl) ? sl : 10);

    builder.Services.AddSingleton<LoggerService>(loggerService);
    builder.Services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
    builder.Services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);

    builder.Services.AddEtlSqlEngine(cfg);
    builder.Services.AddSandboxAdmissionHosting(cfg);
    builder.Services.AddHardenedSandboxExecution(cfg);
    builder.Services.AddSingleton<OrchestratorObjectAuthorizationService>();
    builder.Services.AddSingleton<IOrchestratorObjectAuthorizer>(sp =>
        sp.GetRequiredService<OrchestratorObjectAuthorizationService>());

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

    // Cluster node heartbeat (P1.7): register this daemon in the shared node registry.
    builder.Services.AddNodeHeartbeat("Orchestrator");

    builder.WebHost.ConfigureKestrel(options =>
    {
        var kestrelSection = builder.Configuration.GetSection("Kestrel");
        if (kestrelSection.Exists())
        {
            options.Configure(kestrelSection);
        }
    });

    var app = builder.Build();

    // ── Security guard: never serve the ad-hoc job API unauthenticated on a ──
    //    network-reachable address. Fails fast when no API key is configured
    //    and the service is bound to a non-loopback endpoint.
    OrchestratorStartup.ValidateApiKeyBinding(cfg);

    // ── Authorization mode, said out loud ────────────────────────────────
    //    Legacy mode is a supported Solo configuration and not a startup failure, but it is normally
    //    inferred from the bind address rather than chosen, so a shared deployment can land in it
    //    without anyone deciding to. Reported either way; warned about when the deployment does not
    //    look Solo.
    var authorizationMode = OrchestratorAuthorizationMode.Resolve(cfg);
    if (authorizationMode.RequiresOperatorAttention)
        Log.Warning("{AuthorizationMode}", authorizationMode.Describe());
    else
        Log.Information("{AuthorizationMode}", authorizationMode.Describe());

    // ── Orphan PID cleanup on startup ────────────────────────────────────
    var tracker = app.Services.GetRequiredService<ETL_SQL.Orchestrator.Execution.ChildProcessTracker>();
    tracker.CleanupOrphans();

    app.Use(async (context, next) =>
    {
        var correlationId = context.TraceIdentifier;
        var traceId = Activity.Current?.TraceId.ToString();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.TryAdd("X-Correlation-ID", correlationId);
            return Task.CompletedTask;
        });

        using (app.Logger.BeginScope(new Dictionary<string, object?>
        {
            [ETL_SQL.Core.Observability.ObservabilityConventions.Tags.CorrelationId] = correlationId,
            ["trace_id"] = traceId
        }))
        {
            await next();
        }
    });

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
