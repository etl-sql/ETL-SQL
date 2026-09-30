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

public sealed class RadialStackTests
{
    private const string Script = """
        CREATE VISUAL Totals AS CUSTOM (SOURCE = #prepared, CHART (
          COORDINATE (TYPE = POLAR, INNER_RADIUS = 0.2),
          LAYERS (rings = ARC (ENCODINGS (
            THETA = Category (TYPE = NOMINAL),
            RADIUS = Amount (TYPE = QUANTITATIVE, STACK = ZERO),
            COLOR = Series (TYPE = NOMINAL)
          )))
        ));
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stack_ResolvesIntervalsOnceForAllBackends(bool normalize)
    {
        var (spec, data) = Lower(normalize ? Script.Replace("STACK = ZERO", "STACK = NORMALIZE", StringComparison.Ordinal) : Script);
        var resolver = new PlotPlanResolver();
        var plan = resolver.Resolve(spec, data);
        Assert.Equal(4, plan.Version);
        Assert.Equal(ChartContractVersions.RadialPlotPlanSchema, plan.Schema);
        var rows = plan.Layers.SelectMany(layer => layer.Data).OrderBy(datum => datum.RowIndex).ToArray();
        var expected = normalize ? new[] { (0m, .25m), (.25m, 1m), (0m, .4m), (.4m, 1m) }
            : new[] { (0m, 1m), (1m, 4m), (0m, 2m), (2m, 5m) };
        Assert.Equal(4, rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            var interval = Assert.IsType<ResolvedRadialInterval>(rows[index].RadialInterval);
            Assert.Equal(expected[index], (interval.Start, interval.End));
            Assert.Equal(index < 2 ? 0m : 180m, interval.StartAngle);
            Assert.Equal(index < 2 ? 180m : 360m, interval.EndAngle);
            Assert.Equal(normalize ? 1m : 5m, interval.Maximum);
        }
        foreach (var candidate in new[] { plan, resolver.Relayout(spec, data, plan, new PlotBounds(0m, 0m, 800m, 600m)) })
        {
            var svg = XDocument.Parse(new SvgChartRenderer().Render(candidate));
            var paths = svg.Descendants().Where(element => (string?)element.Attribute("class") == "plot-radial-stack").ToArray();
            Assert.Equal(4, paths.Length);
            Assert.All(paths, path => Assert.Contains(" A ", path.Attribute("d")!.Value));
            var largest = paths.Max(path => decimal.Parse(Regex.Match(path.Attribute("d")!.Value, @" A ([\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture));
            foreach (var path in paths)
            {
                var row = int.Parse(path.Attribute("data-row-index")!.Value, CultureInfo.InvariantCulture);
                var interval = rows[row].RadialInterval!;
                var radii = Regex.Matches(path.Attribute("d")!.Value, @" A ([\d.]+)").Select(match => decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
                Assert.InRange(radii[0] / largest - (.2m + .8m * interval.End / interval.Maximum), -.001m, .001m);
                Assert.InRange(radii[2] / largest - (.2m + .8m * interval.Start / interval.Maximum), -.001m, .001m);
            }
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(candidate), 160).NormalizedText;
            foreach (var datum in rows)
            {
                var description = RadialStackResolver.Description(datum);
                Assert.Contains(paths, path => path.Value == description);
                Assert.Contains(description, terminal);
                Assert.Contains(candidate.Fallback.Items, item => item.Value == description);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, candidate.Bounds)), ChartContractSerializer.Serialize(candidate));
        }
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
        Assert.Throws<InvalidDataException>(() => (plan with { Version = 3, Schema = ChartContractVersions.PlotPlanSchema }).Validate());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegativeValuesFail_AndNullsAndZeroDoNotProduceSegments(bool normalize)
    {
        var (spec, data) = Lower(normalize ? Script.Replace("STACK = ZERO", "STACK = NORMALIZE", StringComparison.Ordinal) : Script);
        ChartDataSet WithValues(params ChartValue[] values) => data with
        {
            Columns = data.Columns.Select(column => column.Name == "Amount"
            ? column with { Values = values.ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
        };
        Assert.Contains("non-negative", Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec,
            WithValues(ChartValue.From(-1m), ChartValue.From(3m), ChartValue.From(2m), ChartValue.From(3m)))).Message);
        var plan = new PlotPlanResolver().Resolve(spec, WithValues(ChartValue.Null(), ChartValue.From(0m), ChartValue.From(0m), ChartValue.From(0m)));
        Assert.DoesNotContain("plot-radial-stack", new SvgChartRenderer().Render(plan));
        Assert.Contains(plan.Fallback.Items, item => item.Value.Contains("gap", StringComparison.Ordinal));
        Assert.All(plan.Layers.SelectMany(layer => layer.Data).Where(datum => !datum.IsGap), datum => Assert.Equal(0m, datum.RadialInterval!.End));
    }

    [Fact]
    public void SingleCategory_UsesTwoArcsForFullCircleAndNormalizesExactly()
    {
        var (spec, data) = Lower(Script.Replace("STACK = ZERO", "STACK = NORMALIZE", StringComparison.Ordinal));
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Category" ? column with
            { Values = [ChartValue.From("Only"), ChartValue.From("Only"), ChartValue.From("Only"), ChartValue.From("Only")], DisplayValues = [] }
            : column.Name == "Amount" ? column with { Values = [ChartValue.From(1m), ChartValue.From(1m), ChartValue.From(1m), ChartValue.From(0m)], DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(1m, plan.Layers.SelectMany(layer => layer.Data).Max(datum => datum.RadialInterval!.End));
        var paths = XDocument.Parse(new SvgChartRenderer().Render(plan)).Descendants().Where(element => (string?)element.Attribute("class") == "plot-radial-stack").ToArray();
        Assert.Equal(3, paths.Length);
        Assert.All(paths, path => Assert.Equal(4, path.Attribute("d")!.Value.Split(" A ").Length - 1));
    }

    [Theory]
    [InlineData("TYPE = NOMINAL", "TYPE = QUANTITATIVE")]
    [InlineData("TYPE = POLAR", "TYPE = POLAR, START_ANGLE = 90, END_ANGLE = 0")]
    public void InvalidFormsAreRejected(string before, string after)
    {
        Assert.NotEmpty(AdvancedChartSemanticValidator.Validate(Parse(Script.Replace(before, after, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task AuthoringDesignerLineageAndPdf_RoundTrip()
    {
        var statement = Parse(Script);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = Script + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Totals)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(Script).Tokenize(), Script).Parse());
        foreach (var field in new[] { "Category", "Amount", "Series" })
            Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Totals" && entry.SourceColumns.Contains(field));
        var (spec, data) = Lower(Script);
        Assert.Equal(ChartContractSerializer.Serialize(spec), ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(ChartContractSerializer.Serialize(spec))));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var report = new ReportManifest
        {
            Title = "Totals",
            Source = "totals.rptsql",
            Visuals =
            [new VisualManifest { Name = "Totals", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = new SvgChartRenderer().Render(plan) }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void MultipleLayers_AccumulateWithoutDroppingRepeatedCategories()
    {
        var (spec, data) = Lower(Script);
        var first = spec.Layers[0] with { Id = "first", Bindings = spec.Layers[0].Bindings.Where(binding => binding.Channel != FieldChannel.Color).ToImmutableArray() };
        spec = spec with { Layers = [first, first with { Id = "second", ZIndex = 1 }] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Equal(8, plan.Layers.Sum(layer => layer.Data.Length));
        Assert.Equal(10m, plan.Layers.SelectMany(layer => layer.Data).Max(datum => datum.RadialInterval!.End));
        Assert.Equal(new[] { "first", "second" }, plan.Layers.Select(layer => layer.SeriesKey));
    }

    [Fact]
    public void DirectContractAndPlan_RejectUnsupportedAndTamperedGeometry()
    {
        var (spec, data) = Lower(Script);
        Assert.Throws<InvalidDataException>(() => (spec with { Facet = new FacetSpec("Category", null, new ScaleResolutionSpec()) }).Validate());
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var first = plan.Layers[0];
        var datum = first.Data[0];
        var invalid = plan with
        {
            Layers = plan.Layers.SetItem(0, first with
            {
                Data = first.Data.SetItem(0, datum with
                { RadialInterval = datum.RadialInterval! with { End = -1m } })
            })
        };
        Assert.Throws<InvalidDataException>(invalid.Validate);
    }

    [Fact]
    public void CategoryOrderAndPartialSweep_AreResolvedInThePlan()
    {
        var (spec, data) = Lower(Script);
        spec = spec with
        {
            Coordinate = spec.Coordinate with { StartAngle = 90m, EndAngle = 270m },
            Scales = spec.Scales.Select(scale => scale.Channel == FieldChannel.Theta ? scale with { CategoryOrder = ["B", "A"] } : scale).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var rows = plan.Layers.SelectMany(layer => layer.Data).OrderBy(datum => datum.RowIndex).ToArray();
        Assert.Equal((180m, 270m), (rows[0].RadialInterval!.StartAngle, rows[0].RadialInterval!.EndAngle));
        Assert.Equal((90m, 180m), (rows[2].RadialInterval!.StartAngle, rows[2].RadialInterval!.EndAngle));
    }

    [Fact]
    public void Goldens()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "34842BD316B150E7059633AAE93FFB03EF21E0DB8C99A2D9C290D384146388F1" && svgHash == "B9B65D00EFC38A406E228AD95DE57C02F06E51B89751F8F9C4CFA8DC614557BC", $"Plan: {planHash}; SVG: {svgHash}");
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
            Name = "Totals",
            Columns = ["Category", "Amount", "Series"],
            Rows = [["A", "1", "First"], ["A", "3", "Second"], ["B", "2", "First"], ["B", "3", "Second"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
