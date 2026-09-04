using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Builders;
using ETL_SQL.Reporting.Renderers;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Matrix;

public class MatrixControlsTests
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

    private static (VisualManifest Manifest, string Svg, JsonDocument Json) BuildMatrix(
        string script,
        List<string>? columns = null,
        List<List<string?>>? rows = null)
    {
        var statement = ParseVisual(script);
        var defaultCols = columns ?? ["Category", "Region", "Revenue"];
        var defaultRows = rows ??
        [
            ["Electronics", "North", "100"],
            ["Electronics", "South", "200"],
            ["Furniture", "North", "300"],
            ["Furniture", "South", "400"]
        ];

        var manifest = new VisualManifest
        {
            Name = statement.Name,
            VisualType = statement.VisualType.ToString().ToUpperInvariant(),
            Columns = defaultCols,
            Rows = defaultRows
        };

        foreach (var opt in statement.Options)
            manifest.Options[opt.Key] = opt.Value;

        foreach (var m in statement.Mappings)
        {
            manifest.Options["mapping:" + m.Role.ToLowerInvariant()] = m.Column;
            if (m.DataBar)
                manifest.Options["mapping:" + m.Role.ToLowerInvariant() + ":data_bar"] = "true";
            if (!string.IsNullOrWhiteSpace(m.DataBarColor))
                manifest.Options["mapping:" + m.Role.ToLowerInvariant() + ":data_bar_color"] = m.DataBarColor;
        }

        if (statement.FormattingRules.Count > 0)
        {
            manifest.FormattingRules = statement.FormattingRules.Select(r => new FormattingRuleManifest
            {
                Condition = r.Condition.ToSql(),
                Color = r.Color,
                FontColor = r.FontColor
            }).ToList();
        }

        manifest.ChartConfig = MatrixPivotBuilder.Build(manifest);
        var svg = new SvgChartRenderer().Render(manifest) ?? string.Empty;
        var json = JsonDocument.Parse(manifest.ChartConfig);
        return (manifest, svg, json);
    }

    // ── 1. Column and Row Totals ────────────────────────────────────────────────

    [Fact]
    public void Matrix_ColumnTotal_EnablesGrandTotalsRow()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue),
                OPTIONS (COLUMN_TOTAL = ON)
            );
            """;

        var (_, _, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.True(root.GetProperty("columnTotalsEnabled").GetBoolean());
        Assert.False(root.GetProperty("rowTotalsEnabled").GetBoolean());

        var grandTotals = root.GetProperty("grandTotals");
        Assert.Equal(JsonValueKind.Array, grandTotals.ValueKind);
        // Column totals: North is 100+300=400, South is 200+400=600
        Assert.Equal(3, grandTotals.GetArrayLength()); // 1 row-dim null + 2 cols
        Assert.Equal(JsonValueKind.Null, grandTotals[0].ValueKind);
        Assert.Equal("400", grandTotals[1].GetString());
        Assert.Equal("600", grandTotals[2].GetString());
    }

    [Fact]
    public void Matrix_RowTotal_SetsRowTotalsEnabled()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue),
                OPTIONS (ROW_TOTAL = ON)
            );
            """;

        var (_, _, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.True(root.GetProperty("rowTotalsEnabled").GetBoolean());
        Assert.False(root.GetProperty("columnTotalsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("grandTotals").ValueKind);
    }

    [Fact]
    public void Matrix_ColumnTotalAndRowTotal_BothEnabled()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue),
                OPTIONS (COLUMN_TOTAL = ON, ROW_TOTAL = ON)
            );
            """;

        var (_, _, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.True(root.GetProperty("columnTotalsEnabled").GetBoolean());
        Assert.True(root.GetProperty("rowTotalsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("grandTotals").ValueKind);
    }

    [Fact]
    public void Matrix_GrandTotal_CompatibleWithColumnTotal()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue),
                OPTIONS (GRAND_TOTAL = ON)
            );
            """;

        var (_, _, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.True(root.GetProperty("columnTotalsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("grandTotals").ValueKind);
    }

    // ── 2. Conditional Cell Formatting ──────────────────────────────────────────

    [Fact]
    public void Matrix_FormattingRules_ParsedAndSerializedInMeta()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue),
                FORMATTING (
                    WHEN value > 250 THEN '#10b981' FONT '#ffffff',
                    WHEN value < 150 THEN '#fee2e2' FONT '#991b1b'
                )
            );
            """;

        var (manifest, svg, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.NotNull(manifest.FormattingRules);
        Assert.Equal(2, manifest.FormattingRules.Count);
        Assert.Equal("#10b981", manifest.FormattingRules[0].Color);
        Assert.Equal("#ffffff", manifest.FormattingRules[0].FontColor);

        var formattingRules = root.GetProperty("formattingRules");
        Assert.Equal(2, formattingRules.GetArrayLength());
        Assert.Contains("250", formattingRules[0].GetProperty("condition").GetString()!);
        Assert.Equal("#10b981", formattingRules[0].GetProperty("color").GetString());
        Assert.Equal("#ffffff", formattingRules[0].GetProperty("fontColor").GetString());

        // In SVG output, cell with value 300 should receive fill '#10b981'
        Assert.Contains("#10b981", svg);
        Assert.Contains("#fee2e2", svg);
    }

    [Fact]
    public void Matrix_FormattingRules_SupportsBetweenCondition()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue),
                FORMATTING (
                    WHEN value BETWEEN 150 AND 350 THEN '#fef3c7'
                )
            );
            """;

        var (_, svg, json) = BuildMatrix(script);
        var formattingRules = json.RootElement.GetProperty("formattingRules");
        Assert.Equal(1, formattingRules.GetArrayLength());

        // 200 and 300 fall into 150..350 range
        Assert.Contains("#fef3c7", svg);
    }

    // ── 3. Cell Data Bars ───────────────────────────────────────────────────────

    [Fact]
    public void Matrix_ValueDataBar_MappingSyntax_SetsDataBarMeta()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (
                    ROW   = Category,
                    COL   = Region,
                    VALUE = Revenue DATA_BAR COLOR '#2563eb'
                )
            );
            """;

        var (manifest, svg, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.True(root.GetProperty("dataBar").GetBoolean());
        Assert.Equal("#2563eb", root.GetProperty("dataBarColor").GetString());
        Assert.Equal(0d, root.GetProperty("dataBarMin").GetDouble());
        Assert.Equal(400d, root.GetProperty("dataBarMax").GetDouble());

        // SVG should contain data bar elements
        Assert.Contains("matrix-data-bar", svg);
        Assert.Contains("#2563eb", svg);
    }

    [Fact]
    public void Matrix_ValueDataBar_PrefixSyntax_Supported()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (
                    ROW = Category,
                    COL = Region,
                    VALUE DATA_BAR COLOR '#dc2626' = Revenue
                )
            );
            """;

        var (_, svg, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.True(root.GetProperty("dataBar").GetBoolean());
        Assert.Equal("#dc2626", root.GetProperty("dataBarColor").GetString());
        Assert.Contains("matrix-data-bar", svg);
    }

    [Fact]
    public void Matrix_DataBar_OptionToggle_Supported()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue),
                OPTIONS (DATA_BAR = ON, DATA_BAR_COLOR = '#059669')
            );
            """;

        var (_, svg, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.True(root.GetProperty("dataBar").GetBoolean());
        Assert.Equal("#059669", root.GetProperty("dataBarColor").GetString());
        Assert.Contains("matrix-data-bar", svg);
    }

    // ── 4. Expand/Collapse Default State ────────────────────────────────────────

    [Theory]
    [InlineData("ALL", "ALL")]
    [InlineData("NONE", "NONE")]
    [InlineData("LEVEL_1", "LEVEL_1")]
    [InlineData("LEVEL_2", "LEVEL_2")]
    public void Matrix_DefaultExpand_SerializedInMeta(string authoredValue, string expectedMeta)
    {
        var script = $"""
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue),
                OPTIONS (DEFAULT_EXPAND = {authoredValue})
            );
            """;

        var (_, _, json) = BuildMatrix(script);
        var root = json.RootElement;

        Assert.Equal(expectedMeta, root.GetProperty("defaultExpand").GetString());
    }

    [Fact]
    public void Matrix_DefaultExpand_DefaultsToAll()
    {
        var script = """
            CREATE VISUAL TestMatrix AS MATRIX (
                SOURCE = #data,
                MAPPINGS (ROW = Category, COL = Region, VALUE = Revenue)
            );
            """;

        var (_, _, json) = BuildMatrix(script);
        Assert.Equal("ALL", json.RootElement.GetProperty("defaultExpand").GetString());
    }
}
