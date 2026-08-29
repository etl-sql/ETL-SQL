using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// Resolves hover documentation for an ETL-SQL / Report-SQL token from the embedded language help
/// corpus (<c>docs/reference</c>, embedded into ETL-SQL.Core as <c>Resources/Help</c>).
/// </summary>
/// <remarks>
/// Host-neutral on purpose. The desktop Workstation Editor and the Portal both serve hover from this
/// one implementation so that hover text, <c>HELP</c>, and Studio's explanations cannot drift apart.
/// </remarks>
public sealed class LanguageHoverService(
    ILanguageHelpRegistry languageHelp,
    IFunctionRegistry functionRegistry)
{
    /// <summary>Looks up help markdown for <paramref name="word"/>, or null when nothing matches.</summary>
    /// <returns>The markdown and the kind of match ("function", "keyword", "help"), or (null, null).</returns>
    public (string? Markdown, string? Kind) Lookup(string? word)
    {
        var token = (word ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            return (null, null);

        var functionHelp = functionRegistry.GetHelp(token);
        if (!string.IsNullOrWhiteSpace(functionHelp))
            return (ScaleDownHeaders(functionHelp), "function");

        var keywordHelp = languageHelp.GetHelp(token);
        if (!string.IsNullOrWhiteSpace(keywordHelp))
            return (ScaleDownHeaders(keywordHelp), "keyword");

        // Dotted tokens address a sub-topic, e.g. "CONNECTION.MSSQL" or "VISUAL.BAR".
        if (token.Contains('.', StringComparison.Ordinal))
        {
            var parts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                var subTopicHelp = languageHelp.GetHelp(parts[0], parts[1]);
                if (!string.IsNullOrWhiteSpace(subTopicHelp))
                    return (ScaleDownHeaders(subTopicHelp), "help");
            }
        }

        return (null, null);
    }

    // Reference docs are authored as standalone pages starting at "#". Demote them three levels so
    // they read as a tooltip fragment rather than a page title.
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
