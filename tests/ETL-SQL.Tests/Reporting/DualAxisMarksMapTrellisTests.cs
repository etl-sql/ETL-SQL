using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Builders;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class DualAxisMarksMapTrellisTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    private static (PlotPlan Plan, string Svg) BuildAndRender(CreateVisualStatement stmt, List<string> columns, List<List<string>> rows)
    {
        var manifest = new VisualManifest
        {
            Name = stmt.Name,
            VisualType = stmt.VisualType.ToString().ToUpperInvariant(),
            Columns = columns,
            Rows = rows,
            Options = stmt.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase)
        };
        var spec = new NamedVisualChartLowerer().Lower(stmt, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        ResolvedGeographicGeometry? geography = null;
        if (spec.Coordinate?.Geography is GeographicCoordinateSpec geoSpec)
        {
            geography = GeographicGeometryResolver.Resolve(geoSpec, null);
        }
        var plan = new PlotPlanResolver().Resolve(spec, data, geography: geography);
        var svg = new SvgChartRenderer().Render(plan);
        return (plan, svg);
    }

    [Fact]
    public void Combo_SyncAxes_SynchronizesYAndY2ScalesAndTicks()
    {
        const string sql = """
CREATE VISUAL SyncCombo AS COMBO (
    SOURCE = #metrics,
    MAPPINGS (X = Period, Y = Revenue, Y2 = Expenses),
    OPTIONS (
        SYNC_AXES = ON
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        var (plan, svg) = BuildAndRender(
            stmt,
            ["Period", "Revenue", "Expenses"],
            [
                ["Q1", "100", "20"],
                ["Q2", "200", "45"],
                ["Q3", "150", "30"]
            ]);

        var yScale = plan.Scales.First(s => s.Channel == FieldChannel.Y);
        var y2Scale = plan.Scales.First(s => s.Channel == FieldChannel.Y2);

        Assert.Equal(yScale.Domain[0], y2Scale.Domain[0]);
        Assert.Equal(yScale.Domain[1], y2Scale.Domain[1]);
        Assert.Equal(yScale.Ticks.Length, y2Scale.Ticks.Length);
        for (int i = 0; i < yScale.Ticks.Length; i++)
        {
            Assert.Equal(yScale.Ticks[i].Value, y2Scale.Ticks[i].Value);
            Assert.Equal(yScale.Ticks[i].Label, y2Scale.Ticks[i].Label);
        }

        Assert.Contains("data-sync-axes='on'", svg);
    }

    [Fact]
    public void Combo_SyncAxes_InvalidOption_Throws()
    {
        const string sql = """
CREATE VISUAL BadCombo AS COMBO (
    SOURCE = #metrics,
    MAPPINGS (X = Period, Y = Revenue, Y2 = Expenses),
    OPTIONS (SYNC_AXES = MAYBE)
);
""";
        var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
        var manifest = new VisualManifest { Name = stmt.Name, VisualType = "COMBO", Columns = ["Period", "Revenue", "Expenses"], Rows = [] };
        var ex = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(stmt, manifest));
        Assert.Contains("Invalid SYNC_AXES", ex.Message);
    }

    [Fact]
    public void Combo_SyncAxes_OnNonCombo_Throws()
    {
        const string sql = """
CREATE VISUAL BadBar AS BAR (
    SOURCE = #metrics,
    MAPPINGS (X = Period, Y = Revenue),
    OPTIONS (SYNC_AXES = ON)
);
""";
        var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
        var manifest = new VisualManifest { Name = stmt.Name, VisualType = "BAR", Columns = ["Period", "Revenue"], Rows = [] };
        var ex = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(stmt, manifest));
        Assert.Contains("supported only on COMBO", ex.Message);
    }

    [Fact]
    public void Combo_PerAxisMarks_AreaAndLine_LowersCorrectMarkKinds()
    {
        const string sql = """
CREATE VISUAL AreaLineCombo AS COMBO (
    SOURCE = #metrics,
    MAPPINGS (X = Period, Y = Revenue, Y2 = GrowthRate),
    OPTIONS (
        Y_MARK = AREA,
        Y2_MARK = LINE
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        var (plan, svg) = BuildAndRender(
            stmt,
            ["Period", "Revenue", "GrowthRate"],
            [
                ["Jan", "100", "5"],
                ["Feb", "120", "8"],
                ["Mar", "110", "6"]
            ]);

        Assert.Equal(2, plan.Layers.Length);
        Assert.Equal(MarkKind.Area, plan.Layers[0].Mark);
        Assert.Equal(MarkKind.Line, plan.Layers[1].Mark);

        Assert.Contains("plot-area", svg);
        Assert.Contains("plot-line", svg);
    }

    [Fact]
    public void Combo_PerAxisMarks_BarAndBar_LowersBothAsRect()
    {
        const string sql = """
CREATE VISUAL DualBarCombo AS COMBO (
    SOURCE = #metrics,
    MAPPINGS (X = Period, Y = Actual, Y2 = Target),
    OPTIONS (
        Y_MARK = BAR,
        Y2_MARK = BAR
    )
);
""";
        var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
        var (plan, _) = BuildAndRender(
            stmt,
            ["Period", "Actual", "Target"],
            [
                ["Jan", "100", "90"],
                ["Feb", "120", "110"]
            ]);

        Assert.Equal(2, plan.Layers.Length);
        Assert.Equal(MarkKind.Rect, plan.Layers[0].Mark);
        Assert.Equal(MarkKind.Rect, plan.Layers[1].Mark);
    }

    [Fact]
    public void Combo_PerAxisMarks_InvalidMark_Throws()
    {
        const string sql = """
CREATE VISUAL BadMarkCombo AS COMBO (
    SOURCE = #metrics,
    MAPPINGS (X = Period, Y = Actual, Y2 = Target),
    OPTIONS (Y_MARK = PIE)
);
""";
        var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
        var manifest = new VisualManifest { Name = stmt.Name, VisualType = "COMBO", Columns = ["Period", "Actual", "Target"], Rows = [] };
        var ex = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(stmt, manifest));
        Assert.Contains("Invalid Y_MARK", ex.Message);
    }

    [Fact]
    public void Combo_PerAxisMarks_OnNonCombo_Throws()
    {
        const string sql = """
CREATE VISUAL BadLine AS LINE (
    SOURCE = #metrics,
    MAPPINGS (X = Period, Y = Actual),
    OPTIONS (Y_MARK = BAR)
);
""";
        var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
        var manifest = new VisualManifest { Name = stmt.Name, VisualType = "LINE", Columns = ["Period", "Actual"], Rows = [] };
        var ex = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(stmt, manifest));
        Assert.Contains("supported only on COMBO", ex.Message);
    }

    [Fact]
    public void Line_And_Scatter_SymbolSize_ControlsMarkerRadius()
    {
        const string lineSql = """
CREATE VISUAL CustomLine AS LINE (
    SOURCE = #sales,
    MAPPINGS (X = Month, Y = Revenue),
    OPTIONS (
        SYMBOLS = ON,
        SYMBOL_SIZE = 8
    )
);
""";
        var lineStmt = Parse(lineSql).Statements.OfType<CreateVisualStatement>().Single();
        var (_, lineSvg) = BuildAndRender(
            lineStmt,
            ["Month", "Revenue"],
            [
                ["Jan", "50"],
                ["Feb", "80"]
            ]);

        Assert.Contains("r='8'", lineSvg);

        const string scatterSql = """
CREATE VISUAL CustomScatter AS SCATTER (
    SOURCE = #data,
    MAPPINGS (X = Weight, Y = Height),
    OPTIONS (
        SYMBOL_SIZE = 6.5
    )
);
""";
        var scatterStmt = Parse(scatterSql).Statements.OfType<CreateVisualStatement>().Single();
        var (_, scatterSvg) = BuildAndRender(
            scatterStmt,
            ["Weight", "Height"],
            [
                ["70", "175"],
                ["80", "182"]
            ]);

        Assert.Contains("r='6.5'", scatterSvg);
    }

    [Fact]
    public void SymbolSize_Validation_NegativeOrNonNumeric_Throws()
    {
        const string negSql = """
CREATE VISUAL BadLine AS LINE (
    SOURCE = #sales,
    MAPPINGS (X = Month, Y = Revenue),
    OPTIONS (SYMBOL_SIZE = -2)
);
""";
        var negStmt = Parse(negSql).Statements.OfType<CreateVisualStatement>().Single();
        var manifest = new VisualManifest { Name = negStmt.Name, VisualType = "LINE", Columns = ["Month", "Revenue"], Rows = [] };
        var ex = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(negStmt, manifest));
        Assert.Contains("Must be a positive number", ex.Message);

        const string barSql = """
CREATE VISUAL BadBar AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Month, Y = Revenue),
    OPTIONS (SYMBOL_SIZE = 5)
);
""";
        var barStmt = Parse(barSql).Statements.OfType<CreateVisualStatement>().Single();
        var barManifest = new VisualManifest { Name = barStmt.Name, VisualType = "BAR", Columns = ["Month", "Revenue"], Rows = [] };
        var exBar = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(barStmt, barManifest));
        Assert.Contains("supported only on LINE, SCATTER, BUBBLE, and COMBO", exBar.Message);
    }

    [Fact]
    public void Map_BaseMap_ValidUrlTemplate_RendersTileGroup()
    {
        const string sql = """
CREATE VISUAL OpenStreetMapVisual AS MAP (
    SOURCE = #locations,
    MODE = POINTS,
    MAPPINGS (LAT = Latitude, LON = Longitude),
    OPTIONS (
        BASE_MAP = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        var (_, svg) = BuildAndRender(
            stmt,
            ["Latitude", "Longitude"],
            [
                ["40.7128", "-74.0060"],
                ["34.0522", "-118.2437"]
            ]);

        Assert.Contains("plot-geographic-basemap", svg);
        Assert.Contains("data-base-map-url='https://tile.openstreetmap.org/{z}/{x}/{y}.png'", svg);
    }

    [Fact]
    public void Map_BaseMap_InvalidTemplates_Throws()
    {
        const string missingPlaceholders = """
CREATE VISUAL BadMap AS MAP (
    SOURCE = #locations,
    MAPPINGS (LAT = Latitude, LON = Longitude),
    OPTIONS (BASE_MAP = 'https://tile.openstreetmap.org/tiles.png')
);
""";
        var stmt1 = Parse(missingPlaceholders).Statements.OfType<CreateVisualStatement>().Single();
        var manifest1 = new VisualManifest { Name = stmt1.Name, VisualType = "MAP", Columns = ["Latitude", "Longitude"], Rows = [] };
        var ex1 = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(stmt1, manifest1));
        Assert.Contains("Must contain '{z}', '{x}', and '{y}' placeholders", ex1.Message);

        const string nonHttpScheme = """
CREATE VISUAL BadMap2 AS MAP (
    SOURCE = #locations,
    MAPPINGS (LAT = Latitude, LON = Longitude),
    OPTIONS (BASE_MAP = 'ftp://tiles.internal/{z}/{x}/{y}.png')
);
""";
        var stmt2 = Parse(nonHttpScheme).Statements.OfType<CreateVisualStatement>().Single();
        var manifest2 = new VisualManifest { Name = stmt2.Name, VisualType = "MAP", Columns = ["Latitude", "Longitude"], Rows = [] };
        var ex2 = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(stmt2, manifest2));
        Assert.Contains("Must be a valid HTTP or HTTPS URL template", ex2.Message);
    }

    [Fact]
    public void Trellis_ScaleSynchronization_Options_LowersScaleResolutions()
    {
        const string sql = """
CREATE VISUAL RegionTrellis AS TRELLIS (
    SOURCE = #sales,
    MAPPINGS (X = Category, Y = Revenue, FACET = Region),
    OPTIONS (
        SHARED_X = OFF,
        SHARED_Y = ON,
        SHARED_COLOR = OFF
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

        var manifest = new VisualManifest
        {
            Name = stmt.Name,
            VisualType = "TRELLIS",
            Columns = ["Category", "Revenue", "Region"],
            Rows = [["Electronics", "500", "North"], ["Furniture", "300", "South"]],
            Options = stmt.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase)
        };
        var spec = new NamedVisualChartLowerer().Lower(stmt, manifest);

        Assert.NotNull(spec.Facet);
        Assert.Equal(ScaleResolutionMode.Independent, spec.Facet.Resolution.X);
        Assert.Equal(ScaleResolutionMode.Shared, spec.Facet.Resolution.Y);
        Assert.Equal(ScaleResolutionMode.Independent, spec.Facet.Resolution.Color);
    }

    [Fact]
    public void Trellis_SharedAxis_SynonymForSharedY()
    {
        const string sql = """
CREATE VISUAL RegionTrellis AS TRELLIS (
    SOURCE = #sales,
    MAPPINGS (X = Category, Y = Revenue, FACET = Region),
    OPTIONS (
        SHARED_AXIS = OFF
    )
);
""";
        var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
        var manifest = new VisualManifest
        {
            Name = stmt.Name,
            VisualType = "TRELLIS",
            Columns = ["Category", "Revenue", "Region"],
            Rows = [["A", "10", "R1"]],
            Options = stmt.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase)
        };
        var spec = new NamedVisualChartLowerer().Lower(stmt, manifest);

        Assert.NotNull(spec.Facet);
        Assert.Equal(ScaleResolutionMode.Independent, spec.Facet.Resolution.Y);
    }

    [Fact]
    public void Trellis_Options_OnNonTrellis_Throws()
    {
        const string sql = """
CREATE VISUAL BadBar AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Category, Y = Revenue),
    OPTIONS (SHARED_X = OFF)
);
""";
        var stmt = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();
        var manifest = new VisualManifest { Name = stmt.Name, VisualType = "BAR", Columns = ["Category", "Revenue"], Rows = [] };
        var ex = Assert.Throws<InvalidOperationException>(() => new NamedVisualChartLowerer().Lower(stmt, manifest));
        Assert.Contains("supported only on TRELLIS visuals", ex.Message);
    }

    [Fact]
    public void CustomChart_Resolve_AlignedNaming_ParsesAndResolves()
    {
        const string sql = """
CREATE VISUAL CustomMultiples AS CUSTOM (
    SOURCE = #sales,
    CHART (
        COORDINATE (TYPE = CARTESIAN),
        RESOLVE (
            SHARED_X = OFF,
            SHARED_COLOR = ON
        ),
        FACET (
            WRAP = Region,
            COLUMNS = 2
        ),
        LAYERS (
            bars = RECT (
                ENCODINGS (
                    X = Category (TYPE = ORDINAL),
                    Y = Revenue (TYPE = QUANTITATIVE)
                )
            )
        )
    )
);
""";
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.NotNull(stmt.AdvancedChart);
        Assert.Equal(AdvancedChartResolutionMode.Independent, stmt.AdvancedChart.Resolution.X);
        Assert.Equal(AdvancedChartResolutionMode.Shared, stmt.AdvancedChart.Resolution.Color);
    }
}
