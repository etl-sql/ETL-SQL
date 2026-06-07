using System;
using System.Collections.Generic;
using System.Linq;

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

        public static IEnumerable<(StatusButton Button, int StartX, int Width)> Segments()
        {
            int x = LeadGap;
            foreach (var b in Buttons)
            {
                yield return (b, x, b.Label.Length);
                x += b.Label.Length + Gap;
            }
        }

        public static StatusButton? HitTest(int x)
        {
            foreach (var seg in Segments())
                if (x >= seg.StartX && x < seg.StartX + seg.Width) return seg.Button;
            return null;
        }

        /// <summary>Plain text of the bar: leading space + buttons joined by two spaces + trailing space.</summary>
        public static string PlainText() => " " + string.Join("  ", Buttons.Select(b => b.Label)) + " ";
    }
}
