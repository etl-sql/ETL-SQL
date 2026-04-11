using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.TUI.UI
{
    public class UndoManager
    {
        private readonly List<List<string>> _undoStack = new();
        private readonly List<List<string>> _redoStack = new();
        private const int MaxStackSize = 100;

        public void SaveState(List<string> lines)
        {
            _undoStack.Add(new List<string>(lines));
            _redoStack.Clear();
            if (_undoStack.Count > MaxStackSize) _undoStack.RemoveAt(0);
        }

        public List<string>? Undo(List<string> currentLines)
        {
            if (_undoStack.Count == 0) return null;

            _redoStack.Add(new List<string>(currentLines));
            var state = _undoStack.Last();
            _undoStack.RemoveAt(_undoStack.Count - 1);
            return state;
        }

        public List<string>? Redo(List<string> currentLines)
        {
            if (_redoStack.Count == 0) return null;

            _undoStack.Add(new List<string>(currentLines));
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
