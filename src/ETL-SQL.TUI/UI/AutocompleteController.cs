using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Common;

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

        /// <summary>Updates the suggestion list based on the current cursor position and prefix.</summary>
        public async Task UpdateAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _metadata.RefreshConnections(_buffer.GetText());
            var line = _buffer.Lines[_buffer.CursorLine].Substring(0, _buffer.CursorColumn);
            var lastWordMatch = Regex.Match(line, @"[\w.#@./\\\""']*$");
            var lastWord = lastWordMatch.Value.Trim('\'', '\"');
            
            if (string.IsNullOrEmpty(lastWord) && !line.EndsWith("=") && !line.EndsWith("("))
            {
                _renderer.AutocompleteVisible = false;
                return;
            }

            _renderer.AutocompleteOptions = await ETLSuggestEngine.GetSuggestionsAsync(lastWord, _buffer.GetText(), _connections, _logger, _helpRegistry);
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
            var lastWordMatch = Regex.Match(line.Substring(0, _buffer.CursorColumn), @"[\w.#@./\\\""']*$");
            var matchValue = lastWordMatch.Value;
            var startPos = _buffer.CursorColumn - matchValue.Length;

            // Handle quoted matches
            if (matchValue.Length > 0 && (matchValue[0] == '\'' || matchValue[0] == '\"') && !choice.StartsWith("'") && !choice.StartsWith("\""))
            {
                startPos++;
                matchValue = matchValue.Substring(1);
            }

            _buffer.Lines[_buffer.CursorLine] = line.Remove(startPos, matchValue.Length).Insert(startPos, choice);
            _buffer.CursorColumn = startPos + choice.Length;
            _renderer.AutocompleteVisible = false;
        }

        /// <summary>Attempts to provide specialized suggestions, such as expansion of '*'.</summary>
        public async Task TrySuggestAsync()
        {
            var text = _buffer.GetText();
            _metadata.RefreshConnections(text, force: true);
            var line = _buffer.Lines[_buffer.CursorLine];
            var currentLinePrefix = line.Substring(0, _buffer.CursorColumn);
            
            // Matches ' *' or 'alias.*' (case insensitive)
            var starMatch = Regex.Match(currentLinePrefix, @"(?<=\s|^)(?:(\w+)\.)?\*$", RegexOptions.IgnoreCase);
            if (starMatch.Success)
            {
                var specificAlias = starMatch.Groups[1].Value;
                var aliases = ETLSuggestEngine.ParseAliases(text);
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

                    if (ds != null)
                    {
                        var cols = (ds is IDatabaseSource db && !string.IsNullOrEmpty(info.BaseTableName))
                            ? await db.GetColumnsAsync(info.BaseTableName)
                            : await ds.GetColumnsAsync();

                        if (!string.IsNullOrEmpty(info.Alias))
                             allCols.AddRange(cols.Select(c => $"{info.Alias}.{c}"));
                        else allCols.AddRange(cols);
                    }
                }

                if (allCols.Any())
                {
                    var expansion = string.Join(", ", allCols.Distinct());
                    var start = _buffer.CursorColumn - starMatch.Length;
                    _buffer.Lines[_buffer.CursorLine] = line.Remove(start, starMatch.Length).Insert(start, expansion);
                    _buffer.CursorColumn = start + expansion.Length;
                    return;
                }
            }
            
            // If no special expansion, just show regular suggestions
            await UpdateAsync();
        }
    }
}
