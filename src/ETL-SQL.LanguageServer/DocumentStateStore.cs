using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol;
using ETL_SQL.Core;
using ETL_SQL.Core.Linting;

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

        /// <summary>Attempts to retrieve the current state for a document URI.</summary>
        public bool TryGetState(DocumentUri uri, out DocumentState state)
            => _states.TryGetValue(uri, out state!);

        /// <summary>Returns the raw text of a document, or null if the URI is not tracked.</summary>
        public string? GetDocumentText(DocumentUri uri)
            => _states.TryGetValue(uri, out var s) ? s.Text : null;
    }

    /// <summary>Immutable snapshot of a parsed document.</summary>
    public record DocumentState(string Text, Script Script, ILineageTracker Lineage);
}
