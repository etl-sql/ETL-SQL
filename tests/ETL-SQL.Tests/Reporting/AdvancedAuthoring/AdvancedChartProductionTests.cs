using System.Collections.Immutable;
using System.Linq;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using ETL_SQL.Tests.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class AdvancedChartProductionTests(ITestOutputHelper output)
{
    [Fact]
    public void GeographicCustomLayers_ParseLowerResolveAndRenderWithBoundedBuiltInGeometry()
    {
        const string sql = """
            CREATE VISUAL NativeGeography AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = GEOGRAPHIC, PROJECTION = EQUIRECTANGULAR, MAP_NAME = 'WORLD', FEATURE_KEY = 'name'),
                LAYERS (
                  regions = RECT (ENCODINGS (
                    REGION = RegionName (TYPE = NOMINAL),
                    COLOR = RegionValue (TYPE = QUANTITATIVE)
                  )),
                  routes = LINE (Z_INDEX = 1, ENCODINGS (
                    LONGITUDE = Longitude (TYPE = QUANTITATIVE),
                    LATITUDE = Latitude (TYPE = QUANTITATIVE),
                    ROUTE = RouteName (TYPE = NOMINAL)
                  )),
                  points = POINT (Z_INDEX = 2, ENCODINGS (
                    LONGITUDE = Longitude (TYPE = QUANTITATIVE),
                    LATITUDE = Latitude (TYPE = QUANTITATIVE),
                    TEXT = PlaceName (TYPE = NOMINAL)
                  )),
                  labels = TEXT (Z_INDEX = 3, ENCODINGS (
                    LONGITUDE = Longitude (TYPE = QUANTITATIVE),
                    LATITUDE = Latitude (TYPE = QUANTITATIVE),
                    TEXT = PlaceName (TYPE = NOMINAL)
                  ))
                )
              ),
              INTERACTIONS (ON_SELECT = FILTER)
            );
            """;
        var parsed = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(parsed.Diagnostics);
        var statement = Assert.Single(parsed.Statements.OfType<CreateVisualStatement>());
        var formatted = statement.ToSql();
        Assert.Equal(formatted, new Parser(new Lexer(formatted).Tokenize(), formatted).Parse()
            .Statements.OfType<CreateVisualStatement>().Single().ToSql());
        var manifest = new VisualManifest
        {
            Name = "NativeGeography",
            Columns = ["RegionName", "RegionValue", "Longitude", "Latitude", "RouteName", "PlaceName"],
            Rows =
            [
                ["United States of America", "90", "-122.3", "47.6", "west-route", "Seattle"],
                ["Canada", "60", "-79.4", "43.7", "west-route", "Toronto"]
            ]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var geography = GeographicGeometryResolver.Resolve(Assert.IsType<GeographicCoordinateSpec>(spec.Coordinate.Geography), null);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest), geography: geography);
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Equal(CoordinateKind.Geographic, plan.Coordinate!.Kind);
        Assert.StartsWith("builtin:WORLD", plan.Geography!.SourceAuthority, StringComparison.Ordinal);
        Assert.Contains("plot-geographic-region", svg);
        Assert.Contains("plot-geographic-route", svg);
        Assert.Contains("plot-geographic-point", svg);
        Assert.Contains("Seattle", svg);
        Assert.Equal(SemanticFallbackKind.TransitionTable, plan.Fallback.Kind);
        Assert.Equal("RegionName", plan.Interaction!.Key);
        var report = new ReportManifest
        {
            Title = "Geography",
            Source = "geography.rptsql",
            Visuals = [new VisualManifest { Name = "NativeGeography", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }]
        };
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, new PdfExporter().Export(report)[..4]);
        Assert.Contains("west-route", new MarkdownRenderer().Render(report));
    }

    [Theory]
    [InlineData("COORDINATE (TYPE = GEOGRAPHIC, PROJECTION = MERCATOR, MAP_NAME = 'WORLD', MAP_FILE = 'maps/world.geojson')", "exactly one")]
    [InlineData("COORDINATE (TYPE = GEOGRAPHIC, MAP_NAME = 'WORLD')", "require PROJECTION")]
    [InlineData("COORDINATE (TYPE = CARTESIAN, MAP_NAME = 'WORLD')", "require GEOGRAPHIC")]
    public void GeographicCoordinateAuthority_IsRejectedConsistently(string coordinate, string expected)
    {
        var sql = $"""
            CREATE VISUAL InvalidMap AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                {coordinate},
                LAYERS (points = POINT (ENCODINGS (
                  LONGITUDE = Longitude (TYPE = QUANTITATIVE),
                  LATITUDE = Latitude (TYPE = QUANTITATIVE)
                )))
              )
            );
            """;
        var parsed = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(parsed.Statements.OfType<CreateVisualStatement>());
        var diagnostics = AdvancedChartSemanticValidator.Validate(statement);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GeographicMapFile_UsesExecutionContextPathBoundaryAndDoesNotExposeResolvedPath()
    {
        var context = new Mock<IExecutionContext>();
        context.Setup(item => item.ResolvePath("maps/tenant.geojson")).Returns("D:/tenant-root/maps/tenant.geojson");
        var spec = new GeographicCoordinateSpec(GeographicProjectionKind.Mercator,
            GeographicMapSourceKind.File, "maps/tenant.geojson", "region_id");

        var resolved = GeographicGeometryResolver.ResolveMapFile(context.Object, spec);
        var manifest = new VisualManifest { ResolvedMapFile = resolved };
        var json = System.Text.Json.JsonSerializer.Serialize(manifest);

        context.Verify(item => item.ResolvePath("maps/tenant.geojson"), Times.Once);
        Assert.Equal("D:/tenant-root/maps/tenant.geojson", resolved);
        Assert.DoesNotContain("tenant-root", json, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() => GeographicGeometryResolver.ResolveMapFile(context.Object,
            spec with { Source = "maps/tenant.json" }));
    }

    [Fact]
    public void StatisticalAndFinancialRectLayers_RenderAlongsideOrdinaryCustomLayers()
    {
        const string sql = """
            CREATE VISUAL NativeFinance AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (
                  dates = BAND (CHANNEL = X, ORDER = SOURCE),
                  price = LINEAR (CHANNEL = Y, INCLUDE_ZERO = OFF),
                  volume = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = ON)
                ),
                LAYERS (
                  candles = RECT (
                    Z_INDEX = 1,
                    ENCODINGS (
                      X = Day (TYPE = ORDINAL, SCALE = dates),
                      OPEN = OpenValue (TYPE = QUANTITATIVE, SCALE = price),
                      CLOSE = CloseValue (TYPE = QUANTITATIVE, SCALE = price),
                      LOW = LowValue (TYPE = QUANTITATIVE, SCALE = price),
                      HIGH = HighValue (TYPE = QUANTITATIVE, SCALE = price)
                    )
                  ),
                  volume_bars = RECT (
                    Z_INDEX = 0,
                    BAND_SIZE = 0.35,
                    ENCODINGS (
                      X = Day (TYPE = ORDINAL, SCALE = dates),
                      Y2 = Volume (TYPE = QUANTITATIVE, SCALE = volume, AXIS = SECONDARY)
                    ),
                    STYLE (COLOR = '#94a3b8')
                  )
                )
              )
            );
            """;
        var parsed = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(parsed.Diagnostics);
        var statement = Assert.Single(parsed.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "NativeFinance",
            Columns = ["Day", "OpenValue", "CloseValue", "LowValue", "HighValue", "Volume"],
            Rows = [["Mon", "10", "13", "8", "15", "1000"], ["Tue", "13", "11", "9", "16", "1400"]]
        };

        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Equal("price", spec.Layers[0].Bindings.Single(binding => binding.Channel == FieldChannel.Open).ScaleId);
        Assert.Contains("class='plot-candlestick'", svg);
        Assert.Contains("data-extent-axis='y'", svg);
        Assert.Contains("O 10, H 15, L 8, C 13", svg);
    }

    [Fact]
    public void BoxPlotChannels_RenderWithAnIndependentMeanTickLayer()
    {
        const string sql = """
            CREATE VISUAL NativeDistribution AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                ENCODINGS (X = GroupName (TYPE = NOMINAL)),
                LAYERS (
                  boxes = RECT (ENCODINGS (
                    LOW = LowValue (TYPE = QUANTITATIVE),
                    Q1 = FirstQuartile (TYPE = QUANTITATIVE),
                    MEDIAN = MedianValue (TYPE = QUANTITATIVE),
                    Q3 = ThirdQuartile (TYPE = QUANTITATIVE),
                    HIGH = HighValue (TYPE = QUANTITATIVE)
                  )),
                  mean = TICK (
                    Z_INDEX = 1,
                    THICKNESS = 0.3,
                    ENCODINGS (Y = MeanValue (TYPE = QUANTITATIVE))
                  )
                )
              )
            );
            """;
        var parsed = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(parsed.Diagnostics);
        var statement = Assert.Single(parsed.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "NativeDistribution",
            Columns = ["GroupName", "LowValue", "FirstQuartile", "MedianValue", "ThirdQuartile", "HighValue", "MeanValue"],
            Rows = [["A", "1", "3", "5", "7", "9", "5.4"]]
        };

        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("class='plot-boxplot'", svg);
        Assert.Contains("class='plot-tick'", svg);
        var domain = plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain.Select(PlotPlanResolver.Number).ToArray();
        Assert.True(domain[0] <= 1m && domain[^1] >= 9m);
    }

    [Fact]
    public void GlobalEncodings_DatumValueAndInference_LowerToExplicitLayerBindings()
    {
        const string sql = """
            CREATE VISUAL Native AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (
                  score = LINEAR (CHANNEL = X),
                  rank = LINEAR (CHANNEL = Y)
                ),
                ENCODINGS (
                  X = Category (TYPE = NOMINAL),
                  Y = DATUM(1500) (TYPE = QUANTITATIVE)
                ),
                LAYERS (
                  bars = RECT (ENCODINGS (COLOR = VALUE('#c62828') (TYPE = NOMINAL))),
                  isolated = POINT (INHERIT_ENCODINGS = OFF, ENCODINGS (
                    X = Score (TYPE = QUANTITATIVE, SCALE = score),
                    Y = Rank (TYPE = QUANTITATIVE, SCALE = rank)
                  ))
                )
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var formatted = statement.ToSql();
        Assert.Contains("Y = DATUM(1500)", formatted);
        Assert.Contains("COLOR = VALUE('#c62828')", formatted);
        Assert.Contains("INHERIT_ENCODINGS = OFF", formatted);

        var manifest = new VisualManifest
        {
            Name = "Native",
            Columns = ["Category", "Score", "Rank"],
            Rows = [["A", "4", "2"], ["B", "8", "1"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);

        var bars = spec.Layers.Single(layer => layer.Id == "bars");
        Assert.Equal(3, bars.Bindings.Length);
        Assert.Equal(BindingSourceKind.Datum, bars.Bindings.Single(binding => binding.Channel == FieldChannel.Y).SourceKind);
        Assert.Equal(1500m, bars.Bindings.Single(binding => binding.Channel == FieldChannel.Y).Constant!.Decimal);
        Assert.Equal(BindingSourceKind.Value, bars.Bindings.Single(binding => binding.Channel == FieldChannel.Color).SourceKind);
        var isolated = spec.Layers.Single(layer => layer.Id == "isolated");
        Assert.Equal([FieldChannel.X, FieldChannel.Y], isolated.Bindings.Select(binding => binding.Channel));
        Assert.DoesNotContain(isolated.Bindings, binding => binding.Field == "Category");
        Assert.All(spec.Layers.SelectMany(layer => layer.Bindings)
            .Where(binding => binding.SourceKind != BindingSourceKind.Value && binding.Channel is FieldChannel.X or FieldChannel.Y),
            binding => Assert.NotNull(binding.ScaleId));

        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data);
        var resolvedDatum = plan.Layers.Single(layer => layer.Id == "bars").Data[0];
        Assert.Equal(1500m, PlotPlanResolver.Number(resolvedDatum.Channels.Single(channel => channel.Channel == FieldChannel.Y).Value));

        var json = ChartContractSerializer.Serialize(spec);
        var roundTrip = ChartContractSerializer.DeserializeChartSpec(json);
        Assert.Equal(BindingSourceKind.Value,
            roundTrip.Layers.Single(layer => layer.Id == "bars").Bindings.Single(binding => binding.Channel == FieldChannel.Color).SourceKind);
    }

    [Fact]
    public void DatumAndValue_RejectArbitraryExpressions()
    {
        const string sql = """
            CREATE VISUAL Bad AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (points = POINT (ENCODINGS (
                  X = DATUM(Score + 1) (TYPE = QUANTITATIVE),
                  Y = Score (TYPE = QUANTITATIVE)
                )))
              )
            );
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Contains(script.Diagnostics, diagnostic => diagnostic.Message.Contains("DATUM accepts only a scalar literal or declared variable"));
    }

    [Fact]
    public async Task ParameterBindings_ReevaluateAndRejectSecretBearingVariablesWithoutDisclosure()
    {
        const string sql = """
            CREATE VISUAL Target AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                ENCODINGS (X = Category (TYPE = NOMINAL)),
                LAYERS (target_mark = TICK (ENCODINGS (
                  Y = DATUM(@Target) (TYPE = QUANTITATIVE)
                )))
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest { Name = "Target", Columns = ["Category"], Rows = [["A"]] };
        await using var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<ETL_SQL.Engine.Evaluator>();

        evaluator.VarContext.DeclareVariable("@Target", 5m);
        var first = new AdvancedChartLowerer(evaluator).Lower(statement, manifest);
        Assert.Equal(5m, first.Layers[0].Bindings.Single(binding => binding.Channel == FieldChannel.Y).Constant!.Decimal);
        Assert.Equal("@Target", first.Layers[0].Bindings.Single(binding => binding.Channel == FieldChannel.Y).Parameter);

        evaluator.VarContext.SetVariable("@Target", 8m);
        var refreshed = new AdvancedChartLowerer(evaluator).Lower(statement, manifest);
        Assert.Equal(8m, refreshed.Layers[0].Bindings.Single(binding => binding.Channel == FieldChannel.Y).Constant!.Decimal);

        evaluator.VarContext.Reset();
        evaluator.VarContext.DeclareVariable("@Target", "do-not-disclose", new VariableMetadata { IsSecret = true });
        var error = Assert.Throws<AdvancedChartSemanticException>(() => new AdvancedChartLowerer(evaluator).Lower(statement, manifest));
        Assert.Contains("secret-bearing", error.Message);
        Assert.DoesNotContain("do-not-disclose", error.Message);
        // The failure is anchored to the offending DATUM encoding, not to the CREATE VISUAL header.
        var diagnostic = Assert.Single(error.Diagnostics);
        Assert.Equal(AdvancedChartSemanticValidator.DiagnosticCode, diagnostic.Code);
        Assert.Equal(7, diagnostic.Line);
        Assert.True(diagnostic.Line > statement.Line);
        Assert.DoesNotContain("do-not-disclose", diagnostic.Message);
    }

    [Fact]
    public void NormalizeStackAndBandSize_ResolveExplicitGeometryIndependentOfLayerNames()
    {
        const string sql = """
            CREATE VISUAL Stacks AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                ENCODINGS (X = Category (TYPE = NOMINAL)),
                LAYERS (
                  alpha = RECT (BAND_SIZE = 1, ENCODINGS (Y = First (TYPE = QUANTITATIVE, STACK = NORMALIZE))),
                  beta = RECT (BAND_SIZE = 0.5, ENCODINGS (Y = Second (TYPE = QUANTITATIVE, STACK = NORMALIZE)))
                )
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Contains("STACK = NORMALIZE", statement.ToSql());
        Assert.Contains("BAND_SIZE = 0.5", statement.ToSql());
        var manifest = new VisualManifest
        {
            Name = "Stacks",
            Columns = ["Category", "First", "Second"],
            Rows = [["A", "20", "30"], ["B", "10", "30"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data);

        var firstA = plan.Layers.Single(layer => layer.Id == "alpha").Data[0];
        var secondA = plan.Layers.Single(layer => layer.Id == "beta").Data[0];
        Assert.Equal(0m, PlotPlanResolver.Number(firstA.Channels.Single(channel => channel.Channel == FieldChannel.YStart).Value));
        Assert.Equal(.4m, PlotPlanResolver.Number(firstA.Channels.Single(channel => channel.Channel == FieldChannel.YEnd).Value));
        Assert.Equal(.4m, PlotPlanResolver.Number(secondA.Channels.Single(channel => channel.Channel == FieldChannel.YStart).Value));
        Assert.Equal(1m, PlotPlanResolver.Number(secondA.Channels.Single(channel => channel.Channel == FieldChannel.YEnd).Value));
        Assert.Equal(30m, PlotPlanResolver.Number(secondA.Channels.Single(channel => channel.Channel == FieldChannel.Y).Value));
        Assert.Equal(.5m, plan.Layers.Single(layer => layer.Id == "beta").BandSize);

        var renamed = spec with { Layers = spec.Layers.Select((layer, index) => layer with { Id = index == 0 ? "z-renamed" : "a-renamed" }).ToImmutableArray() };
        var renamedPlan = new PlotPlanResolver().Resolve(renamed, data);
        Assert.Equal(
            plan.Layers.SelectMany(layer => layer.Data).SelectMany(datum => datum.Channels).Where(channel => channel.Channel is FieldChannel.YStart or FieldChannel.YEnd).Select(channel => channel.Value),
            renamedPlan.Layers.SelectMany(layer => layer.Data).SelectMany(datum => datum.Channels).Where(channel => channel.Channel is FieldChannel.YStart or FieldChannel.YEnd).Select(channel => channel.Value));
    }

    [Fact]
    public void FloatingAreaRibbon_PreservesEndpointsAndRendersRangeGeometry()
    {
        const string sql = """
            CREATE VISUAL Ribbon AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (periods = POINT (CHANNEL = X), y_range = LINEAR (CHANNEL = Y)),
                LAYERS (forecast = AREA (ENCODINGS (
                  X = Period (TYPE = ORDINAL, SCALE = periods),
                  Y_START = Lower (TYPE = QUANTITATIVE, SCALE = y_range),
                  Y_END = Upper (TYPE = QUANTITATIVE, SCALE = y_range),
                  TOOLTIP = Label (TYPE = NOMINAL)
                )))
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "Ribbon",
            Columns = ["Period", "Lower", "Upper", "Label"],
            Rows = [["Q1", "10", "20", "first"], ["Q2", "25", "12", "reversed"],
                ["Q3", null, "30", "gap"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));

        var domain = plan.Scales.Single(scale => scale.Channel == FieldChannel.Y).Domain.Select(PlotPlanResolver.Number).ToArray();
        Assert.True(domain[0] <= 10m && domain[^1] >= 30m);
        Assert.Equal(25m, PlotPlanResolver.Number(plan.Layers[0].Data[1].Channels.Single(channel => channel.Channel == FieldChannel.YStart).Value));
        Assert.Equal(12m, PlotPlanResolver.Number(plan.Layers[0].Data[1].Channels.Single(channel => channel.Channel == FieldChannel.YEnd).Value));
        Assert.True(plan.Layers[0].Data[2].IsGap);
        Assert.Contains("class='plot-ribbon'", new SvgChartRenderer().Render(plan));
        Assert.Equal("first", plan.Layers[0].Data[0].Channels
            .Single(channel => channel.Channel == FieldChannel.Tooltip).DisplayValue);
    }

    [Fact]
    public void OffsetJitterAndNudge_ResolveDeterministicallyBeforeRendering()
    {
        const string sql = """
            CREATE VISUAL Strip AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                LAYERS (observations = POINT (
                  POSITION = JITTER(X = 0.08, Y = 0.04, KEY = Id, SEED = 42),
                  ENCODINGS (
                    X = Score (TYPE = QUANTITATIVE),
                    Y = Rank (TYPE = QUANTITATIVE),
                    X_OFFSET = Cohort (TYPE = NOMINAL),
                    DETAIL = Id (TYPE = NOMINAL)
                  )
                ))
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Contains("POSITION = JITTER( X = 0.08, Y = 0.04, KEY = Id, SEED = 42 )", statement.ToSql());
        var manifest = new VisualManifest
        {
            Name = "Strip",
            Columns = ["Id", "Score", "Rank", "Cohort"],
            Rows = [["a", "1", "4", "left"], ["b", "2", "3", "right"], ["c", "3", "2", "left"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var first = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 350));
        var repeat = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, 600, 350));
        Assert.Equal(first.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)),
            repeat.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)));
        Assert.NotEqual(first.Layers[0].Data[0].DisplayOffsetX, first.Layers[0].Data[1].DisplayOffsetX);
        Assert.Contains("cx='", new SvgChartRenderer().Render(first));

        var renamed = spec with { Layers = [spec.Layers[0] with { Id = "renamed-without-geometry-change" }] };
        var renamedPlan = new PlotPlanResolver().Resolve(renamed, data, new PlotBounds(0, 0, 600, 350));
        Assert.Equal(first.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)),
            renamedPlan.Layers[0].Data.Select(datum => (datum.DisplayOffsetX, datum.DisplayOffsetY)));

        var reorderedData = data with
        {
            Columns = data.Columns.Select(column => column with
            {
                Values = column.Values.Reverse().ToImmutableArray(),
                DisplayValues = column.DisplayValues.IsDefaultOrEmpty ? column.DisplayValues : column.DisplayValues.Reverse().ToImmutableArray()
            }).ToImmutableArray()
        };
        var reorderedPlan = new PlotPlanResolver().Resolve(spec, reorderedData, new PlotBounds(0, 0, 600, 350));
        static Dictionary<string, (decimal X, decimal Y)> OffsetsByKey(PlotPlan value) => value.Layers[0].Data.ToDictionary(
            datum => PlotPlanResolver.Display(datum.Channels.Single(channel => channel.Channel == FieldChannel.Detail).Value),
            datum => (datum.DisplayOffsetX, datum.DisplayOffsetY), StringComparer.Ordinal);
        Assert.Equal(OffsetsByKey(first), OffsetsByKey(reorderedPlan));

        var nudged = spec with
        {
            Layers = [spec.Layers[0] with
            {
                Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 1m, 2m, Unit: PositionAdjustmentUnit.Data)
            }]
        };
        var nudgedPlan = new PlotPlanResolver().Resolve(nudged, data, new PlotBounds(0, 0, 600, 350));
        Assert.All(nudgedPlan.Layers[0].Data, datum =>
        {
            Assert.NotEqual(0m, datum.DisplayOffsetX);
            Assert.NotEqual(0m, datum.DisplayOffsetY);
        });
        Assert.Equal([1m, 2m, 3m], data.Columns.Single(column => column.Name == "Score").Values.Select(PlotPlanResolver.Number));
    }

    [Fact]
    public void JitterRejectsDuplicateOrNullStableKeys()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "X", DataSemanticKind.Quantitative, "x"),
            new FieldBinding(FieldChannel.Y, "Y", DataSemanticKind.Quantitative, "y"));
        var spec = ChartSpec.Create("bad-jitter", "#data", bindings,
            [new MarkLayerSpec("points", MarkKind.Point, 0, bindings, [])
            {
                Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Jitter, .1m, .1m, "Key")
            }],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Linear, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Bad jitter", null, null, true));
        var data = ChartDataSet.Create("#data", 2,
        [
            new ChartColumn("X", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(1m), ChartValue.From(2m)], []),
            new ChartColumn("Y", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(2m), ChartValue.From(3m)], []),
            new ChartColumn("Key", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("same"), ChartValue.From("same")], [])
        ]);

        var error = Assert.Throws<InvalidOperationException>(() => new PlotPlanResolver().Resolve(spec, data));
        Assert.Contains("duplicate value", error.Message);
    }

    [Fact]
    public void FacetWrapAndAspectRatio_ResolveStableRowMajorPhysicalGeometryAcrossSurfaces()
    {
        const string sql = """
            CREATE VISUAL SmallMultiples AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN, ASPECT_RATIO = 2),
                LAYERS (observations = POINT (ENCODINGS (
                  X = Horizontal (TYPE = QUANTITATIVE),
                  Y = Vertical (TYPE = QUANTITATIVE)
                ))),
                FACET (WRAP = Region, COLUMNS = 3)
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Contains("ASPECT_RATIO = 2", statement.ToSql());
        Assert.Contains("FACET ( WRAP = Region, COLUMNS = 3 )", statement.ToSql());
        var manifest = new VisualManifest
        {
            Name = "SmallMultiples",
            Columns = ["Region", "Horizontal", "Vertical"],
            Rows = [["North", "0", "0"], ["South", "2.5", "5"], ["East", "5", "10"],
                ["West", "7.5", "15"], ["Central", "10", "20"]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 600m, 350m));

        Assert.Equal(["North", "South", "East", "West", "Central"], plan.Facets.Select(panel => panel.ColumnLabel));
        Assert.Equal((0m, 0m), (plan.Facets[0].Bounds.X, plan.Facets[0].Bounds.Y));
        Assert.Equal((0m, 175m), (plan.Facets[3].Bounds.X, plan.Facets[3].Bounds.Y));
        Assert.Equal((200m, 175m), (plan.Facets[4].Bounds.X, plan.Facets[4].Bounds.Y));
        Assert.DoesNotContain(plan.Facets, panel => panel.Bounds.X == 400m && panel.Bounds.Y == 175m);

        var viewport = Assert.IsType<PlotBounds>(plan.CartesianViewport);
        var plotWidth = viewport.Width - 80m;
        var plotHeight = viewport.Height - 100m;
        Assert.Equal(2m, (plotHeight / 20m) / (plotWidth / 10m));
        Assert.All(plan.Facets, panel => Assert.NotNull(panel.CartesianViewport));
        Assert.Contains("plot-aspect-viewport", new SvgChartRenderer().Render(plan));
        Assert.NotNull(PlotPlanTerminalRenderer.Render(plan));
    }

    [Fact]
    public void FacetWrap_RejectsPanelBudgetsBeforePanelAllocation()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "X", DataSemanticKind.Quantitative, "x"),
            new FieldBinding(FieldChannel.Y, "Y", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.Wrap, "Group", DataSemanticKind.Nominal));
        var layerBindings = bindings.Where(binding => binding.Channel != FieldChannel.Wrap).ToImmutableArray();
        var spec = ChartSpec.Create("too-many-facets", "#data", bindings,
            [new MarkLayerSpec("points", MarkKind.Point, 0, layerBindings, [])],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Linear, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Budget", null, null, true),
            facet: new FacetSpec(null, null, new ScaleResolutionSpec(), "Group", 10));
        var values = Enumerable.Range(0, 101).ToArray();
        var data = ChartDataSet.Create("#data", values.Length,
        [
            new ChartColumn("X", ChartValueKind.Decimal, DataSemanticKind.Quantitative, values.Select(value => ChartValue.From((decimal)value)).ToImmutableArray(), []),
            new ChartColumn("Y", ChartValueKind.Decimal, DataSemanticKind.Quantitative, values.Select(value => ChartValue.From((decimal)value)).ToImmutableArray(), []),
            new ChartColumn("Group", ChartValueKind.Text, DataSemanticKind.Nominal, values.Select(value => ChartValue.From($"g{value:D3}")).ToImmutableArray(), [])
        ]);

        var error = Assert.Throws<InvalidDataException>(() =>
            new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 2000m, 2000m)));
        Assert.Contains("100-panel limit", error.Message);
        Assert.Contains("filter or group", error.Message);
    }

    [Fact]
    public void DivergingColorRangeAndTick_ResolvePortableSemanticsAcrossSurfaces()
    {
        const string sql = """
            CREATE VISUAL Variance AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (variance = LINEAR (
                  CHANNEL = COLOR,
                  MIN = -10,
                  MAX = 10,
                  RANGE = DIVERGING(LOW = '#2166ac', MID = '#f7f7f7', HIGH = '#b2182b', MIDPOINT = 0)
                )),
                ENCODINGS (X = Category (TYPE = NOMINAL)),
                LAYERS (
                  bars = RECT (ENCODINGS (
                    Y = Amount (TYPE = QUANTITATIVE),
                    COLOR = Variance (TYPE = QUANTITATIVE, SCALE = variance)
                  )),
                  target = TICK (
                    BAND_SIZE = 0.9,
                    THICKNESS = 0.25,
                    ORIENTATION = HORIZONTAL,
                    ENCODINGS (
                      Y = DATUM(5) (TYPE = QUANTITATIVE),
                      COLOR = Variance (TYPE = QUANTITATIVE, SCALE = variance),
                      TOOLTIP = VALUE('Target') (TYPE = NOMINAL)
                    )
                  )
                )
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        Assert.Contains("RANGE = DIVERGING", statement.ToSql());
        Assert.Contains("target = TICK", statement.ToSql());
        Assert.Contains("THICKNESS = 0.25", statement.ToSql());
        var manifest = new VisualManifest
        {
            Name = "Variance",
            Columns = ["Category", "Amount", "Variance"],
            Rows = [["ClippedLow", "2", "-20"], ["Low", "3", "-10"], ["Mid", "6", "0"],
                ["High", "9", "10"], ["Null", "4", null]]
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));

        var range = Assert.IsType<ResolvedColorRange>(plan.Scales.Single(scale => scale.Channel == FieldChannel.Color).ColorRange);
        Assert.Equal(ColorRangeKind.Diverging, range.Kind);
        Assert.Equal(0m, range.Midpoint);
        Assert.Single(plan.Layers.Where(layer => layer.Mark == MarkKind.Tick));
        var svg = new SvgChartRenderer().Render(plan);
        Assert.Contains("class='plot-colorbar'", svg);
        Assert.Contains("class='plot-tick'", svg);
        Assert.Contains("#2166AC", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#F7F7F7", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#B2182B", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#9CA3AF", svg, StringComparison.OrdinalIgnoreCase);
        var terminal = ETL_SQL.Tests.Reporting.TerminalSemantics.TerminalSnapshotHarness.CaptureSnapshot(
            PlotPlanTerminalRenderer.Render(plan), 100).NormalizedText;
        Assert.Contains("Color ranges from", terminal);
        var report = new ReportManifest
        {
            Title = "Variance",
            Source = "variance.rptsql",
            Visuals = [new VisualManifest { Name = "Variance", VisualType = "CUSTOM", PlotPlan = plan, NativeSvg = svg }]
        };
        var pdf = new PdfExporter().Export(report);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, pdf[..4]);
        Assert.Contains("Color ranges from", new MarkdownRenderer().Render(report));
    }

    [Fact]
    public void RepresentativeRefinementWorkload_HasBoundedResolverAndRendererWork()
    {
        const int rowCount = 5_000;
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "X", DataSemanticKind.Quantitative, "x"),
            new FieldBinding(FieldChannel.Y, "Y", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.Color, "Variance", DataSemanticKind.Quantitative, "color"),
            new FieldBinding(FieldChannel.Detail, "Key", DataSemanticKind.Nominal));
        var spec = ChartSpec.Create("refinement-scale", "#data", bindings,
            [new MarkLayerSpec("points", MarkKind.Point, 0, bindings, [])
            {
                Position = new PositionAdjustmentSpec(PositionAdjustmentKind.Jitter, .05m, .05m, "Key", 17)
            }],
            new CoordinateSpec(CoordinateKind.Cartesian, AspectRatio: 1m),
            [
                new ScaleSpec("x", FieldChannel.X, ScaleKind.Linear, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, []),
                new ScaleSpec("color", FieldChannel.Color, ScaleKind.Linear, false, [])
                {
                    ColorRange = new ColorRangeSpec(ColorRangeKind.Diverging, "#2166ac", "#b2182b", "#f7f7f7", 0m)
                }
            ],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Scale", null, null, true));
        var indices = Enumerable.Range(0, rowCount).ToArray();
        var data = ChartDataSet.Create("#data", rowCount,
        [
            new ChartColumn("X", ChartValueKind.Decimal, DataSemanticKind.Quantitative, indices.Select(index => ChartValue.From((decimal)(index % 100))).ToImmutableArray(), []),
            new ChartColumn("Y", ChartValueKind.Decimal, DataSemanticKind.Quantitative, indices.Select(index => ChartValue.From((decimal)(index / 100))).ToImmutableArray(), []),
            new ChartColumn("Variance", ChartValueKind.Decimal, DataSemanticKind.Quantitative, indices.Select(index => ChartValue.From((decimal)(index % 21 - 10))).ToImmutableArray(), []),
            new ChartColumn("Key", ChartValueKind.Text, DataSemanticKind.Nominal, indices.Select(index => ChartValue.From($"row-{index:D5}")).ToImmutableArray(), [])
        ]);

        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var resolverClock = System.Diagnostics.Stopwatch.StartNew();
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 900m, 600m));
        resolverClock.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var planBytes = System.Text.Encoding.UTF8.GetByteCount(ChartContractSerializer.Serialize(plan));
        var renderClock = System.Diagnostics.Stopwatch.StartNew();
        var svg = new SvgChartRenderer().Render(plan);
        renderClock.Stop();
        var svgBytes = System.Text.Encoding.UTF8.GetByteCount(svg);

        output.WriteLine($"resolver_ms={resolverClock.Elapsed.TotalMilliseconds:0.###} render_ms={renderClock.Elapsed.TotalMilliseconds:0.###} allocated_bytes={allocatedBytes} plan_bytes={planBytes} svg_bytes={svgBytes}");

        Assert.Equal(rowCount, Assert.Single(plan.Layers).Data.Length);
        Assert.True(resolverClock.Elapsed < TimeSpan.FromSeconds(5), $"Resolver took {resolverClock.ElapsedMilliseconds} ms."); // flaky-time-bound-ok: 5-second regression budget has substantial headroom over the measured 65 ms workload.
        Assert.True(renderClock.Elapsed < TimeSpan.FromSeconds(5), $"SVG render took {renderClock.ElapsedMilliseconds} ms."); // flaky-time-bound-ok: 5-second regression budget has substantial headroom over the measured 35 ms workload.
        Assert.True(allocatedBytes < 16L * 1024L * 1024L, $"Resolver allocated {allocatedBytes:N0} bytes.");
        Assert.InRange(planBytes, 1, 6 * 1024 * 1024);
        Assert.InRange(svgBytes, 1, 600 * 1024);
    }

    [Fact]
    public void ParsedCustomChart_LowersIntoExecutableRendererNeutralContracts()
    {
        const string sql = """
            CREATE VISUAL Native AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (x = BAND (CHANNEL = X), y = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Category (TYPE = NOMINAL, SCALE = x),
                  Y = Value (TYPE = QUANTITATIVE, SCALE = y)
                )))
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest { Name = "Native", Columns = ["Category", "Value"], Rows = [["A", "12"], ["B", "20"]] };

        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, data);

        Assert.Equal(MarkKind.Rect, Assert.Single(spec.Layers).Mark);
        Assert.Equal(2, Assert.Single(plan.Layers).Data.Length);
        Assert.Contains("<rect", new SvgChartRenderer().Render(plan));
    }

    [Fact]
    public void ConditionalEncodingsAndIndependentFacetScales_AreResolvedOnceForEverySurface()
    {
        var condition = new EncodingConditionSpec(
            ConditionalEncodingChannel.Color,
            new EncodingPredicate(PredicateKind.Comparison,
                new PredicateOperand(PredicateOperandKind.Field, "Value", null),
                new PredicateOperand(PredicateOperandKind.Literal, null, ChartValue.From(0m)),
                ComparisonKind.LessThan),
            ChartValue.From("#b91c1c"), ChartValue.From("#2563eb"));
        var spec = ChartSpec.Create("conditional-facets", "#prepared",
            [
                new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"),
                new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y"),
                new FieldBinding(FieldChannel.Row, "Region", DataSemanticKind.Nominal)
            ],
            [new MarkLayerSpec("bars", MarkKind.Rect, 0,
                [new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"), new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y")], [])
                { Conditions = [condition] }],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Conditional facets", null, null, true),
            facet: new FacetSpec("Region", null, new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Independent)),
            scaleResolution: new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Independent));
        var data = ChartDataSet.Create("#prepared", 4,
        [
            new ChartColumn("Category", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("A"), ChartValue.From("B"), ChartValue.From("A"), ChartValue.From("B")], []),
            new ChartColumn("Value", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(-5m), ChartValue.From(10m), ChartValue.From(100m), ChartValue.From(200m)], []),
            new ChartColumn("Region", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("East"), ChartValue.From("East"), ChartValue.From("West"), ChartValue.From("West")], [])
        ]);

        var plan = new PlotPlanResolver().Resolve(spec, data);

        Assert.Equal(2, plan.Facets.Length);
        Assert.NotEqual(plan.Facets[0].Scales.Single(scale => scale.Id == "y").Domain,
            plan.Facets[1].Scales.Single(scale => scale.Id == "y").Domain);
        Assert.Equal("#b91c1c", plan.Layers[0].Data[0].Encodings.Single().Value.Text);
        Assert.Equal("#2563eb", plan.Layers[0].Data[1].Encodings.Single().Value.Text);
        Assert.Contains("East", new SvgChartRenderer().Render(plan));
        Assert.Contains("#b91c1c", new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan }));
        Assert.NotNull(PlotPlanTerminalRenderer.Render(plan));
    }

    [Fact]
    public async Task AnalysisRejectsConditionsOnConnectedMarks()
    {
        const string sql = """
            CREATE VISUAL BadLine AS CUSTOM (
              SOURCE = #prepared,
              CHART (
                COORDINATE (TYPE = CARTESIAN),
                SCALES (x = BAND (CHANNEL = X), y = LINEAR (CHANNEL = Y)),
                LAYERS (trend = LINE (
                  ENCODINGS (X = Category (TYPE = NOMINAL, SCALE = x), Y = Value (TYPE = QUANTITATIVE, SCALE = y)),
                  CONDITIONS (COLOR WHEN Value < 0 THEN '#b91c1c')
                ))
              )
            );
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);

        var diagnostics = await new AdvancedChartAuthoringRule().AnalyzeAsync(script, new DefaultLintContext());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "RPT-CHART" && diagnostic.Message.Contains("connected LINE"));
    }

    [Fact]
    public void ContractSerializationPreservesConditionsAndFacetPanels()
    {
        var layer = new MarkLayerSpec("points", MarkKind.Point, 0, [], [])
        {
            Conditions = [new EncodingConditionSpec(ConditionalEncodingChannel.Size,
                new EncodingPredicate(PredicateKind.Truthy, new PredicateOperand(PredicateOperandKind.Literal, null, ChartValue.From(true))),
                ChartValue.From(8m))]
        };
        var spec = ChartSpec.Create("serialized", "#data", [], [layer], new CoordinateSpec(CoordinateKind.Cartesian), [],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Serialized", null, null, true));

        var json = ChartContractSerializer.Serialize(spec);
        var roundTrip = ChartContractSerializer.DeserializeChartSpec(json);

        Assert.Single(roundTrip.Layers[0].Conditions);
        Assert.Equal(ConditionalEncodingChannel.Size, roundTrip.Layers[0].Conditions[0].Channel);
    }

    [Fact]
    public void AreaTextAndTransposedCoordinates_RenderAcrossBackends()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.Text, "Label", DataSemanticKind.Nominal));
        var spec = ChartSpec.Create("transposed", "#data", bindings,
            [new MarkLayerSpec("area", MarkKind.Area, 0, bindings, []), new MarkLayerSpec("labels", MarkKind.Text, 1, bindings, [])],
            new CoordinateSpec(CoordinateKind.TransposedCartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Transposed", null, null, true));
        var data = ChartDataSet.Create("#data", 2,
        [
            new ChartColumn("Category", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("A"), ChartValue.From("B")], []),
            new ChartColumn("Value", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(2m), ChartValue.From(8m)], []),
            new ChartColumn("Label", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("low"), ChartValue.From("high")], [])
        ]);
        var plan = new PlotPlanResolver().Resolve(spec, data);

        var svg = new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan });

        Assert.Contains("<path", svg);
        Assert.Contains(">high</text>", svg);
        Assert.NotNull(PlotPlanTerminalRenderer.Render(plan));
    }

    [Fact]
    public void DenseMultiSeriesLabelsAndBandAxis_UseDeterministicBoundedPlacement()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "Category", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "Value", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.Color, "Series", DataSemanticKind.Nominal));
        var spec = ChartSpec.Create("dense-lines", "#data", bindings,
            [new MarkLayerSpec("lines", MarkKind.Line, 0, bindings, [])],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", [new StyleToken("DATA_LABELS", "ON")]),
            new AccessibilitySpec("Two labeled series over twelve crowded categories.", null, null, true));
        var categories = Enumerable.Range(1, 12).Select(index => $"Long category {index:D2}").ToArray();
        var categoryValues = categories.SelectMany(category => new[] { ChartValue.From(category), ChartValue.From(category) }).ToImmutableArray();
        var values = categories.SelectMany((_, index) => new[] { ChartValue.From((decimal)index), ChartValue.From((decimal)index) }).ToImmutableArray();
        var series = categories.SelectMany(_ => new[] { ChartValue.From("Actual"), ChartValue.From("Forecast") }).ToImmutableArray();
        var data = ChartDataSet.Create("#data", 24,
        [
            new ChartColumn("Category", ChartValueKind.Text, DataSemanticKind.Nominal, categoryValues, []),
            new ChartColumn("Value", ChartValueKind.Decimal, DataSemanticKind.Quantitative, values, []),
            new ChartColumn("Series", ChartValueKind.Text, DataSemanticKind.Nominal, series, [])
        ]);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 480m, 300m));

        var first = new SvgChartRenderer().Render(plan);
        var second = new SvgChartRenderer().Render(plan);

        Assert.Equal(first, second);
        Assert.Contains("class='plot-smart-label'", first);
        Assert.Contains("class='plot-smart-label-leader'", first);
        Assert.Contains("class='plot-axis-label-occluded'", first);
        Assert.Contains("rotate(-35", first);
        Assert.Contains("<desc id='dense-lines-desc'>", first);
        Assert.Contains("Additional categories:", first);
    }

    [Fact]
    public void ClusteredScatterText_PrioritizesExplicitLabelsAndKeepsOccludedTextAccessible()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "X", DataSemanticKind.Quantitative, "x"),
            new FieldBinding(FieldChannel.Y, "Y", DataSemanticKind.Quantitative, "y"),
            new FieldBinding(FieldChannel.Text, "Label", DataSemanticKind.Nominal));
        var spec = ChartSpec.Create("clustered-scatter", "#data", bindings,
            [
                new MarkLayerSpec("points", MarkKind.Point, 0, bindings, []),
                new MarkLayerSpec("labels", MarkKind.Text, 2, bindings, [])
            ],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [new ScaleSpec("x", FieldChannel.X, ScaleKind.Linear, false, []), new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Clustered labeled scatter.", null, null, true));
        var labels = Enumerable.Range(1, 10).Select(index => ChartValue.From($"Nearby point {index:D2}")).ToImmutableArray();
        var data = ChartDataSet.Create("#data", 10,
        [
            new ChartColumn("X", ChartValueKind.Decimal, DataSemanticKind.Quantitative, Enumerable.Range(0, 10).Select(index => ChartValue.From(10m + index / 100m)).ToImmutableArray(), []),
            new ChartColumn("Y", ChartValueKind.Decimal, DataSemanticKind.Quantitative, Enumerable.Range(0, 10).Select(index => ChartValue.From(20m + index / 100m)).ToImmutableArray(), []),
            new ChartColumn("Label", ChartValueKind.Text, DataSemanticKind.Nominal, labels, [])
        ]);
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0m, 0m, 360m, 240m));

        var svg = new SvgChartRenderer().Render(plan);

        Assert.Contains("data-priority='202'", svg);
        Assert.Contains("class='plot-smart-label-leader'", svg);
        Assert.Contains("class='plot-smart-label-occluded'", svg);
        Assert.Contains("Nearby point", svg);
    }

    [Fact]
    public void PolarArcConditionalColor_RendersAcrossBackends()
    {
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.Theta, "Kind", DataSemanticKind.Nominal, "theta"),
            new FieldBinding(FieldChannel.Radius, "Amount", DataSemanticKind.Quantitative, "radius"));
        var condition = new EncodingConditionSpec(ConditionalEncodingChannel.Color,
            new EncodingPredicate(PredicateKind.Comparison,
                new PredicateOperand(PredicateOperandKind.Field, "Amount", null),
                new PredicateOperand(PredicateOperandKind.Literal, null, ChartValue.From(5m)), ComparisonKind.GreaterThan),
            ChartValue.From("#b91c1c"));
        var spec = ChartSpec.Create("polar", "#data", bindings,
            [new MarkLayerSpec("arcs", MarkKind.Arc, 0, bindings, []) { Conditions = [condition] }],
            new CoordinateSpec(CoordinateKind.Polar, InnerRadius: .4m),
            [new ScaleSpec("theta", FieldChannel.Theta, ScaleKind.Ordinal, false, []), new ScaleSpec("radius", FieldChannel.Radius, ScaleKind.Linear, true, [])],
            new FormattingSpec("en-US", "UTC", "", []), new NullHandlingSpec(NullValuePolicy.Skip, []),
            new ThemeSpec("default", []), new AccessibilitySpec("Polar", null, null, true));
        var data = ChartDataSet.Create("#data", 2,
        [
            new ChartColumn("Kind", ChartValueKind.Text, DataSemanticKind.Nominal, [ChartValue.From("A"), ChartValue.From("B")], []),
            new ChartColumn("Amount", ChartValueKind.Decimal, DataSemanticKind.Quantitative, [ChartValue.From(3m), ChartValue.From(7m)], [])
        ]);
        var plan = new PlotPlanResolver().Resolve(spec, data);

        Assert.Contains("#b91c1c", new SvgChartRenderer().Render(plan));
        Assert.Contains("#b91c1c", new SvgChartRenderer().Render(new VisualManifest { PlotPlan = plan }));
        Assert.Contains(plan.Fallback.Items, item => item.Detail?.Contains("conditional Color") == true);
        Assert.NotNull(PlotPlanTerminalRenderer.Render(plan));
    }

    private static string GetSamplePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(current.FullName, "samples")))
            {
                return Path.Combine(current.FullName, relativePath);
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    [Fact]
    public async Task KitchenSink39_RendersAllThreeCustomVisuals()
    {
        var scriptPath = GetSamplePath(Path.Combine("samples", "10_Kitchen_Sinks", "39_CUSTOM_LAYERS.rptsql"));
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        Assert.NotNull(manifest);
        Assert.Equal(3, manifest.Visuals.Count);

        var perf = manifest.Visuals.First(v => v.Name == "PerformanceOverview");
        var bullet = manifest.Visuals.First(v => v.Name == "BulletKpiCards");
        var scatter = manifest.Visuals.First(v => v.Name == "CustomerScatter");

        Assert.NotNull(perf.NativeSvg);
        Assert.NotNull(bullet.NativeSvg);
        Assert.NotNull(scatter.NativeSvg);
    }

    [Fact]
    public async Task DeclarativeGeometrySample_IsCopyPasteableAndPortableAcrossBackends()
    {
        var scriptPath = GetSamplePath(Path.Combine("samples", "08_Reporting", "declarative_geometry_refinements.rptsql"));
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();

        Assert.NotNull(manifest);
        Assert.Equal(8, manifest.Visuals.Count);
        Assert.Equal(2, manifest.Pages.Count);
        Assert.All(manifest.Visuals, visual =>
        {
            Assert.True(visual.Options.TryGetValue("title", out var title));
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.NotNull(visual.PlotPlan);
            Assert.NotNull(visual.NativeSvg);
            Assert.NotNull(PlotPlanTerminalRenderer.Render(Assert.IsType<PlotPlan>(visual.PlotPlan), 100));
        });

        var markdown = new MarkdownRenderer().Render(manifest);
        var pdf = new PdfExporter().Export(manifest);
        Assert.Contains("Declarative Geometry Refinements", markdown);
        Assert.NotEmpty(pdf);
        Assert.Contains(manifest.Visuals, visual =>
            visual.Interactions?.TryGetValue("ON_SELECT", out var selection) == true
            && selection == "HIGHLIGHT");
    }

    [Fact]
    public async Task KitchenSink01_OverlayBar_RendersCompositeTerminalOutput()
    {
        var scriptPath = GetSamplePath(Path.Combine("samples", "10_Kitchen_Sinks", "01_BAR.rptsql"));
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        Assert.NotNull(manifest);
        var overlayBar = manifest.Visuals.First(v => v.Name == "OverlayBar");
        var plan = Assert.IsType<PlotPlan>(overlayBar.PlotPlan);
        var renderable = PlotPlanTerminalRenderer.Render(plan, 80);
        var text = ETL_SQL.Tests.Reporting.TerminalSemantics.TerminalSnapshotHarness.CaptureSnapshot(renderable, 80).NormalizedText;
        Assert.Contains("Revenue", text);
        Assert.Contains("Monthly Goal", text);
        Assert.Contains("2-Month Moving Avg", text);
        Assert.Contains("Jan", text);
        Assert.Contains("Apr", text);
    }

    [Fact]
    public async Task KitchenSink04_Scatter_RendersAllFourVisualsInTerminal()
    {
        var scriptPath = GetSamplePath(Path.Combine("samples", "10_Kitchen_Sinks", "04_SCATTER.rptsql"));
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        Assert.NotNull(manifest);

        foreach (var visual in manifest.Visuals)
        {
            var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);
            var renderable = PlotPlanTerminalRenderer.Render(plan, 80);
            var text = ETL_SQL.Tests.Reporting.TerminalSemantics.TerminalSnapshotHarness.CaptureSnapshot(renderable, 80).NormalizedText;
            Assert.NotNull(text);
            Assert.NotEmpty(text);
            Assert.DoesNotContain("Error", text);
        }
    }

    [Fact]
    public async Task KitchenSink11_Heatmap_RendersAllThreeVisualsInTerminal()
    {
        var scriptPath = GetSamplePath(Path.Combine("samples", "10_Kitchen_Sinks", "11_HEATMAP.rptsql"));
        await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        Assert.NotNull(manifest);

        foreach (var visual in manifest.Visuals)
        {
            var plan = Assert.IsType<PlotPlan>(visual.PlotPlan);
            var renderable = PlotPlanTerminalRenderer.Render(plan, 80);
            var text = ETL_SQL.Tests.Reporting.TerminalSemantics.TerminalSnapshotHarness.CaptureSnapshot(renderable, 80).NormalizedText;
            Assert.NotNull(text);
            Assert.NotEmpty(text);
            Assert.Contains("08:00", text);
            Assert.Contains("Mon", text);
            Assert.Contains("Fri", text);
            Assert.Contains("2D Heatmap Grid", text);
        }
    }
}
