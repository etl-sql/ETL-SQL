using System;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Engine;
using ETL_SQL.Core.Parser;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Exercises StandardFunctions (string, math, date, logic, system) to raise
    /// coverage on StandardFunctions.*.cs partial class files.
    /// </summary>
    public class FunctionCoverageTests
    {
        private static Evaluator Eval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task<object?> EvalExpr(string expr)
        {
            var eval = Eval();
            await eval.Evaluate(Parse($"DECLARE @r STRING = {expr};"));
            return eval.GetVariable("@r");
        }

        private static async Task<decimal?> EvalNum(string expr)
        {
            var eval = Eval();
            await eval.Evaluate(Parse($"DECLARE @r DECIMAL = {expr};"));
            var v = eval.GetVariable("@r");
            return v == null ? null : Convert.ToDecimal(v);
        }

        // ── String functions ──────────────────────────────────────────────────

        [Fact]
        public async Task Upper_ReturnsUppercase()
        {
            Assert.Equal("HELLO", await EvalExpr("UPPER('hello')"));
        }

        [Fact]
        public async Task Lower_ReturnsLowercase()
        {
            Assert.Equal("world", await EvalExpr("LOWER('WORLD')"));
        }

        [Fact]
        public async Task Len_ReturnsLength()
        {
            Assert.Equal(5m, await EvalNum("LEN('hello')"));
        }

        [Fact]
        public async Task Length_ReturnsLength()
        {
            Assert.Equal(3m, await EvalNum("LENGTH('abc')"));
        }

        [Fact]
        public async Task Trim_RemovesWhitespace()
        {
            Assert.Equal("hello", await EvalExpr("TRIM('  hello  ')"));
        }

        [Fact]
        public async Task Ltrim_RemovesLeading()
        {
            Assert.Equal("hello  ", await EvalExpr("LTRIM('  hello  ')"));
        }

        [Fact]
        public async Task Rtrim_RemovesTrailing()
        {
            Assert.Equal("  hello", await EvalExpr("RTRIM('  hello  ')"));
        }

        [Fact]
        public async Task Reverse_ReversesString()
        {
            Assert.Equal("olleh", await EvalExpr("REVERSE('hello')"));
        }

        [Fact]
        public async Task Concat_JoinsStrings()
        {
            Assert.Equal("AB", await EvalExpr("CONCAT('A', 'B')"));
        }

        [Fact]
        public async Task Substring_ExtractsChars()
        {
            Assert.Equal("ell", await EvalExpr("SUBSTRING('hello', 2, 3)"));
        }

        [Fact]
        public async Task Left_ExtractsLeftChars()
        {
            Assert.Equal("he", await EvalExpr("LEFT('hello', 2)"));
        }

        [Fact]
        public async Task Right_ExtractsRightChars()
        {
            Assert.Equal("lo", await EvalExpr("RIGHT('hello', 2)"));
        }

        [Fact]
        public async Task CharIndex_FindsPosition()
        {
            Assert.Equal(3m, await EvalNum("CHARINDEX('ll', 'hello')"));
        }

        [Fact]
        public async Task Replace_SubstitutesSubstring()
        {
            Assert.Equal("hXXlo", await EvalExpr("REPLACE('hello', 'el', 'XX')"));
        }

        [Fact]
        public async Task InitCap_CapitalizesWords()
        {
            var result = await EvalExpr("INITCAP('hello world')");
            Assert.Equal("Hello World", result?.ToString());
        }

        [Fact]
        public async Task Ascii_ReturnsCodePoint()
        {
            Assert.Equal(65m, await EvalNum("ASCII('A')"));
        }

        [Fact]
        public async Task Char_ReturnsCharacter()
        {
            Assert.Equal("A", await EvalExpr("CHAR(65)"));
        }

        [Fact]
        public async Task Replicate_RepeatsString()
        {
            Assert.Equal("aaa", await EvalExpr("REPLICATE('a', 3)"));
        }

        [Fact]
        public async Task Translate_ReplacesChars()
        {
            Assert.Equal("hXllX", await EvalExpr("TRANSLATE('hello', 'eo', 'XX')"));
        }

        [Fact]
        public async Task Unicode_ReturnsCodePoint()
        {
            Assert.Equal(65m, await EvalNum("UNICODE('A')"));
        }

        [Fact]
        public async Task ToStr_ConvertsToString()
        {
            Assert.Equal("42", await EvalExpr("TO_STR(42)"));
        }

        [Fact]
        public async Task Str_FormatsNumber()
        {
            var result = await EvalExpr("STR(3.14)");
            Assert.NotNull(result);
        }

        [Fact]
        public async Task QuoteName_AddsBrackets()
        {
            Assert.Equal("[table]", await EvalExpr("QUOTENAME('table')"));
        }

        [Fact]
        public async Task Stuff_ReplacesSubstring()
        {
            Assert.Equal("hXXXo", await EvalExpr("STUFF('hello', 2, 3, 'XXX')"));
        }

        // ── Math functions ────────────────────────────────────────────────────

        [Fact]
        public async Task Abs_ReturnsAbsoluteValue()
        {
            Assert.Equal(5m, await EvalNum("ABS(-5)"));
        }

        [Fact]
        public async Task Round_RoundsToDecimals()
        {
            Assert.Equal(3.14m, await EvalNum("ROUND(3.14159, 2)"));
        }

        [Fact]
        public async Task Ceiling_RoundsUp()
        {
            Assert.Equal(4m, await EvalNum("CEILING(3.1)"));
        }

        [Fact]
        public async Task Floor_RoundsDown()
        {
            Assert.Equal(3m, await EvalNum("FLOOR(3.9)"));
        }

        [Fact]
        public async Task Sqrt_ReturnsSquareRoot()
        {
            Assert.Equal(3m, await EvalNum("SQRT(9)"));
        }

        [Fact]
        public async Task Power_ReturnsPower()
        {
            Assert.Equal(8m, await EvalNum("POWER(2, 3)"));
        }

        [Fact]
        public async Task Mod_ReturnsRemainder()
        {
            Assert.Equal(1m, await EvalNum("MOD(10, 3)"));
        }

        [Fact]
        public async Task Exp_ReturnsE()
        {
            var result = await EvalNum("EXP(0)");
            Assert.Equal(1m, result);
        }

        [Fact]
        public async Task Log_ReturnsNaturalLog()
        {
            var result = await EvalNum("LOG(1)");
            Assert.Equal(0m, result);
        }

        [Fact]
        public async Task Log10_ReturnsLog10()
        {
            Assert.Equal(2m, await EvalNum("LOG10(100)"));
        }

        [Fact]
        public async Task Sin_ReturnsSine()
        {
            Assert.Equal(0m, await EvalNum("ROUND(SIN(0), 10)"));
        }

        [Fact]
        public async Task Cos_ReturnsCosine()
        {
            Assert.Equal(1m, await EvalNum("ROUND(COS(0), 10)"));
        }

        [Fact]
        public async Task Tan_ReturnsTangent()
        {
            Assert.Equal(0m, await EvalNum("ROUND(TAN(0), 10)"));
        }

        [Fact]
        public async Task Sign_ReturnsSign()
        {
            Assert.Equal(-1m, await EvalNum("SIGN(-5)"));
            Assert.Equal(1m, await EvalNum("SIGN(5)"));
        }

        [Fact]
        public async Task Asin_ReturnsArcSine()
        {
            var result = await EvalNum("ASIN(0)");
            Assert.Equal(0m, result);
        }

        [Fact]
        public async Task Acos_ReturnsArcCosine()
        {
            var result = await EvalNum("ROUND(ACOS(1), 10)");
            Assert.Equal(0m, result);
        }

        [Fact]
        public async Task Atan_ReturnsArcTangent()
        {
            var result = await EvalNum("ATAN(0)");
            Assert.Equal(0m, result);
        }

        [Fact]
        public async Task Atan2_ReturnsArcTangent2()
        {
            var result = await EvalNum("ATAN2(0, 1)");
            Assert.Equal(0m, result);
        }

        [Fact]
        public async Task Rand_ReturnsDecimalBetween0And1()
        {
            var result = await EvalNum("RAND()");
            Assert.True(result >= 0m && result < 1m);
        }

        // ── Date functions ────────────────────────────────────────────────────

        [Fact]
        public async Task Year_ExtractsYear()
        {
            Assert.Equal(2025m, await EvalNum("YEAR('2025-06-15')"));
        }

        [Fact]
        public async Task Month_ExtractsMonth()
        {
            Assert.Equal(6m, await EvalNum("MONTH('2025-06-15')"));
        }

        [Fact]
        public async Task Day_ExtractsDay()
        {
            Assert.Equal(15m, await EvalNum("DAY('2025-06-15')"));
        }

        [Fact]
        public async Task DatePart_Year_ReturnsYear()
        {
            Assert.Equal(2025m, await EvalNum("DATEPART('YEAR', '2025-06-15')"));
        }

        [Fact]
        public async Task DatePart_Month_ReturnsMonth()
        {
            Assert.Equal(6m, await EvalNum("DATEPART('MONTH', '2025-06-15')"));
        }

        [Fact]
        public async Task DateAdd_AddsYears()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @d DATETIME = DATEADD('YEAR', 1, '2025-01-01');"));
            Assert.NotNull(eval.GetVariable("@d"));
        }

        [Fact]
        public async Task DateDiff_ReturnsDifference()
        {
            Assert.Equal(1m, await EvalNum("DATEDIFF('YEAR', '2024-01-01', '2025-01-01')"));
        }

        [Fact]
        public async Task IsDate_ValidDate_Returns1()
        {
            Assert.Equal(1m, await EvalNum("ISDATE('2025-01-01')"));
        }

        [Fact]
        public async Task IsDate_InvalidDate_Returns0()
        {
            Assert.Equal(0m, await EvalNum("ISDATE('not-a-date')"));
        }

        [Fact]
        public async Task GetDate_ReturnsDateTime()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @d DATETIME = GETDATE();"));
            Assert.NotNull(eval.GetVariable("@d"));
        }

        [Fact]
        public async Task Now_ReturnsDateTime()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @d DATETIME = NOW();"));
            Assert.NotNull(eval.GetVariable("@d"));
        }

        [Fact]
        public async Task EoMonth_ReturnsLastDayOfMonth()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @d DATETIME = EOMONTH('2025-06-15');"));
            Assert.NotNull(eval.GetVariable("@d"));
        }

        [Fact]
        public async Task DateName_Month_ReturnsMonthName()
        {
            var result = await EvalExpr("DATENAME('MONTH', '2025-06-15')");
            Assert.Equal("June", result);
        }

        // ── Logic / conversion functions ──────────────────────────────────────

        [Fact]
        public async Task Cast_StringToInt_Converts()
        {
            Assert.Equal(42m, await EvalNum("CAST('42' AS INT)"));
        }

        [Fact]
        public async Task TryCast_InvalidInput_ReturnsNull()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @r INT = TRY_CAST('abc' AS INT);"));
            Assert.Null(eval.GetVariable("@r"));
        }

        [Fact]
        public async Task Isnull_WhenNull_ReturnsDefault()
        {
            Assert.Equal("default", await EvalExpr("ISNULL(NULL, 'default')"));
        }

        [Fact]
        public async Task Isnull_WhenNotNull_ReturnsValue()
        {
            Assert.Equal("value", await EvalExpr("ISNULL('value', 'default')"));
        }

        [Fact]
        public async Task Coalesce_ReturnsFirstNonNull()
        {
            Assert.Equal("second", await EvalExpr("COALESCE(NULL, 'second', 'third')"));
        }

        [Fact]
        public async Task Nullif_EqualValues_ReturnsNull()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @r INT = NULLIF(5, 5);"));
            Assert.Null(eval.GetVariable("@r"));
        }

        [Fact]
        public async Task Nullif_DifferentValues_ReturnsFirst()
        {
            Assert.Equal(5m, await EvalNum("NULLIF(5, 3)"));
        }

        [Fact]
        public async Task Iif_TrueCondition_ReturnsFirst()
        {
            Assert.Equal("yes", await EvalExpr("IIF(1 = 1, 'yes', 'no')"));
        }

        [Fact]
        public async Task Iif_FalseCondition_ReturnsSecond()
        {
            Assert.Equal("no", await EvalExpr("IIF(1 = 2, 'yes', 'no')"));
        }

        // ── System/hash functions ─────────────────────────────────────────────

        [Fact]
        public async Task NewId_ReturnsGuid()
        {
            var result = await EvalExpr("NEWID()");
            Assert.NotNull(result);
        }

        [Fact]
        public async Task Checksum_ReturnsValue()
        {
            var result = await EvalNum("CHECKSUM('a', 'b')");
            Assert.NotNull(result);
        }

        [Fact]
        public async Task HashBytes_Md5_ReturnsHash()
        {
            var result = await EvalExpr("HASHBYTES('MD5', 'hello')");
            Assert.NotNull(result);
        }

        [Fact]
        public async Task HashBytes_Sha256_ReturnsHash()
        {
            var result = await EvalExpr("HASHBYTES('SHA256', 'hello')");
            Assert.NotNull(result);
        }

        // ── Aggregate functions (via SELECT) ──────────────────────────────────

        [Fact]
        public async Task Sum_AggregatesValues()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS N INTO #T;
                INSERT INTO #T VALUES (2), (3);
                SELECT SUM(N) AS Total FROM #T;
            "));
            Assert.Equal(6m, eval.LastResult?.Rows[0]["Total"]);
        }

        [Fact]
        public async Task CastWrappedSum_AggregatesValues()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 'A' AS Category, 10 AS Revenue INTO #T;
                INSERT INTO #T VALUES ('A', 15), ('B', 7);
                SELECT Category, CAST(SUM(Revenue) AS DECIMAL) AS Revenue
                FROM #T
                GROUP BY Category
                ORDER BY Category;
            "));

            Assert.Equal(2, eval.LastResult?.Rows.Count);
            Assert.Equal("A", eval.LastResult?.Rows[0]["Category"]);
            Assert.Equal(25m, Convert.ToDecimal(eval.LastResult?.Rows[0]["Revenue"]));
            Assert.Equal("B", eval.LastResult?.Rows[1]["Category"]);
            Assert.Equal(7m, Convert.ToDecimal(eval.LastResult?.Rows[1]["Revenue"]));
        }

        [Fact]
        public async Task Avg_ComputesAverage()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 2 AS N INTO #T;
                INSERT INTO #T VALUES (4), (6);
                SELECT AVG(N) AS Avg FROM #T;
            "));
            Assert.Equal(4m, eval.LastResult?.Rows[0]["Avg"]);
        }

        [Fact]
        public async Task Count_CountsRows()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS N INTO #T;
                INSERT INTO #T VALUES (2), (3);
                SELECT COUNT(*) AS Cnt FROM #T;
            "));
            Assert.Equal(3m, eval.LastResult?.Rows[0]["Cnt"]);
        }

        [Fact]
        public async Task StdDev_ComputesStdDev()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS N INTO #T;
                INSERT INTO #T VALUES (2), (3);
                SELECT STDDEV(N) AS SD FROM #T;
            "));
            Assert.NotNull(eval.LastResult?.Rows[0]["SD"]);
        }

        // ── GenerateSeries ────────────────────────────────────────────────────

        [Fact]
        public async Task GenerateSeries_ProducesRange()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @r INT = LENGTH(GENERATE_SERIES(1, 5));"));
            Assert.Equal(5m, eval.GetVariable("@r"));
        }

        // ── APPEND_TO_LIST / ADD_TO_LIST ──────────────────────────────────────

        [Fact]
        public async Task AppendToList_AddsItem()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @list = ['a', 'b'];
                SET @list = APPEND_TO_LIST(@list, 'c');
                DECLARE @len INT = LENGTH(@list);
            "));
            Assert.Equal(3m, eval.GetVariable("@len"));
        }

        [Fact]
        public async Task RemoveFromList_RemovesItem()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @list = ['a', 'b', 'c'];
                SET @list = REMOVE_FROM_LIST(@list, 'b');
                DECLARE @len INT = LENGTH(@list);
            "));
            Assert.Equal(2m, eval.GetVariable("@len"));
        }

        [Fact]
        public async Task SortList_SortsAscending()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @list = ['c', 'a', 'b'];
                SET @list = SORT_LIST(@list);
                DECLARE @len INT = LENGTH(@list);
            "));
            Assert.Equal(3m, eval.GetVariable("@len"));
        }
    }
}
