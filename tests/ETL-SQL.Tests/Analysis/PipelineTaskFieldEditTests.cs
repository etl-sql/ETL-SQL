using ETL_SQL.Analysis.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// Every task on the pipeline canvas can have its fields edited in place, not only renamed.
///
/// <para>A field is offered only when the host can find it as an exact span in the author's text: a
/// path, message, or address that is one string literal; a condition or loop bound as the expression
/// it is; a connection as a name. An edit replaces that span and nothing else, so an option added by
/// hand survives. A field written some other way — a path built from a variable, a mail sent to two
/// addresses — is not offered at all, because rewriting it from one text box would lose something.</para>
/// </summary>
public class PipelineTaskFieldEditTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    private const string Seed = """
        CREATE CONNECTION staging_db AS MOCKDB();
        CREATE CONNECTION mail AS MOCKDB();

        fetch_orders:
        EXECUTE staging_db BEGIN
            SELECT 1;
        END;
        """;

    private static Script Parse(string script) =>
        new CoreParser(new Lexer(script).Tokenize(), script).Parse();

    private static void AssertParses(string script)
    {
        var error = Parse(script).Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(error is null, $"Script does not parse: {error?.Message}\n---\n{script}");
    }

    private PipelineTask TaskNamed(string script, string id) =>
        _tasks.Read(script).Single(task => task.Id == id);

    private string With(PipelineTaskDraft draft)
    {
        var result = _tasks.Add(Seed, draft);
        Assert.True(result.Applied, result.Error);
        return result.Script;
    }

    private string Applied(PipelineEditResult result)
    {
        Assert.True(result.Applied, result.Error);
        AssertParses(result.Script);
        return result.Script;
    }

    private static Dictionary<string, string?> Change(string name, string? value) => new() { [name] = value };

    public static TheoryData<PipelineTaskDraft, string[]> EveryKindWithFields() => new()
    {
        { new("t", PipelineTaskKind.CopyFile, Source: @"C:\in\a.csv", Target: @"C:\out\a.csv"), ["source", "target"] },
        { new("t", PipelineTaskKind.MoveFile, Source: @"C:\in\a.csv", Target: @"C:\out\a.csv"), ["source", "target"] },
        { new("t", PipelineTaskKind.RenameFile, Source: @"C:\in\a.csv", Target: "b.csv"), ["source", "target"] },
        { new("t", PipelineTaskKind.DeleteFile, Source: @"C:\in\a.csv"), ["source"] },
        { new("t", PipelineTaskKind.CreateDirectory, Source: @"C:\archive"), ["source"] },
        { new("t", PipelineTaskKind.DeleteDirectory, Source: @"C:\archive"), ["source"] },
        { new("t", PipelineTaskKind.DeleteDirectoryContents, Source: @"C:\archive"), ["source"] },
        { new("t", PipelineTaskKind.RenameDirectory, Source: @"C:\archive", Target: "old"), ["source", "target"] },
        { new("t", PipelineTaskKind.MoveDirectory, Source: @"C:\archive", Target: @"D:\cold"), ["source", "target"] },
        { new("t", PipelineTaskKind.CopyDirectory, Source: @"C:\archive", Target: @"D:\mirror"), ["source", "target"] },
        { new("t", PipelineTaskKind.Validation, Condition: "1 = 1", Message: "Nothing staged."), ["condition", "message"] },
        {
            new("t", PipelineTaskKind.Notification, Connection: "mail", Recipient: "ops@example.com",
                Sender: "etl@example.com", Subject: "Done", Body: "All records processed."),
            ["recipient", "sender", "subject", "body", "connection"]
        },
        { new("t", PipelineTaskKind.Throw, Message: "Nothing to do."), ["message"] },
        { new("t", PipelineTaskKind.WaitFor, Delay: "00:00:30"), ["delay"] },
        { new("t", PipelineTaskKind.If, Condition: "1 = 1"), ["condition"] },
        { new("t", PipelineTaskKind.While, Condition: "1 = 0"), ["condition"] },
        { new("t", PipelineTaskKind.For, Variable: "day", Start: "1", End: "7", Step: "2"), ["variable", "start", "end", "step"] },
    };

    // ── Reading ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryKindWithFields))]
    public void EveryKindReportsTheFieldsItWasWrittenWith(PipelineTaskDraft draft, string[] names)
    {
        var task = TaskNamed(With(draft), "t");

        Assert.NotNull(task.Fields);
        Assert.Equal(names.OrderBy(name => name), task.Fields!.Keys.OrderBy(name => name));
        foreach (var name in names)
            Assert.Equal(Expected(draft, name), task.Fields[name]);
    }

    private static string Expected(PipelineTaskDraft draft, string name) => name switch
    {
        "source" => draft.Source!,
        "target" => draft.Target!,
        "condition" => draft.Condition!,
        "message" => draft.Message!,
        "recipient" => draft.Recipient!,
        "sender" => draft.Sender!,
        "subject" => draft.Subject!,
        "body" => draft.Body!,
        "connection" => draft.Connection!,
        "delay" => draft.Delay!,
        "variable" => "@" + draft.Variable,
        "start" => draft.Start!,
        "end" => draft.End!,
        "step" => draft.Step!,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    // ── Writing one field ────────────────────────────────────────────────────

    /// <summary>
    /// Each field, edited on its own, changes exactly its own span: the new script is the old one with
    /// that one piece of text replaced, and it reads back as what was asked for.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryKindWithFields))]
    public void EditingAFieldReplacesOnlyItsOwnText(PipelineTaskDraft draft, string[] names)
    {
        var script = With(draft);

        foreach (var name in names)
        {
            var value = name switch
            {
                "connection" => "staging_db",
                "variable" => "@week",
                "start" or "end" or "step" => "3",
                "condition" => "2 > 1",
                "delay" => "00:01:00",
                _ => "edited value",
            };

            var edited = Applied(_tasks.Update(script, "t", fields: Change(name, value)));

            Assert.Equal(value, TaskNamed(edited, "t").Fields![name]);
            foreach (var other in names.Where(other => other != name))
                Assert.Equal(TaskNamed(script, "t").Fields![other], TaskNamed(edited, "t").Fields![other]);

            // Everything before the task is untouched, byte for byte.
            var taskStart = TaskNamed(script, "t").StartOffset;
            Assert.Equal(script[..taskStart], edited[..taskStart]);
        }
    }

    [Fact]
    public void AQuoteInAFieldIsEscapedAndReadBackAsTyped()
    {
        var script = With(new PipelineTaskDraft("t", PipelineTaskKind.Throw, Message: "Nothing to do."));

        var edited = Applied(_tasks.Update(script, "t", fields: Change("message", "It's not done; stop.")));

        Assert.Contains("'It''s not done; stop.'", edited, StringComparison.Ordinal);
        Assert.Equal("It's not done; stop.", TaskNamed(edited, "t").Fields!["message"]);
    }

    /// <summary>The reason this exists: an option the author added by hand survives an edit made here.</summary>
    [Fact]
    public void AHandAddedOptionSurvivesAnEdit()
    {
        const string script = """
            CREATE CONNECTION staging_db AS MOCKDB();

            t:
            SEND EMAIL
                TO 'ops@example.com'
                FROM 'etl@example.com'
                SUBJECT 'Done'
                BODY 'All records processed.'
                ATTACH 'C:\reports\nightly.csv'
                AT staging_db;
            """;
        AssertParses(script);

        var edited = Applied(_tasks.Update(script, "t", fields: Change("subject", "Nightly load done")));

        Assert.Equal(script.Replace("'Done'", "'Nightly load done'", StringComparison.Ordinal), edited);
    }

    [Fact]
    public void RenamingAndEditingAFieldIsOneEdit()
    {
        var script = With(new PipelineTaskDraft("t", PipelineTaskKind.CopyFile, Source: @"C:\in\a.csv", Target: @"C:\out\a.csv"));

        var edited = Applied(_tasks.Update(script, "t", newId: "archive_orders", fields: Change("target", @"D:\cold\a.csv")));

        var task = TaskNamed(edited, "archive_orders");
        Assert.Equal(@"D:\cold\a.csv", task.Fields!["target"]);
        Assert.Equal(@"C:\in\a.csv", task.Fields["source"]);
        Assert.DoesNotContain(_tasks.Read(edited), other => other.Id == "t");
    }

    // ── What is not offered ──────────────────────────────────────────────────

    /// <summary>
    /// A path built from a variable is an expression, not a path. Offering it as a text box would turn
    /// it into a literal the moment anyone pressed Apply.
    /// </summary>
    [Fact]
    public void AFieldWrittenAsAnExpressionIsNotOfferedAndNotRewritten()
    {
        const string script = """
            DECLARE @dir VARCHAR(100) = 'C:\in';

            t:
            COPY FILE @dir + '\a.csv' TO 'C:\out\a.csv';
            """;
        AssertParses(script);

        var task = TaskNamed(script, "t");
        Assert.False(task.Fields!.ContainsKey("source"));
        Assert.Equal(@"C:\out\a.csv", task.Fields["target"]);

        var refused = _tasks.Update(script, "t", fields: Change("source", @"C:\other\a.csv"));
        Assert.False(refused.Applied);
        Assert.Equal(script, refused.Script);
        Assert.Contains("script", refused.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARecipientHeldInAVariableIsNotOffered()
    {
        const string script = """
            CREATE CONNECTION staging_db AS MOCKDB();
            DECLARE @on_call VARCHAR(100) = 'ops@example.com';

            t:
            SEND EMAIL
                TO @on_call
                FROM 'etl@example.com'
                SUBJECT 'Done'
                BODY 'All records processed.'
                AT staging_db;
            """;
        AssertParses(script);

        var fields = TaskNamed(script, "t").Fields!;
        Assert.False(fields.ContainsKey("recipient"));
        Assert.Equal("Done", fields["subject"]);
    }

    // ── A FOR loop's optional step ───────────────────────────────────────────

    [Fact]
    public void AStepIsAddedToAndRemovedFromAForLoop()
    {
        var script = With(new PipelineTaskDraft("t", PipelineTaskKind.For, Variable: "day", Start: "1", End: "7"));
        // Offered, empty: there is somewhere to write one.
        Assert.Equal(string.Empty, TaskNamed(script, "t").Fields!["step"]);

        var stepped = Applied(_tasks.Update(script, "t", fields: Change("step", "2")));
        Assert.Equal("2", TaskNamed(stepped, "t").Fields!["step"]);
        Assert.Equal("7", TaskNamed(stepped, "t").Fields!["end"]);

        var unstepped = Applied(_tasks.Update(stepped, "t", fields: Change("step", "")));
        Assert.Equal(script, unstepped);
    }

    // ── Containers keep what is inside them ──────────────────────────────────

    [Fact]
    public void EditingAnIfConditionLeavesBothBranchesAlone()
    {
        var script = With(new PipelineTaskDraft("t", PipelineTaskKind.If, Condition: "1 = 1"));
        script = Applied(_tasks.AddElse(script, "t"));
        script = Applied(_tasks.Nest(script, "fetch_orders", PipelineTaskAuthoringService.ElseScope("t")));

        var edited = Applied(_tasks.Update(script, "t", fields: Change("condition", "(SELECT COUNT(*) FROM #orders) > 0")));

        Assert.Equal(script.Replace("IF 1 = 1", "IF (SELECT COUNT(*) FROM #orders) > 0", StringComparison.Ordinal), edited);
        Assert.Equal("t:else", TaskNamed(edited, "fetch_orders").Container);
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("condition", "")]
    [InlineData("condition", "1 = 1; DROP TABLE dbo.Orders")]
    [InlineData("condition", "1 = 1 -- and a comment")]
    [InlineData("message", "  ")]
    [InlineData("nonsense", "x")]
    public void AnUnusableValueIsRefusedAndNothingIsWritten(string name, string value)
    {
        var script = With(new PipelineTaskDraft("t", PipelineTaskKind.Validation, Condition: "1 = 1", Message: "Nothing staged."));

        var result = _tasks.Update(script, "t", fields: Change(name, value));

        Assert.False(result.Applied);
        Assert.Equal(script, result.Script);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ANotificationCanOnlyBePointedAtAConnectionName()
    {
        var script = With(new PipelineTaskDraft("t", PipelineTaskKind.Notification, Connection: "mail",
            Recipient: "ops@example.com", Sender: "etl@example.com", Subject: "Done", Body: "Ok."));

        Assert.False(_tasks.Update(script, "t", fields: Change("connection", "mail; DROP")).Applied);
        Assert.Equal("staging_db", TaskNamed(Applied(_tasks.Update(script, "t", connection: "staging_db")), "t").Fields!["connection"]);
    }
}
