using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Parser.Components;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests
{
    public class DataLabelsTests
    {
        // -------------------------------------------------------------------------
        // Item 3: AST Serializer Tests
        // -------------------------------------------------------------------------

        [Fact]
        public void AstSerializer_ShouldPreserveQuotingForUnrelatedVisualOptions()
        {
            string script = @"CREATE VISUAL SalesChart AS BAR (
  SOURCE = #data,
  OPTIONS (
    TITLE = 'Quarterly Sales',
    LEGEND = 'OFF',
    BAND_SIZE = '0.65'
  )
);";
            var cv = ParseVisual(script);
            var serialized = AstSerializer.Format(cv);

            Assert.Contains("TITLE = 'Quarterly Sales'", serialized);
            Assert.Contains("LEGEND = 'OFF'", serialized);
            Assert.Contains("BAND_SIZE = '0.65'", serialized);
        }

        [Fact]
        public void AstSerializer_ShouldRoundtripSeriesLabelsAndLeaderLines()
        {
            string script = @"CREATE VISUAL LineVisual AS LINE (
  SOURCE = #data,
  OPTIONS (
    SERIES_LABELS = ON WITH (
      POSITION = END
    ),
    DATA_LABELS = ON WITH (
      LABEL_BACKGROUND = '#FFFFFF',
      LABEL_BORDER = '1px solid #E2E8F0',
      LEADER_LINE = ON WITH (
        COLOR = '#94A3B8',
        STYLE = SOLID
      )
    )
  )
);";

            var cv = ParseVisual(script);
            var serialized = AstSerializer.Format(cv);

            Assert.Contains("SERIES_LABELS = ON WITH (", serialized);
            Assert.Contains("POSITION = END", serialized);
            Assert.Contains("DATA_LABELS = ON WITH (", serialized);
            Assert.Contains("LABEL_BACKGROUND = '#FFFFFF'", serialized);
            Assert.Contains("LABEL_BORDER = '1px solid #E2E8F0'", serialized);
            Assert.Contains("LEADER_LINE = ON WITH (", serialized);
            Assert.Contains("COLOR = '#94A3B8'", serialized);
            Assert.Contains("STYLE = SOLID", serialized);
        }

        // -------------------------------------------------------------------------
        // Item 2: Parser Compatibility & Strictness Tests
        // -------------------------------------------------------------------------

        [Fact]
        public void Parser_ShouldAcceptPermissiveEqualsForDataLabels()
        {
            // DATA_LABELS without '='
            var visual1 = ParseVisual("CREATE VISUAL V1 AS BAR (SOURCE = #data, OPTIONS (DATA_LABELS ON));");
            Assert.Equal("ON", visual1.Options.First(o => o.Key == "DATA_LABELS").Value);

            // DATA_LABELS sub-option without '='
            var visual2 = ParseVisual("CREATE VISUAL V2 AS BAR (SOURCE = #data, OPTIONS (DATA_LABELS = ON WITH (POSITION INSIDE_TOP)));");
            Assert.Equal("INSIDE_TOP", visual2.Options.First(o => o.Key == "DATA_LABELS:POSITION").Value);

            // Standard DATA_LABELS with extended options
            var visual3 = ParseVisual(@"CREATE VISUAL V3 AS BAR (
              SOURCE = #data,
              OPTIONS (
                DATA_LABELS = ON WITH (
                  POSITION = INSIDE_TOP_RIGHT,
                  FONT_SIZE = 14,
                  COLOR = '#FF0000',
                  FONT_WEIGHT = BOLD,
                  FORMAT = 'N2'
                )
              )
            );");
            Assert.Equal("ON", visual3.Options.First(o => o.Key == "DATA_LABELS").Value);
            Assert.Equal("INSIDE_TOP_RIGHT", visual3.Options.First(o => o.Key == "DATA_LABELS:POSITION").Value);
            Assert.Equal("14", visual3.Options.First(o => o.Key == "DATA_LABELS:FONT_SIZE").Value);
            Assert.Equal("#FF0000", visual3.Options.First(o => o.Key == "DATA_LABELS:COLOR").Value);
            Assert.Equal("BOLD", visual3.Options.First(o => o.Key == "DATA_LABELS:FONT_WEIGHT").Value);
            Assert.Equal("N2", visual3.Options.First(o => o.Key == "DATA_LABELS:FORMAT").Value);
        }

        [Fact]
        public void Parser_ShouldRequireEqualsForSeriesLabelsAndLeaderLine()
        {
            // SERIES_LABELS missing '=' must fail
            Assert.Throws<SyntaxException>(() => ParseVisual("CREATE VISUAL V1 AS LINE (SOURCE = #data, OPTIONS (SERIES_LABELS ON));"));

            // LEADER_LINE missing '=' must fail
            Assert.Throws<SyntaxException>(() => ParseVisual("CREATE VISUAL V2 AS PIE (SOURCE = #data, OPTIONS (DATA_LABELS = ON WITH (LEADER_LINE ON)));"));
        }

        [Fact]
        public void Parser_ShouldRejectUnknownNestedKeys()
        {
            // Unknown key in SERIES_LABELS WITH (...)
            Assert.Throws<SyntaxException>(() => ParseVisual("CREATE VISUAL V2 AS LINE (SOURCE = #data, OPTIONS (SERIES_LABELS = ON WITH (UNKNOWN_KEY = START)));"));

            // Unknown key in LEADER_LINE WITH (...)
            Assert.Throws<SyntaxException>(() => ParseVisual("CREATE VISUAL V3 AS PIE (SOURCE = #data, OPTIONS (DATA_LABELS = ON WITH (LEADER_LINE = ON WITH (UNKNOWN_KEY = 123))));"));
        }

        [Fact]
        public void Parser_ShouldAllowUnknownFlatDataLabelsSubOptions_ForCompatibility()
        {
            var stmt = ParseVisual("CREATE VISUAL V1 AS BAR (SOURCE = #data, OPTIONS (DATA_LABELS = ON WITH (CUSTOM_TAG = 'xyz')));");
            Assert.NotNull(stmt);
            var opt = stmt.Options.FirstOrDefault(o => o.Key.Equals("DATA_LABELS:CUSTOM_TAG", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(opt);
            Assert.Equal("xyz", opt.Value);

            var sql = AstSerializer.Format(stmt);
            Assert.Contains("DATA_LABELS = ON WITH (CUSTOM_TAG = 'xyz')", sql);
        }

        [Fact]
        public void Parser_ShouldRejectOrphanNestedSettingsWithoutMainToggle()
        {
            // Orphan DATA_LABELS WITH (...) without main toggle
            Assert.Throws<SyntaxException>(() => ParseVisual("CREATE VISUAL V1 AS BAR (SOURCE = #data, OPTIONS (DATA_LABELS WITH (COLOR = '#FF0000')));"));

            // Orphan SERIES_LABELS WITH (...) without main toggle
            Assert.Throws<SyntaxException>(() => ParseVisual("CREATE VISUAL V2 AS LINE (SOURCE = #data, OPTIONS (SERIES_LABELS WITH (POSITION = START)));"));
        }

        // -------------------------------------------------------------------------
        // Item 8: Semantic Lowering & Conformance Tests
        // -------------------------------------------------------------------------

        [Fact]
        public void NamedVisualChartLowerer_ShouldValidateSeriesLabels()
        {
            var lowerer = new ETL_SQL.Reporting.Semantics.Runtime.NamedVisualChartLowerer();
            var manifest = new VisualManifest { Name = "Test", Rows = [], Columns = ["A", "B"] };

            // Valid on LINE
            var lineValid = ParseVisual("CREATE VISUAL L1 AS LINE (SOURCE = #data, OPTIONS (SERIES_LABELS = ON));");
            var exLine = Record.Exception(() => { lowerer.Lower(lineValid, manifest); });
            Assert.Null(exLine);

            // Valid on COMBO
            var comboValid = ParseVisual("CREATE VISUAL C1 AS COMBO (SOURCE = #data, OPTIONS (SERIES_LABELS = ON WITH (POSITION = START)));");
            var exCombo = Record.Exception(() => { lowerer.Lower(comboValid, manifest); });
            Assert.Null(exCombo);

            // Invalid on BAR
            var barInvalid = ParseVisual("CREATE VISUAL B1 AS BAR (SOURCE = #data, OPTIONS (SERIES_LABELS = ON));");
            var exBar = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(barInvalid, manifest); });
            Assert.Contains("SERIES_LABELS is supported only on LINE and COMBO visuals", exBar.Message);

            // Invalid POSITION
            var posInvalid = new CreateVisualStatement
            {
                Name = "L2",
                Source = lineValid.Source,
                VisualType = VisualType.Line,
                Options = [
                    new VisualOption { Key = "SERIES_LABELS", Value = "ON" },
                    new VisualOption { Key = "SERIES_LABELS:POSITION", Value = "CENTER" }
                ]
            };
            var exPos = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(posInvalid, manifest); });
            Assert.Contains("Invalid SERIES_LABELS POSITION 'CENTER'", exPos.Message);

            // Invalid toggle
            var toggleInvalid = new CreateVisualStatement
            {
                Name = "L3",
                Source = lineValid.Source,
                VisualType = VisualType.Line,
                Options = [
                    new VisualOption { Key = "SERIES_LABELS", Value = "MAYBE" }
                ]
            };
            var exToggle = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(toggleInvalid, manifest); });
            Assert.Contains("Invalid SERIES_LABELS value 'MAYBE'", exToggle.Message);

            var orphanPosition = posInvalid with
            {
                Options = [new VisualOption { Key = "SERIES_LABELS:POSITION", Value = "END" }]
            };
            var exOrphan = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(orphanPosition, manifest); });
            Assert.Contains("require the SERIES_LABELS toggle", exOrphan.Message);
        }

        [Fact]
        public void NamedVisualChartLowerer_ShouldValidateLeaderLines()
        {
            var lowerer = new ETL_SQL.Reporting.Semantics.Runtime.NamedVisualChartLowerer();
            var manifest = new VisualManifest { Name = "Test", Rows = [], Columns = ["A", "B"] };

            // Valid on PIE, DONUT, SCATTER
            foreach (var vt in new[] { "PIE", "DONUT", "SCATTER" })
            {
                var valid = ParseVisual($"CREATE VISUAL V1 AS {vt} (SOURCE = #data, OPTIONS (DATA_LABELS = ON WITH (LEADER_LINE = ON WITH (STYLE = DASHED))));");
                var ex = Record.Exception(() => { lowerer.Lower(valid, manifest); });
                Assert.Null(ex);
            }

            // Invalid on BAR
            var barInvalid = ParseVisual("CREATE VISUAL B1 AS BAR (SOURCE = #data, OPTIONS (DATA_LABELS = ON WITH (LEADER_LINE = ON)));");
            var exBar = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(barInvalid, manifest); });
            Assert.Contains("LEADER_LINE is supported only on PIE, DONUT, and SCATTER visuals", exBar.Message);

            // Invalid STYLE
            var pieValid = ParseVisual("CREATE VISUAL P1 AS PIE (SOURCE = #data, OPTIONS (DATA_LABELS = ON));");
            var styleInvalid = new CreateVisualStatement
            {
                Name = "P1",
                Source = pieValid.Source,
                VisualType = VisualType.Pie,
                Options = [
                    new VisualOption { Key = "DATA_LABELS", Value = "ON" },
                    new VisualOption { Key = "DATA_LABELS:LEADER_LINE", Value = "ON" },
                    new VisualOption { Key = "DATA_LABELS:LEADER_LINE:STYLE", Value = "DOTTED" }
                ]
            };
            var exStyle = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(styleInvalid, manifest); });
            Assert.Contains("Invalid LEADER_LINE STYLE 'DOTTED'", exStyle.Message);

            // Invalid toggle
            var leaderToggleInvalid = new CreateVisualStatement
            {
                Name = "P2",
                Source = pieValid.Source,
                VisualType = VisualType.Pie,
                Options = [
                    new VisualOption { Key = "DATA_LABELS", Value = "ON" },
                    new VisualOption { Key = "DATA_LABELS:LEADER_LINE", Value = "MAYBE" }
                ]
            };
            var exLeaderToggle = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(leaderToggleInvalid, manifest); });
            Assert.Contains("Invalid LEADER_LINE value 'MAYBE'", exLeaderToggle.Message);

            var orphanStyle = styleInvalid with
            {
                Options = [
                    new VisualOption { Key = "DATA_LABELS", Value = "ON" },
                    new VisualOption { Key = "DATA_LABELS:LEADER_LINE:STYLE", Value = "SOLID" }
                ]
            };
            var exOrphanStyle = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(orphanStyle, manifest); });
            Assert.Contains("require the LEADER_LINE toggle", exOrphanStyle.Message);

            var orphanDataLabels = styleInvalid with
            {
                Options = [new VisualOption { Key = "DATA_LABELS:LEADER_LINE", Value = "ON" }]
            };
            var exOrphanDataLabels = Assert.Throws<InvalidOperationException>(() => { lowerer.Lower(orphanDataLabels, manifest); });
            Assert.Contains("requires the DATA_LABELS toggle", exOrphanDataLabels.Message);
        }

        [Fact]
        public void Roundtrip_ParseSerializeParse_OptionsDictionaryMatches()
        {
            var scripts = new[]
            {
                @"CREATE VISUAL TestLine AS LINE (
  SOURCE = #data,
  OPTIONS (
    SERIES_LABELS = ON WITH (POSITION = START),
    DATA_LABELS = ON WITH (
      LABEL_BACKGROUND = '#FFFFFF',
      LABEL_BORDER = '1px solid #334155'
    )
  )
);",
                @"CREATE VISUAL TestPie AS PIE (
  SOURCE = #data,
  OPTIONS (
    DATA_LABELS = ON WITH (
      LABEL_BACKGROUND = '#F8FAFC',
      LABEL_BORDER = '2px dashed #64748B',
      LEADER_LINE = ON WITH (COLOR = '#DC2626', STYLE = DASHED)
    )
  )
);",
                @"CREATE VISUAL TestScatter AS SCATTER (
  SOURCE = #data,
  OPTIONS (
    DATA_LABELS = ON WITH (
      LEADER_LINE = ON WITH (COLOR = '#2563EB', STYLE = SOLID)
    )
  )
);"
            };

            foreach (var script in scripts)
            {
                var cv1 = ParseVisual(script);
                var formatted = AstSerializer.Format(cv1);
                var cv2 = ParseVisual(formatted);

                var d1 = cv1.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
                var d2 = cv2.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);

                Assert.Equal(d1.Count, d2.Count);
                foreach (var (k, v) in d1)
                {
                    Assert.True(d2.ContainsKey(k), $"Missing key '{k}' after roundtrip");
                    Assert.Equal(v, d2[k]);
                }
            }
        }

        [Fact]
        public void SemanticFallback_ProducesDeterministicOutput()
        {
            var visual = ParseVisual(@"CREATE VISUAL MyLine AS LINE (
              SOURCE = #data,
              MAPPINGS (X = month, Y = revenue),
              OPTIONS (
                SERIES_LABELS = ON WITH (POSITION = END),
                DATA_LABELS = ON WITH (LABEL_BACKGROUND = '#ffffff')
              )
            );");

            var manifest = new VisualManifest
            {
                Name = visual.Name,
                VisualType = visual.VisualType.ToString().ToUpperInvariant(),
                Options = visual.Options.ToDictionary(o => o.Key, o => o.Value),
                Columns = ["month", "revenue"],
                Rows = [
                    new List<string?> { "Jan", "100" },
                    new List<string?> { "Feb", "200" }
                ]
            };

            var fallback1 = VisualSemanticFallbackBuilder.Build(manifest);
            var fallback2 = VisualSemanticFallbackBuilder.Build(manifest);

            Assert.NotNull(fallback1);
            Assert.Equal(fallback1.Kind, fallback2.Kind);
            Assert.Equal(fallback1.Heading, fallback2.Heading);
            Assert.Equal(fallback1.Summary, fallback2.Summary);
            Assert.Equal(fallback1.Items.Length, fallback2.Items.Length);
            for (int i = 0; i < fallback1.Items.Length; i++)
            {
                Assert.Equal(fallback1.Items[i], fallback2.Items[i]);
            }
            Assert.Contains(fallback1.Items, item => item.Label == "Jan");
            Assert.Contains(fallback1.Items, item => item.Label == "Feb");
        }

        // -------------------------------------------------------------------------
        // Item 6: RendererBase Compatibility Rendering Tests
        // -------------------------------------------------------------------------

        private class TestRenderer : RendererBase
        {
            public static List<object> TestApply(VisualManifest v, List<object> series, bool stacked = false, bool smooth = false) =>
                ApplyCommonSeriesOptions(v, series, stacked, smooth);
        }

        [Fact]
        public void RendererBase_ShouldHardenLabelBackgroundAndBorder()
        {
            // 1. Safe background only
            var vBgOnly = new VisualManifest
            {
                Name = "V1",
                Options = new Dictionary<string, string>
                {
                    ["DATA_LABELS"] = "ON",
                    ["DATA_LABELS:LABEL_BACKGROUND"] = "#10B981"
                }
            };
            var resBg = TestRenderer.TestApply(vBgOnly, [new Dictionary<string, object>()]);
            var dictBg = (Dictionary<string, object>)((Dictionary<string, object>)resBg[0])["label"];
            Assert.Equal("#10B981", dictBg["backgroundColor"]);
            Assert.Equal(3, dictBg["padding"]);
            Assert.False(dictBg.ContainsKey("borderWidth"));

            // 2. Safe border only
            var vBorderOnly = new VisualManifest
            {
                Name = "V2",
                Options = new Dictionary<string, string>
                {
                    ["DATA_LABELS"] = "ON",
                    ["DATA_LABELS:LABEL_BORDER"] = "2px dashed #CA8A04"
                }
            };
            var resBorder = TestRenderer.TestApply(vBorderOnly, [new Dictionary<string, object>()]);
            var dictBorder = (Dictionary<string, object>)((Dictionary<string, object>)resBorder[0])["label"];
            Assert.Equal(2.0, (double)dictBorder["borderWidth"]);
            Assert.Equal("#CA8A04", dictBorder["borderColor"]);
            Assert.Equal("dashed", dictBorder["borderType"]);
            Assert.Equal(3, dictBorder["padding"]);
            Assert.False(dictBorder.ContainsKey("backgroundColor"));
            Assert.False(dictBorder.ContainsKey("LABEL_BORDER"));

            // 3. Both background and border
            var vBoth = new VisualManifest
            {
                Name = "V3",
                Options = new Dictionary<string, string>
                {
                    ["DATA_LABELS"] = "ON",
                    ["DATA_LABELS:LABEL_BACKGROUND"] = "#FEF08A",
                    ["DATA_LABELS:LABEL_BORDER"] = "1px solid #000000"
                }
            };
            var resBoth = TestRenderer.TestApply(vBoth, [new Dictionary<string, object>()]);
            var dictBoth = (Dictionary<string, object>)((Dictionary<string, object>)resBoth[0])["label"];
            Assert.Equal("#FEF08A", dictBoth["backgroundColor"]);
            Assert.Equal(1.0, (double)dictBoth["borderWidth"]);
            Assert.Equal("#000000", dictBoth["borderColor"]);
            Assert.Equal("solid", dictBoth["borderType"]);
            Assert.Equal(3, dictBoth["padding"]);

            // 4. Invalid background
            var vInvalidBg = new VisualManifest
            {
                Name = "V4",
                Options = new Dictionary<string, string>
                {
                    ["DATA_LABELS"] = "ON",
                    ["DATA_LABELS:LABEL_BACKGROUND"] = "not-a-color"
                }
            };
            var resInvalidBg = TestRenderer.TestApply(vInvalidBg, [new Dictionary<string, object>()]);
            var dictInvalidBg = (Dictionary<string, object>)((Dictionary<string, object>)resInvalidBg[0])["label"];
            Assert.False(dictInvalidBg.ContainsKey("backgroundColor"));
            Assert.False(dictInvalidBg.ContainsKey("padding"));

            // 5. Injection-shaped values ignored
            var vInjection = new VisualManifest
            {
                Name = "V5",
                Options = new Dictionary<string, string>
                {
                    ["DATA_LABELS"] = "ON",
                    ["DATA_LABELS:LABEL_BACKGROUND"] = "javascript:alert(1)",
                    ["DATA_LABELS:LABEL_BORDER"] = "1px solid <script>alert(1)</script>"
                }
            };
            var resInjection = TestRenderer.TestApply(vInjection, [new Dictionary<string, object>()]);
            var dictInjection = (Dictionary<string, object>)((Dictionary<string, object>)resInjection[0])["label"];
            Assert.False(dictInjection.ContainsKey("backgroundColor"));
            Assert.False(dictInjection.ContainsKey("borderWidth"));
            Assert.False(dictInjection.ContainsKey("padding"));

            // 6. Dotted border style
            var vDotted = new VisualManifest
            {
                Name = "V6",
                Options = new Dictionary<string, string>
                {
                    ["DATA_LABELS"] = "ON",
                    ["DATA_LABELS:LABEL_BORDER"] = "1.5px dotted #334155"
                }
            };
            var resDotted = TestRenderer.TestApply(vDotted, [new Dictionary<string, object>()]);
            var dictDotted = (Dictionary<string, object>)((Dictionary<string, object>)resDotted[0])["label"];
            Assert.Equal(1.5, (double)dictDotted["borderWidth"]);
            Assert.Equal("#334155", dictDotted["borderColor"]);
            Assert.Equal("dotted", dictDotted["borderType"]);
        }

        // -------------------------------------------------------------------------
        // Item 7: Deterministic Scatter Leader Line Tests
        // -------------------------------------------------------------------------

        [Fact]
        public async Task PlotPlanSvgRenderer_ShouldRenderScatterLeaderLinesDeterministically()
        {
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("scatter_multi_series_inferred.rptsql");
            var visual = Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            // Replace data with 2 colliding points
            var collidingData = ImmutableArray.Create(
                new ResolvedDatum(0, ImmutableArray.Create(
                    new ResolvedChannelValue(FieldChannel.X, ChartValue.From(15m), "15"),
                    new ResolvedChannelValue(FieldChannel.Y, ChartValue.From(64.5m), "64.5"),
                    new ResolvedChannelValue(FieldChannel.Text, ChartValue.From("ClusterA"), "ClusterA")
                ), false),
                new ResolvedDatum(1, ImmutableArray.Create(
                    new ResolvedChannelValue(FieldChannel.X, ChartValue.From(15.01m), "15.01"),
                    new ResolvedChannelValue(FieldChannel.Y, ChartValue.From(64.51m), "64.51"),
                    new ResolvedChannelValue(FieldChannel.Text, ChartValue.From("ClusterB"), "ClusterB")
                ), false)
            );
            var planBase = sourcePlan with
            {
                Layers = ImmutableArray.Create(sourcePlan.Layers[0] with { Data = collidingData })
            };

            // 1. Default OFF: zero leader paths
            var planOff = planBase with
            {
                Style = ImmutableArray.Create(new StyleToken("DATA_LABELS", "ON"))
            };
            var svgOff = new SvgChartRenderer().Render(planOff);
            Assert.Equal(0, CountOccurrences(svgOff, "class='plot-smart-label-leader'"));

            // 2. Explicit ON with DASHED: exactly 1 leader path for the 1 displaced label
            var planOnDashed = planBase with
            {
                Style = ImmutableArray.Create(
                    new StyleToken("DATA_LABELS", "ON"),
                    new StyleToken("DATA_LABELS:LEADER_LINE", "ON"),
                    new StyleToken("DATA_LABELS:LEADER_LINE:COLOR", "#dc2626"),
                    new StyleToken("DATA_LABELS:LEADER_LINE:STYLE", "DASHED")
                )
            };
            var svgOnDashed = new SvgChartRenderer().Render(planOnDashed);
            Assert.Equal(1, CountOccurrences(svgOnDashed, "class='plot-smart-label-leader'"));
            Assert.Contains("stroke='#dc2626'", svgOnDashed);
            Assert.Contains("stroke-dasharray='4 3'", svgOnDashed);

            // 3. Explicit ON with SOLID: exactly 1 leader path, no dasharray
            var planOnSolid = planBase with
            {
                Style = ImmutableArray.Create(
                    new StyleToken("DATA_LABELS", "ON"),
                    new StyleToken("DATA_LABELS:LEADER_LINE", "ON"),
                    new StyleToken("DATA_LABELS:LEADER_LINE:COLOR", "#2563eb"),
                    new StyleToken("DATA_LABELS:LEADER_LINE:STYLE", "SOLID")
                )
            };
            var svgOnSolid = new SvgChartRenderer().Render(planOnSolid);
            Assert.Equal(1, CountOccurrences(svgOnSolid, "class='plot-smart-label-leader'"));
            Assert.Contains("stroke='#2563eb'", svgOnSolid);
            Assert.DoesNotContain("stroke-dasharray", svgOnSolid);

            // 4. Unsafe color falls back safely to safe overlay default
            var planUnsafeColor = planBase with
            {
                Style = ImmutableArray.Create(
                    new StyleToken("DATA_LABELS", "ON"),
                    new StyleToken("DATA_LABELS:LEADER_LINE", "ON"),
                    new StyleToken("DATA_LABELS:LEADER_LINE:COLOR", "javascript:alert(1)")
                )
            };
            var svgUnsafe = new SvgChartRenderer().Render(planUnsafeColor);
            Assert.Equal(1, CountOccurrences(svgUnsafe, "class='plot-smart-label-leader'"));
            Assert.DoesNotContain("javascript", svgUnsafe);
            Assert.Contains("stroke='#444'", svgUnsafe);
        }

        // -------------------------------------------------------------------------
        // Item 4: Series Labels Layout, Coordinate Bounding & Edge Case Tests
        // -------------------------------------------------------------------------

        [Theory]
        [InlineData("START", true)]
        [InlineData("END", true)]
        [InlineData("START", false)]
        [InlineData("END", false)]
        public async Task PlotPlanSvgRenderer_SeriesLabels_BoundsAndLayout(string position, bool continuousX)
        {
            var fixture = continuousX ? "line_temporal_decimals.rptsql" : "line_null_gaps.rptsql";
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixture);
            var visual = Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var plan = sourcePlan with
            {
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", position))
            };

            var svg = new SvgChartRenderer().Render(plan);

            // Parse series label element
            var match = Regex.Match(svg, @"<text class='plot-series-label'[^>]*x='([0-9.]+)'[^>]*text-anchor='(start|end)'[^>]*>(.*?)</text>");
            Assert.True(match.Success, "Expected to find plot-series-label in emitted SVG");

            var labelX = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var anchor = match.Groups[2].Value;
            var labelText = match.Groups[3].Value;
            var textWidth = Math.Min(200m, Math.Max(8m, labelText.Length * 9m * .65m));

            if (position == "START")
            {
                Assert.Equal("end", anchor);
                // The label text spans [labelX - textWidth, labelX]
                Assert.True(labelX - textWidth >= 0m, $"Label left edge ({labelX - textWidth}) must be >= 0 (inside viewBox)");
            }
            else
            {
                Assert.Equal("start", anchor);
                // The label text spans [labelX, labelX + textWidth]
                Assert.True(labelX + textWidth <= plan.Bounds.Width, $"Label right edge ({labelX + textWidth}) must be <= {plan.Bounds.Width} (inside viewBox)");
            }
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_SeriesLabels_LongNameRemainsBounded()
        {
            const string longName = "ExtremelyLongSeriesTitleExceedingTwentyFiveCharacters";
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var plan = sourcePlan with
            {
                Series = sourcePlan.Series.Select(s => s with { Label = longName }).ToImmutableArray(),
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", "END"))
            };

            var svg = new SvgChartRenderer().Render(plan);
            var match = Regex.Match(svg, @"<text class='plot-series-label'[^>]*x='([0-9.]+)'[^>]*text-anchor='start'[^>]*>(.*?)</text>");
            Assert.True(match.Success);

            var labelX = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var textWidth = Math.Min(200m, Math.Max(8m, longName.Length * 9m * .65m));
            Assert.True(labelX + textWidth <= plan.Bounds.Width, $"Long series label ({labelX + textWidth}) must remain within viewBox ({plan.Bounds.Width})");
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_SeriesLabels_MultipleSeriesProduceExactlyOneLabelEach()
        {
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var l1 = sourcePlan.Layers[0] with { Id = "l1", SeriesKey = "s1" };
            var l2 = sourcePlan.Layers[0] with { Id = "l2", SeriesKey = "s2" };
            var s1 = new ResolvedSeries("s1", "SeriesAlpha", 0, "#111");
            var s2 = new ResolvedSeries("s2", "SeriesBeta", 1, "#222");

            var plan = sourcePlan with
            {
                Layers = ImmutableArray.Create(l1, l2),
                Series = ImmutableArray.Create(s1, s2),
                Palette = ImmutableArray.Create(new PaletteAssignment("s1", "#111"), new PaletteAssignment("s2", "#222")),
                Legend = ImmutableArray<LegendEntry>.Empty,
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
            };

            var svg = new SvgChartRenderer().Render(plan);
            Assert.Equal(2, CountOccurrences(svg, "class='plot-series-label'"));
            Assert.Contains(">SeriesAlpha</text>", svg);
            Assert.Contains(">SeriesBeta</text>", svg);
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_SeriesLabels_HandlesGapsAndEmptySeriesSafely()
        {
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            // Series with all gap data -> 0 series labels, no crash
            var emptyData = sourcePlan.Layers[0].Data.Select(d => d with { IsGap = true }).ToImmutableArray();
            var planEmpty = sourcePlan with
            {
                Layers = ImmutableArray.Create(sourcePlan.Layers[0] with { Data = emptyData }),
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
            };
            var svgEmpty = new SvgChartRenderer().Render(planEmpty);
            Assert.Equal(0, CountOccurrences(svgEmpty, "class='plot-series-label'"));

            // Series with nulls/gaps: line_null_gaps.rptsql has gaps in middle
            var (_, gappedManifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_null_gaps.rptsql");
            var gappedVisual = Assert.Single(gappedManifest.Visuals);
            var gappedPlan = Assert.IsType<PlotPlan>(gappedVisual.PlotPlan) with
            {
                Style = ImmutableArray.Create(new StyleToken("SERIES_LABELS", "ON"))
            };
            var svgGapped = new SvgChartRenderer().Render(gappedPlan);
            Assert.Equal(1, CountOccurrences(svgGapped, "class='plot-series-label'"));
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_SeriesLabels_EscapesHostileSeriesName()
        {
            const string hostileName = "<script>alert('xss')</script>";
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var plan = sourcePlan with
            {
                Series = sourcePlan.Series.Select(s => s with { Label = hostileName }).ToImmutableArray(),
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
            };

            var svg = new SvgChartRenderer().Render(plan);
            Assert.DoesNotContain("<script>", svg);
            Assert.Contains("&lt;script&gt;", svg);
        }

        [Theory]
        [InlineData("START")]
        [InlineData("END")]
        public async Task PlotPlanSvgRenderer_ShouldRenderSeriesLabelsOnStackedLine(string position)
        {
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = manifest.Visuals.Single(item => item.Name == "PrecisionTelemetryLine");
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var originalLayer = sourcePlan.Layers[0];
            var stackedData = originalLayer.Data.Select(d =>
            {
                var yVal = d.Channels.FirstOrDefault(c => c.Channel == FieldChannel.Y)?.Value;
                decimal val = yVal?.Decimal ?? (yVal?.FloatingPoint.HasValue == true ? (decimal)yVal.FloatingPoint.Value : 5m);
                return d with
                {
                    Channels = d.Channels
                        .Add(new ResolvedChannelValue(FieldChannel.YStart, ChartValue.From(0m), null))
                        .Add(new ResolvedChannelValue(FieldChannel.YEnd, ChartValue.From(val), null))
                };
            }).ToImmutableArray();

            var stackedLayer = originalLayer with
            {
                Stack = StackMode.Zero,
                Data = stackedData
            };

            var plan = sourcePlan with
            {
                Layers = ImmutableArray.Create(stackedLayer),
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase) && !t.Name.StartsWith("DATA_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("DATA_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", position))
            };

            var renderer = new SvgChartRenderer();
            var svg = renderer.Render(plan);

            // Directive 5: Assert plot-stacked-area exists before asserting the series label
            Assert.Contains("plot-stacked-area", svg);
            Assert.Contains("class='plot-series-label'", svg);
            Assert.Contains(">SensorReading</text>", svg);
            Assert.Contains(position == "START" ? "text-anchor='end'" : "text-anchor='start'", svg);

            // Directive 5: Cover START and END endpoint suppression on the stacked path
            var targetIndex = position == "START" ? 0 : stackedData.Length - 1;
            var targetY = stackedData[targetIndex].Channels.FirstOrDefault(c => c.Channel == FieldChannel.Y)?.Value;
            var targetVal = targetY?.Decimal ?? (targetY?.FloatingPoint.HasValue == true ? (decimal)targetY.FloatingPoint.Value : 0m);
            var targetDisplay = targetVal.ToString("0.###", CultureInfo.InvariantCulture);

            var otherIndex = position == "START" ? stackedData.Length - 1 : 0;
            var otherY = stackedData[otherIndex].Channels.FirstOrDefault(c => c.Channel == FieldChannel.Y)?.Value;
            var otherVal = otherY?.Decimal ?? (otherY?.FloatingPoint.HasValue == true ? (decimal)otherY.FloatingPoint.Value : 0m);
            var otherDisplay = otherVal.ToString("0.###", CultureInfo.InvariantCulture);

            Assert.DoesNotContain($">{targetDisplay}</text>", svg);
            Assert.Contains($">{otherDisplay}</text>", svg);
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_SeriesLabels_NotDescendedFromClipPathElement()
        {
            // Directive 1: Prove every plot-series-label is not descended from an element carrying clip-path
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = manifest.Visuals.Single(item => item.Name == "PrecisionTelemetryLine");
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var plan = sourcePlan with
            {
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", "END"))
            };

            var svg = new SvgChartRenderer().Render(plan);
            var doc = XDocument.Parse(svg);
            var seriesLabels = doc.Descendants().Where(e => e.Name.LocalName == "text" && (string?)e.Attribute("class") == "plot-series-label").ToList();
            Assert.NotEmpty(seriesLabels);

            foreach (var labelElement in seriesLabels)
            {
                var ancestorWithClip = labelElement.Ancestors().FirstOrDefault(a => a.Attribute("clip-path") is not null);
                Assert.Null(ancestorWithClip);
            }
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_ReferenceLine_SpansSameEffectivePlotBoundsAsGridAndAxis()
        {
            // Directive 2: Pass effective plot bounds through all mark renderers; reference line spans same bounds as grid and axis
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = manifest.Visuals.Single(item => item.Name == "PrecisionTelemetryLine");
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var refDatum = new ResolvedDatum(0, ImmutableArray.Create(new ResolvedChannelValue(FieldChannel.Y, ChartValue.From(105m), null)), false);
            var refLayer = new ResolvedMarkLayer("refRule", MarkKind.Rule, 10, null, ImmutableArray.Create(refDatum))
            {
                Style = ImmutableArray.Create(new StyleToken("overlayType", "ReferenceLine"), new StyleToken("label", "Target"))
            };

            var plan = sourcePlan with
            {
                Layers = sourcePlan.Layers.Add(refLayer),
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", "START"))
            };

            var svg = new SvgChartRenderer().Render(plan);
            var doc = XDocument.Parse(svg);

            var refLine = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-reference-line");
            Assert.NotNull(refLine);
            var refX1 = (string?)refLine.Attribute("x1");
            var refX2 = (string?)refLine.Attribute("x2");

            var gridLine = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-grid-line");
            Assert.NotNull(gridLine);
            Assert.Equal(refX1, (string?)gridLine.Attribute("x1"));
            Assert.Equal(refX2, (string?)gridLine.Attribute("x2"));

            var axisLine = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-axis-line" && (string?)e.Attribute("y1") == (string?)e.Attribute("y2"));
            Assert.NotNull(axisLine);
            Assert.Equal(refX1, (string?)axisLine.Attribute("x1"));
            Assert.Equal(refX2, (string?)axisLine.Attribute("x2"));
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_GutterComposition_NonOverlappingRegions()
        {
            // Directive 3: Non-overlapping layout between SERIES_LABELS END, LEGEND = RIGHT, REFERENCE_LINE, DATA_LABELS
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = manifest.Visuals.Single(item => item.Name == "PrecisionTelemetryLine");
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var l1 = sourcePlan.Layers[0] with { Id = "l1", SeriesKey = "s1" };
            var l2 = sourcePlan.Layers[0] with { Id = "l2", SeriesKey = "s2" };
            var s1 = new ResolvedSeries("s1", "AlphaSeries", 0, "#3366cc");
            var s2 = new ResolvedSeries("s2", "BetaSeries", 1, "#dc3912");
            var refDatum = new ResolvedDatum(0,
                ImmutableArray.Create(new ResolvedChannelValue(FieldChannel.Y, ChartValue.From(105m), null)), false);
            var refLayer = new ResolvedMarkLayer("refRule", MarkKind.Rule, 10, null, ImmutableArray.Create(refDatum))
            {
                Style = ImmutableArray.Create(
                    new StyleToken("overlayType", "ReferenceLine"),
                    new StyleToken("label", "Target"))
            };

            var plan = sourcePlan with
            {
                Layers = ImmutableArray.Create(l1, l2, refLayer),
                Series = ImmutableArray.Create(s1, s2),
                Palette = ImmutableArray.Create(new PaletteAssignment("s1", "#3366cc"), new PaletteAssignment("s2", "#dc3912")),
                Legend = ImmutableArray.Create(new LegendEntry("s1", "AlphaSeries", 0, "#3366cc"), new LegendEntry("s2", "BetaSeries", 1, "#dc3912")),
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase) && !t.Name.StartsWith("DATA_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("LEGEND", "ON"))
                    .Add(new StyleToken("LEGEND_POSITION", "RIGHT"))
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", "END"))
                    .Add(new StyleToken("DATA_LABELS", "ON"))
                    .Add(new StyleToken("DATA_LABELS:LABEL_BACKGROUND", "#ffffff"))
            };

            var svg = new SvgChartRenderer().Render(plan);
            var doc = XDocument.Parse(svg);

            var boxes = new List<(string Name, decimal Left, decimal Top, decimal Right, decimal Bottom)>();

            foreach (var el in doc.Descendants().Where(e => (string?)e.Attribute("class") == "plot-series-label"))
            {
                var x = decimal.Parse(el.Attribute("x")!.Value, CultureInfo.InvariantCulture);
                var y = decimal.Parse(el.Attribute("y")!.Value, CultureInfo.InvariantCulture);
                var text = el.Value;
                var w = text.Length * 6.5m;
                var anchor = (string?)el.Attribute("text-anchor") ?? "start";
                var left = anchor == "end" ? x - w : x;
                boxes.Add(($"SeriesLabel:{text}", left, y - 9m, left + w, y + 3m));
            }
            Assert.Equal(2, boxes.Count(box => box.Name.StartsWith("SeriesLabel:", StringComparison.Ordinal)));

            foreach (var el in doc.Descendants().Where(e => (string?)e.Attribute("class") == "plot-overlay-label-bg"))
            {
                var x = decimal.Parse(el.Attribute("x")!.Value, CultureInfo.InvariantCulture);
                var y = decimal.Parse(el.Attribute("y")!.Value, CultureInfo.InvariantCulture);
                var w = decimal.Parse(el.Attribute("width")!.Value, CultureInfo.InvariantCulture);
                var h = decimal.Parse(el.Attribute("height")!.Value, CultureInfo.InvariantCulture);
                boxes.Add(("OverlayLabel", x, y, x + w, y + h));
            }
            Assert.Single(boxes, box => box.Name == "OverlayLabel");

            foreach (var el in doc.Descendants().Where(e => (string?)e.Attribute("class") == "plot-data-label-bg"))
            {
                var x = decimal.Parse(el.Attribute("x")!.Value, CultureInfo.InvariantCulture);
                var y = decimal.Parse(el.Attribute("y")!.Value, CultureInfo.InvariantCulture);
                var w = decimal.Parse(el.Attribute("width")!.Value, CultureInfo.InvariantCulture);
                var h = decimal.Parse(el.Attribute("height")!.Value, CultureInfo.InvariantCulture);
                boxes.Add(("DataLabel", x, y, x + w, y + h));
            }
            Assert.Contains(boxes, box => box.Name == "DataLabel");

            var legendTexts = doc.Descendants()
                .Where(e => e.Name.LocalName == "text" && e.Attribute("class") is null &&
                    (e.Value == "AlphaSeries" || e.Value == "BetaSeries"))
                .ToList();
            Assert.Equal(2, legendTexts.Count);
            foreach (var textEl in legendTexts)
            {
                var x = decimal.Parse(textEl.Attribute("x")!.Value, CultureInfo.InvariantCulture);
                var y = decimal.Parse(textEl.Attribute("y")!.Value, CultureInfo.InvariantCulture);
                var w = textEl.Value.Length * 6m;
                boxes.Add(($"Legend:{textEl.Value}", x - 13m, y - 9m, x + w, y + 3m));
            }

            var firstLegendLeft = boxes.Where(box => box.Name.StartsWith("Legend:", StringComparison.Ordinal))
                .Min(box => box.Left);
            var sideLabelRight = boxes.Where(box => box.Name.StartsWith("SeriesLabel:", StringComparison.Ordinal) || box.Name == "OverlayLabel")
                .Max(box => box.Right);
            Assert.True(sideLabelRight <= firstLegendLeft,
                $"Side-label region ends at {sideLabelRight}, past legend region beginning at {firstLegendLeft}");

            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var b1 = boxes[i];
                    var b2 = boxes[j];
                    var overlap = b1.Left < b2.Right && b1.Right > b2.Left && b1.Top < b2.Bottom && b1.Bottom > b2.Top;
                    Assert.False(overlap, $"Box {b1.Name} [{b1.Left},{b1.Top},{b1.Right},{b1.Bottom}] overlaps with {b2.Name} [{b2.Left},{b2.Top},{b2.Right},{b2.Bottom}]");
                }
            }
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_NarrowChartAndVeryLongLabels_RemainConstrainedAndFitViewBox()
        {
            // Directive 4: Handle narrow charts (<300px) and long labels (>200px)
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = manifest.Visuals.Single(item => item.Name == "PrecisionTelemetryLine");
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            const string extraLongSeriesName = "SensorReadingSuperCalibratedEnterpriseProductionTelemetryMetricOverwhelmingFiftyCharacters";

            var plan = sourcePlan with
            {
                Bounds = sourcePlan.Bounds with { Width = 260m },
                Layers = ImmutableArray.Create(sourcePlan.Layers[0] with { SeriesKey = "s1" }),
                Series = ImmutableArray.Create(new ResolvedSeries("s1", extraLongSeriesName, 0, "#5470c6")),
                Palette = ImmutableArray.Create(new PaletteAssignment("s1", "#5470c6")),
                Legend = ImmutableArray<LegendEntry>.Empty,
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", "END"))
            };

            var svg = new SvgChartRenderer().Render(plan);
            var doc = XDocument.Parse(svg);

            var labelEl = doc.Descendants().FirstOrDefault(e => (string?)e.Attribute("class") == "plot-series-label");
            Assert.NotNull(labelEl);

            // Full name preserved in data-series-label and title
            Assert.Equal(extraLongSeriesName, (string?)labelEl.Attribute("data-series-label"));
            Assert.Equal(extraLongSeriesName, (string?)labelEl.Attribute("title"));

            // Displayed text is truncated ending with ellipsis
            Assert.EndsWith("…", labelEl.Value);

            var x = decimal.Parse(labelEl.Attribute("x")!.Value, CultureInfo.InvariantCulture);
            var textWidth = labelEl.Value.Length * 6.0m;
            Assert.True(x >= 0m, $"Label left edge ({x}) must be >= 0");
            Assert.True(x + textWidth <= 260m, $"Label right edge ({x + textWidth}) must be <= 260m (inside viewBox)");
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_SeriesLabels_StrengthenedDirectives()
        {
            // Directive 7: Strengthen series tests (color, deterministic order, colliding endpoints, hostile names, empty series, continuous numeric X)
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("scatter_multi_series_inferred.rptsql");
            var visual = Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            // Directive 7: Continuous numeric X is truly continuous with ScaleKind.Linear
            var xScale = sourcePlan.Scales.First(s => s.Channel == FieldChannel.X);
            Assert.Equal(ScaleKind.Linear, xScale.Kind);

            // Convert scatter to lines to test continuous linear X with series labels
            var l1 = sourcePlan.Layers[0] with { Mark = MarkKind.Line, SeriesKey = "s1" };
            var l2 = sourcePlan.Layers[1] with { Mark = MarkKind.Line, SeriesKey = "s2" };
            var s1 = new ResolvedSeries("s1", "VelocityA", 0, "#e11d48");
            var s2 = new ResolvedSeries("s2", "VelocityB", 1, "#2563eb");

            var plan = sourcePlan with
            {
                Layers = ImmutableArray.Create(l1, l2),
                Series = ImmutableArray.Create(s1, s2),
                Palette = ImmutableArray.Create(new PaletteAssignment("s1", "#e11d48"), new PaletteAssignment("s2", "#2563eb")),
                Legend = ImmutableArray<LegendEntry>.Empty,
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", "END"))
            };

            var svg = new SvgChartRenderer().Render(plan);
            var doc = XDocument.Parse(svg);

            var labelElements = doc.Descendants()
                .Where(e => (string?)e.Attribute("class") == "plot-series-label")
                .ToList();
            Assert.Equal(2, labelElements.Count);

            // Directive 7: Each series label uses its resolved series color
            Assert.Equal("#e11d48", (string?)labelElements[0].Attribute("fill"));
            Assert.Equal("#2563eb", (string?)labelElements[1].Attribute("fill"));

            // Directive 7: Multiple labels have deterministic order
            Assert.Equal("VelocityA", (string?)labelElements[0].Attribute("data-series-label"));
            Assert.Equal("VelocityB", (string?)labelElements[1].Attribute("data-series-label"));

            // Directive 7: Colliding endpoints do not produce overlapping label boxes
            var y1 = decimal.Parse(labelElements[0].Attribute("y")!.Value, CultureInfo.InvariantCulture);
            var y2 = decimal.Parse(labelElements[1].Attribute("y")!.Value, CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(y1 - y2) >= 15m, $"Series labels must be vertically separated (y1={y1}, y2={y2})");

            // Directive 7: Hostile truncated name remains properly escaped
            const string hostileLongName = "<script>alert('extra_long_hostile_xss_vector_that_will_be_truncated')</script>";
            var planHostile = plan with
            {
                Series = ImmutableArray.Create(s1 with { Label = hostileLongName }, s2)
            };
            var svgHostile = new SvgChartRenderer().Render(planHostile);
            Assert.DoesNotContain("<script>", svgHostile);
            Assert.Contains("&lt;script&gt;", svgHostile);

            // Directive 7: Series with no renderable data emits no label and consumes no unnecessary gutter
            var emptyLayer = l1 with { Data = ImmutableArray<ResolvedDatum>.Empty };
            var planEmpty = plan with
            {
                Layers = ImmutableArray.Create(emptyLayer),
                Series = ImmutableArray.Create(s1),
                Palette = ImmutableArray.Create(new PaletteAssignment("s1", "#e11d48"))
            };
            var svgEmpty = new SvgChartRenderer().Render(planEmpty);
            Assert.DoesNotContain("class='plot-series-label'", svgEmpty);
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_ShouldRenderSeriesLabelsOnCombo()
        {
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("combo_dual_axes.rptsql");
            var visual = manifest.Visuals.Single(item => item.Name == "FactoryPerformanceCombo");
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);
            var plan = sourcePlan with
            {
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("SERIES_LABELS", StringComparison.OrdinalIgnoreCase) && !t.Name.StartsWith("DATA_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("SERIES_LABELS", "ON"))
                    .Add(new StyleToken("SERIES_LABELS:POSITION", "END"))
                    .Add(new StyleToken("DATA_LABELS", "ON"))
            };

            var renderer = new SvgChartRenderer();
            var svg = renderer.Render(plan);

            Assert.Contains("class='plot-series-label'", svg);
            Assert.Contains(">QualityPassRate</text>", svg);
            // And its endpoint data label (Week 4: 98.4) should be suppressed to avoid collision
            Assert.DoesNotContain(">98.4</text>", svg);
            // Prior point data label should still be present
            Assert.Contains(">96.2", svg);
        }

        // -------------------------------------------------------------------------
        // Item 5: DATA_LABELS Background & Border Across All Named Chart Families
        // -------------------------------------------------------------------------

        [Theory]
        [InlineData("hbar_native_plot_plan.rptsql", null)]       // Rect / Bar
        [InlineData("line_temporal_decimals.rptsql", null)]       // Line / Smart label
        [InlineData("scatter_multi_series_inferred.rptsql", null)]// Point / Scatter
        [InlineData("pie_donut_proportions.rptsql", "LeadSourcePie")] // Arc / Pie
        [InlineData("heatmap_native_plot_plan.rptsql", null)]     // Heatmap
        [InlineData("funnel_native_plot_plan.rptsql", null)]      // Funnel
        public async Task PlotPlanSvgRenderer_DataLabelBackgroundAndBorder_AllNamedFamilies(string fixture, string? visualName)
        {
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixture);
            var visual = visualName is not null
                ? manifest.Visuals.Single(item => item.Name == visualName)
                : Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var plan = sourcePlan with
            {
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("DATA_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("DATA_LABELS", "ON"))
                    .Add(new StyleToken("DATA_LABELS:LABEL_BACKGROUND", "#f0fdf4"))
                    .Add(new StyleToken("DATA_LABELS:LABEL_BORDER", "1.5px dashed #16a34a"))
            };

            var renderer = new SvgChartRenderer();
            var svg = renderer.Render(plan);

            Assert.Contains("class='plot-data-label-bg'", svg);
            Assert.Contains("fill='#f0fdf4'", svg);
            Assert.Contains("stroke='#16a34a'", svg);
            Assert.Contains("stroke-width='1.5'", svg);
            Assert.Contains("stroke-dasharray='4 3'", svg);

            // Styling must NOT leak to axis labels, legends, or overlay badges
            Assert.DoesNotContain("class='plot-axis-label' fill='#f0fdf4'", svg);
            Assert.DoesNotContain("class='plot-legend-item' fill='#f0fdf4'", svg);
        }

        [Fact]
        public async Task PlotPlanSvgRenderer_DataLabelBackgroundAndBorder_StackedLine()
        {
            var (_, manifest, _) = await ETL_SQL.Tests.Reporting.Conformance.RepresentativeVisualConformanceHarness.CompileFixtureAsync("line_temporal_decimals.rptsql");
            var visual = Assert.Single(manifest.Visuals);
            var sourcePlan = Assert.IsType<PlotPlan>(visual.PlotPlan);

            var plan = sourcePlan with
            {
                Style = sourcePlan.Style
                    .Where(t => !t.Name.StartsWith("DATA_LABELS", StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray()
                    .Add(new StyleToken("STACKED", "ON"))
                    .Add(new StyleToken("DATA_LABELS", "ON"))
                    .Add(new StyleToken("DATA_LABELS:LABEL_BACKGROUND", "#eff6ff"))
                    .Add(new StyleToken("DATA_LABELS:LABEL_BORDER", "1px dotted #3b82f6"))
            };

            var renderer = new SvgChartRenderer();
            var svg = renderer.Render(plan);

            Assert.Contains("class='plot-data-label-bg'", svg);
            Assert.Contains("fill='#eff6ff'", svg);
            Assert.Contains("stroke='#3b82f6'", svg);
            Assert.Contains("stroke-width='1'", svg);
            Assert.Contains("stroke-dasharray='2 2'", svg);
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static CreateVisualStatement ParseVisual(string script)
        {
            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var statements = new List<Statement>();
            while (parser.Current.Type != TokenType.EOF) statements.Add(parser.ParseStatement());
            return (CreateVisualStatement)statements[0];
        }

        private static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }
    }
}
