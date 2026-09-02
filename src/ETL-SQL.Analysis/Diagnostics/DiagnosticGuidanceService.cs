using System.Text.RegularExpressions;

namespace ETL_SQL.Analysis.Diagnostics;

/// <summary>
/// A one-click repair for a diagnostic: replace the text between two positions with
/// <paramref name="Replacement"/>.
///
/// <para>Positions are <b>zero-based</b>, matching <see cref="AnalysisDiagnostic"/>, which is what
/// this always travels beside. Two conventions in one payload is how an off-by-one gets into an edit
/// that a button applies without the author reading it first.</para>
/// </summary>
/// <param name="Title">What the button says. Phrased as the change, not as the rule it satisfies.</param>
public sealed record DiagnosticQuickFix(
    string Title,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string Replacement);

/// <summary>
/// A diagnostic rewritten for someone who does not already know the answer.
/// </summary>
/// <param name="Summary">What went wrong, in one sentence, with no parser vocabulary in it.</param>
/// <param name="Action">What to do about it. Always present: a diagnosis with no next step is half an answer.</param>
/// <param name="DocPath">
/// A repo-relative path into <c>docs/reference</c>, which is also the embedded runtime help — so the
/// link resolves both in the docs site and inside the product.
/// </param>
/// <param name="Anchor">
/// The named object the offending line sits inside — a visual, a page, a dataset — so a surface can
/// put the message on the card the author is looking at instead of on a line number they are not.
/// </param>
public sealed record DiagnosticGuidance(
    string Summary,
    string Action,
    string? DocPath = null,
    DiagnosticQuickFix? QuickFix = null,
    string? Anchor = null,
    string? AnchorKind = null);

/// <summary>
/// Translates parser and lint diagnostics into something a beginner can act on.
///
/// <para>The parser's own messages are written for someone who knows the grammar: "Expected ')' to
/// close OPTIONS" tells you what the parser wanted, not what you did or what to type. That is the
/// right message for a compiler and the wrong one for someone whose first ETL-SQL script just went
/// red. This maps the shapes that actually occur onto a sentence, a next step, a reference page, and
/// — where the fix is unambiguous — an edit.</para>
///
/// <para>Two rules keep it honest. It <b>only</b> recognises patterns it can act on; anything else
/// gets no guidance at all rather than a generic "check your syntax", which is noise wearing the
/// costume of help. And a quick fix is offered only when there is exactly one correct repair: a
/// button that guesses turns a visible error into an invisible wrong answer, which is strictly
/// worse than leaving the author to type it.</para>
/// </summary>
public sealed class DiagnosticGuidanceService
{
    /// <summary>
    /// Guidance for one diagnostic, or null when this service has nothing specific to add.
    /// </summary>
    /// <param name="lines">The script's lines, used to read what is actually at the reported spot.</param>
    public DiagnosticGuidance? For(AnalysisDiagnostic diagnostic, IReadOnlyList<string> lines)
    {
        var message = diagnostic.Message ?? string.Empty;
        var anchor = AnchorFor(diagnostic.StartLine, lines);

        var guidance =
            UnterminatedString(diagnostic, lines)
            ?? UnclosedParenthesis(diagnostic, message)
            ?? UnquotedTextValue(diagnostic, lines)
            ?? MissingSemicolon(diagnostic, message, lines)
            ?? UnknownOptionValue(message)
            ?? UnexpectedToken(message)
            ?? MissingKeyword(message);

        return guidance is null
            ? null
            : guidance with { Anchor = anchor.Name, AnchorKind = anchor.Kind };
    }

    // ── The patterns ─────────────────────────────────────────────────────────

    /// <summary>An opened quote with no closing one — by far the most common first-week failure.</summary>
    private static DiagnosticGuidance? UnterminatedString(AnalysisDiagnostic diagnostic, IReadOnlyList<string> lines)
    {
        if (!diagnostic.Message.Contains("Unterminated string", StringComparison.OrdinalIgnoreCase)) return null;

        var line = LineAt(diagnostic.StartLine, lines);
        return new DiagnosticGuidance(
            "A piece of text opens with a quote that is never closed.",
            "Add the closing ' at the end of the text. To put an apostrophe inside text, double it: 'O''Brien'.",
            "docs/reference/data-types.md",
            // The end of the line is where the quote belongs in every case this pattern matches: the
            // lexer only reports it once it has run out of line looking for the pair.
            line is null ? null : new DiagnosticQuickFix(
                "Close the quote",
                diagnostic.StartLine, line.TrimEnd().Length,
                diagnostic.StartLine, line.TrimEnd().Length,
                "'"));
    }

    private static DiagnosticGuidance? UnclosedParenthesis(AnalysisDiagnostic diagnostic, string message)
    {
        var match = Regex.Match(message, @"Expected '\)'(?: to close (?<what>[^.]+?))?(?:\s*$|[.,])", RegexOptions.IgnoreCase);
        var what = match.Success && match.Groups["what"].Success ? match.Groups["what"].Value.Trim() : null;

        // The report the author actually gets for an unclosed bracket is not "expected )". The
        // parser reaches the end of the statement still inside the block and complains about the
        // semicolon it found there, which names the symptom at a spot several lines from the cause.
        var strayTerminator = Regex.Match(
            message, @"Unexpected token ';' inside (?<where>[A-Z ]+?) body", RegexOptions.IgnoreCase);
        if (!match.Success && !strayTerminator.Success) return null;

        what ??= strayTerminator.Success ? strayTerminator.Groups["where"].Value.Trim() : null;
        return new DiagnosticGuidance(
            what is null
                ? "A bracket was opened and never closed."
                : $"The {what} was opened with ( and never closed — the statement ended while it was still open.",
            "Add the matching ) . Every ( in an option, mapping, or layout block needs one, and they close in the order they were opened.",
            "docs/reference/visuals-reporting/visuals/chart.md");
    }

    /// <summary>
    /// A bare word where a value belongs. Text values are quoted in ETL-SQL, and forgetting that is
    /// the second thing every new author does.
    /// </summary>
    private static DiagnosticGuidance? UnquotedTextValue(AnalysisDiagnostic diagnostic, IReadOnlyList<string> lines)
    {
        var line = LineAt(diagnostic.StartLine, lines);
        if (line is null) return null;

        var match = Regex.Match(line, @"\b(?<key>TITLE|SUBTITLE|LABEL|FORMAT|PAGE_SIZE|ORIENTATION|UNITS)\s*=\s*(?<value>[A-Za-z][A-Za-z0-9_ ]*)\s*(?<tail>[,)]|$)");
        if (!match.Success) return null;

        var value = match.Groups["value"].Value.TrimEnd();
        // ON/OFF and the enumerations are keywords, not text; quoting those would break them.
        if (value.Equals("ON", StringComparison.OrdinalIgnoreCase)
            || value.Equals("OFF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("AUTO", StringComparison.OrdinalIgnoreCase)) return null;

        var start = match.Groups["value"].Index;
        return new DiagnosticGuidance(
            $"{match.Groups["key"].Value} was given the bare word {value}, and ETL-SQL reads a bare word as a column name.",
            $"Wrap it in single quotes so it is read as text: {match.Groups["key"].Value} = '{value}'.",
            "docs/reference/data-types.md",
            new DiagnosticQuickFix(
                $"Quote '{value}'",
                diagnostic.StartLine, start,
                diagnostic.StartLine, start + value.Length,
                $"'{value.Replace("'", "''")}'"));
    }

    /// <summary>A statement running into the next one, which the parser reports from far away.</summary>
    private static DiagnosticGuidance? MissingSemicolon(AnalysisDiagnostic diagnostic, string message, IReadOnlyList<string> lines)
    {
        if (!Regex.IsMatch(message, @"at start of statement", RegexOptions.IgnoreCase)) return null;

        var previous = PreviousCodeLine(diagnostic.StartLine, lines);
        if (previous is null || previous.Value.Text.TrimEnd().EndsWith(';')) return null;

        return new DiagnosticGuidance(
            "This looks like a new statement, but the one above it was never finished.",
            "End the statement above with a semicolon. ETL-SQL runs one statement at a time, and a missing ; makes the next one look like part of it.",
            "docs/reference/statements/README.md",
            new DiagnosticQuickFix(
                "End the previous statement with ;",
                previous.Value.Line, previous.Value.Text.TrimEnd().Length,
                previous.Value.Line, previous.Value.Text.TrimEnd().Length,
                ";"));
    }

    /// <summary>An option that exists, given a value it does not accept.</summary>
    private static DiagnosticGuidance? UnknownOptionValue(string message)
    {
        var match = Regex.Match(message, @"Unknown (?<what>[A-Z_ ]+?) (?:option|adjustment) '(?<value>[^']+)'", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(message, @"Unknown (?<what>[A-Z_ ]+?) '(?<value>[^']+)'", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
        }

        // The parser names the valid alternatives when it has them; that half of the message is
        // already the most useful thing an author can be told, so it is carried through verbatim.
        var valid = Regex.Match(message, @"Valid (?:option|options|values?) (?:is|are) (?<list>[^.]+)", RegexOptions.IgnoreCase);
        return new DiagnosticGuidance(
            $"'{match.Groups["value"].Value}' is not something {match.Groups["what"].Value.Trim()} accepts.",
            valid.Success
                ? $"Use one of: {valid.Groups["list"].Value.Trim()}."
                : "Check the reference page for this clause and use one of the values it lists.",
            "docs/syntax-index.md");
    }

    private static DiagnosticGuidance? UnexpectedToken(string message)
    {
        var match = Regex.Match(
            message,
            @"Unexpected token (?:type \w+ \('(?<a>[^']*)'\)|'(?<b>[^']*)')(?: inside (?<where>[A-Z ]+?) body)?",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var text = match.Groups["a"].Success ? match.Groups["a"].Value : match.Groups["b"].Value;
        var where = match.Groups["where"].Success ? $" in the {match.Groups["where"].Value.Trim()}" : string.Empty;
        return new DiagnosticGuidance(
            $"ETL-SQL did not expect '{text}' here{where}.",
            "Check the punctuation just before this spot — a missing comma, an unquoted piece of text, or a keyword that belongs in a different clause is usually what puts it here.",
            "docs/syntax-index.md");
    }

    /// <summary>"Expected X after Y" — the parser's most common shape, and its least readable.</summary>
    private static DiagnosticGuidance? MissingKeyword(string message)
    {
        var match = Regex.Match(message, @"Expected (?<expected>[^']+?|'[^']+') after (?<after>.+?)\s*(?:at line|$)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var expected = match.Groups["expected"].Value.Trim().Trim('\'');
        var after = match.Groups["after"].Value.Trim().Trim('\'');
        return new DiagnosticGuidance(
            $"{after} has to be followed by {expected}, and it is not.",
            $"Add {expected} after {after}.",
            "docs/syntax-index.md");
    }

    // ── Anchoring ────────────────────────────────────────────────────────────

    /// <summary>
    /// The named object whose statement the line sits in.
    ///
    /// <para>Found by scanning upward for the nearest <c>CREATE …</c> header, which is exactly how a
    /// reader locates it. A line number is a fine anchor for an editor and a poor one for a canvas:
    /// "the Revenue card will not parse" is actionable, and "line 34" is a lookup the author has to
    /// perform before they can start.</para>
    /// </summary>
    private static (string? Name, string? Kind) AnchorFor(int line, IReadOnlyList<string> lines)
    {
        for (var index = Math.Min(line, lines.Count - 1); index >= 0; index--)
        {
            var match = Regex.Match(
                lines[index],
                @"^\s*CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?(?<kind>VISUAL|PAGE|DATASET|CONNECTION|BOOKMARK|CONTAINER|BUTTON)\s+(?:\[(?<bracket>[^\]]+)\]|(?<plain>[&#]?[A-Za-z_][A-Za-z0-9_]*))",
                RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            var name = match.Groups["bracket"].Success ? match.Groups["bracket"].Value : match.Groups["plain"].Value;
            return (name, match.Groups["kind"].Value.ToUpperInvariant());
        }
        return (null, null);
    }

    // ── Reading the script ───────────────────────────────────────────────────

    /// <summary>The reported line. Zero-based, like every other position in this file.</summary>
    private static string? LineAt(int line, IReadOnlyList<string> lines) =>
        line >= 0 && line < lines.Count ? lines[line] : null;

    /// <summary>The nearest line above that holds something other than blank space or a comment.</summary>
    private static (int Line, string Text)? PreviousCodeLine(int line, IReadOnlyList<string> lines)
    {
        for (var index = Math.Min(line, lines.Count) - 1; index >= 0; index--)
        {
            var text = lines[index];
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("--", StringComparison.Ordinal)) continue;
            return (index, text);
        }
        return null;
    }
}
