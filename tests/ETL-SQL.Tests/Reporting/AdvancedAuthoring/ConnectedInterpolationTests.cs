using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

public sealed class ConnectedInterpolationTests
{
    private static string Sql(string form, string interpolation, string policy, string coordinate, bool grouped, bool facets, bool decorated) => $$"""
        CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = {{coordinate}}),
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MIN = 100, MAX = 200),
            horizontal = LINEAR (CHANNEL = X, MIN = 0, MAX = 10), vertical = LINEAR (CHANNEL = Y, MIN = -10, MAX = 20)),
          {{(facets ? "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT)," : "")}}
          LAYERS (route = {{(form == "LINE" ? "LINE" : "AREA")}} (NULL_HANDLING = {{policy}},
            {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
              {{(form == "RIBBON" ? "Y_START = Lower (TYPE = QUANTITATIVE, SCALE = vertical), Y_END = Upper (TYPE = QUANTITATIVE, SCALE = vertical)" : "Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical)")}}
              {{(grouped ? ", COLOR = Series (TYPE = NOMINAL)" : "")}}),
            STYLE (INTERPOLATION = '{{interpolation}}', COLOR = '#112233'),
            CONDITIONS (COLOR WHEN Flag = 'red' THEN '#ff0000', COLOR WHEN Flag = 'blue' THEN '#0000ff',
              OPACITY WHEN Flag = 'red' THEN 0 ELSE 1
              {{(decorated ? ", SIZE WHEN Flag = 'red' THEN 7 ELSE 2, SHAPE WHEN Flag = 'red' THEN 'SQUARE' ELSE 'CIRCLE', TEXT WHEN Flag = 'red' THEN '<endpoint & raw>' ELSE NULL" : "")}})))
        ));
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var interpolation in new[] { "SMOOTH", "STEP_BEFORE", "STEP_AFTER" })
                foreach (var policy in new[] { "GAP", "CONNECT", "ZERO" })
                    foreach (var coordinate in new[] { "CARTESIAN", "TRANSPOSED_CARTESIAN", "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2" })
                        foreach (var profile in new[] { "NONE", "SERIES", "FACETS", "BOTH" })
                            foreach (var reverse in new[] { false, true })
                                foreach (var decorated in new[] { false, true }) yield return [form, interpolation, policy, coordinate, profile, reverse, decorated];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativePathsPreserveInterpolationOwnershipAndEligibleRunNeighbors(string form, string interpolation, string policy, string coordinate, string profile, bool reverse, bool decorated)
    {
        var grouped = profile is "SERIES" or "BOTH";
        var facets = profile is "FACETS" or "BOTH";
        var (spec, data) = Lower(Sql(form, interpolation, policy, coordinate, grouped, facets, decorated));
        spec = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = reverse }).ToImmutableArray() };
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 900m, 600m));
        Assert.Equal(10, spec.Version);
        Assert.Equal(13, plan.Version);
        Assert.Equal(new ResolvedCartesianAxes("horizontal", "vertical"), plan.CartesianAxes);
        Assert.Equal(16, plan.Layers.Sum(layer => layer.Data.Length));
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var paths = document.Descendants().Where(element => (string?)element.Attribute("class") is "plot-conditional-connection" or "plot-conditional-area").ToArray();
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.Length), paths.Length);
        bool Complete(int row) => row != 6 && (form == "RIBBON" ? row is not (4 or 10) : row != 4 || policy == "ZERO");
        decimal Raw(int row, string field) => PlotPlanResolver.Number(data.Columns.Single(column => column.Name == field).Values[row]) ?? 0m;
        ResolvedConnectionPoint Point(int row, bool lower) => new(Raw(row, "Distance"), lower ? form == "RIBBON" ? Raw(row, "Lower") : 0m : Raw(row, form == "RIBBON" ? "Upper" : "Estimate"));
        foreach (var layer in plan.Layers)
        {
            Assert.Equal(interpolation, ResolvedConnectionGeometry.Name(layer.ConnectionInterpolation!.Value));
            foreach (var connection in layer.Connections)
            {
                var partition = Enumerable.Range(0, 16).Where(row => (!grouped || row % 2 == connection.SourceRowIndex % 2) &&
                    (!facets || row % 4 < 2 == connection.SourceRowIndex % 4 < 2)).ToArray();
                var eligible = partition.Where(Complete).ToArray();
                var sourcePosition = Array.IndexOf(eligible, connection.SourceRowIndex);
                Assert.Equal(eligible[sourcePosition + 1], connection.DestinationRowIndex);
                bool Continuous(int start, int end) => policy == "CONNECT" || !partition.Any(row => row > start && row < end && !Complete(row));
                Assert.True(Continuous(connection.SourceRowIndex, connection.DestinationRowIndex));
                var before = sourcePosition > 0 && Continuous(eligible[sourcePosition - 1], connection.SourceRowIndex) ? eligible[sourcePosition - 1] : connection.SourceRowIndex;
                var after = sourcePosition + 2 < eligible.Length && Continuous(connection.DestinationRowIndex, eligible[sourcePosition + 2]) ? eligible[sourcePosition + 2] : connection.DestinationRowIndex;
                var expectedCubic = interpolation == "SMOOTH" && (before != connection.SourceRowIndex || after != connection.DestinationRowIndex);
                ImmutableArray<ResolvedConnectionPoint> Boundary(bool lower)
                {
                    var first = Point(connection.SourceRowIndex, lower);
                    var last = Point(connection.DestinationRowIndex, lower);
                    if (interpolation == "STEP_BEFORE") return [first, new(first.X, last.Y), last];
                    if (interpolation == "STEP_AFTER") return [first, new(last.X, first.Y), last];
                    if (!expectedCubic) return [first, last];
                    var previous = Point(before, lower);
                    var next = Point(after, lower);
                    return [first, new(first.X + (last.X - previous.X) / 6m, first.Y + (last.Y - previous.Y) / 6m),
                        new(last.X - (next.X - first.X) / 6m, last.Y - (next.Y - first.Y) / 6m), last];
                }
                var geometry = Assert.IsType<ResolvedConnectionGeometry>(connection.Geometry);
                Assert.Equal(expectedCubic, geometry.Cubic);
                Assert.Equal(Boundary(false).ToArray(), geometry.Upper.ToArray());
                if (form == "LINE") Assert.True(geometry.Lower.IsDefault);
                else Assert.Equal(Boundary(true).ToArray(), geometry.Lower.ToArray());
                var panel = plan.Facets.FirstOrDefault(panel => panel.Id == connection.FacetId);
                var frame = panel?.CartesianViewport ?? panel?.Bounds ?? plan.CartesianViewport ?? plan.Bounds;
                var transposed = coordinate.StartsWith("TRANSPOSED", StringComparison.Ordinal);
                string Mapped(ResolvedConnectionPoint point)
                {
                    decimal Map(decimal value, decimal min, decimal max, bool vertical)
                    {
                        var ratio = (value - min) / (max - min);
                        if (reverse) ratio = 1m - ratio;
                        return vertical ? 40m + (1m - ratio) * (frame.Height - 100m) : 60m + ratio * (frame.Width - 80m);
                    }
                    return N(Map(transposed ? point.Y : point.X, transposed ? -10m : 0m, transposed ? 20m : 10m, false)) + " " +
                        N(Map(transposed ? point.X : point.Y, transposed ? 0m : -10m, transposed ? 10m : 20m, true));
                }
                string Trace(ImmutableArray<ResolvedConnectionPoint> boundary, bool reversePath, string command)
                {
                    var values = reversePath ? boundary.Reverse().ToArray() : boundary.ToArray();
                    return command + " " + Mapped(values[0]) + (expectedCubic ? " C " + string.Join(" ", values.Skip(1).Select(Mapped)) : string.Concat(values.Skip(1).Select(point => " L " + Mapped(point))));
                }
                var path = Assert.Single(paths, path => path.Value == ConnectedMarkResolver.Describe(connection, plan, layer, includeOwner: false, includeInterpolation: false));
                Assert.Equal(Trace(Boundary(false), false, "M") + (form == "LINE" ? "" : " " + Trace(Boundary(true), true, "L") + " Z"), path.Attribute("d")!.Value);
                Assert.Contains("Layer " + layer.Id, path.Parent!.Attribute("aria-label")!.Value);
                Assert.Contains("interpolation " + interpolation, path.Parent.Attribute("aria-label")!.Value);
                Assert.Equal(connection.Encodings.FirstOrDefault(value => value.Channel == ConditionalEncodingChannel.Color)?.Value.Text ?? "#112233", path.Attribute(form == "LINE" ? "stroke" : "fill")!.Value);
                Assert.Equal(connection.SourceRowIndex < 4 ? "0" : "1", (string?)path.Attribute("opacity") ?? "1");
                Assert.All(connection.Encodings, value => Assert.True(value.Channel is ConditionalEncodingChannel.Color or ConditionalEncodingChannel.Opacity));
            }
        }
        var roundTrip = ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan));
        Assert.Equal(new SvgChartRenderer().Render(plan), new SvgChartRenderer().Render(roundTrip));
        Assert.Empty(PlotPlanConformanceHarness.Evaluate(plan, [new RoundTripBackend(roundTrip)]).Issues);
        var resized = resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 700m, 800m));
        Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, resized.Bounds)), ChartContractSerializer.Serialize(resized));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText;
        Assert.Contains("interpolation " + interpolation, terminal);
        Assert.Contains(plan.Fallback.Items, item => item.Value.Contains("interpolation " + interpolation, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void LostInventedOrDowngradedGeometryIsRejected(string interpolation)
    {
        var (spec, data) = Lower(Sql("RIBBON", interpolation, "CONNECT", "TRANSPOSED_CARTESIAN", true, true, true));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Throws<InvalidDataException>((spec with { Version = 9, Schema = ChartContractVersions.DecoratedConnectedChartSpecSchema }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Version = 12, Schema = ChartContractVersions.DecoratedConnectedPlotPlanSchema }).Validate);
        var layer = plan.Layers[0];
        var connection = layer.Connections[0];
        var geometry = connection.Geometry!;
        foreach (var changed in new ResolvedConnectionGeometry?[] { null, geometry with { Upper = geometry.Upper.SetItem(0, new(999m, 999m)) },
            geometry with { Lower = default }, geometry with { Cubic = !geometry.Cubic }, geometry with { Upper = [] } })
            Assert.Throws<InvalidDataException>((plan with { Layers = plan.Layers.SetItem(0, layer with { Connections = layer.Connections.SetItem(0, connection with { Geometry = changed }) }) }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Layers = plan.Layers.SetItem(0, layer with { ConnectionInterpolation = null }) }).Validate);
        Assert.Contains(PlotPlanConformanceHarness.Evaluate(plan, [new LostGeometryBackend()]).Issues, issue => issue.SemanticArea == "layers");
    }

    public static IEnumerable<object[]> Forms()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var interpolation in new[] { "SMOOTH", "STEP_BEFORE", "STEP_AFTER" }) yield return [form, interpolation];
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task AuthoringDesignerLineageAndStaticExportsPreserveInterpolation(string form, string interpolation)
    {
        var sql = Sql(form, interpolation, "ZERO", "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", true, true, true);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Route)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in form == "RIBBON" ? new[] { "Distance", "Lower", "Upper", "Flag", "Series", "Cohort" } : new[] { "Distance", "Estimate", "Flag", "Series", "Cohort" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Route" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);
        var report = new ReportManifest { Title = "Interpolated", Source = "interpolated.rptsql", Visuals = [new VisualManifest { Name = "Route", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }] };
        Assert.Contains("interpolation " + interpolation, new MarkdownRenderer().Render(report));
        Assert.Contains("&lt;endpoint &amp; raw&gt;", svg);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void EmptyAllMissingSingletonAndTwoRowRunsRetainDeclaredIntent(string form, string interpolation)
    {
        var (spec, data) = Lower(Sql(form, interpolation, "CONNECT", "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", false, false, true));
        foreach (var count in new[] { 0, 1, 2, 16 })
        {
            var source = data with
            {
                RowCount = count,
                Columns = data.Columns.Select(column => column with
                { Values = column.Values.Take(count).Select(value => count == 16 && column.Name == "Distance" ? ChartValue.Null() : value).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
            };
            var plan = new PlotPlanResolver().Resolve(spec, source);
            var layer = Assert.Single(plan.Layers);
            Assert.Equal(13, plan.Version);
            Assert.Equal(interpolation, ResolvedConnectionGeometry.Name(layer.ConnectionInterpolation!.Value));
            Assert.Equal(count == 2 ? 1 : 0, layer.Connections.Length);
            if (count == 2)
            {
                var geometry = layer.Connections[0].Geometry!;
                Assert.False(geometry.Cubic);
                Assert.Equal(interpolation == "SMOOTH" ? 2 : 3, geometry.Upper.Length);
                Assert.Equal(form == "LINE", geometry.Lower.IsDefault);
            }
            Assert.Equal(count is 1 or 2 ? count : 0, XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Count(element => (string?)element.Attribute("class") == "plot-connection-symbol"));
            Assert.Throws<InvalidDataException>((plan with { Schema = ChartContractVersions.DecoratedConnectedPlotPlanSchema, Version = 12 }).Validate);
            Assert.Throws<InvalidDataException>((plan with { CartesianAxes = null }).Validate);
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        }
    }

    [Theory]
    [InlineData("CARTESIAN")]
    [InlineData("TRANSPOSED_CARTESIAN")]
    [InlineData("TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2")]
    public void MixedLayerInterpolationAndNullPoliciesRemainIndependent(string coordinate)
    {
        var (spec, data) = Lower(Sql("LINE", "SMOOTH", "GAP", coordinate, true, true, true));
        var line = spec.Layers[0];
        MarkLayerSpec Layer(string id, string interpolation, string policy, int priority) => line with
        {
            Id = id,
            ZIndex = priority,
            Style = line.Style.Select(token => token.Name == "INTERPOLATION" ? token with { Value = interpolation } : token.Name == "nullHandling" ? token with { Value = policy } : token).ToImmutableArray()
        };
        spec = spec with { Layers = [line, Layer("stepBefore", "STEP_BEFORE", "CONNECT", 1), Layer("stepAfter", "STEP_AFTER", "ZERO", 2), Layer("straight", "LINEAR", "GAP", 3)] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(64, plan.Layers.Sum(layer => layer.Data.Length));
        Assert.All(plan.Layers.Where(layer => layer.Id.StartsWith("straight", StringComparison.Ordinal)), layer =>
        {
            Assert.Null(layer.ConnectionInterpolation);
            Assert.All(layer.Connections, connection => Assert.Null(connection.Geometry));
        });
        Assert.All(plan.Layers.Where(layer => !layer.Id.StartsWith("straight", StringComparison.Ordinal)), layer => Assert.All(layer.Connections, connection => Assert.NotNull(connection.Geometry)));
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData("TYPE = CARTESIAN", "TYPE = POLAR")]
    [InlineData("TYPE = QUANTITATIVE, SCALE = horizontal", "TYPE = TEMPORAL, SCALE = horizontal")]
    [InlineData("SCALE = vertical)", "SCALE = vertical, STACK = ZERO)")]
    [InlineData("SCALE = vertical)", "SCALE = vertical, AXIS = SECONDARY)")]
    [InlineData("vertical = LINEAR", "vertical = LOGARITHMIC")]
    [InlineData("NULL_HANDLING = GAP", "NULL_HANDLING = SKIP")]
    [InlineData("'SMOOTH'", "'MONOTONE'")]
    public void UnsupportedInterpolationCompositionsHavePositionedDiagnostics(string before, string after)
    {
        var statement = Parse(Sql("LINE", "SMOOTH", "GAP", "CARTESIAN", true, true, true).Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0);
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void GeometryDoesNotChangeRawRowsDomainsOrDecorationAnchors(string form, string interpolation)
    {
        var (spec, data) = Lower(Sql(form, interpolation, "ZERO", "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", true, true, true));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var straightSpec = spec with
        {
            Version = 9,
            Schema = ChartContractVersions.DecoratedConnectedChartSpecSchema,
            Layers = spec.Layers.Select(layer => layer with { Style = layer.Style.Select(token => token.Name == "INTERPOLATION" ? token with { Value = "LINEAR" } : token).ToImmutableArray() }).ToImmutableArray()
        };
        var straight = new PlotPlanResolver().Resolve(straightSpec, data);
        Assert.Equal(JsonSerializer.Serialize(straight.Scales), JsonSerializer.Serialize(plan.Scales));
        Assert.Equal(JsonSerializer.Serialize(straight.Facets), JsonSerializer.Serialize(plan.Facets));
        for (var index = 0; index < plan.Layers.Length; index++)
            Assert.Equal(JsonSerializer.Serialize(straight.Layers[index].Data), JsonSerializer.Serialize(plan.Layers[index].Data));
    }

    [Theory]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void FocusedInterpolationExampleParsesLowersAndResolves(string interpolation)
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var example = Regex.Matches(File.ReadAllText(path), @"```sql\s*(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value.Trim()).Single(block => block.StartsWith("CREATE VISUAL CurvedRoute ", StringComparison.Ordinal))
            .Replace("'SMOOTH'", "'" + interpolation + "'", StringComparison.Ordinal);
        Assert.Equal(Parse(example).ToSql(), Parse(Parse(example).ToSql()).ToSql());
        var (spec, data) = Lower(example);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(10, spec.Version);
        Assert.Equal(13, plan.Version);
        Assert.All(plan.Layers, layer => Assert.Equal(interpolation, ResolvedConnectionGeometry.Name(layer.ConnectionInterpolation!.Value)));
        Assert.NotEmpty(plan.Layers.SelectMany(layer => layer.Connections));
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void ConstantCoincidentEndpointsPreserveEveryRowAndAuthoredBounds(string form, string interpolation)
    {
        var sql = Sql(form, interpolation, "GAP", "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", false, false, true)
            .Replace("X = Distance (", "X = DATUM(3) (", StringComparison.Ordinal)
            .Replace("Y = Estimate (", "Y = DATUM(5) (", StringComparison.Ordinal)
            .Replace("Y_START = Lower (", "Y_START = DATUM(9) (", StringComparison.Ordinal)
            .Replace("Y_END = Upper (", "Y_END = DATUM(2) (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var layer = Assert.Single(plan.Layers);
        Assert.Equal(16, layer.Data.Length);
        Assert.Equal(15, layer.Connections.Length);
        Assert.Equal(Enumerable.Range(0, 15), layer.Connections.Select(connection => connection.SourceRowIndex));
        Assert.All(layer.Connections, connection =>
        {
            Assert.All(connection.Geometry!.Upper, point => Assert.Equal(new(3m, form == "RIBBON" ? 2m : 5m), point));
            if (form != "LINE") Assert.All(connection.Geometry.Lower, point => Assert.Equal(new(3m, form == "RIBBON" ? 9m : 0m), point));
        });
        Assert.All(layer.Data, datum => Assert.Equal(form == "RIBBON" ? 2m : 5m, datum.ConnectionDecoration!.Y));
        Assert.Equal(16, XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Count(element => (string?)element.Attribute("class") == "plot-connection-symbol"));
    }

    [Theory]
    [InlineData("SMOOTH")]
    [InlineData("STEP_BEFORE")]
    [InlineData("STEP_AFTER")]
    public void LineWidthAndDashAreInheritedByEverySourceOwnedPath(string interpolation)
    {
        var sql = Sql("LINE", interpolation, "CONNECT", "CARTESIAN", false, false, false)
            .Replace("COLOR = '#112233'", "COLOR = '#112233', LINE_WIDTH = 3, LINE_DASH = 'DOTTED'", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var document = XDocument.Parse(new SvgChartRenderer().Render(new PlotPlanResolver().Resolve(spec, data)));
        var group = Assert.Single(document.Descendants(), element => (string?)element.Attribute("class") == "plot-connected-layer");
        Assert.Equal("3", group.Attribute("stroke-width")!.Value);
        Assert.NotNull(group.Attribute("stroke-dasharray"));
        Assert.All(group.Elements("path"), path => Assert.Null(path.Attribute("stroke-width")));
    }

    [Theory]
    [InlineData("LINE", "SMOOTH", "CDFF9D4FDFE4DEE17F35FB4F0BFF9835232F796FCA463F0A043880B5A95230C2", "C005B25FDBB11CB90B9751ED43F4B3DD97A6655F09F76F2F6D06550863513A20", "651E42787671E0241AC2F13D1E667BF473F10341196D89D2AF9F102ACDD01488")]
    [InlineData("LINE", "STEP_BEFORE", "90964ABFD3C6C1DB887394CE22E651B9705699A0C0642897C1472AF621148970", "9042266B5E75F71A49E30E677F5CEB2D762D12AA6510D8567FA473211074A7DF", "A8808B891D2D81EF583AD5B9B250D3E0C50443FB34B17ECDE7F94484564C3B11")]
    [InlineData("LINE", "STEP_AFTER", "9E18EDBBF9FCCBAF651DCA493DFB51723D51C88E48D051FDAB0B7A10C29DF298", "73B6DF9906B4CBEEC01D6389580ABE0F9B4CCF30DEEEF93DFBE592D70BF8CA76", "FC154F3968DC8AAF1132DBE0204223DE2414B31C418668038078054982185B84")]
    [InlineData("AREA", "SMOOTH", "32A90D5246EA1396C025E2703128B17933FFF108A52BBA74019C2B8F816C880C", "9F1B77667896266B2FFF7E1D3C2B2C088781E630B7841018289884DF59A2BF0B", "9A5D6AA1D260048F65C1F0844893CCEE07C4633E99D7E23B4034E1225B939B7B")]
    [InlineData("AREA", "STEP_BEFORE", "057AE40C8601B043D9CD7BC5428759DC7B9631D2EB1D2A4DD0FD8EED574381D4", "5A34CC661A4606AF17513B82508EABD6C5FB2B1947AE8A915DD674DFBD029641", "0A178CF113322097D6B523D46DA9282242860D84ECF3A849B73D60BB16A2E219")]
    [InlineData("AREA", "STEP_AFTER", "5CC254D8B7C9223C98F296573E813DE618452EE05ADADC3A481E3BB353E8F3B8", "802288E4AD3EE61C19046710182497752FB099C19FC1B35DBDC6C34A1DD63F0C", "7F7CB456437017B7E6E28E4A790290787D73AB241F6F8345DD9B3D930283275F")]
    [InlineData("RIBBON", "SMOOTH", "8F5C6235460514F20082345B08317B65C8CD6A9C1F643D84DBDD3C44EACF5FDC", "999BC9E21EA63013262B218B71D63E274C6CB116EF3D045E87E2B62109E2DC62", "36E31AF79E1D5EBBC7B8C0DDEBD8C23FC32B4D50E6EE211B25D44CEE9CD54D5B")]
    [InlineData("RIBBON", "STEP_BEFORE", "5BD2F188AE55C8A0A971B2FEF34E7218E27D3139618AB30AB47DE03D69B14339", "26FE30C6D1E871B5EAE308E5FCF8E5140B5ABA12A86D84A7F3418EC4585D3456", "1462F9E7CFBE4DD328CB741E2884D5CDD8323057515780F8FE6862FC0F6BA473")]
    [InlineData("RIBBON", "STEP_AFTER", "E962F5AA4EB76F29373086DA3071C42055DE4A8911311DB294901C7B2AAD8913", "57B135CD42AFD781D9036BA017CF9B5A8CD8470B155C5F494A08F97EBFA9C161", "AC54C64AF44B44096434E3E744268E338224F582E932D7025DC8441265CD785C")]
    public void InterpolatedPlanSvgAndTerminalHaveDeterministicFingerprints(string form, string interpolation, string expectedPlan, string expectedSvg, string expectedTerminal)
    {
        var (spec, data) = Lower(Sql(form, interpolation, "ZERO", "TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2", false, false, true));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        var terminalHash = Hash(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText);
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg && terminalHash == expectedTerminal, $"Plan: {planHash}; SVG: {svgHash}; Terminal: {terminalHash}");
    }

    private static CreateVisualStatement Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return Assert.Single(script.Statements.OfType<CreateVisualStatement>());
    }

    private sealed class LostGeometryBackend : IPlotPlanSemanticBackend
    {
        public string Name => "lost-geometry";
        public PlotSemanticProjection Project(PlotPlan plan) => PlotSemanticProjection.FromPlan(plan) with { Layers = PlotSemanticProjection.FromPlan(plan).Layers.Select(layer => layer with { Connections = layer.Connections.Select(connection => connection with { Geometry = null }).ToImmutableArray() }).ToImmutableArray() };
    }
    private sealed class RoundTripBackend(PlotPlan roundTrip) : IPlotPlanSemanticBackend
    {
        public string Name => "round-trip";
        public PlotSemanticProjection Project(PlotPlan plan) => PlotSemanticProjection.FromPlan(roundTrip);
    }
    private static string N(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static (ChartSpec Spec, ChartDataSet Data) Lower(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Empty(AdvancedChartSemanticValidator.Validate(statement));
        var manifest = new VisualManifest
        {
            Name = "Route",
            Columns = ["Distance", "Estimate", "Lower", "Upper", "Flag", "Series", "Cohort"],
            Rows = Enumerable.Range(0, 16).Select(index => new List<string?>
            {
                index == 6 ? null : new[] { "8", "2", "5", "4" }[index / 4], index == 4 ? null : new[] { "1", "7", "3", "6" }[index / 4],
                index == 4 ? null : new[] { "1", "2", "4", "9" }[index / 4], index == 10 ? null : new[] { "9", "5", "8", "2" }[index / 4],
                index / 4 == 0 ? "red" : index / 4 == 1 ? "blue" : "plain", index % 2 == 0 ? "S" : "T", index % 4 < 2 ? "A" : "B"
            }).ToList()
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
