using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// An in-memory table kept its indexes by column and not by name, so <c>DROP INDEX</c> and
/// <c>REINDEX</c> on a name nothing created succeeded. The engine now records each index name.
/// </summary>
public sealed class IndexCatalogTests
{
    private const string Setup = "CREATE TABLE #t (x INT, y VARCHAR); CREATE INDEX ix_x ON #t (x); ";

    [Theory]
    [InlineData("DROP INDEX ix_x;")]
    [InlineData("DROP INDEX ix_x ON #t;")]
    [InlineData("DROP INDEX IF EXISTS ix_x;")]
    [InlineData("DROP INDEX IF EXISTS never_made;")]
    [InlineData("DROP INDEX IF EXISTS never_made ON #t;")]
    [InlineData("REINDEX ix_x;")]
    [InlineData("REINDEX #t;")]
    [InlineData("REINDEX;")]
    [InlineData("REINDEX NOCASE;")]
    [InlineData("DROP INDEX ix_x; CREATE INDEX ix_x ON #t (y); DROP INDEX ix_x;")]
    public async Task AnIndexThatExistsIsAccepted(string sql)
    {
        await Run(Setup + sql);
    }

    [Theory]
    [InlineData("DROP INDEX never_made;", "never_made")]
    [InlineData("DROP INDEX never_made ON #t;", "never_made")]
    [InlineData("DROP INDEX ix_x; DROP INDEX ix_x;", "ix_x")]
    [InlineData("CREATE TABLE #other (x INT); DROP INDEX ix_x ON #other;", "ix_x")]
    [InlineData("DROP TABLE #t; DROP INDEX ix_x;", "ix_x")]
    [InlineData("REINDEX never_made;", "never_made")]
    [InlineData("DROP INDEX ix_x; REINDEX ix_x;", "ix_x")]
    [InlineData("DROP TABLE #t; REINDEX ix_x;", "ix_x")]
    [InlineData("CREATE CONNECTION mock AS MOCKDB(); DROP INDEX never_made;", "never_made")]
    [InlineData("CREATE CONNECTION mock AS MOCKDB(); DROP INDEX ix_x; DROP INDEX ix_x;", "ix_x")]
    public async Task AnIndexNothingCreatedFails(string sql, string name)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Run(Setup + sql));
        Assert.Matches($"Unknown index[^']*'{name}'", failure.ToString());
    }

    private static async Task Run(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
    }
}
