using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Governs the autocomplete lifecycle within the <see cref="ConsoleEditor"/>.
    /// Handles suggestion triggering, navigation, and acceptance.
    /// </summary>
    public class AutocompleteController
    {
        private readonly EditorBuffer _buffer;
        private readonly EditorRenderer _renderer;
        private readonly MetadataManager _metadata;
        private readonly Dictionary<string, IDataSource> _connections;
        private readonly ETL_SQL.Core.Interfaces.ILanguageHelpRegistry? _helpRegistry;
        private readonly ILogger _logger;

        /// <summary>Initializes a new instance of the <see cref="AutocompleteController"/> class.</summary>
        /// <param name="buffer">The editor text buffer.</param>
        /// <param name="renderer">The console renderer.</param>
        /// <param name="metadata">The metadata manager for connection caching.</param>
        /// <param name="connections">The active data source connections.</param>
        public AutocompleteController(EditorBuffer buffer, EditorRenderer renderer, MetadataManager metadata, Dictionary<string, IDataSource> connections, ILogger logger, Core.Interfaces.ILanguageHelpRegistry? helpRegistry = null)
        {
            _buffer = buffer;
            _renderer = renderer;
            _metadata = metadata;
            _connections = connections;
            _logger = logger;
            _helpRegistry = helpRegistry;
        }

        private System.Threading.CancellationTokenSource? _updateCts;
        private int _updateGen;

        /// <summary>Triggers a debounced, non-blocking autocomplete update on a background task.</summary>
        public void TriggerUpdate()
        {
            _updateCts?.Cancel();
            _updateCts = new System.Threading.CancellationTokenSource();
            var ct = _updateCts.Token;
            int gen = ++_updateGen;

            _renderer.AutocompletePending = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(50, ct);
                    await UpdateAsync(ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.Debug($"Autocomplete update error: {ex.Message}");
                }
                finally
                {
                    if (gen == _updateGen)
                    {
                        _renderer.AutocompletePending = false;
                    }
                }
            }, ct);
        }

        /// <summary>Updates the suggestion list based on the current cursor position and prefix.</summary>
        public async Task UpdateAsync(System.Threading.CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _metadata.RefreshConnections(_buffer.GetText());
            ct.ThrowIfCancellationRequested();
            var line = _buffer.Lines[_buffer.CursorLine].Substring(0, _buffer.CursorColumn);
            var lastWordMatch = Regex.Match(line, @"[\$\w.#@./\\\""']*$");
            var lastWord = lastWordMatch.Value.Trim('\'', '\"');

            if (string.IsNullOrEmpty(lastWord) && !line.EndsWith("=") && !line.EndsWith("("))
            {
                _renderer.AutocompleteVisible = false;
                return;
            }

            var suggestions = await ETLSuggestEngine.GetSuggestionsAsync(lastWord, _buffer.GetText(), _connections, _metadata, _logger, _helpRegistry);
            ct.ThrowIfCancellationRequested();

            if (lastWord.StartsWith('$') && line.TrimStart() == lastWord)
            {
                var snippetSuggestions = SnippetLibrary.Instance
                    .GetByPrefix(lastWord)
                    .Select(s => new Suggestion(s.TuiBody, SuggestionType.Snippet, 1, s.Description, Label: s.Trigger))
                    .ToList();
                suggestions.InsertRange(0, snippetSuggestions);
            }

            _renderer.AutocompleteOptions = suggestions;
            sw.Stop();

            if (_renderer.AutocompleteOptions.Any())
            {
                _renderer.AutocompleteVisible = true;
                _renderer.AutocompleteIndex = 0;
                _renderer.ShowStatus($"Suggestions: {_renderer.AutocompleteOptions.Count} found ({sw.ElapsedMilliseconds}ms)");
            }
            else
            {
                _renderer.AutocompleteVisible = false;
            }
        }

        /// <summary>Handles keyboard input specifically for the autocomplete overlay.</summary>
        /// <param name="key">The key pressed.</param>
        /// <returns>True if the key was handled by autocomplete; otherwise, false.</returns>
        public bool HandleKey(ConsoleKeyInfo key)
        {
            if (!_renderer.AutocompleteVisible) return false;

            if (key.Key == ConsoleKey.Tab || key.Key == ConsoleKey.Enter)
            {
                Accept();
                return true;
            }
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (_renderer.AutocompleteIndex > 0)
                {
                    _renderer.AutocompleteIndex--;
                    return true;
                }
                _renderer.AutocompleteVisible = false;
                return true;
            }
            if (key.Key == ConsoleKey.DownArrow)
            {
                if (_renderer.AutocompleteIndex < _renderer.AutocompleteOptions.Count - 1)
                {
                    _renderer.AutocompleteIndex++;
                    return true;
                }
                _renderer.AutocompleteVisible = false;
                return true;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                _renderer.AutocompleteVisible = false;
                return true;
            }

            return false;
        }

        /// <summary>Accepts the currently selected suggestion and inserts it into the buffer.</summary>
        public void Accept()
        {
            if (!_renderer.AutocompleteVisible || !_renderer.AutocompleteOptions.Any()) return;

            var choice = _renderer.AutocompleteOptions[_renderer.AutocompleteIndex].Text;

            var line = _buffer.Lines[_buffer.CursorLine];
            var lastWordMatch = Regex.Match(line.Substring(0, _buffer.CursorColumn), @"[\$\w.#@./\\\""']*$");
            var matchValue = lastWordMatch.Value;
            var startPos = _buffer.CursorColumn - matchValue.Length;

            // Handle quoted matches
            if (matchValue.Length > 0 && (matchValue[0] == '\'' || matchValue[0] == '\"') && !choice.StartsWith("'") && !choice.StartsWith("\""))
            {
                startPos++;
                matchValue = matchValue.Substring(1);
            }

            if (choice.Contains('\n'))
            {
                // Multi-line snippet: split into lines, expand buffer, position cursor at end
                var choiceLines = choice.Split('\n');
                var beforeSnippet = line.Substring(0, startPos);
                var afterSnippet = line.Substring(startPos + matchValue.Length);

                _buffer.Lines[_buffer.CursorLine] = beforeSnippet + choiceLines[0];
                for (int i = 1; i < choiceLines.Length; i++)
                {
                    var insertLine = i == choiceLines.Length - 1
                        ? choiceLines[i] + afterSnippet
                        : choiceLines[i];
                    _buffer.Lines.Insert(_buffer.CursorLine + i, insertLine);
                }
                _buffer.CursorLine += choiceLines.Length - 1;
                _buffer.CursorColumn = choiceLines[^1].Length;
            }
            else
            {
                _buffer.Lines[_buffer.CursorLine] = line.Remove(startPos, matchValue.Length).Insert(startPos, choice);
                _buffer.CursorColumn = startPos + choice.Length;
            }
            _renderer.AutocompleteVisible = false;

            // Activate snippet tab-stop navigation if placeholders exist
            var firstPlaceholder = FindNextPlaceholder(_buffer, 0, 0);
            if (firstPlaceholder.HasValue)
            {
                _buffer.SelectRange(firstPlaceholder.Value.Line, firstPlaceholder.Value.StartCol, firstPlaceholder.Value.EndCol);
                _renderer.SnippetModeActive = true;
                _renderer.ShowStatus("Snippet mode — Tab: next · Shift+Tab: prev · Esc: exit");
            }
        }

        /// <summary>
        /// The single-line label shown for a suggestion in the popup list. Multi-line snippet
        /// bodies are collapsed to their first line with whitespace squeezed, then truncated to
        /// <paramref name="width"/> (with an ellipsis) so the list can't wrap or overrun the
        /// documentation sidecar.
        /// </summary>
        public static string ToDisplayLabel(string text, int width)
        {
            if (width <= 0) return string.Empty;
            text ??= string.Empty;
            int nl = text.IndexOfAny(new[] { '\n', '\r' });
            string firstLine = nl >= 0 ? text.Substring(0, nl) : text;
            firstLine = Regex.Replace(firstLine, @"\s+", " ").Trim();
            if (firstLine.Length > width)
                firstLine = width == 1 ? firstLine.Substring(0, 1) : firstLine.Substring(0, width - 1) + "…";
            return firstLine;
        }

        /// <summary>Scans forward from (fromLine, fromCol) for the next «placeholder» marker.</summary>
        public static (int Line, int StartCol, int EndCol)? FindNextPlaceholder(EditorBuffer buffer, int fromLine, int fromCol)
        {
            for (int l = fromLine; l < buffer.Lines.Count; l++)
            {
                var ln = buffer.Lines[l];
                int searchStart = (l == fromLine) ? fromCol : 0;
                int open = ln.IndexOf('«', searchStart);
                if (open < 0) continue;
                int close = ln.IndexOf('»', open + 1);
                if (close < 0) continue;
                return (l, open, close + 1);
            }
            return null;
        }

        /// <summary>Scans backward from (fromLine, fromCol) for the previous «placeholder» marker.</summary>
        public static (int Line, int StartCol, int EndCol)? FindPrevPlaceholder(EditorBuffer buffer, int fromLine, int fromCol)
        {
            for (int l = fromLine; l >= 0; l--)
            {
                var ln = buffer.Lines[l];
                int endSearchAt = (l == fromLine) ? fromCol - 1 : ln.Length - 1;
                if (endSearchAt < 0) continue;
                int close = ln.LastIndexOf('»', endSearchAt);
                if (close < 0) continue;
                int open = close > 0 ? ln.LastIndexOf('«', close - 1) : -1;
                if (open < 0) continue;
                return (l, open, close + 1);
            }
            return null;
        }

        /// <summary>Attempts to provide specialized suggestions, such as expansion of '*'.</summary>
        public async Task TrySuggestAsync()
        {
            var text = _buffer.GetText();
            _metadata.RefreshConnections(text, force: true);
            var line = _buffer.Lines[_buffer.CursorLine];
            // Allow the caret to sit on the '*' as well as just after it.
            int caret = _buffer.CursorColumn;
            if (caret < line.Length && line[caret] == '*') caret++;
            var currentLinePrefix = line.Substring(0, Math.Min(caret, line.Length));

            // Matches ' *' or 'alias.*' (case insensitive)
            var starMatch = Regex.Match(currentLinePrefix, @"(?<=\s|^)(?:(\w+)\.)?\*$", RegexOptions.IgnoreCase);
            if (starMatch.Success)
            {
                var specificAlias = starMatch.Groups[1].Value;
                var cursorOffset = _buffer.Lines.Take(_buffer.CursorLine).Sum(l => l.Length + 1) + caret;
                var aliases = ETLSuggestEngine.ParseAliases(text, cursorOffset);
                var allCols = new List<string>();

                var tablesToExpand = string.IsNullOrEmpty(specificAlias)
                    ? aliases.Values.Distinct().ToList()
                    : aliases.Values.Where(a => (a.Alias?.Equals(specificAlias, StringComparison.OrdinalIgnoreCase) == true) ||
                                              (string.IsNullOrEmpty(a.Alias) && a.TableName.Equals(specificAlias, StringComparison.OrdinalIgnoreCase))).Distinct().ToList();

                foreach (var info in tablesToExpand)
                {
                    IDataSource? ds = null;
                    if (!string.IsNullOrEmpty(info.ConnectionName) && _connections.TryGetValue(info.ConnectionName, out var foundConn))
                    {
                        ds = foundConn;
                    }
                    else if (_connections.TryGetValue(info.TableName, out var foundTab))
                    {
                        ds = foundTab;
                    }

                    // Fallback to the live context: temp tables (#t) created by SELECT … INTO
                    // during the last run live in the evaluator's connections, not the static scan.
                    ds ??= _metadata.GetRuntimeSource(info.TableName)
                         ?? (string.IsNullOrEmpty(info.ConnectionName) ? null : _metadata.GetRuntimeSource(info.ConnectionName));

                    List<string> cols;
                    if (ds != null)
                    {
                        cols = ((ds is IDatabaseSource db && !string.IsNullOrEmpty(info.BaseTableName))
                            ? await db.GetColumnsAsync(info.BaseTableName)
                            : await ds.GetColumnsAsync()).ToList();
                    }
                    else if (info.TableName.StartsWith("#"))
                    {
                        // Temp table not yet run: resolve from the SELECT … INTO that defines it.
                        cols = (await _metadata.GetTempColumnsAsync(text, info.TableName)).ToList();
                    }
                    else
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(info.Alias))
                        allCols.AddRange(cols.Select(c => $"{info.Alias}.{c}"));
                    else allCols.AddRange(cols);
                }

                if (allCols.Any())
                {
                    var expansion = string.Join(", ", allCols.Distinct());
                    var start = caret - starMatch.Length;
                    _buffer.Lines[_buffer.CursorLine] = line.Remove(start, starMatch.Length).Insert(start, expansion);
                    _buffer.CursorColumn = start + expansion.Length;
                    return;
                }
            }

            // If no special expansion, just show regular suggestions
            await UpdateAsync(System.Threading.CancellationToken.None);
        }
    }
}
