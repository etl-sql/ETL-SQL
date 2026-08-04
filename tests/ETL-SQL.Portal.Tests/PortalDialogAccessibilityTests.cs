using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Every dialog the Portal renders must be a dialog to a screen reader, not merely to a sighted
/// user.
///
/// <para>An overlay styled to look modal but marked up as a plain <c>&lt;div&gt;</c> is invisible as
/// a dialog to assistive technology: it is announced as ordinary page content, the user is never
/// told a dialog opened, and the content behind it stays reachable — so the "modal" that is
/// blocking a sighted user is not blocking anyone else. The four attributes checked here are the
/// difference, and they are cheap enough that the only reason to omit one is having forgotten.</para>
///
/// <para>This is a source-level sweep on purpose. It covers every page and every JS module at once,
/// including the ones no browser test happens to open — and the dialogs nobody opens are exactly
/// where this regresses. <c>GovernanceDashboardUiTests</c> and the browser lane check the computed
/// behaviour on the pages they visit; the two are complementary.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class PortalDialogAccessibilityTests
{
    /// <summary>
    /// Matches a class token that <em>names itself</em> a modal's outermost element — anything
    /// ending in <c>modal-overlay</c>, <c>modal-backdrop</c>, <c>dialog-overlay</c>, or
    /// <c>dialog-backdrop</c>, with or without a module prefix (<c>gov-modal-backdrop</c>).
    ///
    /// <para>Deliberately a pattern rather than a list of known class names. The first version of
    /// this test carried a fixed list and passed with 31 green assertions while three unmarked
    /// dialogs sat in <c>governance-portal.js</c> under a prefixed class the list did not
    /// contain. A guard whose coverage depends on someone remembering to extend it is a guard that
    /// silently stops covering new code, which is worse than no guard: it reports safety.</para>
    /// </summary>
    private static readonly Regex OverlayClassPattern =
        new(@"^([a-z0-9]+-)*(modal|dialog)-(overlay|backdrop)$", RegexOptions.IgnoreCase);

    public static TheoryData<string> Surfaces()
    {
        var data = new TheoryData<string>();
        var root = Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot");
        foreach (var file in Directory.EnumerateFiles(root, "*.html"))
            data.Add(Path.GetRelativePath(root, file));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "js"), "*.js"))
        {
            // Vendored bundles are not ours to annotate.
            if (Path.GetFileName(file).Contains(".min.", StringComparison.Ordinal)) continue;
            data.Add(Path.GetRelativePath(root, file));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void EveryModalOverlay_IsASemanticNamedModalDialog(string relativePath)
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot", relativePath));

        foreach (var (tag, index) in FindOverlayOpeningTags(source))
        {
            var where = $"{relativePath} at offset {index}: {Truncate(tag)}";

            // Without role=dialog the overlay is announced as ordinary content, so the user is
            // never told anything opened.
            Assert.True(
                tag.Contains("role=\"dialog\"", StringComparison.Ordinal)
                || tag.Contains("role='dialog'", StringComparison.Ordinal)
                || tag.Contains("role=\"alertdialog\"", StringComparison.Ordinal),
                $"Modal overlay is missing role=\"dialog\". {where}");

            // Without aria-modal the content behind stays in the accessibility tree, so the modal
            // blocks a sighted user and nobody else.
            Assert.True(
                tag.Contains("aria-modal=\"true\"", StringComparison.Ordinal)
                || tag.Contains("aria-modal='true'", StringComparison.Ordinal),
                $"Modal overlay is missing aria-modal=\"true\". {where}");

            // An unnamed dialog is announced as just "dialog", which says nothing about what the
            // user is being asked to decide.
            Assert.True(
                tag.Contains("aria-labelledby", StringComparison.Ordinal)
                || tag.Contains("aria-label", StringComparison.Ordinal),
                $"Modal overlay has no accessible name (aria-labelledby or aria-label). {where}");
        }
    }

    [Fact]
    public void ModalOverlays_AreHiddenFromTheAccessibilityTreeWhenClosed()
    {
        // `visibility:hidden` and `display:none` remove an element from the accessibility tree;
        // `opacity:0` and off-screen positioning do not — a dialog hidden that way is still
        // announced, still focusable, and still tab-reachable while invisible.
        var root = Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.Contains(".min.", StringComparison.Ordinal)) continue;
            if (Path.GetExtension(file) is not (".html" or ".js" or ".css")) continue;

            var source = File.ReadAllText(file);
            foreach (Match rule in Regex.Matches(
                source, @"\.(modal-overlay|modal-backdrop|gov-modal-backdrop)\s*\{([^}]*)\}"))
            {
                var body = rule.Groups[2].Value;
                var hidesByOpacityOnly =
                    Regex.IsMatch(body, @"opacity\s*:\s*0")
                    && !Regex.IsMatch(body, @"display\s*:\s*none")
                    && !Regex.IsMatch(body, @"visibility\s*:\s*hidden");
                if (hidesByOpacityOnly)
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {rule.Groups[1].Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These modal styles hide the dialog visually but leave it in the accessibility tree, "
            + "focusable and tab-reachable:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryPageWithADialog_ManagesFocusForIt()
    {
        // Marking an overlay `role="dialog"` tells a screen reader a dialog opened. It does nothing
        // for the keyboard: without focus management the user is left behind the dialog, Tab walks
        // out into the content it is supposedly blocking, and closing it drops focus back at the top
        // of the document. `studio.html` shipped in exactly that state.
        var root = Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot");
        var untrapped = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.html"))
        {
            var source = File.ReadAllText(file);
            if (!FindOverlayOpeningTags(source).Any() && !source.Contains("role=\"dialog\"")) continue;

            var hasOwnTrap = source.Contains("_trapFocus", StringComparison.Ordinal)
                || source.Contains("e.key === 'Tab'", StringComparison.Ordinal)
                || source.Contains("event.key === 'Tab'", StringComparison.Ordinal);
            var usesSharedModule = source.Contains("installDialogAccessibility()", StringComparison.Ordinal);

            if (!hasOwnTrap && !usesSharedModule)
                untrapped.Add(Path.GetFileName(file));
        }

        Assert.True(untrapped.Count == 0,
            "These pages present dialogs with no focus management. Import "
            + "`installDialogAccessibility` from /js/dialog-a11y.js and call it:\n  "
            + string.Join("\n  ", untrapped));
    }

    /// <summary>
    /// Returns each opening tag that carries a modal overlay class, whether it is written as HTML
    /// or built inside a JavaScript template literal.
    /// </summary>
    private static IEnumerable<(string Tag, int Index)> FindOverlayOpeningTags(string source)
    {
        foreach (Match match in Regex.Matches(source, @"<(div|section|aside)\b[^>]*>"))
        {
            var tag = match.Value;
            if (!CarriesOverlayClass(tag)) continue;
            yield return (tag, match.Index);
        }
    }

    /// <summary>
    /// True when any whole class token names an overlay. Tokens are split rather than substring
    /// matched, so <c>modal-backdrop-inner</c> — a child of the dialog — is not asked for
    /// attributes that belong on its parent.
    /// </summary>
    private static bool CarriesOverlayClass(string tag)
    {
        var classAttr = Regex.Match(tag, @"class\s*=\s*[""']([^""']*)[""']");
        return classAttr.Success
            && classAttr.Groups[1].Value
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Any(token => OverlayClassPattern.IsMatch(token));
    }

    private static string Truncate(string value) =>
        value.Length <= 160 ? value : value[..160] + "…";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
