using Xunit;
using System.Collections.Generic;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Undo/redo history including cursor-position restore.</summary>
    public class UndoManagerTests
    {
        private static List<string> Lines(params string[] l) => new(l);

        [Fact]
        public void Undo_RestoresTextAndCursor()
        {
            var undo = new UndoManager();
            // Pre-edit state: caret at line 2, col 3.
            undo.SaveState(Lines("SELECT 1", "FROM t"), 1, 3);

            // After an edit the buffer/caret moved; undo should return the saved snapshot.
            var snap = undo.Undo(Lines("SELECT 1", "FROM t2", "WHERE x"), 2, 0);

            Assert.NotNull(snap);
            Assert.Equal(new[] { "SELECT 1", "FROM t" }, snap!.Lines);
            Assert.Equal(1, snap.CursorLine);
            Assert.Equal(3, snap.CursorColumn);
        }

        [Fact]
        public void Redo_RestoresTheUndoneStateAndCursor()
        {
            var undo = new UndoManager();
            undo.SaveState(Lines("a"), 0, 0);

            // Undo pushes the current (post-edit) state+caret onto the redo stack.
            undo.Undo(Lines("a", "b"), 1, 1);
            var redo = undo.Redo(Lines("a"), 0, 0);

            Assert.NotNull(redo);
            Assert.Equal(new[] { "a", "b" }, redo!.Lines);
            Assert.Equal(1, redo.CursorLine);
            Assert.Equal(1, redo.CursorColumn);
        }

        [Fact]
        public void Undo_EmptyStack_ReturnsNull()
        {
            Assert.Null(new UndoManager().Undo(Lines("x"), 0, 0));
        }

        [Fact]
        public void SaveState_ClearsRedoStack()
        {
            var undo = new UndoManager();
            undo.SaveState(Lines("a"), 0, 0);
            undo.Undo(Lines("a", "b"), 1, 0);   // populates redo
            undo.SaveState(Lines("a", "b"), 1, 0); // a new edit must invalidate redo

            Assert.Null(undo.Redo(Lines("a", "b"), 1, 0));
        }

        [Fact]
        public void Snapshot_IsDecoupledFromCallerList()
        {
            var undo = new UndoManager();
            var live = Lines("one");
            undo.SaveState(live, 0, 0);

            live.Add("two"); // mutating the original must not change the stored snapshot

            var snap = undo.Undo(Lines("changed"), 0, 0);
            Assert.Equal(new[] { "one" }, snap!.Lines);
        }
    }
}
