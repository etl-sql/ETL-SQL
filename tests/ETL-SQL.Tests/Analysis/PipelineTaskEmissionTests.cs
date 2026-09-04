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
/// <para>The rule is the one the roadmap set for the first four kinds and the canvas rebuild keeps
/// for every kind after them: a palette entry may exist "only after each emitted statement passes
/// its focused parser, lint, formatter, and reference checks". A chip that writes a statement
/// failing any of those is worse than no chip at all, because the author finds out when their
/// pipeline runs. Every kind here is checked four ways, and the palette offers exactly the kinds
/// that pass — <see cref="EveryKindTheServiceCanWriteIsCoveredHere"/> is what keeps the two in
/// step, so a kind added to the enum without a case here fails rather than shipping untested.</para>
/// </summary>
public class PipelineTaskEmissionTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    /// <summary>
    /// A script holding everything an emitted statement is allowed to lean on: the connections it
    /// resolves against, a staged table its conditions can count, and a labelled loop with a task
    /// inside it, which is the only place <c>BREAK</c> and <c>CONTINUE</c> are legal.
    /// </summary>
    private const string Preamble = """
        CREATE CONNECTION staging_db AS MOCKDB();
        CREATE CONNECTION mailer AS SMTP('smtp.example.com', PORT = 587);

        SELECT 1 AS Ok INTO #orders;

        DECLARE @keep_going BOOL = TRUE;

        retry_loop:
        WHILE @keep_going = TRUE
        BEGIN
            in_the_loop:
            ASSERT 1 = 1, 'Somewhere inside a loop to anchor against.';
        END;
        """;

    /// <summary>The task inside <see cref="Preamble"/>'s loop, for the kinds that need one.</summary>
    private const string InsideTheLoop = "in_the_loop";

    /// <summary>
    /// One draft per kind the palette offers, which is every kind the service can write.
    ///
    /// <para>The drafts are what the forms produce, not what is convenient to test: a path form
    /// yields paths, a counted loop yields a counter and two bounds. Anything the emitter would have
    /// to invent to make one of these parse is a missing field on the form, and shows up here as a
    /// refusal rather than as a statement the author did not author.</para>
    /// </summary>
    public static TheoryData<PipelineTaskKind, PipelineTaskDraft> EveryOfferedKind() => new()
    {
        {
            PipelineTaskKind.Execution,
            new PipelineTaskDraft("load_orders", PipelineTaskKind.Execution,
                Connection: "staging_db", Body: "SELECT OrderId FROM dbo.Orders;")
        },

        // ── Files ────────────────────────────────────────────────────────────
        {
            PipelineTaskKind.CopyFile,
            new PipelineTaskDraft("archive_extract", PipelineTaskKind.CopyFile,
                Source: @"C:\data\orders.csv", Target: @"C:\data\archive\orders.csv")
        },
        {
            PipelineTaskKind.MoveFile,
            new PipelineTaskDraft("stage_extract", PipelineTaskKind.MoveFile,
                Source: @"C:\data\incoming\orders.csv", Target: @"C:\data\working\orders.csv")
        },
        {
            PipelineTaskKind.RenameFile,
            new PipelineTaskDraft("date_stamp_extract", PipelineTaskKind.RenameFile,
                Source: @"C:\data\working\orders.csv", Target: "orders_20260904.csv")
        },
        {
            PipelineTaskKind.DeleteFile,
            new PipelineTaskDraft("drop_extract", PipelineTaskKind.DeleteFile,
                Source: @"C:\data\working\orders.csv")
        },

        // ── Directories ──────────────────────────────────────────────────────
        {
            PipelineTaskKind.CreateDirectory,
            new PipelineTaskDraft("make_archive", PipelineTaskKind.CreateDirectory,
                Source: @"C:\data\archive\2026-09")
        },
        {
            PipelineTaskKind.DeleteDirectory,
            new PipelineTaskDraft("drop_working", PipelineTaskKind.DeleteDirectory,
                Source: @"C:\data\working")
        },
        {
            PipelineTaskKind.DeleteDirectoryContents,
            new PipelineTaskDraft("empty_working", PipelineTaskKind.DeleteDirectoryContents,
                Source: @"C:\data\working")
        },
        {
            PipelineTaskKind.RenameDirectory,
            new PipelineTaskDraft("close_the_month", PipelineTaskKind.RenameDirectory,
                Source: @"C:\data\archive\current", Target: "2026-09")
        },
        {
            PipelineTaskKind.MoveDirectory,
            new PipelineTaskDraft("shelve_the_month", PipelineTaskKind.MoveDirectory,
                Source: @"C:\data\archive\2026-09", Target: @"D:\cold\2026-09")
        },
        {
            PipelineTaskKind.CopyDirectory,
            new PipelineTaskDraft("mirror_the_month", PipelineTaskKind.CopyDirectory,
                Source: @"C:\data\archive\2026-09", Target: @"D:\mirror\2026-09")
        },

        // ── Checks and messages ──────────────────────────────────────────────
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
        {
            PipelineTaskKind.Throw,
            new PipelineTaskDraft("give_up", PipelineTaskKind.Throw,
                Message: "The nightly load found nothing to do.")
        },

        // ── Containers ───────────────────────────────────────────────────────
        {
            PipelineTaskKind.Parallel,
            new PipelineTaskDraft("load_both_sources", PipelineTaskKind.Parallel)
        },
        {
            PipelineTaskKind.Transaction,
            new PipelineTaskDraft("publish_atomically", PipelineTaskKind.Transaction)
        },
        {
            PipelineTaskKind.Foreach,
            new PipelineTaskDraft("per_order", PipelineTaskKind.Foreach,
                Variable: "@row", Collection: "#orders")
        },
        {
            PipelineTaskKind.For,
            new PipelineTaskDraft("seven_days", PipelineTaskKind.For,
                Variable: "@day", Start: "1", End: "7")
        },
        {
            PipelineTaskKind.While,
            new PipelineTaskDraft("until_drained", PipelineTaskKind.While,
                Condition: "@keep_going = TRUE")
        },
        {
            PipelineTaskKind.If,
            new PipelineTaskDraft("only_when_staged", PipelineTaskKind.If,
                Condition: "(SELECT COUNT(*) FROM #orders) > 0")
        },

        // ── Only legal inside a loop ─────────────────────────────────────────
        {
            PipelineTaskKind.Break,
            new PipelineTaskDraft("stop_early", PipelineTaskKind.Break, After: InsideTheLoop)
        },
        {
            PipelineTaskKind.Continue,
            new PipelineTaskDraft("skip_this_one", PipelineTaskKind.Continue, After: InsideTheLoop)
        },

        // ── Waiting ──────────────────────────────────────────────────────────
        {
            PipelineTaskKind.WaitFor,
            new PipelineTaskDraft("pause_before_retry", PipelineTaskKind.WaitFor, Delay: "00:00:30")
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

    /// <summary>
    /// The palette and this gate say the same thing.
    ///
    /// <para>Without this, adding a kind to the enum and a chip to the palette ships an emitter that
    /// nothing here ever ran. That is the exact shape the gate exists to prevent, so the omission
    /// has to fail rather than be noticed.</para>
    /// </summary>
    [Fact]
    public void EveryKindTheServiceCanWriteIsCoveredHere()
    {
        var covered = EveryOfferedKind().Select(row => (PipelineTaskKind)row[0]!).ToHashSet();
        var missing = Enum.GetValues<PipelineTaskKind>().Where(kind => !covered.Contains(kind)).ToList();

        Assert.True(missing.Count == 0,
            "These kinds have no draft in EveryOfferedKind, so nothing checks what they write: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void EveryKindIsRefusedUntilItsOwnFieldsAreFilledIn()
    {
        // A half-filled task must be refused here, with a sentence about the missing field, rather
        // than rendered into the script and refused by the parser talking about syntax the author
        // never typed.
        var incomplete = new[]
        {
            (new PipelineTaskDraft("t", PipelineTaskKind.Execution, Connection: "staging_db"), "SQL it runs"),
            (new PipelineTaskDraft("t", PipelineTaskKind.CopyFile, Source: @"C:\in.csv"), "target path"),
            (new PipelineTaskDraft("t", PipelineTaskKind.MoveFile), "source path"),
            (new PipelineTaskDraft("t", PipelineTaskKind.DeleteFile), "source path"),
            (new PipelineTaskDraft("t", PipelineTaskKind.CreateDirectory), "directory task needs a source"),
            (new PipelineTaskDraft("t", PipelineTaskKind.MoveDirectory, Source: @"C:\in"), "directory task needs a target"),
            (new PipelineTaskDraft("t", PipelineTaskKind.Validation, Condition: "1 = 1"), "message"),
            (new PipelineTaskDraft("t", PipelineTaskKind.Notification, Connection: "mailer", Recipient: "a@b.c"), "sender"),
            (new PipelineTaskDraft("t", PipelineTaskKind.Notification, Connection: "mailer", Recipient: "a@b.c", Sender: "x@y.z"), "subject"),
            (new PipelineTaskDraft("t", PipelineTaskKind.If), "condition it tests"),
            (new PipelineTaskDraft("t", PipelineTaskKind.While), "condition it repeats on"),
            (new PipelineTaskDraft("t", PipelineTaskKind.For, Variable: "@i"), "counts from"),
            (new PipelineTaskDraft("t", PipelineTaskKind.For, Variable: "@i", Start: "1"), "counts to"),
            (new PipelineTaskDraft("t", PipelineTaskKind.Throw), "message it fails with"),
            (new PipelineTaskDraft("t", PipelineTaskKind.WaitFor), "hh:mm:ss"),
            (new PipelineTaskDraft("t", PipelineTaskKind.WaitFor, Delay: "half an hour"), "is not a time"),
        };

        foreach (var (draft, expected) in incomplete)
        {
            var result = _tasks.Add(Preamble, draft);
            Assert.False(result.Applied, $"{draft.Kind} was written from an incomplete draft.");
            Assert.Contains(expected, result.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Preamble, result.Script);
        }
    }

    /// <summary>
    /// <c>BREAK</c> and <c>CONTINUE</c> are the two kinds the script itself can refuse.
    ///
    /// <para>The engine rejects them outside a loop, so a canvas that wrote one wherever the author
    /// dropped it would produce a script that parses and then fails at run time — the failure shape
    /// this surface is being rebuilt to stop. They are refused with a sentence naming the three
    /// constructs that would make them legal.</para>
    /// </summary>
    [Theory]
    [InlineData(PipelineTaskKind.Break)]
    [InlineData(PipelineTaskKind.Continue)]
    public void LoopOnlyKindsAreRefusedOutsideALoopAndAcceptedInside(PipelineTaskKind kind)
    {
        var atTopLevel = _tasks.Add(Preamble, new PipelineTaskDraft("nope", kind));
        Assert.False(atTopLevel.Applied);
        Assert.Contains("inside a loop", atTopLevel.Error!, StringComparison.OrdinalIgnoreCase);

        // Beside the loop rather than in it is still outside it.
        var beside = _tasks.Add(Preamble, new PipelineTaskDraft("nope", kind, After: "retry_loop"));
        Assert.False(beside.Applied);
        Assert.Contains("inside a loop", beside.Error!, StringComparison.OrdinalIgnoreCase);

        var inside = _tasks.Add(Preamble, new PipelineTaskDraft("yes_here", kind, After: InsideTheLoop));
        Assert.True(inside.Applied, inside.Error);
        AssertParsesCleanly(inside.Script, $"{kind} inside a loop");
        Assert.Equal("retry_loop", _tasks.Read(inside.Script).Single(task => task.Id == "yes_here").Container);

        // And it cannot be dragged back out to a level no loop encloses.
        var pulledOut = _tasks.Nest(inside.Script, "yes_here", null);
        Assert.False(pulledOut.Applied);
        Assert.Contains("inside a loop", pulledOut.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Dropping a chip onto a container writes the statement inside it, in one edit.
    ///
    /// <para>An empty container has no child to be added after, so without this the only way to fill
    /// one was to add a task somewhere else and drag it in — two edits, two undo steps, and a moment
    /// where the script holds the statement in a block the author did not choose.</para>
    /// </summary>
    [Fact]
    public void ADropOntoAContainerWritesTheTaskInsideIt()
    {
        var withBlock = _tasks.Add(Preamble, new PipelineTaskDraft("load_both", PipelineTaskKind.Parallel));
        Assert.True(withBlock.Applied, withBlock.Error);

        var inside = _tasks.Add(withBlock.Script, new PipelineTaskDraft(
            "left_branch", PipelineTaskKind.Execution,
            Connection: "staging_db", Body: "SELECT 1 AS Ok;", Into: "load_both"));

        Assert.True(inside.Applied, inside.Error);
        AssertParsesCleanly(inside.Script, "a task dropped into a container");

        var task = _tasks.Read(inside.Script).Single(entry => entry.Id == "left_branch");
        Assert.Equal("load_both", task.Container);

        // The whole point is that it is one edit: the script before the drop is the one the author
        // undoes back to, not an intermediate holding the task at the top level.
        Assert.Equal(withBlock.Script, _tasks.Remove(inside.Script, "left_branch").Script);
    }

    [Fact]
    public void ADropOntoSomethingThatIsNotAContainerIsRefused()
    {
        var script = _tasks.Add(Preamble, new PipelineTaskDraft(
            "make_archive", PipelineTaskKind.CreateDirectory, Source: @"C:\data\archive"));
        Assert.True(script.Applied, script.Error);

        var refused = _tasks.Add(script.Script, new PipelineTaskDraft(
            "nope", PipelineTaskKind.DeleteFile, Source: @"C:\data\x.csv", Into: "make_archive"));

        Assert.False(refused.Applied);
        Assert.Contains("does not hold other tasks", refused.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(script.Script, refused.Script);

        var missing = _tasks.Add(script.Script, new PipelineTaskDraft(
            "nope", PipelineTaskKind.DeleteFile, Source: @"C:\data\x.csv", Into: "no_such_block"));
        Assert.False(missing.Applied);
        Assert.Contains("no_such_block", missing.Error!, StringComparison.Ordinal);
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
            "odd_path", PipelineTaskKind.CopyFile,
            Source: @"C:\it's\in.csv", Target: @"C:\it's\out.csv"));

        Assert.True(fileTask.Applied, fileTask.Error);
        AssertParsesCleanly(fileTask.Script, "file task with quotes");

        var thrown = _tasks.Add(fileTask.Script, new PipelineTaskDraft(
            "odd_throw", PipelineTaskKind.Throw, Message: "It's over."));

        Assert.True(thrown.Applied, thrown.Error);
        AssertParsesCleanly(thrown.Script, "throw with quotes");
        Assert.Contains("It''s over.", thrown.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryKindCoexistsInOneScriptAndStaysIndividuallyAddressable()
    {
        var drafts = EveryOfferedKind()
            .Select(row => ((PipelineTaskKind)row[0]!, (PipelineTaskDraft)row[1]!))
            .ToList();

        var script = Preamble;
        foreach (var (_, draft) in drafts)
        {
            var result = _tasks.Add(script, draft);
            Assert.True(result.Applied, $"{draft.Kind}: {result.Error}");
            script = result.Script;
        }

        AssertParsesCleanly(script, "a script holding every task kind at once");

        // Every one of them is still readable, still the kind it was asked for, and still findable
        // by its label. A kind that parses on its own but is swallowed by a neighbour would show the
        // author a canvas missing the box they just added.
        var read = _tasks.Read(script);
        foreach (var (kind, draft) in drafts)
        {
            var task = read.SingleOrDefault(entry => entry.Id == draft.Id);
            Assert.True(task is not null, $"{kind} '{draft.Id}' is not readable back out of the script.");
            Assert.Equal(kind, task!.Kind);
        }

        // Deleting one takes exactly one statement with it and leaves the rest addressable.
        var removed = _tasks.Remove(script, "orders_arrived");
        Assert.True(removed.Applied, removed.Error);
        AssertParsesCleanly(removed.Script, "a script after removing one task kind");
        Assert.DoesNotContain("orders_arrived", removed.Script, StringComparison.Ordinal);
        Assert.Equal(read.Count - 1, _tasks.Read(removed.Script).Count);
    }
}
