using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationHelpService(
    ILanguageHelpRegistry languageHelp,
    IFunctionRegistry functionRegistry)
{
    public HoverResponse GetHover(HoverRequest request)
    {
        var word = (request.Word ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(word))
            return new HoverResponse(null);

        var functionHelp = functionRegistry.GetHelp(word);
        if (!string.IsNullOrWhiteSpace(functionHelp))
            return new HoverResponse(ScaleDownHeaders(functionHelp), "function");

        var keywordHelp = languageHelp.GetHelp(word);
        if (!string.IsNullOrWhiteSpace(keywordHelp))
            return new HoverResponse(ScaleDownHeaders(keywordHelp), "keyword");

        if (word.Contains('.', StringComparison.Ordinal))
        {
            var parts = word.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                var subTopicHelp = languageHelp.GetHelp(parts[0], parts[1]);
                if (!string.IsNullOrWhiteSpace(subTopicHelp))
                    return new HoverResponse(ScaleDownHeaders(subTopicHelp), "help");
            }
        }

        return new HoverResponse(null);
    }

    private static string ScaleDownHeaders(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        var lines = markdown.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.StartsWith("#", StringComparison.Ordinal))
                continue;

            var hashCount = 0;
            while (hashCount < line.Length && line[hashCount] == '#')
                hashCount++;

            if (hashCount > 0 && hashCount < line.Length && line[hashCount] == ' ')
                lines[i] = new string('#', Math.Min(6, hashCount + 3)) + line[hashCount..];
        }

        return string.Join("\n", lines);
    }
}

public sealed record HoverRequest(string? Word, string? Script = null, int Line = 0, int Column = 0, string? DocumentUri = null);

public sealed record HoverResponse(string? Markdown, string? Kind = null);
