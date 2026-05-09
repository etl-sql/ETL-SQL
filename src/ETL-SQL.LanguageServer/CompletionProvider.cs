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
        private readonly DatasetStore _datasets;

        public CompletionProvider(Microsoft.Extensions.Logging.ILogger<CompletionProvider> logger, DocumentStateStore store, ILanguageService languageService, DatasetStore datasets)
        {
            _logger = logger;
            _store = store;
            _languageService = languageService;
            _datasets = datasets;
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
            
            // Calculate prefix and script before.
            // Include & so that &datasetName is captured as a single prefix token.
            var prefix = "";
            var startCol = col;
            if (col > 0 && currentLine.Length >= col)
            {
                var beforeCursor = currentLine.Substring(0, col);
                var match = Regex.Match(beforeCursor, @"([&\#@\w\.\*]+)$");
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

            // Inject dataset-name suggestions when context is USE DATASET or prefix starts with &
            var datasetItems = GetDatasetCompletions(scriptBefore, prefix, line, startCol, col);

            var items = suggestions.Select(s => {
                bool isExpansion = s.Type == SuggestionType.Column && s.Text.Contains(",");
                
                return new CompletionItem
                {
                    Label = isExpansion ? "Expand columns" : s.Text,
                    Kind = isExpansion ? CompletionItemKind.Snippet : MapKind(s.Type),
                    Detail = s.Type.ToString(),
                    Documentation = s.Documentation != null ? new MarkupContent { Kind = MarkupKind.Markdown, Value = s.Documentation } : null,
                    SortText = s.Priority.ToString("D4") + "_" + s.Text,
                    FilterText = isExpansion ? prefix : s.Text,
                    InsertText = s.Text,
                    TextEdit = new TextEditOrInsertReplaceEdit(new TextEdit { 
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col), 
                        NewText = s.Text 
                    })
                };
            }).ToList();

            return new CompletionList(items.Concat(datasetItems).ToList());
        }

        // ── Dataset name completions ──────────────────────────────────────────

        private List<CompletionItem> GetDatasetCompletions(string scriptBefore, string prefix, int line, int startCol, int col)
        {
            var all = _datasets.GetAll();
            if (all.Count == 0) return [];

            // Trigger when: (a) prefix starts with &, OR (b) context ends with USE DATASET
            bool prefixIsDataset = prefix.StartsWith('&');
            bool useDatasetContext = IsUseDatasetContext(scriptBefore);

            if (!prefixIsDataset && !useDatasetContext) return [];

            // Filter by prefix (strip & for matching against stored name)
            var nameFilter = prefix.TrimStart('&');

            var items = new List<CompletionItem>();
            foreach (var entry in all)
            {
                var rawName  = entry.Name.StartsWith('&') ? entry.Name : "&" + entry.Name.TrimStart('#');
                var stripped = rawName.TrimStart('&');

                if (!string.IsNullOrEmpty(nameFilter)
                    && !stripped.StartsWith(nameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var staleBadge = entry.IsStale ? " ⚠ stale" : "";
                var detail     = $"{entry.FolderPath}  ·  {entry.RowCount:N0} rows  ·  {entry.AccessLevel}{staleBadge}";

                items.Add(new CompletionItem
                {
                    Label         = rawName,
                    Kind          = CompletionItemKind.Reference,
                    Detail        = detail,
                    SortText      = "0000_" + rawName,
                    FilterText    = rawName,
                    InsertText    = rawName,
                    TextEdit      = new TextEditOrInsertReplaceEdit(new TextEdit
                    {
                        Range   = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col),
                        NewText = rawName
                    })
                });
            }

            return items;
        }

        private static bool IsUseDatasetContext(string scriptBefore)
        {
            // Look for USE DATASET (whitespace) at the end of the script so far
            return Regex.IsMatch(scriptBefore, @"\bUSE\s+DATASET\s+$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(scriptBefore, @"\bSOURCE\s*=\s*$", RegexOptions.IgnoreCase);
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
