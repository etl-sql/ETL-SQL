using System.Linq;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Multi-cursor (Alt+↑/↓) editing broadcast across secondary carets.</summary>
    public class MultiCursorTests
    {
        private static EditorBuffer Buffer(params string[] lines)
        {
            var b = new EditorBuffer();
            b.Load(lines);
            return b;
        }

        // Stack carets down from line 0 onto the given number of following lines.
        private static EditorBuffer WithCarets(int extra, params string[] lines)
        {
            var b = Buffer(lines);
            b.CursorLine = 0; b.CursorColumn = 0;
            for (int i = 0; i < extra; i++) b.AddMultiCursor(1);
            return b;
        }

        [Fact]
        public void InsertChar_BroadcastsToSecondaryCarets()
        {
            var b = WithCarets(2, "aaa", "bbb", "ccc");
            b.InsertChar('X');

            Assert.Equal(new[] { "Xaaa", "Xbbb", "Xccc" }, b.Lines);
        }

        [Fact]
        public void InsertChar_RespectsPerLineColumn_OnDifferentLengthLines()
        {
            var b = Buffer("a", "bbbb");
            b.CursorLine = 0; b.CursorColumn = 1; // end of "a"
            b.AddMultiCursor(1);                  // secondary (0,1); primary at (1, min(1,4)=1)

            b.InsertChar('X');

            Assert.Equal("aX", b.Lines[0]);
            Assert.Equal("bXbbb", b.Lines[1]);
        }

        [Fact]
        public void Backspace_BroadcastsToSecondaryCarets()
        {
            var b = Buffer("aaa", "bbb");
            b.CursorLine = 0; b.CursorColumn = 2;
            b.AddMultiCursor(1); // secondary (0,2); primary (1,2)

            b.Backspace();

            Assert.Equal("aa", b.Lines[0]);
            Assert.Equal("bb", b.Lines[1]);
        }

        [Fact]
        public void Delete_BroadcastsToSecondaryCarets()
        {
            var b = Buffer("aaa", "bbb");
            b.CursorLine = 0; b.CursorColumn = 1;
            b.AddMultiCursor(1); // secondary (0,1); primary (1,1)

            b.Delete();

            Assert.Equal("aa", b.Lines[0]);
            Assert.Equal("bb", b.Lines[1]);
        }

        [Fact]
        public void Delete_DoesNotJoinLinesWhileMultiCursor()
        {
            var b = Buffer("ab", "cd");
            b.CursorLine = 0; b.CursorColumn = 2; // at end of line 0
            b.AddMultiCursor(1);

            b.Delete(); // would join lines if single-caret; must be a no-op join here

            Assert.Equal(2, b.Lines.Count);
            Assert.Equal("ab", b.Lines[0]);
        }

        [Fact]
        public void InsertChar_DoesNotOvertypeClosingBracketInMultiCursor()
        {
            // Primary sits just before ')'. Single-caret would overtype; multi-caret must insert.
            var b = Buffer("()", "()");
            b.CursorLine = 0; b.CursorColumn = 1;
            b.AddMultiCursor(1); // secondary (0,1); primary (1,1)

            b.InsertChar(')');

            Assert.Equal("())", b.Lines[0]);
            Assert.Equal("())", b.Lines[1]);
        }

        [Fact]
        public void NewLine_CollapsesMultiCursor()
        {
            var b = WithCarets(1, "aa", "bb");
            Assert.True(b.IsMultiLineMode);

            b.NewLine();

            Assert.False(b.IsMultiLineMode);
            Assert.Empty(b.SecondaryCursors);
        }

        [Fact]
        public void Paste_CollapsesMultiCursor()
        {
            var b = WithCarets(1, "aa", "bb");
            b.Paste("Z");

            Assert.False(b.IsMultiLineMode);
            Assert.Empty(b.SecondaryCursors);
        }

        [Fact]
        public void AddMultiCursor_TogglesOffWhenReversing()
        {
            var b = Buffer("a", "b", "c");
            b.CursorLine = 0;
            b.AddMultiCursor(1); // add caret on line 0, primary -> 1
            Assert.Single(b.SecondaryCursors);

            // The last secondary is on line 0; moving back up to line 0 removes it.
            b.AddMultiCursor(-1);
            Assert.Empty(b.SecondaryCursors);
            Assert.False(b.IsMultiLineMode);
        }
    }
}
