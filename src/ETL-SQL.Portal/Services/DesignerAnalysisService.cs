using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Portal.Models;
using ETL_SQL.Reporting.Authoring;
using Microsoft.Extensions.DependencyInjection;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Services;

public sealed class DesignerAnalysisService
{
    // Reading a script into design state is the host-neutral service's job. The Portal used to carry
    // its own ~300-line copy of it, which is how a one-line STRUCTURE fix reached the shared service
    // and not this one. The Portal keeps only what is genuinely its own: the AST statement ceiling,
    // the diagnostic contract, and the DTO shape the browser consumes.
    private readonly DesignerScriptParsingService _parsing = new();

    public ParseDesignerResponse Parse(string? script, int maxAstStatements)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new ParseDesignerResponse(EmptyState(), null);

        try
        {
            var ast = ParseScript(script);
            ValidateAstLimit(ast, maxAstStatements);

            // The parser recovers from most syntax errors instead of throwing, so an exception is not
            // the only way a script can be broken. Reporting Error == null for a recovered parse handed
            // the caller a design state built from a damaged AST — the canvas would render it, and the
            // "keep the last valid canvas" guard never fired. Gate on the same condition the patcher
            // does, so a script the patcher refuses to touch is a script the canvas refuses to adopt.
            var firstError = ast.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (firstError is not null)
                return new ParseDesignerResponse(EmptyState(), FormatDiagnostic(firstError));

            return new ParseDesignerResponse(_parsing.Parse(ast, script).ToStateDto(), null);
        }
        catch (DesignerAstLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ParseDesignerResponse(EmptyState(), ex.Message);
        }
    }

    public async Task<AnalyzeDesignerResponse> AnalyzeAsync(
        string? script,
        string? documentUri,
        int maxAstStatements,
        IServiceProvider? serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new AnalyzeDesignerResponse([]);

        var lines = SplitLines(script);
        var diagnostics = new List<AnalysisDiagnostic>();

        try
        {
            var ast = ParseScript(script);
            ValidateAstLimit(ast, maxAstStatements);
            diagnostics.AddRange(AnalysisDiagnosticBuilder.FromParserDiagnostics(ast.Diagnostics, lines));

            var linter = LinterFactory.CreateWithAllRules(serviceProvider);
            var lintContext = new DefaultLintContext
            {
                DocumentUri = string.IsNullOrWhiteSpace(documentUri) ? "portal-designer" : documentUri!
            };
            var lintResults = await linter.AnalyzeAsync(ast, lintContext);
            diagnostics.AddRange(AnalysisDiagnosticBuilder.FromLintResults(lintResults, lines));

            // Register the temp tables this script declares so the editor's session explorer and
            // autocomplete can see them. Connections are deliberately NOT registered here: in the
            // Portal they come from the ACL-gated shared catalog, never from the script.
            if (serviceProvider?.GetService<IMetadataManager>() is { } metadata)
            {
                var discovery = new ScriptMetadataDiscovery(metadata) { RegisterConnections = false };
                await discovery.DiscoverAsync(ast, lintContext.DocumentUri);
            }
        }
        catch (DesignerAstLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add(AnalysisDiagnosticBuilder.FromException(ex, lines));
        }

        var ordered = diagnostics
            .OrderByDescending(d => d.Severity == DiagnosticSeverity.Error)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn)
            .ToList();
        return new AnalyzeDesignerResponse(ordered);
    }

    private static string FormatDiagnostic(Diagnostic diagnostic) =>
        diagnostic.Line > 0
            ? $"Line {diagnostic.Line}, column {diagnostic.Column}: {diagnostic.Message}"
            : diagnostic.Message;

    private static Script ParseScript(string script)
    {
        var tokens = new Lexer(script).Tokenize();
        return new CoreParser(tokens, script).Parse();
    }

    private static void ValidateAstLimit(Script ast, int maxAstStatements)
    {
        if (ast.Statements.Count > maxAstStatements)
            throw new DesignerAstLimitExceededException(maxAstStatements);
    }

    // One blank page, so a script with nothing to draw still gives the canvas something to draw on.
    // studio.js synthesised exactly this page when the server sent none; the two now agree.
    private static DesignerStateDto EmptyState() =>
        DesignerScriptParsingService.EmptyState().ToStateDto();

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
}

public sealed class DesignerAstLimitExceededException(int maxStatements) : Exception(
    $"Designer script exceeds the {maxStatements} statement complexity limit.")
{
    public int MaxStatements { get; } = maxStatements;
}
