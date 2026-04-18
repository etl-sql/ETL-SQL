using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Reporting
{
    public class ReportSqlTests
    {
        [Fact]
        public void TestCreateVisual_SubtitleAndSourceNoEquals()
        {
            var sql = @"
                CREATE VISUAL SalesChart AS BAR (
                    TITLE 'Global Sales Report',
                    SUBTITLE 'Q1 2026',
                    SOURCE SELECT Region, Amount FROM #SalesData
                );
            ";

            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (CreateVisualStatement)parser.ParseStatement();

            Assert.Equal("SalesChart", stmt.Name);
            Assert.Equal(VisualType.Bar, stmt.VisualType);
            Assert.Equal("Global Sales Report", stmt.Title);
            Assert.Equal("Q1 2026", stmt.Subtitle);
            Assert.NotNull(stmt.Source.InlineSelect);
        }

        [Fact]
        public void TestCreateVisual_SourceParenthesesNoEquals()
        {
            var sql = @"
                CREATE VISUAL SalesChart AS BAR (
                    TITLE = 'Global Sales Report',
                    SOURCE (
                        SELECT Region, SUM(Amount) AS Total FROM #SalesData GROUP BY Region
                    )
                );
            ";

            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (CreateVisualStatement)parser.ParseStatement();

            Assert.Equal("SalesChart", stmt.Name);
            Assert.Equal("Global Sales Report", stmt.Title);
            Assert.NotNull(stmt.Source.InlineSelect);
            Assert.Equal("Total", stmt.Source.InlineSelect.Columns[1].Alias);
        }
        
        [Fact]
        public void TestExplainInto_Serialization()
        {
            var sql = "EXPLAIN SELECT * FROM #T INTO #Plan;";
            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (ExplainStatement)parser.ParseStatement();
            
            Assert.NotNull(stmt.IntoTable);
            Assert.Equal("#Plan", stmt.IntoTable.TableName);
            
            var serialized = stmt.ToSql();
            Assert.Contains("INTO #Plan", serialized);
        }
    }
}
