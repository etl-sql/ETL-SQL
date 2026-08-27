using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.HtmlVisual;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.TerminalSemantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting.HtmlVisual;

public class HtmlVisualParserTests
{
    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        return new Parser(tokens, sql).Parse();
    }

    [Fact]
    public void ParseCreateVisual_Html_BasicSingle()
    {
        const string sql = """
            CREATE VISUAL StatusCard AS HTML (
                SOURCE = #data,
                TEMPLATE = '<div>{{Name}}</div>'
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal(VisualType.Html, stmt.VisualType);
        Assert.NotNull(stmt.HtmlTemplate);
        Assert.Equal("<div>{{Name}}</div>", stmt.HtmlTemplate!.Template);
        Assert.Equal(HtmlVisualMode.Single, stmt.HtmlTemplate.Mode);
        Assert.Null(stmt.HtmlTemplate.Css);
        Assert.Null(stmt.HtmlTemplate.Fallback);
    }

    [Fact]
    public void ParseCreateVisual_Html_RepeaterWithStyleAndFallback()
    {
        const string sql = """
            CREATE VISUAL NodeList AS HTML (
                SOURCE = #nodes,
                MODE = REPEATER,
                TEMPLATE = '<article class="card">{{HostName}}</article>',
                STYLE ( CSS = '.card { padding: 1rem; }' ),
                FALLBACK = 'Node: {{HostName}}'
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal(VisualType.Html, stmt.VisualType);
        Assert.NotNull(stmt.HtmlTemplate);
        Assert.Equal(HtmlVisualMode.Repeater, stmt.HtmlTemplate!.Mode);
        Assert.Contains("card", stmt.HtmlTemplate.Template);
        Assert.Equal(".card { padding: 1rem; }", stmt.HtmlTemplate.Css);
        Assert.Equal("Node: {{HostName}}", stmt.HtmlTemplate.Fallback);
    }

    [Fact]
    public void ParseCreateVisual_Html_SourceFree()
    {
        const string sql = """
            CREATE VISUAL Banner AS HTML (
                TEMPLATE = '<h1>Welcome</h1>'
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal(VisualType.Html, stmt.VisualType);
        Assert.NotNull(stmt.Source);
    }

    [Fact]
    public void ParseCreateVisual_Html_RepeaterWithoutSource_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS HTML (
                MODE = REPEATER,
                TEMPLATE = '<div>{{X}}</div>'
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_MissingTemplate_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS HTML (
                SOURCE = #data
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_WithMappings_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS HTML (
                SOURCE = #data,
                TEMPLATE = '<div>test</div>',
                MAPPINGS (X = Col1)
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_WithChart_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS HTML (
                SOURCE = #data,
                TEMPLATE = '<div>test</div>',
                CHART (
                    COORDINATE (TYPE = CARTESIAN),
                    LAYERS (
                        LAYER BAR (X = FIELD(Cat), Y = FIELD(Val))
                    ),
                    RESOLVE (X = SHARED, Y = SHARED, COLOR = SHARED)
                )
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_TemplateOnNonHtml_Fails()
    {
        const string sql = """
            CREATE VISUAL Bad AS BAR (
                SOURCE = #data,
                TEMPLATE = '<div>test</div>',
                MAPPINGS (X = Cat, Y = Val)
            );
            """;
        var script = Parse(sql);
        Assert.NotEmpty(script.Diagnostics);
    }

    [Fact]
    public void ParseCreateVisual_Html_WithActions()
    {
        const string sql = """
            CREATE VISUAL Tile AS HTML (
                SOURCE = #data,
                TEMPLATE = '<div>{{Name}}</div>',
                ACTIONS (
                    ON_CLICK = SET_PARAMETER(@selected, Name)
                )
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Single(stmt.Actions);
    }

    [Fact]
    public void ParseCreateVisual_Html_RoundTrip()
    {
        const string sql = """
            CREATE VISUAL Card AS HTML (
                SOURCE = #data,
                MODE = REPEATER,
                TEMPLATE = '<div>{{Name}}</div>',
                STYLE ( CSS = '.x { color: red; }' ),
                FALLBACK = '{{Name}}'
            );
            """;
        var script = Parse(sql);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        var formatted = stmt.ToSql();
        Assert.Contains("AS HTML", formatted);
        Assert.Contains("MODE = REPEATER", formatted);
        Assert.Contains("TEMPLATE =", formatted);
    }

    [Fact]
    public void ParseCreateVisual_Html_OrAlter()
    {
        const string sql = """
            CREATE OR ALTER VISUAL Card AS HTML (
                SOURCE = #data,
                TEMPLATE = '<p>{{Value}}</p>'
            );
            """;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        var stmt = script.Statements.OfType<CreateVisualStatement>().Single();
        Assert.Equal(ObjectCreationMode.CreateOrAlter, stmt.Mode);
        Assert.Equal(VisualType.Html, stmt.VisualType);
    }
}

public class HtmlTemplateEvaluatorTests
{
    private readonly HtmlTemplateEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_SimpleFieldSubstitution()
    {
        var row = new Dictionary<string, object?> { ["Name"] = "Alice", ["Age"] = 30m };
        var result = _evaluator.Evaluate("<p>{{Name}} is {{Age}}</p>", row, null);
        Assert.Equal("<p>Alice is 30</p>", result);
    }

    [Fact]
    public void Evaluate_ParameterSubstitution()
    {
        var parms = new Dictionary<string, object?> { ["@region"] = "West" };
        var result = _evaluator.Evaluate("<p>Region: {{@region}}</p>", null, parms);
        Assert.Equal("<p>Region: West</p>", result);
    }

    [Fact]
    public void Evaluate_HtmlEncodes_FieldValues()
    {
        var row = new Dictionary<string, object?> { ["Name"] = "<script>alert(1)</script>" };
        var result = _evaluator.Evaluate("<p>{{Name}}</p>", row, null);
        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;script&gt;", result);
    }

    [Fact]
    public void Evaluate_HtmlEncodes_SpecialChars()
    {
        var row = new Dictionary<string, object?> { ["Val"] = "a&b<c>d\"e'f/g" };
        var result = _evaluator.Evaluate("{{Val}}", row, null);
        Assert.Equal("a&amp;b&lt;c&gt;d&quot;e&#x27;f&#x2F;g", result);
    }

    [Fact]
    public void Evaluate_Conditional_Equals()
    {
        var row = new Dictionary<string, object?> { ["Status"] = "Critical" };
        var template = "{{#IF Status = 'Critical'}}<b>ALERT</b>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("<b>ALERT</b>", result);
    }

    [Fact]
    public void Evaluate_Conditional_NotEquals()
    {
        var row = new Dictionary<string, object?> { ["Status"] = "OK" };
        var template = "{{#IF Status = 'Critical'}}<b>ALERT</b>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.DoesNotContain("ALERT", result);
    }

    [Fact]
    public void Evaluate_Conditional_IsNull()
    {
        var row = new Dictionary<string, object?> { ["Status"] = null };
        var template = "{{#IF Status IS NULL}}<i>N/A</i>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("<i>N/A</i>", result);
    }

    [Fact]
    public void Evaluate_Conditional_IsNotNull()
    {
        var row = new Dictionary<string, object?> { ["Status"] = "Active" };
        var template = "{{#IF Status IS NOT NULL}}<span>{{Status}}</span>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("<span>Active</span>", result);
    }

    [Fact]
    public void Evaluate_Conditional_NumericComparison()
    {
        var row = new Dictionary<string, object?> { ["Pct"] = 95m };
        var template = "{{#IF Pct >= 90}}<span>HIGH</span>{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("HIGH", result);
    }

    [Fact]
    public void Evaluate_Conditional_NestedDepth4()
    {
        var row = new Dictionary<string, object?> { ["A"] = "1", ["B"] = "1", ["C"] = "1", ["D"] = "1" };
        var template = "{{#IF A = '1'}}{{#IF B = '1'}}{{#IF C = '1'}}{{#IF D = '1'}}OK{{/IF}}{{/IF}}{{/IF}}{{/IF}}";
        var result = _evaluator.Evaluate(template, row, null);
        Assert.Contains("OK", result);
    }

    [Fact]
    public void Evaluate_Conditional_NestedDepth5_Throws()
    {
        var row = new Dictionary<string, object?> { ["A"] = "1", ["B"] = "1", ["C"] = "1", ["D"] = "1", ["E"] = "1" };
        var template = "{{#IF A = '1'}}{{#IF B = '1'}}{{#IF C = '1'}}{{#IF D = '1'}}{{#IF E = '1'}}X{{/IF}}{{/IF}}{{/IF}}{{/IF}}{{/IF}}";
        Assert.Throws<HtmlTemplateException>(() => _evaluator.Evaluate(template, row, null));
    }

    [Fact]
    public void Evaluate_UnmatchedConditional_Throws()
    {
        var template = "{{#IF Status = 'X'}}content without closing";
        Assert.Throws<HtmlTemplateException>(() => _evaluator.Evaluate(template,
            new Dictionary<string, object?> { ["Status"] = "X" }, null));
    }

    [Fact]
    public void Evaluate_NullFieldValue_EmptyOutput()
    {
        var row = new Dictionary<string, object?> { ["Name"] = null };
        var result = _evaluator.Evaluate("<p>{{Name}}</p>", row, null);
        Assert.Equal("<p></p>", result);
    }

    [Fact]
    public void Evaluate_CaseInsensitive_FieldLookup()
    {
        var row = new Dictionary<string, object?> { ["HostName"] = "srv1" };
        var result = _evaluator.Evaluate("{{hostname}}", row, null);
        Assert.Equal("srv1", result);
    }

    [Fact]
    public void EvaluateRepeater_MultipleRows()
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Name"] = "A" },
            new Dictionary<string, object?> { ["Name"] = "B" },
            new Dictionary<string, object?> { ["Name"] = "C" },
        };
        var result = _evaluator.EvaluateRepeater("<li>{{Name}}</li>", rows, null, 500);
        Assert.Equal("<li>A</li><li>B</li><li>C</li>", result);
    }

    [Fact]
    public void EvaluateRepeater_RespectsMaxRows()
    {
        var rows = Enumerable.Range(1, 100)
            .Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["N"] = i.ToString() })
            .ToList();
        var result = _evaluator.EvaluateRepeater("{{N}} ", rows, null, 5);
        Assert.Equal(5, result.Split(" ", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void EvaluateFallback_PlainText_NoEncoding()
    {
        var row = new Dictionary<string, object?> { ["Name"] = "A<B" };
        var result = _evaluator.EvaluateFallback("Name: {{Name}}", row, null);
        Assert.Equal("Name: A<B", result);
    }

    [Fact]
    public void Evaluate_FormatSpec()
    {
        var row = new Dictionary<string, object?> { ["Pct"] = 0.142m };
        var result = _evaluator.Evaluate("{{Pct FORMAT '0.0%'}}", row, null,
            (val, fmt) => val is decimal d ? d.ToString(fmt) : val?.ToString() ?? "");
        Assert.Equal("14.2%", result);
    }

    [Fact]
    public void Evaluate_VisualEmbed_ProjectsDeterministicDescriptor()
    {
        var row = new Dictionary<string, object?> { ["Region"] = "West" };
        var parameters = new Dictionary<string, object?> { ["@year"] = 2026m };
        HtmlVisualEmbedRequest? captured = null;

        var result = _evaluator.Evaluate(
            "<section>{{VISUAL(RegionalSales, PARAMETERS(@region = Region, @year = @year))}}</section>",
            row,
            parameters,
            renderEmbed: request =>
            {
                captured = request;
                return "<div data-etl-embed-id=\"embed-0\"></div>";
            });

        Assert.Equal("<section><div data-etl-embed-id=\"embed-0\"></div></section>", result);
        Assert.NotNull(captured);
        Assert.Equal("RegionalSales", captured.TargetName);
        Assert.Equal("West", captured.Parameters["@region"]);
        Assert.Equal("2026", captured.Parameters["@year"]);
        Assert.Contains("@year", captured.SourceParameters);
    }

    [Theory]
    [InlineData("{{VISUAL()}}")]
    [InlineData("{{VISUAL(Target, PARAMETERS(@p))}}")]
    [InlineData("{{VISUAL(Target, PARAMETERS(@p = javascript:bad))}}")]
    public void Evaluate_VisualEmbed_MalformedSyntaxFailsClosed(string template)
    {
        Assert.Throws<HtmlTemplateException>(() =>
            _evaluator.Evaluate(template, null, null, renderEmbed: _ => string.Empty));
    }

    [Fact]
    public void Evaluate_MicroChartHelper_ResolvesTypedRequestWithoutEmittingAuthorMarkup()
    {
        var row = new Dictionary<string, object?> { ["Trend"] = "[1,3,2]" };
        HtmlMicroChartRequest? captured = null;

        var result = _evaluator.Evaluate(
            "<div>{{SPARKLINE(Trend, TYPE=\"AREA\", COLOR=\"#123abc\", WIDTH=200, HEIGHT=40)}}</div>",
            row,
            null,
            renderMicroChart: request =>
            {
                captured = request;
                return "<span data-etl-microchart-id=\"chart-0\"></span>";
            });

        Assert.Equal("<div><span data-etl-microchart-id=\"chart-0\"></span></div>", result);
        Assert.NotNull(captured);
        Assert.Equal("Trend", captured.Expression.Field);
        Assert.Equal("AREA", captured.Expression.Type);
        Assert.Equal("#123abc", captured.Expression.Color);
        Assert.Equal(200, captured.Expression.Width);
        Assert.Equal("[1,3,2]", captured.Value);
    }

    [Theory]
    [InlineData("{{SPARKLINE(Trend, TYPE=PIE)}}")]
    [InlineData("{{SPARKLINE(Trend, COLOR=javascript)}}")]
    [InlineData("{{PROGRESS_BAR(Value, MIN=10, MAX=10)}}")]
    [InlineData("{{PROGRESS_BAR(Value, WIDTH=200)}}")]
    public void Evaluate_MicroChartHelper_InvalidOptionsFailClosed(string template)
    {
        Assert.Throws<HtmlTemplateException>(() =>
            _evaluator.Evaluate(template, new Dictionary<string, object?>(), null, renderMicroChart: _ => string.Empty));
    }
}

public class HtmlVisualManifestTests
{
    [Fact]
    public async System.Threading.Tasks.Task Manifest_ResolvesDeclaredVisualEmbedAndAccountsForCost()
    {
        var manifest = await BuildAsync("""
            SELECT 42 AS Value INTO #metric;
            CREATE VISUAL Metric AS CARD (SOURCE = #metric);
            CREATE VISUAL Host AS HTML (
              TEMPLATE = '<section>{{VISUAL(Metric)}}</section>',
              FALLBACK = 'Metric summary'
            );
            """);

        var host = Assert.Single(manifest.Visuals, visual => visual.Name == "Host");
        var embed = Assert.Single(host.HtmlEmbeds!);
        Assert.Equal("Metric", embed.TargetName);
        Assert.Null(embed.Visual);
        Assert.Contains($"data-etl-embed-id=\"{embed.Id}\"", host.HtmlContent);
        Assert.NotNull(host.HtmlCost);
        Assert.Null(host.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task Manifest_ParameterizedEmbedBuildsDetachedTargetWithBoundValue()
    {
        var manifest = await BuildAsync("""
            DECLARE @region VARCHAR(20) INPUT = 'All';
            CREATE VISUAL Metric AS CARD (SOURCE = (SELECT @region AS Region));
            CREATE VISUAL Host AS HTML (
              TEMPLATE = '<section>{{VISUAL(Metric, PARAMETERS(@region = ''West''))}}</section>',
              FALLBACK = 'Regional metric'
            );
            """);

        var embed = Assert.Single(manifest.Visuals.Single(visual => visual.Name == "Host").HtmlEmbeds!);
        Assert.NotNull(embed.Visual);
        Assert.Equal("West", embed.Visual.Rows[0][0]);
        Assert.Equal("All", manifest.Parameters["@region"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task Manifest_EmbedCycleFailsClosedWithoutRenderedMarkup()
    {
        var manifest = await BuildAsync("""
            CREATE VISUAL A AS HTML (TEMPLATE = '<div>{{VISUAL(B)}}</div>');
            CREATE VISUAL B AS HTML (TEMPLATE = '<div>{{VISUAL(A)}}</div>');
            """);

        Assert.All(manifest.Visuals, visual =>
        {
            Assert.Contains("RPT3010", visual.Error);
            Assert.Null(visual.HtmlContent);
        });
        Assert.NotNull(manifest.Error);
    }

    [Fact]
    public void SourceFreeParameterBindingParticipatesInAtomicRefreshDependencyGraph()
    {
        var sql = "DECLARE @region VARCHAR(20) = 'All'; CREATE VISUAL Host AS HTML (TEMPLATE = '<p>{{@region}}</p>');";
        var statement = Parse(sql).Statements.OfType<CreateVisualStatement>().Single();

        Assert.True(ReportInteractionRefresher.DependsOnVariable(statement, "@region"));
    }

    [Fact]
    public async System.Threading.Tasks.Task SourceFreeParameterRefreshPublishesOneCompleteHtmlManifest()
    {
        const string sql = "DECLARE @region VARCHAR(20) INPUT = 'All'; CREATE VISUAL Host AS HTML (TEMPLATE = '<p>{{@region}}</p>', FALLBACK = 'Region {{@region}}');";
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;
        await evaluator.Evaluate(Parse(sql));
        var manifest = await new ManifestBuilder(evaluator, maxVisualParallelism: 1).BuildAsync("refresh.rptsql");

        var refreshed = await ReportInteractionRefresher.RefreshAffectedVisualsAsync(
            evaluator,
            manifest,
            [("@region", "West")]);

        Assert.Equal(1, refreshed);
        var visual = Assert.Single(manifest.Visuals);
        Assert.Equal("<p>West</p>", visual.HtmlContent);
        Assert.Equal("Region West", visual.HtmlFallback);
        Assert.Equal("West", manifest.Parameters["@region"]);
        Assert.NotNull(visual.HtmlCost);
    }

    [Fact]
    public async System.Threading.Tasks.Task Manifest_HtmlOutputIsByteDeterministic()
    {
        const string sql = "CREATE VISUAL Host AS HTML (TEMPLATE = '<p>Stable</p>', FALLBACK = 'Stable');";

        var first = await BuildAsync(sql);
        var second = await BuildAsync(sql);

        Assert.Equal(first.Visuals.Single().HtmlContent, second.Visuals.Single().HtmlContent);
        Assert.Equal(first.Visuals.Single().HtmlCss, second.Visuals.Single().HtmlCss);
        var firstCost = first.Visuals.Single().HtmlCost!;
        var secondCost = second.Visuals.Single().HtmlCost!;
        Assert.Equal(
            (firstCost.TemplateBytes, firstCost.CssBytes, firstCost.TemplateNodes, firstCost.OutputNodes, firstCost.OutputBytes, firstCost.RenderWork),
            (secondCost.TemplateBytes, secondCost.CssBytes, secondCost.TemplateNodes, secondCost.OutputNodes, secondCost.OutputBytes, secondCost.RenderWork));
    }

    [Fact]
    public async System.Threading.Tasks.Task Manifest_AppliesTypedFormattingToHtmlAndFallback()
    {
        var manifest = await BuildAsync("""
            SELECT 0.123 AS Ratio INTO #metric;
            CREATE VISUAL Host AS HTML (
              SOURCE = #metric,
              TEMPLATE = '<p>{{Ratio FORMAT ''0.0%''}}</p>',
              FALLBACK = 'Ratio {{Ratio FORMAT ''0.0%''}}'
            );
            """);

        var visual = Assert.Single(manifest.Visuals);
        Assert.Equal("<p>12.3%</p>", visual.HtmlContent);
        Assert.Equal("Ratio 12.3%", visual.HtmlFallback);
    }

    [Fact]
    public async System.Threading.Tasks.Task Manifest_CompilesInlineMicroChartsToServerResolvedSvgAndSemanticText()
    {
        var manifest = await BuildAsync("""
            SELECT '[1,3,2,5]' AS Trend, 75 AS Completion INTO #metric;
            CREATE VISUAL Host AS HTML (
              SOURCE = #metric,
              TEMPLATE = '<div>{{SPARKLINE(Trend, TYPE="AREA", COLOR="#123abc", WIDTH=200, HEIGHT=40)}}{{PROGRESS_BAR(Completion, MIN=0, MAX=100, COLOR="#0a0", HEIGHT=18)}}</div>',
              FALLBACK = 'Service indicators'
            );
            """);

        var visual = Assert.Single(manifest.Visuals);
        Assert.Null(visual.Error);
        Assert.Equal(2, visual.MicroCharts?.Count);
        Assert.All(visual.MicroCharts!, chart =>
        {
            Assert.Equal("html.inline", chart.Role);
            Assert.StartsWith("<svg", chart.Svg);
            Assert.NotNull(chart.PlotPlan);
            Assert.Contains($"data-etl-microchart-id=\"{chart.Id}\"", visual.HtmlContent);
        });
        Assert.Contains("Trend: first 1, last 5, range 1–5", visual.HtmlFallback);
        Assert.Contains("Progress: 75 of 100 (75%)", visual.HtmlFallback);
        Assert.NotNull(visual.HtmlCost);
    }

    [Fact]
    public async System.Threading.Tasks.Task Manifest_RepeaterCompilesOneInlineMicroChartPerRowWithUniqueIds()
    {
        var manifest = await BuildAsync("""
            CREATE TABLE #metrics (Completion INT);
            INSERT INTO #metrics VALUES (20), (80);
            CREATE VISUAL Host AS HTML (
              SOURCE = #metrics,
              MODE = REPEATER,
              TEMPLATE = '<p>{{PROGRESS_BAR(Completion)}}</p>'
            );
            """);

        var visual = Assert.Single(manifest.Visuals);
        Assert.Equal(2, visual.MicroCharts?.Count);
        Assert.Equal(2, visual.MicroCharts!.Select(chart => chart.Id).Distinct().Count());
        Assert.All(visual.MicroCharts, chart => Assert.Equal("html.inline", chart.Role));
    }

    [Theory]
    [InlineData("not-json", "JSON numeric array")]
    [InlineData("[1,true,3]", "only numbers or nulls")]
    public async System.Threading.Tasks.Task Manifest_InvalidSparklineDataFailsClosed(string trend, string expected)
    {
        var sql = $"SELECT '{trend}' AS Trend INTO #metric; "
            + "CREATE VISUAL Host AS HTML (SOURCE = #metric, TEMPLATE = '<div>{{SPARKLINE(Trend)}}</div>');";
        var manifest = await BuildAsync(sql);

        var visual = Assert.Single(manifest.Visuals);
        Assert.Contains("RPT3015", visual.Error);
        Assert.Contains(expected, visual.Error);
        Assert.Null(visual.HtmlContent);
        Assert.Null(visual.MicroCharts);
    }

    [Fact]
    public async System.Threading.Tasks.Task Manifest_RejectsAggregateOutputNodeBudget()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var manifest = new ReportManifest
        {
            Visuals = Enumerable.Range(1, 6).Select(index => new VisualManifest
            {
                Name = $"H{index}",
                VisualType = "HTML",
                HtmlContent = "<span>x</span>",
                HtmlCost = new HtmlVisualCostManifest
                {
                    OutputNodes = 10_000,
                    OutputBytes = 100_000,
                    RenderWork = 10_000
                }
            }).ToList()
        };

        await new ManifestBuilder(evaluator).PrepareHtmlVisualsAsync(manifest);

        Assert.All(manifest.Visuals, visual => Assert.Contains("RPT3027", visual.Error));
    }

    [Fact]
    public async System.Threading.Tasks.Task Manifest_RejectsEmbeddedQueryBudget()
    {
        var embeds = string.Concat(Enumerable.Range(0, 101)
            .Select(index => $"{{{{VISUAL(Metric, PARAMETERS(@region = 'R{index}'))}}}}"));
        var sqlEmbeds = embeds.Replace("'", "''", StringComparison.Ordinal);
        var manifest = await BuildAsync($"""
            DECLARE @region VARCHAR(20) INPUT = 'All';
            CREATE VISUAL Metric AS CARD (SOURCE = (SELECT @region AS Region));
            CREATE VISUAL Host AS HTML (TEMPLATE = '<div>{sqlEmbeds}</div>');
            """);

        Assert.Contains("RPT3029", manifest.Visuals.Single(visual => visual.Name == "Host").Error);
    }

    private static async System.Threading.Tasks.Task<ReportManifest> BuildAsync(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;
        var script = Parse(sql);
        Assert.Empty(script.Diagnostics);
        await evaluator.Evaluate(script);
        return await new ManifestBuilder(evaluator, maxVisualParallelism: 1).BuildAsync("html-visual-test.rptsql");
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
}

public class HtmlVisualBudgetTests
{
    [Fact]
    public void ValidateAuthored_TemplateByteBudget_FailsClosedWithDiagnosticCode()
    {
        var template = new string('x', HtmlVisualBudgets.MaxTemplateBytes + 1);

        var ex = Assert.Throws<HtmlVisualBudgetException>(() =>
            HtmlVisualBudgets.ValidateAuthored(template, null, 0, HtmlVisualBudgets.DefaultMaxRows, false));

        Assert.Equal("RPT3020", ex.Code);
        Assert.Equal(HtmlVisualBudgets.MaxTemplateBytes + 1, ex.Actual);
    }

    [Fact]
    public void ValidateAuthored_RepeaterRowBudget_FailsInsteadOfTruncating()
    {
        var ex = Assert.Throws<HtmlVisualBudgetException>(() =>
            HtmlVisualBudgets.ValidateAuthored("<p>{{Value}}</p>", null, 6, 5, true));

        Assert.Equal("RPT3023", ex.Code);
    }

    [Fact]
    public void ValidateAuthored_OutputNodeBudget_AccountsForEveryRepeatedRow()
    {
        var template = string.Concat(Enumerable.Repeat("<span>x</span>", 101));

        var ex = Assert.Throws<HtmlVisualBudgetException>(() =>
            HtmlVisualBudgets.ValidateAuthored(template, null, 100, 100, true));

        Assert.Equal("RPT3024", ex.Code);
        Assert.Equal(10_100, ex.Actual);
    }

    [Fact]
    public void ValidateRendered_OutputByteBudget_UsesUtf8Bytes()
    {
        var authored = HtmlVisualBudgets.ValidateAuthored("<p>x</p>", null, 0, 500, false);
        var output = new string('\u00e9', (HtmlVisualBudgets.MaxOutputBytes / 2) + 1);

        var ex = Assert.Throws<HtmlVisualBudgetException>(() =>
            HtmlVisualBudgets.ValidateRendered(authored, output));

        Assert.Equal("RPT3025", ex.Code);
    }

    [Fact]
    public void ValidateRendered_PublishesDeterministicCost()
    {
        var authored = HtmlVisualBudgets.ValidateAuthored("<p>x</p>", ".x { color: red; }", 0, 500, false);

        var first = HtmlVisualBudgets.ValidateRendered(authored, "<p>x</p>");
        var second = HtmlVisualBudgets.ValidateRendered(authored, "<p>x</p>");

        Assert.Equal(first, second);
        Assert.Equal(1, first.OutputNodes);
        Assert.True(first.RenderWork > 0);
    }

    [Fact]
    public void ValidateRendered_AccountsForTrustedMicroChartSvg()
    {
        var authored = HtmlVisualBudgets.ValidateAuthored("<p>x</p>", null, 0, 500, false);

        var cost = HtmlVisualBudgets.ValidateRendered(authored, "<p>x</p>", ["<svg><path></path></svg>"]);

        Assert.Equal(3, cost.OutputNodes);
        Assert.True(cost.OutputBytes > Encoding.UTF8.GetByteCount("<p>x</p>"));
    }
}

public class HtmlVisualStaticFallbackTests
{
    [Fact]
    public void SemanticFallback_UsesResolvedPlainTextWithoutInteractionClaims()
    {
        var visual = new VisualManifest
        {
            Name = "NodeStatus",
            VisualType = "HTML",
            HtmlFallback = "Node db-01: critical"
        };

        var fallback = VisualSemanticFallbackBuilder.Build(visual);

        Assert.Equal("Node db-01: critical", fallback.Summary);
        Assert.Empty(fallback.Items);
        Assert.DoesNotContain("click", fallback.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Markdown_UsesFencedSemanticFallbackAndNeverEmitsAuthoredMarkup()
    {
        var visual = new VisualManifest
        {
            Name = "NodeStatus",
            VisualType = "HTML",
            HtmlContent = "<button type=\"button\">Restart</button><script>alert(1)</script>",
            HtmlFallback = "Node db-01: critical"
        };
        visual.SemanticFallback = VisualSemanticFallbackBuilder.Build(visual);

        var markdown = new MarkdownRenderer().Render(new ReportManifest { Visuals = [visual] });

        Assert.Contains("```text\nNode db-01: critical\n```", markdown.Replace("\r\n", "\n"));
        Assert.DoesNotContain("<button", markdown);
        Assert.DoesNotContain("<script", markdown);
    }

    [Fact]
    public void StaticPdf_UsesSemanticFallbackAndProducesValidPdf()
    {
        var visual = new VisualManifest
        {
            Name = "NodeStatus",
            VisualType = "HTML",
            HtmlContent = "<script>alert('never')</script><button type=\"button\">Restart</button>",
            HtmlFallback = "Node db-01: critical"
        };
        visual.SemanticFallback = VisualSemanticFallbackBuilder.Build(visual);

        var bytes = new PdfExporter().Export(new ReportManifest { Title = "HTML fallback", Visuals = [visual] });

        Assert.True(bytes.Length > 100);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void SnapshotSerialization_PreservesOnlyTheDeterministicManifestContract()
    {
        var manifest = new ReportManifest
        {
            Visuals =
            [
                new VisualManifest
                {
                    Name = "Status",
                    VisualType = "HTML",
                    HtmlContent = "<p>Healthy</p>",
                    HtmlCss = "#etl-v-status p { color: var(--etl-success); }",
                    HtmlFallback = "Status: healthy",
                    HtmlEmbeds = [new HtmlVisualEmbedManifest { Id = "Status-embed-0", TargetName = "Metric" }]
                }
            ]
        };

        var json = JsonSerializer.Serialize(manifest);

        Assert.Contains("\\u003Cp\\u003EHealthy\\u003C/p\\u003E", json);
        Assert.Contains("Status-embed-0", json);
        Assert.Contains("Status: healthy", json);
    }

    [Fact]
    public void Terminal_UsesSemanticFallbackWithoutAuthoredMarkupOrInteractionClaims()
    {
        var visual = new VisualManifest
        {
            Name = "Status",
            VisualType = "HTML",
            HtmlContent = "<button>Restart</button><script>alert(1)</script>",
            HtmlFallback = "Status: healthy"
        };
        visual.SemanticFallback = VisualSemanticFallbackBuilder.Build(visual);

        var snapshot = TerminalSnapshotHarness.CaptureSnapshot(TerminalRenderer.RenderVisual(visual), 80).NormalizedText;

        Assert.Contains("Status: healthy", snapshot);
        Assert.DoesNotContain("Restart", snapshot);
        Assert.DoesNotContain("click", snapshot, StringComparison.OrdinalIgnoreCase);
    }
}

public class HtmlVisualAnalysisTests
{
    [Fact]
    public async System.Threading.Tasks.Task Lint_ReportsUnknownFieldAndParameterWithStableCodes()
    {
        var script = Parse("""
            DECLARE @known VARCHAR(20) = 'ok';
            CREATE VISUAL Status AS HTML (
              SOURCE = (SELECT Name FROM #nodes),
              TEMPLATE = '<p>{{Missing}} {{@unknown}} {{@known}}</p>'
            );
            """);

        var results = await new HtmlVisualAuthoringRule().AnalyzeAsync(script, new DefaultLintContext());

        Assert.Contains(results, result => result.Code == "RPT3001" && result.Message.Contains("Missing"));
        Assert.Contains(results, result => result.Code == "RPT3002" && result.Message.Contains("@unknown"));
        Assert.DoesNotContain(results, result => result.Message.Contains("@known"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Lint_UsesSharedSanitizerPolicy()
    {
        var script = Parse("""
            CREATE VISUAL Unsafe AS HTML (
              TEMPLATE = '<img src="javascript:alert(1)" onerror="steal()">'
            );
            """);

        var results = (await new HtmlVisualAuthoringRule().AnalyzeAsync(script, new DefaultLintContext())).ToList();

        Assert.Contains(results, result => result.Code == "RPT3012" && result.Message.Contains("onerror"));
        Assert.Contains(results, result => result.Code == "RPT3012" && result.Message.Contains("allowed"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Lint_RejectsMissingEmbedCycleAndExcessDepth()
    {
        var script = Parse("""
            CREATE VISUAL A AS HTML (TEMPLATE = '<div>{{VISUAL(B)}} {{VISUAL(Missing)}}</div>');
            CREATE VISUAL B AS HTML (TEMPLATE = '<div>{{VISUAL(C)}}</div>');
            CREATE VISUAL C AS HTML (TEMPLATE = '<div>{{VISUAL(A)}}</div>');
            CREATE VISUAL D AS HTML (TEMPLATE = '<div>{{VISUAL(E)}}</div>');
            CREATE VISUAL E AS HTML (TEMPLATE = '<div>{{VISUAL(F)}}</div>');
            CREATE VISUAL F AS HTML (TEMPLATE = '<div>{{VISUAL(G)}}</div>');
            CREATE VISUAL G AS CARD (SOURCE = (SELECT Value FROM #metrics));
            """);

        var results = (await new HtmlVisualAuthoringRule().AnalyzeAsync(script, new DefaultLintContext())).ToList();

        Assert.Contains(results, result => result.Code == "RPT3010" && result.Message.Contains("Missing"));
        Assert.Contains(results, result => result.Code == "RPT3010" && result.Message.Contains("cycle"));
        Assert.Contains(results, result => result.Code == "RPT3011");
    }

    [Fact]
    public async System.Threading.Tasks.Task LinterFactory_DiscoversHtmlVisualRule()
    {
        var script = Parse("CREATE VISUAL Unsafe AS HTML (TEMPLATE = '<script>x</script>');");

        var results = await LinterFactory.CreateWithAllRules().AnalyzeAsync(script, new DefaultLintContext());

        Assert.Contains(results, result => result.RuleName == "HtmlVisualAuthoring" && result.Code == "RPT3012");
    }

    [Fact]
    public async System.Threading.Tasks.Task Lint_RejectsMalformedEmbedAndSensitiveDisclosure()
    {
        var script = Parse("""
            DECLARE @token SENSITIVE = 'ENC:value';
            CREATE VISUAL Unsafe AS HTML (
              TEMPLATE = '<p>{{@token}}</p><div>{{VISUAL(Target, PARAMETERS(@token = @token}}</div>'
            );
            CREATE VISUAL Target AS CARD (SOURCE = (SELECT Value FROM #metrics));
            """);

        var results = (await new HtmlVisualAuthoringRule().AnalyzeAsync(script, new DefaultLintContext())).ToList();

        Assert.Contains(results, result => result.Code == "RPT3010" && result.Message.Contains("Invalid VISUAL"));
        Assert.Contains(results, result => result.Code == "RPT3014" && result.Message.Contains("@token"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Lint_ValidatesMicroChartSyntaxAndSourceFields()
    {
        var script = Parse("""
            CREATE VISUAL Indicators AS HTML (
              SOURCE = (SELECT Trend FROM #metrics),
              TEMPLATE = '<div>{{SPARKLINE(Missing)}} {{PROGRESS_BAR(Trend, MIN=10, MAX=5)}}</div>'
            );
            """);

        var results = (await new HtmlVisualAuthoringRule().AnalyzeAsync(script, new DefaultLintContext())).ToList();

        Assert.Contains(results, result => result.Code == "RPT3001" && result.Message.Contains("Missing"));
        Assert.Contains(results, result => result.Code == "RPT3015" && result.Message.Contains("greater than MIN"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Lint_MalformedMicroChartUsesStableDiagnostic()
    {
        var script = Parse("CREATE VISUAL Indicators AS HTML (TEMPLATE = '<div>{{SPARKLINE}}</div>');");

        var results = await new HtmlVisualAuthoringRule().AnalyzeAsync(script, new DefaultLintContext());

        Assert.Contains(results, result => result.Code == "RPT3015");
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
}

public class HtmlVisualDocumentationTests
{
    [Fact]
    public void FocusedHelpExamples_AreAcceptedByParser()
    {
        var root = FindRepoRoot();
        var markdown = File.ReadAllText(Path.Combine(root, "docs", "reference", "visuals-reporting", "visuals", "html.md"));
        var examples = markdown[(markdown.IndexOf("## Examples", StringComparison.Ordinal))..];
        var blocks = Regex.Matches(examples, "```sql\\s*(?<sql>[\\s\\S]*?)```")
            .Select(match => match.Groups["sql"].Value).ToList();

        Assert.Equal(2, blocks.Count);
        foreach (var sql in blocks)
        {
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
            Assert.Empty(script.Diagnostics);
        }
    }

    [Fact]
    public void ProductionSample_ParsesEveryHtmlClause()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "samples", "08_Reporting", "constrained_html_components.rptsql");
        var sql = File.ReadAllText(path);

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        Assert.Empty(script.Diagnostics);
        var visuals = script.Statements.OfType<CreateVisualStatement>().Where(visual => visual.VisualType == VisualType.Html).ToList();
        Assert.Equal(2, visuals.Count);
        Assert.Contains(visuals, visual => visual.Source.TempTableName is null);
        Assert.Contains(visuals, visual => visual.HtmlTemplate?.Mode == HtmlVisualMode.Repeater
            && visual.HtmlTemplate.Css is not null && visual.HtmlTemplate.Fallback is not null
            && visual.Actions.Count > 0);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ETL-SQL.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate ETL-SQL.slnx.");
    }
}

public class HtmlSanitizerTests
{
    private readonly HtmlSanitizer _sanitizer = new();

    // ── Element allowlist ────────────────────────────────────────────────

    [Theory]
    [InlineData("<div>safe</div>")]
    [InlineData("<span class=\"x\">ok</span>")]
    [InlineData("<article><h1>Title</h1><p>Text</p></article>")]
    [InlineData("<table><tr><td>cell</td></tr></table>")]
    [InlineData("<ul><li>item</li></ul>")]
    [InlineData("<a href=\"https://example.com\">link</a>")]
    [InlineData("<img src=\"https://example.com/img.png\" alt=\"photo\">")]
    [InlineData("<button type=\"button\">Click</button>")]
    [InlineData("<details><summary>More</summary><p>info</p></details>")]
    [InlineData("<meter min=\"0\" max=\"100\" value=\"75\"></meter>")]
    public void ValidateTemplate_AllowedElements_NoViolations(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.Empty(violations);
    }

    // ── Script injection (T-1) ───────────────────────────────────────────

    [Fact]
    public void ValidateTemplate_ScriptElement_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<script>alert(1)</script>");
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Element);
    }

    [Fact]
    public void ValidateTemplate_StyleElement_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<style>.x{color:red}</style>");
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Element);
    }

    // ── Event handler injection (T-2) ────────────────────────────────────

    [Theory]
    [InlineData("<div onclick=\"alert(1)\">x</div>")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">")]
    [InlineData("<div onmousedown=\"alert(1)\">x</div>")]
    [InlineData("<div onmouseover=\"steal()\">hover</div>")]
    public void ValidateTemplate_EventHandlers_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Attribute);
    }

    [Fact]
    public void ValidateTemplate_RuntimeReservedAttributes_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate(
            "<div data-etl-embed-id=\"forged\"></div><span data-etl-microchart-id=\"forged\"></span>");

        Assert.Equal(2, violations.Count(v => v.Category == SanitizationCategory.Attribute));
    }

    // ── JavaScript URL injection (T-3) ───────────────────────────────────

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    [InlineData("<a href=\"vbscript:msgbox\">click</a>")]
    [InlineData("<a href=\"blob:http://evil\">click</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">click</a>")]
    public void ValidateTemplate_UnsafeUrls_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Url);
    }

    [Theory]
    [InlineData("<a href=\"{{Field}}\">link</a>")]
    [InlineData("<a href=\"{{@param}}\">link</a>")]
    public void ValidateTemplate_SubstitutionAtUrlSchemePosition_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Url);
    }

    [Theory]
    [InlineData("<a href=\"https://example.com/{{Id}}\">link</a>")]
    [InlineData("<a href=\"mailto:{{Email}}\">mail</a>")]
    [InlineData("<a href=\"#{{Section}}\">jump</a>")]
    public void ValidateTemplate_SubstitutionAfterSafeScheme_Allowed(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateTemplate_DataImageUrl_Allowed()
    {
        var violations = _sanitizer.ValidateTemplate("<img src=\"data:image/png;base64,abc\" alt=\"icon\">");
        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateTemplate_DataImageMimePrefixConfusion_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<img src=\"data:image/png-script,abc\" alt=\"icon\">");

        Assert.Contains(violations, v => v.Category == SanitizationCategory.Url);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<svg onload='alert(1)'></svg>")]
    [InlineData("<foreignObject><p>x</p></foreignObject>")]
    public void ValidateTemplate_DataSvgScriptPayload_Rejected(string svg)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        var violations = _sanitizer.ValidateTemplate($"<img src=\"data:image/svg+xml;base64,{encoded}\" alt=\"icon\">");

        Assert.NotEmpty(violations);
    }

    [Theory]
    [InlineData("<script")]
    [InlineData("<!-- hidden -->")]
    [InlineData("<!doctype html>")]
    public void ValidateTemplate_MalformedOrDocumentMarkup_Rejected(string template)
    {
        Assert.NotEmpty(_sanitizer.ValidateTemplate(template));
    }

    // ── Iframe/embed escape (T-5) ────────────────────────────────────────

    [Theory]
    [InlineData("<iframe src=\"https://evil.com\"></iframe>")]
    [InlineData("<object data=\"evil.swf\"></object>")]
    [InlineData("<embed src=\"evil.swf\">")]
    [InlineData("<applet code=\"Evil.class\"></applet>")]
    [InlineData("<form action=\"https://evil.com\"><input></form>")]
    public void ValidateTemplate_FrameAndEmbed_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Category == SanitizationCategory.Element);
    }

    // ── DOM mutation (T-6) ───────────────────────────────────────────────

    [Theory]
    [InlineData("<base href=\"https://evil.com\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=evil\">")]
    [InlineData("<link rel=\"stylesheet\" href=\"evil.css\">")]
    public void ValidateTemplate_DocumentMutation_Rejected(string template)
    {
        var violations = _sanitizer.ValidateTemplate(template);
        Assert.NotEmpty(violations);
    }

    // ── SVG script injection (T-7) ───────────────────────────────────────

    [Fact]
    public void ValidateTemplate_InlineSvg_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<svg onload=\"alert(1)\"><circle r=\"10\"/></svg>");
        Assert.NotEmpty(violations);
    }

    // ── Inline style attribute (T-11) ────────────────────────────────────

    [Fact]
    public void ValidateTemplate_InlineStyleAttribute_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<div style=\"background:url(evil)\">x</div>");
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Message.Contains("style"));
    }

    // ── Button type validation (T-13) ────────────────────────────────────

    [Fact]
    public void ValidateTemplate_ButtonTypeSubmit_Rejected()
    {
        var violations = _sanitizer.ValidateTemplate("<button type=\"submit\">Go</button>");
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidateTemplate_ButtonTypeButton_Allowed()
    {
        var violations = _sanitizer.ValidateTemplate("<button type=\"button\">Click</button>");
        Assert.Empty(violations);
    }

    // ── CSS sanitization ─────────────────────────────────────────────────

    [Fact]
    public void ValidateCss_SafeCss_NoViolations()
    {
        var css = ".card { padding: 1rem; color: var(--etl-text); border: 1px solid var(--etl-border); }";
        Assert.Empty(_sanitizer.ValidateCss(css));
    }

    [Theory]
    [InlineData("@import url('evil.css');")]
    [InlineData("@font-face { font-family: x; src: url('evil.woff'); }")]
    [InlineData(".x { background: expression(alert(1)); }")]
    [InlineData(".x { -moz-binding: url('evil.xml#xbl'); }")]
    [InlineData(".x { behavior: url(evil.htc); }")]
    public void ValidateCss_UnsafePatterns_Rejected(string css)
    {
        var violations = _sanitizer.ValidateCss(css);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidateCss_ExternalUrl_Rejected()
    {
        var violations = _sanitizer.ValidateCss(".x { background: url(https://evil.com/img.png); }");
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidateCss_NonEtlVar_Rejected()
    {
        var violations = _sanitizer.ValidateCss(".x { color: var(--portal-secret); }");
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidateCss_EtlVar_Allowed()
    {
        var violations = _sanitizer.ValidateCss(".x { color: var(--etl-accent); }");
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(".x { background: u/**/rl(https://evil.invalid/pixel); }")]
    [InlineData(".x { color: j\\61vascript:alert(1); }")]
    [InlineData("@supports (display:grid) { .x { display:grid; } }")]
    public void ValidateCss_ObfuscatedOrUnapprovedSyntax_Rejected(string css)
    {
        Assert.NotEmpty(_sanitizer.ValidateCss(css));
    }

    // ── CSS scoping ──────────────────────────────────────────────────────

    [Fact]
    public void ScopeCss_PrefixesSelectors()
    {
        var css = ".card { padding: 1rem; }";
        var scoped = _sanitizer.ScopeCss(css, "etl-v-myvisual");
        Assert.Contains("#etl-v-myvisual .card", scoped);
    }

    [Fact]
    public void ScopeCss_RecursesThroughMediaAndNamespacesKeyframes()
    {
        var css = "@media (max-width: 600px) { .card, .metric { display: grid; } } " +
            "@keyframes pulse { from { opacity: 0; } to { opacity: 1; } } .card { animation: pulse 1s; }";

        var scoped = _sanitizer.ScopeCss(css, "etl-v-test");

        Assert.Contains("@media (max-width: 600px) { #etl-v-test .card, #etl-v-test .metric", scoped);
        Assert.Contains("@keyframes etl-v-test-pulse", scoped);
        Assert.Contains("animation: etl-v-test-pulse 1s", scoped);
        Assert.DoesNotContain("#etl-v-test from", scoped);
    }

    // ── HTML encoding ────────────────────────────────────────────────────

    [Fact]
    public void HtmlEncode_AllDangerousChars()
    {
        var encoded = HtmlTemplateEvaluator.HtmlEncode("&<>\"'/");
        Assert.Equal("&amp;&lt;&gt;&quot;&#x27;&#x2F;", encoded);
    }
}
