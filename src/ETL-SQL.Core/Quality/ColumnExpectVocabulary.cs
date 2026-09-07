namespace ETL_SQL.Core.Quality;

/// <summary>
/// The <c>EXPECT</c> clause's completion vocabulary, in one place.
/// <para>
/// Two independent surfaces offer it: the base language service produces the suggestions, and the
/// grammar state tree decides which of them survive at the cursor — a keyword the tree does not
/// offer is filtered out of every real editor (language server, Portal, TUI, workstation). When the
/// two lists were written separately, one of them was silently wrong and the rule vocabulary
/// vanished from the editors while the base service's own tests still passed.
/// </para>
/// </summary>
public static class ColumnExpectVocabulary
{
    /// <summary>
    /// How each rule form starts. Entries ending in a space or <c>(</c> expect the author to keep
    /// typing; the rest stand alone.
    /// </summary>
    public static readonly string[] RuleStarters =
    [
        "NOT NULL", "NOT BLANK", "UNIQUE", "UNIQUE WITH (", "UNIQUE_FIRST BY ", "UNIQUE_LAST BY ",
        "MATCHES ", "NOT MATCHES ", "IN (", "NOT IN (", "EXISTS IN ", "EXISTS WITH (",
        "LENGTH BETWEEN ", "LENGTH >= ", "LENGTH <= ", "CASTABLE AS ", "BETWEEN ", "EXPR ",
        ">= ", "<= ", "> ", "< ", "= "
    ];

    /// <summary>
    /// What a column's <c>ON FAILURE</c> accepts. <c>NOTIFY</c> is absent deliberately: it is a
    /// job-level action, and offering it here would walk an author into a parse error.
    /// </summary>
    public static readonly string[] ColumnActions = ["THROW", "WARN", "QUARANTINE"];
}
