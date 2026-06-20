using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using SaveOptions = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.SaveOptions;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;
using TextDocumentSyncKind = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.TextDocumentSyncKind;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Handles text document synchronization (open/change/save/close) and drives the
    /// analysis pipeline: lex → parse → metadata discovery → lineage → lint → diagnostics.
    /// Feature handlers (Hover, Definition, Completion, Formatting, SignatureHelp) are in
    /// separate provider classes that share state via <see cref="DocumentStateStore"/>.
    /// </summary>
    public class TextDocumentHandler : TextDocumentSyncHandlerBase, IOnLanguageServerStarted
    {
        private readonly ILogger<TextDocumentHandler> _logger;
        private readonly Linter _linter;
        private readonly IMetadataManager _metadata;
        private readonly DocumentStateStore _store;
        private ILanguageServerFacade? _server;

        public TextDocumentHandler(ILoggerFactory loggerFactory, IMetadataManager metadata, DocumentStateStore store)
        {
            _logger = loggerFactory.CreateLogger<TextDocumentHandler>();
            _metadata = metadata;
            _store = store;
            _linter = new Linter();
            foreach (var type in typeof(ILintRule).Assembly.GetTypes()
                .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
            {
                if (Activator.CreateInstance(type) is ILintRule rule)
                    _linter.AddRule(rule);
            }
            _logger.LogInformation("TextDocumentHandler initialized.");
        }

        public Task OnStarted(ILanguageServer server, CancellationToken cancellationToken)
        {
            _server = server;
            _logger.LogInformation("TextDocumentHandler bound to LanguageServer.");
            return Task.CompletedTask;
        }

        public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
            => new TextDocumentAttributes(uri, "etlsql");

        public override async Task<MediatR.Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
        {
            var text = request.ContentChanges.First().Text;
            _logger.LogInformation("didChange for {Uri}. Length: {Length}", request.TextDocument.Uri, text.Length);

            // Sync text immediately so that completion/hover see the fresh text
            _store.UpdateText(request.TextDocument.Uri, text);

            await AnalyzeAsync(request.TextDocument.Uri, text);
            return MediatR.Unit.Value;
        }

        public override async Task<MediatR.Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
        {
            var uri = request.TextDocument.Uri;
            if (_metadata.DebugMode) _logger.LogInformation("[DIAGNOSTIC] LSP: didOpen {Uri}", uri);

            // Notify the client that a script has been opened/focused to help sync UI state
            _server?.SendNotification("etlsql/scriptOpened", new { uri = uri.ToString() });

            // Sync text immediately so that completion/hover see the fresh text
            _store.UpdateText(uri, request.TextDocument.Text);

            await AnalyzeAsync(uri, request.TextDocument.Text);
            return MediatR.Unit.Value;
        }

        public override Task<MediatR.Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
            => Task.FromResult(MediatR.Unit.Value);

        public override Task<MediatR.Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
        {
            var uri = request.TextDocument.Uri;
            _logger.LogInformation("LSP: didClose {Uri}. Cleaning up metadata and signaling session recycle.", uri);

            // 1. Clear session metadata and temp tables for this document
            _metadata.ClearDocumentConnections(uri.ToString());
            _metadata.ClearTempTables(uri.ToString());

            // 2. Clear LSP state store
            _store.RemoveState(uri);

            // 3. Notify the client that the script is closed and its session can be recycled/deleted
            _server?.SendNotification("etlsql/scriptClosed", new { uri = uri.ToString() });

            return Task.FromResult(MediatR.Unit.Value);
        }

        /// <summary>
        /// Full analysis pipeline:
        /// 1. Lex + Parse
        /// 2. Connection and temp-table discovery
        /// 3. Lineage analysis
        /// 4. Lint
        /// 5. Publish diagnostics
        /// </summary>
        public async Task AnalyzeAsync(DocumentUri uri, string text)
        {
            _logger.LogInformation("Analyzing {Uri}.", uri);
            var diagnostics = new List<Diagnostic>();
            var fileLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            try
            {
                if (_metadata.DebugMode)
                    _logger.LogInformation("[DIAGNOSTIC] LSP: AnalyzeAsync started for {Uri}. Length: {Length}", uri, text.Length);

                var tokens = new Lexer(text).Tokenize();
                var script = new ETL_SQL.Core.Parser.Parser(tokens).Parse();

                if (_metadata.DebugMode)
                    _logger.LogInformation("[DIAGNOSTIC] LSP: Parsed {Count} statements.", script.Statements.Count);

                // Connection and temp-table discovery
                var activeConnections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var activeTempTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stmt in script.Statements)
                    await DiscoverMetadataRecursiveAsync(stmt, uri.ToString(), activeConnections, activeTempTables);

                _metadata.CleanUpDocumentConnectionsAndTempTables(uri.ToString(), activeConnections, activeTempTables);

                // Push connections to client sidebar
                var connections = _metadata.GetConnections(uri.ToString());
                _logger.LogInformation("Pushing {Count} connections to client for {Uri}", connections.Count, uri);
                _server?.SendNotification("etlsql/scriptConnections", new
                {
                    uri = uri.ToString(),
                    connections = connections.Select(c => new
                    {
                        name = c.Name,
                        type = c.Type,
                        connectionString = "",
                        hasConnectionString = !string.IsNullOrEmpty(c.ConnectionString),
                        isDocument = c.IsDocument
                    }).ToList()
                });

                // Push variables to client sidebar
                var scriptVariables = new List<object>();
                var sensitiveVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stmt in script.Statements)
                    DiscoverVariablesRecursive(stmt, scriptVariables, sensitiveVariables);

                _server?.SendNotification("etlsql/scriptVariables", new
                {
                    uri = uri.ToString(),
                    variables = scriptVariables.GroupBy(v => ((dynamic)v).name).Select(g => g.First()).ToList()
                });

                // Lineage analysis — store result in DocumentStateStore
                var tracker = new LineageTracker(NullLogger.Instance);
                var analyzer = new LineageAnalyzer(tracker);
                analyzer.Analyze(script);
                _store.SetState(uri, text, script, analyzer.Tracker);
                _logger.LogInformation("Analysis complete for {Uri}. Lineage entries: {Count}", uri, analyzer.Tracker.GetFullLineage().Count());

                diagnostics.AddRange(AnalysisDiagnosticBuilder
                    .FromParserDiagnostics(script.Diagnostics, fileLines)
                    .Select(ToLspDiagnostic));

                // Lint diagnostics
                var lintContext = new DefaultLintContext
                {
                    Metadata = new LanguageServerMetadataProvider(_metadata, uri.ToString()),
                    DocumentUri = uri.ToString()
                };
                var lintResults = await _linter.AnalyzeAsync(script, lintContext);
                diagnostics.AddRange(AnalysisDiagnosticBuilder
                    .FromLintResults(lintResults, fileLines)
                    .Select(ToLspDiagnostic));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AnalyzeAsync for {Uri}", uri);
                diagnostics.Add(ToLspDiagnostic(AnalysisDiagnosticBuilder.FromException(ex, fileLines)));
            }

            _server?.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = uri,
                Diagnostics = diagnostics
            });
            _logger.LogDebug("Published {Count} diagnostics for {Uri}", diagnostics.Count, uri);
        }

        private static Diagnostic ToLspDiagnostic(AnalysisDiagnostic diagnostic)
        {
            var severity = diagnostic.Severity switch
            {
                ETL_SQL.Core.Common.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
                ETL_SQL.Core.Common.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                ETL_SQL.Core.Common.DiagnosticSeverity.Hint => DiagnosticSeverity.Hint,
                _ => DiagnosticSeverity.Information
            };

            if (diagnostic.Code is not null)
            {
                return new Diagnostic
                {
                    Range = new LSPRange(diagnostic.StartLine, diagnostic.StartColumn, diagnostic.EndLine, diagnostic.EndColumn),
                    Severity = severity,
                    Message = diagnostic.Message,
                    Code = diagnostic.Code,
                    Source = diagnostic.Source
                };
            }

            return new Diagnostic
            {
                Range = new LSPRange(diagnostic.StartLine, diagnostic.StartColumn, diagnostic.EndLine, diagnostic.EndColumn),
                Severity = severity,
                Message = diagnostic.Message,
                Source = diagnostic.Source
            };
        }

        private async Task DiscoverMetadataRecursiveAsync(Statement stmt, string uri, HashSet<string> activeConnections, HashSet<string> activeTempTables)
        {
            if (_metadata.DebugMode)
                _logger.LogInformation("[DIAGNOSTIC] LSP: Discovering {Type}", stmt.GetType().Name);

            if (stmt is CreateConnectionStatement ccs)
            {
                var connStr = ccs.TargetExpression?.ToSql() ?? "";
                connStr = connStr.Trim('\'', '\"', '(', ')', ' ');
                _metadata.RegisterDocumentConnection(uri, ccs.ConnectionName, ccs.ConnectionType ?? "UNKNOWN", connStr);
                activeConnections.Add(ccs.ConnectionName);
            }
            else if (stmt is CreateTableStatement cts)
            {
                var tableName = cts.TargetTable.TableName;
                if (tableName.StartsWith("#"))
                {
                    _metadata.RegisterTempTable(uri, tableName, cts.Columns.Select(c => c.ColumnName).ToList());
                    activeTempTables.Add(tableName);
                }
            }
            else if (stmt is SelectStatement ss && ss.IntoTable != null)
            {
                var tableName = ss.IntoTable.TableName;
                if (tableName.StartsWith("#"))
                {
                    var columns = new List<string>();
                    var hasStar = ss.Columns.Any(c => c.Expression is IdentifierExpression id && id.Name == "*");
                    if (hasStar && ss.FromTable != null)
                    {
                        var conn = ss.FromTable.ConnectionName ?? _metadata.GetConnections(uri).FirstOrDefault(c => c.IsDocument)?.Name ?? "DEFAULT";
                        var sourceCols = await _metadata.GetColumnsAsync(conn, ss.FromTable.TableName, uri);
                        columns.AddRange(sourceCols);
                    }
                    else
                    {
                        columns.AddRange(ss.Columns.Select(c => c.Alias ?? c.Expression.ToSql().Split('.').Last().Trim('[', ']', '"', '\'')));
                    }
                    _metadata.RegisterTempTable(uri, tableName, columns.Distinct().ToList());
                    activeTempTables.Add(tableName);
                }
            }
            else if (stmt is DockerStatement ds)
            {
                // Register Docker alias as a connection so the linter recognizes it
                if (!string.IsNullOrEmpty(ds.Alias))
                {
                    _metadata.RegisterDocumentConnection(uri, ds.Alias, "DOCKER", ds.ImageName.ToSql());
                    activeConnections.Add(ds.Alias);
                }
            }
            else if (stmt is ExecuteRemoteBlockStatement erbs)
                await DiscoverMetadataRecursiveAsync(erbs.Body, uri, activeConnections, activeTempTables);
            else if (stmt is ExecutePushdownStatement eps)
            {
                // For pushdown, if 'INTO' is used, we should register it at least as a 'known' table
                if (eps.IntoTable != null && eps.IntoTable.TableName.StartsWith("#"))
                {
                    // We don't know the columns easily without parsing the SQLText (which is native),
                    // but we can register it with no columns to avoid "Table not found" errors.
                    _metadata.RegisterTempTable(uri, eps.IntoTable.TableName, new List<string>());
                    activeTempTables.Add(eps.IntoTable.TableName);
                }
            }
            else if (stmt is BlockStatement block)
                foreach (var s in block.Statements) await DiscoverMetadataRecursiveAsync(s, uri, activeConnections, activeTempTables);
            else if (stmt is IfStatement ifStmt)
            {
                await DiscoverMetadataRecursiveAsync(ifStmt.IfBody, uri, activeConnections, activeTempTables);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) await DiscoverMetadataRecursiveAsync(ei.Body, uri, activeConnections, activeTempTables);
                if (ifStmt.ElseBody != null) await DiscoverMetadataRecursiveAsync(ifStmt.ElseBody, uri, activeConnections, activeTempTables);
            }
            else if (stmt is WhileStatement w) await DiscoverMetadataRecursiveAsync(w.Body, uri, activeConnections, activeTempTables);
            else if (stmt is ForStatement f) await DiscoverMetadataRecursiveAsync(f.Body, uri, activeConnections, activeTempTables);
            else if (stmt is ForeachStatement fe) await DiscoverMetadataRecursiveAsync(fe.Body, uri, activeConnections, activeTempTables);
            else if (stmt is TryCatchStatement tc)
            {
                await DiscoverMetadataRecursiveAsync(tc.TryBody, uri, activeConnections, activeTempTables);
                await DiscoverMetadataRecursiveAsync(tc.CatchBody, uri, activeConnections, activeTempTables);
            }
        }

        protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
            => new TextDocumentSyncRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("etlsql"),
                Change = TextDocumentSyncKind.Full,
                Save = new SaveOptions { IncludeText = true }
            };

        private static bool IsSensitiveVariableName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.Contains("password", StringComparison.OrdinalIgnoreCase)
                || name.Contains("passwd", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pwd", StringComparison.OrdinalIgnoreCase)
                || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("apiKey", StringComparison.OrdinalIgnoreCase)
                || name.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
                || name.Contains("privateKey", StringComparison.OrdinalIgnoreCase)
                || name.Contains("private_key", StringComparison.OrdinalIgnoreCase);
        }

        private static string RootVariableName(string name)
        {
            var dot = name.IndexOf('.');
            return dot >= 0 ? name[..dot] : name;
        }

        private void DiscoverVariablesRecursive(Statement? stmt, List<object> vars, HashSet<string> sensitiveVariables)
        {
            if (stmt == null) return;

            if (stmt is DeclareStatement dec)
            {
                var isSensitive = dec.IsSensitive
                    || dec.IsSecret
                    || string.Equals(dec.DataType, "SECRET", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dec.DataType, "SENSITIVE", StringComparison.OrdinalIgnoreCase)
                    || IsSensitiveVariableName(dec.VariableName);
                if (isSensitive)
                    sensitiveVariables.Add(dec.VariableName);
                vars.Add(new
                {
                    name = dec.VariableName,
                    typeName = dec.DataType ?? "scalar",
                    value = isSensitive && dec.InitialValue is not null ? "(secret)" : dec.InitialValue?.ToSql()
                });
            }
            else if (stmt is SetVariableStatement set)
            {
                var variableName = set.VariableName;
                var rootName = RootVariableName(variableName);
                var isSensitive = sensitiveVariables.Contains(rootName)
                    || sensitiveVariables.Contains(variableName)
                    || IsSensitiveVariableName(rootName)
                    || IsSensitiveVariableName(variableName);
                vars.Add(new
                {
                    name = variableName,
                    typeName = "unknown",
                    value = isSensitive ? "(secret)" : set.Value?.ToSql()
                });
            }
            else if (stmt is ForStatement f)
            {
                vars.Add(new { name = f.VariableName, typeName = "INT", value = "loop" });
                DiscoverVariablesRecursive(f.Body, vars, sensitiveVariables);
            }
            else if (stmt is ForeachStatement fe)
            {
                vars.Add(new { name = fe.VariableName, typeName = "item", value = "loop" });
                DiscoverVariablesRecursive(fe.Body, vars, sensitiveVariables);
            }
            else if (stmt is BlockStatement block)
                foreach (var s in block.Statements) DiscoverVariablesRecursive(s, vars, sensitiveVariables);
            else if (stmt is IfStatement ifStmt)
            {
                DiscoverVariablesRecursive(ifStmt.IfBody, vars, sensitiveVariables);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) DiscoverVariablesRecursive(ei.Body, vars, sensitiveVariables);
                if (ifStmt.ElseBody != null) DiscoverVariablesRecursive(ifStmt.ElseBody, vars, sensitiveVariables);
            }
            else if (stmt is WhileStatement w) DiscoverVariablesRecursive(w.Body, vars, sensitiveVariables);
            else if (stmt is TryCatchStatement tc)
            {
                DiscoverVariablesRecursive(tc.TryBody, vars, sensitiveVariables);
                DiscoverVariablesRecursive(tc.CatchBody, vars, sensitiveVariables);
            }
        }
    }
}
