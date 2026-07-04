using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    /// <summary>
    /// The -> and ->> JSON access operators (PostgreSQL/MySQL/SQLite style), lowered at parse time
    /// to JSON_GET / JSON_GET_TEXT: -> returns the field/element as JSON (chainable, strings keep
    /// quotes), ->> returns it as text. Null-propagating on missing keys and invalid JSON.
    /// </summary>
    public class JsonAccessOperatorTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task ArrowText_ExtractsUnquotedString()
        {
            var ev = NewEvaluator();
            var res = await ev.ExecuteValue("'{\"name\":\"Alice\"}' ->> 'name'", new Row());
            Assert.Equal("Alice", res?.ToString());
        }

        [Fact]
        public async Task Arrow_ReturnsJson_StringsKeepQuotes()
        {
            var ev = NewEvaluator();
            var res = await ev.ExecuteValue("'{\"name\":\"Alice\"}' -> 'name'", new Row());
            Assert.Equal("\"Alice\"", res?.ToString());
        }

        [Fact]
        public async Task Chaining_WalksNestedObjects()
        {
            var ev = NewEvaluator();
            var res = await ev.ExecuteValue("'{\"customer\":{\"address\":{\"city\":\"Reno\"}}}' -> 'customer' -> 'address' ->> 'city'", new Row());
            Assert.Equal("Reno", res?.ToString());
        }

        [Fact]
        public async Task IntegerIndex_SelectsArrayElement_NegativeCountsFromEnd()
        {
            var ev = NewEvaluator();
            Assert.Equal("20", (await ev.ExecuteValue("'[10,20,30]' ->> 1", new Row()))?.ToString());
            Assert.Equal("30", (await ev.ExecuteValue("'[10,20,30]' ->> -1", new Row()))?.ToString());
        }

        [Fact]
        public async Task MissingKey_OutOfRange_InvalidJson_AllYieldNull()
        {
            var ev = NewEvaluator();
            Assert.Null(await ev.ExecuteValue("'{\"a\":1}' -> 'missing'", new Row()));
            Assert.Null(await ev.ExecuteValue("'[1,2]' -> 5", new Row()));
            Assert.Null(await ev.ExecuteValue("'not json' -> 'a'", new Row()));
        }

        [Fact]
        public async Task ObjectAndArrayValues_ComeBackAsRawJson_ForBothOperators()
        {
            var ev = NewEvaluator();
            Assert.Equal("{\"b\":1}", (await ev.ExecuteValue("'{\"a\":{\"b\":1}}' -> 'a'", new Row()))?.ToString());
            Assert.Equal("{\"b\":1}", (await ev.ExecuteValue("'{\"a\":{\"b\":1}}' ->> 'a'", new Row()))?.ToString());
        }

        [Fact]
        public async Task Subtraction_StillLexesNormally_ArrowNeedsAdjacency()
        {
            var ev = NewEvaluator();
            // '- >' with the tokens apart is still subtraction-then-comparison territory;
            // plain arithmetic must be untouched by the new lexing.
            Assert.Equal(2m, Convert.ToDecimal(await ev.ExecuteValue("5 - 3", new Row())));
            Assert.Equal(true, await ev.ExecuteValue("5 - 3 > 1", new Row()));
        }

        [Fact]
        public async Task OverRows_WithCoalesceFallback()
        {
            var ev = NewEvaluator();
            await TestHelpers.Execute(ev, @"
CREATE TABLE #t (doc STRING);
INSERT INTO #t VALUES ('{""qty"": 3}');
INSERT INTO #t VALUES ('{}');
SELECT doc ->> 'qty' ?? '0' AS qty INTO #out FROM #t;");

            var res = await ev.ExecuteQuery(TestHelpers.Parse("SELECT qty FROM #out ORDER BY qty;").Statements[0]).FirstAsync();
            Assert.Equal(new[] { "0", "3" }, res.Rows.Select(r => r["qty"]?.ToString()).ToArray());
        }
    }
}
