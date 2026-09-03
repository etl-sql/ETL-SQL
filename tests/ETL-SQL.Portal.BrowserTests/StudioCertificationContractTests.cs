using Xunit.Sdk;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// The certification contract, checked against itself.
///
/// <para>Three journeys are about to depend on this harness for their whole verdict, so a clause
/// that silently never fires would take all three green with it — the most expensive kind of passing
/// test. Every clause below is therefore shown to fail on an artifact that violates it and to pass
/// on one that does not.</para>
/// </summary>
[Trait("Category", "Browser")]
public sealed class StudioCertificationContractTests
{
    private const string GoodReport = """
        CREATE CONNECTION corp AS MOCKDB();

        SELECT region, total INTO #sales FROM corp.orders;

        CREATE VISUAL RevenueTable AS TABLE (
            SOURCE = #sales,
            TITLE = 'Revenue'
        );

        CREATE PAGE Main AS DASHBOARD (
            LAYOUT (STRUCTURE = 'A', MAP ('A' = RevenueTable))
        );
        """;

    private const string GoodPipeline = """
        CREATE CONNECTION corp AS MOCKDB();

        SELECT region, total INTO #sales FROM corp.orders;

        SELECT region, SUM(total) AS revenue INTO #by_region FROM #sales GROUP BY region;
        """;

    private static CertifiedArtifact Artifact(string script, string path = "certified.rptsql") =>
        new("test", StudioHost.Desktop, path, script);

    private static string Message(Action act) => Assert.ThrowsAny<XunitException>(act).Message;

    [Fact]
    public void A_report_a_journey_could_really_produce_passes_every_clause()
    {
        StudioCertification.Certify(Artifact(GoodReport), reloaded: GoodReport);
    }

    [Fact]
    public void A_pipeline_script_passes_every_clause()
    {
        StudioCertification.Certify(Artifact(GoodPipeline, "certified.etlsql"), reloaded: GoodPipeline);
    }

    [Fact]
    public void Clause_2_refuses_an_artifact_that_is_not_the_language()
    {
        Assert.Contains("ETL-SQL or Report-SQL",
            Message(() => StudioCertification.Certify(Artifact(GoodReport, "certified.json"))));
    }

    [Fact]
    public void Clause_2_refuses_an_empty_script()
    {
        Assert.Contains("empty script", Message(() => StudioCertification.Certify(Artifact("   "))));
    }

    [Fact]
    public void Clause_3_refuses_a_script_the_parser_rejects()
    {
        Assert.Contains("the parser rejects",
            Message(() => StudioCertification.Certify(Artifact("SELECT * FROM corp.orders WHERE ) = 1;"))));
    }

    [Fact]
    public void Clause_3_refuses_a_script_the_linter_rejects()
    {
        // A hand-written @expect tag is inert and looks enforced, which ColumnRuleValidationRule
        // reports as an error — exactly the kind of thing a GUI must never emit.
        const string handWrittenRule = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT region /* @expect: NOT NULL */ INTO #sales FROM corp.orders;
            CREATE VISUAL T AS TABLE (SOURCE = #sales);
            CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = T)));
            """;

        Assert.Contains("the linter rejects",
            Message(() => StudioCertification.Certify(Artifact(handWrittenRule))));
    }

    [Fact]
    public void Clause_4_refuses_a_script_that_changed_across_save_and_reload()
    {
        var message = Message(() => StudioCertification.Certify(
            Artifact(GoodReport),
            reloaded: GoodReport.Replace("'Revenue'", "'Something else'", StringComparison.Ordinal)));

        Assert.Contains("changed across save and reload", message);
        // The message has to name the line, or somebody has to reproduce the failure to read it.
        Assert.Contains("First difference at line", message);
    }

    [Fact]
    public void Clause_4_ignores_the_line_endings_the_host_chose()
    {
        // A host that writes CRLF has not changed the author's script, and a contract that said it
        // had would fail on the desktop host for every journey.
        var crlf = string.Join("\r\n", GoodReport.Replace("\r\n", "\n").Split('\n'));
        StudioCertification.Certify(Artifact(GoodReport), reloaded: crlf);
    }

    [Fact]
    public void Clause_5_refuses_a_report_whose_round_trip_rewrites_it()
    {
        // STRUCTURE names a visual the script does not declare, so the patcher regenerates the page
        // rather than leaving it alone — a real round-trip failure, and the shape clause 5 exists
        // to catch.
        const string drifting = """
            CREATE CONNECTION corp AS MOCKDB();
            SELECT region INTO #sales FROM corp.orders;
            CREATE VISUAL RevenueTable AS TABLE (SOURCE = #sales);
            CREATE PAGE Main AS DASHBOARD (LAYOUT (STRUCTURE = 'A B', MAP ('A' = RevenueTable, 'B' = Missing)));
            """;

        var message = Message(() => StudioCertification.Certify(Artifact(drifting)));
        Assert.Contains("round-trip changed the file", message);
    }

    [Fact]
    public void The_failure_message_names_the_journey_and_the_host()
    {
        var artifact = new CertifiedArtifact("SSRS-like paginated", StudioHost.Portal, "report.json", GoodReport);

        var message = Message(() => StudioCertification.Certify(artifact));
        Assert.Contains("SSRS-like paginated", message);
        Assert.Contains("Portal", message);
    }
}
