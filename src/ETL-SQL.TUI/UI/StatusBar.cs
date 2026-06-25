using System;
using System.Collections.Generic;

namespace ETL_SQL.TUI.UI
{
    /// <summary>A clickable help-bar shortcut: a label plus the key it stands in for.</summary>
    public sealed record StatusButton(string Label, ConsoleKey Key, ConsoleModifiers Modifiers = 0, char KeyChar = '\0')
    {
        public ConsoleKeyInfo ToKeyInfo() => new ConsoleKeyInfo(
            KeyChar, Key,
            Modifiers.HasFlag(ConsoleModifiers.Shift),
            Modifiers.HasFlag(ConsoleModifiers.Alt),
            Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    /// <summary>
    /// The shortcut buttons on the help bar (second-to-last row). Labels and segment widths
    /// are shared by rendering and hit-testing; clicking a button synthesizes its key so the
    /// behavior in InputHandler is reused rather than duplicated.
    /// </summary>
    public static class StatusBar
    {
        public const int LeadGap = 1; // leading space before the first button
        public const int Gap = 2;     // columns between buttons

        public static readonly IReadOnlyList<StatusButton> Buttons = new[]
        {
            new StatusButton("F1:Help", ConsoleKey.F1),
            new StatusButton("F5:Run", ConsoleKey.F5),
            new StatusButton("F3:Theme", ConsoleKey.F3),
            new StatusButton("F6:Focus", ConsoleKey.F6),
            new StatusButton("F9:Explorer", ConsoleKey.F9),
            new StatusButton("F4:Panel", ConsoleKey.F4),
            new StatusButton("Alt+R:Report", ConsoleKey.R, ConsoleModifiers.Alt, 'r'),
            new StatusButton("F2:Save", ConsoleKey.F2),
            new StatusButton("^Q:Exit", ConsoleKey.Q, ConsoleModifiers.Control),
        };

        private static readonly IReadOnlyList<(StatusButton Button, int StartX, int Width)> CachedSegments = BuildSegments();
        private static readonly string CachedPlainText = BuildPlainText();

        public static IEnumerable<(StatusButton Button, int StartX, int Width)> Segments() => CachedSegments;

        public static StatusButton? HitTest(int x)
        {
            foreach (var seg in CachedSegments)
                if (x >= seg.StartX && x < seg.StartX + seg.Width) return seg.Button;
            return null;
        }

        /// <summary>Plain text of the bar: leading space + buttons joined by two spaces + trailing space.</summary>
        public static string PlainText() => CachedPlainText;

        private static IReadOnlyList<(StatusButton Button, int StartX, int Width)> BuildSegments()
        {
            var segments = new List<(StatusButton Button, int StartX, int Width)>(Buttons.Count);
            int x = LeadGap;
            foreach (var b in Buttons)
            {
                segments.Add((b, x, b.Label.Length));
                x += b.Label.Length + Gap;
            }

            return segments;
        }

        private static string BuildPlainText()
        {
            var labels = new string[Buttons.Count];
            for (int i = 0; i < Buttons.Count; i++)
                labels[i] = Buttons[i].Label;
            return " " + string.Join("  ", labels) + " ";
        }
    }
}
