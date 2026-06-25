using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
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
            int col = (int)request.Position.Character + 1;

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

            if (state.Declarations.TryGetValue(word, out var declaration))
            {
                var range = new LSPRange(
                    declaration.Line - 1,
                    declaration.Column - 1,
                    declaration.Line - 1,
                    declaration.Column - 1 + declaration.Name.Length);
                var location = new Location { Uri = request.TextDocument.Uri, Range = range };
                return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
            }

            return Task.FromResult<LocationOrLocationLinks?>(default);
        }

        public DefinitionRegistrationOptions GetRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
            => new DefinitionRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql") };
    }
}
