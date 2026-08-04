using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Guards the shared state vocabulary in <c>js/portal-states.js</c>.
///
/// <para>Loading, denied, failed and empty look almost identical on screen — a mostly blank panel —
/// which is why they get conflated, and why the difference has to be carried by wording. A user who
/// cannot tell "you may not see this" from "the service is down" from "there is nothing here" reads
/// all three as the last, because it is the only one that needs no action from them.</para>
///
/// <para>What is asserted is what can be: the vocabulary is complete, each state emits a
/// distinguishable marker, and a caller cannot silently produce a state with no marker. Whether a
/// given surface uses it is a judgement about that surface, and a test that demanded every panel go
/// through one helper would block the cases that legitimately need something else.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class PortalStateVocabularyTests
{
    private static string Source() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot", "js", "portal-states.js"));

    [Theory]
    [InlineData("loadingState")]
    [InlineData("deniedState")]
    [InlineData("failedState")]
    [InlineData("emptyState")]
    [InlineData("statusChip")]
    [InlineData("installPortalStateStyles")]
    public void TheVocabularyIsComplete(string export)
    {
        // Either form -- the arrow helpers are `export const`, the style installer is a function.
        var source = Source();
        Assert.True(
            source.Contains($"export const {export}", StringComparison.Ordinal)
            || source.Contains($"export function {export}", StringComparison.Ordinal),
            $"portal-states.js does not export '{export}'.");
    }

    [Theory]
    [InlineData("loading")]
    [InlineData("unauthorized")]
    [InlineData("failed")]
    [InlineData("empty")]
    public void EveryStateEmitsItsOwnMarker(string state)
    {
        // The marker is how a test asserts *which* state a surface reached instead of inferring it
        // from whatever text happens to be on screen — which is exactly the inference that fails
        // when two states are worded similarly.
        //
        // Loading writes its marker literally; the other three go through `shell(state, …)`, so the
        // name is asserted at the call rather than in the template. Asserting the literal string
        // would have quietly passed only for loading.
        var source = Source();
        Assert.True(
            source.Contains($"data-portal-state=\"{state}\"", StringComparison.Ordinal)
            || source.Contains($"'{state}',", StringComparison.Ordinal),
            $"No state in portal-states.js emits the '{state}' marker.");
    }

    [Fact]
    public void TheDeniedStateNamesRoles_RatherThanJustRefusing()
    {
        // A denial without a way forward is a dead end. Naming the roles turns it into a request
        // the user can make of whoever administers their access.
        var source = Source();
        Assert.Contains("roles", source, StringComparison.Ordinal);
        Assert.Contains("not an empty view", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFailedStateRefusesToInventContent()
    {
        // The property that makes a failure state trustworthy: it shows nothing in place of the
        // real answer. A stand-in on screen is indistinguishable from real data.
        Assert.Contains("Nothing is shown in place of the real data", Source(), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryInterpolatedValue_IsEscaped()
    {
        // These helpers take caller-supplied strings — including server error messages — and build
        // HTML from them. One unescaped interpolation is an injection point on every surface that
        // adopts the vocabulary, which is the cost of sharing it.
        var source = Source();
        var body = source[source.IndexOf("const esc", StringComparison.Ordinal)..];

        // A regex cannot parse JavaScript, and this one does not pretend to: nested template
        // literals are skipped rather than mis-parsed, because a check that reports a false
        // position on every run is one people learn to ignore. What it does catch is the case that
        // matters — a bare caller-supplied identifier interpolated straight into markup.
        var unescaped = Regex.Matches(body, @"\$\{(?!esc\()(?<expr>[^}`]+)\}")
            .Select(m => m.Groups["expr"].Value.Trim())
            // Structural, not caller input: state/variant are literals in this file, and
            // title/body/extra arrive already escaped — which is the rule the helpers now follow,
            // so the escaping is visible at the interpolation rather than two frames away.
            .Where(expr => expr is not ("STYLES" or "state" or "variant" or "title" or "body" or "extra"))
            .ToList();

        Assert.True(unescaped.Count == 0,
            "These interpolations do not pass through esc():\n  " + string.Join("\n  ", unescaped));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
