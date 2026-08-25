using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

/// <summary>
/// Proves the editor and report preview report the same positioned CUSTOM chart failures.
/// </summary>
/// <remarks>
/// Before this lane, <c>AdvancedChartAuthoringRule</c> re-implemented most of the lowering contract by
/// hand and stamped every diagnostic on the CREATE VISUAL header, while lowering failures escaped into a
/// broad catch and became an unpositioned string painted inside the rendered visual. Both now run
/// <see cref="AdvancedChartSemanticValidator"/>, so a divergence here is a real drift regression.
/// </remarks>
public sealed class AdvancedChartDiagnosticParityTests
{
    /// <summary>Every lowering failure class, as the shortest script that triggers it.</summary>
    public static TheoryData<string, string, string> FailureClasses() => new()
    {
        {
            "undeclared scale",
            """
                SCALES (months = BAND (CHANNEL = X)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = missing)
                )))
            """,
            "references undeclared scale 'missing'"
        },
        {
            "no deterministic inference",
            """
                SCALES (months = BAND (CHANNEL = X)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  SIZE = Month (TYPE = NOMINAL)
                )))
            """,
            "no deterministic scale inference"
        },
        {
            "conflicting inferred scales",
            """
                LAYERS (
                  bars = RECT (INHERIT_ENCODINGS = OFF, ENCODINGS (
                    X = Month (TYPE = ORDINAL),
                    Y = Revenue (TYPE = QUANTITATIVE)
                  )),
                  dots = POINT (INHERIT_ENCODINGS = OFF, ENCODINGS (
                    X = Revenue (TYPE = QUANTITATIVE),
                    Y = MarginPct (TYPE = QUANTITATIVE)
                  ))
                )
            """,
            "requires incompatible inferred scales"
        },
        {
            "scale declared for another channel",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = revenue),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                )))
            """,
            "which is declared for a different channel"
        },
        {
            "TYPE incompatible with scale kind",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y = Month (TYPE = ORDINAL, SCALE = revenue)
                )))
            """,
            "the TYPE and scale kind are incompatible"
        },
        {
            "VALUE on a positional channel",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue),
                  Y_START = VALUE(0) (TYPE = QUANTITATIVE)
                )))
            """,
            "cannot bind visual-range VALUE to positional channel"
        },
        {
            "DATUM kind incompatible with declared TYPE",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y = DATUM('not-a-number') (TYPE = QUANTITATIVE, SCALE = revenue)
                )))
            """,
            "is incompatible with declared Quantitative TYPE"
        },
        {
            "CONDITIONS on a connected mark",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (trend = LINE (
                  ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                  ),
                  CONDITIONS (COLOR WHEN Revenue < 0 THEN '#b91c1c')
                ))
            """,
            "connected LINE marks"
        },
        {
            "TICK layer shape",
            """
                SCALES (months = LINEAR (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (ticks = TICK (ENCODINGS (
                  X = Revenue (TYPE = QUANTITATIVE, SCALE = months),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                )))
            """,
            "requires a nominal/ordinal X encoding"
        },
        {
            "ARC outside POLAR",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (slices = ARC (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                )))
            """,
            "ARC layers require POLAR coordinates"
        },
        {
            "STACK on a non-quantitative binding",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months, STACK = ZERO),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                )))
            """,
            "STACK requires a quantitative"
        },
        {
            "RECT interval missing its second endpoint",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y_START = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                )))
            """,
            "RECT layer 'bars' requires both endpoints in Y_START/Y_END"
        },
        {
            "RECT interval endpoints with mismatched types",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y_START = Revenue (TYPE = QUANTITATIVE, SCALE = revenue),
                  Y_END = Month (TYPE = ORDINAL, SCALE = revenue)
                )))
            """,
            "interval Y_START/Y_END requires matching quantitative or temporal endpoint types"
        },
        {
            "RECT combining Y with an interval",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue),
                  Y_START = Revenue (TYPE = QUANTITATIVE, SCALE = revenue),
                  Y_END = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                )))
            """,
            "cannot combine Y or Y2 with Y_START/Y_END"
        },
        {
            "RECT combining X with a horizontal interval",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  X_START = Revenue (TYPE = QUANTITATIVE),
                  X_END = Revenue (TYPE = QUANTITATIVE),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                )))
            """,
            "cannot combine X or X2 with X_START/X_END"
        },
        {
            "independent resolution without FACET",
            """
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (bars = RECT (ENCODINGS (
                  X = Month (TYPE = ORDINAL, SCALE = months),
                  Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                ))),
                RESOLVE (Y = INDEPENDENT)
            """,
            "Independent scale resolution requires FACET"
        }
    };

    [Theory]
    [MemberData(nameof(FailureClasses))]
    public async Task LintAndPreview_ReportTheSamePositionedFailure(string failureClass, string chartBody, string expectedFragment)
    {
        var script = BuildScript(chartBody);

        var lint = await LintAsync(script);
        Assert.NotEmpty(lint);
        Assert.Contains(lint, diagnostic => diagnostic.Message.Contains(expectedFragment, StringComparison.Ordinal));

        var preview = await PreviewDiagnosticsAsync(script);

        Assert.Equal(
            lint.Select(diagnostic => (diagnostic.Code, diagnostic.Message, diagnostic.LineNumber, diagnostic.ColumnNumber)).ToList(),
            preview.Select(diagnostic => (diagnostic.Code, diagnostic.Message, diagnostic.Line, diagnostic.Column)).ToList());
        Assert.All(preview, diagnostic => Assert.Equal("ERROR", diagnostic.Severity));
        Assert.False(string.IsNullOrEmpty(failureClass));
    }

    [Fact]
    public async Task Diagnostics_AnchorToTheOffendingNodeNotTheCreateVisualHeader()
    {
        var script = BuildScript("""
                SCALES (months = BAND (CHANNEL = X), revenue = LINEAR (CHANNEL = Y)),
                LAYERS (
                  bars = RECT (ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                  )),
                  dots = POINT (ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y = MarginPct (TYPE = QUANTITATIVE, SCALE = absent)
                  ))
                )
            """);

        var diagnostic = Assert.Single(await LintAsync(script));

        Assert.Contains("undeclared scale 'absent'", diagnostic.Message);
        Assert.Equal(LineOf(script, "SCALE = absent"), diagnostic.LineNumber);
        Assert.NotEqual(LineOf(script, "CREATE VISUAL"), diagnostic.LineNumber);
    }

    [Fact]
    public async Task EveryDuplicateIsReported_NotOnlyTheFirst()
    {
        var script = BuildScript("""
                SCALES (
                  months = BAND (CHANNEL = X),
                  months = POINT (CHANNEL = X),
                  months = ORDINAL (CHANNEL = X),
                  revenue = LINEAR (CHANNEL = Y)
                ),
                LAYERS (
                  bars = RECT (ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
                  )),
                  bars = POINT (ENCODINGS (
                    X = Month (TYPE = ORDINAL, SCALE = months),
                    Y = MarginPct (TYPE = QUANTITATIVE, SCALE = revenue)
                  ))
                )
            """);

        var diagnostics = await LintAsync(script);

        var duplicateScales = diagnostics.Where(diagnostic => diagnostic.Message == "Duplicate scale 'months'.").ToList();
        var duplicateLayers = diagnostics.Where(diagnostic => diagnostic.Message == "Duplicate layer 'bars'.").ToList();

        // Two repeats of the scale name, so two diagnostics — one anchored to each repeated declaration.
        Assert.Equal(2, duplicateScales.Count);
        Assert.Equal(2, duplicateScales.Select(diagnostic => diagnostic.LineNumber).Distinct().Count());
        Assert.Single(duplicateLayers);
    }

    [Fact]
    public async Task PublishedCustomChartExamples_LintClean()
    {
        var root = RepositoryRoot();
        var scripts = new[]
        {
            Path.Combine(root, "samples", "08_Reporting", "custom_chart_learning_path.rptsql"),
            Path.Combine(root, "samples", "08_Reporting", "declarative_geometry_refinements.rptsql"),
            Path.Combine(root, "samples", "10_Kitchen_Sinks", "39_CUSTOM_LAYERS.rptsql"),
            Path.Combine(root, "tests", "fixtures", "reporting", "conformance", "custom_ordinal_secondary_points.rptsql")
        };

        foreach (var path in scripts)
        {
            var diagnostics = await LintAsync(await File.ReadAllTextAsync(path));
            Assert.True(diagnostics.Count == 0,
                $"{Path.GetFileName(path)}: {string.Join(" | ", diagnostics.Select(diagnostic => $"{diagnostic.LineNumber}:{diagnostic.ColumnNumber} {diagnostic.Message}"))}");
        }
    }

    private static async Task<List<LintResult>> LintAsync(string script)
    {
        var parsed = new Parser(new Lexer(script).Tokenize(), script).Parse();
        Assert.Empty(parsed.Diagnostics);
        var results = await new AdvancedChartAuthoringRule().AnalyzeAsync(parsed, new DefaultLintContext());
        return results.ToList();
    }

    private static async Task<List<VisualDiagnosticManifest>> PreviewDiagnosticsAsync(string script)
    {
        var path = Path.Combine(Path.GetTempPath(), $"custom-chart-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, script);
        try
        {
            await using var service = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
            var manifest = await service.GetManifestAsync();
            var visual = manifest.Visuals.Single(item => item.Name == "Broken");
            Assert.False(string.IsNullOrEmpty(visual.Error));
            Assert.NotNull(visual.Diagnostics);
            return visual.Diagnostics!;
        }
        finally { File.Delete(path); }
    }

    private static string BuildScript(string chartBody) => $"""
        SELECT 'Jan' AS Month, 120 AS Revenue, CAST(0.28 AS DECIMAL) AS MarginPct INTO #performance
        UNION ALL SELECT 'Feb', 145, CAST(0.32 AS DECIMAL);

        CREATE VISUAL Broken AS CUSTOM (
          SOURCE = #performance,
          CHART (
            COORDINATE (TYPE = CARTESIAN),
        {chartBody}
          )
        );

        CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = Broken));
        """;

    private static int LineOf(string script, string fragment)
    {
        var lines = script.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (lines[index].Contains(fragment, StringComparison.Ordinal))
                return index + 1;
        throw new InvalidOperationException($"Fragment '{fragment}' is not in the script.");
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ETL-SQL.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
