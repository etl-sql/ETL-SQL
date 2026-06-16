using System;
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
    public class FormattingProvider : IDocumentFormattingHandler
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

                        // Sync legacy / composite settings
                        if (options.KeywordCasing.Equals("upper", StringComparison.OrdinalIgnoreCase))
                            options.UpperCaseKeywords = true;
                        else if (options.KeywordCasing.Equals("lower", StringComparison.OrdinalIgnoreCase) || options.KeywordCasing.Equals("pascal", StringComparison.OrdinalIgnoreCase))
                            options.UpperCaseKeywords = false;
                        
                        if (options.CommaPlacement.Equals("leading", StringComparison.OrdinalIgnoreCase))
                            options.LeadingCommas = true;
                        else if (options.CommaPlacement.Equals("trailing", StringComparison.OrdinalIgnoreCase))
                            options.LeadingCommas = false;
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

        public DocumentFormattingRegistrationOptions GetRegistrationOptions(DocumentFormattingCapability capability, ClientCapabilities clientCapabilities)
            => new DocumentFormattingRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("etlsql") };
    }
}
