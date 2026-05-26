using System;
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
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Handles 'Go to Definition' requests. Navigates to variable, table, or connection declarations.
    /// </summary>
    public class DefinitionProvider : IDefinitionHandler
    {
        private readonly ILogger<DefinitionProvider> _logger;
        private readonly DocumentStateStore _store;

        public DefinitionProvider(ILogger<DefinitionProvider> logger, DocumentStateStore store)
        {
            _logger = logger;
            _store = store;
        }

        public Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
        {
            if (!_store.TryGetState(request.TextDocument.Uri, out var state))
                return Task.FromResult<LocationOrLocationLinks?>(default);

            int line = (int)request.Position.Line + 1;
            int col  = (int)request.Position.Character + 1;

            var lines = state.Text.Split('\n');
            if (line > lines.Length)
                return Task.FromResult<LocationOrLocationLinks?>(default);

            var currentLine = lines[line - 1];

            // Find word boundaries around cursor
            int start = (int)request.Position.Character;
            while (start > 0 && (char.IsLetterOrDigit(currentLine[start - 1]) || currentLine[start - 1] == '@' || currentLine[start - 1] == '#' || currentLine[start - 1] == '_')) start--;
            int end = (int)request.Position.Character;
            while (end < currentLine.Length && (char.IsLetterOrDigit(currentLine[end]) || currentLine[end] == '@' || currentLine[end] == '#' || currentLine[end] == '_')) end++;

            var word = currentLine.Substring(start, end - start);
            if (string.IsNullOrEmpty(word))
                return Task.FromResult<LocationOrLocationLinks?>(default);

            foreach (var stmt in state.Script.Statements)
            {
                var loc = FindDeclaration(stmt, word, request.TextDocument.Uri);
                if (loc is not null)
                    return Task.FromResult<LocationOrLocationLinks?>(loc);
            }

            return Task.FromResult<LocationOrLocationLinks?>(default);
        }

        private static LocationOrLocationLinks? FindDeclaration(Statement stmt, string name, DocumentUri uri)
        {
            if (stmt is SectionLabelStatement sls && string.Equals(sls.LabelName, name, StringComparison.OrdinalIgnoreCase))
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(sls.Line - 1, sls.Column - 1, sls.Line - 1, sls.Column - 1 + name.Length) });

            if (stmt is DeclareStatement ds && string.Equals(ds.VariableName, name, StringComparison.OrdinalIgnoreCase))
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(ds.Line - 1, ds.Column - 1, ds.Line - 1, ds.Column - 1 + name.Length) });

            if (stmt is CreateTableStatement cts && string.Equals(cts.TargetTable.TableName, name, StringComparison.OrdinalIgnoreCase))
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(cts.Line - 1, cts.Column - 1, cts.Line - 1, cts.Column - 1 + name.Length) });

            if (stmt is CreateConnectionStatement ccs && string.Equals(ccs.ConnectionName, name, StringComparison.OrdinalIgnoreCase))
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(ccs.Line - 1, ccs.Column - 1, ccs.Line - 1, ccs.Column - 1 + name.Length) });

            if (stmt is ForStatement fs && string.Equals(fs.VariableName, name, StringComparison.OrdinalIgnoreCase))
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(fs.Line - 1, fs.Column - 1, fs.Line - 1, fs.Column - 1 + name.Length) });

            if (stmt is ForeachStatement fes && string.Equals(fes.VariableName, name, StringComparison.OrdinalIgnoreCase))
                return new LocationOrLocationLinks(new Location { Uri = uri, Range = new LSPRange(fes.Line - 1, fes.Column - 1, fes.Line - 1, fes.Column - 1 + name.Length) });

            if (stmt is BlockStatement block)
            {
                foreach (var s in block.Statements)
                {
                    var found = FindDeclaration(s, name, uri);
                    if (found is not null) return found;
                }
            }

            if (stmt is IfStatement ifStmt)
            {
                var found = FindDeclaration(ifStmt.IfBody, name, uri);
                if (found is not null) return found;
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses)
                    {
                        found = FindDeclaration(ei.Body, name, uri);
                        if (found is not null) return found;
                    }
                if (ifStmt.ElseBody != null)
                    return FindDeclaration(ifStmt.ElseBody, name, uri);
            }

            if (stmt is WhileStatement whileStmt)
                return FindDeclaration(whileStmt.Body, name, uri);

            if (stmt is TryCatchStatement tc)
            {
                var found = FindDeclaration(tc.TryBody, name, uri);
                if (found is not null) return found;
                return FindDeclaration(tc.CatchBody, name, uri);
            }

            return null;
        }

        public DefinitionRegistrationOptions GetRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
            => new DefinitionRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql") };
    }
}
