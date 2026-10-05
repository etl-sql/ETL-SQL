using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Services;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.TerminalSemantics;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

[Trait("CompatBreak", "0.20.0")]
public sealed class TransposedAspectAreaTests
{
    private const string Script = """
        CREATE VISUAL Envelope AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MIN = 0, MAX = 1000),
                    distances = LINEAR (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10),
                    estimates = LINEAR (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10)),
            LAYERS (envelope = AREA (
              INHERIT_ENCODINGS = OFF,
              NULL_HANDLING = GAP,
              ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                         Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),
                         Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
              STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233')
            ))
          )
        );
        """;

    private static string Form(bool ribbon)
    {
        var script = Script.Replace("\r\n", "\n", StringComparison.Ordinal);
        return ribbon ? script : script.Replace("NULL_HANDLING = GAP,", "NULL_HANDLING = GAP, AREA_BASELINE = ZERO,", StringComparison.Ordinal)
            .Replace("Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates),\n                 Y_END = UpperBound", "Y = UpperBound", StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> GeometryCases()
    {
        foreach (var ribbon in new[] { false, true })
            foreach (var reverse in new[] { false, true })
                foreach (var logarithmic in new[] { false, true })
                    foreach (var facets in new[] { false, true })
                        yield return [ribbon, reverse, logarithmic, facets];
    }

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public void Areas_FollowPointEndpointsSharedScalesAndSourceOrder(bool ribbon, bool reverse, bool logarithmic, bool facets)
    {
        var sql = Form(ribbon);
        if (reverse) sql = sql.Replace("INCLUDE_ZERO = OFF, MIN", "REVERSE = ON, INCLUDE_ZERO = OFF, MIN", StringComparison.Ordinal);
        if (logarithmic)
        {
            sql = sql.Replace("distances = LINEAR", "distances = LOGARITHMIC", StringComparison.Ordinal);
            if (ribbon) sql = sql.Replace("estimates = LINEAR", "estimates = LOGARITHMIC", StringComparison.Ordinal);
        }
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data);
        Assert.Equal(new ResolvedCartesianAxes("distances", "estimates"), original.CartesianAxes);
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var paths = Paths(plan);
            Assert.Equal(facets ? 2 : 1, paths.Length);
            foreach (var (frame, scales) in facets ? plan.Facets.Select(panel => (panel.CartesianViewport!, panel.Scales)) : [(plan.CartesianViewport!, plan.Scales)])
            {
                decimal Span(string id)
                {
                    var scale = scales.Single(scale => scale.Id == id);
                    var minimum = PlotPlanResolver.Number(scale.Domain[0])!.Value;
                    var maximum = PlotPlanResolver.Number(scale.Domain[^1])!.Value;
                    return scale.Kind == ScaleKind.Logarithmic ? (decimal)Math.Log10((double)(maximum / minimum)) : maximum - minimum;
                }
                Assert.Equal(2m, Math.Round(((frame.Width - 80m) / Span("estimates")) / ((frame.Height - 100m) / Span("distances")), 10));
            }
            var upper = OraclePoints(plan, ribbon ? FieldChannel.YEnd : FieldChannel.Y);
            var lower = OraclePoints(plan, ribbon ? FieldChannel.YStart : null);
            for (var panel = 0; panel < paths.Length; panel++)
            {
                var top = facets ? upper.Skip(panel * 3).Take(3) : upper;
                var bottom = facets ? lower.Skip(panel * 3).Take(3) : lower;
                var expected = "M " + string.Join(" L ", top.Select(Coordinates)) + " L " + string.Join(" L ", bottom.Reverse().Select(Coordinates)) + " Z";
                Assert.Equal(expected, paths[panel].Attribute("d")!.Value);
                Assert.Contains("Row", paths[panel].Value);
                Assert.Contains("X ", paths[panel].Value);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            var trimmed = plan with
            {
                Scales = plan.Scales.Where(scale => !scale.Id.StartsWith("unused", StringComparison.Ordinal)).ToImmutableArray(),
                Facets = plan.Facets.Select(panel => panel with { Scales = panel.Scales.Where(scale => !scale.Id.StartsWith("unused", StringComparison.Ordinal)).ToImmutableArray() }).ToImmutableArray()
            };
            Assert.Equal(new SvgChartRenderer().Render(trimmed), new SvgChartRenderer().Render(plan));
            Assert.Equal(6, plan.Fallback.Items.Length);
            Assert.Contains(ribbon ? "X 8; Y 1 to 4" : "X 8; Y 0 to 4", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        }
    }

    [Theory]
    [InlineData(true, "Distance")]
    [InlineData(true, "LowerBound")]
    [InlineData(true, "UpperBound")]
    [InlineData(false, "Distance")]
    [InlineData(false, "UpperBound")]
    public void MissingEndpoints_BreakPolygonsAndRetainRawRows(bool ribbon, string field)
    {
        var (spec, data) = Lower(Form(ribbon));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == field ? column with
            { Values = column.Values.SetItem(2, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(2, Paths(plan).Length);
        Assert.Equal(6, plan.Layers[0].Data.Length);
        Assert.Equal(new[] { 2 }, plan.Nulls.GapRows.ToArray());
        Assert.DoesNotContain(Paths(plan), path => path.Value.Contains("Row 3", StringComparison.Ordinal));
        Assert.Contains(plan.Fallback.Items, item => item.Value == "gap");
        Assert.Contains("gap", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 6)]
    [InlineData(false, 6)]
    public void EmptySingletonAndAllNullData_HaveNoInventedPolygons(bool ribbon, int rows)
    {
        var (spec, data) = Lower(Form(ribbon));
        data = data with
        {
            RowCount = rows,
            Columns = data.Columns.Select(column => column with
            { Values = column.Values.Take(rows).Select(value => rows == 6 && column.Name == "Distance" ? ChartValue.Null() : value).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Empty(Paths(plan));
        Assert.Equal(rows, plan.Fallback.Items.Length);
        Assert.Equal(rows, plan.Layers[0].Data.Length);
        Assert.Equal(new SvgChartRenderer().Render(plan), new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SignedCrossingAndCoincidentBounds_KeepRawCoordinates(bool ribbon)
    {
        var (spec, data) = Lower(Form(ribbon));
        spec = spec with { Scales = spec.Scales.Select(scale => scale.Id == "estimates" ? scale with { DomainMinimum = ChartValue.From(-10m), DomainMaximum = ChartValue.From(10m) } : scale).ToImmutableArray() };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "LowerBound" ? column with
            { Values = [ChartValue.From(5m), ChartValue.From(-3m), ChartValue.From(4m), ChartValue.From(1m), ChartValue.From(-1m), ChartValue.From(6m)], DisplayValues = [] } : column.Name == "UpperBound" ? column with
            { Values = [ChartValue.From(-2m), ChartValue.From(-3m), ChartValue.From(8m), ChartValue.From(-5m), ChartValue.From(1m), ChartValue.From(6m)], DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var upper = OraclePoints(plan, ribbon ? FieldChannel.YEnd : FieldChannel.Y);
        var lower = OraclePoints(plan, ribbon ? FieldChannel.YStart : null);
        Assert.Equal("M " + string.Join(" L ", upper.Select(Coordinates)) + " L " + string.Join(" L ", lower.Reverse().Select(Coordinates)) + " Z", Assert.Single(Paths(plan)).Attribute("d")!.Value);
        Assert.Contains(ribbon ? "Y 5 to -2" : "Y 0 to -2", plan.Fallback.Items[0].Value);
        if (!ribbon) Assert.True(plan.Scales.Single(scale => scale.Id == "estimates").IncludesZero);
    }

    [Theory]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = CONNECT")]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = ZERO")]
    [InlineData("INTERPOLATION = 'LINEAR'", "INTERPOLATION = 'MONOTONE'")]
    [InlineData("Y_START = LowerBound", "COLOR = Cohort (TYPE = NOMINAL), Y_START = LowerBound")]
    [InlineData("NULL_HANDLING = GAP,", "NULL_HANDLING = GAP, AREA_BASELINE = ZERO,")]
    [InlineData("Y_START = LowerBound", "Y_START = VALUE(1)")]
    public void UnsupportedAreaForms_HavePositionedDiagnostics(string before, string after)
    {
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(Script.Replace(before, after, StringComparison.Ordinal))), diagnostic =>
            diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("ASPECT_RATIO AREA", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedAxesAndLogZeroBaseline_AreRejectedBeforeRendering()
    {
        var mismatched = Script.Replace("Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)", "Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = unusedY)", StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(mismatched)), diagnostic => diagnostic.Message.Contains("shared quantitative primary scale", StringComparison.Ordinal));
        var log = Form(false).Replace("estimates = LINEAR", "estimates = LOGARITHMIC", StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(log)), diagnostic => diagnostic.Message.Contains("ASPECT_RATIO AREA", StringComparison.Ordinal));
        var (spec, _) = Lower(Script);
        var invalid = spec with { Layers = [spec.Layers[0] with { Bindings = spec.Layers[0].Bindings.Select(binding => binding.Channel == FieldChannel.X ? binding with { ScaleId = "unusedX" } : binding).ToImmutableArray() }] };
        Assert.Throws<InvalidDataException>(invalid.Validate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthoringContractsLineageAndBackends_RetainAreaSemantics(bool ribbon)
    {
        var sql = Form(ribbon);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Envelope)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in ribbon ? new[] { "Distance", "LowerBound", "UpperBound" } : new[] { "Distance", "UpperBound" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Envelope" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        Assert.Equal(5, spec.Version);
        Assert.Throws<InvalidDataException>((spec with { Schema = ChartContractVersions.ChartSpecSchema, Version = 2 }).Validate);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(8, plan.Version);
        Assert.Throws<InvalidDataException>((plan with { Schema = ChartContractVersions.PlotPlanSchema, Version = 3 }).Validate);
        Assert.Throws<InvalidDataException>((plan with { CartesianAxes = null }).Validate);
        Assert.Throws<InvalidDataException>((plan with { CartesianAxes = new ResolvedCartesianAxes("unusedX", "estimates") }).Validate);
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Equal(svg, new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        var report = new ReportManifest { Title = "Envelope", Source = "area.rptsql", Visuals = [new VisualManifest { Name = "Envelope", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }] };
        var browser = BrowserDeliveryProjection.Serialize(report);
        using (var delivery = System.Text.Json.JsonDocument.Parse(browser))
            Assert.Equal(svg, delivery.RootElement.GetProperty("visuals")[0].GetProperty("nativeSvg").GetString());
        Assert.DoesNotContain("\"plotPlan\"", browser, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
        Assert.Single(PlotPlanConformanceHarness.Evaluate(plan, [new LostAxesBackend()]).Issues, issue => issue.SemanticArea == "axes");
    }

    [Theory]
    [InlineData(true, "B0306428BB061998D685DF0DFB0FE78309BBFB372A71B8D381D6FF9D8349BDE6", "32FF220F887E85215CA25075B08981780B4C661CF0BD25B7A4EA06A302CD6664")]
    [InlineData(false, "CC09A109FB0D8929ECA5583247734538AE417CC02B652EAD57E8AB072EA6E8F8", "065D5049EE37B5D6832042D98DAB47B9A28EFA6B32B573FD3CC180F1FEDF74F9")]
    public void PlansAndSvg_MatchDeterministicGoldens(bool ribbon, string expectedPlan, string expectedSvg)
    {
        var (spec, data) = Lower(Form(ribbon));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg, $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void LegacyTransposedRibbons_EmitSharedScaleGeometryAndRawIntervals(bool categorical, bool reverse, bool gap)
    {
        var sql = Script.Replace(", ASPECT_RATIO = 2", "", StringComparison.Ordinal)
            .Replace("unusedX = BAND (CHANNEL = X), ", "", StringComparison.Ordinal);
        if (categorical) sql = sql.Replace("distances = LINEAR (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10)", "distances = BAND (CHANNEL = X)", StringComparison.Ordinal)
            .Replace("X = Distance (TYPE = QUANTITATIVE", "X = Distance (TYPE = NOMINAL", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        spec = spec with { Scales = spec.Scales.Select(scale => scale.Id == "distances" || scale.Id == "estimates" ? scale with { Reverse = reverse } : scale).ToImmutableArray() };
        if (gap) data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "LowerBound" ? column with
            { Values = column.Values.SetItem(2, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(2, spec.Version);
        Assert.Equal(6, plan.Version);
        Assert.Null(plan.CartesianAxes);
        var svg = new SvgChartRenderer().Render(plan);
        var paths = Paths(plan);
        Assert.Equal(gap ? 2 : 1, paths.Length);
        var yScale = plan.Scales.Single(scale => scale.Id == "estimates");
        var xScale = plan.Scales.Single(scale => scale.Id == "distances");
        string Point(ResolvedDatum datum, FieldChannel bound)
        {
            var yValue = PlotPlanResolver.Number(datum.Channels.Single(value => value.Channel == bound).Value)!.Value;
            var yRatio = (yValue - 1m) / 9m;
            var x = 60m + (reverse ? 1m - yRatio : yRatio) * (plan.Bounds.Width - 80m);
            var xValue = datum.Channels.Single(value => value.Channel == FieldChannel.X).Value;
            var ratio = categorical ? (xScale.Categories.IndexOf(PlotPlanResolver.Display(xValue)) + .5m) / 6m : (PlotPlanResolver.Number(xValue)!.Value - 1m) / 9m;
            var y = 40m + (reverse ? 1m - ratio : ratio) * (plan.Bounds.Height - 100m);
            return $"{x.ToString("0.###", CultureInfo.InvariantCulture)} {y.ToString("0.###", CultureInfo.InvariantCulture)}";
        }
        var segments = gap ? new[] { plan.Layers[0].Data.Take(2), plan.Layers[0].Data.Skip(3) } : new[] { plan.Layers[0].Data.AsEnumerable() };
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            Assert.Equal("M " + string.Join(" L ", segment.Select(datum => Point(datum, FieldChannel.YEnd))) + " L " +
                string.Join(" L ", segment.Reverse().Select(datum => Point(datum, FieldChannel.YStart))) + " Z", paths[index].Attribute("d")!.Value);
        }
        Assert.Contains("X 8; Y 1 to 4", plan.Fallback.Items[0].Value);
        Assert.Contains("X 8; Y 1 to 4", TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        Assert.Equal(svg, new SvgChartRenderer().Render(plan with { Scales = plan.Scales.Where(scale => scale.Id != "unusedY").ToImmutableArray() }));
        Assert.Equal(svg, new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        Assert.Equal(9m, PlotPlanResolver.Number(yScale.Domain[^1]) - PlotPlanResolver.Number(yScale.Domain[0]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InferredAndConstantCoordinates_KeepCoincidentSourceRows(bool ribbon)
    {
        var sql = Form(ribbon).Replace("X = Distance", "X = DATUM(3)", StringComparison.Ordinal)
            .Replace("Y_START = LowerBound", "Y_START = DATUM(5)", StringComparison.Ordinal)
            .Replace("Y_END = UpperBound", "Y_END = DATUM(5)", StringComparison.Ordinal)
            .Replace("Y = UpperBound", "Y = DATUM(5)", StringComparison.Ordinal)
            .Replace(", SCALE = distances", "", StringComparison.Ordinal).Replace(", SCALE = estimates", "", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.StartsWith("inferred-", plan.CartesianAxes!.XScaleId);
        Assert.Equal(6, plan.Layers[0].Data.Length);
        Assert.Single(Paths(plan));
        Assert.Single(OraclePoints(plan, ribbon ? FieldChannel.YEnd : FieldChannel.Y).Select(Coordinates).Distinct());
        Assert.Equal(6, plan.Fallback.Items.Length);
    }

    [Fact]
    public void MixedMarks_AreaKeepsOneGeometryAndSharedPhysicalOffsets()
    {
        var (spec, data) = Lower(Form(false));
        var area = spec.Layers[0];
        var point = area with
        {
            Id = "observations",
            Mark = MarkKind.Point,
            ZIndex = 1,
            Style = [],
            Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 1m, -2m, Unit: PositionAdjustmentUnit.Data),
            Bindings = [.. area.Bindings, new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal)]
        };
        var reference = new MarkLayerSpec("threshold", MarkKind.Rule, 3, [FieldBinding.Datum(FieldChannel.Y, ChartValue.From(4m), DataSemanticKind.Quantitative, "estimates")], []);
        spec = spec with { Bindings = [.. spec.Bindings, point.Bindings[^1]], Layers = [area, point, reference] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Area);
        Assert.Equal(6, plan.Layers.Single(layer => layer.Mark == MarkKind.Area).Data.Length);
        Assert.Single(Paths(plan));
        var pointDatum = plan.Layers.First(layer => layer.Mark == MarkKind.Point).Data[0];
        var viewport = plan.CartesianViewport!;
        Assert.Equal(-2m * (viewport.Width - 80m) / 10m, pointDatum.DisplayOffsetX, 10);
        Assert.Equal(-(viewport.Height - 100m) / 9m, pointDatum.DisplayOffsetY, 10);
        var trimmed = spec with { Scales = spec.Scales.Where(scale => !scale.Id.StartsWith("unused", StringComparison.Ordinal)).ToImmutableArray() };
        Assert.Equal(new SvgChartRenderer().Render(plan), new SvgChartRenderer().Render(new PlotPlanResolver().Resolve(trimmed, data)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndependentAutoDomains_IncludeBothBoundsOrZero(bool ribbon)
    {
        var sql = Form(ribbon).Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal)
            .Replace(", MIN = 1, MAX = 10", "", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        foreach (var panel in plan.Facets)
        {
            var scale = panel.Scales.Single(scale => scale.Id == "estimates");
            Assert.Equal(!ribbon, scale.IncludesZero);
            Assert.True(PlotPlanResolver.Number(scale.Domain[0]) <= (ribbon ? 1m : 0m));
            Assert.True(PlotPlanResolver.Number(scale.Domain[^1]) >= (panel.RowIndices[0] == 0 ? 9m : ribbon ? 8m : 6m));
        }
    }

    [Fact]
    public async Task LegacyTemporalTransposedRibbon_MapsDatesAndExportsStaticGeometry()
    {
        var sql = Script.Replace(", ASPECT_RATIO = 2", "", StringComparison.Ordinal).Replace("unusedX = BAND (CHANNEL = X), ", "", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var dates = new[] { 8, 2, 5, 1, 9, 4 }.Select(day => ChartValue.From(new DateOnly(2026, 1, day))).ToImmutableArray();
        spec = spec with
        {
            Scales = spec.Scales.Select(scale => scale.Id == "distances" ? scale with
            { Kind = ScaleKind.Time, DomainMinimum = null, DomainMaximum = null } : scale).ToImmutableArray(),
            Layers = [spec.Layers[0] with { Bindings = spec.Layers[0].Bindings.Select(binding => binding.Channel == FieldChannel.X ? binding with { SemanticKind = DataSemanticKind.Temporal } : binding).ToImmutableArray() }],
            Bindings = spec.Bindings.Select(binding => binding.Channel == FieldChannel.X ? binding with { SemanticKind = DataSemanticKind.Temporal } : binding).ToImmutableArray()
        };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Distance" ? column with
            { ValueKind = ChartValueKind.Date, SemanticKind = DataSemanticKind.Temporal, Values = dates, DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var path = Assert.Single(Paths(plan));
        Assert.StartsWith("M 233.333 258.75", path.Attribute("d")!.Value);
        Assert.Contains("X 2026-01-08; Y 1 to 4", plan.Fallback.Items[0].Value);
        var report = new ReportManifest
        {
            Title = "Dates",
            Source = "dates.rptsql",
            Visuals = [new VisualManifest
        { Name = "Envelope", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void MalformedAreaPlans_RejectAbsentEndpointsAndZeroBaseline()
    {
        var (spec, data) = Lower(Script);
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        var layer = plan.Layers[0];
        Assert.Throws<InvalidDataException>((plan with { Layers = [layer with { AreaRibbon = false }] }).Validate);
        Assert.Throws<InvalidDataException>((plan with
        {
            Layers = [layer with { Data = layer.Data.SetItem(0, layer.Data[0] with
        { Channels = layer.Data[0].Channels.Where(value => value.Channel != FieldChannel.YEnd).ToImmutableArray() }) }]
        }).Validate);
        var zero = resolver.Resolve(Lower(Form(false)).Spec, data);
        Assert.Throws<InvalidDataException>((zero with { Scales = zero.Scales.Select(scale => scale.Id == "estimates" ? scale with { IncludesZero = false } : scale).ToImmutableArray() }).Validate);
        spec = spec with { Scales = spec.Scales.Select(scale => scale.Id == "estimates" ? scale with { Kind = ScaleKind.Logarithmic } : scale).ToImmutableArray() };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "LowerBound" ? column with
            { Values = column.Values.SetItem(0, ChartValue.From(0m)), DisplayValues = [] } : column).ToImmutableArray()
        };
        Assert.Contains("requires positive", Assert.Throws<InvalidOperationException>(() => resolver.Resolve(spec, data)).Message);
    }

    private sealed class LostAxesBackend : IPlotPlanSemanticBackend
    {
        public string Name => "lost-axes";
        public PlotSemanticProjection Project(PlotPlan plan) => PlotSemanticProjection.FromPlan(plan) with { CartesianAxes = null };
    }

    private static string Coordinates(XElement point) => $"{point.Attribute("cx")!.Value} {point.Attribute("cy")!.Value}";
    private static XElement[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") is "plot-area" or "plot-ribbon").ToArray();
    private static XElement[] OraclePoints(PlotPlan plan, FieldChannel? bound)
    {
        var area = plan.Layers[0];
        var points = area with
        {
            Id = "oracle",
            Mark = MarkKind.Point,
            ZIndex = 1,
            AreaRibbon = false,
            AreaRibbonScaleId = null,
            Style = [],
            Data = area.Data.Select(datum => datum with
            {
                Channels = [datum.Channels.Single(value => value.Channel == FieldChannel.X),
                new ResolvedChannelValue(FieldChannel.Y, bound is null ? ChartValue.From(0m) : datum.Channels.Single(value => value.Channel == bound).Value, null)]
            }).ToImmutableArray()
        };
        return XDocument.Parse(new SvgChartRenderer().Render(plan with { Layers = [area with { Data = [] }, points] })).Descendants()
            .Where(element => (string?)element.Attribute("class") == "plot-point").ToArray();
    }

    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }
    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql)
    {
        var statement = Parse(sql);
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Envelope",
            Columns = ["Distance", "LowerBound", "UpperBound", "Cohort"],
            Rows = [["8", "1", "4", "A"], ["2", "7", "2", "A"], ["5", "3", "9", "A"], ["1", "8", "3", "B"], ["9", "2", "6", "B"], ["4", "6", "1", "B"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
