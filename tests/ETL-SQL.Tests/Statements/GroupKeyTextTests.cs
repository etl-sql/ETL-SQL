using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// Codes that look like numbers must group as text. Grouping keys used to parse any number-like
/// string, so '00123', '0123' and '123' became one group, returned as 123.
/// </summary>
public sealed class GroupKeyTextTests
{
    private const string Setup = @"
        CREATE TABLE #c (code VARCHAR(10));
        INSERT INTO #c VALUES ('00123'), ('123'), ('0123'), ('1,000'), ('1000'), ('A1');
    ";

    [Theory]
    [InlineData("SELECT code, COUNT(*) AS n FROM #c GROUP BY code;")]
    [InlineData("SELECT code, COUNT(*) AS n FROM #c GROUP BY code ORDER BY n;")]
    [InlineData("SELECT DISTINCT code FROM #c;")]
    [InlineData("SELECT code FROM #c UNION SELECT code FROM #c;")]
    public async Task NumberLikeCodesStayDistinct(string query)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Lexer(Setup + query).TokenizeToScript());

        var codes = evaluator.LastResult!.Rows.Select(r => r["code"]?.ToString()).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "00123", "0123", "1,000", "1000", "123", "A1" }, codes);
    }

    [Theory]
    [InlineData("123", 123)]
    [InlineData(" 123 ", 123)]
    [InlineData("-4.50", -4.5)]
    [InlineData("0", 0)]
    [InlineData("0.25", 0.25)]
    public void PlainNumberTextKeysAsTheNumber(string text, double number)
    {
        // '1' (from a text file) still joins and groups with 1 (from a typed column).
        Assert.Equal(new CompoundKey((decimal)number), new CompoundKey(text));
    }

    [Theory]
    [InlineData("00123", 123)]
    [InlineData("1,000", 1000)]
    [InlineData("(5)", -5)]
    [InlineData("+5", 5)]
    [InlineData("5.", 5)]
    public void FormattedNumberTextKeysAsText(string text, double number)
    {
        Assert.NotEqual(new CompoundKey((decimal)number), new CompoundKey(text));
    }
}
