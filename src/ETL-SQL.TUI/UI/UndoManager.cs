using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.TUI.UI
{
    /// <summary>A point-in-time editor state: the buffer lines plus the cursor position.</summary>
    public sealed record EditorSnapshot(List<string> Lines, int CursorLine, int CursorColumn);

    /// <summary>
    /// Undo/redo history of editor snapshots. Each entry captures both the text and the
    /// cursor position so an undo/redo restores the caret where the edit happened, not 0,0.
    /// </summary>
    public class UndoManager
    {
        private readonly List<EditorSnapshot> _undoStack = new();
        private readonly List<EditorSnapshot> _redoStack = new();
        private const int MaxStackSize = 100;

        public void SaveState(List<string> lines, int cursorLine, int cursorColumn)
        {
            _undoStack.Add(new EditorSnapshot(new List<string>(lines), cursorLine, cursorColumn));
            _redoStack.Clear();
            if (_undoStack.Count > MaxStackSize) _undoStack.RemoveAt(0);
        }

        public EditorSnapshot? Undo(List<string> currentLines, int cursorLine, int cursorColumn)
        {
            if (_undoStack.Count == 0) return null;

            _redoStack.Add(new EditorSnapshot(new List<string>(currentLines), cursorLine, cursorColumn));
            var state = _undoStack.Last();
            _undoStack.RemoveAt(_undoStack.Count - 1);
            return state;
        }

        public EditorSnapshot? Redo(List<string> currentLines, int cursorLine, int cursorColumn)
        {
            if (_redoStack.Count == 0) return null;

            _undoStack.Add(new EditorSnapshot(new List<string>(currentLines), cursorLine, cursorColumn));
            var state = _redoStack.Last();
            _redoStack.RemoveAt(_redoStack.Count - 1);
            return state;
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
