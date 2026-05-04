using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.LSP;
using ETL_SQL.Connectors.MockDb;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using DocumentUri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri;
using Xunit.Abstractions;
using ETL_SQL.Core.Services;
using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.LanguageServer.Tests
{
    public class SystemIntegrationTests
    {
        private readonly ITestOutputHelper _output;
        public SystemIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Completion_Should_Resolve_Aliases_And_Expand_Columns()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            
            var connectorRegistry = new ETL_SQL.Data.ConnectorRegistry();
            // IMPORTANT: Register the connector manually as Program.cs does
            connectorRegistry.Register(new MockDbConnector());
            var functionRegistry = new Engine.Functions.FunctionRegistry();

            var metadataManager = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, connectorRegistry);
            var helpRegistry = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            var languageService = new LanguageService(metadataManager, helpRegistry);
            var store = new DocumentStateStore();
            var handler = new TextDocumentHandler(loggerFactory, metadataManager, store);
            var completionProvider = new CompletionProvider(loggerFactory.CreateLogger<CompletionProvider>(), store, languageService);
            var hoverHandler = new HoverProvider(loggerFactory.CreateLogger<HoverProvider>(), store, functionRegistry, helpRegistry);
            
            var uri = DocumentUri.From("untitled:Untitled-1");
            var normalizedUri = uri.ToString(); 

            // 1. Analyze script with alias
            var script = "CREATE CONNECTION m ON MOCKDB();\r\nSELECT u. FROM m.Users AS u;";
            _output.WriteLine("Analyzing script...");
            await handler.AnalyzeAsync(uri, script);

            // DIAGNOSTIC: Check if connection is registered
            var conns = metadataManager.GetConnections(normalizedUri);
            _output.WriteLine($"Connections found: {string.Join(", ", conns.Select(c => c.Name))}");
            Assert.Contains(conns, c => string.Equals(c.Name, "m", StringComparison.OrdinalIgnoreCase));

            // 2. Request completion at u. (line 1, col 9)
            var completionParams = new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(1, 9),
                Context = new CompletionContext { TriggerKind = CompletionTriggerKind.TriggerCharacter, TriggerCharacter = "." }
            };

            // Act
            _output.WriteLine("Requesting completion at line 1, col 9...");
            var list = await completionProvider.Handle(completionParams, CancellationToken.None);
            
            _output.WriteLine($"Completion items returned: {list.Count()}");
            foreach (var item in list) _output.WriteLine($" - {item.Label} ({item.Detail})");

            // Assert: Should see columns from Users (UserID, UserName, Email)
            Assert.Contains(list, i => i.Label == "u.UserID");
            script = "CREATE CONNECTION m ON MOCKDB();\r\nSELECT u.* FROM m.Users AS u;";
            await handler.AnalyzeAsync(uri, script);
            
            completionParams = new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(1, 10),
                Context = new CompletionContext { TriggerKind = CompletionTriggerKind.TriggerCharacter, TriggerCharacter = "." }
            };
            list = await completionProvider.Handle(completionParams, CancellationToken.None);
            
             var expandItem = list.FirstOrDefault(i => i.Label == "Expand columns");
             Assert.NotNull(expandItem);
             _output.WriteLine($"Expand columns InsertText: {expandItem.InsertText}");
             
             // Verify it contains the aliased columns
             Assert.Contains("u.UserID, u.UserName, u.Email", expandItem.InsertText);

            // 4. Test expansion WITHOUT alias: SELECT * FROM m.Users;
            script = "CREATE CONNECTION m ON MOCKDB();\r\nSELECT * FROM m.Users;";
            await handler.AnalyzeAsync(uri, script);
            
            completionParams = new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(1, 8), // After *
                Context = new CompletionContext { TriggerKind = CompletionTriggerKind.TriggerCharacter, TriggerCharacter = " " }
            };
            list = await completionProvider.Handle(completionParams, CancellationToken.None);
            
            expandItem = list.FirstOrDefault(i => i.Label == "Expand columns");
            Assert.NotNull(expandItem);
            _output.WriteLine($"Expand columns (no alias) InsertText: {expandItem.InsertText}");
            
            // Should expand WITHOUT alias prefix if not aliased and only 1 table.
            Assert.Contains("m.Users.UserID", expandItem.InsertText);
        }
        [Fact]
        public async Task Variable_Completion_Should_Include_Loop_Variables()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            
            var connectorRegistry = new ETL_SQL.Data.ConnectorRegistry();
            var metadataManager = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, connectorRegistry);
            var helpRegistry = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            var languageService = new LanguageService(metadataManager, helpRegistry);
            var store = new DocumentStateStore();
            var handler = new TextDocumentHandler(loggerFactory, metadataManager, store);
            var completionProvider = new CompletionProvider(loggerFactory.CreateLogger<CompletionProvider>(), store, languageService);
            
            var uri = DocumentUri.From("untitled:Untitled-2");
            
            // Script with nested loops and declarations
            var script = "DECLARE @global_var INT = 100;\r\nFOR @i = 1 TO 10\r\nBEGIN\r\n    PRINT @i;\r\n    FOREACH @item IN [1, 2, 3]\r\n    BEGIN\r\n        PRINT @item;\r\n    END\r\nEND";
            await handler.AnalyzeAsync(uri, script);

            // 1. Completion at line 3 (inside FOR loop)
            var completionParams = new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(3, 10), // Inside PRINT @i;
                Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
            };

            // Act
            var list = await completionProvider.Handle(completionParams, CancellationToken.None);
            
            // Assert: Should see @global_var and @i, but NOT @item
            Assert.Contains(list, i => i.Label == "@global_var");
            Assert.Contains(list, i => i.Label == "@i");
            Assert.DoesNotContain(list, i => i.Label == "@item");

            // 2. Completion at line 6 (inside FOREACH loop)
            completionParams = new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(6, 14), // Inside PRINT @item;
                Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
            };

            list = await completionProvider.Handle(completionParams, CancellationToken.None);
            
            // Assert: Should see @global_var, @i, AND @item
            Assert.Contains(list, i => i.Label == "@global_var");
            Assert.Contains(list, i => i.Label == "@i");
            Assert.Contains(list, i => i.Label == "@item");
        }
        
        [Fact]
        public async Task Hover_Should_Return_Keyword_Help()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            
            var connectorRegistry = new ETL_SQL.Data.ConnectorRegistry();
            var functionRegistry = new Engine.Functions.FunctionRegistry();
            var metadataManager = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, connectorRegistry);
            var store = new DocumentStateStore();
            var helpRegistry = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            ETL_SQL.Engine.Services.LanguageHelpService.Initialize(helpRegistry);
            
            var handler = new TextDocumentHandler(loggerFactory, metadataManager, store);
            var hoverProvider = new HoverProvider(loggerFactory.CreateLogger<HoverProvider>(), store, functionRegistry, helpRegistry);
            
            var uri = DocumentUri.From("untitled:Untitled-3");
            var script = "SELECT * FROM CONNECTION MSSQL;";
            await handler.AnalyzeAsync(uri, script);

            // 1. Hover over CONNECTION (line 0, col 14)
            var hoverParams = new HoverParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(0, 14)
            };

            // Act
            var hover = await hoverProvider.Handle(hoverParams, CancellationToken.None);

            // Assert
            Assert.NotNull(hover);
            var md = hover.Contents.MarkupContent;
            Assert.Contains("Connections link ETL-SQL", md.Value);
            
            // 2. Hover over MSSQL (line 0, col 25)
            hoverParams = new HoverParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(0, 25)
            };
            
            hover = await hoverProvider.Handle(hoverParams, CancellationToken.None);
            
            // Assert
            Assert.NotNull(hover);
            md = hover.Contents.MarkupContent;
            Assert.Contains("# MSSQL", md.Value);
        }
    }
}
