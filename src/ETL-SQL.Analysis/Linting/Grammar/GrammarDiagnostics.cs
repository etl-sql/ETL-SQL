namespace ETL_SQL.Analysis.Linting.Grammar;

/// <summary>
/// Test/diagnostics switch for the grammar-driven suggestion pipeline. In production the suggestion
/// walker and custom suggestion providers swallow their own exceptions and fall back to the broad
/// keyword list, so a grammar bug degrades silently instead of throwing at an author. When
/// <see cref="StrictMode"/> is enabled those swallow points rethrow instead, so tests can prove the
/// grammar path did not quietly fail. It is <c>[ThreadStatic]</c> so a test that opts in cannot
/// affect other threads.
/// </summary>
public static class GrammarDiagnostics
{
    [System.ThreadStatic]
    private static bool _strictMode;

    /// <summary>When true, suggestion walker/provider exceptions propagate instead of being swallowed.</summary>
    public static bool StrictMode
    {
        get => _strictMode;
        set => _strictMode = value;
    }
}
