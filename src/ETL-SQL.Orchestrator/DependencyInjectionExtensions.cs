using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Common;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
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
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.Snowflake;
using ETL_SQL.Connectors.BigQuery;
using ETL_SQL.Connectors;

namespace ETL_SQL.Orchestrator
{
    public static class DependencyInjectionExtensions
    {
        public static IServiceCollection AddEtlSqlEngine(this IServiceCollection services, IConfiguration configuration)
        {
            // 1. Core Services
            services.AddSingleton<CliContext>(new CliContext());
            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager, DockerContainerManager>();
            
            var fnRegistry = new ETL_SQL.Engine.Functions.FunctionRegistry();
            ETL_SQL.Engine.Functions.FileFunctions.Register(fnRegistry);
            ETL_SQL.Engine.Functions.StandardFunctions.Register(fnRegistry);
            ETL_SQL.Engine.Functions.JsonFunctions.Register(fnRegistry);
            ETL_SQL.Engine.Functions.XmlFunctions.Register(fnRegistry);
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
                var customDir = cfg["Session:Root"];
                return new SessionStateManager(log, sec, cfg, customDir);
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
            services.AddSingleton<IConnector, SnowflakeConnector>();
            services.AddSingleton<IConnector, BigQueryConnector>();
            
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
                configuration["Connectors:AzureBlob:Container"]       ?? "test"));

            services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();

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

            // 4. Orchestration & Storage
            services.AddSingleton<IJobHistoryStore, SQLiteJobHistoryStore>();
            services.Configure<JobThrottleOptions>(configuration.GetSection("Orchestration:JobThrottle"));
            services.AddSingleton<JobThrottle>();
            services.AddSingleton<SchedulerService>();
            services.AddSingleton<IJobManager>(sp => sp.GetRequiredService<SchedulerService>());

            // IScriptExecutor — thin adapter used by SchedulerService for job execution
            services.AddTransient<IScriptExecutor, ScriptExecutorAdapter>();

            return services;
        }
    }
}
