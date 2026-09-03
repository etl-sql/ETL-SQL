using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Services;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>Which production host a journey was driven against.</summary>
public enum StudioHost
{
    Portal,
    Desktop,
}

/// <summary>
/// What a certified journey produced, kept so a failure names the journey rather than the assertion.
/// </summary>
public sealed record CertifiedArtifact(
    string Journey,
    StudioHost Host,
    string Path,
    string Script)
{
    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();
}

/// <summary>
/// The contract every certified Studio journey has to satisfy, in one place.
///
/// <para>It is one place on purpose. Three journeys each asserting their own version of "the script
/// is valid" is three subtly different definitions, and the weakest one is the one that decides what
/// ships. The clauses below are the ones the phase actually promises, and each is checked the
/// literal way rather than the convenient way.</para>
///
/// <list type="number">
/// <item><b>A production host.</b> Recorded rather than asserted from inside: the journey drives the
/// real Portal or the real desktop host, and the harness carries which, so a failure says so.</item>
/// <item><b>Only <c>.etlsql</c> or <c>.rptsql</c>.</b> A journey that produced any other artifact —
/// a JSON sidecar, a project file, a hidden state blob — would mean the GUI had invented a format
/// the language does not read.</item>
/// <item><b>Parser, linter and formatter accept it.</b> No error diagnostic, no error-severity lint
/// finding, and a formatter pass that both parses and is idempotent. Idempotence is the part worth
/// having: a formatter whose second pass differs from its first will churn every file it touches.
/// </item>
/// <item><b>It survives save and reload.</b> Asserted against the bytes that came back from the
/// host, not against what the editor believed it had saved.</item>
/// <item><b>Code and canvas round-trip without changing untouched text.</b> Reconciling the parsed
/// design state against the script it came from must change nothing at all. If it did not hold,
/// opening a wizard and cancelling would still rewrite the author's file.</item>
/// </list>
/// </summary>
public static class StudioCertification
{
    private static readonly DesignerAnalysisService Analysis = new();
    private static readonly DesignerScriptPatcher Patcher = new();

    /// <summary>
    /// Applies every clause of the contract to a journey's artifact.
    /// </summary>
    /// <param name="reloaded">
    /// The script as the host handed it back after a save and reload. Null skips clause 4, which is
    /// only for journeys that actually performed one.
    /// </param>
    public static void Certify(CertifiedArtifact artifact, string? reloaded = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        CertifyArtifactShape(artifact);
        CertifyParses(artifact);
        CertifyLints(artifact);
        CertifyFormats(artifact);
        if (reloaded is not null) CertifySurvivesReload(artifact, reloaded);
        CertifyRoundTrips(artifact);
    }

    // ── Clause 2: only the language's own file types ─────────────────────────

    private static void CertifyArtifactShape(CertifiedArtifact artifact)
    {
        Assert.True(
            artifact.Extension is ".etlsql" or ".rptsql",
            $"{Describe(artifact)} emitted '{artifact.Path}'. A certified journey produces ETL-SQL or "
            + "Report-SQL and nothing else — a sidecar file would be a format the language cannot read.");

        Assert.False(
            string.IsNullOrWhiteSpace(artifact.Script),
            $"{Describe(artifact)} produced an empty script.");
    }

    // ── Clause 3: parser, linter, formatter ──────────────────────────────────

    private static void CertifyParses(CertifiedArtifact artifact)
    {
        var ast = Parse(artifact.Script);
        var errors = ast.Diagnostics
            .Where(diagnostic => diagnostic.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error)
            .Select(diagnostic => $"line {diagnostic.Line}: {diagnostic.Message}")
            .ToArray();

        Assert.True(errors.Length == 0,
            $"{Describe(artifact)} produced a script the parser rejects:{Environment.NewLine}"
            + string.Join(Environment.NewLine, errors));
        Assert.True(ast.Statements.Count > 0, $"{Describe(artifact)} produced no statements.");
    }

    private static void CertifyLints(CertifiedArtifact artifact)
    {
        var linter = LinterFactory.CreateWithAllRules();
        var results = linter
            .AnalyzeAsync(Parse(artifact.Script), new DefaultLintContext { DocumentUri = artifact.Path })
            .GetAwaiter()
            .GetResult();

        // Errors only. A warning is advice — SELECT * is legal and sometimes right — and a lane that
        // failed on advice would be turned off within a week, which costs the errors too.
        var errors = results
            .Where(result => result.Severity == LintSeverity.Error)
            .Select(result => $"{result.Code ?? result.RuleName} (line {result.LineNumber}): {result.Message}")
            .ToArray();

        Assert.True(errors.Length == 0,
            $"{Describe(artifact)} produced a script the linter rejects:{Environment.NewLine}"
            + string.Join(Environment.NewLine, errors));
    }

    private static void CertifyFormats(CertifiedArtifact artifact)
    {
        string once;
        try
        {
            once = SqlFormatter.Format(artifact.Script, new FormatterOptions());
        }
        catch (Exception exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"{Describe(artifact)} produced a script the formatter could not format: {exception.Message}");
        }

        var reparsed = Parse(once);
        var errors = reparsed.Diagnostics
            .Where(diagnostic => diagnostic.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error)
            .Select(diagnostic => $"line {diagnostic.Line}: {diagnostic.Message}")
            .ToArray();
        Assert.True(errors.Length == 0,
            $"{Describe(artifact)}: formatting it produced a script that no longer parses:{Environment.NewLine}"
            + string.Join(Environment.NewLine, errors));

        // Idempotence, because a formatter whose second pass differs from its first churns every
        // file it touches and makes every diff unreadable.
        var twice = SqlFormatter.Format(once, new FormatterOptions());
        Assert.True(string.Equals(once, twice, StringComparison.Ordinal),
            $"{Describe(artifact)}: formatting is not idempotent — a second pass changed the file again.");
    }

    // ── Clause 4: save and reload ────────────────────────────────────────────

    private static void CertifySurvivesReload(CertifiedArtifact artifact, string reloaded)
    {
        Assert.True(
            string.Equals(Normalize(artifact.Script), Normalize(reloaded), StringComparison.Ordinal),
            $"{Describe(artifact)}: the script changed across save and reload.{Environment.NewLine}"
            + FirstDifference(Normalize(artifact.Script), Normalize(reloaded)));
    }

    // ── Clause 5: code and canvas round-trip ─────────────────────────────────

    /// <summary>
    /// Reconciling the parsed state against the script it came from must change nothing.
    ///
    /// <para>Byte-for-byte for a document that has a canvas. A pipeline script has no pages, and
    /// patching one scaffolds the page a report would need — so for those the claim is the one that
    /// is actually true and still worth having: every line the author wrote is still there, and no
    /// statement was rewritten on the way through.</para>
    /// </summary>
    private static void CertifyRoundTrips(CertifiedArtifact artifact)
    {
        var parsed = Analysis.Parse(artifact.Script, 5_000);
        Assert.NotNull(parsed.DesignState);

        var patched = Patcher.Patch(artifact.Script, parsed.DesignState!);
        var hasCanvas = Parse(artifact.Script).Statements.OfType<CreatePageStatement>().Any();

        if (hasCanvas)
        {
            Assert.True(string.Equals(artifact.Script, patched, StringComparison.Ordinal),
                $"{Describe(artifact)}: a code → canvas → code round-trip changed the file.{Environment.NewLine}"
                + FirstDifference(artifact.Script, patched));
            return;
        }

        foreach (var line in artifact.Script.Split('\n').Select(line => line.TrimEnd('\r'))
                     .Where(line => line.Trim().Length > 0))
        {
            Assert.True(patched.Contains(line, StringComparison.Ordinal),
                $"{Describe(artifact)}: a round-trip dropped or rewrote a line the author wrote:{Environment.NewLine}{line}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Script Parse(string script) =>
        new CoreParser(new Lexer(script).Tokenize(), script).Parse();

    private static string Describe(CertifiedArtifact artifact) =>
        $"The {artifact.Journey} journey on the {artifact.Host} host";

    /// <summary>Line endings are the host's, not the journey's, so they are not part of the claim.</summary>
    private static string Normalize(string script) =>
        script.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

    /// <summary>
    /// Where two scripts first diverge, with context. A bare "strings differ" on a 40-line script is
    /// a failure message somebody has to reproduce locally before they can read it.
    /// </summary>
    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Replace("\r\n", "\n").Split('\n');
        var actualLines = actual.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < Math.Max(expectedLines.Length, actualLines.Length); index++)
        {
            var left = index < expectedLines.Length ? expectedLines[index] : "(end of file)";
            var right = index < actualLines.Length ? actualLines[index] : "(end of file)";
            if (string.Equals(left, right, StringComparison.Ordinal)) continue;
            return $"First difference at line {index + 1}:{Environment.NewLine}  before: {left}{Environment.NewLine}  after:  {right}";
        }
        return "The scripts differ only in trailing whitespace.";
    }
}
