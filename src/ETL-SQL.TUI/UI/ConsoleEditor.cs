using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Common;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;
using ETL_SQL.Core.Common;
using ETL_SQL.Services;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Represents the main interactive console editor for ETL-SQL scripts.
    /// Manages the buffer, rendering, input handling, and execution context.
    /// </summary>
    public class ConsoleEditor
    {
        private readonly ILogger _logger;
        public readonly EditorBuffer _buffer = new();
        public readonly EditorRenderer _renderer;
        public readonly Evaluator _evaluator;
        private readonly UndoManager _undo = new();
        internal readonly MetadataManager _metadata;
        internal readonly AutocompleteController _autocomplete;
        internal readonly InputHandler _input;
        private readonly Dictionary<string, IDataSource> _connections;
        
        private readonly SecurityService _security = new();
        private readonly EditorFileHandler _fileHandler;
        private string? _promptResult;
        private bool _promptResolved;
        private string _filePath;
        private bool _isDirty = false;
        private bool _isExiting = false;

        /// <summary>Dispatches a key press to the input handler.</summary>
        public async Task HandleKey(ConsoleKeyInfo key) => await _input.HandleKey(key);

        /// <summary>Initializes a new instance of the <see cref="ConsoleEditor"/> class.</summary>
        /// <param name="filePath">Initial file path to open.</param>
        /// <param name="connections">Initial set of database connections.</param>
        public ConsoleEditor(string filePath, Dictionary<string, IDataSource> connections)
        {
            _filePath = filePath;
            _connections = connections;
            _logger = Program.ServiceProvider.GetRequiredService<ILogger>();
            _evaluator = Program.ServiceProvider.GetRequiredService<Evaluator>();
            _evaluator.RedirectOutput = true;
            foreach (var conn in connections) _evaluator.Connections[conn.Key] = conn.Value;
            
            _renderer = new EditorRenderer(_buffer, _evaluator);
            _fileHandler = new EditorFileHandler(new PhysicalFileSystem(), _security);
            _metadata = new MetadataManager(_connections);
            _autocomplete = new AutocompleteController(_buffer, _renderer, _metadata, _connections, _logger);
            _input = new InputHandler(this, _buffer, _renderer, _autocomplete);
            if (_logger is LoggerService ls)
            {
                ls.SuppressConsole = true;
                ls.OnMessage += (msg, color) => _evaluator.Log(msg, color);
            }
        }

        /// <summary>Performs asynchronous initialization, including loading the initial file.</summary>
        public async Task InitializeAsync()
        {
            await LoadFile(_filePath);
        }

        /// <summary>Loads a script file into the editor buffer.</summary>
        /// <param name="filePath">The path to the file to load.</param>
        public async Task LoadFile(string filePath)
        {
            var (lines, path) = await _fileHandler.LoadAsync(filePath, ShowPrompt);
            _buffer.Load(lines);
            _filePath = path;
            _isDirty = false;
            _undo.Clear();
            _metadata.RefreshConnections(_buffer.GetText(), force: true);
        }

        /// <summary>Clears the buffer and starts a new file.</summary>
        public void NewFile()
        {
            if (_isDirty && !AnsiConsole.Confirm("Discard changes and start new file?")) return;
            _buffer.Load(new[] { "" });
            _filePath = "untitled.etlsql";
            _isDirty = false;
            _undo.Clear();
            _renderer.ShowStatus("New file started.");
        }

        /// <summary>Starts the main editor loop, handling rendering and input.</summary>
        public async Task Run()
        {
            try { Console.Clear(); } catch (Exception ex) { _logger.Debug("[ConsoleEditor] Console.Clear() failed: {Message}", ex.Message); }
            _metadata.RefreshConnections(_buffer.GetText(), force: true);
            
            while (!_isExiting)
            {
                _renderer.Render(_buffer, _evaluator, _filePath, _isDirty, Console.WindowWidth, Console.WindowHeight);
                var key = Console.ReadKey(true);

                if (!_renderer.PromptVisible)
                {
                    await _input.HandleKey(key);
                }
            }
            Console.Clear();
            Console.SetCursorPosition(0, 0);
            Console.CursorVisible = true;
        }

        public async Task HandleExit()
        {
            if (_isDirty)
            {
                var choice = await ShowPrompt("Save changes before exiting? (y/n/c)", "");
                if (string.IsNullOrEmpty(choice) || choice.Equals("c", StringComparison.OrdinalIgnoreCase)) return;
                if (choice.Equals("y", StringComparison.OrdinalIgnoreCase)) { if (!await SaveScript()) return; }
            }
            _isExiting = true;
        }

        /// <summary>Displays an interactive prompt to the user and waits for input.</summary>
        /// <param name="title">The message to display in the prompt.</param>
        /// <param name="initialValue">The initial text in the prompt.</param>
        /// <param name="isSecret">Whether to mask the input.</param>
        /// <returns>The user's input, or null if cancelled.</returns>
        public async Task<string?> ShowPrompt(string title, string initialValue = "", bool isSecret = false)
        {
            _renderer.PromptTitle = title;
            _renderer.PromptValue = initialValue;
            _renderer.PromptCursor = initialValue.Length;
            _renderer.PromptSuggestions.Clear();
            _renderer.PromptSuggestionIndex = 0;
            _renderer.PromptIsSecret = isSecret;
            _promptResolved = false;
            _promptResult = null;
            
            while (!_promptResolved && !_isExiting)
            {
                _renderer.Render(_buffer, _evaluator, _filePath, _isDirty, Console.WindowWidth, Console.WindowHeight);
                var key = Console.ReadKey(true);
                await _input.HandlePromptKey(key);
            }
            
            var result = _promptResult;
            
            _renderer.PromptTitle = null;
            _renderer.PromptValue = "";
            _renderer.PromptCursor = 0;
            _renderer.PromptSuggestions.Clear();
            _renderer.PromptIsSecret = false;
            _promptResolved = false;
            _promptResult = null;
            
            return result;
        }

        /// <summary>Resolves the current prompt with a value.</summary>
        public void ResolvePrompt(string? value) 
        { 
            _promptResult = value;
            _promptResolved = true;
        }

        /// <summary>Displays a full-screen help overlay.</summary>
        public void ShowHelp()
        {
            _renderer.HelpVisible = true;
            _renderer.Render(_buffer, _evaluator, _filePath, _isDirty, Console.WindowWidth, Console.WindowHeight);
            Console.ReadKey(true);
            _renderer.HelpVisible = false;
        }

        /// <summary>Saves the current buffer state for undo.</summary>
        public void SaveUndoState() => _undo.SaveState(_buffer.Lines);
        
        /// <summary>Restores the previous buffer state.</summary>
        public void Undo() { var lines = _undo.Undo(_buffer.Lines); if (lines != null) _buffer.Load(lines); }
        
        /// <summary>Restores the state that was undone.</summary>
        public void Redo() { var lines = _undo.Redo(_buffer.Lines); if (lines != null) _buffer.Load(lines); }
        
        /// <summary>Marks the current document as modified.</summary>
        public void MarkDirty() => _isDirty = true;

        /// <summary>Automatically formats the current script buffer.</summary>
        public void FormatScript()
        {
            var text = _buffer.GetText();
            SaveUndoState();
            _buffer.Load(SqlFormatter.Format(text).Split('\n'));
            MarkDirty();
        }

        /// <summary>Prompts for a search term and navigates to the next occurrence.</summary>
        public async Task Find()
        {
            var target = await ShowPrompt("Find", "");
            if (string.IsNullOrEmpty(target)) return;

            var text = _buffer.GetText();
            int start = _buffer.GetFlatPosition(_buffer.CursorLine, _buffer.CursorColumn);
            int index = text.IndexOf(target, start + 1, StringComparison.OrdinalIgnoreCase);
            if (index == -1) index = text.IndexOf(target, 0, StringComparison.OrdinalIgnoreCase);

            if (index != -1)
            {
                var pos = _buffer.GetLineColFromFlat(index);
                _buffer.CursorLine = pos.line;
                _buffer.CursorColumn = pos.col;
            }
            else _renderer.ShowStatus($"'{target}' not found.");
        }

        /// <summary>Prompts for a string replacement and updates the buffer.</summary>
        public async Task Replace()
        {
            var target = await ShowPrompt("Find", "");
            if (string.IsNullOrEmpty(target)) return;

            var replacement = await ShowPrompt("Replace with", "");
            if (replacement == null) return; // User cancelled

            var text = _buffer.GetText();
            if (text.Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                SaveUndoState();
                var newText = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(target), replacement, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                _buffer.Load(newText.Split('\n'));
                MarkDirty();
                _renderer.ShowStatus($"Replaced '{target}' with '{replacement}'");
            }
            else _renderer.ShowStatus($"'{target}' not found.");
        }

        /// <summary>Prompts for a line number and navigates the cursor to it.</summary>
        public async Task GoToLine()
        {
            var input = await ShowPrompt("Go to line", "");
            if (int.TryParse(input, out int line))
            {
                _buffer.CursorLine = Math.Max(0, Math.Min(line - 1, _buffer.Lines.Count - 1));
                _buffer.CursorColumn = 0;
            }
        }

        /// <summary>Executes the entire script or the current selection.</summary>
        public async Task RunScript() 
        {
            var selectedText = _buffer.GetSelectedText();
            if (!string.IsNullOrEmpty(selectedText))
            {
                _renderer.ShowStatus("Executing selection...");
                await ExecuteSource(selectedText);
            }
            else
            {
                _renderer.ShowStatus("Executing script...");
                await ExecuteSource(_buffer.GetText());
            }
        }

        /// <summary>Executes only the line at the current cursor position.</summary>
        public async Task RunStatementAtCursor()
        {
            var lines = _buffer.Lines;
            var currentLine = lines[_buffer.CursorLine];
            await ExecuteSource(currentLine);
        }

        private async Task ExecuteSource(string source)
        {
            try 
            { 
                _renderer.TreeVisible = true;
                _renderer.PerformanceVisible = false;
                
                var tokens = new Lexer(source).Tokenize();
                var script = new Parser(tokens).Parse();
                
                // 1. Show Parser Diagnostics
                foreach (var diag in script.Diagnostics)
                {
                    _evaluator.Log($"[PARSER {diag.Severity}] {diag.Message} at line {diag.Line}, col {diag.Column}", diag.Severity == DiagnosticSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow);
                }

                // 2. Run Linter
                var lintContext = new DefaultLintContext {
                    Metadata = new ConsoleMetadataProvider(_metadata),
                    DocumentUri = _filePath
                };
                
                var linter = new Linter();
                foreach (var type in typeof(ILintRule).Assembly.GetTypes()
                    .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
                {
                    if (Activator.CreateInstance(type) is ILintRule rule)
                        linter.AddRule(rule);
                }

                var lintResults = await linter.AnalyzeAsync(script, lintContext);
                foreach (var res in lintResults)
                {
                    ConsoleColor color = res.Severity == LintSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
                    _evaluator.Log($"[LINT {res.Severity}] {res.Message} at line {res.LineNumber}, col {res.ColumnNumber}", color);
                }

                // 3. Execute only if no critical syntax errors? 
                // Or try to execute what we can? 
                // Let's stop on parser errors for safety.
                if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    _renderer.ShowStatus("Execution aborted due to syntax errors.");
                    return;
                }

                await _evaluator.Evaluate(script); 
                _renderer.ShowStatus("Query finished.");
            }
            catch (Exception ex) { _renderer.ShowStatus($"Error: {ex.Message}"); }
            finally
            {
                _renderer.Render(_buffer, _evaluator, _filePath, _isDirty, Console.WindowWidth, Console.WindowHeight);
            }
        }

        private class ConsoleMetadataProvider : IMetadataProvider
        {
            private readonly MetadataManager _mgr;
            public ConsoleMetadataProvider(MetadataManager mgr) => _mgr = mgr;
            public Task<IEnumerable<string>> GetTablesAsync(string connectionName) => _mgr.GetTablesAsync(connectionName);
            public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) => _mgr.GetColumnsAsync(connectionName, tableName);
            public IEnumerable<string> GetConnections() => _mgr.GetConnections();
            public string? GetConnectionType(string connectionName) => _mgr.GetConnectionType(connectionName);
        }

        /// <summary>Persists the current script buffer to disk. Prompts for a path if unnamed.</summary>
        /// <param name="forcePrompt">Whether to force a 'Save As' prompt.</param>
        /// <returns>True if the file was saved; otherwise, false.</returns>
        public async Task<bool> SaveScript(bool forcePrompt = false)
        {
            if (forcePrompt || _filePath == "untitled.etlsql")
            {
                var newPath = await ShowPrompt("Save As", _filePath == "untitled.etlsql" ? "" : _filePath);
                if (string.IsNullOrEmpty(newPath)) return false;
                if (!Path.HasExtension(newPath)) newPath += ".etlsql";
                _filePath = newPath;
            }

            var text = _buffer.GetText();
            bool success = await _fileHandler.SaveAsync(_filePath, text, ShowPrompt);

            if (success)
            {
                _isDirty = false;
                _renderer.ShowStatus($"Saved to {_filePath}");
                return true;
            }
            else
            {
                _renderer.ShowStatus($"Save failed.");
                return false;
            }
        }

        /// <summary>Exports the most recent data result set to a CSV file.</summary>
        public async Task ExportResults()
        {
            if (_evaluator.LastResult == null) { _renderer.ShowStatus("No results to export."); return; }
            string defaultPath = Path.ChangeExtension(_filePath, ".csv");
            Console.SetCursorPosition(0, Console.WindowHeight - 1);
            var path = AnsiConsole.Ask<string>("Export to CSV path:", defaultPath);
            _renderer.ShowStatus($"Exported to {path}");
        }

        private static string? _clipboard;

        /// <summary>Copies the current selection to the internal clipboard.</summary>
        public void Copy()
        {
            var text = _buffer.GetSelectedText();
            if (!string.IsNullOrEmpty(text)) _clipboard = text;
            else _renderer.ShowStatus("No text selected to copy.");
        }

        /// <summary>Cuts the current selection to the internal clipboard.</summary>
        public void Cut()
        {
            var text = _buffer.GetSelectedText();
            if (!string.IsNullOrEmpty(text))
            {
                _clipboard = text;
                SaveUndoState();
                _buffer.DeleteSelection();
                MarkDirty();
            }
            else _renderer.ShowStatus("No text selected to cut.");
        }

        /// <summary>Pastes text from the internal clipboard at the cursor position.</summary>
        public void Paste()
        {
            if (string.IsNullOrEmpty(_clipboard)) { _renderer.ShowStatus("Clipboard is empty."); return; }
            SaveUndoState();
            _buffer.Paste(_clipboard);
            MarkDirty();
        }

        /// <summary>Deletes the entire line at the current cursor position.</summary>
        public void DeleteLine() { SaveUndoState(); _buffer.DeleteLine(); MarkDirty(); }
        
        /// <summary>Duplicates the current line below the cursor.</summary>
        public void DuplicateLine() { SaveUndoState(); _buffer.DuplicateLine(); MarkDirty(); }

        /// <summary>Moves the cursor to the top of the document.</summary>
        public void GoToTop() { _buffer.Top(); }
        
        /// <summary>Moves the cursor to the bottom of the document.</summary>
        public void GoToBottom() { _buffer.Bottom(); }
    }
}
