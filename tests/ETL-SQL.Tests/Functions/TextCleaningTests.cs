using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions;

public sealed class TextCleaningTests
{
    private static Evaluator CreateEvaluator() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    private static async Task<Evaluator> RunAsync(string sql)
    {
        var eval = CreateEvaluator();
        await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
        return eval;
    }

    [Fact]
    public async Task CleanText_DefaultAndOrderedRules_WorkAndPreserveNulls()
    {
        var eval = await RunAsync("""
            SELECT
                CLEAN_TEXT('  acme   trading  ') AS def_clean,
                CLEAN_TEXT('  acme   trading  ', 'TRIM, COLLAPSE_WHITESPACE, TITLE') AS titled,
                CLEAN_TEXT('   ', 'TRIM, NULL_IF_EMPTY') AS emptied,
                CLEAN_TEXT('hello', 'UPPER') AS uppered,
                CLEAN_TEXT('HeLLo', 'LOWER') AS lowered,
                CLEAN_TEXT(NULL, 'TRIM') AS null_val,
                CLEAN_TEXT('abc', NULL) AS null_rules;
            """);

        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("acme trading", row["def_clean"]);
        Assert.Equal("Acme Trading", row["titled"]);
        Assert.Null(row["emptied"]);
        Assert.Equal("HELLO", row["uppered"]);
        Assert.Equal("hello", row["lowered"]);
        Assert.Null(row["null_val"]);
        Assert.Null(row["null_rules"]);
    }

    [Fact]
    public async Task CleanText_StripControl_RemovesControlAndZeroWidthChars()
    {
        var eval = await RunAsync("""
            DECLARE @raw VARCHAR = 'A' + CHAR(7) + 'B' + CHAR(0) + '  C';
            SELECT CLEAN_TEXT(@raw, 'STRIP_CONTROL, COLLAPSE_WHITESPACE, TRIM') AS cleaned;
            """);
        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("AB C", row["cleaned"]);
    }

    [Fact]
    public async Task Standardize_PhoneEmailPostalCode_ReturnCanonicalForms()
    {
        var eval = await RunAsync("""
            SELECT
                STANDARDIZE('(555) 123-4567', 'PHONE') AS p10,
                STANDARDIZE('+1 (555) 123-4567', 'PHONE') AS p11,
                STANDARDIZE('+44 20 7946 0958', 'PHONE') AS pintl,
                STANDARDIZE('no-digits', 'PHONE') AS pnone,
                STANDARDIZE('  Alice.Smith@Example.COM  ', 'EMAIL') AS email,
                STANDARDIZE('123456789', 'POSTAL_CODE') AS zip9,
                STANDARDIZE('k1a0b1', 'POSTAL_CODE') AS ca,
                STANDARDIZE(NULL, 'PHONE') AS null_val,
                STANDARDIZE('12345', NULL) AS null_fmt;
            """);

        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("555-123-4567", row["p10"]);
        Assert.Equal("+1-555-123-4567", row["p11"]);
        Assert.Equal("+442079460958", row["pintl"]);
        Assert.Null(row["pnone"]);
        Assert.Equal("alice.smith@example.com", row["email"]);
        Assert.Equal("12345-6789", row["zip9"]);
        Assert.Equal("K1A 0B1", row["ca"]);
        Assert.Null(row["null_val"]);
        Assert.Null(row["null_fmt"]);
    }

    [Fact]
    public async Task CleanTextAndStandardize_WorkInWhereJoinAndGroupBy()
    {
        var eval = await RunAsync("""
            CREATE TABLE #leads (Id INT, Email VARCHAR, City VARCHAR);
            INSERT INTO #leads VALUES
                (1, '  ALICE@Example.com ', '  new   york '),
                (2, 'bob@example.com', 'NEW YORK'),
                (3, '  carol@example.com ', 'boston');

            CREATE TABLE #vip (Email VARCHAR);
            INSERT INTO #vip VALUES ('alice@example.com'), ('bob@example.com');

            SELECT CLEAN_TEXT(l.City, 'TRIM, COLLAPSE_WHITESPACE, UPPER') AS city_key, COUNT(*) AS cnt
            FROM #leads l
            INNER JOIN #vip v ON STANDARDIZE(l.Email, 'EMAIL') = v.Email
            WHERE CLEAN_TEXT(l.City, 'TRIM, COLLAPSE_WHITESPACE, UPPER') = 'NEW YORK'
            GROUP BY CLEAN_TEXT(l.City, 'TRIM, COLLAPSE_WHITESPACE, UPPER');
            """);

        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("NEW YORK", row["city_key"]);
        Assert.Equal(2m, Convert.ToDecimal(row["cnt"]));
    }

    [Fact]
    public async Task TransformUsingClean_AllStringColsAndSpecificCols_PreservesSchemaAndNulls()
    {
        var eval = await RunAsync("""
            CREATE TABLE #raw (Id INT, Name VARCHAR, Email VARCHAR, Postal VARCHAR);
            INSERT INTO #raw VALUES
                (1, '  alice   smith  ', '  ALICE@Example.COM ', '123456789'),
                (2, '   ', NULL, 'k1a0b1');

            TRANSFORM #cleaned
            FROM #raw
            USING CLEAN (
                COLS = '*',
                RULES = 'STRIP_CONTROL, COLLAPSE_WHITESPACE, TRIM, NULL_IF_EMPTY'
            );

            TRANSFORM #cleaned
            USING CLEAN (
                COLS = 'Email',
                RULES = 'EMAIL'
            );

            TRANSFORM #cleaned
            USING CLEAN (
                COLS = 'Postal',
                RULES = 'POSTAL_CODE'
            );

            SELECT Id, Name, Email, Postal FROM #cleaned ORDER BY Id;
            """);

        Assert.Equal(2, eval.LastResult!.Rows.Count);
        Assert.Equal(1m, Convert.ToDecimal(eval.LastResult.Rows[0]["Id"]));
        Assert.Equal("alice smith", eval.LastResult.Rows[0]["Name"]);
        Assert.Equal("alice@example.com", eval.LastResult.Rows[0]["Email"]);
        Assert.Equal("12345-6789", eval.LastResult.Rows[0]["Postal"]);

        Assert.Equal(2m, Convert.ToDecimal(eval.LastResult.Rows[1]["Id"]));
        Assert.Null(eval.LastResult.Rows[1]["Name"]);
        Assert.Null(eval.LastResult.Rows[1]["Email"]);
        Assert.Equal("K1A 0B1", eval.LastResult.Rows[1]["Postal"]);
    }

    [Fact]
    public async Task TransformUsingClean_WhatIf_DoesNotMutateTarget()
    {
        var eval = await RunAsync("""
            CREATE TABLE #src (Id INT, Name VARCHAR);
            INSERT INTO #src VALUES (1, '  dirty   name  ');

            SET WHAT_IF ON;
            TRANSFORM #src
            USING CLEAN (COLS = 'Name', RULES = 'TRIM, COLLAPSE_WHITESPACE');
            SET WHAT_IF OFF;

            SELECT Name FROM #src;
            """);

        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("  dirty   name  ", row["Name"]);
    }

    [Fact]
    public async Task CleanText_Standardize_AndTransformClean_RejectInvalidOptions()
    {
        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("SELECT CLEAN_TEXT('a', 'NO_SUCH_RULE');"));
        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("SELECT STANDARDIZE('a', 'NO_SUCH_FMT');"));
        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("""
            CREATE TABLE #t (Id INT, Name VARCHAR);
            INSERT INTO #t VALUES (1, 'a');
            TRANSFORM #out FROM #t USING CLEAN (COLS = 'MissingCol', RULES = 'TRIM');
            """));
        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("""
            CREATE TABLE #t (Id INT, Name VARCHAR);
            INSERT INTO #t VALUES (1, 'a');
            TRANSFORM #out FROM #t USING CLEAN (COLS = '*', RULES = 'BOGUS');
            """));
    }

    [Fact]
    public void CleanTextAndStandardize_AreRegisteredInMetadataAndHelp()
    {
        Assert.Contains("CLEAN_TEXT", LanguageMetadata.Functions);
        Assert.Contains("STANDARDIZE", LanguageMetadata.Functions);

        var sp = DependencyInjectionSetup.BuildServiceProvider();
        var registry = sp.GetRequiredService<IFunctionRegistry>();
        Assert.Contains("CLEAN_TEXT", registry.GetHelp("CLEAN_TEXT"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STANDARDIZE", registry.GetHelp("STANDARDIZE"), StringComparison.OrdinalIgnoreCase);
    }
}
