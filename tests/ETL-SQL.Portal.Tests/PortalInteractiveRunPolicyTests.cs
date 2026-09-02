using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Services;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The interactive-run allow-list is a trust boundary: the Portal executes these statements under
/// the logged-in user's identity against ACL-resolved shared connections, so anything that escapes
/// the allow-list would run with that authority.
/// </summary>
[Trait("Category", "Portal")]
public sealed class PortalInteractiveRunPolicyTests
{
    private static Statement ParseSingle(string sql)
    {
        var script = new CoreParser(new Lexer(sql).Tokenize(), sql).Parse();
        return script.Statements.Single(s => s is not NoOpStatement);
    }

    private static string? Reject(string sql) => PortalInteractiveRunPolicy.Reject(ParseSingle(sql));

    [Theory]
    [InlineData("SELECT 1;")]
    [InlineData("SELECT UserID, UserName FROM m.Users;")]
    [InlineData("SELECT UserID FROM m.Users WHERE UserID > 10 ORDER BY UserID;")]
    public void AllowsReadOnlySelects(string sql) =>
        Assert.Null(Reject(sql));

    [Fact]
    public void AllowsSelectIntoTempTable() =>
        Assert.Null(Reject("SELECT UserID, UserName INTO #staging FROM m.Users;"));

    [Fact]
    public void RejectsSelectIntoRealTable() =>
        Assert.Contains("temp tables", Reject("SELECT UserID INTO m.Archive FROM m.Users;"));

    [Fact]
    public void RejectsCreateConnection()
    {
        // Connections are injected server-side from the ACL-gated shared catalog. A script-declared
        // connection would carry its own credentials and bypass that check entirely.
        Assert.Contains("shared connection", Reject("CREATE CONNECTION m AS MOCKDB();"));
    }

    [Fact]
    public void RejectsSet()
    {
        // The governance preamble sets the row cap, memory grant and session ceiling; a
        // script-supplied SET could raise them back up.
        Assert.NotNull(Reject("SET OPERATOR_MEMORY_GRANT = 4096;"));
    }

    [Theory]
    [InlineData("DROP TABLE m.Users;")]
    [InlineData("CREATE TABLE m.Staging (Id INT);")]
    public void RejectsWritesAndDdl(string sql) =>
        Assert.NotNull(Reject(sql));

    [Fact]
    public void RejectionNamesTheStatement() =>
        // The message reaches the user in the Messages tab, so it should say what was refused.
        Assert.Contains("Drop", Reject("DROP TABLE m.Users;"));

    [Fact]
    public void ExplainOfAnAllowedQuery_IsAllowed()
    {
        // EXPLAIN builds a plan without running the query, which is what lets a design surface offer
        // it at all. The plan is still only offered for a query this policy would let you run.
        Assert.Null(Reject("EXPLAIN SELECT region FROM corp.orders;"));
    }

    [Fact]
    public void ExplainAnalyze_IsRefusedByName()
    {
        // ANALYZE runs the query it explains, and would run it outside the checks the rest of this
        // policy applies. The refusal says which of the two words is the problem.
        var reason = Reject("EXPLAIN ANALYZE SELECT region FROM corp.orders;");

        Assert.NotNull(reason);
        Assert.Contains("EXPLAIN ANALYZE runs the query", reason);
    }

    [Fact]
    public void ExplainOfAStatementThePolicyRefuses_IsRefusedForTheSameReason()
    {
        var reason = Reject("EXPLAIN SELECT region INTO permanent_table FROM corp.orders;");

        Assert.NotNull(reason);
        Assert.Contains("temp tables", reason);
    }
}
