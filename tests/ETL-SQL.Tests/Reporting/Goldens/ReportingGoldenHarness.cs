using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Tests.Reporting.Conformance;
using ETL_SQL.Tests.Reporting.TerminalSemantics;

namespace ETL_SQL.Tests.Reporting.Goldens;

/// <summary>
/// One golden lane for both visual catalogs — named visuals and the <c>CUSTOM</c> <c>CHART</c> grammar.
///
/// Fixtures are discovered from <c>tests/fixtures/reporting/conformance</c>, so adding a chart means
/// adding a <c>.rptsql</c> file and blessing it, not editing C#. Every fixture is pinned by artifacts
/// checked in beside the hashes, because a diff reading <c>AE3BF4... to 7C21B9...</c> is not reviewable:
///
/// <list type="bullet">
///   <item><description><c>NAME.plan.json</c> — the serialized <see cref="PlotPlan"/>. This is the durable
///   semantic gate: <c>PlotPlan.Validate()</c> enforces deterministic series, legend, and layer ordering,
///   so a moved plan hash is a semantic regression to stop on.</description></item>
///   <item><description><c>NAME.svg</c> — the rendered native SVG, compared independently of the plan. A
///   plan hash that holds while the SVG hash moves is a pure rendering change to review.</description></item>
///   <item><description><c>NAME.fallback.json</c> — the <see cref="SemanticFallback"/> served to non-visual
///   consumers.</description></item>
///   <item><description><c>terminal.txt</c> — the terminal render of the first page.</description></item>
/// </list>
///
/// Plan and SVG are recorded as <c>null</c> for visuals that genuinely have neither (TABLE, CARD, SLICER).
/// That absence is pinned like any other value, so a visual silently losing its plan fails rather than
/// quietly dropping out of the lane.
///
/// Bless with <c>pwsh -File scripts\Test-ReportingGoldens.ps1 -UpdateGolden</c>, which sets
/// <c>ETLSQL_REPORTING_GOLDEN_UPDATE</c> and rewrites the artifacts so they land in the diff.
/// </summary>
public static class ReportingGoldenHarness
{
    public const string UpdateEnvironmentVariable = "ETLSQL_REPORTING_GOLDEN_UPDATE";

    /// <summary>Fixed terminal width; the terminal artifact is meaningless without one.</summary>
    public const int TerminalWidth = 100;

    private static readonly JsonSerializerOptions ArtifactJson = CreateArtifactJson();

    public static bool IsUpdating =>
        string.Equals(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable), "1", StringComparison.Ordinal);

    public static string RepoRoot => RepresentativeVisualConformanceHarness.GetRepoRoot();

    public static string FixtureDirectory =>
        Path.Combine(RepoRoot, "tests", "fixtures", "reporting", "conformance");

    public static string GoldenDirectory =>
        Path.Combine(RepoRoot, "tests", "fixtures", "reporting", "goldens");

    public static string IndexPath => Path.Combine(GoldenDirectory, "index.json");

    /// <summary>
    /// Every fixture in the discovery directory, ordinal-sorted so theory case order is stable across
    /// platforms and file systems.
    /// </summary>
    public static IReadOnlyList<string> DiscoverFixtures()
    {
        if (!Directory.Exists(FixtureDirectory)) return [];
        return Directory.GetFiles(FixtureDirectory, "*.rptsql")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    // ── Artifact production ──────────────────────────────────────────────────

    public sealed record GoldenVisual(string Name, string? Plan, string? Svg, string Fallback);

    public sealed record GoldenFixture(string Fixture, string Terminal, ImmutableArray<GoldenVisual> Visuals);

    public static async Task<GoldenFixture> ProduceAsync(string fixtureFileName)
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);
        var svgRenderer = new SvgChartRenderer();

        var visuals = manifest.Visuals.Select(visual => new GoldenVisual(
            Name: visual.Name,
            Plan: visual.PlotPlan is null ? null : Normalize(ChartContractSerializer.Serialize(visual.PlotPlan)),
            Svg: NormalizeOrNull(svgRenderer.Render(visual)),
            Fallback: Normalize(SerializeFallback(visual)))).ToImmutableArray();

        return new GoldenFixture(fixtureFileName, RenderTerminal(manifest), visuals);
    }

    /// <summary>
    /// The first page rendered at a fixed width with colour and ANSI disabled, which is what makes the
    /// artifact a stable text file rather than an escape-sequence soup.
    /// </summary>
    public static string RenderTerminal(ReportManifest manifest)
    {
        var page = manifest.Pages.FirstOrDefault();
        if (page is null) return string.Empty;
        return TerminalSnapshotHarness.CaptureSnapshot(
            TerminalRenderer.RenderPage(page, manifest), TerminalWidth).NormalizedText;
    }

    /// <summary>
    /// The visual semantic fallback when it carries one, otherwise the resolved plan fallback. Recorded
    /// even when absent so a visual that stops serving one fails rather than disappearing from the lane.
    /// </summary>
    private static string SerializeFallback(VisualManifest visual)
    {
        var fallback = visual.SemanticFallback ?? visual.PlotPlan?.Fallback;
        return fallback is null ? "null" : JsonSerializer.Serialize(fallback, ArtifactJson);
    }

    // ── Artifact storage ─────────────────────────────────────────────────────

    public static string FixtureGoldenDirectory(string fixtureFileName) =>
        Path.Combine(GoldenDirectory, Path.GetFileNameWithoutExtension(fixtureFileName));

    public static string ArtifactPath(string fixtureFileName, string visualName, string suffix) =>
        Path.Combine(FixtureGoldenDirectory(fixtureFileName), SafeName(visualName) + "." + suffix);

    public static string TerminalPath(string fixtureFileName) =>
        Path.Combine(FixtureGoldenDirectory(fixtureFileName), "terminal.txt");

    /// <summary>Visual names reach the file system, so restrict them to a portable character set.</summary>
    public static string SafeName(string visualName)
    {
        var builder = new StringBuilder(visualName.Length);
        foreach (var character in visualName)
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        return builder.ToString();
    }

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // LF, no BOM: these artifacts are compared byte for byte on Windows and Linux runners alike.
        File.WriteAllText(path, Normalize(content), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string? ReadOrNull(string path) =>
        File.Exists(path) ? Normalize(File.ReadAllText(path)) : null;

    public static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    // ── Hashing and normalization ────────────────────────────────────────────

    /// <summary>
    /// Cross-platform normalization. Line endings collapse to LF and trailing whitespace is trimmed per
    /// line, so a Windows checkout and a Linux runner hash the same bytes.
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join("\n", lines.Select(line => line.TrimEnd())).Trim() + "\n";
    }

    private static string? NormalizeOrNull(string? value) => value is null ? null : Normalize(value);

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(value))));

    public static string? HashOrNull(string? value) => value is null ? null : Hash(value);

    private static JsonSerializerOptions CreateArtifactJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    // ── Index ────────────────────────────────────────────────────────────────

    public sealed class GoldenIndex
    {
        public string Schema { get; set; } = "etlsql.reporting.goldens/v1";

        public string Note { get; set; } =
            "Hashes are the fast comparison; the artifacts beside them are the reviewable record. " +
            "Bless with scripts/Test-ReportingGoldens.ps1 -UpdateGolden.";

        public SortedDictionary<string, GoldenIndexFixture> Fixtures { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class GoldenIndexFixture
    {
        public string Terminal { get; set; } = string.Empty;
        public List<GoldenIndexVisual> Visuals { get; set; } = [];
    }

    public sealed class GoldenIndexVisual
    {
        public string Name { get; set; } = string.Empty;
        public string? Plan { get; set; }
        public string? Svg { get; set; }
        public string Fallback { get; set; } = string.Empty;
    }

    public static GoldenIndex LoadIndex()
    {
        if (!File.Exists(IndexPath)) return new GoldenIndex();
        return JsonSerializer.Deserialize<GoldenIndex>(File.ReadAllText(IndexPath), ArtifactJson) ?? new GoldenIndex();
    }

    public static void SaveIndex(GoldenIndex index) =>
        Write(IndexPath, JsonSerializer.Serialize(index, ArtifactJson));

    public static GoldenIndexFixture ToIndexEntry(GoldenFixture fixture) => new()
    {
        Terminal = Hash(fixture.Terminal),
        Visuals = fixture.Visuals.Select(visual => new GoldenIndexVisual
        {
            Name = visual.Name,
            Plan = HashOrNull(visual.Plan),
            Svg = HashOrNull(visual.Svg),
            Fallback = Hash(visual.Fallback)
        }).ToList()
    };
}
