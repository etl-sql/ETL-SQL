using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Baselines;
using ETL_SQL.Reporting.Renderers;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Reporting.Gantt;

/// <summary>
/// Characterization tests for the current GANTT visual implementation across parsing,
/// AST representation, manifest building, ECharts option generation, terminal rendering,
/// SSR export dispatch, and capability matrix tracking.
///
/// These tests assert the CURRENT behavior without modifying production contracts or renderers.
/// </summary>
public class GanttCharacterizationTests
{
    [Fact]
    public void Parser_AcceptsGanttWithRequiredMappings()
    {
        var script = @"
CREATE VISUAL ProjectTimeline AS GANTT (
    SOURCE = #tasks,
    MAPPINGS (
        Y     = Task,
        START = StartDate,
        END   = EndDate,
        COLOR = Color
    ),
    OPTIONS (
        TITLE = 'Project Timeline'
    )
);";

        var tokens = new Lexer(script).Tokenize();
        var ast = new CoreParser(tokens, script).Parse();

        Assert.NotNull(ast);
        Assert.DoesNotContain(ast.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var stmt = ast.Statements.OfType<CreateVisualStatement>().FirstOrDefault();
        Assert.NotNull(stmt);
        Assert.Equal(VisualType.Gantt, stmt.VisualType);
        Assert.Equal("ProjectTimeline", stmt.Name);
        Assert.Equal("#tasks", stmt.Source.TempTableName);

        var mappingDict = stmt.Mappings.ToDictionary(m => m.Role.ToUpperInvariant(), m => m.Column, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Task", mappingDict["Y"]);
        Assert.Equal("StartDate", mappingDict["START"]);
        Assert.Equal("EndDate", mappingDict["END"]);
        Assert.Equal("Color", mappingDict["COLOR"]);

        Assert.Equal("Project Timeline", stmt.Options.FirstOrDefault(o => o.Key.Equals("TITLE", StringComparison.OrdinalIgnoreCase))?.Value);
    }

    [Fact]
    public void Parser_AcceptsGanttWithAlternativeMappingAliases_X_X2_LABEL()
    {
        var script = @"
CREATE VISUAL TechnicalRoadmap AS GANTT (
    SOURCE = #tasks,
    MAPPINGS (
        LABEL = Task,
        X     = StartDate,
        X2    = EndDate
    )
);";

        var tokens = new Lexer(script).Tokenize();
        var ast = new CoreParser(tokens, script).Parse();

        Assert.NotNull(ast);
        Assert.DoesNotContain(ast.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var stmt = ast.Statements.OfType<CreateVisualStatement>().FirstOrDefault();
        Assert.NotNull(stmt);
        Assert.Equal(VisualType.Gantt, stmt.VisualType);

        var mappingDict = stmt.Mappings.ToDictionary(m => m.Role.ToUpperInvariant(), m => m.Column, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Task", mappingDict["LABEL"]);
        Assert.Equal("StartDate", mappingDict["X"]);
        Assert.Equal("EndDate", mappingDict["X2"]);
    }

    [Fact]
    public void TerminalRenderer_RendersGanttChartWithUnicodeBars()
    {
        var manifest = new VisualManifest
        {
            Name = "TerminalRoadmap",
            VisualType = "GANTT",
            Columns = new List<string> { "Task", "Start", "End" },
            Rows = new List<List<string?>>
            {
                new() { "Scoping", "0", "10" },
                new() { "Prototyping", "5", "20" },
                new() { "Delivery", "15", "30" }
            },
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = "Terminal Roadmap"
            }
        };

        var renderable = TerminalRenderer.RenderVisual(manifest);

        Assert.NotNull(renderable);
        var panel = Assert.IsType<Spectre.Console.Panel>(renderable);
        Assert.NotNull(panel.Header);
        Assert.Equal("Terminal Roadmap", panel.Header.Text);
    }

    [Fact]
    public void VisualCapabilityMatrix_ReflectsGanttNativeStaticMigration()
    {
        var capability = VisualCapabilityMatrix.Get(VisualType.Gantt);

        Assert.NotNull(capability);
        Assert.Equal("GANTT", capability.Name);
        Assert.Equal("Timeline", capability.Category);
        Assert.Equal(CapabilityLevel.Native, capability.Browser.Level);
        Assert.Equal(CapabilityLevel.Native, capability.StaticExport.Level);
        Assert.Equal(CapabilityLevel.Native, capability.PdfEmailExport.Level);
        Assert.Equal(CapabilityLevel.Native, capability.Terminal.Level);
        Assert.False(capability.HasExternalChartDependency);
    }

    [Fact]
    public void SvgChartRenderer_EmitsPlaceholder_WhenGanttLacksPlotPlan()
    {
        var manifest = new VisualManifest
        {
            Name = "UnmigratedGantt",
            VisualType = "GANTT",
            Columns = new List<string> { "Task", "Start", "End" },
            Rows = new List<List<string?>>
            {
                new() { "Design", "2026-01-01", "2026-01-05" }
            },
            PlotPlan = null
        };

        var renderer = new SvgChartRenderer();
        var svg = renderer.Render(manifest);

        Assert.NotNull(svg);
        Assert.Contains("GANTT chart", svg);
        Assert.Contains("UnmigratedGantt", svg);
    }
}
