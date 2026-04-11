using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ETL_SQL.Core;
using ETL_SQL.Common;
using ETL_SQL.Engine.Handlers;
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
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Storage;
using ETL_SQL.Engine.Scheduling;

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
            Logger.Instance = loggerService;

            services.AddSingleton<LoggerService>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);

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
            
            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager, DockerContainerManager>();
            services.AddSingleton<ETL_SQL.Engine.Services.SessionStateManager>();
            services.AddSingleton<ETL_SQL.Services.SecurityService>();
            
            // Connectors
            services.AddSingleton<IConnector, MockDbConnector>();
            services.AddSingleton<IConnector, SqlServerConnector>();
            services.AddSingleton<IConnector, OracleConnector>();
            services.AddSingleton<IConnector, PostgresConnector>();
            services.AddSingleton<IConnector, FlatFileConnector>();
            services.AddSingleton<IConnector, JsonConnector>();
            services.AddSingleton<IConnector, XmlConnector>();
            services.AddSingleton<IConnector, ExcelConnector>();
            services.AddSingleton<IConnector, DirectoryConnector>();
            services.AddSingleton<IConnector, ParquetConnector>();
            services.AddSingleton<IConnector, AvroConnector>();
            services.AddSingleton<IConnector, SmtpConnector>();
            
            // Options-based remote connectors
            var ftpHost = configuration["Connectors:Ftp:Host"] ?? "localhost";
            var ftpUser = configuration["Connectors:Ftp:Username"] ?? "anonymous";
            var ftpPass = configuration["Connectors:Ftp:Password"] ?? "";
            
            var sftpHost = configuration["Connectors:Sftp:Host"] ?? "localhost";
            var sftpUser = configuration["Connectors:Sftp:Username"] ?? "user";
            var sftpPass = configuration["Connectors:Sftp:Password"] ?? "pass";
            
            var azureConn = configuration["Connectors:AzureBlob:ConnectionString"] ?? "UseDevelopmentStorage=true";
            var azureContainer = configuration["Connectors:AzureBlob:Container"] ?? "test";

            services.AddSingleton<IConnector>(new FtpConnector(ftpHost, ftpUser, ftpPass));
            services.AddSingleton<IConnector>(new SftpConnector(sftpHost, sftpUser, sftpPass));
            services.AddSingleton<IConnector>(new AzureBlobConnector(azureConn, azureContainer));

            services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
            services.AddTransient<ExecutionSession>();
            services.AddTransient<Evaluator>();
            services.AddTransient<IExecutionContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<IVariableContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<IQueryContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<ILineageContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<ISqlCompilerContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<ITransactionContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<IDockerContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<ILoggingContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<IEvaluationContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<IDataContext>(sp => sp.GetRequiredService<Evaluator>());
            services.AddTransient<IEngineContext>(sp => sp.GetRequiredService<Evaluator>());
            
            // Storage & Scheduling
            services.AddSingleton<IJobHistoryStore, SQLiteJobHistoryStore>();
            services.AddSingleton<SchedulerService>();

            // Register Handlers
            var handlerAssembly = typeof(DeclareStatementHandler).Assembly;
            var handlerTypes = handlerAssembly.GetTypes()
                .Where(t => typeof(IStatementHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            
            foreach (var type in handlerTypes)
            {
                services.AddTransient(typeof(IStatementHandler), type);
                services.AddTransient(type);
            }

            return services.BuildServiceProvider();
        }
    }
}
