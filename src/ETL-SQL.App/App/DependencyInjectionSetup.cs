using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Common;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.Directory;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Services;
using ETL_SQL.Connectors.Shared;



namespace ETL_SQL.App
{
    public static class DependencyInjectionSetup
    {
        public static IServiceProvider BuildServiceProvider()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var services = new ServiceCollection();
            
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<CliContext>(new CliContext());

            // ── Logging via LoggerService ──────────────────────────────────────
            // Read config values (fall back to sensible defaults)
            string appLogDir     = configuration["Logging:AppLog:Directory"]     ?? "logs/app";
            int    retentionDays = int.TryParse(configuration["Logging:AppLog:RetentionDays"],    out var rd) ? rd : 30;
            int    sizeLimitMb   = int.TryParse(configuration["Logging:AppLog:FileSizeLimitMb"],  out var sl) ? sl : 10;

            var loggerService = new LoggerService();
            loggerService.InitializeAppLogger(appLogDir, retentionDays, sizeLimitMb);
            
            // Set as global façade instance
            services.AddSingleton<LoggerService>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);

            // MEL bridge for ILogger<T> consumers
            services.AddLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddSerilog(dispose: false); // Serilog is already configured in LoggerService
                lb.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            });

            // Core Engine
            var registry = new ETL_SQL.Engine.Functions.FunctionRegistry();
            ETL_SQL.Engine.Functions.FileFunctions.Register(registry);
            ETL_SQL.Engine.Functions.StandardFunctions.Register(registry);
            ETL_SQL.Engine.Functions.JsonFunctions.Register(registry);
            ETL_SQL.Engine.Functions.XmlFunctions.Register(registry);
            services.AddSingleton<Core.Functions.IFunctionRegistry>(registry);
            
            var helpRegistry = new Core.Metadata.LanguageHelpRegistry();
            services.AddSingleton<Core.Interfaces.ILanguageHelpRegistry>(helpRegistry);
            
            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager, DockerContainerManager>();
            services.AddSingleton<ETL_SQL.Engine.Services.SessionStateManager>();
            
            var securityService = new ETL_SQL.Services.SecurityService(loggerService);
            // Automatically enable TestMode if we are running in a Unit Test context
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName?.Contains("xunit") == true || a.FullName?.Contains("Test") == true))
            {
                securityService.IsTestMode = true;
            }

            // Centralized loading of Security section (Hosts, Safe Zones, Env Vars, and runaway guards)
            securityService.UpdateFromConfiguration(configuration);

            services.AddSingleton<ETL_SQL.Services.SecurityService>(securityService);

            // ── Connector Retry Policy (CFG-9) ──────────────────────────────────
            var retryOptions = new ConnectorRetryOptions
            {
                MaxAttempts = int.TryParse(configuration["Connectors:Retry:MaxAttempts"], out var rma) ? rma : 3,
                BaseDelaySeconds = double.TryParse(configuration["Connectors:Retry:BaseDelaySeconds"], out var rbd) ? rbd : 1.0
            };
            ConnectorRetryPolicy.Initialize(retryOptions);


            
            services.AddSingleton<IConnectorRegistry>(sp => {
                var connectors = sp.GetServices<IConnector>();
                return new ConnectorRegistry(connectors);
            });
            services.AddSingleton<ISystemResources, DefaultSystemResources>();
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new BufferManagerOptions()));
            
            services.AddEtlSqlEngine(configuration);

            // Linter & Security Rules

            return services.BuildServiceProvider();
        }
    }
}
