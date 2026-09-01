using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The gate the task palette is allowed to offer a kind behind.
///
/// <para>The roadmap item is explicit: file-operation, validation, and notification tasks may be
/// added "only after each emitted statement passes its focused parser, lint, formatter, and
/// reference checks". A chip in the palette that writes a statement failing any of those is worse
/// than no chip at all, because the author finds out when their pipeline runs. Each kind here is
/// therefore checked four ways, and the palette enables exactly the kinds that pass.</para>
/// </summary>
public class PipelineTaskEmissionTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    /// <summary>A script with the connections every emitted statement has to resolve against.</summary>
    private const string Preamble = """
        CREATE CONNECTION staging_db AS MOCKDB();
        CREATE CONNECTION mailer AS SMTP('smtp.example.com', PORT = 587);

        SELECT 1 AS Ok INTO #orders;
        """;

    public static TheoryData<PipelineTaskKind, PipelineTaskDraft> EveryOfferedKind() => new()
    {
        {
            PipelineTaskKind.Execution,
            new PipelineTaskDraft("load_orders", PipelineTaskKind.Execution,
                Connection: "staging_db", Body: "SELECT OrderId FROM dbo.Orders;")
        },
        {
            PipelineTaskKind.FileOperation,
            new PipelineTaskDraft("archive_extract", PipelineTaskKind.FileOperation,
                Source: @"C:\data\orders.csv", Target: @"C:\data\archive\orders.csv")
        },
        {
            PipelineTaskKind.Validation,
            new PipelineTaskDraft("orders_arrived", PipelineTaskKind.Validation,
                Condition: "(SELECT COUNT(*) FROM #orders) > 0", Message: "No orders were staged.")
        },
        {
            PipelineTaskKind.Notification,
            new PipelineTaskDraft("tell_ops", PipelineTaskKind.Notification,
                Connection: "mailer", Recipient: "ops@example.com", Sender: "etl@example.com",
                Subject: "Nightly load finished", Body: "All records processed.")
        },
    };

    private static Script Parse(string script) =>
        new CoreParser(new Lexer(script).Tokenize(), script).Parse();

    private static void AssertParsesCleanly(string script, string what)
    {
        var error = Parse(script).Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(error is null, $"{what} does not parse: {error?.Message}\n---\n{script}");
    }

    [Theory]
    [MemberData(nameof(EveryOfferedKind))]
    public void EveryOfferedKind_EmitsAStatementTheParserAccepts(PipelineTaskKind kind, PipelineTaskDraft draft)
    {
        var result = _tasks.Add(Preamble, draft);

        Assert.True(result.Applied, result.Error);
        AssertParsesCleanly(result.Script, $"{kind} task");

        // It must come back as the kind that was asked for, or the canvas is showing a lie.
        var task = _tasks.Read(result.Script).Single(t => t.Id == draft.Id);
        Assert.Equal(kind, task.Kind);
    }

    [Theory]
    [MemberData(nameof(EveryOfferedKind))]
    public async Task EveryOfferedKind_EmitsAStatementTheLinterIsHappyWith(PipelineTaskKind kind, PipelineTaskDraft draft)
    {
        var result = _tasks.Add(Preamble, draft);
        Assert.True(result.Applied, result.Error);

        var before = await Lint(Preamble);
        var after = await Lint(result.Script);

        // The task must not introduce a lint finding of its own. Comparing against the preamble's own
        // findings keeps this honest: it fails on what the emitted statement added, not on whatever
        // the fixture already trips.
        var introduced = after.Except(before, StringComparer.Ordinal).ToList();
        Assert.True(introduced.Count == 0,
            $"The {kind} task introduced lint findings:\n  " + string.Join("\n  ", introduced));

        static async Task<List<string>> Lint(string script)
        {
            var linter = LinterFactory.CreateWithAllRules(null);
            var results = await linter.AnalyzeAsync(Parse(script), new DefaultLintContext());
            return results
                .Where(finding => finding.Severity is LintSeverity.Error or LintSeverity.Warning)
                .Select(finding => $"{finding.Severity} {finding.Code ?? finding.RuleName}: {finding.Message}")
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToList();
        }
    }

    [Theory]
    [MemberData(nameof(EveryOfferedKind))]
    public void EveryOfferedKind_SurvivesTheCanonicalFormatter(PipelineTaskKind kind, PipelineTaskDraft draft)
    {
        var result = _tasks.Add(Preamble, draft);
        Assert.True(result.Applied, result.Error);

        // The first author to press Format must not lose the task the canvas wrote, and the canvas
        // must still recognise it afterwards — the label is how it finds the node again.
        var formatted = SqlFormatter.Format(result.Script, new FormatterOptions());
        AssertParsesCleanly(formatted, $"formatted {kind} task");

        var task = _tasks.Read(formatted).SingleOrDefault(t => t.Id == draft.Id);
        Assert.True(task is not null, $"The formatter lost the {kind} task:\n{formatted}");
        Assert.Equal(kind, task!.Kind);
    }

    [Theory]
    [MemberData(nameof(EveryOfferedKind))]
    public void EveryOfferedKind_ReferencesOnlyWhatTheScriptDeclares(PipelineTaskKind kind, PipelineTaskDraft draft)
    {
        var result = _tasks.Add(Preamble, draft);
        Assert.True(result.Applied, result.Error);
        Assert.Equal(kind, _tasks.Read(result.Script).Single(t => t.Id == draft.Id).Kind);

        var ast = Parse(result.Script);
        var declared = ast.Statements.OfType<CreateConnectionStatement>()
            .Select(statement => statement.ConnectionName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A task that names an alias the script does not declare previews correctly for its author
        // and fails for every other reader — the same defect the dataset wizard already had to fix.
        if (!string.IsNullOrWhiteSpace(draft.Connection))
            Assert.Contains(draft.Connection, declared, StringComparer.OrdinalIgnoreCase);

        var tempTables = ast.Statements
            .Select(statement => statement.GetCreatedTable())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var referenced in ReferencedTempTables(result.Script[
            _tasks.Read(result.Script).Single(t => t.Id == draft.Id).StartOffset..]))
        {
            Assert.Contains(referenced, tempTables, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<string> ReferencedTempTables(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"#[A-Za-z_][A-Za-z0-9_]*")
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void EveryKindIsRefusedUntilItsOwnFieldsAreFilledIn()
    {
        // A half-filled task must be refused here, with a sentence about the missing field, rather
        // than rendered into the script and refused by the parser talking about syntax the author
        // never typed.
        var incomplete = new[]
        {
            (new PipelineTaskDraft("t", PipelineTaskKind.Execution, Connection: "staging_db"), "SQL it runs"),
            (new PipelineTaskDraft("t", PipelineTaskKind.FileOperation, Source: @"C:\in.csv"), "target path"),
            (new PipelineTaskDraft("t", PipelineTaskKind.Validation, Condition: "1 = 1"), "message"),
            (new PipelineTaskDraft("t", PipelineTaskKind.Notification, Connection: "mailer", Recipient: "a@b.c"), "sender"),
            (new PipelineTaskDraft("t", PipelineTaskKind.Notification, Connection: "mailer", Recipient: "a@b.c", Sender: "x@y.z"), "subject"),
        };

        foreach (var (draft, expected) in incomplete)
        {
            var result = _tasks.Add(Preamble, draft);
            Assert.False(result.Applied);
            Assert.Contains(expected, result.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Preamble, result.Script);
        }
    }

    [Fact]
    public void AQuoteInAPathOrAMessageIsEscapedRatherThanClosingTheLiteral()
    {
        // The one way a value-carrying emitter rewrites the rest of the file as something else.
        var result = _tasks.Add(Preamble, new PipelineTaskDraft(
            "odd_names", PipelineTaskKind.Validation,
            Condition: "1 = 1", Message: "O'Brien's rule: don't ship."));

        Assert.True(result.Applied, result.Error);
        AssertParsesCleanly(result.Script, "validation task with quotes");
        Assert.Contains("O''Brien''s rule", result.Script, StringComparison.Ordinal);

        var fileTask = _tasks.Add(result.Script, new PipelineTaskDraft(
            "odd_path", PipelineTaskKind.FileOperation,
            Source: @"C:\it's\in.csv", Target: @"C:\it's\out.csv"));

        Assert.True(fileTask.Applied, fileTask.Error);
        AssertParsesCleanly(fileTask.Script, "file task with quotes");
    }

    [Fact]
    public void AllFourKindsCoexistInOneScriptAndStayIndividuallyAddressable()
    {
        var script = Preamble;
        foreach (var (_, draft) in EveryOfferedKind().Select(row => ((PipelineTaskKind)row[0]!, (PipelineTaskDraft)row[1]!)))
        {
            var result = _tasks.Add(script, draft);
            Assert.True(result.Applied, result.Error);
            script = result.Script;
        }

        AssertParsesCleanly(script, "a script with every task kind");
        Assert.Equal(
            ["load_orders", "archive_extract", "orders_arrived", "tell_ops"],
            _tasks.Read(script).Select(task => task.Id));

        // Deleting one takes exactly one statement with it.
        var removed = _tasks.Remove(script, "orders_arrived");
        Assert.True(removed.Applied, removed.Error);
        AssertParsesCleanly(removed.Script, "a script after removing one task kind");
        Assert.Equal(["load_orders", "archive_extract", "tell_ops"], _tasks.Read(removed.Script).Select(task => task.Id));
        Assert.DoesNotContain("orders_arrived", removed.Script, StringComparison.Ordinal);
    }
}
