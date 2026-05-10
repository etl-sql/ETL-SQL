using System;
using System.Linq;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Core.Parsing
{
    /// <summary>
    /// Phase 9A: Parser tests for CREATE VISUAL, CREATE PAGE, CREATE DATASET.
    /// </summary>
    public class ReportSqlParserTests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        // ── CREATE VISUAL ────────────────────────────────────────────────────

        [Fact]
        public void ParseCreateVisual_InlineSelect_ReturnsCreateVisualStatement()
        {
            var sql = @"
CREATE VISUAL SalesChart AS BAR (
    SOURCE = (SELECT Month, Revenue FROM #sales),
    MAPPINGS (x = Month, y = Revenue),
    OPTIONS (title = 'Monthly Revenue')
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("SalesChart", stmt!.Name);
            Assert.Equal(VisualType.Bar, stmt.VisualType);
            Assert.True(stmt.Source.IsInlineSelect);
            Assert.Equal(2, stmt.Mappings.Count);
            Assert.Equal("X", stmt.Mappings[0].Role);
            Assert.Equal("Month", stmt.Mappings[0].Column);
            Assert.Single(stmt.Options);
            Assert.Equal("TITLE", stmt.Options[0].Key);
        }

        [Fact]
        public void ParseCreateVisual_TempTableSource_StoresTempTableName()
        {
            var sql = @"
CREATE VISUAL PieChart AS PIE (
    SOURCE = #region_summary,
    MAPPINGS (label = Region, value = Sales)
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.False(stmt!.Source.IsInlineSelect);
            Assert.Equal("#region_summary", stmt.Source.TempTableName);
        }

        [Fact]
        public void ParseCreateVisual_WithAxisOptions_ParsesAxisBlocks()
        {
            var sql = @"
CREATE VISUAL LineChart AS LINE (
    SOURCE = #data,
    OPTIONS (
        X_AXIS (scale = linear),
        Y_AXIS (scale = logarithmic)
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal(2, stmt!.AxisOptions.Count);
            Assert.Equal("X", stmt.AxisOptions[0].Axis);
            Assert.Equal("Y", stmt.AxisOptions[1].Axis);
        }

        [Theory]
        [InlineData("BAR",     VisualType.Bar)]
        [InlineData("LINE",    VisualType.Line)]
        [InlineData("SCATTER", VisualType.Scatter)]
        [InlineData("PIE",     VisualType.Pie)]
        [InlineData("CARD",    VisualType.Card)]
        [InlineData("SLICER",  VisualType.Slicer)]
        public void ParseCreateVisual_AllVisualTypes_ParseCorrectly(string typeName, VisualType expected)
        {
            var sql = $"CREATE VISUAL V AS {typeName} (SOURCE = #t);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal(expected, stmt!.VisualType);
        }

        [Fact]
        public void ParseCreateVisual_WithActions_ParsesDrillDown()
        {
            var sql = @"
CREATE VISUAL Chart1 AS BAR (
    SOURCE = #data,
    ACTIONS (
        ON_CLICK = DRILL_DOWN(Target = Chart2, Key = Category)
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Single(stmt!.Actions);
            var action = stmt.Actions[0] as DrillDownAction;
            Assert.NotNull(action);
            Assert.Equal("ON_CLICK", action!.Trigger);
            Assert.Equal("Chart2", action.TargetVisual);
            Assert.Equal(new[] { "Category" }, action.KeyColumns);
        }

        [Fact]
        public void ParseCreateVisual_WithActions_ParsesSetParameter()
        {
            var sql = @"
CREATE VISUAL Slicer1 AS SLICER (
    SOURCE = #regions,
    ACTIONS (
        ON_CHANGE = SET_PARAMETER(@region, Region)
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            var action = stmt!.Actions[0] as SetParameterAction;
            Assert.NotNull(action);
            Assert.Equal("@region", action!.ParameterName);
            Assert.Equal("Region", action.ValueExpression);
        }

        // ── CREATE PAGE ───────────────────────────────────────────────────────

        [Fact]
        public void ParseCreatePage_BasicLayout_ReturnsCreatePageStatement()
        {
            var sql = @"
CREATE PAGE Dashboard AS LAYOUT (
    STRUCTURE = 'AB/CC',
    MAP ('A' = SalesChart, 'B' = PieChart, 'C' = SummaryTable)
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreatePageStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("Dashboard", stmt!.Name);
            Assert.Equal("AB/CC", stmt.Structure);
            Assert.Equal(3, stmt.SlotMap.Count);
            Assert.Equal("SalesChart", stmt.SlotMap["A"]);
        }

        [Fact]
        public void ParseCreatePage_Minimal_ParsesCorrectly()
        {
            var sql = @"
CREATE PAGE SimplePage AS LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = Chart1)
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreatePageStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("A", stmt!.Structure);
        }

        // ── CREATE DATASET ────────────────────────────────────────────────────

        [Fact]
        public void ParseCreateDataset_AllOptions_ReturnsCreateDatasetStatement()
        {
            var sql = @"
CREATE DATASET #daily_sales
    REFRESH EVERY '1h'
    TTL = '24h'
    COMPRESS = ON
    ENCRYPT = ON
    KEYFILE = '/keys/sales.key'
AS (SELECT Date, SUM(Amount) AS Total FROM orders GROUP BY Date);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateDatasetStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("#daily_sales", stmt!.TempTableName);
            Assert.Equal("1h", stmt.RefreshInterval);
            Assert.Equal("24h", stmt.Ttl);
            Assert.True(stmt.Compress);
            Assert.Equal(DatasetEncryptionMode.MachineBound, stmt.EncryptionMode);
            Assert.Equal("/keys/sales.key", stmt.KeyFile);
            Assert.NotNull(stmt.SourceQuery);
        }

        [Fact]
        public void ParseCreateDataset_Minimal_OnlyRequiresNameAndQuery()
        {
            var sql = @"CREATE DATASET #summary AS (SELECT 1 AS Val FROM orders);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateDatasetStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("#summary", stmt!.TempTableName);
            Assert.Null(stmt.RefreshInterval);
            Assert.False(stmt.Compress);
            Assert.Equal(DatasetEncryptionMode.None, stmt.EncryptionMode);
        }
        [Fact]
        public void ParseCreateVisual_WithActions_ParsesDrillIn()
        {
            var sql = @"
CREATE VISUAL SalesChart AS BAR (
    SOURCE = #data,
    ACTIONS (
        ON_CLICK = DRILL_IN(HIERARCHY = (Year, Quarter, Month))
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Single(stmt!.Actions);
            var action = stmt.Actions[0] as DrillInAction;
            Assert.NotNull(action);
            Assert.Equal("ON_CLICK", action!.Trigger);
            Assert.Equal(new[] { "Year", "Quarter", "Month" }, action.Hierarchy);
        }

        [Fact]
        public void ParseSetReport_TitleAndDescription_ReturnsMetadataStatements()
        {
            var sql = @"
SET REPORT TITLE = 'Sales Dashboard';
SET REPORT DESCRIPTION = 'Regional and product-level revenue by month.';";
            var script = Parse(sql);
            
            var statements = script.Statements.OfType<SetReportMetadataStatement>().ToList();
            Assert.Equal(2, statements.Count);
            
            Assert.Equal("TITLE", statements[0].Key);
            Assert.Equal("Sales Dashboard", statements[0].Value);
            
            Assert.Equal("DESCRIPTION", statements[1].Key);
            Assert.Equal("Regional and product-level revenue by month.", statements[1].Value);
        }

        [Fact]
        public void ParseSetReport_CaseInsensitivity_MetadataParsesCorrectly()
        {
            var sql = "set report title = 'Test'; SET REPORT description = 'Test';";
            var script = Parse(sql);
            
            var statements = script.Statements.OfType<SetReportMetadataStatement>().ToList();
            Assert.Equal(2, statements.Count);
            Assert.Equal("TITLE", statements[0].Key);
            Assert.Equal("DESCRIPTION", statements[1].Key);
        }
    }
}
