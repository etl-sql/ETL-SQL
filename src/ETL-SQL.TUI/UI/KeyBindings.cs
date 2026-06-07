using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.TUI.UI
{
    /// <summary>Logical grouping a keybinding appears under in the F1 help reference.</summary>
    public enum KeyCategory
    {
        FileTabs,
        Edit,
        Navigate,
        Run,
        ViewPanels,
        Explorer
    }

    /// <summary>
    /// A single documented keyboard shortcut.
    /// </summary>
    /// <param name="Keys">Display string, e.g. "F9 / Ctrl+B".</param>
    /// <param name="Category">Section the binding is rendered under.</param>
    /// <param name="Description">Human-readable description.</param>
    /// <param name="LiveAnnotation">
    /// Optional callback producing a live "now: …" suffix (used by F6 focus and
    /// F4 panel cycling) so the help reflects current editor state.
    /// </param>
    /// <param name="Essential">When true, included in the compact status-bar hint strip.</param>
    public sealed record KeyBinding(
        string Keys,
        KeyCategory Category,
        string Description,
        Func<EditorRenderer, string>? LiveAnnotation = null,
        bool Essential = false);

    /// <summary>
    /// Single source of truth for the keyboard shortcuts shown in the F1 help
    /// overlay. Help text is rendered from this catalog rather than hand-written
    /// alongside it, so the reference cannot silently drift from the bindings
    /// dispatched in <see cref="InputHandler"/>. <c>KeyBindingsTests</c> enforces
    /// that the bindings most prone to drift remain documented here.
    /// </summary>
    public static class KeyBindings
    {
        /// <summary>Section title for each category.</summary>
        public static readonly IReadOnlyDictionary<KeyCategory, string> CategoryTitles =
            new Dictionary<KeyCategory, string>
            {
                [KeyCategory.FileTabs]   = "File & Tabs",
                [KeyCategory.Edit]       = "Edit",
                [KeyCategory.Navigate]   = "Navigate",
                [KeyCategory.Run]        = "Run",
                [KeyCategory.ViewPanels] = "View & Panels",
                [KeyCategory.Explorer]   = "File Explorer",
            };

        /// <summary>
        /// Every documented shortcut, in display order within each category.
        /// Arrow glyphs are used for compactness in the card layout.
        /// </summary>
        public static readonly IReadOnlyList<KeyBinding> All = new[]
        {
            // ── File & Tabs ──
            new KeyBinding("Ctrl+S",            KeyCategory.FileTabs, "Save", Essential: true),
            new KeyBinding("Ctrl+Shift+S",      KeyCategory.FileTabs, "Save As"),
            new KeyBinding("Ctrl+O",            KeyCategory.FileTabs, "Open (file autocomplete)"),
            new KeyBinding("Ctrl+N",            KeyCategory.FileTabs, "New script"),
            new KeyBinding("Ctrl+T",            KeyCategory.FileTabs, "New tab"),
            new KeyBinding("Ctrl+W",            KeyCategory.FileTabs, "Close active tab"),
            new KeyBinding("Alt+← / →", KeyCategory.FileTabs, "Switch active tab"),
            new KeyBinding("Ctrl+P",            KeyCategory.FileTabs, "Export result set to CSV"),
            new KeyBinding("Ctrl+Q",            KeyCategory.FileTabs, "Exit editor"),
            new KeyBinding("Alt+P",             KeyCategory.FileTabs, "Command palette"),

            // ── Edit ──
            new KeyBinding("Ctrl+Z / Ctrl+Y",   KeyCategory.Edit, "Undo / Redo"),
            new KeyBinding("Ctrl+C / Ctrl+V",   KeyCategory.Edit, "Copy / Paste"),
            new KeyBinding("Ctrl+X",            KeyCategory.Edit, "Cut selection"),
            new KeyBinding("Ctrl+A",            KeyCategory.Edit, "Select all"),
            new KeyBinding("Ctrl+D / Ctrl+K",   KeyCategory.Edit, "Duplicate / Delete line"),
            new KeyBinding("Ctrl+/",            KeyCategory.Edit, "Toggle line comment"),
            new KeyBinding("Tab / Shift+Tab",   KeyCategory.Edit, "Indent / Outdent"),
            new KeyBinding("Ctrl+I / Alt+F / F12", KeyCategory.Edit, "Format SQL"),
            new KeyBinding("Ctrl+Space",        KeyCategory.Edit, "Autocomplete"),
            new KeyBinding("Alt+↑ / ↓", KeyCategory.Edit, "Add cursor above / below"),
            new KeyBinding("Escape",            KeyCategory.Edit, "Clear selection / cursors"),

            // ── Navigate ──
            new KeyBinding("Ctrl+F",            KeyCategory.Navigate, "Find (filters Results)"),
            new KeyBinding("Ctrl+H",            KeyCategory.Navigate, "Replace"),
            new KeyBinding("Ctrl+G",            KeyCategory.Navigate, "Go to line"),
            new KeyBinding("Ctrl+Home / End",   KeyCategory.Navigate, "Start / End of script"),
            new KeyBinding("Ctrl+← / →", KeyCategory.Navigate, "Jump word"),
            new KeyBinding("Ctrl+Shift+← / →", KeyCategory.Navigate, "Select word"),
            new KeyBinding("Shift+Arrows",      KeyCategory.Navigate, "Select text"),
            new KeyBinding("Ctrl+↑ / ↓", KeyCategory.Navigate, "Scroll panel (line)"),
            new KeyBinding("Ctrl+PgUp / PgDn",  KeyCategory.Navigate, "Scroll panel (page)"),

            // ── Run ──
            new KeyBinding("F5",                KeyCategory.Run, "Run entire script", Essential: true),
            new KeyBinding("Shift+F5",          KeyCategory.Run, "Run current statement"),
            new KeyBinding("Ctrl+F5",           KeyCategory.Run, "Run selected text"),
            new KeyBinding("Ctrl+R",            KeyCategory.Run, "Clear results & output"),
            new KeyBinding("Ctrl+Shift+R",      KeyCategory.Run, "Serve report in browser"),

            // ── View & Panels ──
            new KeyBinding("F6",                KeyCategory.ViewPanels, "Toggle focus", FocusAnnotation, Essential: true),
            new KeyBinding("F4",                KeyCategory.ViewPanels, "Cycle lower panel", PanelAnnotation, Essential: true),
            new KeyBinding("F3",                KeyCategory.ViewPanels, "Cycle theme", Essential: true),
            new KeyBinding("Ctrl+M",            KeyCategory.ViewPanels, "Maximize / restore panel"),
            new KeyBinding("F7 / F8",           KeyCategory.ViewPanels, "Compare mode / cycle pane / diagnostic"),
            new KeyBinding("F9 / Ctrl+B",       KeyCategory.ViewPanels, "Toggle file explorer", Essential: true),
            new KeyBinding("Alt+R",             KeyCategory.ViewPanels, "Toggle report preview", Essential: true),
            new KeyBinding("F1",                KeyCategory.ViewPanels, "Help (this screen)", Essential: true),
            new KeyBinding("Shift+F1 / Ctrl+L", KeyCategory.ViewPanels, "Help / lineage at cursor"),

            // ── File Explorer (sidebar focus) ──
            new KeyBinding("↑ / ↓",   KeyCategory.Explorer, "Move selection"),
            new KeyBinding("→ / Enter",    KeyCategory.Explorer, "Expand folder / open file"),
            new KeyBinding("←",            KeyCategory.Explorer, "Collapse folder"),
            new KeyBinding("Space",             KeyCategory.Explorer, "Toggle folder / open file"),
            new KeyBinding("Esc",               KeyCategory.Explorer, "Return focus to editor"),
        };

        /// <summary>Maximum rows a single help column may occupy and still fit the panel.</summary>
        /// <remarks>
        /// Panel height is capped at 32; minus 2 border rows = 30 inner rows, minus a
        /// separator row and the footer row leaves 28 for the tallest column.
        /// </remarks>
        public const int MaxHelpColumnRows = 28;

        private static string FocusAnnotation(EditorRenderer r) => r.Focus switch
        {
            EditorFocus.Editor        => "now: EDITOR",
            EditorFocus.Results       => "now: RESULTS",
            EditorFocus.Performance   => "now: PERF",
            EditorFocus.Messages      => "now: MESSAGES",
            EditorFocus.ExecutionTree => "now: PIPELINE",
            EditorFocus.Sidebar       => "now: EXPLORER",
            _                         => "now: EDITOR"
        };

        private static string PanelAnnotation(EditorRenderer r) =>
              r.PerformanceVisible ? "now: PERF"
            : r.ResultsVisible     ? "now: RESULTS"
            :                        "now: PIPELINE";

        /// <summary>Bindings under a single category, in catalog order.</summary>
        public static IEnumerable<KeyBinding> InCategory(KeyCategory category) =>
            All.Where(b => b.Category == category);

        /// <summary>Bindings flagged for the compact status-bar hint strip.</summary>
        public static IEnumerable<KeyBinding> Essentials => All.Where(b => b.Essential);

        /// <summary>Row count a category occupies in the card (entries + its title).</summary>
        public static int ColumnHeight(IEnumerable<KeyCategory> categories) =>
            categories.Sum(c => InCategory(c).Count() + 1);

        /// <summary>
        /// Distributes categories across <paramref name="columns"/> columns, greedily
        /// adding each (in enum order) to the currently shortest column to keep the
        /// card balanced. Deterministic, so the renderer and tests agree.
        /// </summary>
        public static List<List<KeyCategory>> HelpColumnLayout(int columns = 2)
        {
            var cols = new List<List<KeyCategory>>();
            var heights = new int[columns];
            for (int i = 0; i < columns; i++) cols.Add(new List<KeyCategory>());

            foreach (KeyCategory cat in Enum.GetValues(typeof(KeyCategory)))
            {
                int target = 0;
                for (int i = 1; i < columns; i++)
                    if (heights[i] < heights[target]) target = i;

                cols[target].Add(cat);
                heights[target] += InCategory(cat).Count() + 1;
            }

            return cols;
        }
    }
}
