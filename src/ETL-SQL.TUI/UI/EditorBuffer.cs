using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ETL_SQL.TUI.UI
{
    public class EditorBuffer
    {
        public List<string> Lines { get; private set; } = new() { "" };
        public int CursorLine { get; set; } = 0;
        public int CursorColumn { get; set; } = 0;
        public List<(int Line, int Col)> SecondaryCursors { get; } = new();
        public int? SelectionStartLine { get; set; }
        public int? SelectionStartCol { get; set; }
        public bool IsMultiLineMode { get; set; }

        public void Load(IEnumerable<string> lines)
        {
            Lines = lines.ToList();
            if (Lines.Count == 0) Lines.Add("");
            CursorLine = 0;
            CursorColumn = 0;
            SecondaryCursors.Clear();
            SelectionStartLine = null;
            SelectionStartCol = null;
            IsMultiLineMode = false;
        }

        public void InsertChar(char ch)
        {
            // Overtype check for closing characters
            if (CursorColumn < Lines[CursorLine].Length && Lines[CursorLine][CursorColumn] == ch)
            {
                if (ch == ')' || ch == ']' || ch == '"' || ch == '\'')
                {
                    CursorColumn++;
                    return;
                }
            }

            char? close = ch switch {
                '[' => ']',
                '(' => ')',
                '"' => '"',
                '\'' => '\'',
                _ => null
            };

            string toInsert = close.HasValue ? $"{ch}{close.Value}" : ch.ToString();
            
            if (IsMultiLineMode)
            {
                foreach (var c in SecondaryCursors)
                {
                    if (c.Line < 0 || c.Line >= Lines.Count || c.Line == CursorLine) continue;
                    int col = Math.Max(0, Math.Min(c.Col, Lines[c.Line].Length));
                    Lines[c.Line] = Lines[c.Line].Insert(col, toInsert);
                }
                // Update secondary cursors' columns
                for (int i = 0; i < SecondaryCursors.Count; i++)
                    SecondaryCursors[i] = (SecondaryCursors[i].Line, SecondaryCursors[i].Col + 1);
            }

            Lines[CursorLine] = Lines[CursorLine].Insert(CursorColumn, toInsert);
            CursorColumn++;
        }

        public void Backspace()
        {
            if (SelectionStartLine.HasValue) { DeleteSelection(); return; }

            if (IsMultiLineMode)
            {
                foreach (var c in SecondaryCursors.OrderByDescending(x => x.Line))
                {
                    if (c.Col > 0) Lines[c.Line] = Lines[c.Line].Remove(c.Col - 1, 1);
                }
            }

            if (CursorColumn > 0)
            {
                // Pair deletion
                if (CursorColumn < Lines[CursorLine].Length)
                {
                    char curChar = Lines[CursorLine][CursorColumn - 1];
                    char nextChar = Lines[CursorLine][CursorColumn];
                    if ((curChar == '[' && nextChar == ']') || (curChar == '(' && nextChar == ')') ||
                        (curChar == '"' && nextChar == '"') || (curChar == '\'' && nextChar == '\''))
                    {
                        Lines[CursorLine] = Lines[CursorLine].Remove(CursorColumn - 1, 2);
                        CursorColumn--;
                        return;
                    }
                }

                Lines[CursorLine] = Lines[CursorLine].Remove(CursorColumn - 1, 1);
                CursorColumn--;
                if (IsMultiLineMode)
                {
                    for (int i = 0; i < SecondaryCursors.Count; i++)
                        SecondaryCursors[i] = (SecondaryCursors[i].Line, Math.Max(0, SecondaryCursors[i].Col - 1));
                }
            }
            else if (CursorLine > 0 && !IsMultiLineMode)
            {
                var currentLineContent = Lines[CursorLine];
                Lines.RemoveAt(CursorLine);
                CursorLine--;
                CursorColumn = Lines[CursorLine].Length;
                Lines[CursorLine] += currentLineContent;
            }
        }

        public void Delete()
        {
            if (SelectionStartLine.HasValue) { DeleteSelection(); return; }

            if (CursorColumn < Lines[CursorLine].Length)
            {
                Lines[CursorLine] = Lines[CursorLine].Remove(CursorColumn, 1);
            }
            else if (CursorLine < Lines.Count - 1)
            {
                var nextLine = Lines[CursorLine + 1];
                Lines.RemoveAt(CursorLine + 1);
                Lines[CursorLine] += nextLine;
            }
        }

        public void NewLine()
        {
            if (SelectionStartLine.HasValue) DeleteSelection();
            var remaining = Lines[CursorLine].Substring(CursorColumn);
            Lines[CursorLine] = Lines[CursorLine].Substring(0, CursorColumn);
            Lines.Insert(CursorLine + 1, remaining);
            CursorLine++;
            CursorColumn = 0;
        }

        public void Tab(bool reverse = false)
        {
            if (reverse)
            {
                if (Lines[CursorLine].StartsWith("    "))
                {
                    Lines[CursorLine] = Lines[CursorLine].Substring(4);
                    CursorColumn = Math.Max(0, CursorColumn - 4);
                }
            }
            else
            {
                Lines[CursorLine] = Lines[CursorLine].Insert(CursorColumn, "    ");
                CursorColumn += 4;
            }
        }

        public void Home() => CursorColumn = 0;
        public void End() => CursorColumn = Lines[CursorLine].Length;

        public void AddMultiCursor(int dy)
        {
            int targetLine = CursorLine + dy;
            if (SecondaryCursors.Count > 0 && SecondaryCursors.Last().Line == targetLine)
            {
                SecondaryCursors.RemoveAt(SecondaryCursors.Count - 1);
            }
            else
            {
                if (!SecondaryCursors.Any(c => c.Line == CursorLine))
                {
                    SecondaryCursors.Add((CursorLine, CursorColumn));
                }
            }
            IsMultiLineMode = SecondaryCursors.Any();
            CursorLine = targetLine;
            CursorColumn = Math.Min(CursorColumn, Lines[CursorLine].Length);
        }

        public void ClearMultiCursors()
        {
            SecondaryCursors.Clear();
            IsMultiLineMode = false;
        }

        public (int startL, int startC, int endL, int endC) GetSelectionBounds()
        {
            if (!SelectionStartLine.HasValue) return (0, 0, 0, 0);
            int sL = SelectionStartLine.Value;
            int sC = SelectionStartCol ?? 0;
            int eL = CursorLine;
            int eC = CursorColumn;

            if (sL < eL || (sL == eL && sC < eC)) return (sL, sC, eL, eC);
            return (eL, eC, sL, sC);
        }

        public void DeleteSelection()
        {
            if (!SelectionStartLine.HasValue) return;
            var (startL, startC, endL, endC) = GetSelectionBounds();

            if (startL == endL)
            {
                Lines[startL] = Lines[startL].Remove(startC, endC - startC);
            }
            else
            {
                string firstPart = Lines[startL].Substring(0, startC);
                string lastPart = Lines[endL].Substring(endC);
                Lines[startL] = firstPart + lastPart;
                for (int i = 0; i < endL - startL; i++) Lines.RemoveAt(startL + 1);
            }

            CursorLine = startL;
            CursorColumn = startC;
            SelectionStartLine = null;
            SelectionStartCol = null;
        }

        public void SelectAll()
        {
            SelectionStartLine = 0;
            SelectionStartCol = 0;
            CursorLine = Lines.Count - 1;
            CursorColumn = Lines[CursorLine].Length;
        }

        public int GetFlatPosition(int line, int col)
        {
            int flat = 0;
            for (int i = 0; i < line; i++) flat += Lines[i].Length + 1; // +1 for newline
            return flat + col;
        }

        public (int line, int col) GetLineColFromFlat(int flat)
        {
            int current = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (flat <= current + Lines[i].Length) return (i, flat - current);
                current += Lines[i].Length + 1;
            }
            return (Lines.Count - 1, Lines.Last().Length);
        }

        public string GetText() => string.Join("\n", Lines);

        public string? GetSelectedText()
        {
            if (!SelectionStartLine.HasValue) return null;
            var (startL, startC, endL, endC) = GetSelectionBounds();
            
            if (startL == endL)
            {
                return Lines[startL].Substring(startC, endC - startC);
            }
            
            var sb = new StringBuilder();
            sb.AppendLine(Lines[startL].Substring(startC));
            for (int i = startL + 1; i < endL; i++)
            {
                sb.AppendLine(Lines[i]);
            }
            sb.Append(Lines[endL].Substring(0, endC));
            return sb.ToString();
        }

        public void Paste(string text)
        {
            if (SelectionStartLine.HasValue) DeleteSelection();

            var pasteLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            if (pasteLines.Length == 1)
            {
                Lines[CursorLine] = Lines[CursorLine].Insert(CursorColumn, pasteLines[0]);
                CursorColumn += pasteLines[0].Length;
            }
            else
            {
                var suffix = Lines[CursorLine].Substring(CursorColumn);
                Lines[CursorLine] = Lines[CursorLine].Substring(0, CursorColumn) + pasteLines[1 - 1]; // Use index 0
                for (int i = 1; i < pasteLines.Length - 1; i++)
                {
                    Lines.Insert(CursorLine + i, pasteLines[i]);
                }
                Lines.Insert(CursorLine + pasteLines.Length - 1, pasteLines.Last() + suffix);
                CursorLine += pasteLines.Length - 1;
                CursorColumn = pasteLines.Last().Length;
            }
        }

        public void DeleteLine()
        {
            if (Lines.Count > 1)
            {
                Lines.RemoveAt(CursorLine);
                CursorLine = Math.Min(CursorLine, Lines.Count - 1);
                CursorColumn = Math.Min(CursorColumn, Lines[CursorLine].Length);
            }
            else
            {
                Lines[0] = "";
                CursorColumn = 0;
            }
        }

        public void DuplicateLine()
        {
            Lines.Insert(CursorLine + 1, Lines[CursorLine]);
            CursorLine++;
        }

        public void Top() { CursorLine = 0; CursorColumn = 0; }
        public void Bottom() { CursorLine = Lines.Count - 1; CursorColumn = Lines[CursorLine].Length; }

        public void IndentSelection(bool reverse)
        {
            if (!SelectionStartLine.HasValue) { Tab(reverse); return; }

            var (startL, _, endL, endC) = GetSelectionBounds();
            int lastLine = (endC == 0 && endL > startL) ? endL - 1 : endL;

            for (int i = startL; i <= lastLine; i++)
            {
                if (reverse)
                {
                    int remove = Lines[i].StartsWith("    ") ? 4
                               : Lines[i].StartsWith("   ")  ? 3
                               : Lines[i].StartsWith("  ")   ? 2
                               : Lines[i].StartsWith(" ")    ? 1 : 0;
                    if (remove > 0) Lines[i] = Lines[i].Substring(remove);
                    if (i == CursorLine) CursorColumn = Math.Max(0, CursorColumn - remove);
                }
                else
                {
                    Lines[i] = "    " + Lines[i];
                    if (i == CursorLine) CursorColumn += 4;
                }
            }
        }

        public void ToggleLineComment()
        {
            int startL, endL;
            if (SelectionStartLine.HasValue)
            {
                var (sl, _, el, ec) = GetSelectionBounds();
                startL = sl;
                endL = (ec == 0 && el > sl) ? el - 1 : el;
            }
            else { startL = endL = CursorLine; }

            bool allCommented = true;
            for (int i = startL; i <= endL; i++)
            {
                if (Lines[i].Trim().Length == 0) continue;
                if (!Lines[i].TrimStart().StartsWith("--")) { allCommented = false; break; }
            }

            for (int i = startL; i <= endL; i++)
            {
                if (allCommented)
                {
                    int idx = Lines[i].IndexOf("--", StringComparison.Ordinal);
                    if (idx < 0) continue;
                    int remove = (idx + 2 < Lines[i].Length && Lines[i][idx + 2] == ' ') ? 3 : 2;
                    Lines[i] = Lines[i].Remove(idx, remove);
                    if (i == CursorLine) CursorColumn = Math.Max(0, CursorColumn - remove);
                }
                else
                {
                    Lines[i] = "-- " + Lines[i];
                    if (i == CursorLine) CursorColumn += 3;
                }
            }
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        public void WordRight()
        {
            var line = Lines[CursorLine];
            int col = CursorColumn;
            if (col >= line.Length)
            {
                if (CursorLine < Lines.Count - 1) { CursorLine++; CursorColumn = 0; }
                return;
            }
            if (IsWordChar(line[col])) { while (col < line.Length && IsWordChar(line[col])) col++; }
            else                       { while (col < line.Length && !IsWordChar(line[col])) col++; }
            CursorColumn = col;
        }

        public void WordLeft()
        {
            int col = CursorColumn;
            if (col == 0)
            {
                if (CursorLine > 0) { CursorLine--; CursorColumn = Lines[CursorLine].Length; }
                return;
            }
            var line = Lines[CursorLine];
            col--;
            while (col > 0 && !IsWordChar(line[col])) col--;
            while (col > 0 && IsWordChar(line[col - 1])) col--;
            CursorColumn = col;
        }
    }
}
