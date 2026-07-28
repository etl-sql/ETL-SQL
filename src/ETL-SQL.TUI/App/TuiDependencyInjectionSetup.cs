using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Directory;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.Kafka;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.Mongodb;
using ETL_SQL.Connectors.MySql;
using ETL_SQL.Connectors.Neo4j;
using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Connectors.S3;
using ETL_SQL.Connectors.Sqlite;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace ETL_SQL.TUI
{
    public static class TuiDependencyInjectionSetup
    {
        /// <summary>Composition root for the editor: resolves its dependencies from the provider.</summary>
        public static UI.ConsoleEditor CreateEditor(IServiceProvider sp, string filePath,
            System.Collections.Generic.Dictionary<string, ETL_SQL.Data.IDataSource> connections)
        {
            return new UI.ConsoleEditor(
                filePath,
                connections,
                sp.GetRequiredService<ETL_SQL.Common.ILogger>(),
                sp.GetRequiredService<Services.IClipboardService>(),
                sp.GetRequiredService<ETL_SQL.Engine.Evaluator>(),
                sp.GetRequiredService<ETL_SQL.Core.Services.ILanguageService>(),
                sp.GetService<ETL_SQL.Core.Functions.IFunctionRegistry>(),
                sp.GetService<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry>());
        }

        public static IServiceProvider BuildServiceProvider()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddEnterprisePolicy()
                .Build();

            ETL_SQL.Core.Metadata.SnippetLibrary.Initialize(
                configuration["Snippets:UserSnippetsPath"]);

            var services = new ServiceCollection();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<CliContext>(new CliContext());

            // ── Logging ────────────────────────────────────────────────────────
            string appLogDir = configuration["Logging:AppLog:Directory"] ?? "logs/app";
            int retentionDays = int.TryParse(configuration["Logging:AppLog:RetentionDays"], out var rd) ? rd : 30;
            int sizeLimitMb = int.TryParse(configuration["Logging:AppLog:FileSizeLimitMb"], out var sl) ? sl : 10;

            var loggerService = new LoggerService();
            loggerService.InitializeAppLogger(appLogDir, retentionDays, sizeLimitMb);

            services.AddSingleton<LoggerService>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);

            services.AddLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddSerilog(dispose: false);
                lb.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            });

            // ── Engine core ────────────────────────────────────────────────────
            var registry = new ETL_SQL.Engine.Functions.FunctionRegistry();
            ETL_SQL.Engine.Functions.FileFunctions.Register(registry);
            ETL_SQL.Engine.Functions.StandardFunctions.Register(registry);
            ETL_SQL.Engine.Functions.JsonFunctions.Register(registry);
            ETL_SQL.Engine.Functions.XmlFunctions.Register(registry);
            ETL_SQL.Engine.Functions.FuzzyFunctions.Register(registry);
            services.AddSingleton<Core.Functions.IFunctionRegistry>(registry);

            var helpRegistry = new Core.Metadata.LanguageHelpRegistry();
            Engine.Services.LanguageHelpService.Initialize(helpRegistry);
            services.AddSingleton<Core.Interfaces.ILanguageHelpRegistry>(helpRegistry);

            // ── Language Intelligence ──────────────────────────────────────────
            services.AddSingleton<Core.IMetadataManager, Core.Services.MetadataManager>();
            services.AddSingleton<Core.Services.ILanguageService, Core.Services.LanguageService>();

            services.AddTransient<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager, DockerContainerManager>();
            services.AddSingleton<ISessionMetadataStoreFactory, SqliteSessionMetadataStoreFactory>();
            services.AddSingleton<ISecurityEventOutboxFactory, SqliteSecurityEventOutboxFactory>();
            services.AddSingleton<ETL_SQL.Core.Execution.ISessionStateManager, ETL_SQL.Engine.Services.SessionStateManager>();
            services.AddSingleton<ETL_SQL.Engine.Services.SessionStateManager>(sp => (ETL_SQL.Engine.Services.SessionStateManager)sp.GetRequiredService<ETL_SQL.Core.Execution.ISessionStateManager>());
            var securityService = new ETL_SQL.Services.SecurityService(loggerService);
            securityService.UpdateFromConfiguration(configuration);
            services.AddSingleton<ETL_SQL.Services.SecurityService>(securityService);
            services.AddSingleton<ETL_SQL.TUI.Services.IClipboardService, ETL_SQL.TUI.Services.ClipboardService>();
            services.AddSingleton<ISystemResources, DefaultSystemResources>();
            services.AddSingleton<ETL_SQL.Core.Execution.IBufferManager, BufferManager>();
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new BufferManagerOptions()));
            services.AddTransient<ETL_SQL.Engine.Services.EvaluatorComponentRegistry>();

            // ── Connectors ─────────────────────────────────────────────────────
            services.AddSingleton<IConnector, MockDbConnector>();
            services.AddSingleton<IConnector, SqlServerConnector>();
            services.AddSingleton<IConnector, OracleConnector>();
            services.AddSingleton<IConnector, PostgresConnector>();
            services.AddSingleton<IConnector, ETL_SQL.Connectors.MySql.MySqlConnector>();
            services.AddSingleton<IConnector, FlatFileConnector>();
            services.AddSingleton<IConnector, JsonConnector>();
            services.AddSingleton<IConnector, XmlConnector>();
            services.AddSingleton<IConnector, ExcelConnector>();
            services.AddSingleton<IConnector, DirectoryConnector>();
            services.AddSingleton<IConnector, ParquetConnector>();
            services.AddSingleton<IConnector, AvroConnector>();
            services.AddSingleton<IConnector, SmtpConnector>();
            services.AddSingleton<IConnector, ETL_SQL.Connectors.Webhook.WebhookConnector>();
            services.AddSingleton<IConnector, RestConnector>();
            services.AddSingleton<IConnector, OdbcConnector>();
            services.AddSingleton<IConnector, PortalConnector>();
            services.AddSingleton<IConnector, OrchestratorConnector>();

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
            services.AddSingleton<IConnector, SharePointConnector>();
            services.AddSingleton<IConnector, ActiveDirectoryConnector>();
            services.AddSingleton<IConnector, SqliteConnector>();
            services.AddSingleton<IConnector, S3Connector>();
            services.AddSingleton<IConnector, MongodbConnector>();
            services.AddSingleton<IConnector, Neo4jConnector>();
            services.AddSingleton<IConnector, KafkaConnector>();
            services.AddSingleton<IConnector, ETL_SQL.Connectors.BigQuery.BigQueryConnector>();
            services.AddSingleton<IConnector, ETL_SQL.Connectors.Snowflake.SnowflakeConnector>();
            services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();

            // ── Evaluator + execution ──────────────────────────────────────────
            services.AddTransient<ExecutionSession>();
            services.AddTransient<Evaluator>();
            services.AddTransient<IExecutionContext>(sp => (IExecutionContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<IVariableContext>(sp => (IVariableContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<IQueryContext>(sp => (IQueryContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<ILineageContext>(sp => (ILineageContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<ISqlCompilerContext>(sp => (ISqlCompilerContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<ITransactionContext>(sp => (ITransactionContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<IDockerContext>(sp => (IDockerContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<ILoggingContext>(sp => (ILoggingContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<IEvaluationContext>(sp => (IEvaluationContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<IDataContext>(sp => (IDataContext)sp.GetRequiredService<Evaluator>());
            services.AddTransient<IEngineContext>(sp => (IEngineContext)sp.GetRequiredService<Evaluator>());

            // ── Storage & Scheduling ───────────────────────────────────────────
            services.Configure<JobThrottleOptions>(configuration.GetSection("Scheduling:Throttle"));
            services.AddSingleton<JobThrottle>();

            services.Configure<BufferManagerOptions>(configuration.GetSection("Orchestration:ResourceManagement"));
            services.AddSingleton<BufferManager>();
            services.AddSingleton<IBufferManager>(sp => sp.GetRequiredService<BufferManager>());

            services.AddSingleton<SQLiteJobHistoryStore>();
            services.AddSingleton<IJobHistoryStore>(sp => sp.GetRequiredService<SQLiteJobHistoryStore>());
            services.AddSingleton<IJobCatalogStore>(sp => sp.GetRequiredService<SQLiteJobHistoryStore>());
            services.AddSingleton<IBundleStore>(sp => sp.GetRequiredService<SQLiteJobHistoryStore>());
            services.AddSingleton<ILineageCatalogStore>(sp => sp.GetRequiredService<SQLiteJobHistoryStore>());
            services.AddSingleton<IHostMetricsStore>(sp => sp.GetRequiredService<SQLiteJobHistoryStore>());
            services.AddSingleton<SchedulerService>();
            services.AddTransient<IScriptExecutor, ScriptExecutorAdapter>();

            // ── Statement handlers ─────────────────────────────────────────────
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
