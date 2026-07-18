using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core.Common;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationAnalysisService(
    IServiceProvider serviceProvider,
    WorkstationMetadataService metadataService)
{
    public async Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request)
    {
        var script = request.Script ?? string.Empty;
        if (string.IsNullOrWhiteSpace(script))
            return new AnalyzeResponse([]);

        var lines = SplitLines(script);
        var diagnostics = new List<AnalysisDiagnostic>();

        try
        {
            var documentUri = string.IsNullOrWhiteSpace(request.DocumentUri)
                ? "workstation-editor"
                : request.DocumentUri!;
            var ast = await metadataService.RegisterScriptMetadataAsync(script, documentUri);
            diagnostics.AddRange(AnalysisDiagnosticBuilder.FromParserDiagnostics(ast.Diagnostics, lines));

            var linter = LinterFactory.CreateWithAllRules(serviceProvider);
            var lintResults = await linter.AnalyzeAsync(ast, new DefaultLintContext
            {
                DocumentUri = documentUri,
                Metadata = metadataService.CreateLintMetadataProvider(documentUri)
            });
            diagnostics.AddRange(AnalysisDiagnosticBuilder.FromLintResults(lintResults, lines));
        }
        catch (Exception ex)
        {
            diagnostics.Add(AnalysisDiagnosticBuilder.FromException(ex, lines));
        }

        return new AnalyzeResponse(diagnostics
            .OrderByDescending(d => d.Severity == DiagnosticSeverity.Error)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn)
            .ToList());
    }

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
}

public sealed record AnalyzeRequest(string? Script, string? DocumentUri);

public sealed record AnalyzeResponse(IReadOnlyList<AnalysisDiagnostic> Diagnostics);
