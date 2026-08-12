using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.BigQuery;
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
using ETL_SQL.Connectors.Snowflake;
using ETL_SQL.Connectors.Sqlite;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Orchestrator
{
    public static class DependencyInjectionExtensions
    {
        /// <summary>
        /// Maps the Governance:Secrets (fallback Secrets) configuration section to secret provider
        /// options. Shared with hosts (e.g. the Portal) that override the provider at
        /// resolve time but fall back to the factory-based default for non-host-specific kinds.
        /// </summary>
        public static SecretProviderOptions BuildSecretProviderOptions(IConfiguration configuration) => new()
        {
            Provider = configuration["Governance:Secrets:Provider"]
                ?? configuration["Secrets:Provider"]
                ?? "Environment",
            EnvironmentPrefix = configuration["Governance:Secrets:EnvironmentPrefix"]
                ?? configuration["Secrets:EnvironmentPrefix"],
            OsStoreRoot = configuration["Governance:Secrets:OsStoreRoot"]
                ?? configuration["Secrets:OsStoreRoot"],
            VaultEndpoint = configuration["Governance:Secrets:VaultEndpoint"]
                ?? configuration["Secrets:VaultEndpoint"],
            VaultBearerToken = configuration["Governance:Secrets:VaultBearerToken"]
                ?? configuration["Secrets:VaultBearerToken"]
        };

        /// <summary>Maps the Governance:ConnectionCatalog configuration section to catalog options.</summary>
        public static ConnectionCatalogOptions BuildConnectionCatalogOptions(IConfiguration configuration) => new()
        {
            Provider = configuration["Governance:ConnectionCatalog:Provider"],
            LocalRoot = configuration["Governance:ConnectionCatalog:LocalRoot"]
        };

        public static ToolCatalogOptions BuildToolCatalogOptions(IConfiguration configuration) => new()
        {
            Provider = configuration["Governance:Tools:Provider"],
            LocalRoot = configuration["Governance:Tools:LocalRoot"] ?? configuration["Governance:Tools:OsStoreRoot"]
        };

        public static IServiceCollection AddEtlSqlEngine(this IServiceCollection services, IConfiguration configuration)
        {
            // 1. Core Services
            services.AddSingleton<CliContext>(new CliContext());
            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager, DockerContainerManager>();
            services.AddSingleton<ISessionMetadataStoreFactory, SqliteSessionMetadataStoreFactory>();
            services.AddSingleton<ISecurityEventOutboxFactory, SqliteSecurityEventOutboxFactory>();
            services.AddSingleton<IGovernancePolicyRegistry>(_ => GovernancePolicyRegistry.CreateDefault());
            services.AddHostedService<EnterprisePolicyRefreshService>();
            services.AddSingleton<ISecretProvider>(_ =>
                new SecretProviderFactory(PolicyBoundHttp.CreateClient())
                    .Create(BuildSecretProviderOptions(configuration)));

            // Organization-designated sensitive connection metadata (design §6): the listed fields
            // become SECRET:-resolvable and masked in display/diagnostic surfaces process-wide.
            SecretResolvableFields.ConfigureOrganizationFields(
                (configuration["Governance:Secrets:SensitiveConnectionFields"] ?? string.Empty)
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var connectionCatalog = ConnectionCatalogProviderFactory.Create(BuildConnectionCatalogOptions(configuration));
            if (connectionCatalog != null)
                services.AddSingleton(connectionCatalog);

            var toolCatalog = ToolCatalogProviderFactory.Create(BuildToolCatalogOptions(configuration));
            if (toolCatalog != null)
                services.AddSingleton(toolCatalog);

            var fnRegistry = new ETL_SQL.Engine.Functions.FunctionRegistry();
            ETL_SQL.Engine.Functions.FileFunctions.Register(fnRegistry);
            ETL_SQL.Engine.Functions.StandardFunctions.Register(fnRegistry);
            ETL_SQL.Engine.Functions.JsonFunctions.Register(fnRegistry);
            ETL_SQL.Engine.Functions.XmlFunctions.Register(fnRegistry);
            ETL_SQL.Engine.Functions.FuzzyFunctions.Register(fnRegistry);
            services.AddSingleton<ETL_SQL.Core.Functions.IFunctionRegistry>(fnRegistry);

            var helpRegistry = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            services.AddSingleton<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry>(helpRegistry);

            services.AddSingleton<ISystemResources, DefaultSystemResources>();
            services.AddSingleton<IBufferManager, BufferManager>();
            services.AddSingleton<BufferManager>();
            services.Configure<BufferManagerOptions>(configuration.GetSection("Orchestration:ResourceManagement"));

            services.AddSingleton<ETL_SQL.Core.Execution.ISessionStateManager>(sp =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                var log = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
                var sec = sp.GetRequiredService<ETL_SQL.Services.SecurityService>();
                var metadataStoreFactory = sp.GetRequiredService<ISessionMetadataStoreFactory>();
                var customDir = cfg["Session:Root"];
                return new SessionStateManager(log, sec, cfg, metadataStoreFactory, customDir);
            });
            services.AddSingleton<SessionStateManager>(sp => (SessionStateManager)sp.GetRequiredService<ETL_SQL.Core.Execution.ISessionStateManager>());

            services.AddSingleton<ETL_SQL.Services.SecurityService>(sp =>
            {
                var log = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
                var sec = new ETL_SQL.Services.SecurityService(log);
                sec.UpdateFromConfiguration(configuration);
                return sec;
            });

            // 2. Connectors
            services.AddSingleton<IConnector, MockDbConnector>();
            services.AddSingleton<IConnector, SqlServerConnector>();
            services.AddSingleton<IConnector, OracleConnector>();
            services.AddSingleton<IConnector, PostgresConnector>();
            services.AddSingleton<IConnector, ETL_SQL.Connectors.MySql.MySqlConnector>();
            services.AddSingleton<IConnector, FlatFileConnector>();
            services.AddSingleton<IConnector, JsonConnector>();
            services.AddSingleton<IConnector, XmlConnector>();
            services.AddSingleton<IConnector, OdbcConnector>();
            services.AddSingleton<IConnector, RestConnector>();
            services.AddSingleton<IConnector, ExcelConnector>();
            services.AddSingleton<IConnector, DirectoryConnector>();
            services.AddSingleton<IConnector, ParquetConnector>();
            services.AddSingleton<IConnector, AvroConnector>();
            services.AddSingleton<IConnector, SmtpConnector>();
            services.AddSingleton<IConnector, ETL_SQL.Connectors.Webhook.WebhookConnector>();
            services.AddSingleton<IConnector, SnowflakeConnector>();
            services.AddSingleton<IConnector, BigQueryConnector>();
            services.AddSingleton<IConnector, PortalConnector>();
            services.AddSingleton<IConnector, OrchestratorConnector>();
            services.AddSingleton<IConnector, SharePointConnector>();
            services.AddSingleton<IConnector, ActiveDirectoryConnector>();
            services.AddSingleton<IConnector, SqliteConnector>();
            services.AddSingleton<IConnector, S3Connector>();
            services.AddSingleton<IConnector, MongodbConnector>();
            services.AddSingleton<IConnector, Neo4jConnector>();
            services.AddSingleton<IConnector, KafkaConnector>();

            services.AddSingleton<IConnector>(sp => new FtpConnector(
                configuration["Connectors:Ftp:Host"] ?? "localhost",
                configuration["Connectors:Ftp:Username"] ?? "anonymous",
                configuration["Connectors:Ftp:Password"] ?? ""));

            services.AddSingleton<IConnector>(sp => new SftpConnector(
                configuration["Connectors:Sftp:Host"] ?? "localhost",
                configuration["Connectors:Sftp:Username"] ?? "user",
                configuration["Connectors:Sftp:Password"] ?? "pass"));

            services.AddSingleton<IConnector>(sp => new AzureBlobConnector(
                configuration["Connectors:AzureBlob:ConnectionString"] ?? "UseDevelopmentStorage=true",
                configuration["Connectors:AzureBlob:Container"] ?? "test"));

            services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
            services.AddSingleton<ConnectionDiagnosticEngine>();

            // 3. Engine & Execution
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

            var handlerTypes = typeof(DeclareStatementHandler).Assembly.GetTypes()
                .Where(t => typeof(IStatementHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            foreach (var type in handlerTypes)
            {
                services.AddTransient(typeof(IStatementHandler), type);
                services.AddTransient(type);
            }

            services.AddTransient<IStatementHandler, ETL_SQL.ReportBuilder.ExportReportStatementHandler>();
            services.AddTransient<ETL_SQL.ReportBuilder.ExportReportStatementHandler>();

            // 4. Orchestration & Storage
            // Provider is config-selected (Orchestrator:Database:Provider, default Sqlite); the store
            // is built through the factory so SQLite and PostgreSQL share one registration.
            services.AddSingleton<IOrchestratorStoreFactory, OrchestratorStoreFactory>();
            services.AddSingleton(sp => (RelationalJobHistoryStore)sp
                .GetRequiredService<IOrchestratorStoreFactory>()
                .Create(string.IsNullOrWhiteSpace(configuration["Orchestrator:DatabasePath"])
                    ? null
                    : configuration["Orchestrator:DatabasePath"]));
            services.AddSingleton<IJobHistoryStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<IJobCatalogStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<IOrchestratorAuthorizationStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<IBundleStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<ILineageCatalogStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<INodeRegistryStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<IWriteEpochStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<IClusterLockStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<IHostMetricsStore>(sp => sp.GetRequiredService<RelationalJobHistoryStore>());
            services.AddSingleton<ISandboxAdmissionLedger>(sp => sp
                .GetRequiredService<IOrchestratorStoreFactory>()
                .CreateSandboxAdmissionLedger(string.IsNullOrWhiteSpace(configuration["Orchestrator:DatabasePath"])
                    ? null
                    : configuration["Orchestrator:DatabasePath"]));
            // Engine→Orchestrator seam for ASSERT JOB ... WITHIN ... OF HISTORICAL. Absent in
            // pure-engine/CLI hosts, where HISTORICAL predicates fail cleanly instead.
            services.AddSingleton<IJobMetricsProvider>(sp =>
                new JobHistoryMetricsProvider(
                    sp.GetRequiredService<IJobHistoryStore>(),
                    sp.GetRequiredService<IClusterLockStore>()));
            services.Configure<JobThrottleOptions>(configuration.GetSection("Orchestration:JobThrottle"));
            services.AddSingleton<INodeCapacityMonitor, NodeCapacityMonitor>();
            services.AddSingleton<JobThrottle>();
            services.AddSingleton<NotificationDispatchService>();
            services.AddSingleton<SchedulerService>();
            services.AddSingleton<IJobManager>(sp => sp.GetRequiredService<SchedulerService>());

            // IScriptExecutor — thin adapter used by SchedulerService for job execution
            services.AddTransient<IScriptExecutor, ScriptExecutorAdapter>();

            return services;
        }
    }
}
