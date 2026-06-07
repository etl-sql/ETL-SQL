using System;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Immutable screen geometry for the editor frame, computed once per frame/input so
    /// that rendering and mouse hit-testing share a single source of truth and cannot
    /// drift. All rows/columns are 0-based screen coordinates.
    /// </summary>
    public readonly struct EditorLayout
    {
        public int TotalWidth { get; }
        public int TotalHeight { get; }

        /// <summary>First row of the editor/sidebar band (below the header + tab bar).</summary>
        public int EditorAreaTop { get; }
        /// <summary>Rows reserved at the very bottom (help bar + status bar).</summary>
        public int StatusHeight { get; }
        /// <summary>Height of the bottom results/messages/pipeline/performance pane.</summary>
        public int LowerAreaHeight { get; }
        /// <summary>Height of the editor/sidebar band.</summary>
        public int EditorAreaHeight { get; }
        /// <summary>First row of the lower pane.</summary>
        public int LowerY { get; }

        public bool SidebarVisible { get; }
        /// <summary>Configured sidebar width when visible.</summary>
        public int SidebarWidth { get; }
        /// <summary>Effective sidebar width (0 when hidden).</summary>
        public int SidebarW { get; }
        /// <summary>Editor line-number gutter width.</summary>
        public int GutterWidth { get; }

        public EditorLayout(int totalWidth, int totalHeight, int editorAreaTop, int statusHeight,
            int lowerAreaHeight, int editorAreaHeight, bool sidebarVisible, int sidebarWidth, int gutterWidth)
        {
            TotalWidth = totalWidth;
            TotalHeight = totalHeight;
            EditorAreaTop = editorAreaTop;
            StatusHeight = statusHeight;
            LowerAreaHeight = lowerAreaHeight;
            EditorAreaHeight = editorAreaHeight;
            LowerY = editorAreaTop + editorAreaHeight;
            SidebarVisible = sidebarVisible;
            SidebarWidth = sidebarWidth;
            SidebarW = sidebarVisible ? sidebarWidth : 0;
            GutterWidth = gutterWidth;
        }

        // The sidebar is drawn as a bordered panel with an "Explorer" header, so its first
        // item sits one row below the band top and it shows two fewer rows than the band
        // height. The editor, by contrast, draws its first line at the band top (no border).
        public int SidebarContentTop => EditorAreaTop + 1;
        public int SidebarMaxVisibleItems => Math.Max(0, EditorAreaHeight - 2);

        public bool InEditorBand(int y) => y >= EditorAreaTop && y < EditorAreaTop + EditorAreaHeight;
        public bool InSidebar(int x, int y) => SidebarVisible && x >= 0 && x < SidebarWidth && InEditorBand(y);
        public bool InLowerPane(int y) => y >= LowerY && y < LowerY + LowerAreaHeight;

        // The lower pane reserves its first row for the clickable bottom tab strip; the
        // result/message/performance panels are drawn in the rows below it.
        public int BottomTabStripY => LowerY;
        public int LowerContentTop => LowerY + 1;
        public int LowerContentHeight => Math.Max(1, LowerAreaHeight - 1);
        public bool OnBottomTabStrip(int y) => y == BottomTabStripY;
        public bool InLowerContent(int y) => y >= LowerContentTop && y < LowerY + LowerAreaHeight;

        /// <summary>Flat tree index for a sidebar click, or -1 if the row is a border/empty line.</summary>
        public int SidebarItemIndexAt(int y, int scrollRow)
        {
            int row = y - SidebarContentTop;
            if (row < 0 || row >= SidebarMaxVisibleItems) return -1;
            return scrollRow + row;
        }

        /// <summary>Buffer line index for an editor click (unclamped — caller validates range).</summary>
        public int EditorLineAt(int y, int scrollLine) => (y - EditorAreaTop) + scrollLine;

        /// <summary>Buffer column for an editor click (unclamped — caller clamps to line length).</summary>
        public int EditorColumnAt(int x, int scrollCol) => (x - SidebarW - GutterWidth) + scrollCol;
    }

    /// <summary>
    /// Single source of truth for editor frame geometry. The arithmetic here was previously
    /// duplicated in <c>Render</c>, <c>ScrollRegion</c>, and <c>HandleMouseClick</c>; centralizing
    /// it keeps the drawn layout and the mouse hit-testing in lock-step.
    /// </summary>
    public static class LayoutCalculator
    {
        public const int EditorAreaTopRows = 2;       // header + tab bar
        public const int StatusRows = 2;              // help bar + status bar
        public const int DefaultLowerAreaHeight = 14; // 10 messages + rounded borders

        public static EditorLayout Compute(int totalWidth, int totalHeight, int bufferLineCount,
            bool sidebarVisible, int sidebarWidth, bool isBottomMaximized, bool compareMode)
        {
            int editorAreaTop = EditorAreaTopRows;
            int statusHeight = StatusRows;
            int reservedBottom = statusHeight;
            int available = totalHeight - editorAreaTop - reservedBottom;

            int lowerAreaHeight = DefaultLowerAreaHeight;
            if (isBottomMaximized || compareMode) lowerAreaHeight = Math.Max(5, available - 5);
            else if (lowerAreaHeight > available - 8) lowerAreaHeight = Math.Max(5, available - 8);

            int editorAreaHeight = Math.Max(3, totalHeight - lowerAreaHeight - reservedBottom - editorAreaTop);
            int gutterWidth = Math.Max(1, bufferLineCount).ToString().Length + 2;

            return new EditorLayout(totalWidth, totalHeight, editorAreaTop, statusHeight,
                lowerAreaHeight, editorAreaHeight, sidebarVisible, sidebarWidth, gutterWidth);
        }
    }
}
