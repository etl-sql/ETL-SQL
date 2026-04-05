using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using ETL_SQL.Core;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;
using TextDocumentSyncKind = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.TextDocumentSyncKind;
using SaveOptions = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.SaveOptions;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Postgres;
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
                    .ConfigureLogging(lb => lb.AddDebug().AddLanguageProtocolLogging().SetMinimumLevel(LogLevel.Trace))
                    .WithServices(services => {
                        var registry = new Data.ConnectorRegistry();
                        registry.Register(new MockDbConnector());
                        registry.Register(new FlatFileConnector());
                        registry.Register(new SqlServerConnector());
                        registry.Register(new PostgresConnector());
                        registry.Register(new OracleConnector());

                        services.AddSingleton<IConnectorRegistry>(registry);
                        services.AddSingleton<IMetadataManager, MetadataManager>();
                        services.AddSingleton<DocumentStateStore>();
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
                    .WithHandler<CustomMethodsHandler>()
                    .WithHandler<RefreshMetadataHandler>()
            );

            await server.WaitForExit;
        }
    }
}
