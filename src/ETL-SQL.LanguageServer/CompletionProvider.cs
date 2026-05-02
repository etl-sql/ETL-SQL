using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ETL_SQL.Core.Services;
using ETL_SQL.Core;

namespace ETL_SQL.LSP
{
    public class CompletionProvider : ICompletionHandler
    {
        private readonly Microsoft.Extensions.Logging.ILogger<CompletionProvider> _logger;
        private readonly DocumentStateStore _store;
        private readonly ILanguageService _languageService;

        public CompletionProvider(Microsoft.Extensions.Logging.ILogger<CompletionProvider> logger, DocumentStateStore store, ILanguageService languageService)
        {
            _logger = logger;
            _store = store;
            _languageService = languageService;
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
            
            // Calculate prefix and script before
            var prefix = "";
            var startCol = col;
            if (col > 0 && currentLine.Length >= col)
            {
                var beforeCursor = currentLine.Substring(0, col);
                var match = Regex.Match(beforeCursor, @"([#@\w\.\*]+)$");
                if (match.Success) 
                {
                    prefix = match.Value;
                    startCol = col - prefix.Length;
                }
            }

            var scriptBefore = string.Join("\n", lines.Take(line)) + (line > 0 ? "\n" : "") + currentLine.Substring(0, col);

            var context = new SuggestionContext
            {
                Prefix = prefix,
                FullScript = text,
                ScriptBefore = scriptBefore,
                DocumentUri = request.TextDocument.Uri.ToString()
            };

            var suggestions = await _languageService.GetSuggestionsAsync(context);

            var items = suggestions.Select(s => {
                bool isExpansion = s.Type == SuggestionType.Column && s.Text.Contains(",");
                
                return new CompletionItem
                {
                    Label = isExpansion ? "Expand columns" : s.Text,
                    Kind = isExpansion ? CompletionItemKind.Snippet : MapKind(s.Type),
                    Detail = s.Type.ToString(),
                    Documentation = s.Documentation != null ? new MarkupContent { Kind = MarkupKind.Markdown, Value = s.Documentation } : null,
                    SortText = s.Priority.ToString("D4") + "_" + s.Text,
                    InsertText = s.Text,
                    TextEdit = isExpansion ? new TextEdit { 
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col), 
                        NewText = s.Text 
                    } : null
                };
            }).ToList();

            return new CompletionList(items);
        }

        private CompletionItemKind MapKind(SuggestionType type)
        {
            return type switch
            {
                SuggestionType.Keyword => CompletionItemKind.Keyword,
                SuggestionType.Function => CompletionItemKind.Function,
                SuggestionType.Table => CompletionItemKind.Class,
                SuggestionType.Column => CompletionItemKind.Field,
                SuggestionType.Variable => CompletionItemKind.Variable,
                SuggestionType.Alias => CompletionItemKind.Reference,
                SuggestionType.Connection => CompletionItemKind.Module,
                SuggestionType.Path => CompletionItemKind.File,
                SuggestionType.OptionName => CompletionItemKind.Property,
                SuggestionType.OptionValue => CompletionItemKind.EnumMember,
                _ => CompletionItemKind.Text
            };
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
