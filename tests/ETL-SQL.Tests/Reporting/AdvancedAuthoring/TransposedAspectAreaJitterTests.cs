using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

public sealed class TransposedAspectAreaJitterTests
{
    private static string Script(bool ribbon) => $$"""
        CREATE VISUAL Envelope AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
          SCALES (unusedX = BAND (CHANNEL = X), unusedY = LINEAR (CHANNEL = Y, MAX = 1000),
            distances = LINEAR (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10),
            estimates = LINEAR (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10)),
          LAYERS (envelope = AREA (
            INHERIT_ENCODINGS = OFF, NULL_HANDLING = GAP,
            {{(ribbon ? "" : "AREA_BASELINE = ZERO,")}}
            POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42),
            ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
              {{(ribbon ? "Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound" : "Y = UpperBound")}} (TYPE = QUANTITATIVE, SCALE = estimates)),
            STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233')
          ))
        ));
        """;

    public static IEnumerable<object[]> GeometryCases()
    {
        foreach (var ribbon in new[] { false, true })
            foreach (var reverse in new[] { false, true })
                foreach (var logarithmic in new[] { false, true })
                    foreach (var facets in new[] { false, true })
                        foreach (var missing in ribbon ? new[] { "none", "Distance", "LowerBound", "UpperBound" } : new[] { "none", "Distance", "UpperBound" })
                            yield return [ribbon, reverse, logarithmic, facets, missing];
    }

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public void SeededOffsetsTranslateWholeCrossSectionsThroughAxesFacetsResizeAndGaps(bool ribbon, bool reverse, bool logarithmic, bool facets, string missing)
    {
        var sql = Script(ribbon);
        if (reverse) sql = sql.Replace("INCLUDE_ZERO = OFF, MIN", "REVERSE = ON, INCLUDE_ZERO = OFF, MIN", StringComparison.Ordinal);
        if (logarithmic)
        {
            sql = sql.Replace("distances = LINEAR", "distances = LOGARITHMIC", StringComparison.Ordinal);
            if (ribbon) sql = sql.Replace("estimates = LINEAR", "estimates = LOGARITHMIC", StringComparison.Ordinal);
        }
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        if (missing != "none") data = data with
        {
            Columns = data.Columns.Select(column => column.Name == missing ? column with
            { Values = column.Values.SetItem(2, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray()
        };
        var resolver = new PlotPlanResolver();
        var initial = resolver.Resolve(spec, data);
        foreach (var bounds in new[] { initial.Bounds, new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var plan = resolver.Relayout(spec, data, initial, bounds);
            var baseline = resolver.Resolve(spec with { Layers = [spec.Layers[0] with { Position = null }] }, data, bounds);
            Assert.Equal(5, spec.Version);
            Assert.Equal(8, plan.Version);
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            Assert.Equal(baseline.Scales.SelectMany(scale => scale.Domain), plan.Scales.SelectMany(scale => scale.Domain));
            Assert.Equal(baseline.Layers[0].Data.SelectMany(datum => datum.Channels), plan.Layers[0].Data.SelectMany(datum => datum.Channels));
            foreach (var datum in plan.Layers[0].Data)
            {
                var viewport = facets ? Assert.Single(plan.Facets, panel => panel.RowIndices.Contains(datum.RowIndex)).CartesianViewport! : plan.CartesianViewport!;
                Assert.Equal(Hash("K" + datum.RowIndex, "y", ribbon, facets) * 0.03m * (viewport.Width - 80m), datum.DisplayOffsetX);
                Assert.Equal(-Hash("K" + datum.RowIndex, "x", ribbon, facets) * 0.02m * (viewport.Height - 100m), datum.DisplayOffsetY);
            }
            var before = Paths(baseline);
            var after = Paths(plan);
            Assert.Equal(facets || missing != "none" ? 2 : 1, after.Length);
            Assert.Equal(before.Length, after.Length);
            for (var run = 0; run < after.Length; run++)
            {
                Assert.Equal(before[run].Value, after[run].Value);
                var rows = Rows(after[run]);
                var original = Vertices(before[run]);
                var actual = Vertices(after[run]);
                Assert.Equal(rows.Length * 2, actual.Length);
                for (var index = 0; index < rows.Length; index++)
                {
                    var datum = Assert.Single(plan.Layers[0].Data, datum => datum.RowIndex == rows[index]);
                    foreach (var vertex in new[] { index, actual.Length - index - 1 })
                    {
                        Assert.InRange(Math.Abs(actual[vertex].X - original[vertex].X - datum.DisplayOffsetX), 0m, 0.002m);
                        Assert.InRange(Math.Abs(actual[vertex].Y - original[vertex].Y - datum.DisplayOffsetY), 0m, 0.002m);
                    }
                }
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
            Assert.Equal(TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(baseline), 140).NormalizedText,
                TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 140).NormalizedText);
        }
    }

    [Theory]
    [InlineData(false, "LEFT")]
    [InlineData(true, "LEFT")]
    [InlineData(false, "RIGHT")]
    [InlineData(true, "RIGHT")]
    [InlineData(false, "BOTTOM")]
    [InlineData(true, "BOTTOM")]
    public void OtherLayersConditionsAndColorLegendsRetainSeparatePlacement(bool ribbon, string legend)
    {
        var sql = Script(ribbon).Replace("LAYERS (envelope = AREA", """
            LAYERS (route = LINE (
              INHERIT_ENCODINGS = OFF, NULL_HANDLING = GAP,
              ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
              STYLE (INTERPOLATION = 'LINEAR'), CONDITIONS (COLOR WHEN UpperBound >= 5 THEN '#ff0000')
            ), envelope = AREA
            """, StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var area = spec.Layers.Single(layer => layer.Mark == MarkKind.Area);
        var point = new MarkLayerSpec("observations", MarkKind.Point, 2,
            [new FieldBinding(FieldChannel.X, "Distance", DataSemanticKind.Quantitative, ScaleId: "distances"),
             new FieldBinding(FieldChannel.Y, "UpperBound", DataSemanticKind.Quantitative, ScaleId: "estimates"),
             new FieldBinding(FieldChannel.Color, "Cohort", DataSemanticKind.Nominal)], []);
        spec = spec with
        {
            Layers = spec.Layers.Add(point),
            Bindings = spec.Bindings.Add(point.Bindings[^1]),
            Theme = spec.Theme with { Tokens = [.. spec.Theme.Tokens, new StyleToken("LEGEND_POSITION", legend)] }
        };
        var resolver = new PlotPlanResolver();
        foreach (var bounds in new[] { new PlotBounds(0m, 0m, 800m, 600m), new PlotBounds(0m, 0m, 1000m, 650m) })
        {
            var plan = resolver.Resolve(spec, data, bounds);
            var baseline = resolver.Resolve(spec with { Layers = spec.Layers.Select(layer => layer.Id == area.Id ? layer with { Position = null } : layer).ToImmutableArray() }, data, bounds);
            Assert.Equal(11, plan.Version);
            Assert.True(plan.Legend.Length > 1);
            var before = XDocument.Parse(new SvgChartRenderer().Render(baseline));
            var after = XDocument.Parse(new SvgChartRenderer().Render(plan));
            foreach (var name in new[] { "plot-point", "plot-conditional-line" })
                Assert.Equal(before.Descendants().Where(element => (string?)element.Attribute("class") == name).Select(element => element.ToString()),
                    after.Descendants().Where(element => (string?)element.Attribute("class") == name).Select(element => element.ToString()));
            Assert.All(plan.Layers.Where(layer => layer.Mark != MarkKind.Area).SelectMany(layer => layer.Data), datum =>
            {
                Assert.Equal(0m, datum.DisplayOffsetX);
                Assert.Equal(0m, datum.DisplayOffsetY);
            });
            var original = Vertices(Assert.Single(Paths(baseline)));
            var vertices = Vertices(Assert.Single(Paths(plan)));
            foreach (var datum in plan.Layers.Single(layer => layer.Mark == MarkKind.Area).Data)
                foreach (var vertex in new[] { datum.RowIndex, vertices.Length - datum.RowIndex - 1 })
                {
                    Assert.InRange(Math.Abs(vertices[vertex].X - original[vertex].X - datum.DisplayOffsetX), 0m, 0.002m);
                    Assert.InRange(Math.Abs(vertices[vertex].Y - original[vertex].Y - datum.DisplayOffsetY), 0m, 0.002m);
                }
            Assert.Equal(baseline.Fallback.Items.ToArray(), plan.Fallback.Items.ToArray());
            Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        }
    }

    [Theory]
    [InlineData(false, -0.01, 0.03)]
    [InlineData(true, -0.01, 0.03)]
    [InlineData(false, 0.02, 1.01)]
    [InlineData(true, 0.02, 1.01)]
    public void InvalidAmplitudesFailAuthoringAndContractValidation(bool ribbon, double x, double y)
    {
        var sql = Script(ribbon).Replace("X = 0.02, Y = 0.03", $"X = {x.ToString(CultureInfo.InvariantCulture)}, Y = {y.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal);
        Assert.Contains(AdvancedChartSemanticValidator.Validate(Parse(sql)), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 && diagnostic.Message.Contains("amplitudes", StringComparison.Ordinal));
        var (spec, _) = Lower(Script(ribbon));
        var invalid = spec with { Layers = [spec.Layers[0] with { Position = spec.Layers[0].Position! with { X = (decimal)x, Y = (decimal)y } }] };
        Assert.Contains("amplitudes", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeysRetainOffsetsThroughRowAndFieldRenamesWhileSeedsChangePattern(bool ribbon)
    {
        var (spec, data) = Lower(Script(ribbon));
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data);
        var order = new[] { 5, 3, 1, 4, 0, 2 };
        var reordered = data with { Columns = data.Columns.Select(column => column with { Values = order.Select(row => column.Values[row]).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray() };
        var changed = resolver.Resolve(spec, reordered);
        for (var index = 0; index < order.Length; index++)
        {
            Assert.Equal(original.Layers[0].Data[order[index]].DisplayOffsetX, changed.Layers[0].Data[index].DisplayOffsetX);
            Assert.Equal(original.Layers[0].Data[order[index]].DisplayOffsetY, changed.Layers[0].Data[index].DisplayOffsetY);
        }
        Assert.Equal(Enumerable.Range(0, 6), Rows(Assert.Single(Paths(changed))));
        var renamed = Lower(Script(ribbon).Replace("envelope = AREA", "renamed = AREA", StringComparison.Ordinal).Replace("distances", "renamedScale", StringComparison.Ordinal)
            .Replace("Distance", "RenamedDistance", StringComparison.Ordinal).Replace("KEY = Id", "KEY = RenamedId", StringComparison.Ordinal)).Spec;
        var renamedData = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Distance" ? column with { Name = "RenamedDistance" }
            : column.Name == "Id" ? column with { Name = "RenamedId" } : column).ToImmutableArray()
        };
        Assert.Equal(original.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)), resolver.Resolve(renamed, renamedData).Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)));
        var seeded = spec with { Layers = [spec.Layers[0] with { Position = spec.Layers[0].Position! with { Seed = 43 } }] };
        Assert.NotEqual(original.Layers[0].Data[0].DisplayOffsetX, resolver.Resolve(seeded, data).Layers[0].Data[0].DisplayOffsetX);
    }

    [Theory]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "null")]
    [InlineData(true, "null")]
    [InlineData(false, "duplicate")]
    [InlineData(true, "duplicate")]
    public void KeyValidationIncludesMissingCoordinateRows(bool ribbon, string invalid)
    {
        var (spec, data) = Lower(Script(ribbon));
        data = data with { Columns = data.Columns.Select(column => column.Name == "Distance" ? column with { Values = column.Values.SetItem(2, ChartValue.Null()), DisplayValues = [] } : column).ToImmutableArray() };
        data = data with
        {
            Columns = invalid == "missing" ? data.Columns.Where(column => column.Name != "Id").ToImmutableArray()
            : data.Columns.Select(column => column.Name == "Id" ? column with { Values = column.Values.SetItem(2, invalid == "null" ? ChartValue.Null() : column.Values[0]), DisplayValues = [] } : column).ToImmutableArray()
        };
        Assert.Contains(invalid == "missing" ? "does not exist" : invalid == "null" ? "contains nulls" : "duplicate", Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data)).Message);
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 1, 0)]
    [InlineData(true, 1, 0)]
    [InlineData(false, 0, 1)]
    [InlineData(true, 0, 1)]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 1)]
    public void BoundaryAmplitudesRetainConstantCoincidentCrossSections(bool ribbon, int x, int y)
    {
        var sql = Script(ribbon).Replace("X = 0.02, Y = 0.03", $"X = {x}, Y = {y}", StringComparison.Ordinal)
            .Replace("X = Distance", "X = DATUM(5)", StringComparison.Ordinal).Replace("Y_START = LowerBound", "Y_START = DATUM(5)", StringComparison.Ordinal)
            .Replace("Y_END = UpperBound", "Y_END = DATUM(5)", StringComparison.Ordinal).Replace("Y = UpperBound", "Y = DATUM(5)", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var vertices = Vertices(Assert.Single(Paths(plan)));
        Assert.Equal(12, vertices.Length);
        for (var index = 0; index < 6; index++)
        {
            Assert.Equal(vertices[index].Y, vertices[^(index + 1)].Y);
            if (ribbon) Assert.Equal(vertices[index], vertices[^(index + 1)]);
        }
        Assert.All(plan.Layers[0].Data, datum =>
        {
            Assert.InRange(Math.Abs(datum.DisplayOffsetX), 0m, y * (plan.CartesianViewport!.Width - 80m));
            Assert.InRange(Math.Abs(datum.DisplayOffsetY), 0m, x * (plan.CartesianViewport!.Height - 100m));
        });
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void EmptySingletonAndAllMissingInputsProduceNoPolygon(bool ribbon, int profile)
    {
        var (spec, data) = Lower(Script(ribbon));
        data = data with
        {
            RowCount = profile == 0 ? 0 : profile == 1 ? 1 : data.RowCount,
            Columns = data.Columns.Select(column => column with
            { Values = profile == 0 ? [] : profile == 1 ? [column.Values[0]] : column.Name == "Distance" ? column.Values.Select(_ => ChartValue.Null()).ToImmutableArray() : column.Values, DisplayValues = [] }).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Empty(Paths(plan));
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthoringDesignerContractsLineageAndPdfRetainRawIntent(bool ribbon)
    {
        var sql = Script(ribbon);
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Envelope)));";
        var design = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(design.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, design.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        foreach (var field in ribbon ? new[] { "Distance", "LowerBound", "UpperBound", "Id" } : new[] { "Distance", "UpperBound", "Id" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Envelope" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(sql);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        Assert.Throws<InvalidDataException>((spec with { Schema = ChartContractVersions.ChartSpecSchema, Version = 2 }).Validate);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Throws<InvalidDataException>((plan with { Schema = ChartContractVersions.PlotPlanSchema, Version = 3 }).Validate);
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Equal(svg, new SvgChartRenderer().Render(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        var report = new ReportManifest { Title = "Jittered area", Source = "jittered-area.rptsql", Visuals = [new VisualManifest { Name = "Envelope", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }] };
        using (var delivery = System.Text.Json.JsonDocument.Parse(BrowserDeliveryProjection.Serialize(report)))
            Assert.Equal(svg, delivery.RootElement.GetProperty("visuals")[0].GetProperty("nativeSvg").GetString());
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepresentativeWorkloadRetainsExistingBudgets(bool ribbon)
    {
        const int rowCount = 600;
        var (spec, data) = Lower(Script(ribbon).Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 4), RESOLVE (X = INDEPENDENT, Y = INDEPENDENT), SCALES (", StringComparison.Ordinal));
        data = data with
        {
            RowCount = rowCount,
            Columns = data.Columns.Select(column => column with
            { Values = Enumerable.Range(0, rowCount).Select(row => column.Name == "Id" ? ChartValue.From("K" + row) : column.Name == "Cohort" ? ChartValue.From("P" + row / 50) : column.Values[row % 6]).ToImmutableArray(), DisplayValues = [] }).ToImmutableArray()
        };
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 1200m, 800m));
        var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        var resolution = clock.Elapsed;
        clock.Restart();
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Equal(12, plan.Facets.Length);
        Assert.Equal(12, Paths(plan).Length);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "AREA SVG rendering exceeded 5 seconds."); // flaky-time-bound-ok: 5 seconds bounds a 600-row, 12-panel native workload with ample headroom.
        Assert.True(resolution < TimeSpan.FromSeconds(5), "AREA resolution exceeded 5 seconds."); // flaky-time-bound-ok: 5 seconds bounds a 600-row, 12-panel native workload with ample headroom.
        Assert.True(allocation < 16L * 1024 * 1024, $"AREA resolution allocated {allocation:N0} bytes.");
        Assert.InRange(Encoding.UTF8.GetByteCount(ChartContractSerializer.Serialize(plan)), 1, 6 * 1024 * 1024);
        Assert.InRange(Encoding.UTF8.GetByteCount(svg), 1, 600 * 1024);
    }

    [Theory]
    [InlineData(false, "D709EB119AF62083D988658A3535F2733FFDBD7B922CCC6148D8ABD41ACE88D2", "2584C03C83F40158E9E19D8D041F334BB02246EF245C49A2BED88A7BD335F6CD")]
    [InlineData(true, "9FE1F1105F369C99FAE041E8DE50D4ADE434CECCE5AF3D2A8435FAEFC152B6A7", "40658AC1CE1968E7A3CD0FC391045F42F0164135A079344FBF7AEA7AB664272A")]
    public void PlanAndSvgHaveDeterministicFingerprints(bool ribbon, string expectedPlan, string expectedSvg)
    {
        var (spec, data) = Lower(Script(ribbon));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Fingerprint(ChartContractSerializer.Serialize(plan));
        var svgHash = Fingerprint(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == expectedPlan && svgHash == expectedSvg, $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Fact]
    public void PublishedJitterExampleParsesLowersAndResolves()
    {
        var path = Path.Combine(TerminalSnapshotHarness.GetRepoRoot(), "docs", "reference", "visuals-reporting", "visuals", "chart.md");
        var example = Regex.Matches(File.ReadAllText(path), @"```sql\s*(.*?)```", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value.Trim()).Single(block => block.StartsWith("CREATE VISUAL JitteredRibbon AS CUSTOM", StringComparison.Ordinal));
        var (spec, data) = Lower(example);
        Assert.Equal(Parse(example).ToSql(), Parse(Parse(example).ToSql()).ToSql());
        Assert.Equal(PositionAdjustmentKind.Jitter, spec.Layers[0].Position!.Kind);
        Assert.Single(Paths(new PlotPlanResolver().Resolve(spec, data)));
    }

    private static decimal Hash(string key, string axis, bool ribbon, bool facets)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"Envelope\u001fArea|0|{(ribbon ? "X,YStart,YEnd" : "X,Y")}{(facets ? ",Wrap" : "")}\u001f{key}\u001f{axis}\u001f42"));
        return (decimal)BinaryPrimitives.ReadUInt64BigEndian(bytes) / ulong.MaxValue * 2m - 1m;
    }
    private static XElement[] Paths(PlotPlan plan) => XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") is "plot-area" or "plot-ribbon").ToArray();
    private static int[] Rows(XElement path) => Regex.Matches(path.Value, @"Row (\d+):").Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1).ToArray();
    private static (decimal X, decimal Y)[] Vertices(XElement path) => path.Attribute("d")!.Value.Split(' ').SkipLast(1).Chunk(3).Select(tokens =>
        (decimal.Parse(tokens[1], CultureInfo.InvariantCulture), decimal.Parse(tokens[2], CultureInfo.InvariantCulture))).ToArray();
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
            Columns = ["Distance", "LowerBound", "UpperBound", "Cohort", "Id"],
            Rows = [["8", "1", "4", "A", "K0"], ["2", "7", "2", "A", "K1"], ["5", "3", "9", "A", "K2"], ["1", "8", "3", "B", "K3"], ["9", "2", "6", "B", "K4"], ["4", "6", "1", "B", "K5"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
