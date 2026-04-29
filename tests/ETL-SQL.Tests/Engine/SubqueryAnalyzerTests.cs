using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Services;
using Xunit;
using System.Collections.Generic;

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
            
            Assert.Contains("outer_table.id", refs);
            Assert.Contains("col", refs); // col is unqualified and not a local table name, so it is treated as a potential outer ref
            Assert.Equal(2, refs.Count);
        }

        [Fact]
        public void IgnoresLocalAlias()
        {
            var sql = "SELECT * FROM local_table AS l WHERE col = l.id";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);
            
            // l.id is ignored because l is a local table alias
            // col is detected as potential outer ref
            Assert.Contains("col", refs);
            Assert.Single(refs);
        }

        [Fact]
        public void DetectsUnqualifiedOuterReference()
        {
            // Statically, if it's not a local table alias, we treat it as a potential outer ref.
            var sql = "SELECT * FROM local_table WHERE col = outer_col";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);
            
            Assert.Contains("outer_col", refs);
            Assert.Contains("col", refs);
            Assert.Equal(2, refs.Count);
        }
        
        [Fact]
        public void DetectsCorrelationInJoins()
        {
            var sql = "SELECT * FROM local_table JOIN other_local ON local_table.id = other_local.id WHERE other_local.val = outer_table.val";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);
            
            Assert.Contains("outer_table.val", refs);
            // We don't care about local_table.id or other_local.id because they are qualified with local aliases
            Assert.Single(refs);
        }

        [Fact]
        public void HandlesComplexExpressions()
        {
            var sql = "SELECT col FROM t WHERE y > o_y";
            var stmt = Parse(sql);
            var refs = _analyzer.GetOuterReferences(stmt);
            
            Assert.Contains("col", refs);
            Assert.Contains("y", refs);
            Assert.Contains("o_y", refs);
            Assert.Equal(3, refs.Count);
        }
    }
}
