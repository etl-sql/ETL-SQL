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

public sealed class TransposedAspectRuleTests
{
    private const string Script = """
        CREATE VISUAL Measurement AS CUSTOM (
          SOURCE = #prepared,
          CHART (
            COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
            SCALES (
              distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
              estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)
            ),
            LAYERS (
              observations = POINT (ENCODINGS (
                X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)
              )),
              threshold = RULE (
                Z_INDEX = 1,
                INHERIT_ENCODINGS = OFF,
                ENCODINGS (Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)),
                STYLE (LABEL = '<target>', COLOR = '#112233')
              )
            )
          )
        );
        """;

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, true, false, false, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, true, false, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, false, true)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, true, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, true, true, true, true)]
    public void Rules_FollowSemanticAxesThroughFacetsAndResize(bool xRule, bool reverse, bool logarithmic, bool facets, bool field)
    {
        var sql = Script;
        if (xRule) sql = sql.Replace("Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)", "X = DATUM(5) (TYPE = QUANTITATIVE, SCALE = distances)", StringComparison.Ordinal);
        if (field) sql = sql.Replace("DATUM(5)", xRule ? "Distance" : "Estimate", StringComparison.Ordinal);
        if (reverse) sql = sql.Replace("CHANNEL = X,", "CHANNEL = X, REVERSE = ON,", StringComparison.Ordinal)
            .Replace("CHANNEL = Y,", "CHANNEL = Y, REVERSE = ON,", StringComparison.Ordinal);
        if (logarithmic) sql = sql.Replace("LINEAR (CHANNEL", "LOGARITHMIC (CHANNEL", StringComparison.Ordinal)
            .Replace("MIN = 0,", "INCLUDE_ZERO = OFF, MIN = 1,", StringComparison.Ordinal);
        if (facets) sql = sql.Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        var resolver = new PlotPlanResolver();
        var original = resolver.Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        foreach (var bounds in new[] { original.Bounds, new PlotBounds(0m, 0m, 800m, 600m) })
        {
            var plan = resolver.Relayout(spec, data, original, bounds);
            var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
            var rules = Elements(svg, "plot-reference-rule");
            Assert.Equal((facets ? 2 : 1) * (field ? 3 : 1), rules.Length);
            foreach (var rule in rules)
            {
                var value = int.Parse(rule.Value.Split(" = ")[^1], CultureInfo.InvariantCulture);
                var container = rule.Parent!;
                var point = container.Descendants().First(element => (string?)element.Attribute("class") == "plot-point" &&
                    int.Parse(element.Attribute("data-row-index")!.Value, CultureInfo.InvariantCulture) % 3 == (value - 3) / 2);
                Assert.Equal(xRule ? "X" : "Y", (string?)rule.Attribute("data-semantic-axis"));
                if (xRule)
                {
                    Assert.Equal(Read(point, "cy"), Read(rule, "y1"));
                    Assert.Equal(Read(rule, "y1"), Read(rule, "y2"));
                    Assert.True(Read(rule, "x2") > Read(rule, "x1"));
                }
                else
                {
                    Assert.Equal(Read(point, "cx"), Read(rule, "x1"));
                    Assert.Equal(Read(rule, "x1"), Read(rule, "x2"));
                    Assert.True(Read(rule, "y2") > Read(rule, "y1"));
                }
            }
            Assert.All(Elements(svg, "plot-reference-rule-label"), label => Assert.Equal("<target>", label.Value));
            Assert.Equal(rules.Length, Elements(svg, "plot-reference-rule-label").Length);
            var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText;
            Assert.Contains("<target>", terminal);
            Assert.Contains(xRule ? "X = 5" : "Y = 5", terminal);
            var references = plan.Fallback.Items.Where(item => item.Group == "Reference").ToArray();
            Assert.Equal(field ? ["3", "5", "7"] : new[] { "5" }, references.Select(item => item.Value));
            foreach (var reference in references)
            {
                var axisValue = $"{(xRule ? "X" : "Y")} = {reference.Value}";
                Assert.Contains(axisValue, reference.Detail);
                Assert.Contains(axisValue, terminal);
            }
            Assert.Equal(ChartContractSerializer.Serialize(resolver.Resolve(spec, data, bounds)), ChartContractSerializer.Serialize(plan));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void References_ParticipateInGlobalAndFacetDomains(bool field)
    {
        var sql = Script.Replace("DATUM(5)", "DATUM(20)", StringComparison.Ordinal)
            .Replace(", MIN = 0, MAX = 10", "", StringComparison.Ordinal)
            .Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        if (field) sql = sql.Replace("Y = Estimate", "Y = Distance", StringComparison.Ordinal).Replace("DATUM(20)", "Estimate", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        if (field) data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate"
            ? column with { Values = Enumerable.Repeat(ChartValue.From(20m), 6).ToImmutableArray(), DisplayValues = [] } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        Assert.True(PlotPlanResolver.Number(plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]) >= 20m);
        Assert.All(plan.Facets, panel => Assert.True(PlotPlanResolver.Number(panel.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain[^1]) >= 20m));
    }

    [Theory]

    [InlineData("ENCODINGS (Y = DATUM(5)", "POSITION = NUDGE(X = 1, Y = 0, UNIT = EM), ENCODINGS (Y = DATUM(5)")]
    [InlineData("STYLE (LABEL", "CONDITIONS (COLOR WHEN Estimate > 0 THEN '#112233'), STYLE (LABEL")]
    [InlineData("ENCODINGS (Y = DATUM(5)", "ENCODINGS (X = DATUM(5) (TYPE = QUANTITATIVE, SCALE = distances), Y = DATUM(5)")]
    [InlineData("ENCODINGS (Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates))", "ENCODINGS (Y_START = DATUM(3) (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = DATUM(7) (TYPE = QUANTITATIVE, SCALE = estimates))")]
    public void UnsupportedRuleForms_HavePositionedDiagnostics(string before, string after)
    {
        var statement = Parse(Script.Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(AdvancedChartSemanticValidator.Validate(statement), diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0 &&
            diagnostic.Message.Contains("ASPECT_RATIO RULE", StringComparison.Ordinal));
    }

    [Fact]
    public void DirectContract_RejectsMultiAxisAndAdjustedRules()
    {
        var (spec, _) = Lower(Script);
        var rule = spec.Layers[1];
        var field = rule with { Bindings = [new FieldBinding(FieldChannel.X, "Distance", DataSemanticKind.Quantitative, "distances"), new FieldBinding(FieldChannel.Y, "Estimate", DataSemanticKind.Quantitative, "estimates")] };
        var invalid = spec with { Layers = [spec.Layers[0], field] };
        Assert.Contains("ASPECT_RATIO RULE", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
        invalid = spec with { Layers = [spec.Layers[0], rule with { Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 1m, 0m) }] };
        Assert.Contains("ASPECT_RATIO RULE", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthoringContractsAndStaticExport_PreserveReferenceRule(bool field)
    {
        var sql = field ? Script.Replace("DATUM(5)", "Estimate", StringComparison.Ordinal) : Script;
        var statement = Parse(sql);
        Assert.Equal(statement.ToSql(), Parse(statement.ToSql()).ToSql());
        var source = sql + "CREATE PAGE Dashboard AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Measurement)));";
        var parsed = new DesignerAnalysisService().Parse(source, 100);
        Assert.Null(parsed.Error);
        Assert.Equal(statement.ToSql(), Parse(new DesignerScriptPatcher().Patch(source, parsed.DesignState)).ToSql());
        var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
        new ETL_SQL.Analysis.Lineage.LineageAnalyzer(tracker).Analyze(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        Assert.Contains(tracker.GetFullLineage(), entry => entry.TargetTable == "report:Measurement" && entry.SourceColumns.Contains("Estimate"));
        var (spec, data) = Lower(sql);
        var json = ChartContractSerializer.Serialize(spec);
        Assert.Equal(json, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializeChartSpec(json)));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var planJson = ChartContractSerializer.Serialize(plan);
        Assert.Equal(planJson, ChartContractSerializer.Serialize(ChartContractSerializer.DeserializePlotPlan(planJson)));
        var svg = new SvgChartRenderer().Render(plan);
        var report = new ReportManifest
        {
            Title = "Measurement",
            Source = "measurement.rptsql",
            Visuals = [new VisualManifest { Name = "Measurement", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, (await new PdfExporter().ExportAsync(report))[..4]);
    }

    [Fact]
    public void RulesOnly_PreserveValuesAndLabels()
    {
        var (spec, data) = Lower(Script);
        spec = spec with { Layers = [spec.Layers[1]] };
        var plan = new PlotPlanResolver().Resolve(spec, data);
        Assert.Single(Elements(XDocument.Parse(new SvgChartRenderer().Render(plan)), "plot-reference-rule"));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 100).NormalizedText;
        Assert.Contains("Y = 5", terminal);
        Assert.Single(plan.Fallback.Items);
    }

    [Fact]
    public void PlanAndSvg_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "04C4E6BB9F890105FCB4F1531F51993AAC73C137FF8756BF9EE9E0983954D559" && svgHash == "4533F5F606CE8C72EABB93AEEB42BD33E0B841A4464FA86159D26941E6B8744C", $"Plan: {planHash}; SVG: {svgHash}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldRules_DeduplicateWithinPanelsAndSkipNulls(bool allNull)
    {
        var sql = Script.Replace("DATUM(5)", "Estimate", StringComparison.Ordinal)
            .Replace("SCALES (", "FACET (WRAP = Cohort, COLUMNS = 2), RESOLVE (Y = INDEPENDENT), SCALES (", StringComparison.Ordinal);
        var (spec, data) = Lower(sql);
        data = data with
        {
            Columns = data.Columns.Select(column => column.Name == "Estimate" ? column with
            {
                Values = allNull ? Enumerable.Repeat(ChartValue.Null(), 6).ToImmutableArray() :
                [ChartValue.From(7m), ChartValue.From(7m), ChartValue.Null(), ChartValue.From(3m), ChartValue.From(5m), ChartValue.Null()],
                DisplayValues = []
            } : column).ToImmutableArray()
        };
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 1000m, 650m));
        var svg = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var rules = Elements(svg, "plot-reference-rule");
        Assert.Equal(allNull ? 0 : 3, rules.Length);
        if (allNull)
        {
            Assert.DoesNotContain(plan.Fallback.Items, item => item.Group == "Reference" && item.Value.Length > 0);
            return;
        }
        Assert.Equal(new[] { "<target>: Y = 7", "<target>: Y = 3", "<target>: Y = 5" }, rules.Select(rule => rule.Value));
        Assert.Equal(new[] { 1, 2 }, rules.GroupBy(rule => rule.Parent).Select(group => group.Count()));
        Assert.Equal(new[] { "7", "3", "5" }, plan.Fallback.Items.Where(item => item.Group == "Reference").Select(item => item.Value));
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(plan), 120).NormalizedText;
        foreach (var value in new[] { "7", "3", "5" }) Assert.Contains($"Y = {value}", terminal);
    }

    [Fact]
    public void FieldRules_MatchDeterministicGoldens()
    {
        var (spec, data) = Lower(Script.Replace("DATUM(5)", "Estimate", StringComparison.Ordinal));
        var plan = new PlotPlanResolver().Resolve(spec, data);
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal))));
        var planHash = Hash(ChartContractSerializer.Serialize(plan));
        var svgHash = Hash(new SvgChartRenderer().Render(plan));
        Assert.True(planHash == "551122F6BD0B46655D21990DBDAE995F7BA001BB800DCB497355FFD526F33818" && svgHash == "A0DDFAD3CB3B7B9EDDE9DF757F9027810087234218A07CF4FA0D0F9EB35B4A84", $"Plan: {planHash}; SVG: {svgHash}");
    }

    private static XElement[] Elements(XDocument document, string name) => document.Descendants().Where(element => (string?)element.Attribute("class") == name).ToArray();
    private static decimal Read(XElement element, string name) => decimal.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
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
            Name = "Measurement",
            Columns = ["Distance", "Estimate", "Cohort"],
            Rows = [["3", "3", "A"], ["5", "5", "A"], ["7", "7", "A"], ["3", "3", "B"], ["5", "5", "B"], ["7", "7", "B"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return (spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
