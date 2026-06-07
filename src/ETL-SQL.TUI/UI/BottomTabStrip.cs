using System.Collections.Generic;

namespace ETL_SQL.TUI.UI
{
    /// <summary>The three bottom-pane views, mirroring the F4 cycle.</summary>
    public enum BottomTab { Pipeline, Results, Performance }

    /// <summary>
    /// The clickable tab strip drawn above the bottom pane. The same segment widths drive
    /// both rendering and hit-testing, so a click always lands on the tab that was drawn.
    /// Each cell is " Label " (one column of padding per side); cells are separated by a
    /// single column.
    /// </summary>
    public static class BottomTabStrip
    {
        public static readonly IReadOnlyList<(BottomTab Tab, string Label)> Tabs = new[]
        {
            (BottomTab.Pipeline, "Pipeline"),
            (BottomTab.Results, "Results"),
            (BottomTab.Performance, "Performance"),
        };

        /// <summary>Yields each tab's screen segment (start column and width) in render order.</summary>
        public static IEnumerable<(BottomTab Tab, string Label, int StartX, int Width)> Segments()
        {
            int x = 0;
            foreach (var (tab, label) in Tabs)
            {
                int width = label.Length + 2; // " Label "
                yield return (tab, label, x, width);
                x += width + 1; // trailing separator column
            }
        }

        /// <summary>Returns the tab whose segment contains <paramref name="x"/>, or null (gap/past end).</summary>
        public static BottomTab? HitTest(int x)
        {
            foreach (var seg in Segments())
                if (x >= seg.StartX && x < seg.StartX + seg.Width)
                    return seg.Tab;
            return null;
        }
    }
}
