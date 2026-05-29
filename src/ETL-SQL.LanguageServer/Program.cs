using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using ETL_SQL.Core;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Services;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;
using TextDocumentSyncKind = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.TextDocumentSyncKind;
using SaveOptions = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.SaveOptions;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.MySql;
using ETL_SQL.Connectors.Oracle;

namespace ETL_SQL.LSP
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var server = await LanguageServer.From(options =>
                options
                    .WithInput(Console.OpenStandardInput())
                    .WithOutput(Console.OpenStandardOutput())
                    .ConfigureLogging(lb => lb.AddDebug().AddLanguageProtocolLogging().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace))
                    .WithServices(services => {
                        var configuration = new ConfigurationBuilder()
                            .SetBasePath(AppContext.BaseDirectory)
                            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                            .Build();
                        services.AddSingleton<IConfiguration>(configuration);

                        var registry = new Data.ConnectorRegistry();
                        registry.Register(new MockDbConnector());
                        registry.Register(new FlatFileConnector());
                        registry.Register(new SqlServerConnector());
                        registry.Register(new PostgresConnector());
                        registry.Register(new ETL_SQL.Connectors.MySql.MySqlConnector());
                        registry.Register(new OracleConnector());
                        registry.Register(new ETL_SQL.Connectors.Parquet.ParquetConnector());
                        registry.Register(new ETL_SQL.Connectors.Avro.AvroConnector());
                        registry.Register(new ETL_SQL.Connectors.Json.JsonConnector());
                        registry.Register(new ETL_SQL.Connectors.Xml.XmlConnector());
                        registry.Register(new ETL_SQL.Connectors.Excel.ExcelConnector());
                        registry.Register(new ETL_SQL.Connectors.Odbc.OdbcConnector());
                        registry.Register(new ETL_SQL.Connectors.Rest.RestConnector());
                        registry.Register(new ETL_SQL.Connectors.Email.SmtpConnector());
                        registry.Register(new ETL_SQL.Connectors.SftpConnector());
                        registry.Register(new ETL_SQL.Connectors.AzureBlobConnector());
                        registry.Register(new ETL_SQL.Connectors.FtpConnector());
                        registry.Register(new ETL_SQL.Connectors.Directory.DirectoryConnector());
                        registry.Register(new ETL_SQL.Connectors.SharePointConnector());
                        registry.Register(new ETL_SQL.Connectors.ActiveDirectoryConnector());
                        registry.Register(new ETL_SQL.Connectors.Sqlite.SqliteConnector());
                        registry.Register(new ETL_SQL.Connectors.S3.S3Connector());
                        registry.Register(new ETL_SQL.Connectors.Mongodb.MongodbConnector());
                        registry.Register(new ETL_SQL.Connectors.Kafka.KafkaConnector());

                        var functionRegistry = new Engine.Functions.FunctionRegistry();
                        Engine.Functions.StandardFunctions.Register(functionRegistry);
                        Engine.Functions.FileFunctions.Register(functionRegistry);
                        Engine.Functions.JsonFunctions.Register(functionRegistry);
                        Engine.Functions.XmlFunctions.Register(functionRegistry);
                        Engine.Functions.RegexFunctions.Register(functionRegistry);
                        Engine.Functions.LineageFunctions.Register(functionRegistry);
                        Engine.Functions.FuzzyFunctions.Register(functionRegistry);

                        services.AddSingleton<IConnectorRegistry>(registry);
                        var helpRegistry = new Core.Metadata.LanguageHelpRegistry();
                        Engine.Services.LanguageHelpService.Initialize(helpRegistry);
                        services.AddSingleton<Core.Interfaces.ILanguageHelpRegistry>(helpRegistry);
                        services.AddSingleton<IFunctionRegistry>(functionRegistry);
                        services.AddSingleton<IMetadataManager, MetadataManager>();
                        services.AddSingleton<ILanguageService, LanguageService>();
                        services.AddSingleton<DocumentStateStore>();
                        services.AddSingleton<TextDocumentHandler>();
                        
                        // Engine Services
                        services.AddSingleton<Common.ILogger>(Common.NullLogger.Instance);
                        services.AddSingleton<ILineageTracker, LineageTracker>();
                        services.AddSingleton<IDockerManager, DockerContainerManager>();
                        services.AddSingleton<ISessionStateManager, Engine.Services.SessionStateManager>();
                        services.AddSingleton<Engine.Services.SessionStateManager>(sp => (Engine.Services.SessionStateManager)sp.GetRequiredService<ISessionStateManager>());
                        services.AddSingleton<Services.SecurityService>(sp =>
                        {
                            var sec = new Services.SecurityService(sp.GetRequiredService<Common.ILogger>());
                            sec.UpdateFromConfiguration(configuration);
                            return sec;
                        });
                        services.AddSingleton<ISystemResources, DefaultSystemResources>();
                        services.AddSingleton<IBufferManager, ETL_SQL.Orchestrator.Execution.BufferManager>();
                        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new BufferManagerOptions()));
                        services.AddSingleton<ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore>();
                        services.AddSingleton<IJobHistoryStore>(sp => sp.GetRequiredService<ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore>());
                        services.AddSingleton<IBundleStore>(sp => sp.GetRequiredService<ETL_SQL.Orchestrator.Storage.SQLiteJobHistoryStore>());
                        
                        services.AddTransient<IReportContext, Engine.Services.ReportRegistry>();
                        services.AddTransient<Engine.Services.EvaluatorComponentRegistry>();
                        services.AddTransient<Engine.Evaluator>();

                        // Handlers
                        var handlerAssemblies = new[]
                        {
                            typeof(Engine.Handlers.DeclareStatementHandler).Assembly,
                            typeof(ReportBuilder.ExportReportStatementHandler).Assembly
                        };

                        foreach (var asm in handlerAssemblies)
                        {
                            foreach (var type in asm.GetTypes()
                                .Where(t => typeof(IStatementHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
                            {
                                services.AddTransient(typeof(IStatementHandler), type);
                                services.AddTransient(type);
                            }
                        }

                        services.AddSingleton<DatasetStore>();
                        services.AddSingleton<CustomMethodsHandler>();
                        services.AddSingleton<RefreshMetadataHandler>();
                        services.AddSingleton<UpdateNotebookContextHandler>();
                    })
                    .OnStarted((server, ct) => {
                        server.Configuration.AddConfigurationItem(new ConfigurationItem { Section = "etlsql" });
                        return Task.CompletedTask;
                    })
                    .WithHandler<TextDocumentHandler>()
                    .WithHandler<HoverProvider>()
                    .WithHandler<DefinitionProvider>()
                    .WithHandler<CompletionProvider>()
                    .WithHandler<SignatureHelpProvider>()
                    .WithHandler<FormattingProvider>()
                    .WithHandler<DocumentSymbolProvider>()
                    .WithHandler<CustomMethodsHandler>()
                    .WithHandler<RefreshMetadataHandler>()
                    .WithHandler<UpdateNotebookContextHandler>()
            );

            await server.WaitForExit;
        }
    }
}
