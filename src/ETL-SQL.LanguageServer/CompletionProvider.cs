using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Handles autocomplete (completion) requests. Supports connection-qualified columns, FROM/JOIN/INTO context,
    /// keyword/function completion, variable completion, and star-expansion.
    /// </summary>
    public class CompletionProvider : ICompletionHandler
    {
        private readonly ILogger<CompletionProvider> _logger;
        private readonly DocumentStateStore _store;
        private readonly IMetadataManager _metadata;
        private readonly Core.Interfaces.ILanguageHelpRegistry _languageHelp;

        private static readonly string[] _keywords = new[]
        {
            "SELECT", "FROM", "WHERE", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET",
            "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "TRUNCATE", "MERGE", "MATCHED",
            "CREATE", "TABLE", "CONNECTION", "ON", "TYPE", "TARGET", 
            "FLATFILE", "CSV", "EXCEL", "JSON", "XML", "PARQUET", "AVRO", "API", "REST", "SMTP", "DIRECTORY", "SFTP", "FTP", "AZURE_BLOB",
            "MSSQL", "POSTGRES", "ORACLE", "ODBC", "MOCKDB",
            "DROP", "DECLARE", "PRINT", "IF", "ELSE", "WHILE", "FOR", "FOREACH", "BEGIN", "END", "TRY", "CATCH", "THROW", "RAISEERROR",
            "EXEC", "EXECUTE", "RETURN", "BREAK", "CONTINUE", "JOB", "STEP", "LOAD", "BULK", "SHOW",
            "LINEAGE", "DOCKER", "START", "STOP", "PAUSE", "CLOSE", "PARALLEL", "RUN", "SCRIPT", "USE", "LINT",
            "OUTPUT", "INPUT", "WITH", "WITHIN", "OVER", "PARTITION", "RANGE", "BETWEEN", "PRECEDING", "FOLLOWING", "UNBOUNDED", "CURRENT",
            "PROFILE", "PROFILING", "ON", "OFF", "EXPLAIN", "RECURSIVE", "PATH", "AUTO", "RAW", "EXPLICIT", "ROOT", "ELEMENTS",
            "INCLUDE_NULL_VALUES", "WITHOUT_ARRAY_WRAPPER", "WAITFOR", "DELAY", "TIME"
        };

        private static readonly string[] _functions = new[]
        {
            "GETDATE", "DATEADD", "DATEDIFF", "CAST", "CONVERT", "ISNULL", "COALESCE", "FORMAT",
            "SUM", "COUNT", "AVG", "MIN", "MAX", "HASHBYTES", "NEWID", "CHECKSUM", "AT TIME ZONE",
            "FILE_EXISTS", "DIRECTORY_EXISTS", "FILE_LIST", "DIRECTORY_LIST", "GET_TAGS", "GET_TAG",
            "JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "ISJSON", "JSON_EXISTS", "JSON_OBJECT", "JSON_ARRAY", "OPENJSON",
            "XMLVALUE", "XMLEXISTS", "XMLQUERY", "XMLELEMENT", "XMLATTRIBUTES", "XMLFOREST",
            "REGEXP_LIKE", "REGEXP_SUBSTR", "REGEXP_REPLACE", "REGEXP_INSTR", "REGEXP_COUNT", "REGEXP_MATCHES",
            "CUME_DIST", "PERCENT_RANK", "NTH_VALUE", "PERCENTILE_CONT", "PERCENTILE_DISC"
        };

        public CompletionProvider(ILogger<CompletionProvider> logger, DocumentStateStore store, IMetadataManager metadata, Core.Interfaces.ILanguageHelpRegistry languageHelp)
        {
            _logger = logger;
            _store = store;
            _metadata = metadata;
            _languageHelp = languageHelp;
        }

        public async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
        {
            var line = (int)request.Position.Line;
            var col  = (int)request.Position.Character;

            if (!_store.TryGetState(request.TextDocument.Uri, out var state))
                return new CompletionList();

            var text = state.Text;
            var lines = text.Split('\n');
            var currentLine = lines.Length > line ? lines[line] : "";
            var prefix = col > 0 && currentLine.Length >= col ? currentLine.Substring(0, col).TrimEnd() : "";

            var items = new List<CompletionItem>();
            string docUri = request.TextDocument.Uri.ToString();
            var contextConnections = _metadata.GetConnections(docUri);

            // 1. Connection-qualified table/view/column completion (conn.table.col)
            if (prefix.Contains('.'))
            {
                var parts = prefix.Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Last().Split('.');

                if (parts.Length == 1)
                {
                    var connName = parts[0];
                    if (contextConnections.Any(c => string.Equals(c.Name, connName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var conn = contextConnections.First(c => string.Equals(c.Name, connName, StringComparison.OrdinalIgnoreCase));
                        var tables = (await _metadata.GetTablesAsync(connName, docUri)).ToList();
                        _logger.LogInformation("Found {Count} tables for {ConnName}", tables.Count, connName);

                        bool isFileBased = new[] { "FLATFILE", "CSV", "JSON", "XML", "PARQUET", "AVRO" }.Contains(conn.Type.ToUpperInvariant());
                        if (isFileBased || (tables.Count == 1 && string.Equals(tables[0], connName, StringComparison.OrdinalIgnoreCase)))
                        {
                            var tableName = tables.Count > 0 ? tables[0] : connName;
                            var columns = await _metadata.GetColumnsAsync(connName, tableName, docUri);
                            items.AddRange(columns.Select(c => new CompletionItem { Label = c, Kind = CompletionItemKind.Field, Detail = $"Column ({connName})" }));
                        }
                        else
                        {
                            items.AddRange(tables.Select(t => new CompletionItem { Label = t, Kind = CompletionItemKind.Class, Detail = $"Table ({connName})" }));
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Connection '{ConnName}' not found in: {List}", connName, string.Join(", ", contextConnections.Select(c => c.Name)));
                    }
                }
                else if (parts.Length == 2 || parts.Length == 3)
                {
                    string? conn  = null;
                    string? table = null;
                    string first  = parts[0];
                    string second = parts[1];

                    _logger.LogInformation("LSP: Completion Parts: first='{First}', second='{Second}'", first, second);

                    if (parts.Length == 3)
                    {
                        if (contextConnections.Any(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase)))
                        {
                            conn  = first;
                            table = second;
                        }
                    }
                    else if (string.IsNullOrEmpty(second))
                    {
                        if (contextConnections.Any(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase)))
                        {
                            var cinfo = contextConnections.First(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase));
                            var tables = (await _metadata.GetTablesAsync(first, docUri)).ToList();
                            bool isFileBased = new[] { "FLATFILE", "CSV", "JSON", "XML", "PARQUET", "AVRO" }.Contains(cinfo.Type.ToUpperInvariant());

                            if (isFileBased || (tables.Count == 1 && string.Equals(tables[0], first, StringComparison.OrdinalIgnoreCase)))
                            {
                                table = tables.Count > 0 ? tables[0] : first;
                                conn  = first;
                            }
                            else
                            {
                                items.AddRange(tables.Select(t => new CompletionItem { Label = t, Kind = CompletionItemKind.Class, Detail = $"Table ({first})" }));
                                return new CompletionList(items);
                            }
                        }
                    }

                    if (conn == null || table == null)
                    {
                        var activeStatement = state.Script.Statements.FirstOrDefault(s => line + 1 >= s.Line && line + 1 <= s.EndLine);
                        var resolved = ResolveTableFromAlias(activeStatement, first);
                        if (resolved == null)
                        {
                            var aliases = AliasScanner.Scan(text);
                            if (aliases.TryGetValue(first, out var info))
                                resolved = (info.ConnectionName, info.BaseTableName ?? info.TableName);
                        }

                        var defaultConn = contextConnections.FirstOrDefault(c => c.IsDocument)?.Name ?? "DEFAULT";
                        conn  = resolved?.Conn ?? (contextConnections.Any(c => string.Equals(c.Name, first, StringComparison.OrdinalIgnoreCase)) ? first : defaultConn);
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
                items.AddRange(contextConnections.Select(c => new CompletionItem { Label = c.Name, Kind = CompletionItemKind.Module, Detail = $"Connection ({c.Type})" }));
                var defaultConn = contextConnections.FirstOrDefault(c => c.IsDocument)?.Name ?? "DEFAULT";
                var tables = await _metadata.GetTablesAsync(defaultConn, docUri);
                items.AddRange(tables.Select(t => new CompletionItem { Label = t, Kind = CompletionItemKind.Class, Detail = $"Table ({defaultConn})" }));
            }

            // 3. Keywords, functions, and variables
            if (!prefix.Contains('.'))
            {
                items.AddRange(_keywords.Select(kw => new CompletionItem { 
                    Label = kw, 
                    Kind = CompletionItemKind.Keyword, 
                    Detail = "Keyword",
                    Documentation = _languageHelp.GetHelp(kw) != null ? new MarkupContent { Kind = MarkupKind.Markdown, Value = _languageHelp.GetHelp(kw)! } : null
                }));
                
                items.AddRange(_functions.Select(f => new CompletionItem { 
                    Label = f, 
                    Kind = CompletionItemKind.Function, 
                    Detail = "Function",
                    Documentation = _languageHelp.GetHelp("FUNCTION", f) != null ? new MarkupContent { Kind = MarkupKind.Markdown, Value = _languageHelp.GetHelp("FUNCTION", f)! } : null
                }));

                var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stmt in state.Script.Statements)
                    CollectAvailableVariables(stmt, line + 1, vars);
                items.AddRange(vars.Select(v => new CompletionItem { Label = v, Kind = CompletionItemKind.Variable, Detail = "Variable" }));
            }

            // 4. Star expansion
            if (prefix.TrimEnd().EndsWith("*"))
            {
                var lastWord = prefix.Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Last();
                var specificAlias = prefix.EndsWith(".*") ? lastWord.TrimEnd('.', '*') : null;
                var currentStmt = state.Script.Statements.FirstOrDefault(s => s.Line <= line + 1 && s.EndLine >= line + 1);

                var tablesToExpand = new List<AliasInfo>();
                if (currentStmt != null)
                {
                    var allLines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                    var stmtLines = allLines.Skip(currentStmt.Line - 1).Take(currentStmt.EndLine - currentStmt.Line + 1);
                    var aliases = AliasScanner.Scan(string.Join("\n", stmtLines));

                    if (string.IsNullOrEmpty(specificAlias))
                        tablesToExpand.AddRange(aliases.Values.Distinct());
                    else
                        tablesToExpand.AddRange(aliases.Values.Where(a =>
                            (a.Alias?.Equals(specificAlias, StringComparison.OrdinalIgnoreCase) == true) ||
                            (string.IsNullOrEmpty(a.Alias) && a.TableName.Equals(specificAlias, StringComparison.OrdinalIgnoreCase))
                        ).Distinct());
                }

                var allColumns = new List<string>();
                var defaultConn = contextConnections.FirstOrDefault(c => c.IsDocument)?.Name ?? "DEFAULT";
                foreach (var info in tablesToExpand)
                {
                    var conn  = info.ConnectionName ?? defaultConn;
                    var table = info.BaseTableName ?? info.TableName;
                    var cols  = (await _metadata.GetColumnsAsync(conn, table, docUri)).ToList();
                    if (!string.IsNullOrEmpty(info.Alias))
                        allColumns.AddRange(cols.Select(c => $"{info.Alias}.{c}"));
                    else
                        allColumns.AddRange(cols);
                }

                if (allColumns.Any())
                {
                    var expansion  = string.Join(", ", allColumns.Distinct());
                    var startCol   = col - lastWord.Length;
                    items.Add(new CompletionItem
                    {
                        Label         = "Expand columns",
                        InsertText    = expansion,
                        FilterText    = lastWord,
                        SortText      = "0000_expand",
                        Preselect     = true,
                        TextEdit      = new TextEdit { Range = new LSPRange(line, startCol, line, col), NewText = expansion },
                        Kind          = CompletionItemKind.Snippet,
                        Detail        = $"Expands to {allColumns.Count} columns ({tablesToExpand.Count} tables)",
                        Documentation = expansion
                    });
                }
            }

            return new CompletionList(items);
        }

        private static (string? Conn, string Table)? ResolveTableFromAlias(Statement? stmt, string aliasOrTable)
        {
            if (stmt is SelectStatement sel)
            {
                if (sel.FromTable != null && (string.Equals(sel.FromTable.Alias, aliasOrTable, StringComparison.OrdinalIgnoreCase) || string.Equals(sel.FromTable.TableName, aliasOrTable, StringComparison.OrdinalIgnoreCase)))
                    return (sel.FromTable.ConnectionName, sel.FromTable.TableName);
                foreach (var join in sel.Joins)
                    if (join.Table != null && (string.Equals(join.Table.Alias, aliasOrTable, StringComparison.OrdinalIgnoreCase) || string.Equals(join.Table.TableName, aliasOrTable, StringComparison.OrdinalIgnoreCase)))
                        return (join.Table.ConnectionName, join.Table.TableName);
            }
            return null;
        }

        private static void CollectAvailableVariables(Statement? stmt, int currentLine, HashSet<string> vars)
        {
            if (stmt == null) return;

            if (stmt is DeclareStatement declare && declare.Line <= currentLine)
                vars.Add(declare.VariableName);
            else if (stmt is SetVariableStatement setVar && setVar.Line <= currentLine)
                vars.Add(setVar.VariableName);
            else if (stmt is ForStatement forLoop && currentLine >= forLoop.Line && currentLine <= forLoop.EndLine)
            {
                vars.Add(forLoop.VariableName);
                CollectAvailableVariables(forLoop.Body, currentLine, vars);
            }
            else if (stmt is ForeachStatement foreachLoop && currentLine >= foreachLoop.Line && currentLine <= foreachLoop.EndLine)
            {
                vars.Add(foreachLoop.VariableName);
                CollectAvailableVariables(foreachLoop.Body, currentLine, vars);
            }
            else if (stmt is BlockStatement block)
                foreach (var s in block.Statements) CollectAvailableVariables(s, currentLine, vars);
            else if (stmt is IfStatement ifStmt && currentLine >= ifStmt.Line && currentLine <= ifStmt.EndLine)
            {
                CollectAvailableVariables(ifStmt.IfBody, currentLine, vars);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) CollectAvailableVariables(ei.Body, currentLine, vars);
                if (ifStmt.ElseBody != null) CollectAvailableVariables(ifStmt.ElseBody, currentLine, vars);
            }
            else if (stmt is WhileStatement whileStmt && currentLine >= whileStmt.Line && currentLine <= whileStmt.EndLine)
                CollectAvailableVariables(whileStmt.Body, currentLine, vars);
            else if (stmt is TryCatchStatement tryCatch && currentLine >= tryCatch.Line && currentLine <= tryCatch.EndLine)
            {
                CollectAvailableVariables(tryCatch.TryBody, currentLine, vars);
                CollectAvailableVariables(tryCatch.CatchBody, currentLine, vars);
            }
            else if (stmt is CreateProcedureStatement proc && currentLine >= proc.Line && currentLine <= proc.EndLine)
            {
                foreach (var p in proc.Parameters) vars.Add(p.Name);
                if (proc.Body != null) CollectAvailableVariables(proc.Body, currentLine, vars);
            }
        }

        public CompletionRegistrationOptions GetRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
            => new CompletionRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("etlsql"),
                ResolveProvider  = false,
                TriggerCharacters = new Container<string>(" ", ".", "*")
            };
    }
}
