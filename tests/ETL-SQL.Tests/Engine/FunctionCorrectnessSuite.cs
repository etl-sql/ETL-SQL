using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests
{
    public class FunctionCorrectnessSuite
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task String_Len_NullHandling()
        {
            var ev = NewEvaluator();
            // Standard SQL: LEN(NULL) is NULL
            var resNull = await ev.ExecuteValue("LEN(NULL)", new Row());
            Assert.Null(resNull);

            // LEN('') should be 0 (user confirmed)
            var resEmpty = await ev.ExecuteValue("LEN('')", new Row());
            Assert.Equal(0m, Convert.ToDecimal(resEmpty));
        }

        [Fact]
        public async Task Date_DateDiff_BoundaryCrossings()
        {
            var ev = NewEvaluator();
            // DATEDIFF(day, '2024-12-31 23:59:59', '2025-01-01 00:00:01')
            // This crosses the midnight boundary, so it should be 1.
            var res = await ev.ExecuteValue("DATEDIFF(day, '2024-12-31 23:59:59', '2025-01-01 00:00:01')", new Row());
            Assert.Equal(1m, Convert.ToDecimal(res));
        }

        [Fact]
        public async Task Date_DateDiff_IntegerOnly()
        {
            var ev = NewEvaluator();
            // DATEDIFF should return an integer, not a decimal fraction (user confirmed).
            var res = await ev.ExecuteValue("DATEDIFF(day, '2024-01-01 00:00', '2024-01-02 12:00')", new Row());
            Assert.Equal(1m, Convert.ToDecimal(res));
        }

        [Fact]
        public async Task String_LeftRight_ExtremeBounds()
        {
            var ev = NewEvaluator();
            // LEFT('abc', 100) -> 'abc'
            var resLeft = await ev.ExecuteValue("LEFT('abc', 100)", new Row());
            Assert.Equal("abc", resLeft);

            // RIGHT('abc', 100) -> 'abc'
            var resRight = await ev.ExecuteValue("RIGHT('abc', 100)", new Row());
            Assert.Equal("abc", resRight);

            // LEFT('abc', -1) -> ''
            var resLeftNeg = await ev.ExecuteValue("LEFT('abc', -1)", new Row());
            Assert.Equal("", resLeftNeg);
        }

        [Fact]
        public async Task Math_Absolute_NullPropagation()
        {
            var ev = NewEvaluator();
            // ABS(NULL) -> NULL
            var res = await ev.ExecuteValue("ABS(NULL)", new Row());
            Assert.Null(res);
        }

        [Fact]
        public async Task Math_Safety_InvalidInputs()
        {
            var ev = NewEvaluator();
            // SQRT(-1) should ideally return NULL or throw gracefully. 
            // Our plan suggests returning NULL for "defensive/safety".
            var resSqrt = await ev.ExecuteValue("SQRT(-1)", new Row());
            Assert.Null(resSqrt);

            // POWER(0, -1) -> NULL (avoid infinity/exception)
            var resPower = await ev.ExecuteValue("POWER(0, -1)", new Row());
            Assert.Null(resPower);
        }

        [Fact]
        public async Task String_Substring_1Based()
        {
            var ev = NewEvaluator();
            // SUBSTRING('abc', 1, 1) -> 'a'
            var res = await ev.ExecuteValue("SUBSTRING('abc', 1, 1)", new Row());
            Assert.Equal("a", res);

            // SUBSTRING('abc', 0, 2) -> 'a' (SQL usually treats anything < 1 as 1 or offset)
            var res0 = await ev.ExecuteValue("SUBSTRING('abc', 0, 2)", new Row());
            Assert.Equal("a", res0);
        }

        [Fact]
        public async Task Regex_NullPropagation()
        {
            var ev = NewEvaluator();
            // REGEXP_LIKE(NULL, '.*') -> NULL
            var res = await ev.ExecuteValue("REGEXP_LIKE(NULL, '.*')", new Row());
            Assert.Null(res);

            // REGEXP_INSTR('abc', NULL) -> NULL
            var resInstr = await ev.ExecuteValue("REGEXP_INSTR('abc', NULL)", new Row());
            Assert.Null(resInstr);
        }

        [Fact]
        public async Task Json_NullPropagation()
        {
            var ev = NewEvaluator();
            // ISJSON(NULL) -> NULL
            var resIs = await ev.ExecuteValue("ISJSON(NULL)", new Row());
            Assert.Null(resIs);

            // JSON_EXISTS(NULL, '$.a') -> NULL
            var resExists = await ev.ExecuteValue("JSON_EXISTS(NULL, '$.a')", new Row());
            Assert.Null(resExists);
        }

        [Fact]
        public async Task Xml_NullPropagation()
        {
            var ev = NewEvaluator();
            // XMLEXISTS(NULL, '/root') -> NULL
            var res = await ev.ExecuteValue("XMLEXISTS(NULL, '/root')", new Row());
            Assert.Null(res);
        }

        [Fact]
        public async Task File_NullPropagation()
        {
            var ev = NewEvaluator();
            // FILE_EXISTS(NULL) -> NULL
            var res = await ev.ExecuteValue("FILE_EXISTS(NULL)", new Row());
            Assert.Null(res);
        }
    }
}
