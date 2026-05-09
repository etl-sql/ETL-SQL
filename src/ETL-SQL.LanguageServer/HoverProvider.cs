using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core;
using ETL_SQL.Analysis.Linting;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Handles hover requests. Renders the lineage graph and metadata for the column under the cursor.
    /// </summary>
    public class HoverProvider : IHoverHandler
    {
        private readonly ILogger<HoverProvider> _logger;
        private readonly DocumentStateStore _store;
        private readonly Core.Functions.IFunctionRegistry _functionRegistry;
        private readonly Core.Interfaces.ILanguageHelpRegistry _languageHelp;
        private readonly DatasetStore _datasets;

        public HoverProvider(ILogger<HoverProvider> logger, DocumentStateStore store, Core.Functions.IFunctionRegistry functionRegistry, Core.Interfaces.ILanguageHelpRegistry languageHelp, DatasetStore datasets)
        {
            _logger = logger;
            _store = store;
            _functionRegistry = functionRegistry;
            _languageHelp = languageHelp;
            _datasets = datasets;
        }

        public Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Hover requested for {Uri} at {Line}:{Col}",
                request.TextDocument.Uri, request.Position.Line, request.Position.Character);

            if (!_store.TryGetState(request.TextDocument.Uri, out var state))
                return Task.FromResult<Hover?>(null);

            int line = (int)request.Position.Line + 1;
            int col  = (int)request.Position.Character + 1;

            var entries = state.Lineage.GetFullLineage().ToList();
            var entry = entries.FirstOrDefault(e =>
                (line > e.Line || (line == e.Line && col >= e.Column)) &&
                (line < e.EndLine || (line == e.EndLine && col <= e.EndColumn)));

            // Word detection for function help
            var lines = state.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string? word = null;
            if (line <= lines.Length)
            {
                var currentLine = lines[line - 1];
                int start = (int)request.Position.Character;
                if (start < currentLine.Length)
                {
                    while (start > 0 && (char.IsLetterOrDigit(currentLine[start - 1]) || currentLine[start - 1] == '_' || currentLine[start - 1] == '@' || currentLine[start - 1] == '#' || currentLine[start - 1] == '&')) start--;
                    int end = (int)request.Position.Character;
                    while (end < currentLine.Length && (char.IsLetterOrDigit(currentLine[end]) || currentLine[end] == '_' || currentLine[end] == '@' || currentLine[end] == '#' || currentLine[end] == '&')) end++;
                    if (start < end) word = currentLine.Substring(start, end - start);
                }
            }

            string? functionHelp = word != null ? _functionRegistry.GetHelp(word) : null;
            string? keywordHelp  = (word != null && functionHelp == null) ? _languageHelp.GetHelp(word) : null;

            // Dataset hover: word starts with & — look up in portal registry
            DatasetStore.DatasetEntry? datasetEntry = null;
            if (word != null && word.StartsWith('&'))
                datasetEntry = _datasets.Find(word);

            if (entry == null && functionHelp == null && keywordHelp == null && datasetEntry == null)
                return Task.FromResult<Hover?>(null);

            var md = new List<string>();

            if (entry != null)
            {
                md.Add($"**Column**: `{entry.TargetColumn}`");

                if (entry.Metadata.Count > 0)
                {
                    md.Add("### Metadata");
                    foreach (var m in entry.Metadata)
                    {
                        var key = m.Key.Equals("d", StringComparison.OrdinalIgnoreCase) ? "Description" : m.Key;
                        md.Add($"- **{key}**: {m.Value}");
                    }
                }
                else if (!string.IsNullOrEmpty(entry.Description))
                {
                    md.Add($"**Description**: {entry.Description}");
                }

                if (!string.IsNullOrEmpty(entry.DerivedFromDescriptions))
                    md.Add($"> [!NOTE]\n> Derived from: {entry.DerivedFromDescriptions}");

                var renderer = new LineageGraphRenderer();
                string graph = renderer.Render(state.Lineage, entry.TargetTable, entry.TargetColumn);
                md.Add("### Lineage Graph");
                md.Add($"```text\n{graph.TrimEnd()}\n```");
            }

            if (datasetEntry != null)
            {
                if (md.Count > 0) md.Add("---");
                md.Add($"### Dataset `{datasetEntry.Name}`");
                md.Add($"- **Folder**: `{datasetEntry.FolderPath}`");
                md.Add($"- **Access**: {datasetEntry.AccessLevel}");
                md.Add($"- **Rows**: {datasetEntry.RowCount:N0}");
                if (datasetEntry.LastRefresh.HasValue)
                    md.Add($"- **Last Refresh**: {datasetEntry.LastRefresh.Value:u}");
                else
                    md.Add("- **Last Refresh**: _never_");
                if (!string.IsNullOrWhiteSpace(datasetEntry.Ttl))
                    md.Add($"- **TTL**: {datasetEntry.Ttl}");
                if (datasetEntry.IsStale)
                    md.Add("> ⚠ This dataset is stale and will re-materialise on next script execution.");
            }

            if (functionHelp != null)
            {
                if (md.Count > 0) md.Add("---");
                md.Add("### Function Help");
                md.Add(functionHelp);
            }

            if (keywordHelp != null)
            {
                if (md.Count > 0) md.Add("---");
                md.Add("### Help");
                md.Add(keywordHelp);
            }

            var content = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = string.Join("\n\n", md)
            });

            return Task.FromResult<Hover?>(new Hover { Contents = content });
        }

        public HoverRegistrationOptions GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
            => new HoverRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql") };
    }
}
