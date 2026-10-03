using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(StudioSurfaceCollection.Name)]
public sealed class ConnectedMarkSvgTests(StudioSurfaceFixture fixture)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var form in new[] { "LINE", "AREA", "RIBBON" })
        {
            foreach (var policy in new[] { "CONNECT", "ZERO" }) yield return [form, policy, false, false, false, "LINEAR"];
            foreach (var policy in new[] { "GAP", "CONNECT", "ZERO" })
                foreach (var aspect in new[] { false, true }) yield return [form, policy, true, aspect, false, "LINEAR"];
            foreach (var policy in new[] { "GAP", "CONNECT", "ZERO" })
                foreach (var coordinate in new[] { "CARTESIAN", "TRANSPOSED", "ASPECT" })
                    yield return [form, policy, coordinate != "CARTESIAN", coordinate == "ASPECT", true, "LINEAR"];
            foreach (var interpolation in new[] { "SMOOTH", "STEP_BEFORE", "STEP_AFTER" })
                foreach (var policy in new[] { "GAP", "CONNECT", "ZERO" })
                    foreach (var coordinate in new[] { "CARTESIAN", "TRANSPOSED", "ASPECT" })
                        foreach (var decorated in new[] { false, true })
                            yield return [form, policy, coordinate != "CARTESIAN", coordinate == "ASPECT", decorated, interpolation];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ChromiumRendersPartitionedNativeConnectionsWithSourceOwnedPresentation(string form, string policy, bool transposed, bool aspect, bool decorated, string interpolation)
    {
        var sql = $$"""
            CREATE VISUAL Routes AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = {{(transposed ? "TRANSPOSED_CARTESIAN" : "CARTESIAN")}}{{(aspect ? ", ASPECT_RATIO = 2" : "")}}),
              FACET (WRAP = Cohort, COLUMNS = 2),
              RESOLVE (X = INDEPENDENT, Y = INDEPENDENT),
              LAYERS (route = {{(form == "LINE" ? "LINE" : "AREA")}} (
                NULL_HANDLING = {{policy}}, {{(form == "AREA" ? "AREA_BASELINE = ZERO," : "")}}
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE),
                  {{(form == "RIBBON" ? "Y_START = Lower (TYPE = QUANTITATIVE), Y_END = Upper (TYPE = QUANTITATIVE)" : "Y = Estimate (TYPE = QUANTITATIVE)")}},
                  COLOR = Series (TYPE = NOMINAL)),
                STYLE (INTERPOLATION = '{{interpolation}}'),
                CONDITIONS (COLOR WHEN Flag = 'red' THEN '#ff0000' ELSE '#0000ff',
                  OPACITY WHEN Flag = 'red' THEN 0 ELSE 1
                  {{(decorated ? ", SIZE WHEN Flag = 'red' THEN 7 ELSE 2, SHAPE WHEN Flag = 'red' THEN 'SQUARE' ELSE 'STAR', TEXT WHEN Flag = 'red' THEN '<endpoint & raw>' ELSE NULL" : "")}})))
            ));
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var statement = Assert.Single(script.Statements.OfType<CreateVisualStatement>());
        var manifest = new VisualManifest
        {
            Name = "Routes",
            Columns = ["Distance", "Estimate", "Lower", "Upper", "Series", "Cohort", "Flag"],
            Rows = Enumerable.Range(0, 12).Select(index => new List<string?>
            { new[] { "8", "2", "5" }[index / 4], index == 4 && form != "RIBBON" ? null : new[] { "1", "7", "3" }[index / 4],
                index == 4 && form == "RIBBON" ? null : "1", "9", index % 2 == 0 ? "S" : "T", index % 4 < 2 ? "A" : "B", index / 4 == 0 ? "red" : "blue" }).ToList()
        };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(statement, manifest);
        var plan = new PlotPlanResolver().Resolve(spec, new VisualChartDataBuilder().Build(spec, manifest));
        var svg = new SvgChartRenderer().Render(plan);
        await using var session = await fixture.NewSessionAsync();
        await session.Page.SetContentAsync("<!doctype html><html><body>" + svg + "</body></html>");
        var glyphs = await session.Page.EvaluateAsync<ConnectionGlyph[]>("""
            () => Array.from(document.querySelectorAll('.plot-conditional-connection, .plot-conditional-area')).map(path => {
              const bounds = path.getBBox();
              return {
                title: (path.closest('.plot-connected-layer')?.getAttribute('aria-label') ? path.closest('.plot-connected-layer').getAttribute('aria-label') + '; ' : '') + path.querySelector('title').textContent,
                paint: path.getAttribute('stroke') === 'none' ? path.getAttribute('fill') : path.getAttribute('stroke'),
                opacity: Number(getComputedStyle(path).opacity),
                length: path.getTotalLength(), width: bounds.width, height: bounds.height,
                path: path.getAttribute('d'), strokeWidth: Number.parseFloat(getComputedStyle(path).strokeWidth)
              };
            })
            """);
        Assert.Equal(plan.Layers.Sum(layer => layer.Connections.Length), glyphs.Length);
        if (interpolation != "LINEAR")
            foreach (var connection in plan.Layers.SelectMany(layer => layer.Connections))
            {
                var glyph = Assert.Single(glyphs, glyph => glyph.Title.Contains($"Row {connection.SourceRowIndex + 1} to row {connection.DestinationRowIndex + 1};", StringComparison.Ordinal));
                Assert.Contains("interpolation " + interpolation, glyph.Title);
                Assert.Equal(connection.Geometry!.Cubic, glyph.Path.Contains(" C ", StringComparison.Ordinal));
                if (form == "LINE") Assert.Equal(2d, glyph.StrokeWidth);
            }
        if (policy == "CONNECT")
        {
            var skipped = Assert.Single(glyphs, glyph => glyph.Title.Contains("Row 1 to row 9", StringComparison.Ordinal));
            Assert.Contains("series S, facet A", skipped.Title);
            Assert.Equal("#ff0000", skipped.Paint);
            Assert.Equal(0d, skipped.Opacity);
            Assert.DoesNotContain(glyphs, glyph => glyph.Title.Contains("Row 5 to", StringComparison.Ordinal));
        }
        else if (policy == "GAP" || form == "RIBBON")
        {
            Assert.DoesNotContain(glyphs, glyph => glyph.Title.Contains("series S, facet A", StringComparison.Ordinal));
            if (form == "RIBBON") Assert.Contains(plan.Fallback.Items, item => item.Value.Contains("null to 9; gap", StringComparison.Ordinal));
        }
        else
        {
            var incoming = Assert.Single(glyphs, glyph => glyph.Title.Contains("Row 1 to row 5", StringComparison.Ordinal));
            var outgoing = Assert.Single(glyphs, glyph => glyph.Title.Contains("Row 5 to row 9", StringComparison.Ordinal));
            Assert.Equal("#ff0000", incoming.Paint);
            Assert.Equal(0d, incoming.Opacity);
            Assert.Equal("#0000ff", outgoing.Paint);
            Assert.Equal(1d, outgoing.Opacity);
            Assert.Contains("destination null Y rendered at zero", incoming.Title);
            Assert.Contains("source null Y rendered at zero", outgoing.Title);
        }
        Assert.All(glyphs, glyph => Assert.True(glyph.Length > 0d && glyph.Width > 0d && glyph.Height >= 0d));
        Assert.Contains(glyphs, glyph => glyph.Paint == "#0000ff" && glyph.Opacity == 1d);
        if (transposed) Assert.All(glyphs, glyph => Assert.Contains("X vertical, Y horizontal", glyph.Title));
        if (decorated)
        {
            var symbols = await session.Page.EvaluateAsync<DecorationGlyph[]>("""
                () => Array.from(document.querySelectorAll('.plot-connection-symbol')).map(symbol => {
                  const bounds = symbol.getBBox();
                  return { row: Number(symbol.dataset.rowIndex), shape: symbol.dataset.symbolShape,
                    title: symbol.querySelector('title').textContent, opacity: Number(getComputedStyle(symbol).opacity),
                    width: bounds.width, height: bounds.height };
                })
                """);
            Assert.Equal(form != "RIBBON" && policy == "ZERO" ? 12 : 11, symbols.Length);
            Assert.All(symbols, symbol => Assert.True(symbol.Width > 0d && symbol.Height > 0d));
            Assert.All(symbols.Where(symbol => symbol.Row < 4), symbol =>
            {
                Assert.Equal("SQUARE", symbol.Shape);
                Assert.Equal(14d, symbol.Width);
                Assert.Equal(14d, symbol.Height);
                Assert.Equal(0d, symbol.Opacity);
                Assert.Contains("text <endpoint & raw>", symbol.Title);
            });
            Assert.All(symbols.Where(symbol => symbol.Row > 4), symbol => Assert.Equal("STAR", symbol.Shape));
            var captions = await session.Page.Locator(".plot-smart-label, .plot-smart-label-occluded").AllTextContentsAsync();
            Assert.Equal(4, captions.Count);
            Assert.All(captions, caption => Assert.Equal("<endpoint & raw>", caption));
            Assert.Equal(0, await session.Page.Locator("svg script").CountAsync());
        }
    }

    [Theory]
    [InlineData(false, false, "EM")]
    [InlineData(true, false, "EM")]
    [InlineData(false, true, "EM")]
    [InlineData(true, true, "EM")]
    [InlineData(false, false, "BAND")]
    [InlineData(true, false, "BAND")]
    [InlineData(false, true, "BAND")]
    [InlineData(true, true, "BAND")]
    [InlineData(false, false, "DATA")]
    [InlineData(true, false, "DATA")]
    [InlineData(false, true, "DATA")]
    [InlineData(true, true, "DATA")]
    [InlineData(false, false, "JITTER")]
    [InlineData(true, false, "JITTER")]
    [InlineData(false, true, "JITTER")]
    [InlineData(true, true, "JITTER")]
    [MemberData(nameof(LineInterpolationCases))]
    public async Task ChromiumPreservesOrdinaryLineGeometryThroughPlacement(bool reverse, bool logarithmic, string unit, string interpolation = "LINEAR")
    {
        var kind = logarithmic ? "LOGARITHMIC" : "LINEAR";
        var sql = $$"""
            CREATE VISUAL Route AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
              SCALES (horizontal = {{kind}} (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10{{(reverse ? ", REVERSE = ON" : "")}}),
                vertical = {{kind}} (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = 1, MAX = 10{{(reverse ? ", REVERSE = ON" : "")}})),
              LAYERS (route = LINE (NULL_HANDLING = GAP,
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal), Y = Estimate (TYPE = QUANTITATIVE, SCALE = vertical)),
                STYLE (INTERPOLATION = '{{interpolation}}', COLOR = '#112233')))
            ));
            """;
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var manifest = new VisualManifest { Name = "Route", Columns = ["Distance", "Estimate", "Id"], Rows = [["8", "1", "A"], ["2", "7", "B"], ["5", "3", "C"]] };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(Assert.Single(script.Statements.OfType<CreateVisualStatement>()), manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var before = new PlotPlanResolver().Resolve(spec, data);
        var position = unit switch
        {
            "EM" => new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 1m, -0.5m, Unit: PositionAdjustmentUnit.Em),
            "BAND" => new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 0.02m, -0.03m, Unit: PositionAdjustmentUnit.Band),
            "JITTER" => new PositionAdjustmentSpec(PositionAdjustmentKind.Jitter, 0.02m, 0.03m, StableKeyField: "Id", Seed: 42),
            _ => new PositionAdjustmentSpec(PositionAdjustmentKind.Nudge, 0.5m, -0.5m, Unit: PositionAdjustmentUnit.Data)
        };
        var nudged = spec with { Layers = spec.Layers.SetItem(0, spec.Layers[0] with { Position = position }) };
        var after = new PlotPlanResolver().Resolve(nudged, data);
        await using var session = await fixture.NewSessionAsync();
        await session.Page.SetContentAsync("<!doctype html><html><body>" + new SvgChartRenderer().Render(before) + new SvgChartRenderer().Render(after) + "</body></html>");
        var views = await session.Page.EvaluateAsync<LinePlacementView[]>("""
            () => Array.from(document.querySelectorAll('svg')).map(svg => {
              const path = svg.querySelector('path[stroke="#112233"][fill="none"]');
              return { length: path.getTotalLength(),
                x: Array.from(svg.querySelectorAll('.plot-line-symbol')).map(symbol => Number(symbol.getAttribute('cx'))),
                y: Array.from(svg.querySelectorAll('.plot-line-symbol')).map(symbol => Number(symbol.getAttribute('cy'))) };
            })
            """);
        Assert.Equal(2, views.Length);
        Assert.Equal(3, views[1].X.Length);
        if (unit != "JITTER" && (unit != "DATA" || !logarithmic)) Assert.InRange(Math.Abs(views[0].Length - views[1].Length), 0d, 0.002d);
        Assert.True(views[1].Length > 0d);
        var expectedLength = Enumerable.Range(1, 2).Sum(row => Math.Sqrt(Math.Pow(views[1].X[row] - views[1].X[row - 1], 2) + Math.Pow(views[1].Y[row] - views[1].Y[row - 1], 2)));
        if (interpolation is "STEP_BEFORE" or "STEP_AFTER")
            expectedLength = Enumerable.Range(1, 2).Sum(row => Math.Abs(views[1].X[row] - views[1].X[row - 1]) + Math.Abs(views[1].Y[row] - views[1].Y[row - 1]));
        else if (interpolation == "SMOOTH")
        {
            expectedLength = 0d;
            for (var segment = 0; segment < 2; segment++)
            {
                var previousX = views[1].X[segment];
                var previousY = views[1].Y[segment];
                double Coordinate(double[] values, double fraction)
                {
                    var start = values[segment];
                    var end = values[segment + 1];
                    var first = start + (end - values[Math.Max(0, segment - 1)]) / 6d;
                    var second = end - (values[Math.Min(2, segment + 2)] - start) / 6d;
                    var inverse = 1d - fraction;
                    return inverse * inverse * inverse * start + 3d * inverse * inverse * fraction * first +
                        3d * inverse * fraction * fraction * second + fraction * fraction * fraction * end;
                }
                for (var sample = 1; sample <= 1000; sample++)
                {
                    var x = Coordinate(views[1].X, sample / 1000d);
                    var y = Coordinate(views[1].Y, sample / 1000d);
                    expectedLength += Math.Sqrt(Math.Pow(x - previousX, 2) + Math.Pow(y - previousY, 2));
                    (previousX, previousY) = (x, y);
                }
            }
        }
        Assert.InRange(Math.Abs(views[1].Length - expectedLength), 0d, interpolation == "SMOOTH" ? .05d : .002d);
        for (var row = 0; row < 3; row++)
        {
            var dx = unit == "EM" ? -6m : unit == "BAND" ? -0.03m * (before.CartesianViewport!.Width - 80m) : after.Layers[0].Data[row].DisplayOffsetX;
            var dy = unit == "EM" ? -12m : unit == "BAND" ? -0.02m * (before.CartesianViewport!.Height - 100m) : after.Layers[0].Data[row].DisplayOffsetY;
            Assert.InRange(Math.Abs(views[1].X[row] - views[0].X[row] - (double)dx), 0d, 0.002d);
            Assert.InRange(Math.Abs(views[1].Y[row] - views[0].Y[row] - (double)dy), 0d, 0.002d);
        }
    }

    [Theory]
    [InlineData(false, false, "EM")]
    [InlineData(false, true, "EM")]
    [InlineData(true, false, "EM")]
    [InlineData(true, true, "EM")]
    [InlineData(false, false, "BAND")]
    [InlineData(false, true, "BAND")]
    [InlineData(true, false, "BAND")]
    [InlineData(true, true, "BAND")]
    [InlineData(false, false, "DATA")]
    [InlineData(false, true, "DATA")]
    [InlineData(true, false, "DATA")]
    [InlineData(true, true, "DATA")]
    [InlineData(false, false, "DATA", true)]
    [InlineData(false, true, "DATA", true)]
    [InlineData(true, false, "DATA", true)]
    [InlineData(true, true, "DATA", true)]
    [InlineData(false, false, "JITTER")]
    [InlineData(false, true, "JITTER")]
    [InlineData(true, false, "JITTER")]
    [InlineData(true, true, "JITTER")]
    [InlineData(false, false, "JITTER", true)]
    [InlineData(false, true, "JITTER", true)]
    [InlineData(true, false, "JITTER", true)]
    [InlineData(true, true, "JITTER", true)]
    [MemberData(nameof(AreaInterpolationCases))]
    public async Task ChromiumTranslatesWholeAreaPolygons(bool ribbon, bool reverse, string unit, bool logarithmic = false, string interpolation = "LINEAR", bool confidence = false)
    {
        var sql = $$"""
            CREATE VISUAL Envelope AS CUSTOM (SOURCE = #prepared, CHART (
              COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
              SCALES (distances = {{(logarithmic ? "LOGARITHMIC" : "LINEAR")}} (CHANNEL = X, INCLUDE_ZERO = OFF, MIN = {{(logarithmic ? "1" : "0")}}, MAX = 10{{(reverse ? ", REVERSE = ON" : "")}}),
                estimates = {{(logarithmic && ribbon ? "LOGARITHMIC" : "LINEAR")}} (CHANNEL = Y, INCLUDE_ZERO = OFF, MIN = {{(logarithmic && ribbon ? "1" : "0")}}, MAX = 10{{(reverse ? ", REVERSE = ON" : "")}})),
              LAYERS (envelope = AREA (INHERIT_ENCODINGS = OFF, NULL_HANDLING = GAP,
                {{(ribbon ? "" : "AREA_BASELINE = ZERO,")}}
                POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = {{unit}}),
                ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
                  {{(ribbon ? "Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound" : "Y = UpperBound")}} (TYPE = QUANTITATIVE, SCALE = estimates)),
                STYLE (INTERPOLATION = '{{interpolation}}', COLOR = '#112233')))
            ));
            """;
        if (unit == "JITTER") sql = sql.Replace("POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = JITTER)", "POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42)", StringComparison.Ordinal);
        if (confidence) sql = sql.Replace("Y_START", "CONFIDENCE_LOW", StringComparison.Ordinal).Replace("Y_END", "CONFIDENCE_HIGH", StringComparison.Ordinal);
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.Empty(script.Diagnostics);
        var manifest = new VisualManifest { Name = "Envelope", Columns = ["Distance", "LowerBound", "UpperBound", "Id"], Rows = [["8", "1", "4", "A"], ["2", "7", "2", "B"], ["5", "3", "9", "C"]] };
        var spec = new AdvancedChartLowerer(new SystemExecutionContext()).Lower(Assert.Single(script.Statements.OfType<CreateVisualStatement>()), manifest);
        var data = new VisualChartDataBuilder().Build(spec, manifest);
        var resolver = new PlotPlanResolver();
        var before = resolver.Resolve(spec with { Layers = [spec.Layers[0] with { Position = null }] }, data);
        var after = resolver.Resolve(spec, data);
        await using var session = await fixture.NewSessionAsync();
        await session.Page.SetContentAsync("<!doctype html><html><body>" + new SvgChartRenderer().Render(before) + new SvgChartRenderer().Render(after) + "</body></html>");
        var views = await session.Page.EvaluateAsync<AreaPlacementView[]>("""
            () => Array.from(document.querySelectorAll('svg')).map(svg => {
              const path = svg.querySelector('.plot-area, .plot-ribbon, .plot-confidence-band');
              const box = path.getBBox();
              const tokens = path.getAttribute('d').match(/[MLCZ]|-?\d+(?:\.\d+)?/g);
              const numbers = [];
              let expectedLength = 0, x = 0, y = 0, firstX = 0, firstY = 0;
              for (let index = 0; index < tokens.length;) {
                const command = tokens[index++];
                if (command === 'Z') { expectedLength += Math.hypot(x - firstX, y - firstY); continue; }
                if (command === 'C') {
                  const x1 = Number(tokens[index++]), y1 = Number(tokens[index++]);
                  const x2 = Number(tokens[index++]), y2 = Number(tokens[index++]);
                  const endX = Number(tokens[index++]), endY = Number(tokens[index++]);
                  const startX = x, startY = y;
                  for (let sample = 1; sample <= 1000; sample++) {
                    const t = sample / 1000, u = 1 - t;
                    const nextX = u*u*u*startX + 3*u*u*t*x1 + 3*u*t*t*x2 + t*t*t*endX;
                    const nextY = u*u*u*startY + 3*u*u*t*y1 + 3*u*t*t*y2 + t*t*t*endY;
                    expectedLength += Math.hypot(nextX - x, nextY - y);
                    x = nextX; y = nextY;
                  }
                } else {
                  const nextX = Number(tokens[index++]), nextY = Number(tokens[index++]);
                  if (command === 'M') { firstX = nextX; firstY = nextY; }
                  else expectedLength += Math.hypot(nextX - x, nextY - y);
                  x = nextX; y = nextY;
                }
                numbers.push(x, y);
              }
              return { x: box.x, y: box.y, width: box.width, height: box.height,
                length: path.getTotalLength(), expectedLength, coordinates: numbers, title: path.querySelector('title').textContent };
            })
            """);
        Assert.Equal(2, views.Length);
        var dx = unit == "DATA" ? after.Layers[0].Data[0].DisplayOffsetX : -0.03m * (unit == "EM" ? 12m : before.CartesianViewport!.Width - 80m);
        var dy = unit == "DATA" ? after.Layers[0].Data[0].DisplayOffsetY : -0.02m * (unit == "EM" ? 12m : before.CartesianViewport!.Height - 100m);
        if (!logarithmic && unit != "JITTER")
        {
            Assert.InRange(Math.Abs(views[1].X - views[0].X - (double)dx), 0d, 0.002d);
            Assert.InRange(Math.Abs(views[1].Y - views[0].Y - (double)dy), 0d, 0.002d);
            Assert.InRange(Math.Abs(views[1].Width - views[0].Width), 0d, 0.002d);
            Assert.InRange(Math.Abs(views[1].Height - views[0].Height), 0d, 0.002d);
            Assert.InRange(Math.Abs(views[1].Length - views[0].Length), 0d, 0.005d);
        }
        Assert.All(views, view => Assert.InRange(Math.Abs(view.Length - view.ExpectedLength), 0d, interpolation == "SMOOTH" ? .05d : .02d));
        Assert.True(views[1].Width > 0d && views[1].Height > 0d);
        var stepped = interpolation is "STEP_BEFORE" or "STEP_AFTER";
        Assert.Equal(stepped ? 20 : 12, views[1].Coordinates.Length);
        for (var vertex = 0; vertex < 6; vertex++)
        {
            var row = vertex < 3 ? vertex : 5 - vertex;
            var anchor = stepped ? vertex < 3 ? vertex * 2 : 5 + (vertex - 3) * 2 : vertex;
            var datum = after.Layers[0].Data[row];
            Assert.InRange(Math.Abs(views[1].Coordinates[anchor * 2] - views[0].Coordinates[anchor * 2] - (double)datum.DisplayOffsetX), 0d, 0.002d);
            Assert.InRange(Math.Abs(views[1].Coordinates[anchor * 2 + 1] - views[0].Coordinates[anchor * 2 + 1] - (double)datum.DisplayOffsetY), 0d, 0.002d);
        }
        Assert.Equal(views[0].Title, views[1].Title);
        Assert.Contains("X 8; Y", views[1].Title);
    }

    public static IEnumerable<object[]> AreaInterpolationCases()
    {
        foreach (var mode in new[] { "LINEAR", "SMOOTH", "STEP_BEFORE", "STEP_AFTER" })
            foreach (var unit in new[] { "DATA", "JITTER" })
                foreach (var reverse in new[] { false, true }) yield return [true, reverse, unit, true, mode, true];
        foreach (var mode in new[] { "SMOOTH", "STEP_BEFORE", "STEP_AFTER" })
            foreach (var ribbon in new[] { false, true })
                foreach (var unit in new[] { "DATA", "JITTER" })
                    foreach (var reverse in new[] { false, true }) yield return [ribbon, reverse, unit, true, mode];
    }

    public static IEnumerable<object[]> LineInterpolationCases()
    {
        foreach (var mode in new[] { "SMOOTH", "STEP_BEFORE", "STEP_AFTER" })
            foreach (var unit in new[] { "EM", "BAND", "DATA", "JITTER" })
                foreach (var reverse in new[] { false, true }) yield return [reverse, true, unit, mode];
    }

    private sealed class AreaPlacementView
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Length { get; set; }
        public double ExpectedLength { get; set; }
        public double[] Coordinates { get; set; } = [];
        public string Title { get; set; } = "";
    }

    private sealed class LinePlacementView
    {
        public double Length { get; set; }
        public double[] X { get; set; } = [];
        public double[] Y { get; set; } = [];
    }

    private sealed class DecorationGlyph
    {
        public int Row { get; set; }
        public string Shape { get; set; } = "";
        public string Title { get; set; } = "";
        public double Opacity { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed class ConnectionGlyph
    {
        public string Path { get; set; } = "";
        public double StrokeWidth { get; set; }
        public string Title { get; set; } = "";
        public string Paint { get; set; } = "";
        public double Opacity { get; set; }
        public double Length { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
