using System.Collections.Immutable;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting.TerminalSemantics;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class ConnectedLinePlanTests
{
    [Fact]
    public void GuardedPlanPreservesConnectionsAcrossSerializationAndBackends()
    {
        var plan = Create();
        var json = ChartContractSerializer.Serialize(plan);
        var restored = ChartContractSerializer.DeserializePlotPlan(json);
        Assert.Equal(json, ChartContractSerializer.Serialize(restored));
        Assert.Equal(5, restored.Version);
        var paths = XDocument.Parse(new SvgChartRenderer().Render(restored)).Descendants()
            .Where(element => (string?)element.Attribute("class") == "plot-conditional-connection").ToArray();
        Assert.Equal(2, paths.Length);
        Assert.Equal(new[] { "#ff0000", "#0000ff" }, paths.Select(path => (string?)path.Attribute("stroke")));
        Assert.Equal(new[] { "0", "1" }, paths.Select(path => (string?)path.Attribute("opacity")));
        Assert.Equal(new[] { "0", "1" }, paths.Select(path => (string?)path.Attribute("data-source-index")));
        Assert.Equal(new[] { "1", "2" }, paths.Select(path => (string?)path.Attribute("data-destination-index")));
        // The destination of one segment is the source of the next, including coincident endpoints.
        var firstCoordinates = paths[0].Attribute("d")!.Value.Split(' ');
        var secondCoordinates = paths[1].Attribute("d")!.Value.Split(' ');
        Assert.Equal(firstCoordinates[4..6], secondCoordinates[1..3]);
        var terminal = TerminalSnapshotHarness.CaptureSnapshot(PlotPlanTerminalRenderer.Render(restored), 180).NormalizedText;
        foreach (var connection in restored.Layers[0].Connections)
        {
            var description = ConnectedMarkResolver.Describe(connection);
            Assert.Contains(description, terminal);
            Assert.Contains(paths, path => path.Value == description);
            Assert.Contains(restored.Fallback.Items, item => item.Value == description);
        }
    }

    [Fact]
    public void GuardRejectsDowngradesOmissionsReorderingAndMissingCoordinates()
    {
        var plan = Create();
        var layer = plan.Layers[0];
        Assert.Throws<InvalidDataException>(() => (plan with
        { Schema = ChartContractVersions.PlotPlanSchema, Version = ChartContractVersions.PlotPlanCurrent }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with
        { Layers = [layer with { Connections = layer.Connections.RemoveAt(0) }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with
        { Layers = [layer with { Connections = layer.Connections.Reverse().ToImmutableArray() }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (plan with
        { Layers = [layer with { Data = layer.Data.SetItem(0, layer.Data[0] with { Channels = [] }) }] }).Validate());
    }

    [Fact]
    public void GapsHaveNoVisibleOrSemanticConnection()
    {
        var plan = Create(true);
        Assert.Empty(plan.Layers[0].Connections);
        Assert.DoesNotContain("plot-conditional-connection", new SvgChartRenderer().Render(plan));
        Assert.DoesNotContain(plan.Fallback.Items, item => item.Detail == "outgoing connection");
        Assert.Equal(ChartContractSerializer.Serialize(plan), ChartContractSerializer.Serialize(
            ChartContractSerializer.DeserializePlotPlan(ChartContractSerializer.Serialize(plan))));
    }

    private static PlotPlan Create(bool gap = false)
    {
        const string sql = """
            CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = CARTESIAN),
              LAYERS (route = LINE (NULL_HANDLING = GAP,
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE), Y = Estimate (TYPE = QUANTITATIVE)),
                STYLE (INTERPOLATION = 'LINEAR')))
            ));
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "Route",
            Columns = ["Distance", "Estimate"],
            Rows = [["1", "2"], ["2", "4"], ["3", "3"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
        var layer = Assert.Single(plan.Layers);
        var colors = new[] { "#ff0000", "#0000ff", "#00ff00" };
        layer = layer with
        {
            Data = layer.Data.Select((datum, index) => datum with
            {
                IsGap = gap && index == 1,
                Encodings = [new(ConditionalEncodingChannel.Color, ChartValue.From(colors[index])),
                    new(ConditionalEncodingChannel.Opacity, ChartValue.From(index == 0 ? 0m : 1m))]
            }).ToImmutableArray()
        };
        return ConnectedMarkResolver.Attach(plan with { Layers = [layer] });
    }
}
