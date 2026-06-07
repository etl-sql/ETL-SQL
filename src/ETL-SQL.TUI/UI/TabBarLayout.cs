using System.Collections.Generic;
using System.IO;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Shared geometry for the top tab bar so rendering and mouse hit-testing agree on each
    /// tab's width, its close-button column, and the "+" position. Each tab cell is
    /// " label " + "x" + " " (label = file name + dirty marker), and every tab is followed
    /// by a one-column separator before the next tab or the "+" button.
    /// </summary>
    public static class TabBarLayout
    {
        public const int PlusWidth = 3; // " + "

        public static string Label(string? filePath, bool isDirty)
            => (string.IsNullOrEmpty(filePath) ? "Untitled.etlsql" : Path.GetFileName(filePath)) + (isDirty ? "*" : "");

        public readonly record struct TabSegment(int Index, string Label, int StartX, int Width, int CloseX);

        /// <summary>Yields each tab's screen segment in render order. CloseX is the "x" column.</summary>
        public static IEnumerable<TabSegment> Tabs(IReadOnlyList<string> labels)
        {
            int x = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                int width = labels[i].Length + 4;       // " " + label + " " + "x" + " "
                int closeX = x + labels[i].Length + 2;  // the "x" column
                yield return new TabSegment(i, labels[i], x, width, closeX);
                x += width + 1;                          // one-column separator after every tab
            }
        }

        /// <summary>Start column of the "+" button (after the trailing separator).</summary>
        public static int PlusStartX(IReadOnlyList<string> labels)
        {
            int x = 0;
            foreach (var t in Tabs(labels)) x = t.StartX + t.Width + 1;
            return x;
        }
    }
}
