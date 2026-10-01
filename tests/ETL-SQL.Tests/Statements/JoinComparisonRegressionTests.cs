using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

[Trait("CompatBreak", "0.20.0")]
public sealed class JoinComparisonRegressionTests
{
    [Theory]
    [InlineData("HASH", false)]
    [InlineData("HASH", true)]
    [InlineData("MERGE", false)]
    [InlineData("MERGE", true)]
    [InlineData("LOOP", false)]
    [InlineData("LOOP", true)]
    public async Task JoinHints_UseTheActiveStringComparison(string algorithm, bool caseSensitive)
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var providerLifetime = provider as IAsyncDisposable;
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        evaluator.CaseSensitiveComparison = caseSensitive;
        await evaluator.Evaluate(Parse("""
            CREATE TABLE #l (join_key VARCHAR(20));
            CREATE TABLE #r (join_key VARCHAR(20));
            INSERT INTO #l VALUES ('alpha'), ('beta');
            INSERT INTO #r VALUES ('ALPHA'), ('beta');
            """));
        var keys = new List<string>();
        await foreach (var batch in evaluator.ExecuteQuery(Parse($"SELECT l.join_key FROM #l l INNER {algorithm} JOIN #r r ON l.join_key = r.join_key;").Statements[0]))
            keys.AddRange(batch.Rows.Select(row => row["join_key"]!.ToString()!));
        Assert.Equal(caseSensitive ? new[] { "beta" } : new[] { "alpha", "beta" }, keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalHashJoin_UsesTheSameComparisonForPartitionBuildAndProbe(bool caseSensitive)
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        await using var providerLifetime = provider as IAsyncDisposable;
        var logger = provider.GetRequiredService<ILogger>();
        logger.SuppressConsole = true;
        var evaluator = provider.GetRequiredService<Evaluator>();
        evaluator.CaseSensitiveComparison = caseSensitive;
        evaluator.ExternalHashPartitions = 4;
        var left = new[] { new Row { ["left_key"] = "alpha" }, new Row { ["left_key"] = "beta" } };
        var right = new[] { new Row { ["right_key"] = "ALPHA" }, new Row { ["right_key"] = "beta" } };
        var join = new JoinClause("INNER", new TableReference("right"),
            new BinaryExpression(new IdentifierExpression("left_key"), TokenType.EQUALS, new IdentifierExpression("right_key")));
        var keys = new List<string>();
        await foreach (var row in new ExternalJoinEngine(evaluator, logger).ApplyHashJoinExternal(
            left.ToAsyncEnumerable(), right.ToAsyncEnumerable(), join, ["left_key"], ["right_key"]))
            keys.Add(row["left_key"]!.ToString()!);
        Assert.Equal(caseSensitive ? new[] { "beta" } : new[] { "alpha", "beta" }, keys.Order(StringComparer.Ordinal));
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();
}
