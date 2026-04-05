using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ETL_SQL.Core.Formatting;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Handles document formatting requests using the ETL-SQL <see cref="SqlFormatter"/>.
    /// </summary>
    public class FormattingProvider : IDocumentFormattingHandler
    {
        private readonly ILogger<FormattingProvider> _logger;
        private readonly DocumentStateStore _store;

        public FormattingProvider(ILogger<FormattingProvider> logger, DocumentStateStore store)
        {
            _logger = logger;
            _store  = store;
        }

        public Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
        {
            if (!_store.TryGetState(request.TextDocument.Uri, out var state))
                return Task.FromResult<TextEditContainer?>(null);

            var formatted = SqlFormatter.Format(state.Text);
            var lines  = state.Text.Split('\n');
            var endLine = lines.Length - 1;
            var endCol  = lines[endLine].Length;

            return Task.FromResult<TextEditContainer?>(new TextEditContainer(new TextEdit
            {
                Range   = new LSPRange(0, 0, endLine, endCol),
                NewText = formatted
            }));
        }

        public DocumentFormattingRegistrationOptions GetRegistrationOptions(DocumentFormattingCapability capability, ClientCapabilities clientCapabilities)
            => new DocumentFormattingRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql") };
    }
}
