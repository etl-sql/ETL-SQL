using ETL_SQL.Analysis.Services;

namespace ETL_SQL.Tests.Analysis;

/// <summary>
/// What a pipeline task can see from where it sits.
///
/// <para>The claim this makes is positional, and that is the whole value of it. ETL-SQL runs a script
/// top to bottom, so a variable declared below a task is not one that task can read and a
/// <c>#temp</c> created below it does not exist yet. A panel that listed every name in the file would
/// tell the author they can use things that are not there — true of the file, false of the moment,
/// and only discoverable at run time.</para>
/// </summary>
public class ScriptScopeTests
{
    private readonly ScriptScopeService _scope = new();

    private const string Script = """
        CREATE CONNECTION staging_db AS MOCKDB();

        DECLARE @batch VARCHAR = 'B-001';

        SELECT 1 AS OrderId, 'north' AS Region INTO #orders;

        load_orders:
        EXECUTE staging_db BEGIN
            SELECT 1;
        END;

        DECLARE @after INT = 7;

        SELECT 2 AS RateId INTO #rates;

        publish_orders:
        EXECUTE staging_db BEGIN
            SELECT 2;
        END;
        """;

    private ScriptScope At(string id) => _scope.At(Script, id);

    [Fact]
    public void ATaskSeesOnlyWhatWasDeclaredAboveIt()
    {
        var scope = At("load_orders");

        Assert.True(scope.Resolved, scope.Error);
        Assert.Equal(["@batch"], scope.Variables.Select(variable => variable.Name));
        Assert.Equal(["#orders"], scope.TempTables.Select(table => table.Name));
    }

    [Fact]
    public void ATaskFurtherDownSeesWhatTheOneAboveItLeftBehind()
    {
        var scope = At("publish_orders");

        Assert.True(scope.Resolved, scope.Error);
        Assert.Equal(["@batch", "@after"], scope.Variables.Select(variable => variable.Name));
        Assert.Equal(["#orders", "#rates"], scope.TempTables.Select(table => table.Name));
    }

    [Fact]
    public void AVariableCarriesItsTypeAndTheValueTheAuthorWrote()
    {
        var variable = Assert.Single(At("load_orders").Variables);

        Assert.Equal("@batch", variable.Name);
        Assert.Equal("VARCHAR", variable.Type);
        Assert.Equal("'B-001'", variable.Value);
        Assert.Equal("declared", variable.Origin);
        Assert.Equal(3, variable.Line);
    }

    /// <summary>
    /// A re-assignment is what the task will actually read, so the panel shows that value rather than
    /// the one the declaration started with.
    /// </summary>
    [Fact]
    public void AReassignedVariableShowsTheValueTheTaskWillSee()
    {
        const string script = """
            DECLARE @batch VARCHAR = 'B-001';
            SET @batch = 'B-002';

            load_orders:
            ASSERT 1 = 1, 'ok';
            """;

        var variable = Assert.Single(_scope.At(script, "load_orders").Variables);

        Assert.Equal("@batch", variable.Name);
        Assert.Equal("'B-002'", variable.Value);
    }

    [Fact]
    public void ATempTableNamesTheColumnsItsSelectProduces()
    {
        var table = Assert.Single(At("load_orders").TempTables);

        Assert.Equal("#orders", table.Name);
        Assert.Equal("SELECT INTO", table.Origin);
        Assert.Equal(["OrderId", "Region"], table.Columns.Select(column => column.Name));
    }

    [Fact]
    public void ACreateTableCarriesItsDeclaredColumnTypes()
    {
        const string script = """
            CREATE TABLE #staged (OrderId INT, Region VARCHAR(20));

            load_orders:
            ASSERT 1 = 1, 'ok';
            """;

        var table = Assert.Single(_scope.At(script, "load_orders").TempTables);

        Assert.Equal("#staged", table.Name);
        Assert.Equal("CREATE TABLE", table.Origin);
        Assert.Equal(["OrderId", "Region"], table.Columns.Select(column => column.Name));
        Assert.Equal("INT", table.Columns[0].Type);
    }

    // ── Scope is a place, not a file ─────────────────────────────────────────

    /// <summary>
    /// A task inside a loop can read the loop variable. One outside it cannot — which is exactly the
    /// distinction a flat list of every name in the script would lose.
    /// </summary>
    [Fact]
    public void ATaskInsideALoopSeesTheLoopVariableAndOneOutsideItDoesNot()
    {
        const string script = """
            SELECT 1 AS Id INTO #regions;

            per_region:
            FOREACH @region IN #regions
            BEGIN
                inside:
                ASSERT 1 = 1, 'ok';
            END;

            outside:
            ASSERT 1 = 1, 'ok';
            """;

        var inside = _scope.At(script, "inside");
        Assert.True(inside.Resolved, inside.Error);
        var loopVariable = Assert.Single(inside.Variables);
        Assert.Equal("@region", loopVariable.Name);
        Assert.Equal("loop", loopVariable.Origin);
        Assert.Equal("#regions", loopVariable.Value);

        var outside = _scope.At(script, "outside");
        Assert.True(outside.Resolved, outside.Error);
        Assert.DoesNotContain(outside.Variables, variable => variable.Name == "@region");
    }

    /// <summary>
    /// What a sibling branch staged is not in scope: the branches of a <c>PARALLEL</c> block run at
    /// the same time, so reading one from another is a race, not a dependency.
    /// </summary>
    [Fact]
    public void ATaskDoesNotSeeWhatASiblingBranchStaged()
    {
        const string script = """
            load_all:
            PARALLEL BEGIN
                branch_a:
                ASSERT 1 = 1, 'ok';

                SELECT 1 AS Id INTO #from_a;

                branch_b:
                ASSERT 1 = 1, 'ok';
            END;

            after_all:
            ASSERT 1 = 1, 'ok';
            """;

        // branch_b is written after #from_a in the block, so the script does put it in scope — the
        // point of this test is the branch that runs before it, which cannot see it at all.
        Assert.DoesNotContain(_scope.At(script, "branch_a").TempTables, table => table.Name == "#from_a");
    }

    [Fact]
    public void NothingAboveATaskIsNotTheSameAsNotKnowing()
    {
        const string script = """
            load_orders:
            ASSERT 1 = 1, 'ok';
            """;

        var scope = _scope.At(script, "load_orders");

        Assert.True(scope.Resolved);
        Assert.Empty(scope.Variables);
        Assert.Empty(scope.TempTables);
    }

    [Theory]
    [InlineData("no_such_task")]
    [InlineData("")]
    [InlineData(null)]
    public void AScopeItCannotWorkOutIsReportedAsUnresolved(string? taskId)
    {
        var scope = _scope.At(Script, taskId);

        // Not an empty scope: "there is nothing here" and "I cannot tell" are different answers, and
        // rendering the second as the first is how a panel quietly lies.
        Assert.False(scope.Resolved);
        Assert.NotNull(scope.Error);
        Assert.Empty(scope.Variables);
        Assert.Empty(scope.TempTables);
    }

    [Fact]
    public void AScriptThatDoesNotParseIsReportedWithItsReason()
    {
        var scope = _scope.At("SELECT FROM WHERE ;;;", "load_orders");

        Assert.False(scope.Resolved);
        Assert.NotNull(scope.Error);
    }

    // -- Scope at a cursor, rather than at a task -------------------------------------------------

    private const string CursorScript = """
        CREATE CONNECTION corp AS MOCKDB();

        DECLARE @cutoff INT = 100;

        SELECT order_id, total INTO #recent FROM corp.orders WHERE total > @cutoff;

        SELECT region, total INTO #by_region FROM #recent;

        DECLARE @after INT = 7;
        """;

    [Fact]
    public void AtLine_ReportsOnlyWhatTheStatementUnderTheCursorCanSee()
    {
        // Line 7 is the SELECT that reads #recent. #recent exists by then; #by_region is what this
        // very statement produces, and @after is declared below it.
        var scope = _scope.AtLine(CursorScript, 7);

        Assert.True(scope.Resolved);
        Assert.Contains(scope.TempTables, temp => temp.Name == "#recent");
        Assert.DoesNotContain(scope.TempTables, temp => temp.Name == "#by_region");
        Assert.Contains(scope.Variables, variable => variable.Name == "@cutoff");
        Assert.DoesNotContain(scope.Variables, variable => variable.Name == "@after");
    }

    [Fact]
    public void AtLine_HandsBackTheStatementAndEverythingAboveIt()
    {
        var scope = _scope.AtLine(CursorScript, 7);

        // The statement itself, so a caller can plan or run just that one.
        Assert.Equal("SELECT region, total INTO #by_region FROM #recent;", scope.StatementText);
        Assert.Equal(7, scope.StatementLine);

        // And the prefix that builds what it reads. Without it the statement is unplannable: the
        // #temp tables in scope do not exist until the statements that create them have run.
        Assert.Contains("INTO #recent", scope.PrefixScript);
        Assert.DoesNotContain("#by_region", scope.PrefixScript);
        Assert.DoesNotContain("@after", scope.PrefixScript);
    }

    [Fact]
    public void AtLine_ACursorInsideAStatementResolvesToThatStatement()
    {
        // A caret anywhere in a multi-line statement is a caret in that statement, not in the next.
        var scope = _scope.AtLine(CursorScript, 8);

        Assert.Equal("SELECT region, total INTO #by_region FROM #recent;", scope.StatementText);
    }

    [Fact]
    public void AtLine_RefusesAScriptItCannotParse()
    {
        var scope = _scope.AtLine(">>> INVALID <<<", 1);

        Assert.False(scope.Resolved);
        Assert.NotNull(scope.Error);
    }
}
