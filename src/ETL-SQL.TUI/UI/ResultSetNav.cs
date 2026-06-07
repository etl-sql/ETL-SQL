using System;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// The right-aligned "◀ i/N ▶" result-set navigator drawn on the bottom tab strip when
    /// the Results view holds more than one set. The geometry is shared by rendering and
    /// hit-testing so a click on an arrow lands where it was drawn.
    /// </summary>
    public static class ResultSetNav
    {
        public const int ArrowWidth = 3; // " ◀ " / " ▶ "

        public static string FormatLabel(int index, int count) => $" {index + 1}/{count} ";

        public static int Width(int index, int count) => ArrowWidth + FormatLabel(index, count).Length + ArrowWidth;

        public static int StartX(int totalWidth, int index, int count) =>
            Math.Max(0, totalWidth - Width(index, count));

        /// <summary>Returns -1 (prev) or +1 (next) when x hits an arrow, otherwise 0.</summary>
        public static int HitTest(int x, int totalWidth, int index, int count)
        {
            if (count <= 1) return 0;
            int start = StartX(totalWidth, index, count);
            if (x >= start && x < start + ArrowWidth) return -1;
            int nextStart = start + ArrowWidth + FormatLabel(index, count).Length;
            if (x >= nextStart && x < nextStart + ArrowWidth) return +1;
            return 0;
        }
    }
}
