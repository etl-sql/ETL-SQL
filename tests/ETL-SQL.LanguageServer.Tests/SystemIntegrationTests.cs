using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Services;
using ETL_SQL.LSP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;
using Xunit.Abstractions;
using DocumentUri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri;

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
        public void ScriptVariableDiscovery_MasksSensitiveValues()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
            var metadataManager = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry());
            var handler = new TextDocumentHandler(loggerFactory, metadataManager, new DocumentStateStore());

            var scriptText = @"
DECLARE @normal STRING = 'visible';
DECLARE @portalPassword SECRET = 'super-secret';
SET @apiToken = 'raw-token';
SET @normal = 'still-visible';
";
            var tokens = new ETL_SQL.Core.Parser.Lexer(scriptText).Tokenize();
            var script = new ETL_SQL.Core.Parser.Parser(tokens, scriptText).Parse();
            var method = typeof(TextDocumentHandler).GetMethod(
                "DiscoverVariablesRecursive",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var variables = new List<object>();
            var sensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var statement in script.Statements)
            {
                method!.Invoke(handler, new object[] { statement, variables, sensitive });
            }

            var json = JsonSerializer.Serialize(variables);
            Assert.Contains("visible", json);
            Assert.Contains("still-visible", json);
            Assert.Contains("(secret)", json);
            Assert.DoesNotContain("super-secret", json);
            Assert.DoesNotContain("raw-token", json);
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
            var completionProvider = new CompletionProvider(loggerFactory.CreateLogger<CompletionProvider>(), store, languageService, new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()));
            var hoverHandler = new HoverProvider(loggerFactory.CreateLogger<HoverProvider>(), store, functionRegistry, helpRegistry, new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()));

            var uri = DocumentUri.From("untitled:Untitled-1");
            var normalizedUri = uri.ToString();

            // 1. Analyze script with alias
            var script = "CREATE CONNECTION m AS MOCKDB();\r\nSELECT u. FROM m.Users AS u;";
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
            script = "CREATE CONNECTION m AS MOCKDB();\r\nSELECT u.* FROM m.Users AS u;";
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
            script = "CREATE CONNECTION m AS MOCKDB();\r\nSELECT * FROM m.Users;";
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

            // One table, no alias — bare column names. The prefix must never include the connection
            // name ("m."): a connector-qualified column does not survive pushdown to the remote server.
            Assert.Contains("UserID, UserName, Email", expandItem.InsertText);
            Assert.DoesNotContain("m.Users.", expandItem.InsertText);

            // 5. Two tables, neither aliased: qualify by table name only, never by connection name.
            //    Bare columns would be ambiguous across the join; "m.Users.UserID" would not push down.
            script = "CREATE CONNECTION m AS MOCKDB();\r\nSELECT * FROM m.Users JOIN m.Orders ON m.Users.UserID = m.Orders.UserID;";
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
            _output.WriteLine($"Expand columns (two unaliased tables) InsertText: {expandItem.InsertText}");

            Assert.Contains("Users.UserID", expandItem.InsertText);
            Assert.DoesNotContain("m.Users.", expandItem.InsertText);
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
            var completionProvider = new CompletionProvider(loggerFactory.CreateLogger<CompletionProvider>(), store, languageService, new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()));

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
            var hoverProvider = new HoverProvider(loggerFactory.CreateLogger<HoverProvider>(), store, functionRegistry, helpRegistry, new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()));

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

        [Fact]
        public async Task DocumentSymbols_Should_Include_Labels()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var store = new DocumentStateStore();
            var handler = new TextDocumentHandler(loggerFactory, new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry()), store);
            var symbolProvider = new DocumentSymbolProvider(store);

            var uri = DocumentUri.From("untitled:Untitled-4");
            var script = "DECLARE @val INT = 10;\r\ncheckpoint1:\r\nSET @val = 20;\r\nIF 1=1 BEGIN\r\n    inner_label:\r\n    PRINT 'ok';\r\nEND";
            await handler.AnalyzeAsync(uri, script);

            var request = new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier(uri)
            };

            // Act
            var container = await symbolProvider.Handle(request, CancellationToken.None);

            // Assert
            Assert.NotNull(container);
            var list = container.Select(s => s.DocumentSymbol).ToList();
            Assert.Equal(2, list.Count);

            var cp1 = list.FirstOrDefault(s => s.Name == "checkpoint1");
            Assert.NotNull(cp1);
            Assert.Equal("Top-level Checkpoint", cp1.Detail);

            var cp2 = list.FirstOrDefault(s => s.Name == "inner_label");
            Assert.NotNull(cp2);
            Assert.Equal("Control Flow Target", cp2.Detail);
        }

        [Fact]
        public async Task ParsedDocumentState_Should_Index_Definitions_For_GoToDefinition()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var store = new DocumentStateStore();
            var handler = new TextDocumentHandler(loggerFactory, new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry()), store);

            var uri = DocumentUri.From("untitled:Untitled-Definitions");
            var script = "DECLARE @val INT = 10;\r\nmy_label:\r\nFOR @i = 1 TO 2 BEGIN\r\n    PRINT @i;\r\nEND";
            await handler.AnalyzeAsync(uri, script);

            Assert.True(store.TryGetState(uri, out var state));
            Assert.True(state.Declarations.ContainsKey("@val"));
            Assert.True(state.Declarations.ContainsKey("my_label"));
            Assert.True(state.Declarations.ContainsKey("@i"));
        }

        [Fact]
        public async Task Hover_Should_Find_Declarations_In_Other_Open_Files()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var store = new DocumentStateStore();
            var metadataManager = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry());
            var helpRegistry = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            var handler = new TextDocumentHandler(loggerFactory, metadataManager, store);
            var hoverProvider = new HoverProvider(
                loggerFactory.CreateLogger<HoverProvider>(),
                store,
                new Engine.Functions.FunctionRegistry(),
                helpRegistry,
                new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()));

            var libraryUri = DocumentUri.From("untitled:Library");
            var useUri = DocumentUri.From("untitled:Use");
            await handler.AnalyzeAsync(libraryUri, "DECLARE @shared INT = 10;");
            await handler.AnalyzeAsync(useUri, "PRINT @shared;");

            var hover = await hoverProvider.Handle(new HoverParams
            {
                TextDocument = new TextDocumentIdentifier(useUri),
                Position = new Position(0, 8)
            }, CancellationToken.None);

            Assert.NotNull(hover);
            var markdown = hover!.Contents.MarkupContent.Value;
            Assert.Contains("Declaration `@shared`", markdown);
            Assert.Contains("untitled:Library", markdown);
        }

        [Fact]
        public async Task Completion_After_Goto_Should_Suggest_Labels()
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
            var completionProvider = new CompletionProvider(loggerFactory.CreateLogger<CompletionProvider>(), store, languageService, new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()));

            var uri = DocumentUri.From("untitled:Untitled-5");
            var script = "DECLARE @val INT = 10;\r\nmy_label1:\r\nSET @val = 20;\r\nGOTO ";
            await handler.AnalyzeAsync(uri, script);

            var completionParams = new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(3, 5), // Cursor right after "GOTO " (line 3, index 5)
                Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
            };

            // Act
            var list = await completionProvider.Handle(completionParams, CancellationToken.None);

            // Assert: Should suggest my_label1
            Assert.Contains(list, i => i.Label == "my_label1");
        }

        [Fact]
        public async Task Completion_Should_Resolve_Connection_Immediately_On_Keystroke()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var connectorRegistry = new ETL_SQL.Data.ConnectorRegistry();
            connectorRegistry.Register(new MockDbConnector());
            var metadataManager = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, connectorRegistry);
            var helpRegistry = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            var languageService = new LanguageService(metadataManager, helpRegistry);
            var store = new DocumentStateStore();
            var handler = new TextDocumentHandler(loggerFactory, metadataManager, store);
            var completionProvider = new CompletionProvider(loggerFactory.CreateLogger<CompletionProvider>(), store, languageService, new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()));

            var uri = DocumentUri.From("untitled:Untitled-ConnImmediate");

            // 1. Initial document state with CREATE CONNECTION statement
            var initialScript = "CREATE CONNECTION m AS MOCKDB();\r\n";
            await handler.AnalyzeAsync(uri, initialScript);

            // Verify metadata is aware of connection
            var connections = metadataManager.GetConnections(uri.ToString());
            Assert.Contains(connections, c => string.Equals(c.Name, "m", StringComparison.OrdinalIgnoreCase));

            // 2. User types "m." on the second line.
            // In a real editor, this triggers didChange notification immediately.
            var changedScript = "CREATE CONNECTION m AS MOCKDB();\r\nm.";
            store.UpdateText(uri, changedScript);

            // Simulating concurrent autocomplete request before AnalyzeAsync is completed or run.
            var completionParams = new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(1, 2), // Line 1, index 2 (just after "m.")
                Context = new CompletionContext { TriggerKind = CompletionTriggerKind.TriggerCharacter, TriggerCharacter = "." }
            };

            // Act
            var list = await completionProvider.Handle(completionParams, CancellationToken.None);

            // Assert: Connection "m" has tables Users, Products, Sales, etc.
            // Check that the completion list contains suggestions prefixed with m.
            Assert.NotEmpty(list);
            Assert.Contains(list, i => i.Label == "m.Users");
            Assert.Contains(list, i => i.Label == "m.Products");
        }

        [Fact]
        public async Task Completion_InsideNativeChart_SuggestsAcceptedGrammar()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var metadata = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, new ETL_SQL.Data.ConnectorRegistry());
            var help = new ETL_SQL.Core.Metadata.LanguageHelpRegistry();
            var store = new DocumentStateStore();
            var provider = new CompletionProvider(loggerFactory.CreateLogger<CompletionProvider>(), store,
                new LanguageService(metadata, help), new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()));
            var uri = DocumentUri.From("untitled:advanced-chart-completion.rptsql");
            const string script = "CREATE VISUAL Native AS CUSTOM (SOURCE = #data, CHART (";
            store.UpdateText(uri, script);

            var result = await provider.Handle(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(0, script.Length),
                Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
            }, CancellationToken.None);

            Assert.Contains(result, item => item.Label == "LAYERS");
            Assert.Contains(result, item => item.Label == "SCALES");
            Assert.Contains(result, item => item.Label == "FACET");
            Assert.Contains(result, item => item.Label == "INDEPENDENT");
            Assert.Contains(result, item => item.Label == "TICK");
            Assert.Contains(result, item => item.Label == "JITTER");
            Assert.Contains(result, item => item.Label == "DIVERGING");
            Assert.Contains(result, item => item.Label == "ASPECT_RATIO");
            Assert.Contains(result, item => item.Label == "Q1");
            Assert.Contains(result, item => item.Label == "MEDIAN");
            Assert.Contains(result, item => item.Label == "OPEN");
            Assert.Contains(result, item => item.Label == "CLOSE");
            Assert.Contains(result, item => item.Label == "GEOGRAPHIC");
            Assert.Contains(result, item => item.Label == "MERCATOR");
            Assert.Contains(result, item => item.Label == "MAP_FILE");
            Assert.Contains(result, item => item.Label == "REGION");
            Assert.Contains(result, item => item.Label == "ROUTE");
        }

        private class MockConfiguration : Microsoft.Extensions.Configuration.IConfiguration
        {
            public string this[string key]
            {
                get { return key == "Linting:AvoidSelectStar:Enabled" ? "true" : null; }
                set { }
            }
            public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) { return null; }
            public System.Collections.Generic.IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() { return null; }
            public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() { return null; }
        }

        [Fact]
        public void Lsp_Linter_AvoidSelectStar_RespectsConfiguration()
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().AddDebug());
            services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new MockConfiguration());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
            var connectorRegistry = new ETL_SQL.Data.ConnectorRegistry();
            var metadataManager = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, connectorRegistry);

            var storeEnabled = new DocumentStateStore();
            var handlerEnabled = new TextDocumentHandler(loggerFactory, metadataManager, storeEnabled, serviceProvider);
            Assert.True(handlerEnabled.GetLinter().HasRuleOfType(typeof(ETL_SQL.Analysis.Linting.Rules.AvoidSelectStarRule)));

            var storeDisabled = new DocumentStateStore();
            var handlerDisabled = new TextDocumentHandler(loggerFactory, metadataManager, storeDisabled);
            Assert.False(handlerDisabled.GetLinter().HasRuleOfType(typeof(ETL_SQL.Analysis.Linting.Rules.AvoidSelectStarRule)));
        }
    }
}
