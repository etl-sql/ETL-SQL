using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// A guard on the shipped governance module's source.
///
/// <para>The failure this exists to prevent already happened once: the dashboard substituted a
/// hard-coded set of assets whenever its API call threw, and kept findings, decisions, glossary
/// terms, badges, and scoring thresholds in browser memory. Both bugs are invisible from the
/// outside — the page renders, the numbers look plausible, and nothing on screen says the estate
/// being described is fictional. An operator making a compliance statement from that screen has no
/// way to tell.</para>
///
/// <para>Source-level assertions are used deliberately. A behavioural test proves the module does
/// the right thing on the paths it exercises; only reading the source proves there is no fallback
/// dataset on a path nobody thought to exercise. The two are complementary, and
/// <c>GovernanceDashboardTests</c> covers the behaviour.</para>
/// </summary>
[Trait("Category", "Portal")]
[Trait("Category", "Smoke.Security")]
public sealed class GovernanceNoDemoDataTests
{
    private static string ModuleSource()
    {
        var path = Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot", "js", "governance-portal.js");
        Assert.True(File.Exists(path), $"Governance module not found at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ShippedModule_HasNoFallbackDatasetWhenTheApiFails()
    {
        var source = ModuleSource();

        // The original defect verbatim: `catch { state.items = [ …hard-coded assets… ] }`. Any catch
        // block that assigns a non-empty array literal is the same shape whatever it is named.
        foreach (Match c in Regex.Matches(source, @"catch\s*\([^)]*\)\s*\{", RegexOptions.None))
        {
            var block = BalancedBlock(source, source.IndexOf('{', c.Index));
            Assert.False(
                Regex.IsMatch(block, @"=\s*\[\s*\{"),
                "A catch block seeds an object array. When the governance API fails the dashboard must "
                + "show the failure, not stand-in records: invented assets on screen are "
                + "indistinguishable from real ones.\n" + block);
        }

        // Failure must be reachable as a state, not swallowed.
        Assert.Contains("'failed'", source);
        Assert.Contains("'unauthorized'", source);
    }

    [Fact]
    public void ShippedModule_KeepsNoSeededWorkflowStateInTheBrowser()
    {
        var source = ModuleSource();
        var stateBlock = BalancedBlock(source, source.IndexOf("const state = {", StringComparison.Ordinal)
            + "const state = ".Length);

        // Workflow collections start empty and are filled from the API. A seeded entry here is a
        // finding, decision, term, or threshold that exists only in this tab — unreviewable by a
        // second person and gone on refresh, while looking exactly like durable state.
        foreach (var collection in new[] { "findings", "categories", "glossary", "dashboard", "settings" })
        {
            var match = Regex.Match(stateBlock, collection + @"\s*:\s*([^,\r\n]+)");
            Assert.True(match.Success, $"state.{collection} is missing.");
            var initial = match.Groups[1].Value.Trim();
            Assert.True(
                initial is "[]" or "null",
                $"state.{collection} is seeded with '{initial}'. Governance state must arrive from "
                + "/api/governance/*, never from a literal in the page.");
        }
    }

    [Theory]
    // Names from the retired prototype's fixture set. They are asserted individually because this
    // is the exact content that shipped in the production bundle, and a paste-back is the most
    // likely way it returns.
    [InlineData("sales_yearly_rollup")]
    [InlineData("hr_salary_report")]
    [InlineData("patient_health_audit")]
    [InlineData("finance_balance_sheet")]
    [InlineData("inventory_reorder_trigger")]
    [InlineData("bi_report_debug")]
    [InlineData("stage_customer_temp")]
    public void ShippedModule_ContainsNoPrototypeFixtureNames(string fixtureName)
    {
        Assert.DoesNotContain(fixtureName, ModuleSource(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShippedModule_NamesNoPersonAsADefaultSteward()
    {
        // The prototype attributed every decision to a hard-coded steward, so the audit trail on
        // screen named someone who had not made the decision. Attribution comes from the server's
        // record of who acted, or it is not attribution.
        var source = ModuleSource();
        foreach (var name in new[] { "'Chuck'", "\"Chuck\"", "'Sarah'", "\"Sarah\"", "'Dan'", "\"Dan\"" })
            Assert.DoesNotContain(name, source, StringComparison.Ordinal);
    }

    [Fact]
    public void SandboxStory_DrivesTheRealModuleRatherThanReimplementingIt()
    {
        var path = Path.Combine(RepoRoot(), "tools", "ui-sandbox", "stories", "governance.story.js");
        Assert.True(File.Exists(path), $"Governance story not found at {path}");
        var story = File.ReadAllText(path);

        // Fixture data in a story is correct and expected. A story that re-implements the UI is not:
        // it can look right while the shipped module is broken, and its fixtures sit in the repo as
        // a ready-made source of fake governance records.
        Assert.Contains("import { createGovernancePortal }", story);
        Assert.Contains("governance-portal.js", story);
    }

    /// <summary>Returns the source from <paramref name="openBraceIndex"/> through its matching brace.</summary>
    private static string BalancedBlock(string source, int openBraceIndex)
    {
        var start = source.IndexOf('{', openBraceIndex);
        Assert.True(start >= 0, "No opening brace found.");
        var depth = 0;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[start..(i + 1)];
        }
        return source[start..];
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
