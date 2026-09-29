using ETL_SQL.Analysis.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// The transform steps: reshape one <c>#temp</c> into another, join two, or summarise one.
///
/// <para>All three write <c>SELECT … INTO #next FROM #prev</c> — the statement an author would type,
/// and the one the reference pages teach. <c>TRANSFORM</c> is ETL-SQL's name for a named algorithm such
/// as <c>FILL_DATES</c>, not for a general reshape, so it is not what these write.</para>
///
/// <para>The text is pinned because the author learns the statement from it.</para>
/// </summary>
public class PipelineTransformTaskTests
{
    private readonly PipelineTaskAuthoringService _tasks = new();

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private void AssertWrites(string expected, PipelineTaskDraft draft)
    {
        var preview = _tasks.Preview(draft);
        Assert.True(preview.Applied, preview.Error);
        Assert.Equal(Normalise(expected), Normalise(preview.Script));
    }

    // ── Filter & pick columns ────────────────────────────────────────────────

    [Fact]
    public void ReshapingPicksAddsAndFilters()
    {
        AssertWrites("""
            clean_users:
            SELECT UserID, UPPER(UserName) AS UserNameUpper
            INTO #clean_users
            FROM #staged_users
            WHERE Active = 1;
            """, new PipelineTaskDraft("clean_users", PipelineTaskKind.Reshape,
            Source: "#staged_users", Target: "#clean_users", Columns: "UserID",
            Derived: "UPPER(UserName) AS UserNameUpper", Condition: "Active = 1"));
    }

    [Fact]
    public void AddingAColumnKeepsEveryOtherColumn()
    {
        AssertWrites("""
            with_year:
            SELECT *, YEAR(OrderDate) AS OrderYear
            INTO #orders_with_year
            FROM #orders;
            """, new PipelineTaskDraft("with_year", PipelineTaskKind.Reshape,
            Source: "orders", Target: "orders_with_year", Derived: "YEAR(OrderDate) AS OrderYear"));
    }

    // ── Join ─────────────────────────────────────────────────────────────────

    [Fact]
    public void JoiningBringsTheChosenColumnsAcross()
    {
        AssertWrites("""
            users_with_orders:
            SELECT l.*, r.Total, r.OrderDate
            INTO #users_orders
            FROM #staged_users AS l
            INNER JOIN #orders AS r
                ON l.UserID = r.UserID;
            """, new PipelineTaskDraft("users_with_orders", PipelineTaskKind.Join,
            Source: "#staged_users", Right: "#orders", JoinType: "inner", Keys: "UserID",
            Columns: "Total, OrderDate", Target: "#users_orders"));
    }

    [Fact]
    public void ALeftJoinOnSeveralKeysKeepsEveryLeftRow()
    {
        AssertWrites("""
            with_region:
            SELECT l.*, r.RegionName
            INTO #with_region
            FROM #sales AS l
            LEFT JOIN #regions AS r
                ON l.Country = r.Country AND l.RegionID = r.RegionID;
            """, new PipelineTaskDraft("with_region", PipelineTaskKind.Join,
            Source: "#sales", Right: "#regions", JoinType: "LEFT", Keys: "Country, RegionID",
            Columns: "RegionName", Target: "#with_region"));
    }

    // ── Summarise ────────────────────────────────────────────────────────────

    [Fact]
    public void SummarisingGroupsAndMeasures()
    {
        AssertWrites("""
            sales_by_region:
            SELECT Region, SUM(Total) AS TotalSales, COUNT(*) AS Orders
            INTO #sales_by_region
            FROM #orders
            GROUP BY Region;
            """, new PipelineTaskDraft("sales_by_region", PipelineTaskKind.Summarise,
            Source: "#orders", Columns: "Region", Measures: "SUM(Total) AS TotalSales, COUNT(*) AS Orders",
            Target: "#sales_by_region"));
    }

    [Fact]
    public void SummarisingWithNoGroupsIsOneRow()
    {
        AssertWrites("""
            totals:
            SELECT SUM(Total) AS TotalSales
            INTO #totals
            FROM #orders;
            """, new PipelineTaskDraft("totals", PipelineTaskKind.Summarise,
            Source: "#orders", Measures: "SUM(Total) AS TotalSales", Target: "#totals"));
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PipelineTaskKind.Reshape, "#temp table it reads")]
    [InlineData(PipelineTaskKind.Join, "#temp table it reads")]
    [InlineData(PipelineTaskKind.Summarise, "#temp table it reads")]
    public void ATransformNeedsASource(PipelineTaskKind kind, string expected)
    {
        var result = _tasks.Preview(new PipelineTaskDraft("t", kind, Target: "#out"));

        Assert.False(result.Applied);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Right", "the #temp table it joins")]
    [InlineData("Keys", "columns it matches on")]
    [InlineData("Columns", "columns it brings across")]
    [InlineData("JoinType", "is not a join")]
    public void AJoinIsRefusedUntilItIsComplete(string missing, string expected)
    {
        var draft = new PipelineTaskDraft("t", PipelineTaskKind.Join, Source: "#a", Target: "#out",
            Right: missing == "Right" ? null : "#b",
            Keys: missing == "Keys" ? null : "Id",
            Columns: missing == "Columns" ? null : "Name",
            JoinType: missing == "JoinType" ? "SIDEWAYS" : "INNER");

        var result = _tasks.Preview(draft);

        Assert.False(result.Applied);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASummaryNeedsAMeasure()
    {
        var result = _tasks.Preview(new PipelineTaskDraft("t", PipelineTaskKind.Summarise,
            Source: "#orders", Columns: "Region", Target: "#out"));

        Assert.False(result.Applied);
        Assert.Contains("what it measures", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Free-text expressions go into the statement as typed, so the preview reads what it would write
    /// back through the parser: a broken expression is a sentence here, not a parse error on Add.
    /// </summary>
    [Theory]
    [InlineData("UPPER(UserName AS Broken")]
    [InlineData("1; DROP TABLE dbo.Users")]
    public void AnExpressionThatWouldNotParseIsRefusedInThePreview(string derived)
    {
        var result = _tasks.Preview(new PipelineTaskDraft("t", PipelineTaskKind.Reshape,
            Source: "#a", Target: "#b", Derived: derived));

        Assert.False(result.Applied);
        Assert.NotNull(result.Error);
    }

    // ── Running ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A pipeline built only from palette steps runs, and does what its steps say. The checks are the
    /// pipeline's own ASSERT steps, so a wrong result fails the run the way it would for an author.
    /// </summary>
    [Fact]
    public async Task APipelineBuiltFromTheStepsRunsAndItsNumbersAddUp()
    {
        var script = "CREATE CONNECTION m AS MOCKDB();\n";
        foreach (var draft in new[]
        {
            new PipelineTaskDraft("read_users", PipelineTaskKind.Extract,
                Connection: "m", Table: "Users", Columns: "UserID, UserName", Target: "#staged_users"),
            new PipelineTaskDraft("clean_users", PipelineTaskKind.Reshape,
                Source: "#staged_users", Columns: "UserID", Derived: "UPPER(UserName) AS UserNameUpper",
                Condition: "UserID > 0", Target: "#clean_users"),
            new PipelineTaskDraft("count_names", PipelineTaskKind.Summarise,
                Source: "#clean_users", Columns: "UserNameUpper", Measures: "COUNT(*) AS Users", Target: "#name_counts"),
            new PipelineTaskDraft("with_counts", PipelineTaskKind.Join,
                Source: "#clean_users", Right: "#name_counts", JoinType: "LEFT", Keys: "UserNameUpper",
                Columns: "Users", Target: "#with_counts"),
            new PipelineTaskDraft("some_users", PipelineTaskKind.Validation,
                Condition: "(SELECT COUNT(*) FROM #clean_users) > 0", Message: "No users were read."),
            new PipelineTaskDraft("join_kept_rows", PipelineTaskKind.Validation,
                Condition: "(SELECT COUNT(*) FROM #with_counts) = (SELECT COUNT(*) FROM #clean_users)",
                Message: "The join lost or duplicated rows."),
            new PipelineTaskDraft("counts_add_up", PipelineTaskKind.Validation,
                Condition: "(SELECT SUM(Users) FROM #name_counts) = (SELECT COUNT(*) FROM #clean_users)",
                Message: "The summary does not add up to the rows it summarised."),
            new PipelineTaskDraft("every_row_counted", PipelineTaskKind.Validation,
                Condition: "(SELECT COUNT(*) FROM #with_counts WHERE Users IS NULL) = 0",
                Message: "A row found no count."),
        })
        {
            var added = _tasks.Add(script, draft);
            Assert.True(added.Applied, $"{draft.Id}: {added.Error}");
            script = added.Script;
        }

        var evaluator = ETL_SQL.App.DependencyInjectionSetup.BuildServiceProvider()
            .GetRequiredService<ETL_SQL.Engine.Evaluator>();
        await evaluator.Evaluate(new ETL_SQL.Core.Parser.Lexer(script).TokenizeToScript());
    }

    // ── Reading back ─────────────────────────────────────────────────────────

    /// <summary>
    /// A SELECT INTO is an extract when it reads a connection's table, and a transform when it reads a
    /// #temp — a join or a summary when it joins or groups, and a reshape otherwise.
    /// </summary>
    [Theory]
    [InlineData("SELECT * INTO #x FROM warehouse.Users;", PipelineTaskKind.Extract)]
    [InlineData("SELECT UserID INTO #y FROM #x WHERE UserID > 0;", PipelineTaskKind.Reshape)]
    [InlineData("SELECT l.*, r.Total INTO #y FROM #x AS l INNER JOIN #x AS r ON l.UserID = r.UserID;", PipelineTaskKind.Join)]
    [InlineData("SELECT UserID, COUNT(*) AS N INTO #y FROM #x GROUP BY UserID;", PipelineTaskKind.Summarise)]
    public void ASelectIntoIsReadAsTheStepItIs(string statement, PipelineTaskKind kind)
    {
        var script = "CREATE CONNECTION warehouse AS MOCKDB();\nSELECT 1 AS UserID, 2 AS Total INTO #x;\n\nstep:\n" + statement + "\n";

        Assert.Equal(kind, _tasks.Read(script).Single(task => task.Id == "step").Kind);
    }
}
