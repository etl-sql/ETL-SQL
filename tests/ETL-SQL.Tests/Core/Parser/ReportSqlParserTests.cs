using System;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using Xunit;

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
        public void ParseCreateVisual_FetchMode_ParsesCorrectly()
        {
            var sql = @"
CREATE VISUAL ResultTable AS TABLE (
    SOURCE = #results,
    FETCH = ON_RUN
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal(VisualFetchMode.OnRun, stmt!.FetchMode);
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
        [InlineData("BAR", VisualType.Bar)]
        [InlineData("LINE", VisualType.Line)]
        [InlineData("SCATTER", VisualType.Scatter)]
        [InlineData("PIE", VisualType.Pie)]
        [InlineData("CARD", VisualType.Card)]
        [InlineData("SLICER", VisualType.Slicer)]
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
CREATE PAGE Dashboard AS DASHBOARD (
    STRUCTURE = 'AB/CC',
    MAP ('A' = SalesChart, 'B' = PieChart, 'C' = SummaryTable)
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreatePageStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("Dashboard", stmt!.Name);
            Assert.Equal(PageMode.Dashboard, stmt.PageMode);
            Assert.Equal("AB/CC", stmt.Structure);
            Assert.Equal(3, stmt.SlotMap.Count);
            Assert.Equal("SalesChart", stmt.SlotMap["A"]);
        }

        [Fact]
        public void ParseCreatePage_PaginatedLayoutAndRefresh_ParsesCorrectly()
        {
            var sql = @"
CREATE PAGE Detail AS PAGINATED (
    REFRESH = 60,
    LAYOUT (
        STRUCTURE = 'A',
        MAP ('A' = ResultTable)
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreatePageStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal(PageMode.Paginated, stmt!.PageMode);
            Assert.Equal(60, stmt.RefreshIntervalSeconds);
            Assert.Equal("A", stmt.Structure);
            Assert.Equal("ResultTable", stmt.SlotMap["A"]);
        }

        [Fact]
        public void ParseCreatePage_Minimal_ParsesCorrectly()
        {
            var sql = @"
CREATE PAGE SimplePage AS DASHBOARD (
    STRUCTURE = 'A',
    MAP ('A' = Chart1),
    GAP = '12px'
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreatePageStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("A", stmt!.Structure);
            Assert.Equal("12px", stmt.Styles["GAP"]);
        }

        [Fact]
        public void ParseCreatePage_MissingAs_ReportsSyntaxError()
        {
            var script = Parse("CREATE PAGE OldPage (STRUCTURE = 'A', MAP ('A' = Chart1));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("Expected AS after page name", StringComparison.OrdinalIgnoreCase));
        }

        // ── CREATE DATASET ────────────────────────────────────────────────────

        [Fact]
        public void ParseCreateDataset_AllOptions_ReturnsCreateDatasetStatement()
        {
            var sql = @"
CREATE DATASET &daily_sales
    TTL = '24h'
    COMPRESS = ON
    ENCRYPT = ON
    KEYFILE = '/keys/sales.key'
AS (SELECT Date, SUM(Amount) AS Total FROM orders GROUP BY Date);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateDatasetStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("&daily_sales", stmt!.TempTableName);
            Assert.Null(stmt.RefreshInterval);
            Assert.Equal("24h", stmt.Ttl);
            Assert.True(stmt.Compress);
            Assert.Equal(DatasetEncryptionMode.MachineBound, stmt.EncryptionMode);
            Assert.Equal("/keys/sales.key", stmt.KeyFile);
            Assert.NotNull(stmt.SourceQuery);
        }

        [Fact]
        public void ParseCreateDataset_RefreshEvery_NamesTheJobScheduleReplacement()
        {
            var script = Parse("CREATE DATASET &summary REFRESH EVERY '1h' AS (SELECT 1 AS Val);");

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Contains("retired", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CREATE JOB", diagnostic.Message, StringComparison.Ordinal);
            Assert.Contains("CREATE SCHEDULE", diagnostic.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ParseCreateDataset_Minimal_OnlyRequiresNameAndQuery()
        {
            var sql = @"CREATE DATASET &summary AS (SELECT 1 AS Val FROM orders);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateDatasetStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("&summary", stmt!.TempTableName);
            Assert.Null(stmt.RefreshInterval);
            Assert.False(stmt.Compress);
            Assert.Equal(DatasetEncryptionMode.MachineBound, stmt.EncryptionMode);
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
        public void ParseCreateVisual_WithInteractions_ParsesOnSelect()
        {
            var sql = @"
CREATE VISUAL SalesChart AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = region, Y = total),
    INTERACTIONS (ON_SELECT = HIGHLIGHT, MATCHING = region)
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("HIGHLIGHT", stmt!.Interactions.Single(i => i.Key == "ON_SELECT").Value);
            Assert.Equal("REGION", stmt.Interactions.Single(i => i.Key == "MATCHING").Value);
        }

        [Fact]
        public void ParseCreateVisual_CrossVisualActionOption_ReportsSyntaxError()
        {
            var script = Parse("CREATE VISUAL OldChart AS BAR (SOURCE = #sales, OPTIONS (CROSS_VISUAL_ACTION = HIGHLIGHT));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("INTERACTIONS", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseCreateButton_ActionFirstSyntax_ParsesCommandActions()
        {
            var sql = @"
CREATE BUTTON RefreshButton AS (
    TITLE = 'Refresh',
    OPTIONS (TARGET = SalesTable),
    ACTIONS (ON_CLICK = (REFRESH_REPORT, EXPORT_CSV))
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateButtonStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("BUTTON", stmt!.ButtonType);
            Assert.Equal(2, stmt.Actions.Count);
            Assert.Contains(stmt.Actions.OfType<ReportCommandAction>(), a => a.Command == "REFRESH");
            Assert.Contains(stmt.Actions.OfType<ReportCommandAction>(), a => a.Command == "EXPORT_CSV");
        }

        [Fact]
        public void ParseCreateButton_NavigatePageAction_ParsesTargetPage()
        {
            var sql = @"
CREATE BUTTON DetailsButton AS (
    TITLE = 'Details',
    ACTIONS (ON_CLICK = NAVIGATE_PAGE(Details))
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateButtonStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            var action = stmt!.Actions.OfType<NavigatePageAction>().SingleOrDefault();
            Assert.NotNull(action);
            Assert.Equal("Details", action!.TargetPage);
            Assert.Equal("ON_CLICK", action.Trigger);
        }

        [Fact]
        public void ParseCreateButton_RefreshVisualsAction_ParsesTargets()
        {
            var sql = @"
CREATE BUTTON RefreshSelection AS (
    TITLE = 'Refresh Selection',
    ACTIONS (ON_CLICK = REFRESH_VISUALS(SalesTable, RevenueChart))
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateButtonStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            var action = stmt!.Actions.OfType<RefreshVisualsAction>().SingleOrDefault();
            Assert.NotNull(action);
            Assert.Equal(new[] { "SalesTable", "RevenueChart" }, action!.Targets);
            Assert.Equal("ON_CLICK", action.Trigger);
            Assert.Equal("REFRESH_VISUALS(SalesTable, RevenueChart)", action.ToSql());
        }

        [Fact]
        public void ParseCreateButton_OldTypedSyntax_ReportsSyntaxError()
        {
            var script = Parse("CREATE BUTTON OldRefresh AS REFRESH (TITLE = 'Refresh');");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("Expected '(' after AS", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseCreateVisual_ChartOnChangeAction_ReportsSyntaxError()
        {
            var script = Parse("CREATE VISUAL SalesChart AS BAR (SOURCE = #sales, ACTIONS (ON_CHANGE = SET_PARAMETER(@region, region)));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("BAR visuals only support ACTIONS (ON_CLICK", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseCreateVisual_ControlOnClickAction_ReportsSyntaxError()
        {
            var script = Parse("CREATE VISUAL RegionFilter AS SLICER (SOURCE = #regions, ACTIONS (ON_CLICK = SET_PARAMETER(@region, region)));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("SLICER visuals only support ACTIONS (ON_CHANGE", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseCreateVisual_PassiveVisualWithActions_ReportsSyntaxError()
        {
            var script = Parse("CREATE VISUAL HelpText AS TEXT (CONTENT = 'Pick a region', ACTIONS (ON_CLICK = SET_UI_STATE('Filters', 'VISIBLE', ON)));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("TEXT visuals do not support ACTIONS", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseCreateButton_OnChangeAction_ReportsSyntaxError()
        {
            var script = Parse("CREATE BUTTON ApplyFilters AS (TITLE = 'Apply', ACTIONS (ON_CHANGE = APPLY_PARAMETERS));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("BUTTON actions only support ACTIONS (ON_CLICK", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseCreateNavigation_PagesInBody_ParsesCorrectly()
        {
            var sql = @"
CREATE NAVIGATION MainNav AS TAB (
    ORIENTATION = HORIZONTAL,
    DEFAULT = Overview,
    PAGES (Overview, Details)
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateNavigationStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("MainNav", stmt!.Name);
            Assert.Equal("Overview", stmt.DefaultPage);
            Assert.Equal(new[] { "Overview", "Details" }, stmt.Pages);
        }

        [Fact]
        public void ParseCreateContainer_LayoutAndOptions_ParsesCorrectly()
        {
            var sql = @"
CREATE CONTAINER FilterDrawer AS DRAWER (
    TITLE = 'Filters',
    VISIBLE = ON,
    ICON = 'filter',
    LAYOUT (
        STRUCTURE = 'A / B',
        MAP ('A' = RegionFilter, 'B' = YearSlider),
        GAP = '8px',
        PINNABLE = OFF
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateContainerStatement>().FirstOrDefault();

            Assert.NotNull(stmt);
            Assert.Equal("DRAWER", stmt!.ContainerType);
            Assert.True(stmt.IsCollapsible);
            Assert.False(stmt.IsPinnable);
            Assert.Equal("A / B", stmt.Structure);
            Assert.Equal("RegionFilter", stmt.SlotMap["A"]);
            Assert.Equal("YearSlider", stmt.SlotMap["B"]);
            Assert.Equal("8px", stmt.Styles["GAP"]);
            Assert.Equal("filter", stmt.Icon);
        }

        [Fact]
        public void ParseCreateContainer_TopLevelStructure_ReportsSyntaxError()
        {
            var script = Parse("CREATE CONTAINER OldPanel AS BOX (STRUCTURE = 'A', MAP ('A' = Visual1));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("Unexpected token 'STRUCTURE'", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseCreateContainer_PinnableInOptions_ReportsSyntaxError()
        {
            var script = Parse("CREATE CONTAINER OldDrawer AS DRAWER (LAYOUT (STRUCTURE = 'A', MAP ('A' = Visual1)), OPTIONS (PINNABLE = OFF));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("PINNABLE", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ParseCreateContainer_CollapsibleOption_ReportsSyntaxError()
        {
            var script = Parse("CREATE CONTAINER OldDrawer AS BOX (OPTIONS (COLLAPSIBLE = ON));");

            Assert.Contains(script.Diagnostics, d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Message.Contains("COLLAPSIBLE", StringComparison.OrdinalIgnoreCase));
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

        // ── TABLE column MAPPINGS ────────────────────────────────────────────

        [Fact]
        public void ParseTableMappings_ColumnWithFormatAndAlign_ParsesMetadata()
        {
            var sql = @"
CREATE VISUAL Orders AS TABLE (
    SOURCE = #orders,
    MAPPINGS (
        order_id AS 'Order #',
        amount FORMAT 'C2' ALIGN 'right' AS 'Amount',
        status
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Equal(VisualType.Table, stmt.VisualType);
            Assert.Equal(3, stmt.Mappings.Count);

            var m0 = stmt.Mappings[0];
            Assert.Equal("order_id", m0.Column);
            Assert.Equal("Order #", m0.DisplayName);
            Assert.Null(m0.Format);

            var m1 = stmt.Mappings[1];
            Assert.Equal("amount", m1.Column);
            Assert.Equal("C2", m1.Format);
            Assert.Equal("right", m1.Align);
            Assert.Equal("Amount", m1.DisplayName);

            var m2 = stmt.Mappings[2];
            Assert.Equal("status", m2.Column);
            Assert.Null(m2.Format);
            Assert.Null(m2.DisplayName);
        }

        [Fact]
        public void ParseTableMappings_RoleEqualsColumnSyntax_StillWorks()
        {
            // MATRIX / chart ROLE = column syntax must be unchanged
            var sql = @"
CREATE VISUAL SalesByRegion AS MATRIX (
    SOURCE = #data,
    MAPPINGS ( ROW = category, COL = region, VALUE = revenue )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Equal(VisualType.Matrix, stmt.VisualType);
            var row = stmt.Mappings.First(m => m.Role == "ROW");
            Assert.Equal("category", row.Column);
            var col = stmt.Mappings.First(m => m.Role == "COL");
            Assert.Equal("region", col.Column);
        }

        [Fact]
        public void ParseFormattingRule_WithFontColor_ParsesBothColors()
        {
            var sql = @"
CREATE VISUAL T AS TABLE (
    SOURCE = #t,
    FORMATTING (
        WHEN amount < 0 THEN '#ffe0e0' FONT_COLOR '#cc0000',
        WHEN amount > 1000 THEN '#d4edda'
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Equal(2, stmt.FormattingRules.Count);
            Assert.Equal("#ffe0e0", stmt.FormattingRules[0].Color);
            Assert.Equal("#cc0000", stmt.FormattingRules[0].FontColor);
            Assert.Equal("#d4edda", stmt.FormattingRules[1].Color);
            Assert.Null(stmt.FormattingRules[1].FontColor);
        }

        [Fact]
        public void ParseTableMappings_DataBarWithColor_ParsesCorrectly()
        {
            var sql = @"
CREATE VISUAL T AS TABLE (
    SOURCE = #t,
    MAPPINGS (
        revenue FORMAT 'C2' DATA_BAR COLOR '#4472C4' AS 'Revenue',
        cost DATA_BAR
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Equal(2, stmt.Mappings.Count);
            Assert.True(stmt.Mappings[0].DataBar);
            Assert.Equal("#4472C4", stmt.Mappings[0].DataBarColor);
            Assert.Equal("C2", stmt.Mappings[0].Format);
            Assert.Equal("Revenue", stmt.Mappings[0].DisplayName);
            Assert.True(stmt.Mappings[1].DataBar);
            Assert.Null(stmt.Mappings[1].DataBarColor);
        }

        [Fact]
        public void ParseTableMappings_ColorScale_ParsesFromAndTo()
        {
            var sql = @"
CREATE VISUAL T AS TABLE (
    SOURCE = #t,
    MAPPINGS (
        score COLOR_SCALE FROM '#FF0000' TO '#00FF00'
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Single(stmt.Mappings);
            Assert.Equal("#FF0000", stmt.Mappings[0].ColorScaleFrom);
            Assert.Equal("#00FF00", stmt.Mappings[0].ColorScaleTo);
        }

        [Fact]
        public void ParseTableMappings_ImageWithWidth_ParsesCorrectly()
        {
            var sql = @"
CREATE VISUAL T AS TABLE (
    SOURCE = #t,
    MAPPINGS (
        logo_url IMAGE WIDTH 48 AS 'Logo'
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Single(stmt.Mappings);
            var m = stmt.Mappings[0];
            Assert.Equal("logo_url", m.Column);
            Assert.Equal("image", m.CellRenderer);
            Assert.Equal(48, m.ImageWidth);
            Assert.Equal("Logo", m.DisplayName);
        }

        [Fact]
        public void ParseTableMappings_HyperlinkWithLabel_ParsesCorrectly()
        {
            var sql = @"
CREATE VISUAL T AS TABLE (
    SOURCE = #t,
    MAPPINGS (
        product_url HYPERLINK LABEL 'View' AS 'Link'
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Single(stmt.Mappings);
            var m = stmt.Mappings[0];
            Assert.Equal("product_url", m.Column);
            Assert.Equal("hyperlink", m.CellRenderer);
            Assert.Equal("View", m.HyperlinkLabel);
            Assert.Equal("Link", m.DisplayName);
        }

        [Fact]
        public void ParseTableMappings_Sparkline_ParsesColumnsAndType()
        {
            var sql = @"
CREATE VISUAL T AS TABLE (
    SOURCE = #t,
    MAPPINGS (
        SPARKLINE(jan, feb, mar, apr, may) LINE AS 'Trend'
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Single(stmt.Mappings);
            var m = stmt.Mappings[0];
            Assert.Equal("SPARKLINE", m.Role);
            Assert.NotNull(m.SparklineColumns);
            Assert.Equal(new[] { "jan", "feb", "mar", "apr", "may" }, m.SparklineColumns!);
            Assert.Equal("line", m.SparklineType);
            Assert.Equal("Trend", m.DisplayName);
        }

        [Fact]
        public void ParseTableMappings_SparklineArea_DefaultsAndMixed()
        {
            // Sparkline can appear alongside regular column mappings
            var sql = @"
CREATE VISUAL T AS TABLE (
    SOURCE = #t,
    MAPPINGS (
        region,
        SPARKLINE(q1, q2, q3, q4) AREA AS 'Quarterly'
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Equal(2, stmt.Mappings.Count);
            Assert.Equal("region", stmt.Mappings[0].Column);
            var sparkline = stmt.Mappings[1];
            Assert.Equal("SPARKLINE", sparkline.Role);
            Assert.Equal("area", sparkline.SparklineType);
            Assert.Equal(new[] { "q1", "q2", "q3", "q4" }, sparkline.SparklineColumns!);
        }

        [Fact]
        public void ParseCardMappings_SparklineSource_PreservesSemanticBinding()
        {
            var sql = @"
CREATE VISUAL Revenue AS CARD (
    SOURCE = #summary,
    MAPPINGS (
        VALUE = total,
        SPARKLINE = #daily (X = SaleDate, Y = Amount, TYPE = AREA)
    )
);";
            var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
            var sparkline = Assert.Single(stmt.Mappings, mapping => mapping.Role == "SPARKLINE");

            Assert.Equal("#daily", sparkline.SparklineSource);
            Assert.Equal("SaleDate", sparkline.SparklineXColumn);
            Assert.Equal("Amount", sparkline.SparklineYColumn);
            Assert.Equal("area", sparkline.SparklineType);
            Assert.Contains("SPARKLINE = #daily (X = SaleDate, Y = Amount, TYPE = AREA)", stmt.ToSql());
        }

        [Fact]
        public void ParseTableMappings_ProgressBar_PreservesBoundsAndSafeColorIntent()
        {
            var sql = @"
CREATE VISUAL Goals AS TABLE (
    SOURCE = #goals,
    MAPPINGS (
        team,
        attainment PROGRESS_BAR (MIN = 0, MAX = 1, COLOR = '#16A34A') AS 'Attainment'
    )
);";
            var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
            var progress = stmt.Mappings[1];

            Assert.True(progress.ProgressBar);
            Assert.Equal(0m, progress.ProgressMinimum);
            Assert.Equal(1m, progress.ProgressMaximum);
            Assert.Equal("#16A34A", progress.ProgressColor);
            Assert.Contains("attainment PROGRESS_BAR (MIN = 0, MAX = 1, COLOR = '#16A34A') AS 'Attainment'", stmt.ToSql());
        }

        [Fact]
        public void ToSql_TableMappingsWithCellFormatting_PreservesOptions()
        {
            var sql = @"
CREATE VISUAL T AS TABLE (
    SOURCE = #t,
    MAPPINGS (
        revenue FORMAT 'C2' DATA_BAR COLOR '#4472C4' AS 'Revenue',
        score COLOR_SCALE FROM '#FF0000' TO '#00FF00',
        logo_url IMAGE WIDTH 48 AS 'Logo',
        product_url HYPERLINK LABEL 'View' AS 'Link',
        SPARKLINE(jan, feb, mar) AREA AS 'Trend'
    )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            var serialized = stmt.ToSql();

            Assert.Contains("revenue FORMAT 'C2' DATA_BAR COLOR '#4472C4' AS 'Revenue'", serialized);
            Assert.Contains("score COLOR_SCALE FROM '#FF0000' TO '#00FF00'", serialized);
            Assert.Contains("logo_url IMAGE WIDTH 48 AS 'Logo'", serialized);
            Assert.Contains("product_url HYPERLINK LABEL 'View' AS 'Link'", serialized);
            Assert.Contains("SPARKLINE(jan, feb, mar) AREA AS 'Trend'", serialized);
        }

        [Fact]
        public void ParseMatrixMappings_MultipleValues_ParsesAllRoles()
        {
            var sql = @"
CREATE VISUAL SalesPivot AS MATRIX (
    SOURCE = #data,
    MAPPINGS ( ROW = category, COL = region, VALUE = revenue, VALUE2 = units )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.Equal(VisualType.Matrix, stmt.VisualType);
            Assert.Equal("revenue", stmt.Mappings.First(m => m.Role == "VALUE").Column);
            Assert.Equal("units", stmt.Mappings.First(m => m.Role == "VALUE2").Column);
        }

        [Fact]
        public void ParseMatrixOptions_SubtotalsAndAxisSort_ParseCorrectly()
        {
            var sql = @"
CREATE VISUAL SalesPivot AS MATRIX (
    SOURCE = #data,
    MAPPINGS ( ROW = category, COL = region, VALUE = revenue ),
    OPTIONS  ( SUBTOTALS = ON, AXIS_SORT = DESC )
);";
            var script = Parse(sql);
            var stmt = script.Statements.OfType<CreateVisualStatement>().First();

            Assert.NotNull(stmt.Options!.FirstOrDefault(o => o.Key == "SUBTOTALS"));
            Assert.Equal("DESC", stmt.Options!.First(o => o.Key == "AXIS_SORT").Value);
        }
    }
}
