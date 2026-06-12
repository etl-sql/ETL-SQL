using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Services;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class SubqueryAnalyzerTests
    {
        private readonly SubqueryAnalyzer _analyzer = new();

        private SelectStatement Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var parser = new Parser(lexer.Tokenize(), sql);
            var stmt = parser.ParseStatement();
            return (SelectStatement)stmt;
        }

        [Fact]
        public void DetectsQualifiedOuterReference()
        {
            var sql = "SELECT * FROM local_table WHERE col = outer_table.id";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);

            // Only qualified identifiers (table.column form) with non-local qualifiers are outer refs.
            // Unqualified identifiers resolve against the current row and are never treated as outer refs.
            Assert.Contains("outer_table.id", refs);
            Assert.Single(refs);
        }

        [Fact]
        public void IgnoresLocalAlias()
        {
            var sql = "SELECT * FROM local_table AS l WHERE col = l.id";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);

            // l.id is ignored because l is a local table alias.
            // Unqualified col is not an outer ref.
            Assert.Empty(refs);
        }

        [Fact]
        public void DetectsUnqualifiedOuterReference()
        {
            // Unqualified identifiers cannot be statically classified as outer refs —
            // they resolve against the current row at runtime, never the outer row stack.
            var sql = "SELECT * FROM local_table WHERE col = outer_col";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);

            Assert.Empty(refs);
        }

        [Fact]
        public void DetectsCorrelationInJoins()
        {
            var sql = "SELECT * FROM local_table JOIN other_local ON local_table.id = other_local.id WHERE other_local.val = outer_table.val";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);

            Assert.Contains("outer_table.val", refs);
            // local_table.id and other_local.id are qualified with local aliases — not outer refs
            Assert.Single(refs);
        }

        [Fact]
        public void HandlesComplexExpressions()
        {
            var sql = "SELECT col FROM t WHERE y > o_y";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);

            // All identifiers here are unqualified — none are outer refs
            Assert.Empty(refs);
        }
    }
}
