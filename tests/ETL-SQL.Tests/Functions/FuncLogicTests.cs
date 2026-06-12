using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    public class LogicCorrectnessSuite
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task Logic_OperatorPrecedence_AndBeforeOr()
        {
            var ev = NewEvaluator();
            // TRUE OR FALSE AND FALSE 
            // If AND is higher: TRUE OR (FALSE) = TRUE
            // If Left-to-right: (TRUE OR FALSE) AND FALSE = FALSE

            var res = await ev.ExecuteValue("TRUE OR FALSE AND FALSE", new Row());
            Assert.Equal(true, res);
        }

        [Fact]
        public async Task Arithmetic_Precedence_MulBeforeAdd()
        {
            var ev = NewEvaluator();
            // 2 + 3 * 4 = 14 (not 20)
            var res = await ev.ExecuteValue("2 + 3 * 4", new Row());
            Assert.Equal(14m, Convert.ToDecimal(res));
        }

        [Fact]
        public async Task Logic_Ternary_NotUnknownIsUnknown()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (v INT);");
            await TestHelpers.Execute(ev, "INSERT INTO #t VALUES (NULL);");

            // In SQL 3VL: NOT UNKNOWN is UNKNOWN. 
            // WHERE NOT (v = 1) where v is NULL => NOT (UNKNOWN) => UNKNOWN => NO ROW
            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT * FROM #t WHERE NOT (v = 1);").Statements[0]).FirstAsync();
            Assert.Empty(res.Rows);
        }

        [Fact]
        public async Task Logic_DeMorgan_WithNulls()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, "CREATE TABLE #t (a INT, b INT);");
            await TestHelpers.Execute(ev, "INSERT INTO #t VALUES (1, NULL);");

            // NOT (a=1 OR b=1)  vs  (NOT a=1) AND (NOT b=1)
            // a=1 is TRUE, b=1 is UNKNOWN. 
            // TRUE OR UNKNOWN is TRUE. NOT TRUE is FALSE.
            var res1 = await ev.ExecuteQuery(TestHelpers.Parse("SELECT * FROM #t WHERE NOT (a=1 OR b=1);").Statements[0]).FirstAsync();
            Assert.Empty(res1.Rows);

            // (NOT a=1) is FALSE. FALSE AND (anything) is FALSE.
            var res2 = await ev.ExecuteQuery(TestHelpers.Parse("SELECT * FROM #t WHERE (NOT a=1) AND (NOT b=1);").Statements[0]).FirstAsync();
            Assert.Empty(res2.Rows);
        }

        [Fact]
        public async Task Coercion_StringAddition_PromotesToNumeric()
        {
            var ev = NewEvaluator();
            // '100' + 50 should be 150
            var res = await ev.ExecuteValue("'100' + 50", new Row());
            Assert.Equal(150m, Convert.ToDecimal(res));
        }

        [Fact]
        public async Task Logic_ShortCircuit_Or()
        {
            var ev = NewEvaluator();
            // TRUE OR (1/0 = 1) should be TRUE and NOT error
            var res = await ev.ExecuteValue("TRUE OR 1/0 = 1", new Row());
            Assert.Equal(true, res);
        }

        [Fact]
        public async Task Logic_ShortCircuit_And()
        {
            var ev = NewEvaluator();
            // FALSE AND (1/0 = 1) should be FALSE and NOT error
            var res = await ev.ExecuteValue("FALSE AND 1/0 = 1", new Row());
            Assert.Equal(false, res);
        }

        [Fact]
        public async Task Logic_3VL_And()
        {
            var ev = NewEvaluator();
            // NULL AND TRUE = NULL
            var res1 = await ev.ExecuteValue("NULL AND TRUE", new Row());
            Assert.Null(res1);

            // NULL AND FALSE = FALSE
            var res2 = await ev.ExecuteValue("NULL AND FALSE", new Row());
            Assert.Equal(false, res2);
        }

        [Fact]
        public async Task Logic_3VL_Or()
        {
            var ev = NewEvaluator();
            // NULL OR TRUE = TRUE
            var res1 = await ev.ExecuteValue("NULL OR TRUE", new Row());
            Assert.Equal(true, res1);

            // NULL OR FALSE = NULL
            var res2 = await ev.ExecuteValue("NULL OR FALSE", new Row());
            Assert.Null(res2);

            // NULL OR NULL = NULL
            var res3 = await ev.ExecuteValue("NULL OR NULL", new Row());
            Assert.Null(res3);
        }
    }
}
