using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Enforces the authoring component contract documented at the top of <c>studio-authoring.js</c>.
///
/// <para>Studio's guided wizards are the surface that lets an author produce Report-SQL without
/// writing it. They ship as one canonical module across five hosts, so a wizard that reaches past its
/// injected dependencies works on the host it was written against and degrades silently everywhere
/// else — which is exactly how the earlier route mismatch stayed invisible. These are the three rules
/// that can be checked by inspection; preview-before-write and read-state-from-the-parse are
/// behavioural and belong to the wizard test lane.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioAuthoringContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string DesignerAsset(string fileName) => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", fileName));

    private static string AuthoringJs() => DesignerAsset("studio-authoring.js");

    /// <summary>Every module bound by the authoring component contract.</summary>
    public static TheoryData<string> AuthoringModules() => new()
    {
        "studio-authoring.js",
        "studio-query-workbench.js",
        "studio-authoring-ui.js",
    };

    /// <summary>
    /// Strips comments so a rule documented in prose is not mistaken for a rule being broken in code.
    /// Deliberately simple: the module is authored with no regex or string literal containing the
    /// comment markers this would misread.
    /// </summary>
    private static string CodeOnly(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*[\s\S]*?\*/", string.Empty);
        return Regex.Replace(withoutBlocks, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
    }

    [Theory]
    [MemberData(nameof(AuthoringModules))]
    public void EveryAuthoringModule_IsHostNeutral(string module)
    {
        var code = CodeOnly(DesignerAsset(module));

        // Rule 1. Everything a surface needs arrives through its factory. A component that reads
        // localStorage or queries the shell is making an assumption about one host.
        Assert.DoesNotContain("localStorage", code, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", code, StringComparison.Ordinal);
        Assert.DoesNotContain("window.", code, StringComparison.Ordinal);

        // `document` is the injected active document; the browser global is reached only to build
        // detached elements, never to query the shell the surfaces are mounted into.
        Assert.DoesNotContain("document.querySelector", code, StringComparison.Ordinal);
        Assert.DoesNotContain("document.getElementById", code, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AuthoringModules))]
    public void EveryAuthoringModule_PerformsNoNetworkIoOfItsOwn(string module)
    {
        var code = CodeOnly(DesignerAsset(module));

        // Rule 2. All I/O goes through the injected `request`. `editorTransport.authFetch` is handed
        // to createScriptEditor, which owns its own transport; no authoring module calls it.
        Assert.DoesNotContain("fetch(", code.Replace("editorTransport.authFetch", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest", code, StringComparison.Ordinal);

        // Literal API paths are what made a route mismatch a user-visible failure rather than a test
        // failure. Routes come from the injected tables so the cross-host contract test can see them.
        var literalRoutes = Regex.Matches(code, @"['""`]/api/[^'""`]*['""`]")
            .Select(match => match.Value)
            .Distinct()
            .ToList();
        Assert.True(literalRoutes.Count == 0,
            $"{module} hardcodes API paths instead of using the injected route tables: "
            + string.Join(", ", literalRoutes));
    }
    [Fact]
    public void AuthoringModule_WritesScriptOnlyThroughTheCanonicalMutation()
    {
        var code = CodeOnly(AuthoringJs());

        // Rule 3. Every document change goes through the injected `mutate`, so a hand edit is never
        // clobbered and an unparseable document is never overwritten. The single deliberate exception
        // is USE DATASET, which the patcher cannot express; it is confined to one helper.
        var directWrites = Regex.Matches(code, @"shell\.setScriptText\(").Count;
        Assert.True(directWrites <= 1,
            $"studio-authoring.js writes the script directly in {directWrites} places. Only the "
            + "USE DATASET insertion may bypass the canonical mutation; everything else must go "
            + "through `mutate` so hand edits survive.");

        Assert.Contains("mutate(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("editorInstance", code, StringComparison.Ordinal);
        Assert.DoesNotContain("designerInstance", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringModule_CallsNothingThatOnlyExistsInTheCompositionLayer()
    {
        // Extracting the wizards out of studio.js left one call to `designerApiJson`, a helper that
        // only exists in the composition layer. It threw a ReferenceError on every open, a catch
        // meant for unparseable documents swallowed it, and the reuse-an-existing-dataset path was
        // dead behind an honest-looking "None available". Source inspection could see it; nothing
        // was looking. This is that check.
        var authoring = CodeOnly(AuthoringJs());
        var studio = CodeOnly(DesignerAsset("studio.js"));

        var compositionOnly = Regex.Matches(studio, @"\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToHashSet(StringComparer.Ordinal);

        // Names the authoring module defines for itself are its own, whatever studio.js also calls them.
        foreach (var defined in Regex.Matches(authoring, @"\bfunction\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")
                     .Select(match => match.Groups[1].Value))
        {
            compositionOnly.Remove(defined);
        }

        var called = Regex.Matches(authoring, @"(?<![.\w])([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(match => match.Groups[1].Value)
            .Where(compositionOnly.Contains)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(called.Count == 0,
            "studio-authoring.js calls helpers that live only in studio.js, so they are undefined at "
            + "runtime and any surrounding catch will hide it. Inject them instead: "
            + string.Join(", ", called));
    }

    [Fact]
    public void AuthoringModule_DefinesOrImportsEveryConstantItReferences()
    {
        // The same extraction left STUDIO_PARAMETER_TYPES and STUDIO_TOTAL_AGGREGATES behind in
        // studio.js. The parameter dialog opened with its header and an empty body, because render()
        // threw on a missing binding before it painted. The function check above could not see it —
        // a constant is not a call — so this covers the module-level constant convention as well.
        var code = CodeOnly(AuthoringJs());

        // Deliberately narrow: a constant is matched only where it is *used* as an object —
        // STUDIO_PARAMETER_TYPES.map(...) — never as a bare word. That skips SQL keywords sitting
        // in markup (GRAND_TOTAL = SUM) without needing to parse JavaScript, which a regex cannot
        // do correctly: an earlier attempt desynchronised on the /'/g literal in this same module
        // and silently stopped checking everything after it. The trade is that a constant passed
        // only as a bare argument is not covered; every one in this module is used as an object.
        var referenced = Regex.Matches(code, @"(?<![.\w$])([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)\s*\.")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        var available = Regex.Matches(code, @"(?:const|let|var)\s+([A-Z][A-Z0-9_]*)")
            .Select(match => match.Groups[1].Value)
            .Concat(Regex.Matches(code, @"import\s*\{([^}]*)\}")
                .SelectMany(match => match.Groups[1].Value.Split(','))
                .Select(entry => entry.Trim().Split(" as ").Last().Trim()))
            .ToHashSet(StringComparer.Ordinal);

        var undefined = referenced.Where(name => !available.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(undefined.Count == 0,
            "studio-authoring.js references module-level constants it neither defines nor imports, so "
            + "they are undefined at runtime: " + string.Join(", ", undefined));
    }

    [Fact]
    public void StudioComposition_DelegatesAuthoringToTheSharedModule()
    {
        var studio = DesignerAsset("studio.js");

        Assert.Contains("'./studio-authoring.js'", studio, StringComparison.Ordinal);
        Assert.Contains("createStudioAuthoringSurfaces({", studio, StringComparison.Ordinal);

        // The surfaces must not drift back into the composition layer, which is what made studio.js
        // grow 48 KB in a single commit before the split.
        Assert.DoesNotContain("async function openDataWizard", studio, StringComparison.Ordinal);
        Assert.DoesNotContain("async function openChartBuilder", studio, StringComparison.Ordinal);
        Assert.DoesNotContain("function studioDialog", studio, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualTypes_AreDeclaredOnceBesideTheirRoles()
    {
        var visualPreview = DesignerAsset("visual-preview.js");
        var studio = DesignerAsset("studio.js");

        // A palette entry with no role definition cannot be configured, and a role definition with no
        // palette entry cannot be reached, so the two lists live in one file.
        Assert.Contains("export const STUDIO_VISUAL_GROUPS", visualPreview, StringComparison.Ordinal);
        Assert.Contains("export const VISUAL_ROLES", visualPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("const STUDIO_VISUAL_GROUPS = [", studio, StringComparison.Ordinal);

        var declaredTypes = Regex.Matches(visualPreview, @"types: \[([^\]]+)\]")
            .SelectMany(match => match.Groups[1].Value.Split(','))
            .Select(entry => entry.Trim().Trim('\'').ToUpperInvariant())
            .Where(entry => entry.Length > 0)
            .ToList();

        Assert.NotEmpty(declaredTypes);
        Assert.Contains("BAR", declaredTypes);
        Assert.Contains("TABLE", declaredTypes);
    }
}
