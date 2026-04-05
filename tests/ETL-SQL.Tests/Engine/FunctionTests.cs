using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;

namespace ETL_SQL.Tests
{
    public class FunctionTests
    {

        [Fact]
        public async Task TestCountFunction()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await evaluator.Evaluate(Parse("DECLARE @myList LIST; SET @myList = (1,2,3,4,5);"));
            await AssertEval(evaluator, "COUNT(@myList)", 5m);
            await AssertEval(evaluator, "COUNT(123)", 1m);
            await AssertEval(evaluator, "COUNT('hello')", 1m);
            await AssertEval(evaluator, "COUNT(NULL)", 0m);
        }

        [Fact]
        public async Task TestStringFunctions()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(evaluator, "UPPER('hello')", "HELLO");
            await AssertEval(evaluator, "LOWER('HELLO')", "hello");
            await AssertEval(evaluator, "REVERSE('abc')", "cba");
            await AssertEval(evaluator, "LEN('abc')", 3m);
            await AssertEval(evaluator, "TRIM('  abc  ')", "abc");
            await AssertEval(evaluator, "CONCAT('A', 'B', 'C')", "ABC");
            await AssertEval(evaluator, "SUBSTRING('HelloWorld', 1, 5)", "Hello");
            await AssertEval(evaluator, "REPLACE('abab', 'a', 'c')", "cbcb");
            await AssertEval(evaluator, "INITCAP('hello world')", "Hello World");
        }

        [Fact]
        public async Task TestMathFunctions()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(evaluator, "ABS(-10.5)", 10.5m);
            await AssertEval(evaluator, "ROUND(10.55, 1)", 10.6m);
            await AssertEval(evaluator, "CEILING(10.1)", 11m);
            await AssertEval(evaluator, "FLOOR(10.9)", 10m);
            await AssertEval(evaluator, "SQRT(16)", 4m);
            await AssertEval(evaluator, "MOD(10, 3)", 1m);
        }

        [Fact]
        public async Task TestDateFunctions()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(evaluator, "YEAR('2024-03-14')", 2024m);
            var res = await EvaluateExpression(evaluator, "GETDATE()");
            Assert.True(res is DateTime, "GETDATE() should return DateTime");
        }

        [Fact]
        public async Task TestGeneralFunctions()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(evaluator, "COALESCE(NULL, 'found')", "found");
            await AssertEval(evaluator, "ISNULL(NULL, 'fallback')", "fallback");
            await AssertEval(evaluator, "NULLIF('A', 'A')", null!);
            await AssertEval(evaluator, "NULLIF('A', 'B')", "A");
        }

        [Fact]
        public async Task TestLIKEWildcards()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            Assert.True(evaluator.EvaluateLike("Smith, John", "Smith%"), "Smith% match failed");
            Assert.True(evaluator.EvaluateLike("Test_1", "%_1"), "%_1 match failed");
            Assert.True(!evaluator.EvaluateLike("ABC", "A"), "A should not match ABC without %");
            await Task.CompletedTask;
        }

        [Fact]
        public async Task TestCast()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(evaluator, "CAST('123' AS INT)", 123m);
            await AssertEval(evaluator, "CAST(123.45 AS STRING)", "123.45");
        }

        [Fact]
        public async Task TestCaseExpression()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(evaluator, "CASE WHEN 1=1 THEN 'Yes' ELSE 'No' END", "Yes");
            await AssertEval(evaluator, "CASE WHEN 1=0 THEN 'Yes' ELSE 'No' END", "No");
            await AssertEval(evaluator, "CASE WHEN 1=0 THEN 'A' WHEN 1=1 THEN 'B' ELSE 'C' END", "B");
            await AssertEval(evaluator, "CASE WHEN 1=0 THEN 'A' WHEN 1=2 THEN 'B' ELSE 'C' END", "C");
            await AssertEval(evaluator, "CASE WHEN 1=1 THEN (CASE WHEN 2=2 THEN 'Nested' ELSE 'No' END) ELSE 'No' END", "Nested");
            await AssertEval(evaluator, "CASE WHEN 1=1 THEN 10+20 ELSE 0 END", 30m);
        }

        [Fact]
        public async Task TestStringAggOrdered()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #T (Val STRING); INSERT INTO #T VALUES ('C'), ('A'), ('B');"));
            
            var resAsc = await ev.EvaluateSelect((SelectStatement)Parse("SELECT STRING_AGG(Val, ',') WITHIN GROUP (ORDER BY Val ASC) AS Res FROM #T;").Statements[0]).FirstAsync();
            Assert.True(resAsc.Rows[0]["Res"]?.ToString() == "A,B,C", $"Ordered ASC failed: {resAsc.Rows[0]["Res"]}");
            
            var resDesc = await ev.EvaluateSelect((SelectStatement)Parse("SELECT STRING_AGG(Val, ',') WITHIN GROUP (ORDER BY Val DESC) AS Res FROM #T;").Statements[0]).FirstAsync();
            Assert.True(resDesc.Rows[0]["Res"]?.ToString() == "C,B,A", $"Ordered DESC failed: {resDesc.Rows[0]["Res"]}");
        }

        [Fact]
        public async Task TestDatePartRegression()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(evaluator, "DATEPART(YEAR, '2023-05-15 14:30:05')", 2023m);
            await AssertEval(evaluator, "DATEPART(MONTH, '2023-05-15 14:30:05')", 5m);
            await AssertEval(evaluator, "DATEPART(DAY, '2023-05-15 14:30:05')", 15m);
            await AssertEval(evaluator, "DATEPART(HOUR, '2023-05-15 14:30:05')", 14m);
            await AssertEval(evaluator, "DATEPART(MINUTE, '2023-05-15 14:30:05')", 30m);
            await AssertEval(evaluator, "DATEPART(SECOND, '2023-05-15 14:30:05')", 5m);
        }

        [Fact]
        public async Task TestNewFunctions()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            // DateTimeFromParts (year, month, day, hour, minute, second, ms)
            var dtParts = await EvaluateExpression(evaluator, "DATETIMEFROMPARTS(2024, 3, 24, 12, 0, 0, 500)");
            Assert.Equal(new DateTime(2024, 3, 24, 12, 0, 0, 500), dtParts);
            
            // HashBytes
            var hashRes = await EvaluateExpression(evaluator, "HASHBYTES('SHA2_256', 'test')");
            Assert.True(hashRes is byte[], "HASHBYTES should return byte[]");
            
            // Checksum (Robust 64-bit)
            var ck1 = await EvaluateExpression(evaluator, "CHECKSUM('ABC')");
            var ck2 = await EvaluateExpression(evaluator, "CHECKSUM('ABD')");
            Assert.NotEqual(ck1, ck2);
            Assert.True(ck1 is long, "CHECKSUM should return long");
            
            // NewID - UUID v7 (RFC 9562)
            var id1 = await EvaluateExpression(evaluator, "NEWID()");
            var id2 = await EvaluateExpression(evaluator, "NEWID()");
            Assert.NotEqual(id1, id2);
            Assert.True(id1 is Guid, "NEWID should return Guid");
            // Verify UUID version 7: 4th group starts with '7'
            var guidStr = id1!.ToString()!;
            Assert.Equal('7', guidStr[14]); // xxxxxxxx-xxxx-7xxx-... position 14
        }

        [Fact]
        public async Task TestAtTimeZone()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            // Use a standard zone name (Windows uses different names than IANA, but 'UTC' is universal)
            var res = await EvaluateExpression(evaluator, "'2024-03-24 12:00:00' AT TIME ZONE 'UTC'");
            Assert.True(res is DateTime, "AT TIME ZONE should return DateTime");
            Assert.Equal(12, ((DateTime)res).Hour);
        }

        [Fact]
        public async Task TestDateDiffRegression()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(evaluator, "DATEDIFF(DAY, '2023-01-01', '2023-12-31')", 364m);
            await AssertEval(evaluator, "DATEDIFF(MONTH, '2023-01-01', '2023-12-31')", 11m);
            await AssertEval(evaluator, "DATEDIFF(YEAR, '2023-01-01', '2023-12-31')", 0m);
        }

        private static async Task AssertEval(Evaluator ev, string expression, object? expected)
        {
            var res = await EvaluateExpression(ev, expression);
            if (res is int i) res = (decimal)i;
            if (expected is int ei) expected = (decimal)ei;
            Assert.Equal(expected, res);
        }

        private static async Task<object?> EvaluateExpression(Evaluator ev, string expression)
        {
            var varName = "@res_" + Guid.NewGuid().ToString("N");
            var script = $"DECLARE {varName} ANY; SET {varName} = ({expression});";
            await ev.Evaluate(Parse(script));
            return ev.Variables[varName];
        }

        [Fact]
        public async Task TestLikeEscape()
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            // Should match '100% pure'
            await AssertEval(evaluator, "'100% pure' LIKE '100\\% pure' ESCAPE '\\'", true);
            // Should not match '1000 pure' (escaped % is literal)
            await AssertEval(evaluator, "'1000 pure' LIKE '100\\% pure' ESCAPE '\\'", false);
            
            // Should match 'A_B'
            await AssertEval(evaluator, "'A_B' LIKE 'A\\_B' ESCAPE '\\'", true);
            // Should not match 'ATB' (escaped _ is literal)
            await AssertEval(evaluator, "'ATB' LIKE 'A\\_B' ESCAPE '\\'", false);

            // Escaping the escape character itself '\\' -> '\'
            await AssertEval(evaluator, "'C:\\dir' LIKE 'C:\\\\dir' ESCAPE '\\'", true);
            
            // Mixing escaped characters and wildcards
            await AssertEval(evaluator, "'100% pure orange juice' LIKE '100\\% pure %' ESCAPE '\\'", true);
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }

        
    }
}
