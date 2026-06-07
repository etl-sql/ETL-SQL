using Xunit;
using System.Collections.Generic;
using ETL_SQL.TUI.UI;
using ETL_SQL.Core;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Drag-to-select mapping: given a press anchor and a screen point, the selection spans
    /// the anchor to the mapped buffer position, clamped into the editor band and line.
    /// </summary>
    public class MouseDragTests
    {
        static MouseDragTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        private static ConsoleEditor EditorWith(params string[] lines)
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._buffer.Load(lines);
            editor._renderer.Render(editor, 80, 24); // establishes width/height + gutter (3 for 3 lines)
            return editor;
        }

        [Fact]
        public void DragExtendSelection_SpansAnchorToMappedPosition()
        {
            var editor = EditorWith("SELECT 1", "FROM orders", "WHERE x = 1");

            // Anchor at (0,0); drag to line 1, column 4 -> screen y=3 (editorAreaTop 2 + line 1), x=7 (gutter 3 + 4).
            editor._renderer.DragExtendSelection(7, 3, editor, 0, 0);

            Assert.True(editor._buffer.SelectionStartLine.HasValue);
            Assert.Equal(0, editor._buffer.SelectionStartLine!.Value);
            Assert.Equal(0, editor._buffer.SelectionStartCol!.Value);
            Assert.Equal(1, editor._buffer.CursorLine);
            Assert.Equal(4, editor._buffer.CursorColumn);
        }

        [Fact]
        public void DragExtendSelection_ClampsBelowAndPastLineEnd()
        {
            var editor = EditorWith("SELECT 1", "FROM orders", "WHERE x = 1");

            // Drag far below and far right -> clamp to last line, end of that line.
            editor._renderer.DragExtendSelection(500, 100, editor, 0, 2);

            Assert.Equal(2, editor._buffer.CursorLine);                       // last line
            Assert.Equal("WHERE x = 1".Length, editor._buffer.CursorColumn);  // clamped to line length
            Assert.Equal(0, editor._buffer.SelectionStartLine!.Value);
            Assert.Equal(2, editor._buffer.SelectionStartCol!.Value);
        }

        [Fact]
        public void DragExtendSelection_IntoGutterClampsToColumnZero()
        {
            var editor = EditorWith("SELECT 1", "FROM orders", "WHERE x = 1");

            editor._renderer.DragExtendSelection(0, 3, editor, 0, 5); // x in the gutter
            Assert.Equal(1, editor._buffer.CursorLine);
            Assert.Equal(0, editor._buffer.CursorColumn);
        }
    }
}
