using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Directory;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;



namespace ETL_SQL.App
{
    public static class DependencyInjectionSetup
    {
        public static IServiceProvider BuildServiceProvider(Dictionary<string, string?>? configOverrides = null)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();
            if (configOverrides != null)
                builder.AddInMemoryCollection(configOverrides);
            builder.AddEnterprisePolicy();
            var configuration = builder.Build();

            ETL_SQL.Core.Metadata.SnippetLibrary.Initialize(
                configuration["Snippets:UserSnippetsPath"]);

            var services = new ServiceCollection();

            services.AddSingleton<IConfiguration>(configuration);

            // ── Logging via LoggerService ──────────────────────────────────────
            string appLogDir = configuration["Logging:AppLog:Directory"] ?? "logs/app";
            int retentionDays = int.TryParse(configuration["Logging:AppLog:RetentionDays"], out var rd) ? rd : 30;
            int sizeLimitMb = int.TryParse(configuration["Logging:AppLog:FileSizeLimitMb"], out var sl) ? sl : 10;

            var loggerService = new LoggerService();
            loggerService.InitializeAppLogger(appLogDir, retentionDays, sizeLimitMb);

            services.AddSingleton<LoggerService>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);

            services.AddLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddSerilog(dispose: false);
                lb.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            });

            // ── Core Engine via Extension Method ───────────────────────────────
            services.AddEtlSqlEngine(configuration);

            // ── Overrides / Post-Initialization ───────────────────────────────
            // (If we need to force TestMode or other runtime flags on already registered services)

            return services.BuildServiceProvider();
        }
    }
}
