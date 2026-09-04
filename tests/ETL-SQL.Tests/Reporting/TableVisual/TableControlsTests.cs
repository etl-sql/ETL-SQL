using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Builders;
using ETL_SQL.Reporting.Renderers;
using Xunit;

namespace ETL_SQL.Tests.Reporting.TableVisual;

public class TableControlsTests
{
    private static CreateVisualStatement ParseVisual(string script)
    {
        var lexer = new Lexer(script);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var statements = new List<Statement>();
        while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());
        return (CreateVisualStatement)statements[0];
    }

    [Fact]
    public void Parse_FreezeAndWidthModifiers_AstPopulated()
    {
        var sql = @"
CREATE VISUAL OrdersTable AS TABLE (
    SOURCE = #orders,
    MAPPINGS (
        order_id FREEZE LEFT WIDTH 120 AS 'Order ID',
        customer_name WIDTH 200 AS 'Customer',
        total_amount FORMAT 'C2' ALIGN 'right' WIDTH 100 AS 'Total',
        status FREEZE RIGHT WIDTH 90 AS 'Status'
    )
);";

        var visual = ParseVisual(sql);
        Assert.Equal(VisualType.Table, visual.VisualType);
        Assert.Equal(4, visual.Mappings.Count);

        var m0 = visual.Mappings[0];
        Assert.Equal("ORDER_ID", m0.Column, ignoreCase: true);
        Assert.Equal("LEFT", m0.Freeze);
        Assert.Equal(120, m0.Width);
        Assert.Equal("Order ID", m0.DisplayName);

        var m1 = visual.Mappings[1];
        Assert.Null(m1.Freeze);
        Assert.Equal(200, m1.Width);

        var m2 = visual.Mappings[2];
        Assert.Null(m2.Freeze);
        Assert.Equal(100, m2.Width);
        Assert.Equal("C2", m2.Format);
        Assert.Equal("right", m2.Align);

        var m3 = visual.Mappings[3];
        Assert.Equal("RIGHT", m3.Freeze);
        Assert.Equal(90, m3.Width);
    }

    [Fact]
    public void Format_FreezeAndWidthModifiers_RoundTrips()
    {
        var sql = @"
CREATE VISUAL OrdersTable AS TABLE (
    SOURCE = #orders,
    MAPPINGS (
        order_id FREEZE LEFT WIDTH 120 AS 'Order ID',
        status FREEZE RIGHT WIDTH 90 AS 'Status'
    )
);";

        var visual = ParseVisual(sql);
        var roundtripped = visual.ToSql();

        Assert.Contains("FREEZE LEFT", roundtripped);
        Assert.Contains("WIDTH 120", roundtripped);
        Assert.Contains("FREEZE RIGHT", roundtripped);
        Assert.Contains("WIDTH 90", roundtripped);
    }

    [Fact]
    public void Parse_DefaultSortAndTotalPosition_OptionsPopulated()
    {
        var sql = @"
CREATE VISUAL ProductSummary AS TABLE (
    SOURCE = #products,
    OPTIONS (
        DEFAULT_SORT = (category ASC, revenue DESC),
        TOTAL_POSITION = TOP,
        GRAND_TOTAL = SUM
    )
);";

        var visual = ParseVisual(sql);
        Assert.Equal(3, visual.Options.Count);

        var sortOpt = visual.Options.FirstOrDefault(o => o.Key == "DEFAULT_SORT");
        Assert.NotNull(sortOpt);
        Assert.Equal("category ASC, revenue DESC", sortOpt.Value);

        var posOpt = visual.Options.FirstOrDefault(o => o.Key == "TOTAL_POSITION");
        Assert.NotNull(posOpt);
        Assert.Equal("TOP", posOpt.Value);

        var totalOpt = visual.Options.FirstOrDefault(o => o.Key == "GRAND_TOTAL");
        Assert.NotNull(totalOpt);
        Assert.Equal("SUM", totalOpt.Value);

        var sqlOut = visual.ToSql();
        Assert.Contains("DEFAULT_SORT = (category ASC, revenue DESC)", sqlOut);
        Assert.Contains("TOTAL_POSITION = TOP", sqlOut);
        Assert.Contains("GRAND_TOTAL = 'SUM'", sqlOut);
    }

    [Fact]
    public void Parse_SummaryClause_TotalPositionPopulated()
    {
        var sql = @"
CREATE VISUAL ProductSummary AS TABLE (
    SOURCE = #products,
    SUMMARY (
        GRAND_TOTAL = ON,
        TOTAL_POSITION = TOP,
        SUM(revenue) AS 'Total Revenue'
    )
);";

        var visual = ParseVisual(sql);
        Assert.NotNull(visual.SummaryOptions);
        Assert.True(visual.SummaryOptions.GrandTotalRow);
        Assert.Equal("TOP", visual.SummaryOptions.TotalPosition);
        Assert.Single(visual.Summaries);
        Assert.Equal("SUM", visual.Summaries[0].Aggregate);

        var sqlOut = visual.ToSql();
        Assert.Contains("TOTAL_POSITION = TOP", sqlOut);
        Assert.Contains("GRAND_TOTAL = ON", sqlOut);
    }

    [Fact]
    public void VisualBuilder_TableMappings_PopulatesFreezeAndWidthInManifest()
    {
        var sql = @"
CREATE VISUAL TestTable AS TABLE (
    SOURCE = #data,
    MAPPINGS (
        colA FREEZE LEFT WIDTH 140 AS 'Column A',
        colB WIDTH 180 AS 'Column B',
        colC FREEZE RIGHT AS 'Column C'
    )
);";

        var visual = ParseVisual(sql);
        var manifest = new VisualManifest
        {
            Name = visual.Name,
            VisualType = "TABLE",
            Columns = ["colA", "colB", "colC"],
            Rows = [["a1", "b1", "c1"], ["a2", "b2", "c2"]]
        };

        // Call private ApplyTableMappings via reflection or invoke VisualBuilder
        var method = typeof(VisualBuilder).GetMethod("ApplyTableMappings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        // Dummy instance
        var builder = RuntimeHelpers.GetUninitializedObject(typeof(VisualBuilder));
        method.Invoke(builder, [visual, manifest]);

        Assert.NotNull(manifest.ColumnMeta);
        Assert.Equal(3, manifest.ColumnMeta.Count);

        var cm0 = manifest.ColumnMeta[0];
        Assert.NotNull(cm0);
        Assert.Equal("left", cm0.Freeze);
        Assert.Equal(140, cm0.Width);

        var cm1 = manifest.ColumnMeta[1];
        Assert.NotNull(cm1);
        Assert.Null(cm1.Freeze);
        Assert.Equal(180, cm1.Width);

        var cm2 = manifest.ColumnMeta[2];
        Assert.NotNull(cm2);
        Assert.Equal("right", cm2.Freeze);
        Assert.Null(cm2.Width);
    }

    [Fact]
    public void VisualBuilder_ApplyTableSort_MultiColumnOrder()
    {
        var sql = @"
CREATE VISUAL SortDemo AS TABLE (
    SOURCE = #data,
    OPTIONS (
        DEFAULT_SORT = (category ASC, revenue DESC)
    )
);";

        var visual = ParseVisual(sql);
        var manifest = new VisualManifest
        {
            Name = visual.Name,
            VisualType = "TABLE",
            Columns = ["category", "revenue", "product"],
            Rows =
            [
                ["Furniture", "100", "Chair"],
                ["Electronics", "50", "Mouse"],
                ["Furniture", "400", "Desk"],
                ["Electronics", "500", "Laptop"],
                ["Electronics", "200", "Keyboard"]
            ]
        };

        foreach (var opt in visual.Options)
            manifest.Options[opt.Key] = opt.Value;

        var method = typeof(VisualBuilder).GetMethod("ApplyTableSort",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var builder = RuntimeHelpers.GetUninitializedObject(typeof(VisualBuilder));
        method.Invoke(builder, [visual, manifest]);

        // Expected sorted order:
        // Electronics 500 Laptop
        // Electronics 200 Keyboard
        // Electronics 50 Mouse
        // Furniture 400 Desk
        // Furniture 100 Chair
        Assert.Equal("Electronics", manifest.Rows[0][0]);
        Assert.Equal("500", manifest.Rows[0][1]);

        Assert.Equal("Electronics", manifest.Rows[1][0]);
        Assert.Equal("200", manifest.Rows[1][1]);

        Assert.Equal("Electronics", manifest.Rows[2][0]);
        Assert.Equal("50", manifest.Rows[2][1]);

        Assert.Equal("Furniture", manifest.Rows[3][0]);
        Assert.Equal("400", manifest.Rows[3][1]);

        Assert.Equal("Furniture", manifest.Rows[4][0]);
        Assert.Equal("100", manifest.Rows[4][1]);
    }

    [Fact]
    public void VisualBuilder_CalculateSummaries_HonorsGrandTotalOptionsAndTotalPosition()
    {
        var sql = @"
CREATE VISUAL TotalsDemo AS TABLE (
    SOURCE = #data,
    OPTIONS (
        GRAND_TOTAL = SUM,
        TOTAL_POSITION = TOP
    )
);";

        var visual = ParseVisual(sql);
        var manifest = new VisualManifest
        {
            Name = visual.Name,
            VisualType = "TABLE",
            Columns = ["Product", "Revenue", "Cost"],
            Rows =
            [
                ["Laptop", "1000", "600"],
                ["Mouse", "50", "20"],
                ["Chair", "300", "150"]
            ]
        };

        foreach (var opt in visual.Options)
            manifest.Options[opt.Key] = opt.Value;

        var method = typeof(VisualBuilder).GetMethod("CalculateSummaries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var builder = RuntimeHelpers.GetUninitializedObject(typeof(VisualBuilder));
        method.Invoke(builder, [visual, manifest]);

        Assert.NotNull(manifest.SummaryData);
        Assert.Equal("TOP", manifest.SummaryData.TotalPosition);
        Assert.NotNull(manifest.SummaryData.GrandTotals);
        Assert.Equal("1350", manifest.SummaryData.GrandTotals["Revenue"]);
        Assert.Equal("770", manifest.SummaryData.GrandTotals["Cost"]);
    }

    [Fact]
    public void MarkdownRenderer_RendersTopAndBottomTotalPositions()
    {
        var manifestTop = new VisualManifest
        {
            Name = "TopTotals",
            VisualType = "TABLE",
            Columns = ["Product", "Revenue"],
            Rows = [["Widget", "100"]],
            SummaryData = new TableSummaryData
            {
                TotalPosition = "TOP",
                GrandTotals = new Dictionary<string, string> { ["Product"] = "Total", ["Revenue"] = "100" }
            }
        };

        var sbTop = new System.Text.StringBuilder();
        var renderTableMethod = typeof(MarkdownRenderer).GetMethod("RenderTable",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(renderTableMethod);

        renderTableMethod.Invoke(null, [sbTop, manifestTop]);
        var mdTop = sbTop.ToString();

        // In TOP position, bold total row should appear before data rows
        var totalIdx = mdTop.IndexOf("**Total**", StringComparison.Ordinal);
        var dataIdx = mdTop.IndexOf("Widget", StringComparison.Ordinal);
        Assert.True(totalIdx > 0 && totalIdx < dataIdx, "Grand total should appear before data rows when TOTAL_POSITION = TOP");

        // In BOTTOM position, bold total row should appear after data rows
        var manifestBottom = new VisualManifest
        {
            Name = "BottomTotals",
            VisualType = "TABLE",
            Columns = ["Product", "Revenue"],
            Rows = [["Widget", "100"]],
            SummaryData = new TableSummaryData
            {
                TotalPosition = "BOTTOM",
                GrandTotals = new Dictionary<string, string> { ["Product"] = "Total", ["Revenue"] = "100" }
            }
        };

        var sbBottom = new System.Text.StringBuilder();
        renderTableMethod.Invoke(null, [sbBottom, manifestBottom]);
        var mdBottom = sbBottom.ToString();

        var totalBottomIdx = mdBottom.IndexOf("**Total**", StringComparison.Ordinal);
        var dataBottomIdx = mdBottom.IndexOf("Widget", StringComparison.Ordinal);
        Assert.True(totalBottomIdx > dataBottomIdx, "Grand total should appear after data rows when TOTAL_POSITION = BOTTOM");
    }
}
