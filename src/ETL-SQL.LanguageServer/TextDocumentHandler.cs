using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using ETL_SQL.Core;
using OmniSharp.Extensions.JsonRpc;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;
using TextDocumentSyncKind = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.TextDocumentSyncKind;
using SaveOptions = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.SaveOptions;
namespace ETL_SQL.LSP
{
    /// <summary>
    /// Core handler for text document synchronization and Language Server features (Hover, Definition, Completion, Formatting, Signature Help).
    /// Integrates the ETL-SQL Parser, Linter, and Lineage Analyzer to provide real-time feedback and IntelliSense.
    /// </summary>
    public class TextDocumentHandler : TextDocumentSyncHandlerBase, IHoverHandler, IDefinitionHandler, ICompletionHandler, IDocumentFormattingHandler, ISignatureHelpHandler, IOnLanguageServerStarted
    {
        private readonly ILogger _logger;
        private readonly Linter _linter;
        private readonly IMetadataManager _metadata;
        private ILanguageServerFacade? _server;
        private readonly ConcurrentDictionary<DocumentUri, (string Text, Script Script, ILineageTracker Lineage)> _documentStates = new();

        /// <summary>Initializes a new instance of the <see cref="TextDocumentHandler"/> class.</summary>
        /// <param name="loggerFactory">The logger factory for creating component loggers.</param>
        /// <param name="metadata">The metadata manager for database schema info.</param>
        public TextDocumentHandler(ILoggerFactory loggerFactory, IMetadataManager metadata)
        {
            _logger = loggerFactory.CreateLogger<TextDocumentHandler>();
            _metadata = metadata;
            _linter = new Linter();
            foreach (var type in typeof(ILintRule).Assembly.GetTypes()
                .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
            {
                if (Activator.CreateInstance(type) is ILintRule rule)
                    _linter.AddRule(rule);
            }
            _logger.LogInformation("TextDocumentHandler initialized with DI (lazy server binding).");
        }

        /// <summary>Binds the handler to the language server instance once it has started.</summary>
        public Task OnStarted(ILanguageServer server, CancellationToken cancellationToken)
        {
            _server = server;
            _logger.LogInformation("TextDocumentHandler bound to LanguageServer.");
            return Task.CompletedTask;
        }

        public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new TextDocumentAttributes(uri, "etlsql");

        /// <summary>Handles document change notifications and triggers re-analysis.</summary>
        public override async Task<MediatR.Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
        {
            var text = request.ContentChanges.First().Text;
            _logger.LogInformation("didChange received for {Uri}. Text length: {Length}", request.TextDocument.Uri, text.Length);
            await AnalyzeAsync(request.TextDocument.Uri, text);
            return MediatR.Unit.Value;
        }

        /// <summary>Handles document open notifications and triggers initial analysis.</summary>
        public override async Task<MediatR.Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
        {
            if (_metadata.DebugMode) _logger.LogInformation("[DIAGNOSTIC] LSP: didOpen {Uri}", request.TextDocument.Uri);
            _logger.LogInformation("didOpen received for {Uri}", request.TextDocument.Uri);
            await AnalyzeAsync(request.TextDocument.Uri, request.TextDocument.Text);
            return MediatR.Unit.Value;
        }

        public override Task<MediatR.Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken) => Task.FromResult(MediatR.Unit.Value);
        public override Task<MediatR.Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken) => Task.FromResult(MediatR.Unit.Value);

        public string? GetDocumentText(DocumentUri uri)
        {
            return _documentStates.TryGetValue(uri, out var state) ? state.Text : null;
        }

        /// <summary>
        /// Performs deep analysis of the document text:
        /// 1. Lexing & Parsing
        /// 2. Connection Discovery
        /// 3. Lineage Analysis
        /// 4. Linting
        /// 5. Diagnostic Publication
        /// </summary>
        /// <param name="uri">The document URI.</param>
        /// <param name="text">The full document text.</param>
        public async Task AnalyzeAsync(DocumentUri uri, string text)
        {
            _logger.LogInformation("Analyzing document: {Uri}. Start.", uri);
            var diagnostics = new List<Diagnostic>();
            try
            {
                var snippet = text.Length > 100 ? text.Substring(0, 100) : text;
                if (_metadata.DebugMode) _logger.LogInformation("[DIAGNOSTIC] LSP: AnalyzeAsync started for {Uri}. Length: {Length}. Prefix: [{Snippet}]", uri, text.Length, snippet.Replace("\r", "\\r").Replace("\n", "\\n"));
                
                var lexer = new Lexer(text); 
                var tokens = lexer.Tokenize();
                if (_metadata.DebugMode) _logger.LogInformation("[DIAGNOSTIC] LSP: Lexed {Count} tokens.", tokens.Count);
                
                var parser = new ETL_SQL.Core.Parser.Parser(tokens);
                var script = parser.Parse();
                if (_metadata.DebugMode) _logger.LogInformation("[DIAGNOSTIC] LSP: Script parsed. Total statements: {Count}", script.Statements.Count);
                
                _logger.LogDebug("Parsed script with {Count} statements.", script.Statements.Count);

                // Discover script-based connections and temp tables recursively
                _metadata.ClearDocumentConnections(uri.ToString());
                _metadata.ClearTempTables(uri.ToString()); // Ensure we start fresh
                
                foreach (var stmt in script.Statements)
                {
                    await DiscoverMetadataRecursiveAsync(stmt, uri.ToString());
                }
                // Push connections to client for sidebar sync
                var connections = _metadata.GetConnections(uri.ToString());
                _logger.LogInformation("Pushing {Count} connections to client for {Uri}", connections.Count, uri);
                if (_server != null)
                {
                    _server.SendNotification("etlsql/scriptConnections", new {
                        uri = uri.ToString(),
                        connections = connections.Select(c => new {
                            name = c.Name,
                            type = c.Type,
                            connectionString = c.ConnectionString,
                            isDocument = c.IsDocument
                        }).ToList()
                    });
                }
                // Run Lineage Analysis
                var tracker = new LineageTracker();
                var analyzer = new LineageAnalyzer(tracker);
                analyzer.Analyze(script);
                var entries = analyzer.Tracker.GetFullLineage().ToList();
                _documentStates[uri] = (text, script, analyzer.Tracker);
                _logger.LogInformation("Analysis complete for {Uri}. Entries in tracker: {Count}", uri, entries.Count);

                // Populate syntax diagnostics from Parser
                foreach (var diag in script.Diagnostics)
                {
                    diagnostics.Add(new Diagnostic
                    {
                        Range = new LSPRange(Math.Max(0, diag.Line - 1), Math.Max(0, diag.Column - 1), Math.Max(0, diag.Line - 1), Math.Max(0, diag.Column + 5)),
                        Severity = diag.Severity switch {
                            ETL_SQL.Core.Common.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
                            ETL_SQL.Core.Common.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                            _ => DiagnosticSeverity.Information
                        },
                        Message = diag.Message,
                        Source = "ETL-SQL " + diag.Source
                    });
                }

                // Run Linter with Context
                var lintContext = new DefaultLintContext {
                    Metadata = new LanguageServerMetadataProvider(_metadata, uri.ToString()),
                    DocumentUri = uri.ToString()
                };

                var lintResults = await _linter.AnalyzeAsync(script, lintContext);
                foreach (var res in lintResults)
                {
                    int line = Math.Max(0, res.LineNumber - 1);
                    int col = Math.Max(0, res.ColumnNumber - 1);
                    diagnostics.Add(new Diagnostic
                    {
                        Range = new LSPRange(line, col, line, col + 5),
                        Severity = res.Severity == LintSeverity.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        Message = res.Message,
                        Source = "ETL-SQL Linter"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AnalyzeAsync for {Uri}", uri);
                int errLine = 0, errCol = 0;
                if (ex is ETL_SQL.Core.Common.Exceptions.SyntaxException sx)
                {
                    errLine = Math.Max(0, sx.Line - 1);
                    errCol  = Math.Max(0, sx.Column - 1);
                }
                diagnostics.Add(new Diagnostic
                {
                    Range = new LSPRange(errLine, errCol, errLine, errCol + 5),
                    Severity = DiagnosticSeverity.Error,
                    Message = ex.Message,
                    Source = "ETL-SQL Parser"
                });
            }

            if (_server != null)
            {
                _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
                {
                    Uri = uri,
                    Diagnostics = diagnostics
                });
                _logger.LogDebug("Published {Count} diagnostics for {Uri}", diagnostics.Count, uri);
            }
        }

        /// <summary>Handles hover requests and renders the lineage graph for the target column.</summary>
        public async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Hover requested for {Uri} at {Line}:{Col}", request.TextDocument.Uri, request.Position.Line, request.Position.Character);
            if (!_documentStates.TryGetValue(request.TextDocument.Uri, out var state))
            {
                return null;
            }

            int line = (int)request.Position.Line + 1;
            int col = (int)request.Position.Character + 1;

            var entries = state.Lineage.GetFullLineage().ToList();
            var entry = entries.FirstOrDefault(e => 
                (line > e.Line || (line == e.Line && col >= e.Column)) && 
                (line < e.EndLine || (line == e.EndLine && col <= e.EndColumn)));

            if (entry == null) return null;

            var renderer = new LineageGraphRenderer();
            string graph = renderer.Render(state.Lineage, entry.TargetTable, entry.TargetColumn);

            var md = new List<string>();
            md.Add($"**Column**: `{entry.TargetColumn}`");
            
            if (entry.Metadata.Count > 0)
            {
                md.Add("### Metadata");
                foreach (var m in entry.Metadata)
                {
                    var key = m.Key.Equals("d", StringComparison.OrdinalIgnoreCase) ? "Description" : m.Key;
                    md.Add($"- **{key}**: {m.Value}");
                }
            }
            else if (!string.IsNullOrEmpty(entry.Description))
            {
                md.Add($"**Description**: {entry.Description}");
            }

            if (!string.IsNullOrEmpty(entry.DerivedFromDescriptions))
            {
                md.Add($"> [!NOTE]\n> Derived from: {entry.DerivedFromDescriptions}");
            }

            md.Add("### Lineage Graph");
            md.Add($"```text\n{graph.TrimEnd()}\n```");

            var content = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = string.Join("\n\n", md)
            });

            return new Hover { Contents = content };
        }

        /// <summary>Handles 'Go to Definition' requests (currently returns a stub location at the start of the file if lineage is found).</summary>
        public async Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
        {
            if (!_documentStates.TryGetValue(request.TextDocument.Uri, out var state)) 
                return null;

            int line = (int)request.Position.Line + 1;
            int col = (int)request.Position.Character + 1;

            var lines = state.Text.Split('\n');
            if (line > lines.Length) return null;
            var currentLine = lines[line - 1];
            
            // Find word boundaries around the cursor
            int start = (int)request.Position.Character;
            while (start > 0 && (char.IsLetterOrDigit(currentLine[start - 1]) || currentLine[start - 1] == '@' || currentLine[start - 1] == '#' || currentLine[start - 1] == '_')) start--;
            int end = (int)request.Position.Character;
            while (end < currentLine.Length && (char.IsLetterOrDigit(currentLine[end]) || currentLine[end] == '@' || currentLine[end] == '#' || currentLine[end] == '_')) end++;
            
            var word = currentLine.Substring(start, end - start);
            if (string.IsNullOrEmpty(word)) return null;

            foreach (var stmt in state.Script.Statements)
            {
                var loc = FindDeclaration(stmt, word, request.TextDocument.Uri);
                if (loc != null) return loc;
            }

            return null;
        }

        private LocationOrLocationLinks? FindDeclaration(Statement stmt, string name, DocumentUri uri)
        {
            if (stmt is DeclareStatement ds && string.Equals(ds.VariableName, name, StringComparison.OrdinalIgnoreCase))
            {
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(ds.Line - 1, ds.Column - 1, ds.Line - 1, ds.Column - 1 + name.Length) });
            }
            if (stmt is CreateTableStatement cts && string.Equals(cts.TargetTable.TableName, name, StringComparison.OrdinalIgnoreCase))
            {
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(cts.Line - 1, cts.Column - 1, cts.Line - 1, cts.Column - 1 + name.Length) });
            }
            if (stmt is CreateConnectionStatement ccs && string.Equals(ccs.ConnectionName, name, StringComparison.OrdinalIgnoreCase))
            {
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(ccs.Line - 1, ccs.Column - 1, ccs.Line - 1, ccs.Column - 1 + name.Length) });
            }
            if (stmt is ForStatement fs && string.Equals(fs.VariableName, name, StringComparison.OrdinalIgnoreCase))
            {
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(fs.Line - 1, fs.Column - 1, fs.Line - 1, fs.Column - 1 + name.Length) });
            }
            if (stmt is ForeachStatement fes && string.Equals(fes.VariableName, name, StringComparison.OrdinalIgnoreCase))
            {
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(fes.Line - 1, fes.Column - 1, fes.Line - 1, fes.Column - 1 + name.Length) });
            }
            if (stmt is BlockStatement block)
            {
                foreach (var s in block.Statements)
                {
                    var found = FindDeclaration(s, name, uri);
                    if (found != null) return found;
                }
            }
            if (stmt is IfStatement ifStmt)
            {
                var found = FindDeclaration(ifStmt.IfBody, name, uri);
                if (found != null) return found;
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses)
                    {
                        found = FindDeclaration(ei.Body, name, uri);
                        if (found != null) return found;
                    }
                }
                if (ifStmt.ElseBody != null)
                {
                    found = FindDeclaration(ifStmt.ElseBody, name, uri);
                    if (found != null) return found;
                }
            }
            if (stmt is WhileStatement whileStmt) return FindDeclaration(whileStmt.Body, name, uri);
            if (stmt is TryCatchStatement tc)
            {
                var found = FindDeclaration(tc.TryBody, name, uri);
                if (found != null) return found;
                return FindDeclaration(tc.CatchBody, name, uri);
            }

            return null;
        }

        /// <summary>
        /// Handles autocomplete requests. Supports:
        /// 1. Connection-qualified tables and columns.
        /// 2. Contextual completion after FROM/JOIN/INTO.
        /// 3. Keywords and built-in functions.
        /// 4. Column expansion for '*'.
        /// </summary>
        public async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
        {
            var line = (int)request.Position.Line;
            var col = (int)request.Position.Character;

            if (!_documentStates.TryGetValue(request.TextDocument.Uri, out var state))
                return new CompletionList();

            var text = state.Text;
            var lines = text.Split('\n');
            var currentLine = lines.Length > line ? lines[line] : "";
            var prefix = col > 0 && currentLine.Length >= col ? currentLine.Substring(0, col).TrimEnd() : "";

            var items = new List<CompletionItem>();
            string docUri = request.TextDocument.Uri.ToString();
            var contextConnections = _metadata.GetConnections(docUri);

            // 1. Connection-qualified table/view/column completion (e.g., conn.table.col)
            if (prefix.Contains('.'))
            {
                var parts = prefix.Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Last().Split('.');
                
                if (parts.Length == 1) // Should not really happen with prefix.Contains('.')
                {
                    var connName = parts[0];
                    if (contextConnections.Any(c => string.Equals(c.Name, connName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var conn = contextConnections.First(c => string.Equals(c.Name, connName, StringComparison.OrdinalIgnoreCase));
                        var tables = (await _metadata.GetTablesAsync(connName, docUri)).ToList();
                        _logger.LogInformation("Found {Count} tables for {ConnName}", tables.Count, connName);
                        
                        // If it's a single-table connection (like FLATFILE), return columns directly
                        // Also check if the connection type is one that typically has a single table (CSV, FILE, etc)
                        bool isFileBased = new[] { "FLATFILE", "CSV", "FILE", "JSON", "XML" }.Contains(conn.Type.ToUpperInvariant());
                        
                        if (isFileBased || (tables.Count == 1 && string.Equals(tables[0], connName, StringComparison.OrdinalIgnoreCase)))
                        {
                            var tableName = tables.Count > 0 ? tables[0] : connName;
                            var columns = await _metadata.GetColumnsAsync(connName, tableName, docUri);
                            _logger.LogInformation("Single-table/File-based detected. Returning {Count} columns.", columns.Count());
                            items.AddRange(columns.Select(c => new CompletionItem { Label = c, Kind = CompletionItemKind.Field, Detail = $"Column ({connName})" }));
                        }
                        else
                        {
                            items.AddRange(tables.Select(t => new CompletionItem { Label = t, Kind = CompletionItemKind.Class, Detail = $"Table ({connName})" }));
                        }
                    }
                    else
                    {
                         _logger.LogWarning("Connection '{ConnName}' not found in registered list: {List}", connName, string.Join(", ", contextConnections.Select(c => c.Name)));
                    }
                }
                else if (parts.Length == 2 || parts.Length == 3) // conn.table. or table. or alias. or conn.table.col
                {
                    string? conn = null;
                    string? table = null;
                    string first = parts[0];
                    string second = parts[1];
                    string? third = parts.Length == 3 ? parts[2] : null;

                    _logger.LogInformation("LSP: Completion Parts: first='{First}', second='{Second}', third='{Third}'", first, second, third);

                    // Case A: 3-part name (conn.table.col)
                    if (parts.Length == 3)
                    {
                        if (contextConnections.Any(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase)))
                        {
                            conn = first;
                            table = second;
                        }
                    }
                    else if (string.IsNullOrEmpty(second)) // Case B: first. (could be conn. or table. or alias.)
                    {
                        if (contextConnections.Any(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase)))
                        {
                            var cinfo = contextConnections.First(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase));
                            var tables = (await _metadata.GetTablesAsync(first, docUri)).ToList();
                            bool isFileBased = new[] { "FLATFILE", "CSV", "FILE", "JSON", "XML" }.Contains(cinfo.Type.ToUpperInvariant());
                            
                            if (isFileBased || (tables.Count == 1 && string.Equals(tables[0], first, StringComparison.OrdinalIgnoreCase)))
                            {
                                table = tables.Count > 0 ? tables[0] : first;
                                conn = first;
                            }
                            else
                            {
                                items.AddRange(tables.Select(t => new CompletionItem { Label = t, Kind = CompletionItemKind.Class, Detail = $"Table ({first})" }));
                                return new CompletionList(items);
                            }
                        }
                    }
                    
                    // Case C: Resolve alias/table if not resolved yet
                    if (conn == null || table == null)
                    {
                        var activeStatement = state.Script.Statements.FirstOrDefault(s => line + 1 >= s.Line && line + 1 <= s.EndLine);
                        var resolved = ResolveTableFromAlias(activeStatement, first);
                        if (resolved == null)
                        {
                            var aliases = AliasScanner.Scan(text);
                            if (aliases.TryGetValue(first, out var info))
                            {
                                resolved = (info.ConnectionName, info.BaseTableName ?? info.TableName);
                            }
                        }

                        var defaultConn = contextConnections.FirstOrDefault(c => c.IsDocument)?.Name ?? "DEFAULT";
                        conn = resolved?.Conn ?? (contextConnections.Any(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase)) ? first : defaultConn);
                        table = resolved?.Table ?? (resolved == null && !string.IsNullOrEmpty(second) ? second : first);
                    }

                    if (!string.IsNullOrEmpty(conn) && !string.IsNullOrEmpty(table))
                    {
                        _logger.LogInformation("LSP: Fetching columns for {Conn}.{Table}", conn, table);
                        var columns = await _metadata.GetColumnsAsync(conn, table, docUri);
                        items.AddRange(columns.Select(c => new CompletionItem { Label = c, Kind = CompletionItemKind.Field, Detail = $"Column from {conn}.{table}" }));
                    }
                }
            }
            // 2. FROM / JOIN / INTO context
            else if (prefix.EndsWith("FROM", StringComparison.OrdinalIgnoreCase) || 
                     prefix.EndsWith("JOIN", StringComparison.OrdinalIgnoreCase) ||
                     prefix.EndsWith("INTO", StringComparison.OrdinalIgnoreCase))
            {
                // Show connections
                items.AddRange(contextConnections.Select(c => new CompletionItem { Label = c.Name, Kind = CompletionItemKind.Module, Detail = $"Connection ({c.Type})" }));
                
                // Also show default tables
                var defaultConn = contextConnections.FirstOrDefault(c => c.IsDocument)?.Name ?? "DEFAULT";
                var tables = await _metadata.GetTablesAsync(defaultConn, docUri);
                items.AddRange(tables.Select(t => new CompletionItem { Label = t, Kind = CompletionItemKind.Class, Detail = $"Table ({defaultConn})" }));
            }

            // 3. Keywords & Functions (if not after a dot)
            if (!prefix.Contains('.'))
            {
                var keywords = new[] {
                    "SELECT", "FROM", "WHERE", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET",
                    "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "TRUNCATE", "MERGE", "MATCHED",
                    "CREATE", "TABLE", "CONNECTION", "ON", "TYPE", "TARGET", "FLATFILE", "CSV", "EXCEL", "JSON", "XML", "MSSQL", "POSTGRES", "ORACLE", "ODBC", "MOCKDB",
                    "DROP", "DECLARE", "PRINT", "IF", "ELSE", "WHILE", "FOR", "FOREACH", "BEGIN", "END", "TRY", "CATCH", "THROW", "RAISEERROR",
                    "EXEC", "EXECUTE", "RETURN", "BREAK", "CONTINUE", "JOB", "STEP", "ORCHESTRATE", "LOAD", "BULK", "SHOW",
                    "LINEAGE", "DOCKER", "START", "STOP", "PAUSE", "CLOSE", "PARALLEL", "RUN", "SCRIPT", "USE", "LINT",
                    "OUTPUT", "INPUT", "WITH", "WITHIN", "OVER", "PARTITION", "RANGE", "BETWEEN", "PRECEDING", "FOLLOWING", "UNBOUNDED", "CURRENT",
                    "PROFILE", "PROFILING", "ON", "OFF", "EXPLAIN"
                };

                var functions = new[] {
                    "GETDATE", "DATEADD", "DATEDIFF", "CAST", "CONVERT", "ISNULL", "COALESCE", "FORMAT",
                    "SUM", "COUNT", "AVG", "MIN", "MAX", "HASHBYTES", "NEWID", "CHECKSUM", "AT TIME ZONE",
                    "FILE_EXISTS", "DIRECTORY_EXISTS", "FILE_LIST"
                };

                items.AddRange(keywords.Select(kw => new CompletionItem { Label = kw, Kind = CompletionItemKind.Keyword, Detail = "Keyword" }));
                items.AddRange(functions.Select(f => new CompletionItem { Label = f, Kind = CompletionItemKind.Function, Detail = "Function" }));

                // 3b. Variable completion
                var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stmt in state.Script.Statements)
                {
                    CollectAvailableVariables(stmt, line + 1, vars);
                }
                items.AddRange(vars.Select(v => new CompletionItem { Label = v, Kind = CompletionItemKind.Variable, Detail = "Variable" }));
            }

            // 4. Support expansion of *
            if (prefix.TrimEnd().EndsWith("*"))
            {
                var currentStmt = state.Script.Statements.FirstOrDefault(s => s.Line <= line + 1 && s.EndLine >= line + 1);
                var lastWord = prefix.Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Last();
                var specificAlias = prefix.EndsWith(".*") ? lastWord.TrimEnd('.', '*') : null;

                var tablesToExpand = new List<AliasInfo>();
                if (currentStmt != null)
                {
                    // Scan only the text of the current statement for aliases to avoid cross-statement noise
                    // Split text into lines, handling both \n and \r\n
                    var allLines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                    var stmtLines = allLines.Skip(currentStmt.Line - 1).Take(currentStmt.EndLine - currentStmt.Line + 1);
                    var stmtText = string.Join("\n", stmtLines);

                    var aliases = AliasScanner.Scan(stmtText);
                    
                    if (string.IsNullOrEmpty(specificAlias))
                    {
                        tablesToExpand.AddRange(aliases.Values.Distinct());
                    }
                    else
                    {
                        tablesToExpand.AddRange(aliases.Values.Where(a => 
                            (a.Alias?.Equals(specificAlias, StringComparison.OrdinalIgnoreCase) == true) ||
                            (string.IsNullOrEmpty(a.Alias) && a.TableName.Equals(specificAlias, StringComparison.OrdinalIgnoreCase))
                        ).Distinct());
                    }
                }

                var allColumns = new List<string>();
                var defaultConn = contextConnections.FirstOrDefault(c => c.IsDocument)?.Name ?? "DEFAULT";
                
                foreach (var info in tablesToExpand)
                {
                    var conn = info.ConnectionName ?? defaultConn;
                    var table = info.BaseTableName ?? info.TableName;

                    var columns = await _metadata.GetColumnsAsync(conn, table, docUri);
                    var cols = columns.ToList();
                    
                    if (!string.IsNullOrEmpty(info.Alias))
                         allColumns.AddRange(cols.Select(c => $"{info.Alias}.{c}"));
                    else allColumns.AddRange(cols);
                }

                if (allColumns.Any())
                {
                    var expansion = string.Join(", ", allColumns.Distinct());
                    var startCol = col - lastWord.Length;

                    items.Add(new CompletionItem
                    {
                        Label = "Expand columns",
                        InsertText = expansion,
                        FilterText = lastWord,
                        SortText = "0000_expand",
                        Preselect = true,
                        TextEdit = new TextEdit
                        {
                            Range = new LSPRange(line, startCol, line, col),
                            NewText = expansion
                        },
                        Kind = CompletionItemKind.Snippet,
                        Detail = $"Expands to {allColumns.Count} columns ({tablesToExpand.Count} tables)",
                        Documentation = expansion
                    });
                }
            }

            return new CompletionList(items);
        }

        /// <summary>Resolves a table/connection tuple from an alias in the context of a statement.</summary>
        private (string? Conn, string Table)? ResolveTableFromAlias(Statement? stmt, string aliasOrTable)
        {
            if (stmt is SelectStatement sel)
            {
                if (sel.FromTable != null && (string.Equals(sel.FromTable.Alias, aliasOrTable, StringComparison.OrdinalIgnoreCase) || string.Equals(sel.FromTable.TableName, aliasOrTable, StringComparison.OrdinalIgnoreCase)))
                {
                    return (sel.FromTable.ConnectionName, sel.FromTable.TableName);
                }
                foreach (var join in sel.Joins)
                {
                    if (join.Table != null && (string.Equals(join.Table.Alias, aliasOrTable, StringComparison.OrdinalIgnoreCase) || string.Equals(join.Table.TableName, aliasOrTable, StringComparison.OrdinalIgnoreCase)))
                    {
                        return (join.Table.ConnectionName, join.Table.TableName);
                    }
                }
            }
            return null;
        }

        private void CollectAvailableVariables(Statement? stmt, int currentLine, HashSet<string> vars)
        {
            if (stmt == null) return;
            
            // Only add variables declared AT OR BEFORE the current line, 
            // unless they are loop variables that enclose the current line.
            
            if (stmt is DeclareStatement declare)
            {
                if (declare.Line <= currentLine) vars.Add(declare.VariableName);
            }
            else if (stmt is SetVariableStatement setVar)
            {
                // In some dialects SET can declare, but in ETL-SQL it usually requires DECLARE.
                // However, we can add it to be safe if it starts with @.
                if (setVar.Line <= currentLine) vars.Add(setVar.VariableName);
            }
            else if (stmt is ForStatement forLoop)
            {
                if (currentLine >= forLoop.Line && currentLine <= forLoop.EndLine)
                {
                    vars.Add(forLoop.VariableName);
                    CollectAvailableVariables(forLoop.Body, currentLine, vars);
                }
            }
            else if (stmt is ForeachStatement foreachLoop)
            {
                if (currentLine >= foreachLoop.Line && currentLine <= foreachLoop.EndLine)
                {
                    vars.Add(foreachLoop.VariableName);
                    CollectAvailableVariables(foreachLoop.Body, currentLine, vars);
                }
            }
            else if (stmt is BlockStatement block)
            {
                foreach (var s in block.Statements) CollectAvailableVariables(s, currentLine, vars);
            }
            else if (stmt is IfStatement ifStmt)
            {
                if (currentLine >= ifStmt.Line && currentLine <= ifStmt.EndLine)
                {
                    CollectAvailableVariables(ifStmt.IfBody, currentLine, vars);
                    if (ifStmt.ElseIfClauses != null)
                    {
                        foreach (var ei in ifStmt.ElseIfClauses) CollectAvailableVariables(ei.Body, currentLine, vars);
                    }
                    if (ifStmt.ElseBody != null) CollectAvailableVariables(ifStmt.ElseBody, currentLine, vars);
                }
            }
            else if (stmt is WhileStatement whileStmt)
            {
                if (currentLine >= whileStmt.Line && currentLine <= whileStmt.EndLine)
                {
                    CollectAvailableVariables(whileStmt.Body, currentLine, vars);
                }
            }
            else if (stmt is TryCatchStatement tryCatch)
            {
                if (currentLine >= tryCatch.Line && currentLine <= tryCatch.EndLine)
                {
                    CollectAvailableVariables(tryCatch.TryBody, currentLine, vars);
                    CollectAvailableVariables(tryCatch.CatchBody, currentLine, vars);
                }
            }
            else if (stmt is CreateProcedureStatement proc)
            {
                if (currentLine >= proc.Line && currentLine <= proc.EndLine)
                {
                    foreach (var p in proc.Parameters) vars.Add(p.Name);
                    if (proc.Body != null) CollectAvailableVariables(proc.Body, currentLine, vars);
                }
            }
        }
        /// <summary>Handles signature help requests for built-in functions and connector-specific options.</summary>
        public async Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
        {
            var line = (int)request.Position.Line;
            var col = (int)request.Position.Character;

            if (!_documentStates.TryGetValue(request.TextDocument.Uri, out var state))
                return null;

            var currentLine = state.Text.Split('\n')[line];
            var prefix = col > 0 ? currentLine.Substring(0, col) : "";

            // Find the active function call (last open parenthesis before cursor)
            int openParen = prefix.LastIndexOf('(');
            if (openParen == -1) return null;

            var funcPart = prefix.Substring(0, openParen).Trim();
            var funcName = funcPart.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrEmpty(funcName)) return null;

            // Simple parameter index counting
            var argsPart = prefix.Substring(openParen + 1);
            int activeParam = argsPart.Count(c => c == ',');

            // 1. Check for CREATE CONNECTION ... ON TYPE(
            if (prefix.Contains("CREATE", StringComparison.OrdinalIgnoreCase) && prefix.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase))
            {
                var connector = _metadata.GetConnections().FirstOrDefault(c => string.Equals(c.Name, funcName, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Type, funcName, StringComparison.OrdinalIgnoreCase));
                var regConnector = _metadata.GetRegisteredNames().Contains(funcName) ? _metadata.GetConnector(funcName) : null;
                
                if (regConnector != null)
                {
                    var help = regConnector.GetHelp();
                    var options = regConnector.GetSupportedOptions();
                    
                    var signature = new SignatureInformation
                    {
                        Label = $"{regConnector.Name}(options)",
                        Documentation = help,
                        Parameters = options.Select(o => new ParameterInformation { Label = o.Key, Documentation = string.Join("|", o.Value) }).ToList()
                    };
                    
                    return new SignatureHelp { Signatures = new List<SignatureInformation> { signature }, ActiveSignature = 0, ActiveParameter = activeParam };
                }
            }

            // 2. Built-in Functions
            var builtInSignatures = new Dictionary<string, (string Label, string Doc, string[] Params)>(StringComparer.OrdinalIgnoreCase)
            {
                // String Functions
                { "UPPER", ("UPPER(string)", "Converts a string to uppercase.", new[] { "string" }) },
                { "LOWER", ("LOWER(string)", "Converts a string to lowercase.", new[] { "string" }) },
                { "LEN", ("LEN(string)", "Returns the number of characters in a string.", new[] { "string" }) },
                { "LENGTH", ("LENGTH(string)", "Returns the number of characters in a string.", new[] { "string" }) },
                { "TRIM", ("TRIM(string)", "Removes leading and trailing spaces.", new[] { "string" }) },
                { "LTRIM", ("LTRIM(string)", "Removes leading spaces.", new[] { "string" }) },
                { "RTRIM", ("RTRIM(string)", "Removes trailing spaces.", new[] { "string" }) },
                { "REVERSE", ("REVERSE(string)", "Reverses the characters in a string.", new[] { "string" }) },
                { "CONCAT", ("CONCAT(string1, string2, ...)", "Concatenates multiple strings.", new[] { "string1", "string2" }) },
                { "SUBSTRING", ("SUBSTRING(string, start, length)", "Returns a part of a string.", new[] { "string", "start", "length" }) },
                { "SUBSTR", ("SUBSTR(string, start, length)", "Returns a part of a string.", new[] { "string", "start", "length" }) },
                { "LEFT", ("LEFT(string, length)", "Returns the leftmost part of a string.", new[] { "string", "length" }) },
                { "RIGHT", ("RIGHT(string, length)", "Returns the rightmost part of a string.", new[] { "string", "length" }) },
                { "CHARINDEX", ("CHARINDEX(substring, string)", "Returns the position of a substring.", new[] { "substring", "string" }) },
                { "INSTR", ("INSTR(string, substring)", "Returns the position of a substring.", new[] { "string", "substring" }) },
                { "REPLACE", ("REPLACE(string, old, new)", "Replaces occurrences of a substring.", new[] { "string", "old", "new" }) },
                { "INITCAP", ("INITCAP(string)", "Capitalizes the first letter of each word.", new[] { "string" }) },
                { "FORMAT", ("FORMAT(value, format [, culture])", "Returns a value formatted with the specified format.", new[] { "value", "format", "culture" }) },

                // Math Functions
                { "ABS", ("ABS(number)", "Returns the absolute value of a number.", new[] { "number" }) },
                { "ROUND", ("ROUND(number, decimals)", "Rounds a number to a specified number of decimal places.", new[] { "number", "decimals" }) },
                { "CEILING", ("CEILING(number)", "Returns the smallest integer greater than or equal to a number.", new[] { "number" }) },
                { "FLOOR", ("FLOOR(number)", "Returns the largest integer less than or equal to a number.", new[] { "number" }) },
                { "SQRT", ("SQRT(number)", "Returns the square root of a number.", new[] { "number" }) },
                { "POWER", ("POWER(base, exponent)", "Returns the result of a number raised to a power.", new[] { "base", "exponent" }) },
                { "MOD", ("MOD(dividend, divisor)", "Returns the remainder of a division.", new[] { "dividend", "divisor" }) },

                // Date Functions
                { "GETDATE", ("GETDATE()", "Returns the current system date and time.", Array.Empty<string>()) },
                { "NOW", ("NOW()", "Returns the current system date and time.", Array.Empty<string>()) },
                { "SYSDATE", ("SYSDATE()", "Returns the current system date and time.", Array.Empty<string>()) },
                { "SYSDATETIME", ("SYSDATETIME()", "Returns the current system date and time.", Array.Empty<string>()) },
                { "DATENAME", ("DATENAME(part, date)", "Returns a string representing the specified datepart of a date.", new[] { "part", "date" }) },
                { "DATEPART", ("DATEPART(part, date)", "Returns an integer representing the specified datepart of a date.", new[] { "part", "date" }) },
                { "DATEDIFF", ("DATEDIFF(part, start, end)", "Returns the count of boundaries crossed between two dates.", new[] { "part", "start", "end" }) },
                { "DATEADD", ("DATEADD(part, number, date)", "Returns a date after adding an interval.", new[] { "part", "number", "date" }) },
                { "ISDATE", ("ISDATE(string)", "Returns 1 if the expression is a valid date.", new[] { "string" }) },
                { "EOMONTH", ("EOMONTH(date)", "Returns the last day of the month containing the specified date.", new[] { "date" }) },
                { "YEAR", ("YEAR(date)", "Returns an integer representing the year of a date.", new[] { "date" }) },
                { "MONTH", ("MONTH(date)", "Returns an integer representing the month of a date.", new[] { "date" }) },
                { "DAY", ("DAY(date)", "Returns an integer representing the day of a date.", new[] { "date" }) },

                // Logic & Utility Functions
                { "COALESCE", ("COALESCE(expression1, expression2, ...)", "Returns the first non-null expression.", new[] { "expression1", "expression2" }) },
                { "ISNULL", ("ISNULL(check, replacement)", "Replaces NULL with a specified value.", new[] { "check", "replacement" }) },
                { "NVL", ("NVL(check, replacement)", "Replaces NULL with a specified value.", new[] { "check", "replacement" }) },
                { "IFNULL", ("IFNULL(check, replacement)", "Replaces NULL with a specified value.", new[] { "check", "replacement" }) },
                { "NULLIF", ("NULLIF(expression1, expression2)", "Returns NULL if two expressions are equal.", new[] { "expression1", "expression2" }) },
                { "CAST", ("CAST(expression AS type)", "Converts an expression to a specified data type.", new[] { "expression", "type" }) },
                { "CONVERT", ("CONVERT(type, expression [, style])", "Converts an expression to a specified data type.", new[] { "type", "expression", "style" }) },
                { "IIF", ("IIF(condition, true_value, false_value)", "Returns one of two values based on a condition.", new[] { "condition", "true_value", "false_value" }) },
                { "GREATEST", ("GREATEST(value1, value2, ...)", "Returns the largest value in a list.", new[] { "value1", "value2" }) },
                { "LEAST", ("LEAST(value1, value2, ...)", "Returns the smallest value in a list.", new[] { "value1", "value2" }) },
                { "IS_NULL", ("IS_NULL(expression)", "Returns true if the expression is NULL.", new[] { "expression" }) },
                { "IS_NOT_NULL", ("IS_NOT_NULL(expression)", "Returns true if the expression is NOT NULL.", new[] { "expression" }) },
                { "COUNT", ("COUNT(expression)", "Returns the number of items in a group or list.", new[] { "expression" }) },

                // File Functions
                { "FILE_EXISTS", ("FILE_EXISTS(path)", "Returns true if the file exists.", new[] { "path" }) },
                { "DIRECTORY_EXISTS", ("DIRECTORY_EXISTS(path)", "Returns true if the directory exists.", new[] { "path" }) },
                { "FILE_LIST", ("FILE_LIST(path [, recursive])", "Returns a list of files in a directory.", new[] { "path", "recursive" }) },
                { "REMOTE_FILE_LIST", ("REMOTE_FILE_LIST(connectionName [, path])", "Returns a list of files from a remote connection.", new[] { "connectionName", "path" }) },

                // List Functions
                { "APPEND_TO_LIST", ("APPEND_TO_LIST(list, value)", "Appends a value to a list.", new[] { "list", "value" }) },
                { "ADD_TO_LIST", ("ADD_TO_LIST(list, value)", "Adds a value to a list.", new[] { "list", "value" }) },
                { "REMOVE_FROM_LIST", ("REMOVE_FROM_LIST(list, value)", "Removes a value from a list.", new[] { "list", "value" }) },
                { "SORT_LIST", ("SORT_LIST(list)", "Sorts the elements of a list.", new[] { "list" }) },

                { "HASHBYTES", ("HASHBYTES('algorithm', expression)", "Returns the hash of its input.", new[] { "algorithm", "expression" }) }
            };

            if (builtInSignatures.TryGetValue(funcName, out var info))
            {
                var signature = new SignatureInformation
                {
                    Label = info.Label,
                    Documentation = info.Doc,
                    Parameters = info.Params.Select(p => new ParameterInformation { Label = p }).ToList()
                };
                return new SignatureHelp { Signatures = new List<SignatureInformation> { signature }, ActiveSignature = 0, ActiveParameter = Math.Min(activeParam, info.Params.Length - 1) };
            }

            return null;
        }

        /// <summary>Handles document formatting requests using the <see cref="SqlFormatter"/>.</summary>
        public Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
        {
            if (!_documentStates.TryGetValue(request.TextDocument.Uri, out var state)) return Task.FromResult<TextEditContainer?>(null);
            var formatted = SqlFormatter.Format(state.Text);
            
            var lines = state.Text.Split('\n');
            var endLine = lines.Length - 1;
            var endCol = lines[endLine].Length;

            return Task.FromResult<TextEditContainer?>(new TextEditContainer(new TextEdit
            {
                Range = new LSPRange(0, 0, endLine, endCol),
                NewText = formatted
            }));
        }

        protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        {
            return new TextDocumentSyncRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("etlsql"),
                Change = TextDocumentSyncKind.Full,
                Save = new SaveOptions { IncludeText = true }
            };
        }

        HoverRegistrationOptions IRegistration<HoverRegistrationOptions, HoverCapability>.GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities) => new HoverRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql") };
        DefinitionRegistrationOptions IRegistration<DefinitionRegistrationOptions, DefinitionCapability>.GetRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities) => new DefinitionRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql") };
        CompletionRegistrationOptions IRegistration<CompletionRegistrationOptions, CompletionCapability>.GetRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities) => new CompletionRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql"), ResolveProvider = false, TriggerCharacters = new Container<string>(" ", ".", "*") };
        DocumentFormattingRegistrationOptions IRegistration<DocumentFormattingRegistrationOptions, DocumentFormattingCapability>.GetRegistrationOptions(DocumentFormattingCapability capability, ClientCapabilities clientCapabilities) => new DocumentFormattingRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql") };
        SignatureHelpRegistrationOptions IRegistration<SignatureHelpRegistrationOptions, SignatureHelpCapability>.GetRegistrationOptions(SignatureHelpCapability capability, ClientCapabilities clientCapabilities) => new SignatureHelpRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql"), TriggerCharacters = new Container<string>("(", ",") };

        private async Task DiscoverMetadataRecursiveAsync(Statement stmt, string uri)
        {
            if (_metadata.DebugMode) _logger.LogInformation("[DIAGNOSTIC] LSP: Discovering metadata in {Type}", stmt.GetType().Name);

            if (stmt is CreateConnectionStatement ccs)
            {
                var connStr = ccs.TargetExpression?.ToSql() ?? "";
                connStr = connStr.Trim('\'', '\"', '(', ')', ' ');
                _metadata.RegisterDocumentConnection(uri, ccs.ConnectionName, ccs.ConnectionType, connStr);
            }
            else if (stmt is CreateTableStatement cts)
            {
                var tableName = cts.TargetTable.TableName;
                if (tableName.StartsWith("#"))
                {
                    var columns = cts.Columns.Select(c => c.ColumnName).ToList();
                    _metadata.RegisterTempTable(uri, tableName, columns);
                }
            }
            else if (stmt is SelectStatement ss && ss.IntoTable != null)
            {
                var tableName = ss.IntoTable.TableName;
                if (tableName.StartsWith("#"))
                {
                    // Discover columns from the SELECT list
                    var columns = new List<string>();
                    var hasStar = ss.Columns.Any(c => c.Expression is IdentifierExpression id && id.Name == "*");
                    
                    if (hasStar && ss.FromTable != null)
                    {
                        // Expand '*' by fetching columns from the source table
                        var conn = ss.FromTable.ConnectionName ?? _metadata.GetConnections(uri).FirstOrDefault(c => c.IsDocument)?.Name ?? "DEFAULT";
                        var table = ss.FromTable.TableName;
                        var sourceCols = await _metadata.GetColumnsAsync(conn, table, uri);
                        columns.AddRange(sourceCols);
                    }
                    else
                    {
                         columns.AddRange(ss.Columns.Select(c => c.Alias ?? c.Expression.ToSql().Split('.').Last().Trim('[', ']', '"', '\'')));
                    }
                    _metadata.RegisterTempTable(uri, tableName, columns.Distinct().ToList());
                }
            }
            else if (stmt is BlockStatement block)
            {
                foreach (var s in block.Statements) await DiscoverMetadataRecursiveAsync(s, uri);
            }
            else if (stmt is IfStatement ifStmt)
            {
                await DiscoverMetadataRecursiveAsync(ifStmt.IfBody, uri);
                if (ifStmt.ElseIfClauses != null) 
                    foreach (var ei in ifStmt.ElseIfClauses) await DiscoverMetadataRecursiveAsync(ei.Body, uri);
                if (ifStmt.ElseBody != null) await DiscoverMetadataRecursiveAsync(ifStmt.ElseBody, uri);
            }
            else if (stmt is WhileStatement whileStmt)
            {
                await DiscoverMetadataRecursiveAsync(whileStmt.Body, uri);
            }
            else if (stmt is ForStatement forStmt)
            {
                await DiscoverMetadataRecursiveAsync(forStmt.Body, uri);
            }
            else if (stmt is ForeachStatement foreachStmt)
            {
                await DiscoverMetadataRecursiveAsync(foreachStmt.Body, uri);
            }
            else if (stmt is TryCatchStatement tc)
            {
                await DiscoverMetadataRecursiveAsync(tc.TryBody, uri);
                await DiscoverMetadataRecursiveAsync(tc.CatchBody, uri);
            }
        }
    }
}

