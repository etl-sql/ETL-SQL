using System.Text.RegularExpressions;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Services;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationCompletionService(
    ILanguageService languageService,
    WorkstationMetadataService metadataService)
{
    public async Task<CompleteResponse> CompleteAsync(CompleteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var script = request.Script ?? string.Empty;
        var (scriptBefore, prefix, currentLine) = GetCompletionPosition(script, request.Line, request.Column);
        var documentUri = string.IsNullOrWhiteSpace(request.DocumentUri)
            ? "workstation-editor"
            : request.DocumentUri!;
        await metadataService.RegisterScriptMetadataAsync(script, documentUri);

        var suggestions = await languageService.GetSuggestionsAsync(new SuggestionContext
        {
            Prefix = prefix,
            FullScript = script,
            ScriptBefore = scriptBefore,
            DocumentUri = documentUri
        });

        // Snippets lead: a `$trigger` match is an explicit request for that template, so burying it
        // under keyword suggestions would make the library undiscoverable in the GUI editors.
        var items = SnippetCompletionSource.GetMatches(scriptBefore, prefix)
            .Select(snippet => new CompletionItemResponse(
                snippet.Trigger,
                snippet.TuiBody,
                "snippet",
                snippet.Label,
                snippet.Description,
                Math.Max(0, request.Column - prefix.Length),
                request.Column))
            .ToList();

        items.AddRange(suggestions
            .Take(100)
            .Select(s => ToCompletionItem(s, prefix, currentLine, request.Column)));

        return new CompleteResponse(items);
    }

    private static CompletionItemResponse ToCompletionItem(Suggestion suggestion, string prefix, string currentLine, int column)
    {
        var isExpansion = suggestion.Type == SuggestionType.Column
            && suggestion.Text.Contains(",", StringComparison.Ordinal);
        var replacement = isExpansion
            ? FindStarReplacementRange(currentLine, column)
            : null;

        return new CompletionItemResponse(
            isExpansion ? "Expand columns" : suggestion.Text,
            suggestion.Text,
            isExpansion ? "snippet" : suggestion.Type.ToString().ToLowerInvariant(),
            isExpansion ? "Column expansion" : suggestion.Type.ToString(),
            isExpansion ? "Replace * with explicit column names." : suggestion.Documentation,
            replacement?.StartColumn,
            replacement?.EndColumn);
    }

    private static (string ScriptBefore, string Prefix, string CurrentLine) GetCompletionPosition(string script, int line, int column)
    {
        var lines = script.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        if (lines.Length == 0)
            return (string.Empty, string.Empty, string.Empty);

        var safeLine = Math.Clamp(line, 0, lines.Length - 1);
        var currentLine = lines[safeLine];
        var safeColumn = Math.Clamp(column, 0, currentLine.Length);
        var beforeCursor = currentLine[..safeColumn];
        var match = Regex.Match(beforeCursor, @"([\$&\#@\w\.\*]+)$");
        var prefix = match.Success ? match.Value : string.Empty;
        var scriptBefore = string.Join("\n", lines.Take(safeLine));
        if (safeLine > 0)
            scriptBefore += "\n";
        scriptBefore += beforeCursor;

        return (scriptBefore, prefix, currentLine);
    }

    private static (int StartColumn, int EndColumn)? FindStarReplacementRange(string currentLine, int column)
    {
        var safeColumn = Math.Clamp(column, 0, currentLine.Length);
        if (safeColumn > 0 && currentLine[safeColumn - 1] == '*')
            return (safeColumn - 1, safeColumn);

        if (safeColumn < currentLine.Length && currentLine[safeColumn] == '*')
            return (safeColumn, safeColumn + 1);

        return null;
    }
}

public sealed record CompleteRequest(string? Script, int Line, int Column, string? DocumentUri);

public sealed record CompleteResponse(IReadOnlyList<CompletionItemResponse> Items);

public sealed record CompletionItemResponse(
    string Label,
    string InsertText,
    string Kind,
    string? Detail = null,
    string? Documentation = null,
    int? StartColumn = null,
    int? EndColumn = null);
