using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.TUI.UI
{
    /// <summary>A palette entry: a title, an optional shortcut hint, and the action to run.</summary>
    public sealed record PaletteCommand(string Title, string? Shortcut, Func<ConsoleEditor, Task> Run);

    /// <summary>
    /// The curated command palette (Ctrl+Shift+P). Most entries reuse existing behavior by
    /// synthesizing their key through <see cref="ConsoleEditor.HandleKey"/>; report actions
    /// call dedicated methods. Filtering is a simple case-insensitive substring/subsequence
    /// match so typing narrows the list like VS Code.
    /// </summary>
    public static class CommandPalette
    {
        private static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false, bool alt = false, bool ctrl = false)
            => new('\0', key, shift, alt, ctrl);

        public static IReadOnlyList<PaletteCommand> Commands { get; } = new[]
        {
            // ── Report ──
            new PaletteCommand("Serve report in browser", "Ctrl+Shift+R", e => e.ServeInBrowser()),
            new PaletteCommand("Serve folder (all reports)", null, e => e.ServeFolderInBrowser()),
            new PaletteCommand("Export report to Markdown", null, e => e.ExportReportMarkdown()),
            new PaletteCommand("Export report to PDF", null, e => e.ExportReportPdf()),

            // ── Run / format ──
            new PaletteCommand("Run script", "F5", e => e.HandleKey(Key(ConsoleKey.F5))),
            new PaletteCommand("Run statement at cursor", "Shift+F5", e => e.HandleKey(Key(ConsoleKey.F5, shift: true))),
            new PaletteCommand("Format SQL", "Ctrl+I", e => e.HandleKey(Key(ConsoleKey.I, ctrl: true))),
            new PaletteCommand("Export results to CSV", "Ctrl+P", e => e.HandleKey(Key(ConsoleKey.P, ctrl: true))),

            // ── File / tabs ──
            new PaletteCommand("Save", "Ctrl+S", e => e.HandleKey(Key(ConsoleKey.S, ctrl: true))),
            new PaletteCommand("Save As", "Ctrl+Shift+S", e => e.HandleKey(Key(ConsoleKey.S, shift: true, ctrl: true))),
            new PaletteCommand("Open file", "Ctrl+O", e => e.HandleKey(Key(ConsoleKey.O, ctrl: true))),
            new PaletteCommand("New tab", "Ctrl+T", e => e.HandleKey(Key(ConsoleKey.T, ctrl: true))),

            // ── Navigate / edit ──
            new PaletteCommand("Find", "Ctrl+F", e => e.HandleKey(Key(ConsoleKey.F, ctrl: true))),
            new PaletteCommand("Replace", "Ctrl+H", e => e.HandleKey(Key(ConsoleKey.H, ctrl: true))),
            new PaletteCommand("Go to line", "Ctrl+G", e => e.HandleKey(Key(ConsoleKey.G, ctrl: true))),

            // ── View ──
            new PaletteCommand("Toggle file explorer", "F9", e => e.HandleKey(Key(ConsoleKey.F9))),
            new PaletteCommand("Toggle report preview", "Alt+R", e => e.HandleKey(Key(ConsoleKey.R, alt: true))),
            new PaletteCommand("Cycle theme", "F3", e => e.HandleKey(Key(ConsoleKey.F3))),
            new PaletteCommand("Cycle bottom panel", "F4", e => e.HandleKey(Key(ConsoleKey.F4))),

            // ── Help / lineage ──
            new PaletteCommand("Help", "F1", e => e.HandleKey(Key(ConsoleKey.F1))),
            new PaletteCommand("Help at cursor", "Shift+F1", e => e.HandleKey(Key(ConsoleKey.F1, shift: true))),
            new PaletteCommand("Lineage at cursor", "Ctrl+L", e => e.HandleKey(Key(ConsoleKey.L, ctrl: true))),
        };

        /// <summary>Commands matching the query, best matches first. Empty query returns all.</summary>
        public static List<PaletteCommand> Filter(string? query)
        {
            string q = (query ?? string.Empty).Trim().ToLowerInvariant();
            if (q.Length == 0) return Commands.ToList();

            return Commands
                .Select(c => (cmd: c, score: Score(c.Title.ToLowerInvariant(), q)))
                .Where(x => x.score >= 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.cmd.Title, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.cmd)
                .ToList();
        }

        private static int Score(string title, string q)
        {
            int idx = title.IndexOf(q, StringComparison.Ordinal);
            if (idx >= 0) return 1000 - idx; // substring match: prefer earlier position

            // subsequence match (characters in order)
            int ti = 0, qi = 0, matched = 0;
            while (ti < title.Length && qi < q.Length)
            {
                if (title[ti] == q[qi]) { qi++; matched++; }
                ti++;
            }
            return qi == q.Length ? matched : -1;
        }
    }
}
