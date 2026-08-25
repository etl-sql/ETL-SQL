using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using ETL_SQL.Tests.Reporting.Conformance;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Goldens;

/// <summary>
/// The golden lane for both visual catalogs. Each fixture is its own theory case, so a failure names
/// the chart instead of reporting that something, somewhere, moved.
/// </summary>
public class ReportingGoldenTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        var fixtures = ReportingGoldenHarness.DiscoverFixtures();
        if (fixtures.Count == 0)
        {
            // Never yield an empty theory: xUnit would report the lane as passing with zero cases.
            yield return new object[] { "NO-FIXTURES-DISCOVERED" };
            yield break;
        }
        foreach (var fixture in fixtures) yield return new object[] { fixture };
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Fixture_PlanSvgTerminalAndFallback_MatchApprovedGoldens(string fixtureFileName)
    {
        Assert.NotEqual("NO-FIXTURES-DISCOVERED", fixtureFileName);

        var produced = await ReportingGoldenHarness.ProduceAsync(fixtureFileName);
        var index = ReportingGoldenHarness.LoadIndex();

        if (ReportingGoldenHarness.IsUpdating)
        {
            BlessFixture(produced, index);
            ReportingGoldenHarness.SaveIndex(index);
            return;
        }

        Assert.True(index.Fixtures.TryGetValue(fixtureFileName, out var approved),
            $"'{fixtureFileName}' has no golden. A fixture is discovered from the directory, so a new one " +
            $"must be blessed: pwsh -File scripts\\Test-ReportingGoldens.ps1 -UpdateGolden");

        var failures = new List<string>();

        CompareArtifact(failures, "terminal", ReportingGoldenHarness.TerminalPath(fixtureFileName),
            produced.Terminal, approved!.Terminal);

        // Visual identity and order are part of the golden: a renamed, reordered, dropped, or added
        // visual is a change to review, not something the comparison should absorb.
        var producedNames = produced.Visuals.Select(visual => visual.Name).ToArray();
        var approvedNames = approved.Visuals.Select(visual => visual.Name).ToArray();
        if (!producedNames.SequenceEqual(approvedNames, StringComparer.Ordinal))
        {
            failures.Add($"visuals: approved [{string.Join(", ", approvedNames)}] but rendered " +
                         $"[{string.Join(", ", producedNames)}]");
        }
        else
        {
            foreach (var (visual, expected) in produced.Visuals.Zip(approved.Visuals))
            {
                CompareNullableArtifact(failures, $"{visual.Name}.plan",
                    ReportingGoldenHarness.ArtifactPath(fixtureFileName, visual.Name, "plan.json"),
                    visual.Plan, expected.Plan);

                CompareNullableArtifact(failures, $"{visual.Name}.svg",
                    ReportingGoldenHarness.ArtifactPath(fixtureFileName, visual.Name, "svg"),
                    visual.Svg, expected.Svg);

                CompareArtifact(failures, $"{visual.Name}.fallback",
                    ReportingGoldenHarness.ArtifactPath(fixtureFileName, visual.Name, "fallback.json"),
                    visual.Fallback, expected.Fallback);
            }
        }

        Assert.True(failures.Count == 0,
            $"{fixtureFileName} diverged from its goldens:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures.Select(failure => "  " + failure)) +
            $"{Environment.NewLine}{Environment.NewLine}A moved plan hash is a semantic regression. A moved SVG " +
            $"hash with the plan holding is a rendering change. Review the checked-in artifacts under " +
            $"tests/fixtures/reporting/goldens/{Path.GetFileNameWithoutExtension(fixtureFileName)}, then bless " +
            $"with: pwsh -File scripts\\Test-ReportingGoldens.ps1 -UpdateGolden");
    }

    private static void CompareArtifact(List<string> failures, string label, string path, string produced, string approvedHash)
    {
        var hash = ReportingGoldenHarness.Hash(produced);
        if (!hash.Equals(approvedHash, StringComparison.Ordinal))
        {
            failures.Add($"{label}: hash {approvedHash} -> {hash}");
            return;
        }

        // The hash is the fast comparison; the artifact is the reviewable record. If they disagree, the
        // checked-in file no longer represents what the index approved.
        var onDisk = ReportingGoldenHarness.ReadOrNull(path);
        if (onDisk is null) failures.Add($"{label}: artifact missing at {path}");
        else if (!onDisk.Equals(ReportingGoldenHarness.Normalize(produced), StringComparison.Ordinal))
            failures.Add($"{label}: hash matches but the checked-in artifact at {path} does not");
    }

    private static void CompareNullableArtifact(List<string> failures, string label, string path, string? produced, string? approvedHash)
    {
        switch (produced, approvedHash)
        {
            case (null, null):
                if (File.Exists(path)) failures.Add($"{label}: approved as absent but {path} exists");
                return;
            case (null, not null):
                failures.Add($"{label}: was pinned but the visual no longer produces one");
                return;
            case (not null, null):
                failures.Add($"{label}: the visual now produces one but none is pinned");
                return;
            default:
                CompareArtifact(failures, label, path, produced!, approvedHash!);
                return;
        }
    }

    private static void BlessFixture(ReportingGoldenHarness.GoldenFixture produced, ReportingGoldenHarness.GoldenIndex index)
    {
        var fixture = produced.Fixture;
        ReportingGoldenHarness.Write(ReportingGoldenHarness.TerminalPath(fixture), produced.Terminal);

        foreach (var visual in produced.Visuals)
        {
            WriteOrRemove(ReportingGoldenHarness.ArtifactPath(fixture, visual.Name, "plan.json"), visual.Plan);
            WriteOrRemove(ReportingGoldenHarness.ArtifactPath(fixture, visual.Name, "svg"), visual.Svg);
            ReportingGoldenHarness.Write(ReportingGoldenHarness.ArtifactPath(fixture, visual.Name, "fallback.json"), visual.Fallback);
        }

        index.Fixtures[fixture] = ReportingGoldenHarness.ToIndexEntry(produced);
    }

    private static void WriteOrRemove(string path, string? content)
    {
        if (content is null) ReportingGoldenHarness.DeleteIfPresent(path);
        else ReportingGoldenHarness.Write(path, content);
    }

    /// <summary>
    /// The index may only describe fixtures that exist. Deleting a fixture without re-blessing would
    /// otherwise leave a stale entry that nothing ever evaluates.
    /// </summary>
    [Fact]
    public void GoldenIndex_HasNoEntriesWithoutAFixture()
    {
        if (ReportingGoldenHarness.IsUpdating) return;

        var discovered = ReportingGoldenHarness.DiscoverFixtures().ToHashSet(StringComparer.Ordinal);
        var orphaned = ReportingGoldenHarness.LoadIndex().Fixtures.Keys
            .Where(fixture => !discovered.Contains(fixture))
            .ToArray();

        Assert.True(orphaned.Length == 0,
            $"The golden index still pins fixtures that no longer exist: {string.Join(", ", orphaned)}. " +
            $"Re-bless to drop them.");
    }

    /// <summary>
    /// Both catalogs are covered by one harness. This fails if the CUSTOM catalog ever stops being
    /// represented — the state the lane was built to end.
    /// </summary>
    [Fact]
    public void GoldenLane_CoversBothTheNamedAndCustomCatalogs()
    {
        var fixtures = ReportingGoldenHarness.DiscoverFixtures();
        Assert.Contains(fixtures, fixture => fixture.StartsWith("custom_", StringComparison.Ordinal));
        Assert.Contains(fixtures, fixture => !fixture.StartsWith("custom_", StringComparison.Ordinal));
    }
}

/// <summary>
/// The SVG lane is hash-stable across platforms only because <c>PlotPlanSvgRenderer</c> emits no
/// timestamp, GUID, or <c>CurrentCulture</c> formatting, and asks for a generic <c>sans-serif</c> family
/// with no text measurement. ADR section 8.2 permits text measurement "where needed"; the day that option
/// is taken, SVG hashes become font- and platform-dependent and this lane goes flaky between developer
/// machines and CI.
///
/// These tests keep that precondition enforced rather than assumed. The plan hash stays the durable gate;
/// if text measurement ever lands, the SVG lane needs pinned metrics or must become advisory.
/// </summary>
public class NativeSvgDeterminismPreconditionTests
{
    private const string ProbeFixture = "custom_gradient_color_tick.rptsql";

    /// <summary>
    /// The strongest form of the locale precondition: render the same plan under a culture with a comma
    /// decimal separator and non-Gregorian-looking date formats, and require byte equality.
    /// </summary>
    [Fact]
    public async Task NativeSvg_IsByteIdenticalUnderALocaleWithADifferentDecimalSeparator()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(ProbeFixture);
        var visual = manifest.Visuals.First();

        var invariant = RenderUnder(CultureInfo.InvariantCulture, () => new SvgChartRenderer().Render(visual));
        var german = RenderUnder(new CultureInfo("de-DE"), () => new SvgChartRenderer().Render(visual));

        Assert.NotNull(invariant);
        Assert.Equal(invariant, german);
    }

    [Fact]
    public async Task NativeSvg_ContainsNoClockDerivedText()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(ProbeFixture);
        var svg = new SvgChartRenderer().Render(manifest.Visuals.First());
        Assert.NotNull(svg);

        // A render taken a moment apart must not differ, and must not carry today's date or a GUID.
        var again = new SvgChartRenderer().Render(manifest.Visuals.First());
        Assert.Equal(svg, again);

        Assert.DoesNotContain(DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), svg);
        Assert.False(Regex.IsMatch(svg!, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"),
            "The rendered SVG contains a GUID, so its hash is no longer reproducible.");
    }

    /// <summary>
    /// The font family is the other half of the precondition. A measured or non-generic family makes the
    /// geometry depend on what is installed on the machine that rendered it.
    /// </summary>
    [Fact]
    public async Task NativeSvg_RequestsOnlyTheGenericSansSerifFamily()
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(ProbeFixture);
        var svg = new SvgChartRenderer().Render(manifest.Visuals.First());
        Assert.NotNull(svg);

        var families = Regex.Matches(svg!, @"font-family='([^']*)'").Select(match => match.Groups[1].Value).ToArray();
        Assert.NotEmpty(families);
        Assert.All(families, family => Assert.Equal("sans-serif", family));
    }

    /// <summary>
    /// Source-level guard on the same precondition. Text measurement, clock reads, and GUIDs are the three
    /// ways the SVG lane becomes machine-dependent; this fails at the moment one is introduced rather than
    /// when CI hashes start disagreeing with a developer machine.
    /// </summary>
    [Fact]
    public void PlotPlanSvgRenderer_UsesNoClockGuidOrTextMeasurementApi()
    {
        var path = Path.Combine(ReportingGoldenHarness.RepoRoot,
            "src", "ETL-SQL.Reporting", "Renderers", "PlotPlanSvgRenderer.cs");
        Assert.True(File.Exists(path), $"Expected the native SVG renderer at {path}.");

        var source = File.ReadAllText(path).Replace("\r\n", "\n");
        string[] forbidden =
        [
            "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow",
            "Guid.NewGuid", "CultureInfo.CurrentCulture", "CultureInfo.CurrentUICulture",
            "MeasureString", "MeasureText", "System.Drawing", "SKPaint", "SkiaSharp"
        ];

        var found = forbidden.Where(token => source.Contains(token, StringComparison.Ordinal)).ToArray();
        Assert.True(found.Length == 0,
            $"PlotPlanSvgRenderer now uses {string.Join(", ", found)}. The SVG golden lane is only " +
            $"hash-stable across platforms while the renderer stays free of clock, GUID, ambient-culture, " +
            $"and text-measurement input. Either revert, or pin font metrics and make the SVG lane advisory " +
            $"while the plan hash stays the durable gate.");
    }

    private static T RenderUnder<T>(CultureInfo culture, Func<T> render)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
