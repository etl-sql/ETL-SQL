using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Singleton store for parsed document state. Shared between TextDocumentHandler (writer)
    /// and all LSP feature providers (readers: Hover, Definition, Completion, Formatting).
    /// </summary>
    public class DocumentStateStore
    {
        private readonly ConcurrentDictionary<DocumentUri, DocumentState> _states = new();

        // Stores only parser-level diagnostics so the slow lint pass can append to them
        // without needing to re-run the full fast parse path.
        private readonly ConcurrentDictionary<DocumentUri, List<Diagnostic>> _parserDiagnostics = new();

        /// <summary>Updates the stored state for a document after a successful parse + analysis cycle.</summary>
        public void SetState(DocumentUri uri, string text, Script script, ILineageTracker lineage)
        {
            script ??= new Script();
            _states[uri] = new DocumentState(text, script, lineage, BuildDeclarationIndex(script));
        }

        /// <summary>Updates only the raw text of a document state, leaving the previous AST and lineage intact (or initializing if first time).</summary>
        public void UpdateText(DocumentUri uri, string text)
        {
            _states.AddOrUpdate(uri,
                _ => new DocumentState(text, new Script(), new LineageTracker(ETL_SQL.Common.NullLogger.Instance)),
                (_, oldState) => oldState with { Text = text });
        }

        /// <summary>Attempts to retrieve the current state for a document URI.</summary>
        public bool TryGetState(DocumentUri uri, out DocumentState state)
            => _states.TryGetValue(uri, out state!);

        /// <summary>Removes the stored state for a document.</summary>
        public void RemoveState(DocumentUri uri)
            => _states.TryRemove(uri, out _);

        /// <summary>Returns the raw text of a document, or null if the URI is not tracked.</summary>
        public string? GetDocumentText(DocumentUri uri)
            => _states.TryGetValue(uri, out var s) ? s.Text : null;

        /// <summary>Convenience alias for <see cref="GetDocumentText"/> used by the debounced lint path.</summary>
        public string? GetText(DocumentUri uri) => GetDocumentText(uri);

        /// <summary>Returns the current document state, or null if the URI is not tracked.</summary>
        public DocumentState? GetState(DocumentUri uri)
            => _states.TryGetValue(uri, out var s) ? s : null;

        /// <summary>Stores the parser-only diagnostics produced by <c>FastAnalyzeAsync</c>.</summary>
        public void SetParserDiagnostics(DocumentUri uri, List<Diagnostic> diagnostics)
            => _parserDiagnostics[uri] = diagnostics;

        /// <summary>Returns the last cached parser diagnostics, or an empty list if none stored yet.</summary>
        public List<Diagnostic> GetParserDiagnostics(DocumentUri uri)
            => _parserDiagnostics.TryGetValue(uri, out var d) ? d : new List<Diagnostic>();

        private readonly ConcurrentDictionary<string, NotebookContext> _notebookContexts = new();

        public void SetNotebookContext(string uri, string prefix, string path)
        {
            _notebookContexts[uri] = new NotebookContext(prefix, path);
        }

        public string GetNotebookPrefix(string uri)
            => _notebookContexts.TryGetValue(uri, out var context) ? context.Prefix : "";

        public string? GetNotebookPath(string uri)
            => _notebookContexts.TryGetValue(uri, out var context) ? context.Path : null;

        public bool TryFindDeclaration(string name, DocumentUri preferredUri, out DocumentUri uri, out DocumentDeclaration declaration)
        {
            if (_states.TryGetValue(preferredUri, out var preferredState) &&
                preferredState.Declarations.TryGetValue(name, out declaration!))
            {
                uri = preferredUri;
                return true;
            }

            foreach (var pair in _states)
            {
                if (pair.Key.Equals(preferredUri)) continue;
                if (pair.Value.Declarations.TryGetValue(name, out declaration!))
                {
                    uri = pair.Key;
                    return true;
                }
            }

            uri = default!;
            declaration = default!;
            return false;
        }

        private static IReadOnlyDictionary<string, DocumentDeclaration> BuildDeclarationIndex(Script script)
        {
            var declarations = new Dictionary<string, DocumentDeclaration>(StringComparer.OrdinalIgnoreCase);
            if (script.Statements == null) return declarations;

            foreach (var statement in script.Statements)
                AddDeclarations(statement, declarations);

            return declarations;
        }

        private static void AddDeclarations(Statement stmt, Dictionary<string, DocumentDeclaration> declarations)
        {
            switch (stmt)
            {
                case SectionLabelStatement sls:
                    AddDeclaration(declarations, sls.LabelName, sls.Line, sls.Column);
                    break;
                case DeclareStatement ds:
                    AddDeclaration(declarations, ds.VariableName, ds.Line, ds.Column);
                    break;
                case CreateTableStatement cts:
                    AddDeclaration(declarations, cts.TargetTable.TableName, cts.Line, cts.Column);
                    break;
                case CreateConnectionStatement ccs:
                    AddDeclaration(declarations, ccs.ConnectionName, ccs.Line, ccs.Column);
                    break;
                case ForStatement fs:
                    AddDeclaration(declarations, fs.VariableName, fs.Line, fs.Column);
                    AddDeclarations(fs.Body, declarations);
                    break;
                case ForeachStatement fes:
                    AddDeclaration(declarations, fes.VariableName, fes.Line, fes.Column);
                    AddDeclarations(fes.Body, declarations);
                    break;
                case BlockStatement block:
                    foreach (var child in block.Statements)
                        AddDeclarations(child, declarations);
                    break;
                case IfStatement ifStmt:
                    AddDeclarations(ifStmt.IfBody, declarations);
                    if (ifStmt.ElseIfClauses != null)
                    {
                        foreach (var elseIf in ifStmt.ElseIfClauses)
                            AddDeclarations(elseIf.Body, declarations);
                    }
                    if (ifStmt.ElseBody != null)
                        AddDeclarations(ifStmt.ElseBody, declarations);
                    break;
                case WhileStatement whileStmt:
                    AddDeclarations(whileStmt.Body, declarations);
                    break;
                case TryCatchStatement tryCatch:
                    AddDeclarations(tryCatch.TryBody, declarations);
                    AddDeclarations(tryCatch.CatchBody, declarations);
                    break;
            }
        }

        private static void AddDeclaration(Dictionary<string, DocumentDeclaration> declarations, string name, int line, int column)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            declarations.TryAdd(name, new DocumentDeclaration(name, line, column));
        }
    }

    /// <summary>Immutable snapshot of a parsed document.</summary>
    public record DocumentState(string Text, Script Script, ILineageTracker Lineage, IReadOnlyDictionary<string, DocumentDeclaration> Declarations)
    {
        public DocumentState(string text, Script script, ILineageTracker lineage)
            : this(text, script, lineage, new Dictionary<string, DocumentDeclaration>(StringComparer.OrdinalIgnoreCase))
        {
        }
    }

    public record DocumentDeclaration(string Name, int Line, int Column);

    /// <summary>Immutable notebook context so prefix and path are updated atomically.</summary>
    public record NotebookContext(string Prefix, string Path);
}
