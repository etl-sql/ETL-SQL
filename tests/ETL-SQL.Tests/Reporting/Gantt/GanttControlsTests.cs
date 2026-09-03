using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Gantt;

public class GanttControlsTests
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

    private static (ChartSpec Spec, VisualManifest Manifest) ParseAndLower(
        string script,
        List<List<string>>? rows = null,
        List<string>? columns = null)
    {
        var statement = ParseVisual(script);
        var cols = columns ?? (statement.Mappings.Count > 0 ? statement.Mappings.Select(m => m.Column).ToList() : ["Task", "StartDate", "EndDate"]);
        var defaultRows = rows ??
        [
            ["Design", "2026-01-01", "2026-01-10"],
            ["Development", "2026-01-11", "2026-01-25"],
            ["Deploy", "2026-01-26", "2026-01-26"]
        ];

        var manifest = new VisualManifest
        {
            Name = statement.Name,
            VisualType = statement.VisualType.ToString().ToUpperInvariant(),
            Columns = cols,
            Rows = defaultRows
        };
        foreach (var opt in statement.Options)
        {
            manifest.Options[opt.Key] = opt.Value;
        }

        var lowerer = new NamedVisualChartLowerer();
        var spec = lowerer.Lower(statement, manifest);
        return (spec, manifest);
    }

    private static string RenderToSvg(string script, List<List<string>>? rows = null, List<string>? columns = null)
    {
        var (spec, manifest) = ParseAndLower(script, rows, columns);
        var dataBuilder = new VisualChartDataBuilder();
        var data = dataBuilder.Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 800, 500));
        return new SvgChartRenderer().Render(plan);
    }

    [Fact]
    public void Gantt_TodayLine_RendersDashedLineAndTodayText()
    {
        var script = @"CREATE VISUAL TodayGantt AS GANTT (
            SOURCE = #tasks,
            MAPPINGS (Y = Task, START = StartDate, END = EndDate),
            OPTIONS (
                TODAY_LINE = ON,
                TODAY_COLOR = '#ff0000',
                TODAY_DATE = '2026-01-15'
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Sprint 1", "2026-01-01", "2026-01-20" },
            new() { "Sprint 2", "2026-01-21", "2026-02-10" }
        };

        var svg = RenderToSvg(script, rows, ["Task", "StartDate", "EndDate"]);

        Assert.NotNull(svg);
        Assert.Contains("stroke='#ff0000' stroke-width='1.5' stroke-dasharray='4,3'", svg);
        Assert.Contains(">Today</text>", svg);
    }

    [Fact]
    public void Gantt_LabelPosition_Inside_RendersInsideBarWithWhiteText()
    {
        var script = @"CREATE VISUAL InsideGantt AS GANTT (
            SOURCE = #tasks,
            MAPPINGS (Y = Task, START = StartDate, END = EndDate),
            OPTIONS (
                LABEL_POSITION = INSIDE
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Long Task Planning", "2026-01-01", "2026-01-30" }
        };

        var svg = RenderToSvg(script, rows, ["Task", "StartDate", "EndDate"]);

        Assert.NotNull(svg);
        Assert.Contains("fill='#ffffff'>Long Task Planning</text>", svg);
    }

    [Fact]
    public void Gantt_LabelPosition_Right_RendersToRightOfBar()
    {
        var script = @"CREATE VISUAL RightGantt AS GANTT (
            SOURCE = #tasks,
            MAPPINGS (Y = Task, START = StartDate, END = EndDate),
            OPTIONS (
                LABEL_POSITION = RIGHT
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Quick Task", "2026-01-01", "2026-01-05" }
        };

        var svg = RenderToSvg(script, rows, ["Task", "StartDate", "EndDate"]);

        Assert.NotNull(svg);
        Assert.Contains("text-anchor='start' font-size='9' fill='#4b5563'>Quick Task</text>", svg);
    }

    [Fact]
    public void Gantt_LabelPosition_None_OmitsTaskLabels()
    {
        var script = @"CREATE VISUAL NoLabelGantt AS GANTT (
            SOURCE = #tasks,
            MAPPINGS (Y = Task, START = StartDate, END = EndDate),
            OPTIONS (
                LABEL_POSITION = NONE
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "HiddenTaskLabel", "2026-01-01", "2026-01-05" }
        };

        var svg = RenderToSvg(script, rows, ["Task", "StartDate", "EndDate"]);

        Assert.NotNull(svg);
        Assert.DoesNotContain(">HiddenTaskLabel</text>", svg);
    }

    [Fact]
    public void Gantt_MilestoneMarkers_ExplicitAndImplicit()
    {
        var script = @"CREATE VISUAL MilestoneGantt AS GANTT (
            SOURCE = #tasks,
            MAPPINGS (
                Y = Task,
                START = StartDate,
                END = EndDate,
                MILESTONE = IsMile
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Task Bar", "2026-01-01", "2026-01-10", "0" },
            new() { "Explicit Milestone", "2026-01-10", "2026-01-15", "1" },
            new() { "Implicit Milestone", "2026-01-15", "2026-01-15", "0" }
        };

        var svg = RenderToSvg(script, rows, ["Task", "StartDate", "EndDate", "IsMile"]);

        Assert.NotNull(svg);
        // Both explicit milestone and implicit (start == end) render diamond SVG paths
        Assert.Contains("<title>Explicit Milestone</title></path>", svg);
        Assert.Contains("<title>Implicit Milestone</title></path>", svg);
        Assert.Contains("<rect", svg); // The standard task renders rect
    }

    [Fact]
    public void Gantt_PredecessorDependencies_RendersElbowArrowMarker()
    {
        var script = @"CREATE VISUAL DepGantt AS GANTT (
            SOURCE = #tasks,
            MAPPINGS (
                Y = Task,
                START = StartDate,
                END = EndDate,
                DEPENDS_ON = Pred
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Task 1", "2026-01-01", "2026-01-10", "" },
            new() { "Task 2", "2026-01-11", "2026-01-20", "Task 1" }
        };

        var svg = RenderToSvg(script, rows, ["Task", "StartDate", "EndDate", "Pred"]);

        Assert.NotNull(svg);
        Assert.Contains("<marker id='gantt-arrow'", svg);
        Assert.Contains("marker-end='url(#gantt-arrow)'", svg);
    }

    [Fact]
    public void Gantt_SwimLanes_GroupMapping_RendersSectionHeaderBands()
    {
        var script = @"CREATE VISUAL PhasedGantt AS GANTT (
            SOURCE = #tasks,
            MAPPINGS (
                GROUP = Phase,
                Y = Task,
                START = StartDate,
                END = EndDate
            )
        );";

        var rows = new List<List<string>>
        {
            new() { "Phase 1: Inception", "Scoping", "2026-01-01", "2026-01-10" },
            new() { "Phase 1: Inception", "Architecture", "2026-01-11", "2026-01-20" },
            new() { "Phase 2: Execution", "Development", "2026-01-21", "2026-02-15" }
        };

        var svg = RenderToSvg(script, rows, ["Phase", "Task", "StartDate", "EndDate"]);

        Assert.NotNull(svg);
        Assert.Contains("fill='#f1f5f9'", svg); // Group header band
        Assert.Contains("font-weight='bold' fill='#334155'>Phase 1: Inception</text>", svg);
        Assert.Contains("font-weight='bold' fill='#334155'>Phase 2: Execution</text>", svg);
    }

    [Theory]
    [InlineData("TODAY_LINE", "MAYBE")]
    [InlineData("LABEL_POSITION", "TOP")]
    public void Gantt_InvalidOptions_ThrowDescriptiveExceptions(string optionKey, string optionValue)
    {
        var script = $@"CREATE VISUAL BadOptGantt AS GANTT (
            SOURCE = #tasks,
            MAPPINGS (Y = Task, START = StartDate, END = EndDate),
            OPTIONS ({optionKey} = '{optionValue}')
        );";

        var ex = Assert.Throws<InvalidOperationException>(() => ParseAndLower(script));
        Assert.Contains(optionKey, ex.Message);
    }
}
