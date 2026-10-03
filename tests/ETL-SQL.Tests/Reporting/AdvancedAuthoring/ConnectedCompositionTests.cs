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

public sealed class ConnectedCompositionTests
{
    private static string Sql(string form, bool connect, bool grouped, string facet = "WRAP", bool mixed = false) => $$"""
        CREATE VISUAL Routes AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = CARTESIAN),
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MIN = 0, MAX = 1000),
            horizontal = LINEAR (CHANNEL = X, MIN = 0, MAX = 10), vertical = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)),
          {{(facet == "NONE" ? "" : facet == "GRID" ? "FACET (ROW = Cohort, COLUMN = Phase)," : "FACET (WRAP = Cohort, COLUMNS = 2),")}}
          {{(facet == "NONE" ? "" : "RESOLVE (X = INDEPENDENT, Y = INDEPENDENT),")}}
          LAYERS (route = {{(form == "LINE" ? "LINE" : "AREA")}} (
            INHERIT_ENCODINGS = OFF, NULL_HANDLING = {{(connect ? "CONNECT" : "GAP")}},
            {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
              {{(form == "RIBBON" ? "Y_START = Lower (TYPE = QUANTITATIVE, SCALE = vertical), Y_END = Upper (TYPE = QUANTITATIVE, SCALE = vertical)" : "Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical)")}}
              {{(grouped ? ", COLOR = Series (TYPE = NOMINAL)" : "")}}),
            STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233'),
            CONDITIONS (COLOR WHEN Flag = 'red' THEN '#ff0000',
              COLOR WHEN Flag = 'blue' THEN '#0000ff', OPACITY WHEN Flag = 'red' THEN 0 ELSE 1))
            {{(mixed ? ", observations = POINT (INHERIT_ENCODINGS = OFF, Z_INDEX = 10, NULL_HANDLING = GAP, ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal), Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical)))" : "")}})
        ));
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
            foreach (var connect in new[] { false, true })
                foreach (var grouped in new[] { false, true })
                    foreach (var facet in new[] { "NONE", "WRAP", "GRID" })
                        if (grouped || facet != "NONE") yield return [form, connect, grouped, facet];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConnectionsRemainInsideSeriesFacetsAndLayers(string form, bool connect, bool grouped, string facet)
    {
        var (spec, data) = Lower(Sql(form, connect, grouped, facet));
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        Assert.Equal(6, spec.Version);
        Assert.Equal(9, plan.Version);
        Assert.Equal(new ResolvedCartesianAxes("horizontal", "vertical"), plan.CartesianAxes);
        Assert.Equal(grouped ? 2 : 1, plan.Layers.Length);
        foreach (var layer in plan.Layers)
        {
            Assert.Equal(grouped ? 6 : 12, layer.Data.Length);
            Assert.Empty(layer.ConnectionSkippedRows);
            Assert.NotEmpty(layer.Connections);
            foreach (var connection in layer.Connections)
            {
                var source = layer.Data[connection.SourceIndex];
                var destination = layer.Data[connection.DestinationIndex];
                Assert.Equal(source.RowIndex, connection.SourceRowIndex);
                Assert.Equal(destination.RowIndex, connection.DestinationRowIndex);
                Assert.Equal(source.Encodings.ToArray(), connection.Encodings.ToArray());
                if (grouped) Assert.Equal(source.Channels.Single(value => value.Channel == FieldChannel.Color).Value, destination.Channels.Single(value => value.Channel == FieldChannel.Color).Value);
                var panel = plan.Facets.FirstOrDefault(panel => panel.Id == connection.FacetId);
                connection.Validate(layer, panel?.RowIndices.ToHashSet());
                Assert.Equal(facet == "NONE", connection.FacetId is null);
            }
        }
        var paths = Paths(plan);
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.Length), paths.Length);
        foreach (var layer in plan.Layers)
            foreach (var connection in layer.Connections)
            {
                var description = ConnectedMarkResolver.Describe(connection, plan, layer);
                var path = Assert.Single(paths, path => path.Value == description);
                Assert.Equal(connection.Encodings.FirstOrDefault(value => value.Channel == ConditionalEncodingChannel.Color)?.Value.Text ?? "#112233", path.Attribute(form == "LINE" ? "stroke" : "fill")!.Value);
                Assert.Equal(PlotPlanResolver.Number(connection.Encodings.Single(value => value.Channel == ConditionalEncodingChannel.Opacity).Value) == 0m ? "0" : "1", path.Attribute("opacity")!.Value);
                Assert.Contains(plan.Fallback.Items, item => item.Value == description && item.Group == layer.Id);
                var panel = plan.Facets.FirstOrDefault(panel => panel.Id == connection.FacetId);
                var scaleX = (panel?.Scales ?? plan.Scales).Single(scale => scale.Id == "horizontal");
                var scaleY = (panel?.Scales ?? plan.Scales).Single(scale => scale.Id == "vertical");
                var frame = panel?.Bounds ?? plan.Bounds;
                decimal Map(ResolvedDatum datum, FieldChannel channel, ResolvedScale scale, bool vertical)
                {
                    var value = PlotPlanResolver.Number(datum.Channels.Single(value => value.Channel == channel).Value)!.Value;
                    var minimum = PlotPlanResolver.Number(scale.Domain[0])!.Value;
                    var maximum = PlotPlanResolver.Number(scale.Domain[^1])!.Value;
                    var ratio = (value - minimum) / (maximum - minimum);
                    return vertical ? 40m + (1m - ratio) * (frame.Height - 100m) : 60m + ratio * (frame.Width - 80m);
                }
                var source = layer.Data[connection.SourceIndex];
                var destination = layer.Data[connection.DestinationIndex];
                string Point(ResolvedDatum datum, FieldChannel channel) => $"{Number(Map(datum, FieldChannel.X, scaleX, false))} {Number(Map(datum, channel, scaleY, true))}";
                var expected = $"M {Point(source, form == "RIBBON" ? FieldChannel.YEnd : FieldChannel.Y)} L {Point(destination, form == "RIBBON" ? FieldChannel.YEnd : FieldChannel.Y)}";
                if (form == "RIBBON") expected += $" L {Point(destination, FieldChannel.YStart)} L {Point(source, FieldChannel.YStart)} Z";
                else if (form == "AREA")
                {
                    var minimum = PlotPlanResolver.Number(scaleY.Domain[0])!.Value;
                    var maximum = PlotPlanResolver.Number(scaleY.Domain[^1])!.Value;
                    var baseline = 40m + (1m + minimum / (maximum - minimum)) * (frame.Height - 100m);
                    expected += $" L {Number(Map(destination, FieldChannel.X, scaleX, false))} {Number(baseline)} L {Number(Map(source, FieldChannel.X, scaleX, false))} {Number(baseline)} Z";
                }
                Assert.Equal(expected, path.Attribute("d")!.Value);
            }
        foreach (var candidate in new[] { plan, resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 1000m, 600m)) })
        {
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, candidate.Bounds)), ChartContractSerializer.Serialize(candidate));
            Assert.Equal(plan.Layers.SelectMany(layer => layer.Connections).Select(connection => (connection.SourceRowIndex, connection.DestinationRowIndex, connection.FacetId)), candidate.Layers.SelectMany(layer => layer.Connections).Select(connection => (connection.SourceRowIndex, connection.DestinationRowIndex, connection.FacetId)));
        }
        Assert.Equal(new SvgChartRenderer().Render(plan), new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public void MixedPoliciesKeepRawRowsAndLayerSpecificSkippedMetadata(string form)
    {
        var (spec, data) = Lower(Sql(form, true, true, "WRAP", true));
        var first = spec.Layers[0];
        spec = spec with { Layers = [first, first with { Id = "gapped", ZIndex = 4, Style = first.Style.Select(token => token.Name == "nullHandling" ? token with { Value = "GAP" } : token).ToImmutableArray() }, spec.Layers[1]] };
        data = Missing(data, 4, form == "RIBBON" ? "Lower" : "Estimate");
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var connected = plan.Layers.Where(layer => layer.Id.StartsWith("route", StringComparison.Ordinal)).ToArray();
        var gap = plan.Layers.Where(layer => layer.Id.StartsWith("gapped", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { 4 }, connected.SelectMany(layer => layer.ConnectionSkippedRows));
        Assert.Empty(gap.SelectMany(layer => layer.ConnectionSkippedRows));
        Assert.Equal(12, connected.Sum(layer => layer.Data.Length));
        Assert.Equal(12, gap.Sum(layer => layer.Data.Length));
        Assert.All(connected.SelectMany(layer => layer.Data), datum => Assert.False(datum.IsGap));
        Assert.True(gap.SelectMany(layer => layer.Data).Single(datum => datum.RowIndex == 4).IsGap);
        Assert.Contains(connected.SelectMany(layer => layer.Connections), connection => connection.SourceRowIndex == 0 && connection.DestinationRowIndex == 8);
        Assert.DoesNotContain(gap.SelectMany(layer => layer.Connections), connection => connection.SourceRowIndex == 0 && connection.DestinationRowIndex == 8);
        Assert.True(Assert.Single(plan.Layers, layer => layer.Mark == MarkKind.Point).Connections.IsDefault);
        Assert.Contains(plan.Nulls.GapRows, row => row == 4);
        Assert.Empty(plan.Nulls.SkippedRows);
        Assert.Contains(PlotPlanConformanceHarness.Evaluate(plan, [new LostFacetBackend(true)]).Issues, issue => issue.SemanticArea == "layers");
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.IsDefault ? 0 : layer.Connections.Length), Paths(plan).Length);
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText;
        Assert.Contains("Row 1 to row 9", terminal);
        Assert.Contains("facet A", terminal);
        Assert.Contains("series S", terminal);
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData("LINE", "Distance")]
    [InlineData("LINE", "Estimate")]
    [InlineData("AREA", "Distance")]
    [InlineData("AREA", "Estimate")]
    public void TerminalUsesTypedConnectionsWhenMixedWithPoints(string form, string missingField)
    {
        var (spec, data) = Lower(Sql(form, true, true, "WRAP", true));
        data = Missing(data, 4, missingField);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Flag" ? column with
            { Values = column.Values.Select(_ => ChartValue.From("blue")).ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
        };
        var connected = new PlotPlanResolver().Resolve(spec, data);
        var gappedSpec = spec with
        {
            Layers = spec.Layers.Select(layer => layer with
            { Style = layer.Style.Select(token => token.Name == "nullHandling" ? token with { Value = "GAP" } : token).ToImmutableArray() }).ToImmutableArray()
        };
        var gapped = new PlotPlanResolver().Resolve(gappedSpec, data);
        static string Snapshot(PlotPlan plan) => TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 220).NormalizedText;
        static string Glyphs(string snapshot) => string.Join('\n', snapshot.Split('\n').Where(line => line.Any(character => character is >= '\u2800' and <= '\u28ff')));
        var connectedText = Snapshot(connected);
        var gappedText = Snapshot(gapped);
        Assert.Contains("source-owned connections", connectedText);
        Assert.Contains("source-owned connections", gappedText);
        Assert.NotEqual(Glyphs(connectedText), Glyphs(gappedText));
        Assert.Contains(connected.Fallback.Items, item => item.Detail == "outgoing connection" && item.Value.Contains("Row 1 to row 9", StringComparison.Ordinal));
        Assert.DoesNotContain(gapped.Fallback.Items, item => item.Detail == "outgoing connection" && item.Value.Contains("Row 1 to row 9", StringComparison.Ordinal));
        Assert.All(connected.Fallback.Items.Where(item => item.Detail != "outgoing connection"), item => Assert.Contains(connected.Layers, layer => layer.Id == item.Group));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    public void EmptySingletonAndAllNullCompositionsNeverInventConnections(int rowCount)
    {
        var (spec, data) = Lower(Sql("RIBBON", true, true));
        data = data with
        {
            RowCount = rowCount,
            Columns = data.Columns.Select(column => column with
            { Values = column.Values.Take(rowCount).Select(value => rowCount == 12 && column.Name == "Distance" ? ChartValue.Null() : value).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(9, plan.Version);
        Assert.NotEmpty(plan.Layers);
        Assert.Empty(plan.Layers.SelectMany(layer => layer.Connections));
        Assert.Empty(Paths(plan));
        Assert.Equal(rowCount, plan.Layers.Sum(layer => layer.Data.Length));
    }

    [Fact]
    public void NullSeriesIsDistinctFromTextNullAndPreservesRows()
    {
        var (spec, data) = Lower(Sql("LINE", false, true, "NONE"));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Series" ? column with
            { Values = column.Values.SetItem(0, ChartValue.Null()).SetItem(1, ChartValue.From("null:")), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(12, plan.Layers.Sum(layer => layer.Data.Length));
        Assert.Contains(plan.Series, series => series.Key == "null:" && series.Label == "(null)");
        Assert.Contains(plan.Series, series => series.Key == "value:null:" && series.Label == "null:");
        Assert.Empty(plan.Layers.Single(layer => layer.SeriesKey == "null:").Connections);
    }

    [Theory]
    [InlineData("WRAP")]
    [InlineData("GRID")]
    public void NullFacetCategoriesRetainRowsAndNamedConnectionDescriptions(string facet)
    {
        var (spec, data) = Lower(Sql("RIBBON", true, true, facet, true));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name is "Cohort" or "Phase" ? column with
            { Values = column.Values.Select(_ => ChartValue.Null()).ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(12, Assert.Single(plan.Facets).RowIndices.Length);
        Assert.Equal(12, plan.Layers.Where(layer => !layer.Connections.IsDefault).Sum(layer => layer.Data.Length));
        Assert.All(Paths(plan), path => Assert.Contains("facet (null)", path.Value));
        Assert.All(plan.Fallback.Items.Where(item => item.Detail == "outgoing connection"), item => Assert.Contains("facet (null)", item.Value));
    }

    [Fact]
    public void CaseDistinctSeriesRetainEveryRawRowAndSeparateIdentity()
    {
        var (spec, data) = Lower(Sql("LINE", true, true, "WRAP", true));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Series" ? column with
            { Values = column.Values.Select((value, index) => ChartValue.From(index % 2 == 0 ? "S" : "s")).ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var grouped = plan.Layers.Where(layer => !layer.Connections.IsDefault).ToArray();
        Assert.Equal(2, grouped.Length);
        Assert.Equal(12, grouped.Sum(layer => layer.Data.Length));
        Assert.Equal(2, plan.Series.Count(series => series.Label is "S" or "s"));
        Assert.NotEqual(plan.Series.Single(series => series.Key == grouped[0].SeriesKey).Order,
            plan.Series.Single(series => series.Key == grouped[1].SeriesKey).Order);
        Assert.Equal(grouped.Sum(layer => layer.Connections.Length), Paths(plan).Length);
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData("LINE")]
    [InlineData("AREA")]
    [InlineData("RIBBON")]
    public void ReversalSignedBoundsAndCoincidentRowsPreserveOwnership(string form)
    {
        var (spec, data) = Lower(Sql(form, true, true, "WRAP", true));
        spec = spec with { Scales = spec.Scales.Select(scale => scale with { Reverse = true }).ToImmutableArray() };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name is "Estimate" or "Lower" or "Upper" ? column with
            { Values = column.Values.SetItem(0, ChartValue.From(-3m)).SetItem(4, ChartValue.From(-3m)), DisplayValues = [] } : column.Name == "Distance" ? column with
            { Values = column.Values.SetItem(4, column.Values[0]), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var layer = plan.Layers.First(layer => layer.Connections.Any(connection => connection.SourceRowIndex == 0 && connection.DestinationRowIndex == 4));
        var connection = layer.Connections.Single(connection => connection.SourceRowIndex == 0 && connection.DestinationRowIndex == 4);
        var path = Paths(plan).Single(path => path.Value == ConnectedMarkResolver.Describe(connection, plan, layer));
        Assert.Equal("0", path.Attribute("opacity")!.Value);
        Assert.Contains("#ff0000", path.Value);
        var points = path.Attribute("d")!.Value.Split(' ');
        Assert.Equal(points[1..3], points[4..6]);
        Assert.Contains(plan.Fallback.Items, item => item.Value == (form == "RIBBON" ? "X 8; Y -3 to -3" : "-3") && item.Detail != "outgoing connection");
    }

    [Fact]
    public void GridRetainsEmptyPanelsAndMultipleColorFieldsRemainIsolated()
    {
        var (spec, data) = Lower(Sql("LINE", true, true, "GRID", true));
        var line = spec.Layers[0];
        spec = spec with { Layers = [line, line with { Id = "other", ZIndex = 5, Bindings = line.Bindings.Select(binding => binding.Channel == FieldChannel.Color ? binding with { Field = "OtherSeries" } : binding).ToImmutableArray() }, spec.Layers[1]] };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Phase" ? column with
            { Values = column.Values.Select((value, index) => ChartValue.From(index % 4 < 2 ? "P" : "Q")).ToImmutableArray(), DisplayValues = [] } : column).Append(
            new ChartColumn("OtherSeries", ChartValueKind.Text, DataSemanticKind.Nominal, Enumerable.Range(0, 12).Select(index => ChartValue.From(index % 4 < 2 ? "U" : "V")).ToImmutableArray(), [])).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(4, plan.Facets.Length);
        Assert.Equal(2, plan.Facets.Count(panel => panel.RowIndices.IsEmpty));
        Assert.All(plan.Layers.Where(layer => layer.Id.StartsWith("other", StringComparison.Ordinal)), layer => Assert.True(layer.SeriesKey is "value:U" or "value:V"));
        Assert.Equal(24, plan.Layers.Where(layer => !layer.Connections.IsDefault).Sum(layer => layer.Data.Length));
        Assert.Equal(plan.Layers.Where(layer => !layer.Connections.IsDefault).Sum(layer => layer.Connections.Length), Paths(plan).Length);
        var projection = PlotPlanConformanceHarness.Evaluate(plan, [new LostFacetBackend()]);
        Assert.Contains(projection.Issues, issue => issue.SemanticArea == "layers");
    }

    private sealed class LostFacetBackend(bool dropSkippedRows = false) : IPlotPlanSemanticBackend
    {
        public string Name => "lost-facets";
        public PlotSemanticProjection Project(PlotPlan plan)
        {
            var projection = PlotSemanticProjection.FromPlan(plan);
            return projection with
            {
                Layers = projection.Layers.Select(layer => layer.Connections.IsDefault ? layer : layer with
                {
                    Connections = dropSkippedRows ? layer.Connections : layer.Connections.Select(connection => connection with { FacetId = null }).ToImmutableArray(),
                    ConnectionSkippedRows = dropSkippedRows ? [] : layer.ConnectionSkippedRows
                }).ToImmutableArray()
            };
        }
    }

    [Fact]
    public void FacetAndNullMetadataRejectCrossingsMissingPairsAndDowngrades()
    {
        var (spec, data) = Lower(Sql("LINE", true, false));
        var plan = new PlotPlanResolver().Resolve(spec, Missing(data, 4, "Estimate"));
        var layer = plan.Layers[0];
        Assert.Throws<InvalidDataException>((spec with { Version = 4, Schema = ChartContractVersions.ConnectChartSpecSchema }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Version = 7, Schema = ChartContractVersions.ConnectPlotPlanSchema }).Validate);
        Assert.Throws<InvalidDataException>((plan with { CartesianAxes = null }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Layers = [layer with { ConnectionSkippedRows = [] }] }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Layers = [layer with { Connections = layer.Connections.RemoveAt(0) }] }).Validate);
        var connection = layer.Connections[0];
        Assert.Throws<InvalidDataException>((plan with { Layers = [layer with { Connections = layer.Connections.SetItem(0, connection with { FacetId = plan.Facets[1].Id }) }] }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Layers = [layer with { Connections = layer.Connections.SetItem(0, connection with { DestinationIndex = 2, DestinationRowIndex = 2 }) }] }).Validate);
        Assert.Throws<InvalidDataException>((plan with { Facets = plan.Facets.SetItem(1, plan.Facets[1] with { RowIndices = plan.Facets[1].RowIndices.Add(0) }) }).Validate);
    }

    [Fact]
    public async Task AuthoringLineageAndExportsPreservePartitionedIntent()
    {
        var sql = Sql("RIBBON", true, true, "GRID", true);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Routes)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in new[] { "Distance", "Lower", "Upper", "Flag", "Series", "Cohort", "Phase" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Routes" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var svg = new SvgChartRenderer().Render(plan);
        var report = new ReportManifest { Title = "Routes", Source = "routes.rptsql", Visuals = [new VisualManifest { Name = "Routes", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }] };
        using var browser = JsonDocument.Parse(BrowserDeliveryProjection.Serialize(report));
        Assert.Equal(svg, browser.RootElement.GetProperty("visuals")[0].GetProperty("nativeSvg").GetString());
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [InlineData("TYPE = CARTESIAN", "TYPE = POLAR")]
    [InlineData("INTERPOLATION = 'LINEAR'", "INTERPOLATION = 'MONOTONE'")]
    [InlineData("COLOR = Series (TYPE = NOMINAL)", "COLOR = Estimate (TYPE = QUANTITATIVE)")]
    [InlineData("SCALE = horizontal", "SCALE = unusedX")]
    [InlineData("vertical = LINEAR", "vertical = LOGARITHMIC")]
    [InlineData("SCALE = vertical)", "SCALE = vertical, AXIS = SECONDARY)")]
    public void UnsupportedCompositionFormsHavePositionedDiagnostics(string before, string after)
    {
        var sql = Sql("LINE", false, true, "WRAP", true).Replace(before, after, StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(sql)), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("Connected CONDITIONS", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("coordinate")]
    [InlineData("scale")]
    [InlineData("positionType")]
    [InlineData("groupType")]
    [InlineData("groupConstant")]
    [InlineData("stack")]
    [InlineData("secondary")]
    [InlineData("nudge")]
    [InlineData("zero")]
    [InlineData("smooth")]
    [InlineData("text")]
    [InlineData("ordinaryScale")]
    public void DirectContractsRejectUnsupportedCompositionIntent(string form)
    {
        var (spec, _) = Lower(Sql("LINE", true, true, "WRAP", true));
        var layer = spec.Layers[0];
        var changed = form switch
        {
            "coordinate" => spec with { Coordinate = spec.Coordinate with { Kind = CoordinateKind.TransposedCartesian } },
            "scale" => spec with { Scales = spec.Scales.Select(scale => scale.Id == "vertical" ? scale with { Kind = ScaleKind.Logarithmic } : scale).ToImmutableArray() },
            "ordinaryScale" => spec with { Layers = spec.Layers.SetItem(1, spec.Layers[1] with { Bindings = spec.Layers[1].Bindings.Select(binding => binding.Channel == FieldChannel.Y ? binding with { ScaleId = "unusedY" } : binding).ToImmutableArray() }) },
            _ => spec with
            {
                Layers = spec.Layers.SetItem(0, form switch
                {
                    "positionType" => layer with { Bindings = layer.Bindings.Select(binding => binding.Channel == FieldChannel.X ? binding with { SemanticKind = DataSemanticKind.Temporal } : binding).ToImmutableArray() },
                    "groupType" => layer with { Bindings = layer.Bindings.Select(binding => binding.Channel == FieldChannel.Color ? binding with { SemanticKind = DataSemanticKind.Quantitative } : binding).ToImmutableArray() },
                    "groupConstant" => layer with { Bindings = layer.Bindings.Select(binding => binding.Channel == FieldChannel.Color ? FieldBinding.Datum(FieldChannel.Color, ChartValue.From("S"), DataSemanticKind.Nominal) : binding).ToImmutableArray() },
                    "stack" => layer with { Bindings = layer.Bindings.Select(binding => binding.Channel == FieldChannel.Y ? binding with { Stack = StackMode.Zero } : binding).ToImmutableArray() },
                    "secondary" => layer with { Bindings = layer.Bindings.Select(binding => binding.Channel == FieldChannel.Y ? binding with { Axis = AxisRole.Secondary } : binding).ToImmutableArray() },
                    "nudge" => layer with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 1m) },
                    "zero" => layer with { Style = layer.Style.Select(token => token.Name == "nullHandling" ? token with { Value = "ZERO" } : token).ToImmutableArray() },
                    "smooth" => layer with { Style = layer.Style.Select(token => token.Name == "INTERPOLATION" ? token with { Value = "SMOOTH" } : token).ToImmutableArray() },
                    "text" => layer with { Conditions = [layer.Conditions[0] with { Channel = ConditionalEncodingChannel.Text }] },
                    _ => throw new ArgumentOutOfRangeException(nameof(form))
                })
            }
        };
        Assert.Throws<InvalidDataException>(changed.Validate);
    }

    [Fact]
    public void FocusedCompositionExampleParsesLowersAndResolves()
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var example = Regex.Matches(File.ReadAllText(path), @"```sql\s*(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value.Trim()).Single(block => block.StartsWith("CREATE VISUAL Routes ", StringComparison.Ordinal));
        var (spec, data) = Lower(example);
        Assert.Equal(Parse(example).ToSql(), Parse(Parse(example).ToSql()).ToSql());
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(9, plan.Version);
        Assert.Equal(5, plan.Layers.Length);
        Assert.NotEmpty(plan.Layers.SelectMany(layer => layer.Connections.IsDefault ? [] : layer.Connections));
    }

    [Fact]
    public void InferredAxesUseTheSameScalesAcrossLayersAndFacets()
    {
        var sql = Regex.Replace(Sql("RIBBON", true, true, "WRAP", true), @"SCALES \(unusedX.*?MAX = 10\)\),", "", RegexOptions.Singleline)
            .Replace(", SCALE = horizontal", "", StringComparison.Ordinal).Replace(", SCALE = vertical", "", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(2, spec.Scales.Count(scale => scale.Channel is FieldChannel.X or FieldChannel.Y));
        Assert.Equal(spec.Scales.Single(scale => scale.Channel == FieldChannel.X).Id, plan.CartesianAxes!.XScaleId);
        Assert.Equal(spec.Scales.Single(scale => scale.Channel == FieldChannel.Y).Id, plan.CartesianAxes.YScaleId);
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.IsDefault ? 0 : layer.Connections.Length), Paths(plan).Length);
    }

    [Fact]
    public void SeriesExpansionPreservesAuthoredLayerPriority()
    {
        var (spec, data) = Lower(Sql("LINE", true, true, "NONE", true));
        spec = spec with { Layers = spec.Layers.SetItem(1, spec.Layers[1] with { ZIndex = 1 }) };
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Series" ? column with
            { Values = column.Values.SetItem(2, ChartValue.From("U")), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.All(plan.Layers.Where(layer => !layer.Connections.IsDefault), layer => Assert.Equal(0, layer.ZIndex));
        Assert.Equal(1, plan.Layers[^1].ZIndex);
        Assert.Equal(MarkKind.Point, plan.Layers[^1].Mark);
    }

    public static IEnumerable<object[]> WorkloadCases()
    {
        foreach (var zero in new[] { false, true })
            foreach (var transposed in new[] { false, true })
                foreach (var decorated in new[] { false, true })
                    foreach (var interpolation in decorated ? new[] { "LINEAR", "SMOOTH", "STEP_BEFORE", "STEP_AFTER" } : new[] { "LINEAR" })
                        yield return [zero, transposed, decorated, interpolation];
    }

    [Theory]
    [MemberData(nameof(WorkloadCases))]
    public void RepresentativeCompositionStaysWithinExistingWorkloadBudgets(bool zero, bool transposed, bool decorated, string interpolation)
    {
        const int rowCount = 600;
        var (spec, data) = Lower(Sql("RIBBON", true, true, "GRID", true));
        var ribbon = spec.Layers[0];
        spec = spec with
        {
            Layers = [ribbon, ribbon with { Id = "trend", Mark = MarkKind.Line, ZIndex = 4,
            Bindings = ribbon.Bindings.Where(binding => binding.Channel is not (FieldChannel.YStart or FieldChannel.YEnd)).Append(new FieldBinding(FieldChannel.Y, "Estimate", DataSemanticKind.Quantitative, "vertical")).ToImmutableArray() }, spec.Layers[1]]
        };
        if (zero) spec = spec with
        {
            Schema = ChartContractVersions.ZeroConnectedChartSpecSchema,
            Version = ChartContractVersions.ZeroConnectedChartSpecVersion,
            Layers = spec.Layers.Select(layer => layer with { Style = layer.Style.Select(token => token.Name == "nullHandling" ? token with { Value = "ZERO" } : token).ToImmutableArray() }).ToImmutableArray()
        };
        if (transposed) spec = spec with { Coordinate = spec.Coordinate with { Kind = CoordinateKind.TransposedCartesian, AspectRatio = 2m }, Schema = ChartContractVersions.TransposedConnectedChartSpecSchema, Version = ChartContractVersions.TransposedConnectedChartSpecVersion };
        if (decorated) spec = spec with
        {
            Schema = ChartContractVersions.DecoratedConnectedChartSpecSchema,
            Version = ChartContractVersions.DecoratedConnectedChartSpecVersion,
            Layers = spec.Layers.Select(layer => layer.Mark is not (MarkKind.Line or MarkKind.Area) ? layer : layer with
            {
                Conditions = layer.Conditions.Add(layer.Conditions[0] with { Channel = ConditionalEncodingChannel.Size, WhenTrue = ChartValue.From(4m), WhenFalse = ChartValue.From(2m) })
                    .Add(layer.Conditions[0] with { Channel = ConditionalEncodingChannel.Shape, WhenTrue = ChartValue.From("DIAMOND"), WhenFalse = ChartValue.Null() })
                    .Add(layer.Conditions[0] with { Channel = ConditionalEncodingChannel.Text, WhenTrue = ChartValue.From("R"), WhenFalse = ChartValue.Null() })
            }).ToImmutableArray()
        };
        if (interpolation != "LINEAR") spec = spec with
        {
            Schema = ChartContractVersions.InterpolatedConnectedChartSpecSchema,
            Version = ChartContractVersions.InterpolatedConnectedChartSpecVersion,
            Layers = spec.Layers.Select(layer => layer.Mark is not (MarkKind.Line or MarkKind.Area) ? layer : layer with
            { Style = layer.Style.Select(token => token.Name == "INTERPOLATION" ? token with { Value = interpolation } : token).ToImmutableArray() }).ToImmutableArray()
        };
        data = data with
        {
            RowCount = rowCount,
            Columns = data.Columns.Select(column => column with
            { Values = Enumerable.Range(0, rowCount).Select(index => column.Name == "Phase" ? ChartValue.From($"P{index / 12 % 6}") : column.Values[index % 12]).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
        };
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 1200m, 600m));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var resolution = clock.Elapsed;
        clock.Restart();
        var svg = new SvgChartRenderer().Render(plan);
        var rendering = clock.Elapsed;
        Assert.Equal(12, plan.Facets.Length);
        Assert.Equal(rowCount * 3, plan.Layers.Sum(layer => layer.Data.Length));
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.IsDefault ? 0 : layer.Connections.Length), XDocument.Parse(svg).Descendants().Count(element => (string?)element.Attribute("class") is "plot-conditional-connection" or "plot-conditional-area"));
        Assert.True(resolution < TimeSpan.FromSeconds(5), $"Resolver took {resolution.TotalMilliseconds} ms."); // flaky-time-bound-ok: 5 seconds leaves headroom for a bounded 600-row, 12-panel workload.
        Assert.True(rendering < TimeSpan.FromSeconds(5), $"SVG rendering took {rendering.TotalMilliseconds} ms."); // flaky-time-bound-ok: 5 seconds leaves headroom for the bounded native SVG workload.
        Assert.True(allocated < 16L * 1024 * 1024, $"Resolver allocated {allocated:N0} bytes.");
        Assert.InRange(Encoding.UTF8.GetByteCount(ChartContractSerializer.Serialize(plan)), 1, 6 * 1024 * 1024);
        Assert.InRange(Encoding.UTF8.GetByteCount(svg), 1, 600 * 1024);
    }

    [Theory]
    [InlineData("LINE", "43BA5337992B4A933EB2482922C930379C32C53D71D10CF9703CE97AB71A6936", "04C14677DD7E11F9A04E5C5D7A04AD194F567FF3FC0CE2A283E1D7A3DA46EF0A")]
    [InlineData("AREA", "692CAA3A7FB9B28D6DC92E3B4C14D72DC43273F816A1EEFDAF9B07FFD5850CA6", "57A0EE023F905528AB92CA33E3481906DA545B831FEF5D4B402CA1E0AF513DAC")]
    [InlineData("RIBBON", "C4367673FD868E833A21D399A1F3D2222D86DBDDB8860CEA576DA7A76483F752", "256D9D8C97789655C398C4E9CC6183138CE5A47BEBD0473B98F96A7CDE0A0D92")]
    public void DeterministicConnectionAndSvgGoldens(string form, string expectedPlan, string expectedSvg)
    {
        var (spec, data) = Lower(Sql(form, true, true, "WRAP", true));
        var plan = new PlotPlanResolver().Resolve(spec, Missing(data, 4, form == "RIBBON" ? "Lower" : "Estimate"));
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg, $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static XElement[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") is "plot-conditional-connection" or "plot-conditional-area").ToArray();
    private static ChartDataSet Missing(ChartDataSet data, int row, string field) => data with { Columns = data.Columns.Select(column => column.Name == field ? column with { Values = column.Values.SetItem(row, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray() };
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
            Name = "Routes",
            Columns = ["Distance", "Estimate", "Lower", "Upper", "Flag", "Series", "Cohort", "Phase"],
            Rows = Enumerable.Range(0, 12).Select(index => new List<string?>
        { new[] { "8", "2", "5" }[index / 4], new[] { "1", "7", "3" }[index / 4], new[] { "4", "1", "6" }[index / 4], new[] { "2", "9", "6" }[index / 4],
            new[] { "red", "blue", "none" }[index / 4], index % 2 == 0 ? "S" : "T", index % 4 < 2 ? "A" : "B", "P" }).ToList()
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
