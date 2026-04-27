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
            var inlineSelect = Assert.IsType<SelectStatement>(stmt.Source.InlineSelect);
            Assert.Equal("Total", inlineSelect.Columns[1].Alias);
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

        [Fact]
        public void CreateTheme_ParsesNameAndProperties()
        {
            var sql = @"
                CREATE THEME corporate AS (
                    BACKGROUND   = '#1a1a2e',
                    TEXT_COLOR   = '#eee',
                    ACCENT_COLOR = '#4ecca3',
                    COLORS       = '#4ecca3, #e94560, #f5a623'
                );
            ";
            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (CreateThemeStatement)parser.ParseStatement();

            Assert.Equal("corporate", stmt.Name);
            Assert.Equal(ObjectCreationMode.Create, stmt.Mode);
            Assert.Equal("#1a1a2e", stmt.Properties["BACKGROUND"]);
            Assert.Equal("#eee",    stmt.Properties["TEXT_COLOR"]);
            Assert.Equal("#4ecca3", stmt.Properties["ACCENT_COLOR"]);
        }

        [Fact]
        public void CreateTheme_BuildEChartsTheme_MapsProperties()
        {
            var props = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BACKGROUND"]   = "#1a1a2e",
                ["TEXT_COLOR"]   = "#eeeeee",
                ["ACCENT_COLOR"] = "#4ecca3",
                ["GRID_COLOR"]   = "#333333"
            };

            var json = ETL_SQL.Engine.Handlers.CreateThemeStatementHandler.BuildEChartsTheme(props);

            Assert.Equal("#1a1a2e", json["backgroundColor"]?.GetValue<string>());
            Assert.Equal("#eeeeee", json["textStyle"]?["color"]?.GetValue<string>());
            Assert.Equal("#4ecca3", json["color"]?[0]?.GetValue<string>());
            // Axis objects should be present
            Assert.NotNull(json["categoryAxis"]);
        }

        [Fact]
        public void DropTheme_ParsesNameAndObjectType()
        {
            var sql = "DROP THEME corporate;";
            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (DropReportObjectStatement)parser.ParseStatement();

            Assert.Equal(ReportObjectType.Theme, stmt.ObjectType);
            Assert.Equal("corporate", stmt.Name);
            Assert.False(stmt.IfExists);
        }
    }
}
