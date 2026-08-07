using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Implements document symbol capability to expose script labels in the outline.
    /// </summary>
    public class DocumentSymbolProvider : IDocumentSymbolHandler
    {
        private readonly DocumentStateStore _store;

        public DocumentSymbolProvider(DocumentStateStore store)
        {
            _store = store;
        }

        public Task<SymbolInformationOrDocumentSymbolContainer?> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
        {
            var container = new List<SymbolInformationOrDocumentSymbol>();

            if (!_store.TryGetState(request.TextDocument.Uri, out var state))
                return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(new SymbolInformationOrDocumentSymbolContainer(container));

            var symbols = new List<DocumentSymbol>();
            FindLabels(state.Script.Statements, symbols);

            var result = symbols.Select(s => new SymbolInformationOrDocumentSymbol(s));
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(new SymbolInformationOrDocumentSymbolContainer(result));
        }

        private void FindLabels(IEnumerable<Statement> statements, List<DocumentSymbol> symbols)
        {
            foreach (var stmt in statements)
            {
                if (stmt is SectionLabelStatement sls)
                {
                    var range = new LSPRange(sls.Line - 1, sls.Column - 1, sls.EndLine - 1, sls.EndColumn);
                    symbols.Add(new DocumentSymbol
                    {
                        Name = sls.LabelName,
                        Kind = SymbolKind.Key,
                        Range = range,
                        SelectionRange = range,
                        Detail = sls.IsTopLevel ? "Top-level Checkpoint" : "Control Flow Target"
                    });
                }
                else if (stmt is BlockStatement block)
                {
                    FindLabels(block.Statements, symbols);
                }
                else if (stmt is WhileStatement @while)
                {
                    FindLabels(new[] { @while.Body }, symbols);
                }
                else if (stmt is ForStatement @for)
                {
                    FindLabels(new[] { @for.Body }, symbols);
                }
                else if (stmt is ForeachStatement @foreach)
                {
                    FindLabels(new[] { @foreach.Body }, symbols);
                }
                else if (stmt is IfStatement @if)
                {
                    FindLabels(new[] { @if.IfBody }, symbols);
                    if (@if.ElseIfClauses != null)
                    {
                        foreach (var elseif in @if.ElseIfClauses)
                        {
                            FindLabels(new[] { elseif.Body }, symbols);
                        }
                    }
                    if (@if.ElseBody != null)
                    {
                        FindLabels(new[] { @if.ElseBody }, symbols);
                    }
                }
                else if (stmt is TryCatchStatement tc)
                {
                    FindLabels(new[] { tc.TryBody }, symbols);
                    FindLabels(new[] { tc.CatchBody }, symbols);
                }
                else if (stmt is ParallelStatement p)
                {
                    FindLabels(new[] { p.Body }, symbols);
                }
                else if (stmt is ParallelForStatement pf)
                {
                    FindLabels(new[] { pf.Body }, symbols);
                }
            }
        }

        public DocumentSymbolRegistrationOptions GetRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
            => new DocumentSymbolRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql", "rptsql") };
    }
}
