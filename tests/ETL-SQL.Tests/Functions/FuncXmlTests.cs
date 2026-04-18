using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Functions
{
    public class XmlFunctionTests
    {
        // Single-quoted XML attributes so the string embeds cleanly in SQL string literals
        private const string BookCatalog =
            "<catalog>" +
            "<book id=\"1\"><title>SQL Mastery</title><price>29.99</price></book>" +
            "<book id=\"2\"><title>ETL Design</title><price>39.99</price></book>" +
            "</catalog>";

        private const string SimpleXml = "<root><name>Alice</name><age>30</age></root>";

        private static Evaluator Build() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        // ── XMLVALUE ──────────────────────────────────────────────────────────

        [Fact]
        public async Task XMLVALUE_SimpleElement()
        {
            var ev = Build();
            var result = await EvalExpr(ev, $"XMLVALUE('{SimpleXml}', '/root/name')");
            Assert.Equal("Alice", result?.ToString());
        }

        [Fact]
        public async Task XMLVALUE_MissingPath_ReturnsNull()
        {
            var ev = Build();
            var result = await EvalExpr(ev, $"XMLVALUE('{SimpleXml}', '/root/missing')");
            Assert.Null(result);
        }

        [Fact]
        public async Task XMLVALUE_Attribute_ViaXPath()
        {
            var ev = Build();
            var xml = "<root><item id=\"42\"/></root>";
            var result = await EvalExpr(ev, $"XMLVALUE('{xml}', '/root/item/@id')");
            Assert.Equal("42", result?.ToString());
        }

        // ── XMLEXISTS ─────────────────────────────────────────────────────────

        [Fact]
        public async Task XMLEXISTS_PresentPath_Returns1()
        {
            var ev = Build();
            var result = await EvalExpr(ev, $"XMLEXISTS('{SimpleXml}', '/root/name')");
            Assert.Equal(1m, result);
        }

        [Fact]
        public async Task XMLEXISTS_MissingPath_Returns0()
        {
            var ev = Build();
            var result = await EvalExpr(ev, $"XMLEXISTS('{SimpleXml}', '/root/missing')");
            Assert.Equal(0m, result);
        }

        // ── XMLQUERY ─────────────────────────────────────────────────────────

        [Fact]
        public async Task XMLQUERY_ReturnsFragment()
        {
            var ev = Build();
            var result = await EvalExpr(ev, $"XMLQUERY('{SimpleXml}', '/root/name')");
            Assert.NotNull(result);
            Assert.Contains("Alice", result!.ToString());
        }

        // ── XMLELEMENT ────────────────────────────────────────────────────────

        [Fact]
        public async Task XMLELEMENT_ProducesElement()
        {
            var ev = Build();
            var result = await EvalExpr(ev, "XMLELEMENT('city', 'Seattle')");
            Assert.NotNull(result);
            Assert.Contains("<city>Seattle</city>", result!.ToString());
        }

        // ── XMLFOREST ─────────────────────────────────────────────────────────

        [Fact]
        public async Task XMLFOREST_ProducesMultipleElements()
        {
            var ev = Build();
            var result = await EvalExpr(ev, "XMLFOREST('first', 'Alice', 'last', 'Smith')");
            Assert.NotNull(result);
            Assert.Contains("<first>Alice</first>", result!.ToString());
            Assert.Contains("<last>Smith</last>", result!.ToString());
        }

        // ── XMLTABLE (table-valued) ───────────────────────────────────────────

        [Fact]
        public async Task XMLTABLE_ReturnsRows()
        {
            var ev = Build();
            var script = $"SELECT * FROM XMLTABLE('{BookCatalog}', '/catalog/book')";
            var rows = await CollectRows(ev, script);
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public async Task XMLTABLE_ColumnsFromChildElements()
        {
            var ev = Build();
            var script = $"SELECT * FROM XMLTABLE('{BookCatalog}', '/catalog/book')";
            var rows = await CollectRows(ev, script);
            Assert.Equal("SQL Mastery", rows[0]["title"]?.ToString());
            Assert.Equal("39.99", rows[1]["price"]?.ToString());
        }

        [Fact]
        public async Task XMLTABLE_SimpleLeafNodes()
        {
            var ev = Build();
            var xml = "<items><item>A</item><item>B</item><item>C</item></items>";
            var script = $"SELECT * FROM XMLTABLE('{xml}', '/items/item')";
            var rows = await CollectRows(ev, script);
            Assert.Equal(3, rows.Count);
            Assert.Equal("A", rows[0]["VALUE"]?.ToString());
            Assert.Equal("C", rows[2]["VALUE"]?.ToString());
        }

        // ── EXTRACTVALUE alias ────────────────────────────────────────────────

        [Fact]
        public async Task EXTRACTVALUE_WorksLikeXmlValue()
        {
            var ev = Build();
            var result = await EvalExpr(ev, $"EXTRACTVALUE('{SimpleXml}', '/root/age')");
            Assert.Equal("30", result?.ToString());
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
