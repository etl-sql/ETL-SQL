using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>In-editor find: next/prev navigation with wrap.</summary>
    public class FindTests
    {
        static FindTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        private static ConsoleEditor EditorWith(params string[] lines)
        {
            var e = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            e._renderer.Headless = true;
            e._buffer.Load(lines);
            return e;
        }

        [Fact]
        public void TryFindNext_AdvancesThroughMatches_ThenWraps()
        {
            var e = EditorWith("foo bar foo", "baz foo");
            e._buffer.CursorLine = 0; e._buffer.CursorColumn = 0; // on first foo

            Assert.True(e.TryFindNext("foo"));            // -> col 8 on line 0
            Assert.Equal((0, 8), (e._buffer.CursorLine, e._buffer.CursorColumn));

            Assert.True(e.TryFindNext("foo"));            // -> line 1 "baz foo"
            Assert.Equal((1, 4), (e._buffer.CursorLine, e._buffer.CursorColumn));

            Assert.True(e.TryFindNext("foo"));            // wraps -> line 0 col 0
            Assert.Equal((0, 0), (e._buffer.CursorLine, e._buffer.CursorColumn));
        }

        [Fact]
        public void TryFindPrev_GoesBackwards_ThenWraps()
        {
            var e = EditorWith("foo bar foo", "baz foo");
            e._buffer.CursorLine = 1; e._buffer.CursorColumn = 4; // on the line-1 foo

            Assert.True(e.TryFindPrev("foo"));            // -> line 0 col 8
            Assert.Equal((0, 8), (e._buffer.CursorLine, e._buffer.CursorColumn));

            Assert.True(e.TryFindPrev("foo"));            // -> line 0 col 0
            Assert.Equal((0, 0), (e._buffer.CursorLine, e._buffer.CursorColumn));

            Assert.True(e.TryFindPrev("foo"));            // wraps -> last match line 1 col 4
            Assert.Equal((1, 4), (e._buffer.CursorLine, e._buffer.CursorColumn));
        }

        [Fact]
        public void TryFind_IsCaseInsensitive()
        {
            var e = EditorWith("SELECT * FROM Foo");
            e._buffer.CursorLine = 0; e._buffer.CursorColumn = 0;
            Assert.True(e.TryFindNext("foo"));
            Assert.Equal(14, e._buffer.CursorColumn);
        }

        [Fact]
        public void TryFind_NoMatch_ReturnsFalse()
        {
            var e = EditorWith("SELECT 1");
            Assert.False(e.TryFindNext("zzz"));
            Assert.False(e.TryFindPrev("zzz"));
        }

        [Fact]
        public void ClearFind_RemovesTerm()
        {
            var e = EditorWith("foo");
            e._renderer.FindTerm = "foo";
            e.ClearFind();
            Assert.Null(e._renderer.FindTerm);
        }
    }
}
