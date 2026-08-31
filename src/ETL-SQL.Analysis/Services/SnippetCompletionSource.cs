using ETL_SQL.Core.Metadata;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// Offers the shared `$trigger` snippet library as editor completions.
/// </summary>
/// <remarks>
/// <para>The 83-snippet library under <c>/snippets</c> is embedded into ETL-SQL.Core and already
/// reaches the TUI and the VS Code language server. Neither GUI editor exposed it, so the two
/// surfaces a newcomer is most likely to start in were the two without starter templates.</para>
/// <para>Both GUI hosts insert completion text literally, so these carry <see cref="SnippetDef.TuiBody"/>
/// with its <c>«placeholder»</c> markers rather than the LSP <c>${1:…}</c> tab stops — the guillemets
/// are visible instructions to overwrite, which is the more useful behaviour without a snippet-aware
/// insert.</para>
/// </remarks>
public static class SnippetCompletionSource
{
    /// <summary>
    /// Snippets matching <paramref name="prefix"/>, or empty when the cursor is not at a position
    /// where a snippet trigger makes sense.
    /// </summary>
    /// <param name="scriptBefore">Script text up to the cursor.</param>
    /// <param name="prefix">The token being completed.</param>
    public static IReadOnlyList<SnippetDef> GetMatches(string? scriptBefore, string? prefix)
    {
        var token = prefix ?? string.Empty;
        if (!token.StartsWith('$'))
            return [];

        // A snippet expands to a whole statement, so it is only offered when the trigger is the
        // only thing on the line — otherwise it would fire mid-expression.
        if (!IsAtStatementStart(scriptBefore ?? string.Empty, token))
            return [];

        return SnippetLibrary.Instance.GetByPrefix(token).ToList();
    }

    private static bool IsAtStatementStart(string scriptBefore, string prefix)
    {
        var lastNewline = scriptBefore.LastIndexOf('\n');
        var lineContent = lastNewline >= 0 ? scriptBefore[(lastNewline + 1)..] : scriptBefore;
        return lineContent.TrimStart() == prefix;
    }
}
