using System.Collections.Concurrent;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Singleton store for parsed document state. Shared between TextDocumentHandler (writer)
    /// and all LSP feature providers (readers: Hover, Definition, Completion, Formatting).
    /// </summary>
    public class DocumentStateStore
    {
        private readonly ConcurrentDictionary<DocumentUri, DocumentState> _states = new();

        /// <summary>Updates the stored state for a document after a successful parse + analysis cycle.</summary>
        public void SetState(DocumentUri uri, string text, Script script, ILineageTracker lineage)
            => _states[uri] = new DocumentState(text, script, lineage);

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

        private readonly ConcurrentDictionary<string, string> _notebookPrefixes = new();
        private readonly ConcurrentDictionary<string, string> _notebookPaths = new();

        public void SetNotebookContext(string uri, string prefix, string path)
        {
            _notebookPrefixes[uri] = prefix;
            _notebookPaths[uri] = path;
        }

        public string GetNotebookPrefix(string uri)
            => _notebookPrefixes.TryGetValue(uri, out var prefix) ? prefix : "";

        public string? GetNotebookPath(string uri)
            => _notebookPaths.TryGetValue(uri, out var path) ? path : null;
    }

    /// <summary>Immutable snapshot of a parsed document.</summary>
    public record DocumentState(string Text, Script Script, ILineageTracker Lineage);
}
