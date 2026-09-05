using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

/// <summary>
/// Covers the v0.19.0 chart-property gaps for interactions and actions (<c>OPEN_URL</c> templates,
/// <c>EMIT_FILTER</c>), large-dataset rendering (<c>SAMPLING</c>, <c>PROGRESSIVE</c>), the chart
/// toolbox (<c>SHOW_EXPORT</c>, <c>SHOW_DATA_VIEW</c>), linked zoom, and declarative tooltips.
/// </summary>
public sealed class ChartInteractionAndLargeDataTests
{
    [Fact]
    public void OpenUrlTemplate_CarriesTemplateAndDeclaredParams()
    {
        const string sql = """
            CREATE VISUAL V AS BAR (
              SOURCE = #data,
              MAPPINGS (X = Region, Y = Amount),
              ACTIONS (ON_CLICK = OPEN_URL(
                TEMPLATE = 'https://example.com/orders/{Region}?amount={Amount}',
                PARAMS = (Region, Amount),
                TARGET = '_self'))
            );
            """;
        var action = Assert.IsType<OpenUrlAction>(Assert.Single(ParseVisual(sql).Actions));
        Assert.True(action.IsTemplate);
        Assert.Equal("https://example.com/orders/{Region}?amount={Amount}", action.Url);
        Assert.Equal(["Region", "Amount"], action.Params);
        Assert.Equal("_self", action.Target);
    }

    [Fact]
    public void OpenUrlTemplate_RoundTripsThroughTheFormatter()
    {
        const string sql = """
            CREATE VISUAL V AS BAR (
              SOURCE = #data,
              MAPPINGS (X = Region, Y = Amount),
              ACTIONS (ON_CLICK = OPEN_URL(TEMPLATE = 'https://example.com/{Region}', PARAMS = (Region)))
            );
            """;
        var formatted = Assert.Single(ParseVisual(sql).Actions).ToSql();
        Assert.Equal("OPEN_URL(TEMPLATE = 'https://example.com/{Region}', PARAMS = (Region))", formatted);

        // The formatted form has to parse back to the same action, or a saved script drifts.
        var reparsed = Assert.IsType<OpenUrlAction>(Assert.Single(ParseVisual(
            $"CREATE VISUAL V AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Amount), ACTIONS (ON_CLICK = {formatted}));").Actions));
        Assert.True(reparsed.IsTemplate);
        Assert.Equal(["Region"], reparsed.Params);
    }

    [Fact]
    public void OpenUrl_LiteralFormStillParsesAndDoesNotClaimToBeATemplate()
    {
        const string sql = """
            CREATE VISUAL V AS BAR (
              SOURCE = #data, MAPPINGS (X = Region, Y = Amount),
              ACTIONS (ON_CLICK = OPEN_URL('https://example.com', TARGET = '_self'))
            );
            """;
        var action = Assert.IsType<OpenUrlAction>(Assert.Single(ParseVisual(sql).Actions));
        Assert.False(action.IsTemplate);
        Assert.Empty(action.Params);
        Assert.Equal("OPEN_URL('https://example.com', TARGET = '_self')", action.ToSql());
    }

    [Fact]
    public void OpenUrl_ParamsWithoutTemplateIsRejected()
    {
        const string sql = """
            CREATE VISUAL V AS BAR (
              SOURCE = #data, MAPPINGS (X = Region, Y = Amount),
              ACTIONS (ON_CLICK = OPEN_URL('https://example.com/{Region}', PARAMS = (Region)))
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Contains(script.Diagnostics,
            diagnostic => diagnostic.Message.Contains("PARAMS requires the TEMPLATE form", StringComparison.Ordinal));
    }

    [Fact]
    public void EmitFilter_RecordsItsTargetsAsAVisualOption()
    {
        const string sql = """
            CREATE VISUAL Source AS BAR (
              SOURCE = #data,
              MAPPINGS (X = Region, Y = Amount),
              EMIT_FILTER (TARGETS = (Detail, Trend))
            );
            """;
        var statement = ParseVisual(sql);
        var option = Assert.Single(statement.Options,
            item => item.Key.Equals("EMIT_FILTER:TARGETS", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Detail,Trend", option.Value);
    }

    [Fact]
    public void EmitFilter_RequiresAtLeastOneTarget()
    {
        const string sql = "CREATE VISUAL S AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Amount), EMIT_FILTER (TARGETS = ()));";
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Contains(script.Diagnostics,
            diagnostic => diagnostic.Message.Contains("TARGETS requires at least one visual name", StringComparison.Ordinal));
    }

    [Fact]
    public void EmitFilterTargets_ResolveFromTheSourceVisualAndDefaultToEveryReceiver()
    {
        const string script = """
            CREATE VISUAL Source AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Amount),
              EMIT_FILTER (TARGETS = (Detail, Trend)));
            CREATE VISUAL Plain AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Amount));
            """;
        var context = new SystemExecutionContext();
        foreach (var statement in Parse(script).Statements.OfType<CreateVisualStatement>())
            context.ReportContext.VisualDefinitions[statement.Name] = statement;

        var targets = ReportInteractionRefresher.ResolveEmitFilterTargets(context, "Source");
        Assert.NotNull(targets);
        Assert.Contains("Detail", targets);
        Assert.Contains("Trend", targets);
        Assert.DoesNotContain("Elsewhere", targets);

        // No clause on the source, or no source at all, means every receiver still responds.
        Assert.Null(ReportInteractionRefresher.ResolveEmitFilterTargets(context, "Plain"));
        Assert.Null(ReportInteractionRefresher.ResolveEmitFilterTargets(context, null));
        Assert.Null(ReportInteractionRefresher.ResolveEmitFilterTargets(context, "NotAVisual"));
    }

    [Theory]
    [InlineData("LTTB")]
    [InlineData("AVERAGE")]
    [InlineData("MAX")]
    [InlineData("MIN")]
    public void Sampling_ReducesDrawnPointsWhileKeepingRealRows(string mode)
    {
        var manifest = DenseSeries(4000);
        var full = CountLineVertices(ResolveNamed(Line("GRID_LINES = ON"), DenseSeries(4000)));
        var sampled = ResolveNamed(Line($"SAMPLING = {mode}"), manifest);
        var reduced = CountLineVertices(sampled);

        Assert.True(reduced < full, $"{mode} drew {reduced} vertices, no fewer than the unsampled {full}");
        Assert.True(reduced > 10, $"{mode} collapsed the series to {reduced} vertices");

        // The plan still carries every row: sampling is a rendering approximation, not a data loss.
        var layer = Assert.Single(sampled.Layers, item => item.Mark == MarkKind.Line);
        Assert.Equal(4000, layer.Data.Length);
    }

    [Fact]
    public void Sampling_None_DrawsEveryPoint()
    {
        var full = CountLineVertices(ResolveNamed(Line("GRID_LINES = ON"), DenseSeries(4000)));
        var explicitNone = CountLineVertices(ResolveNamed(Line("SAMPLING = NONE"), DenseSeries(4000)));
        Assert.Equal(full, explicitNone);
    }

    [Fact]
    public void Sampling_LeavesShortSeriesAlone()
    {
        var plain = CountLineVertices(ResolveNamed(Line("GRID_LINES = ON"), DenseSeries(20)));
        var sampled = CountLineVertices(ResolveNamed(Line("SAMPLING = LTTB"), DenseSeries(20)));
        Assert.Equal(plain, sampled);
    }

    [Fact]
    public void LargeDataOptions_AreRejectedWhereTheyCannotApply()
    {
        var wrongVisual = Assert.Throws<InvalidOperationException>(() => ResolveNamed(
            "CREATE VISUAL V AS PIE (SOURCE = #data, MAPPINGS (LABEL = XValue, VALUE = YValue), OPTIONS (SAMPLING = LTTB));",
            DenseSeries(10)));
        Assert.Contains("supported only on LINE, SCATTER, BUBBLE, and COMBO", wrongVisual.Message, StringComparison.Ordinal);

        var badMode = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamed(Line("SAMPLING = MEDIAN"), DenseSeries(10)));
        Assert.Contains("NONE, LTTB, AVERAGE, MAX, or MIN", badMode.Message, StringComparison.Ordinal);

        var orphanChunk = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamed(Line("PROGRESSIVE_CHUNK = 500"), DenseSeries(10)));
        Assert.Contains("requires the PROGRESSIVE toggle", orphanChunk.Message, StringComparison.Ordinal);

        var badChunk = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamed(Line("PROGRESSIVE = ON, PROGRESSIVE_CHUNK = 0"), DenseSeries(10)));
        Assert.Contains("positive row count", badChunk.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolboxAndZoomGroupOptions_ReachThePlanStyleAndRejectBadValues()
    {
        var plan = ResolveNamed(
            Line("SHOW_EXPORT = ON, SHOW_DATA_VIEW = ON, ZOOM_GROUP = 'timeline', PROGRESSIVE = ON, PROGRESSIVE_CHUNK = 250"),
            DenseSeries(10));

        // ON reaches the option list as the parser's boolean spelling; the runtime accepts both.
        Assert.Contains(plan.Style, token => token.Name == "SHOW_EXPORT" && IsOn(token.Value));
        Assert.Contains(plan.Style, token => token.Name == "SHOW_DATA_VIEW" && IsOn(token.Value));
        Assert.Contains(plan.Style, token => token.Name == "ZOOM_GROUP" && token.Value == "timeline");
        Assert.Contains(plan.Style, token => token.Name == "PROGRESSIVE_CHUNK" && token.Value == "250");

        var bad = Assert.Throws<InvalidOperationException>(() =>
            ResolveNamed(Line("SHOW_EXPORT = MAYBE"), DenseSeries(10)));
        Assert.Contains("Valid values are ON or OFF", bad.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooltipFieldList_ParsesFormatsAndRoundTrips()
    {
        const string sql = """
            CREATE VISUAL V AS BAR (
              SOURCE = #data,
              MAPPINGS (X = Region, Y = Amount),
              TOOLTIP (FIELDS = (Region, Amount FORMAT 'C0', Share FORMAT 'P1'))
            );
            """;
        var tooltip = ParseVisual(sql).Tooltip;
        Assert.NotNull(tooltip);
        Assert.NotNull(tooltip.Fields);
        Assert.Equal(3, tooltip.Fields.Count);
        Assert.Equal(new TooltipField("Region", null), tooltip.Fields[0]);
        Assert.Equal(new TooltipField("Amount", "C0"), tooltip.Fields[1]);
        Assert.Equal(new TooltipField("Share", "P1"), tooltip.Fields[2]);

        // A field list carries no visuals, so it stays a transient tooltip, not a popover.
        Assert.Equal(DetailSurfaceKind.Transient, tooltip.Kind);
        Assert.True(tooltip.IsInline);

        Assert.Contains("FIELDS (Region, Amount FORMAT 'C0', Share FORMAT 'P1')",
            ParseVisual(sql).ToSql(), StringComparison.Ordinal);
    }

    [Fact]
    public void TooltipFieldList_RequiresAtLeastOneField()
    {
        const string sql = "CREATE VISUAL V AS BAR (SOURCE = #data, MAPPINGS (X = Region, Y = Amount), TOOLTIP (FIELDS = ()));";
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Contains(script.Diagnostics,
            diagnostic => diagnostic.Message.Contains("FIELDS requires at least one field", StringComparison.Ordinal));
    }

    private static bool IsOn(string value) =>
        value.Equals("ON", StringComparison.OrdinalIgnoreCase) || value.Equals("True", StringComparison.OrdinalIgnoreCase);

    private static string Line(string options) =>
        $"CREATE VISUAL V AS LINE (SOURCE = #data, MAPPINGS (X = XValue, Y = YValue), OPTIONS ({options}));";

    /// <summary>Counts the vertices actually drawn for the primary line, which is what sampling reduces.</summary>
    private static int CountLineVertices(PlotPlan plan)
    {
        var document = XDocument.Parse(new SvgChartRenderer().Render(plan));
        var path = document.Descendants()
            .First(element => element.Name.LocalName == "path" && (string?)element.Attribute("fill") == "none");
        return ((string)path.Attribute("d")!).Split([" L "], StringSplitOptions.None).Length;
    }

    private static VisualManifest DenseSeries(int count)
    {
        var rows = new List<List<string?>>(count);
        for (var index = 0; index < count; index++)
        {
            // A sawtooth with a slow drift: LTTB has real extremes to preserve.
            var value = (index % 37) * (index % 5 == 0 ? 3 : 1) + index / 100;
            rows.Add([index.ToString(), value.ToString()]);
        }
        return new VisualManifest { Name = "V", Columns = ["XValue", "YValue"], Rows = rows };
    }

    private static Script Parse(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        return script;
    }

    private static CreateVisualStatement ParseVisual(string sql) =>
        Assert.Single(Parse(sql).Statements.OfType<CreateVisualStatement>());

    private static PlotPlan ResolveNamed(string sql, VisualManifest manifest)
    {
        var statement = ParseVisual(sql);
        var spec = new NamedVisualChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        return new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
    }
}
