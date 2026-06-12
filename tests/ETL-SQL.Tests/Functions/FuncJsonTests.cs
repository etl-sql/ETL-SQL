using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    public class JsonFunctionTests
    {
        private static Evaluator Build() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        // ── ISJSON ────────────────────────────────────────────────────────────

        [Fact]
        public async Task ISJSON_ValidJson_Returns1()
        {
            var ev = Build();
            await AssertEval(ev, "ISJSON('{\"a\":1}')", 1m);
        }

        [Fact]
        public async Task ISJSON_InvalidJson_Returns0()
        {
            var ev = Build();
            await AssertEval(ev, "ISJSON('not json')", 0m);
        }

        // ── JSON_VALUE ────────────────────────────────────────────────────────

        [Fact]
        public async Task JSON_VALUE_SimpleKey()
        {
            var ev = Build();
            await AssertEval(ev, "JSON_VALUE('{\"name\":\"Alice\"}', '$.name')", "Alice");
        }

        [Fact]
        public async Task JSON_VALUE_NestedKey()
        {
            var ev = Build();
            await AssertEval(ev, "JSON_VALUE('{\"a\":{\"b\":42}}', '$.a.b')", 42m);
        }

        [Fact]
        public async Task JSON_VALUE_ArrayIndex()
        {
            var ev = Build();
            await AssertEval(ev, "JSON_VALUE('[10,20,30]', '$[1]')", 20m);
        }

        [Fact]
        public async Task JSON_VALUE_MissingKey_ReturnsNull()
        {
            var ev = Build();
            await AssertEval(ev, "JSON_VALUE('{\"a\":1}', '$.z')", null);
        }

        [Fact]
        public async Task JSON_VALUE_ObjectPath_ReturnsNull()
        {
            // JSON_VALUE returns null for objects (use JSON_QUERY for those)
            var ev = Build();
            await AssertEval(ev, "JSON_VALUE('{\"a\":{\"b\":1}}', '$.a')", null);
        }

        // ── JSON_QUERY ────────────────────────────────────────────────────────

        [Fact]
        public async Task JSON_QUERY_ReturnsObject()
        {
            var ev = Build();
            var result = await EvalExpr(ev, "JSON_QUERY('{\"a\":{\"b\":1}}', '$.a')");
            Assert.NotNull(result);
            Assert.Contains("\"b\"", result!.ToString());
        }

        [Fact]
        public async Task JSON_QUERY_ReturnsArray()
        {
            var ev = Build();
            var result = await EvalExpr(ev, "JSON_QUERY('{\"items\":[1,2,3]}', '$.items')");
            Assert.NotNull(result);
            Assert.Contains("1", result!.ToString());
        }

        // ── JSON_EXISTS ───────────────────────────────────────────────────────

        [Fact]
        public async Task JSON_EXISTS_PresentPath_Returns1()
        {
            var ev = Build();
            await AssertEval(ev, "JSON_EXISTS('{\"a\":1}', '$.a')", 1m);
        }

        [Fact]
        public async Task JSON_EXISTS_MissingPath_Returns0()
        {
            var ev = Build();
            await AssertEval(ev, "JSON_EXISTS('{\"a\":1}', '$.z')", 0m);
        }

        // ── JSON_OBJECT / JSON_ARRAY ──────────────────────────────────────────

        [Fact]
        public async Task JSON_OBJECT_BuildsObject()
        {
            var ev = Build();
            var result = await EvalExpr(ev, "JSON_OBJECT('name', 'Alice', 'age', 30)");
            Assert.NotNull(result);
            Assert.Contains("\"name\"", result!.ToString());
            Assert.Contains("Alice", result.ToString()!);
        }

        [Fact]
        public async Task JSON_ARRAY_BuildsArray()
        {
            var ev = Build();
            var result = await EvalExpr(ev, "JSON_ARRAY(1, 'two', 3)");
            Assert.NotNull(result);
            Assert.Contains("two", result!.ToString());
        }

        // ── JSON_MODIFY ───────────────────────────────────────────────────────

        [Fact]
        public async Task JSON_MODIFY_UpdatesValue()
        {
            var ev = Build();
            var result = await EvalExpr(ev, "JSON_MODIFY('{\"a\":1}', '$.a', 99)");
            Assert.NotNull(result);
            Assert.Contains("99", result!.ToString());
        }

        // ── JSON_TABLE (table-valued) ─────────────────────────────────────────

        [Fact]
        public async Task JSON_TABLE_ScalarArray()
        {
            var ev = Build();
            var script = "SELECT * FROM JSON_TABLE('[\"A\",\"B\",\"C\"]', '$')";
            var rows = await CollectRows(ev, script);
            Assert.Equal(3, rows.Count);
            Assert.Equal("A", rows[0]["VALUE"]?.ToString());
            Assert.Equal("C", rows[2]["VALUE"]?.ToString());
        }

        [Fact]
        public async Task JSON_TABLE_ObjectArray()
        {
            var ev = Build();
            var script = "SELECT * FROM JSON_TABLE('{\"items\":[{\"id\":1,\"name\":\"X\"},{\"id\":2,\"name\":\"Y\"}]}', '$.items')";
            var rows = await CollectRows(ev, script);
            Assert.Equal(2, rows.Count);
            Assert.Equal(1m, Convert.ToDecimal(rows[0]["id"]));
            Assert.Equal("Y", rows[1]["name"]?.ToString());
        }

        [Fact]
        public async Task JSON_TABLE_NestedPath()
        {
            var ev = Build();
            var script = "SELECT * FROM JSON_TABLE('{\"data\":{\"rows\":[10,20,30]}}', '$.data.rows')";
            var rows = await CollectRows(ev, script);
            Assert.Equal(3, rows.Count);
            Assert.Equal(20m, Convert.ToDecimal(rows[1]["VALUE"]));
        }

        [Fact]
        public async Task JSON_TABLE_ColumnsClause_ProjectsTypedColumns()
        {
            var ev = Build();
            var script = @"
                SELECT ord, id, name
                FROM JSON_TABLE('{""items"":[{""id"":1,""name"":""A""},{""id"":2,""name"":""B""}]}', '$.items[*]' COLUMNS (
                    ord FOR ORDINALITY,
                    id INT PATH '$.id',
                    name STRING PATH '$.name'
                ));";

            var rows = await CollectRows(ev, script);

            Assert.Equal(2, rows.Count);
            Assert.Equal(1m, Convert.ToDecimal(rows[0]["ord"]));
            Assert.Equal(2m, Convert.ToDecimal(rows[1]["id"]));
            Assert.Equal("B", rows[1]["name"]?.ToString());
        }

        [Fact]
        public async Task JSON_TABLE_ColumnsClause_AppliesDefaultsAndExistsPath()
        {
            var ev = Build();
            var script = @"
                SELECT sku, discount_present, qty
                FROM JSON_TABLE('{""items"":[{""sku"":""A"",""qty"":3},{""sku"":""B"",""discount"":true}]}', '$.items[*]' COLUMNS (
                    sku STRING PATH '$.sku',
                    discount_present EXISTS PATH '$.discount',
                    qty INT PATH '$.qty' DEFAULT 0 ON EMPTY
                ));";

            var rows = await CollectRows(ev, script);

            Assert.Equal(2, rows.Count);
            Assert.Equal(0m, Convert.ToDecimal(rows[0]["discount_present"]));
            Assert.Equal(1m, Convert.ToDecimal(rows[1]["discount_present"]));
            Assert.Equal(0m, Convert.ToDecimal(rows[1]["qty"]));
        }

        // ── OPENJSON ──────────────────────────────────────────────────────────

        [Fact]
        public async Task OPENJSON_ObjectReturnsKeyValueType()
        {
            var ev = Build();
            var script = "SELECT * FROM OPENJSON('{\"name\":\"Alice\",\"age\":30}')";
            var rows = await CollectRows(ev, script);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r["KEY"]?.ToString() == "name" && r["VALUE"]?.ToString() == "Alice");
            Assert.Contains(rows, r => r["KEY"]?.ToString() == "age");
        }

        [Fact]
        public async Task OPENJSON_ArrayOfObjects_ReturnsColumns()
        {
            var ev = Build();
            var script = "SELECT * FROM OPENJSON('[{\"id\":1},{\"id\":2}]')";
            var rows = await CollectRows(ev, script);
            Assert.Equal(2, rows.Count);
            Assert.Equal(1m, Convert.ToDecimal(rows[0]["id"]));
        }

        // ── JSON_EXTRACT alias ────────────────────────────────────────────────

        [Fact]
        public async Task JSON_EXTRACT_WorksLikeJsonValue()
        {
            var ev = Build();
            await AssertEval(ev, "JSON_EXTRACT('{\"x\":7}', '$.x')", 7m);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static async Task AssertEval(Evaluator ev, string expression, object? expected)
        {
            var result = await EvalExpr(ev, expression);
            if (result is int i) result = (decimal)i;
            if (expected is int ei) expected = (decimal)ei;
            Assert.Equal(expected, result);
        }

        private static async Task<object?> EvalExpr(Evaluator ev, string expression)
        {
            var script = $"SELECT {expression} AS _RESULT";
            var batches = ev.ExecuteQuery(new Parser(new Lexer(script).Tokenize()).Parse().Statements[0]);
            await foreach (var batch in batches)
                if (batch.Rows.Count > 0) return batch.Rows[0]["_RESULT"];
            return null;
        }

        private static async Task<List<Row>> CollectRows(Evaluator ev, string script)
        {
            var rows = new List<Row>();
            var stmt = new Parser(new Lexer(script).Tokenize()).Parse().Statements[0];
            await foreach (var batch in ev.ExecuteQuery(stmt))
                rows.AddRange(batch.Rows);
            return rows;
        }
    }
}
