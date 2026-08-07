using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Formatting;
using Microsoft.Extensions.Configuration;
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
    /// Handles document formatting requests using the ETL-SQL <see cref="SqlFormatter"/>.
    /// </summary>
    public class FormattingProvider : IDocumentFormattingHandler, IDocumentRangeFormattingHandler
    {
        private readonly ILogger<FormattingProvider> _logger;
        private readonly DocumentStateStore _store;
        private readonly ILanguageServerConfiguration _configuration;

        public FormattingProvider(ILogger<FormattingProvider> logger, DocumentStateStore store, ILanguageServerConfiguration configuration)
        {
            _logger = logger;
            _store = store;
            _configuration = configuration;
        }

        public Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
        {
            if (!_store.TryGetState(request.TextDocument.Uri, out var state))
                return Task.FromResult<TextEditContainer?>(null);

            string? filePath = null;
            try
            {
                filePath = new System.Uri(request.TextDocument.Uri.ToString()).LocalPath;
            }
            catch { }

            // 1. Try loading from workspace or local folder .etlsqlformat.json first
            var options = FormatterOptions.LoadFromWorkspace(filePath);
            if (options == null)
            {
                // 2. If no workspace file, fall back to VS Code client configuration via LSP Configuration
                options = new FormatterOptions();
                try
                {
                    var formatSection = _configuration.GetSection("etlsql:format");
                    if (formatSection.Exists())
                    {
                        var keywordCasing = formatSection.GetValue<string>("keywordCasing");
                        if (!string.IsNullOrEmpty(keywordCasing)) options.KeywordCasing = keywordCasing;

                        var indentSize = formatSection.GetValue<int?>("indentSize");
                        if (indentSize.HasValue) options.IndentSize = indentSize.Value;

                        var commaPlacement = formatSection.GetValue<string>("commaPlacement");
                        if (!string.IsNullOrEmpty(commaPlacement)) options.CommaPlacement = commaPlacement;

                        var indentJoins = formatSection.GetValue<bool?>("indentJoins");
                        if (indentJoins.HasValue) options.IndentJoins = indentJoins.Value;

                        var onClauseOnNewLine = formatSection.GetValue<bool?>("onClauseOnNewLine");
                        if (onClauseOnNewLine.HasValue) options.OnClauseOnNewLine = onClauseOnNewLine.Value;

                        var caseWhenThenNewLine = formatSection.GetValue<bool?>("caseWhenThenNewLine");
                        if (caseWhenThenNewLine.HasValue) options.CaseWhenThenNewLine = caseWhenThenNewLine.Value;

                        var breakoutWindowFunctions = formatSection.GetValue<bool?>("breakoutWindowFunctions");
                        if (breakoutWindowFunctions.HasValue) options.BreakoutWindowFunctions = breakoutWindowFunctions.Value;

                    }
                    else
                    {
                        // 3. Fall back to executable folder config or defaults
                        options = FormatterOptions.LoadFromFile(filePath);
                    }
                }
                catch
                {
                    // 3. Fall back to executable folder config or defaults
                    options = FormatterOptions.LoadFromFile(filePath);
                }
            }

            var formatted = SqlFormatter.Format(state.Text, options);
            var lines = state.Text.Split('\n');
            var endLine = lines.Length - 1;
            var endCol = lines[endLine].Length;

            return Task.FromResult<TextEditContainer?>(new TextEditContainer(new TextEdit
            {
                Range = new LSPRange(0, 0, endLine, endCol),
                NewText = formatted
            }));
        }

        public Task<TextEditContainer> Handle(DocumentRangeFormattingParams request, CancellationToken cancellationToken)
        {
            if (!_store.TryGetState(request.TextDocument.Uri, out var state))
                return Task.FromResult(new TextEditContainer());

            string? filePath = null;
            try
            {
                filePath = new System.Uri(request.TextDocument.Uri.ToString()).LocalPath;
            }
            catch { }

            var options = FormatterOptions.LoadFromWorkspace(filePath);
            if (options == null)
            {
                options = new FormatterOptions();
                try
                {
                    var formatSection = _configuration.GetSection("etlsql:format");
                    if (formatSection.Exists())
                    {
                        var keywordCasing = formatSection.GetValue<string>("keywordCasing");
                        if (!string.IsNullOrEmpty(keywordCasing)) options.KeywordCasing = keywordCasing;

                        var indentSize = formatSection.GetValue<int?>("indentSize");
                        if (indentSize.HasValue) options.IndentSize = indentSize.Value;

                        var commaPlacement = formatSection.GetValue<string>("commaPlacement");
                        if (!string.IsNullOrEmpty(commaPlacement)) options.CommaPlacement = commaPlacement;

                        var indentJoins = formatSection.GetValue<bool?>("indentJoins");
                        if (indentJoins.HasValue) options.IndentJoins = indentJoins.Value;

                        var onClauseOnNewLine = formatSection.GetValue<bool?>("onClauseOnNewLine");
                        if (onClauseOnNewLine.HasValue) options.OnClauseOnNewLine = onClauseOnNewLine.Value;

                        var caseWhenThenNewLine = formatSection.GetValue<bool?>("caseWhenThenNewLine");
                        if (caseWhenThenNewLine.HasValue) options.CaseWhenThenNewLine = caseWhenThenNewLine.Value;

                        var breakoutWindowFunctions = formatSection.GetValue<bool?>("breakoutWindowFunctions");
                        if (breakoutWindowFunctions.HasValue) options.BreakoutWindowFunctions = breakoutWindowFunctions.Value;
                    }
                    else
                    {
                        options = FormatterOptions.LoadFromFile(filePath);
                    }
                }
                catch
                {
                    options = FormatterOptions.LoadFromFile(filePath);
                }
            }

            var range = request.Range;
            var lines = state.Text.Split('\n');
            var selectedLines = new List<string>();

            for (int lineIdx = range.Start.Line; lineIdx <= range.End.Line; lineIdx++)
            {
                if (lineIdx >= lines.Length) break;
                var lineText = lines[lineIdx];
                if (lineIdx == range.Start.Line && lineIdx == range.End.Line)
                {
                    selectedLines.Add(lineText.Substring(range.Start.Character, Math.Max(0, range.End.Character - range.Start.Character)));
                }
                else if (lineIdx == range.Start.Line)
                {
                    selectedLines.Add(lineText.Substring(range.Start.Character));
                }
                else if (lineIdx == range.End.Line)
                {
                    selectedLines.Add(lineText.Substring(0, Math.Min(lineText.Length, range.End.Character)));
                }
                else
                {
                    selectedLines.Add(lineText);
                }
            }

            var selectionText = string.Join("\n", selectedLines);
            var formatted = SqlFormatter.Format(selectionText, options);

            return Task.FromResult(new TextEditContainer(new TextEdit
            {
                Range = range,
                NewText = formatted
            }));
        }

        public DocumentFormattingRegistrationOptions GetRegistrationOptions(DocumentFormattingCapability capability, ClientCapabilities clientCapabilities)
            => new DocumentFormattingRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql", "rptsql") };

        public DocumentRangeFormattingRegistrationOptions GetRegistrationOptions(DocumentRangeFormattingCapability capability, ClientCapabilities clientCapabilities)
            => new DocumentRangeFormattingRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql", "rptsql") };
    }
}
