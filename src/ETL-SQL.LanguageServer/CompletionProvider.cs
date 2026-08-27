using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Services;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

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
            var col = (int)request.Position.Character;

            if (!_store.TryGetState(request.TextDocument.Uri, out var state))
                return new CompletionList();

            var text = state.Text;
            var prefixStr = _store.GetNotebookPrefix(request.TextDocument.Uri.ToString());
            var prefixLines = 0;

            if (!string.IsNullOrEmpty(prefixStr))
            {
                prefixLines = prefixStr.Count(c => c == '\n');
                text = prefixStr + text;
            }

            var lines = text.Split('\n');
            var adjustedLine = line + prefixLines;
            var currentLine = lines.Length > adjustedLine ? lines[adjustedLine] : "";

            // Calculate prefix and script before.
            // Include & so that &datasetName is captured as a single prefix token.
            var prefix = "";
            var startCol = col;
            if (col > 0 && currentLine.Length >= col)
            {
                var beforeCursor = currentLine.Substring(0, col);
                var match = Regex.Match(beforeCursor, @"([\$&\#@\w\.\*]+)$");
                if (match.Success)
                {
                    prefix = match.Value;
                    startCol = col - prefix.Length;
                }
            }

            var scriptBefore = string.Join("\n", lines.Take(adjustedLine)) + (adjustedLine > 0 ? "\n" : "") + currentLine.Substring(0, col);

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
            var snippetItems = GetSnippetCompletions(scriptBefore, prefix, line, startCol, col);
            var chartItems = GetAdvancedChartCompletions(scriptBefore, prefix, line, startCol, col);
            var bookmarkItems = GetBookmarkCompletions(state.Script, scriptBefore, prefix, line, startCol, col);
            var htmlItems = GetHtmlVisualCompletions(state.Script, scriptBefore, prefix, line, startCol, col);

            // Where a bookmark identifier is the only thing that can legally appear, offering the
            // generic word list alongside it would just be noise — the declared bookmarks are the
            // complete set of valid answers.
            if (bookmarkItems.Count > 0) return new CompletionList(bookmarkItems);
            if (htmlItems.Count > 0 && (HtmlVisualSymbols.IsTemplateBindingContext(scriptBefore) || HtmlVisualSymbols.IsCssContext(scriptBefore)))
                return new CompletionList(htmlItems);

            var items = suggestions.Select(s =>
            {
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
                    TextEdit = new TextEditOrInsertReplaceEdit(new TextEdit
                    {
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col),
                        NewText = s.Text
                    })
                };
            }).ToList();

            return new CompletionList(snippetItems.Concat(chartItems).Concat(htmlItems).Concat(items).Concat(datasetItems)
                .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList());
        }

        private static List<CompletionItem> GetHtmlVisualCompletions(
            Script script, string scriptBefore, string prefix, int line, int startCol, int col)
        {
            IEnumerable<(string Label, CompletionItemKind Kind, string Detail)> symbols;
            var cssContext = HtmlVisualSymbols.IsCssContext(scriptBefore);
            if (cssContext)
            {
                symbols = HtmlVisualSymbols.ThemeTokens.Select(token => (token, CompletionItemKind.Variable, "Approved report theme token"));
            }
            else if (HtmlVisualSymbols.IsTemplateBindingContext(scriptBefore)
                && HtmlVisualSymbols.ActiveVisual(script, scriptBefore) is { } visual)
            {
                symbols = HtmlVisualSymbols.Columns(script, visual)
                    .Select(name => (name, CompletionItemKind.Field, "Escaped HTML source field"))
                    .Concat(HtmlVisualSymbols.Parameters(script)
                        .Select(parameter => ("@" + parameter.VariableName.TrimStart('@'), CompletionItemKind.Variable,
                            $"Escaped HTML parameter ({parameter.DataType})")));
            }
            else if (Regex.IsMatch(scriptBefore, @"(?is)\bCREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?VISUAL\s+\w+\s+AS\s+\w*$"))
            {
                symbols = [("HTML", CompletionItemKind.EnumMember, "Constrained semantic HTML visual")];
            }
            else return [];

            return symbols.Where(symbol => cssContext || string.IsNullOrEmpty(prefix)
                    || symbol.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(symbol => new CompletionItem
                {
                    Label = symbol.Label,
                    Kind = symbol.Kind,
                    Detail = symbol.Detail,
                    Documentation = new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = "Constrained HTML values are escaped and cannot execute author JavaScript."
                    },
                    SortText = "0001_" + symbol.Label,
                    InsertText = symbol.Label,
                    TextEdit = new TextEditOrInsertReplaceEdit(new TextEdit
                    {
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col),
                        NewText = symbol.Label
                    })
                }).ToList();
        }

        /// <summary>
        /// Offers the bookmarks declared in this script wherever a bookmark identifier is expected —
        /// inside <c>APPLY_BOOKMARK(</c> and after <c>DROP BOOKMARK</c>. Author bookmarks are the only
        /// valid values there, so completing them from the AST is what keeps an in-canvas action from
        /// being typed against a bookmark that does not exist.
        /// </summary>
        private static List<CompletionItem> GetBookmarkCompletions(
            ETL_SQL.Core.Script? script, string scriptBefore, string prefix, int line, int startCol, int col)
        {
            if (!BookmarkSymbols.ExpectsBookmarkName(scriptBefore)) return [];
            return BookmarkSymbols.Declared(script)
                .Where(bookmark => string.IsNullOrEmpty(prefix)
                    || bookmark.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(bookmark => new CompletionItem
                {
                    Label = bookmark.Name,
                    Kind = CompletionItemKind.Reference,
                    Detail = bookmark.IsDefault ? "Author bookmark (report default)" : "Author bookmark",
                    Documentation = new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = BookmarkSymbols.Describe(bookmark)
                    },
                    // Sorted ahead of everything else: in this position nothing else is valid.
                    SortText = "0000_" + bookmark.Name,
                    InsertText = bookmark.Name,
                    TextEdit = new TextEditOrInsertReplaceEdit(new TextEdit
                    {
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col),
                        NewText = bookmark.Name
                    })
                }).ToList();
        }

        private static List<CompletionItem> GetAdvancedChartCompletions(string scriptBefore, string prefix, int line, int startCol, int col)
        {
            var chartStart = scriptBefore.LastIndexOf("CHART", StringComparison.OrdinalIgnoreCase);
            if (chartStart < 0) return [];
            var scope = scriptBefore[chartStart..];
            if (scope.Count(character => character == '(') <= scope.Count(character => character == ')')) return [];
            string[] keywords =
            [
                "COORDINATE", "SCALES", "LAYERS", "ENCODINGS", "INHERIT_ENCODINGS", "DATUM", "VALUE", "STYLE", "CONDITIONS", "FACET", "RESOLVE",
                "CARTESIAN", "TRANSPOSED_CARTESIAN", "POLAR", "GEOGRAPHIC", "EQUIRECTANGULAR", "MERCATOR", "RECT", "LINE", "AREA", "POINT", "RULE", "ARC", "TEXT", "TICK",
                "LINEAR", "LOGARITHMIC", "TIME", "BAND", "POINT", "ORDINAL", "IDENTITY",
                "QUANTITATIVE", "TEMPORAL", "NOMINAL", "ORDINAL", "PRIMARY", "SECONDARY", "SHARED", "INDEPENDENT",
                "X_START", "X_END", "X_OFFSET", "Y_START", "Y_END", "Y_OFFSET", "STACK", "ZERO", "NORMALIZE",
                "BAND_SIZE", "POSITION", "JITTER", "NUDGE", "KEY", "SEED", "DATA", "EM", "WRAP", "COLUMNS",
                "ASPECT_RATIO", "PROJECTION", "MAP_NAME", "MAP_FILE", "FEATURE_KEY", "LONGITUDE", "LATITUDE", "REGION", "ROUTE", "RANGE", "GRADIENT", "DIVERGING", "LOW", "Q1", "MEDIAN", "Q3", "HIGH", "OPEN", "CLOSE", "MID", "MIDPOINT", "NULL_COLOR",
                "THICKNESS", "ORIENTATION", "AUTO", "HORIZONTAL", "VERTICAL"
            ];
            return keywords.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(keyword => string.IsNullOrEmpty(prefix) || keyword.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(keyword => new CompletionItem
                {
                    Label = keyword,
                    Kind = CompletionItemKind.Keyword,
                    Detail = "Native CHART keyword",
                    Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = "Renderer-neutral advanced chart authoring keyword." },
                    SortText = "0002_" + keyword,
                    InsertText = keyword,
                    TextEdit = new TextEditOrInsertReplaceEdit(new TextEdit
                    {
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col),
                        NewText = keyword
                    })
                }).ToList();
        }

        // ── Snippet completions ───────────────────────────────────────────────

        private List<CompletionItem> GetSnippetCompletions(string scriptBefore, string prefix, int line, int startCol, int col)
        {
            if (!prefix.StartsWith('$')) return [];
            if (!IsAtStatementStart(scriptBefore, prefix)) return [];

            var items = new List<CompletionItem>();
            foreach (var snippet in SnippetLibrary.Instance.GetByPrefix(prefix))
            {
                items.Add(new CompletionItem
                {
                    Label = snippet.Trigger,
                    Kind = CompletionItemKind.Snippet,
                    Detail = snippet.Label,
                    Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = snippet.Description },
                    SortText = "0001_" + snippet.Trigger,
                    FilterText = snippet.Trigger,
                    InsertText = snippet.LspBody,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    TextEdit = new TextEditOrInsertReplaceEdit(new TextEdit
                    {
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col),
                        NewText = snippet.LspBody
                    })
                });
            }
            return items;
        }

        private static bool IsAtStatementStart(string scriptBefore, string prefix)
        {
            // Snippet trigger must be the only non-whitespace content on the current line
            var lastNewline = scriptBefore.LastIndexOf('\n');
            var lineContent = lastNewline >= 0 ? scriptBefore.Substring(lastNewline + 1) : scriptBefore;
            return lineContent.TrimStart() == prefix;
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
                var rawName = entry.Name.StartsWith('&') ? entry.Name : "&" + entry.Name.TrimStart('#');
                var stripped = rawName.TrimStart('&');

                if (!string.IsNullOrEmpty(nameFilter)
                    && !stripped.StartsWith(nameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var staleBadge = entry.IsStale ? " ⚠ stale" : "";
                var detail = $"{entry.FolderPath}  ·  {entry.RowCount:N0} rows  ·  {entry.AccessLevel}{staleBadge}";

                items.Add(new CompletionItem
                {
                    Label = rawName,
                    Kind = CompletionItemKind.Reference,
                    Detail = detail,
                    SortText = "0000_" + rawName,
                    FilterText = rawName,
                    InsertText = rawName,
                    TextEdit = new TextEditOrInsertReplaceEdit(new TextEdit
                    {
                        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, startCol, line, col),
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
                DocumentSelector = TextDocumentSelector.ForLanguage("etlsql", "rptsql"),
                ResolveProvider = false,
                TriggerCharacters = new Container<string>(" ", ".", "*", "$")
            };
    }
}
