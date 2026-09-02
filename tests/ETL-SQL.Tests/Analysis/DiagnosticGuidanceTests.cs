using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using Xunit;
using CoreDiagnostic = ETL_SQL.Core.Common.Diagnostic;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The beginner-facing translation of parser diagnostics.
///
/// <para>Every case here starts from a script and the parser's <em>real</em> message rather than
/// from a message string typed into the test. A translation table written against remembered
/// wording is a table that silently stops matching the first time a parser message is reworded, and
/// it fails by producing no guidance — which looks exactly like "this diagnostic has no guidance",
/// the normal case.</para>
///
/// <para>The other thing under test is restraint. A quick fix is only offered where exactly one
/// repair is correct; a button that guesses converts a visible error into an invisible wrong
/// answer.</para>
/// </summary>
public class DiagnosticGuidanceTests
{
    private readonly DiagnosticGuidanceService _guidance = new();

    /// <summary>Parses, and hands back the first error the way the analyze route does.</summary>
    private (AnalysisDiagnostic Diagnostic, IReadOnlyList<string> Lines) FirstError(string script)
    {
        var lines = script.ReplaceLineEndings("\n").Split('\n');
        List<CoreDiagnostic> diagnostics;
        try
        {
            diagnostics = [.. new CoreParser(new Lexer(script).Tokenize(), script).Parse().Diagnostics];
        }
        catch (SyntaxException ex)
        {
            diagnostics = [new CoreDiagnostic { Message = ex.Message, Line = ex.Line, Column = ex.Column, Severity = DiagnosticSeverity.Error, Source = "Parser" }];
        }

        var error = diagnostics.FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error);
        Assert.NotNull(error);
        var built = AnalysisDiagnosticBuilder.FromParserDiagnostics([error!], lines);
        return (built.Single(), lines);
    }

    private DiagnosticGuidance? GuidanceFor(string script)
    {
        var (diagnostic, lines) = FirstError(script);
        return _guidance.For(diagnostic, lines);
    }

    /// <summary>
    /// Applies a quick fix the way a client would, so what is asserted is the repaired text rather
    /// than the coordinates. Positions are zero-based, matching the diagnostic they travel with.
    /// </summary>
    private static string Apply(string script, DiagnosticQuickFix fix)
    {
        var lines = script.ReplaceLineEndings("\n").Split('\n').ToList();
        Assert.Equal(fix.StartLine, fix.EndLine);
        var line = lines[fix.StartLine];
        var start = Math.Clamp(fix.StartColumn, 0, line.Length);
        var end = Math.Clamp(fix.EndColumn, start, line.Length);
        lines[fix.StartLine] = line[..start] + fix.Replacement + line[end..];
        return string.Join("\n", lines);
    }

    /// <summary>True when the repaired script has no error left.</summary>
    private static bool Parses(string script)
    {
        try
        {
            var parsed = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
            return !parsed.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
        }
        catch (SyntaxException)
        {
            return false;
        }
    }

    [Fact]
    public void AnUnclosedQuote_IsExplainedAndClosedByTheFix()
    {
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT 'north AS region
            FROM corp.orders;
            """;

        var guidance = GuidanceFor(script);

        Assert.NotNull(guidance);
        Assert.Contains("never closed", guidance!.Summary, StringComparison.OrdinalIgnoreCase);
        // The action tells the author the escape rule too, because the next thing they hit is an
        // apostrophe inside the text they just quoted.
        Assert.Contains("''", guidance.Action, StringComparison.Ordinal);
        Assert.NotNull(guidance.QuickFix);
        Assert.True(Parses(Apply(script, guidance.QuickFix!)), "Closing the quote should leave a script that parses.");
    }

    [Fact]
    public void ABareWordWhereTextBelongs_IsQuotedByTheFixAndNamesTheCardItIsIn()
    {
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT region, total INTO #rows FROM corp.orders;

            CREATE VISUAL RevenueCard AS CARD (
                SOURCE = #rows,
                TITLE = Total Revenue,
                MAPPINGS (VALUE = total)
            );
            """;

        var guidance = GuidanceFor(script);

        Assert.NotNull(guidance);
        Assert.Contains("bare word", guidance!.Summary, StringComparison.OrdinalIgnoreCase);
        // Anchored to the card, not to a line number: "line 6" is a lookup the author has to perform
        // before they can start, and the canvas has no line numbers on it at all.
        Assert.Equal("RevenueCard", guidance.Anchor);
        Assert.Equal("VISUAL", guidance.AnchorKind);

        Assert.NotNull(guidance.QuickFix);
        var fixedScript = Apply(script, guidance.QuickFix!);
        Assert.Contains("TITLE = 'Total Revenue'", fixedScript, StringComparison.Ordinal);
        Assert.True(Parses(fixedScript), "Quoting the title should leave a script that parses.");
    }

    [Fact]
    public void AKeywordValue_IsNotQuoted()
    {
        // ON, OFF, and AUTO are keywords rather than text, so quoting one would take a working
        // clause and break it — the one thing a repair button must never do. Asserted against a
        // diagnostic built here rather than parsed, because a script whose only fault is a valid
        // `= OFF` does not exist: the guard has to hold for a diagnostic that lands on that line for
        // any other reason.
        var lines = new[]
        {
            "CREATE VISUAL Chart AS BAR (",
            "    SOURCE = #rows,",
            "    OPTIONS (LEGEND = OFF, FORMAT = AUTO)",
            ");",
        };
        var diagnostic = new AnalysisDiagnostic(
            2, 4, 2, 9, DiagnosticSeverity.Error,
            "Unexpected token 'x' inside CREATE VISUAL body.", null, "ETL-SQL Parser");

        var guidance = _guidance.For(diagnostic, lines);

        Assert.NotNull(guidance);
        Assert.Null(guidance!.QuickFix);
        // It still names the card, because that part is useful whatever the fault turns out to be.
        Assert.Equal("Chart", guidance.Anchor);
    }

    [Fact]
    public void AnUnclosedBracket_IsExplainedInTermsOfWhatWasOpened()
    {
        const string script = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT region INTO #rows FROM corp.orders;

            CREATE VISUAL Chart AS BAR (
                SOURCE = #rows,
                MAPPINGS (X = region, Y = region
            );
            """;

        var guidance = GuidanceFor(script);

        // The parser does not say "expected )". It runs to the end of the statement still inside the
        // block and reports the semicolon it found there — several lines from the cause, and about
        // the wrong character. This is the translation that matters most.
        Assert.NotNull(guidance);
        Assert.Contains("never closed", guidance!.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("matching )", guidance.Action, StringComparison.Ordinal);
        Assert.Equal("Chart", guidance.Anchor);
        // No quick fix: where the bracket belongs is a judgement, and a button that put it in the
        // wrong place would produce a script that parses and means something else.
        Assert.Null(guidance.QuickFix);
    }

    [Fact]
    public void EveryGuidanceCarriesANextStepAndAReferencePage()
    {
        // A diagnosis with no next step is half an answer, and the reference tree is also the
        // product's embedded help — so a path that does not exist is a dead link inside the app.
        string[] scripts =
        [
            "SELECT 'north AS region\nFROM corp.orders;",
            "CREATE VISUAL C AS CARD (SOURCE = #r, TITLE = Total Revenue);",
            "CREATE VISUAL C AS BAR (SOURCE = #r, MAPPINGS (X = a, Y = b);",
        ];

        foreach (var script in scripts)
        {
            var guidance = GuidanceFor(script);
            Assert.NotNull(guidance);
            Assert.False(string.IsNullOrWhiteSpace(guidance!.Summary));
            Assert.False(string.IsNullOrWhiteSpace(guidance.Action));
            Assert.NotNull(guidance.DocPath);
            Assert.True(
                File.Exists(Path.Combine(RepoRoot(), guidance.DocPath!.Replace('/', Path.DirectorySeparatorChar))),
                $"Guidance points at {guidance.DocPath}, which does not exist.");
        }
    }

    [Fact]
    public void ADiagnosticItCannotActOn_GetsNoGuidanceRatherThanAPlatitude()
    {
        // "Check your syntax" is noise wearing the costume of help, and it crowds out the messages
        // that do say something.
        var diagnostic = new AnalysisDiagnostic(
            1, 1, 1, 5, DiagnosticSeverity.Warning,
            "Table 'orders' has no governance tag.", "LINT001", "ETL-SQL Lint");

        Assert.Null(_guidance.For(diagnostic, ["SELECT 1;"]));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
