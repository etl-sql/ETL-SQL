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
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Common;
using ETL_SQL.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Services;

namespace ETL_SQL.TUI.UI
{
    public record EditorDiagnostic(string Source, string Severity, string Message, int Line, int Column);

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
        private readonly ILanguageService _languageService;
        private readonly Dictionary<string, IDataSource> _connections;
        private readonly List<EditorDiagnostic> _diagnostics = new();
        private int _activeDiagnosticIndex = -1;

        private readonly Services.IClipboardService _clipboard;
        private readonly SecurityService _security;
        private readonly EditorFileHandler _fileHandler;
        private string? _promptResult;
        private bool _promptResolved;
        internal string _filePath;
        internal bool _isDirty = false;
        private bool _isExiting = false;

        /// <summary>Dispatches a key press to the input handler.</summary>
        public async Task HandleKey(ConsoleKeyInfo key) => await _input.HandleKey(key);

        public IReadOnlyList<EditorDiagnostic> Diagnostics => _diagnostics;

        /// <summary>Initializes a new instance of the <see cref="ConsoleEditor"/> class.</summary>
        /// <param name="filePath">Initial file path to open.</param>
        /// <param name="connections">Initial set of database connections.</param>
        public ConsoleEditor(string filePath, Dictionary<string, IDataSource> connections)
        {
            _filePath = filePath;
            _connections = connections;
            _logger = Program.ServiceProvider.GetRequiredService<ILogger>();
            _clipboard = Program.ServiceProvider.GetRequiredService<Services.IClipboardService>();
            _security = new SecurityService(_logger);
            _evaluator = Program.ServiceProvider.GetRequiredService<Evaluator>();
            _evaluator.RedirectOutput = true;
            foreach (var conn in connections) _evaluator.Connections[conn.Key] = conn.Value;
            _evaluator.Telemetry.IsProfiling = true;
            _renderer = new EditorRenderer(_buffer, _evaluator);
            _fileHandler = new EditorFileHandler(new PhysicalFileSystem(), _security);
            _metadata = new MetadataManager(_evaluator, _connections);
            var helpRegistry = Program.ServiceProvider.GetService<Core.Interfaces.ILanguageHelpRegistry>();
            _languageService = Program.ServiceProvider.GetRequiredService<ILanguageService>();
            _autocomplete = new AutocompleteController(_buffer, _renderer, _metadata, _connections, _logger, helpRegistry);
            _input = new InputHandler(this, _buffer, _renderer, _autocomplete);
            _tabs.Add(new TabState { FilePath = filePath });

            if (_logger is LoggerService ls)
            {
                ls.SuppressConsole = true;
            }
        }

        /// <summary>Performs asynchronous initialization, including loading the initial file.</summary>
        public async Task InitializeAsync()
        {
            await LoadFile(_filePath);
            _renderer._sidebarPanel.Initialize(_filePath);
        }

        /// <summary>Loads a script file into the editor buffer.</summary>
        /// <param name="filePath">The path to the file to load.</param>
        public async Task LoadFile(string filePath)
        {
            await _evaluator.ResetSessionAsync();
            var (lines, path) = await _fileHandler.LoadAsync(filePath, ShowPrompt);
            _buffer.Load(lines);
            _filePath = path;
            _isDirty = false;
            _undo.Clear();
            _metadata.RefreshConnections(_buffer.GetText(), force: true);
            _renderer._sidebarPanel.Initialize(filePath);
        }

        /// <summary>Clears the buffer and starts a new file.</summary>
        public async Task NewFile()
        {
            if (_isDirty && !AnsiConsole.Confirm("Discard changes and start new file?")) return;
            await _evaluator.ResetSessionAsync();
            _buffer.Load(new[] { "" });
            _filePath = "untitled.etlsql";
            _isDirty = false;
            _undo.Clear();
            _renderer.ShowStatus("New file started.");
            _renderer._sidebarPanel.Initialize(_filePath);
        }

        /// <summary>Starts the main editor loop, handling rendering and input.</summary>
        public async Task Run()
        {
            Console.OutputEncoding = Encoding.UTF8;
            // Perform a robust full-screen clear to purge artifacts from previous CLI statements
            try 
            { 
                if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
                {
                    Console.BufferHeight = Console.WindowHeight;
                }
                AnsiConsole.Console.Cursor.Hide();
                AnsiConsole.Console.Write("\x1b[?1049h\x1b[?1000h\x1b[?1006h"); // Switch to alternative buffer and enable mouse tracking
                AnsiConsole.Console.Write("\x1b[H\x1b[2J\x1b[3J");
                AnsiConsole.Console.Clear(); 
                AnsiConsole.Console.Cursor.SetPosition(1, 1);
                _renderer.ForceFullRepaint();
            } 
            catch { }

            _metadata.RefreshConnections(_buffer.GetText(), force: true);

            while (!_isExiting)
            {
                _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);
                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Escape && Console.KeyAvailable)
                {
                    var sb = new System.Text.StringBuilder();
                    while (Console.KeyAvailable)
                    {
                        sb.Append(Console.ReadKey(true).KeyChar);
                    }
                    string seq = sb.ToString();
                    if (seq.StartsWith("[<"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(seq, @"^\[<(\d+);(\d+);(\d+)([Mm])");
                        if (match.Success)
                        {
                            int button = int.Parse(match.Groups[1].Value);
                            int mouseX = int.Parse(match.Groups[2].Value) - 1;
                            int mouseY = int.Parse(match.Groups[3].Value) - 1;
                            bool isRelease = match.Groups[4].Value == "m";

                            _renderer.HandleMouseClick(button, mouseX, mouseY, isRelease, this);
                            continue;
                        }
                    }
                }

                if (!_renderer.PromptVisible)
                {
                    await _input.HandleKey(key);
                }
            }
            AnsiConsole.Console.Write("\x1b[?1049l\x1b[?1000l\x1b[?1006l"); // Exit alternative buffer and disable mouse tracking
            AnsiConsole.Console.Cursor.Show();
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
                RenderCurrent();
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
            _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);
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
            if (!TryFindNext(target))
            {
                _renderer.ShowStatus($"'{target}' not found.");
            }
        }

        /// <summary>Moves the cursor to the next case-insensitive match, wrapping to the top when needed.</summary>
        public bool TryFindNext(string? target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            var text = _buffer.GetText();
            int start = _buffer.GetFlatPosition(_buffer.CursorLine, _buffer.CursorColumn);
            int index = text.IndexOf(target, start + 1, StringComparison.OrdinalIgnoreCase);
            if (index == -1) index = text.IndexOf(target, 0, StringComparison.OrdinalIgnoreCase);

            if (index != -1)
            {
                var pos = _buffer.GetLineColFromFlat(index);
                _buffer.CursorLine = pos.line;
                _buffer.CursorColumn = pos.col;
                return true;
            }

            return false;
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

        public bool NavigateDiagnostic(int direction)
        {
            if (_diagnostics.Count == 0)
            {
                _renderer.ShowStatus("No diagnostics.");
                return false;
            }

            _activeDiagnosticIndex = _activeDiagnosticIndex < 0
                ? (direction >= 0 ? 0 : _diagnostics.Count - 1)
                : (_activeDiagnosticIndex + direction + _diagnostics.Count) % _diagnostics.Count;

            var diagnostic = _diagnostics[_activeDiagnosticIndex];
            _buffer.CursorLine = Math.Clamp(diagnostic.Line - 1, 0, _buffer.Lines.Count - 1);
            _buffer.CursorColumn = Math.Clamp(diagnostic.Column - 1, 0, _buffer.Lines[_buffer.CursorLine].Length);
            _renderer.Focus = EditorFocus.Editor;
            _renderer.AutocompleteVisible = false;
            _renderer.ReportVisible = false;
            _renderer.ShowStatus($"Diagnostic {_activeDiagnosticIndex + 1}/{_diagnostics.Count}: {diagnostic.Source} {diagnostic.Severity} - {diagnostic.Message}");
            return true;
        }

        /// <summary>Executes the entire script.</summary>
        public async Task RunScript()
        {
            _renderer.ShowStatus("Executing script...");
            await ExecuteSource(_buffer.GetText());
        }

        /// <summary>Executes only the currently selected text.</summary>
        public async Task RunSelectedText()
        {
            var selectedText = _buffer.GetSelectedText();
            if (string.IsNullOrEmpty(selectedText))
            {
                _renderer.ShowStatus("No text selected.");
                return;
            }

            _renderer.ShowStatus("Executing selection...");
            await ExecuteSource(selectedText);
        }

        /// <summary>Executes only the line at the current cursor position.</summary>
        public async Task RunStatementAtCursor()
        {
            var lines = _buffer.Lines;
            var currentLine = lines[_buffer.CursorLine];
            _renderer.ShowStatus($"Executing line {_buffer.CursorLine + 1}...");
            await ExecuteSource(currentLine);
        }

        private async Task ExecuteSource(string source)
        {
            try
            {
                _diagnostics.Clear();
                _activeDiagnosticIndex = -1;
                _renderer.IsBottomMaximized = false;
                var totalSw = System.Diagnostics.Stopwatch.StartNew();

                var lexSw = System.Diagnostics.Stopwatch.StartNew();
                var tokens = new Lexer(source).Tokenize();
                lexSw.Stop();
                _evaluator.LastLexTimeMs = lexSw.ElapsedMilliseconds;

                var parseSw = System.Diagnostics.Stopwatch.StartNew();
                var script = new Parser(tokens).Parse();
                parseSw.Stop();
                _evaluator.LastParseTimeMs = parseSw.ElapsedMilliseconds;

                // 1. Show Parser Diagnostics
                foreach (var diag in script.Diagnostics)
                {
                    AddDiagnostic("PARSER", diag.Severity.ToString(), diag.Message, diag.Line, diag.Column);
                    _evaluator.Log($"[PARSER {diag.Severity}] {diag.Message} at line {diag.Line}, col {diag.Column}", diag.Severity == DiagnosticSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow);
                }

                // 2. Run Linter
                var lintContext = new DefaultLintContext
                {
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
                    AddDiagnostic("LINT", res.Severity.ToString(), res.Message, res.LineNumber, res.ColumnNumber);
                    _evaluator.Log($"[LINT {res.Severity}] {res.Message} at line {res.LineNumber}, col {res.ColumnNumber}", color);
                }

                SortDiagnostics();

                // 3. Execute only if no critical syntax errors? 
                // Or try to execute what we can? 
                // Let's stop on parser errors for safety.
                if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    _renderer.ShowStatus("Execution aborted due to syntax errors.");
                    return;
                }

                var execSw = System.Diagnostics.Stopwatch.StartNew();
                _evaluator.Telemetry.IsProfiling = true; // Enable profiling by default in IDE mode for Performance Dashboard
                var oldScriptPath = _evaluator.CurrentScriptPath;
                var oldWorkingDirectory = _evaluator.WorkingDirectory;
                ApplyEditorExecutionPath();
                try
                {
                    await _evaluator.Evaluate(script);
                }
                finally
                {
                    _evaluator.CurrentScriptPath = oldScriptPath;
                    _evaluator.WorkingDirectory = oldWorkingDirectory;
                }
                execSw.Stop();
                _evaluator.Telemetry.LastExecutionTimeMs = execSw.ElapsedMilliseconds;

                // After each run, default to showing the execution tree and messages
                _renderer.ResultsVisible = false;
                _renderer.PerformanceVisible = false;
                _renderer.ActiveLowerTab = EditorFocus.ExecutionTree;
                _renderer.Focus = EditorFocus.Editor;

                if (_evaluator.LastResultSets.Count > 0)
                {
                    _renderer.ActiveResultSetIndex = _evaluator.LastResultSets.Count - 1;
                    _renderer.ResultScrollRow = 0;
                    _renderer.ResultScrollCol = 0;
                    _renderer.FilterText = "";
                }

                totalSw.Stop();
                _renderer.ShowStatus($"Query finished in {totalSw.ElapsedMilliseconds}ms.");

                // Phase 5: Build report manifest if any visuals/pages were defined
                if (_evaluator.ReportContext.PageDefinitions.Count > 0 || _evaluator.ReportContext.VisualDefinitions.Count > 0)
                {
                    try
                    {
                        var manifestBuilder = new ETL_SQL.Reporting.ManifestBuilder(_evaluator);
                        _renderer.CurrentReportManifest = await manifestBuilder.BuildAsync(_filePath);
                        _renderer.ActiveReportPageIndex = 0;
                        _renderer.ShowStatus($"Query finished. Report built with {_renderer.CurrentReportManifest.Visuals.Count} visuals.");
                    }
                    catch (Exception ex)
                    {
                        _evaluator.Log($"[REPORT ERROR] {ex.Message}", ConsoleColor.Red);
                    }
                }
                else
                {
                    _renderer.CurrentReportManifest = null;
                }
            }
            catch (Exception ex)
            {
                _evaluator.Log($"[ERROR] {ex.Message}", ConsoleColor.Red);
                _renderer.ShowStatus($"Error: {ex.Message}");
            }
            finally
            {
                _renderer.MessageScrollRow = int.MaxValue; // Auto-scroll to latest messages
                RenderCurrent();
            }
        }

        private void ApplyEditorExecutionPath()
        {
            if (string.IsNullOrWhiteSpace(_filePath) || _filePath == "untitled.etlsql")
            {
                _evaluator.CurrentScriptPath = null;
                _evaluator.WorkingDirectory = Directory.GetCurrentDirectory();
                return;
            }

            var fullPath = Path.GetFullPath(_filePath);
            _evaluator.CurrentScriptPath = fullPath;

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _evaluator.WorkingDirectory = directory;
            }
        }

        private void RenderCurrent()
        {
            if (_renderer.Headless)
            {
                _renderer.Render(this, 100, 30);
                return;
            }

            _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);
        }

        private void AddDiagnostic(string source, string severity, string message, int line, int column)
        {
            _diagnostics.Add(new EditorDiagnostic(
                source,
                severity,
                message,
                Math.Max(1, line),
                Math.Max(1, column)));
        }

        private void SortDiagnostics()
        {
            _diagnostics.Sort((left, right) =>
            {
                int line = left.Line.CompareTo(right.Line);
                if (line != 0) return line;
                int col = left.Column.CompareTo(right.Column);
                if (col != 0) return col;
                return string.Compare(left.Source, right.Source, StringComparison.Ordinal);
            });
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
                _renderer._sidebarPanel.Initialize(_filePath);
                return true;
            }
            else
            {
                _renderer.ShowStatus($"Save failed.");
                return false;
            }
        }

        /// <summary>Opens a filter prompt for the specified compare pane.</summary>
        public async Task FilterComparePane(int paneIndex)
        {
            while (_renderer.CompareFilters.Count <= paneIndex) _renderer.CompareFilters.Add("");
            var filter = await ShowPrompt($"Filter pane {paneIndex + 1}", _renderer.CompareFilters[paneIndex]);
            if (filter == null) return;
            _renderer.CompareFilters[paneIndex] = filter.Trim();
            _renderer.CompareScrollRows[paneIndex] = 0;
            _renderer.ShowStatus(string.IsNullOrEmpty(filter.Trim()) ? "Filter cleared." : $"Pane {paneIndex + 1} filter: {filter.Trim()}");
        }

        /// <summary>Opens a filter prompt for the active result set.</summary>
        public async Task FilterResults()
        {
            var filter = await ShowPrompt("Filter rows", _renderer.FilterText);
            if (filter == null) return;
            _renderer.FilterText = filter.Trim();
            _renderer.ResultScrollRow = 0;
            _renderer.ShowStatus(string.IsNullOrEmpty(_renderer.FilterText) ? "Filter cleared." : $"Filtering: {_renderer.FilterText}");
        }

        /// <summary>Copies the current selection (or results) to the clipboard.</summary>
        /// <summary>Copies the current selection (or results) to the clipboard.</summary>
        public async Task Copy()
        {
            switch (_renderer.Focus)
            {
                case EditorFocus.Results:
                    if (_renderer.ResultsVisible && _evaluator.LastResultSets.Count > _renderer.ActiveResultSetIndex)
                    {
                        var rs = _evaluator.LastResultSets[_renderer.ActiveResultSetIndex];
                        var sb = new StringBuilder();
                        sb.AppendLine(string.Join("\t", rs.ColumnNames));
                        foreach (var row in rs.Rows) sb.AppendLine(string.Join("\t", row.Columns.Values));
                        await _clipboard.SetTextAsync(sb.ToString());
                        _renderer.ShowStatus("Results copied as TSV.");
                    }
                    break;

                case EditorFocus.Performance:
                    if (_renderer.PerformanceVisible)
                    {
                        var text = string.Join(Environment.NewLine, _evaluator.Telemetry.ProfileMetrics.Select(m => $"{m.Sql}: {m.DurationMs}ms"));
                        await _clipboard.SetTextAsync(text);
                        _renderer.ShowStatus("Performance metrics copied.");
                    }
                    break;

                case EditorFocus.Messages:
                    {
                        // Copy messages — clean text only, no tree borders
                        var text = string.Join(Environment.NewLine, _evaluator.Messages.Select(m => m.Message));
                        await _clipboard.SetTextAsync(text);
                        _renderer.ShowStatus("Messages copied.");
                    }
                    break;

                case EditorFocus.ExecutionTree:
                    {
                        var treeRenderer = new ExecutionTreeAsciiRenderer();
                        var treeLines = treeRenderer.Render(_evaluator.Telemetry.ExecutionTree);
                        var treeText = string.Join(Environment.NewLine, treeLines.Select(l => l.Indent + l.Connector + l.Label + (string.IsNullOrEmpty(l.Stats) ? "" : " " + l.Stats)));
                        await _clipboard.SetTextAsync(treeText);
                        _renderer.ShowStatus("Pipeline tree copied.");
                    }
                    break;

                default:
                    {
                        var text = _buffer.GetSelectedText();
                        if (string.IsNullOrEmpty(text)) text = _buffer.Lines[_buffer.CursorLine];
                        
                        if (!string.IsNullOrEmpty(text))
                        {
                            await _clipboard.SetTextAsync(text);
                            _renderer.ShowStatus("Text copied to clipboard.");
                        }
                    }
                    break;
            }
        }

        /// <summary>Prompts for a file path and exports the active result set as RFC 4180 CSV.</summary>
        public async Task ExportResults()
        {
            if (_evaluator.LastResultSets.Count == 0)
            {
                _renderer.ShowStatus("No results to export.");
                return;
            }

            var scriptBase = Path.GetFileNameWithoutExtension(_filePath);
            var defaultPath = scriptBase + ".csv";
            var path = await ShowPrompt("Export CSV", defaultPath);
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var rs = _evaluator.LastResultSets[_renderer.ActiveResultSetIndex];
                await using var writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8);
                await writer.WriteLineAsync(string.Join(",", rs.ColumnNames.Select(EscapeCsv)));
                foreach (var row in rs.Rows)
                    await writer.WriteLineAsync(string.Join(",", rs.ColumnNames.Select(col => EscapeCsv(row[col]?.ToString() ?? ""))));
                _renderer.ShowStatus($"Exported {rs.Rows.Count} rows to {path}");
            }
            catch (Exception ex)
            {
                _renderer.ShowStatus($"Export failed: {ex.Message}");
            }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        /// <summary>Cuts the current selection to the clipboard.</summary>
        public async Task Cut()
        {
            if (_renderer.ResultsFocus) return; // Cannot cut from results
            
            var text = _buffer.GetSelectedText();
            if (!string.IsNullOrEmpty(text))
            {
                await _clipboard.SetTextAsync(text);
                SaveUndoState();
                _buffer.DeleteSelection();
                MarkDirty();
                _renderer.ShowStatus("Text cut to clipboard.");
            }
        }

        /// <summary>Pastes the clipboard content at the current cursor position.</summary>
        public async Task Paste()
        {
            if (_renderer.ResultsFocus) return; // Cannot paste into results
            
            var text = await _clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                MarkDirty();
                SaveUndoState();
                _buffer.Paste(text);
                _renderer.ShowStatus("Text pasted.");
            }
        }


        /// <summary>Deletes the entire line at the current cursor position.</summary>
        public void DeleteLine() { SaveUndoState(); _buffer.DeleteLine(); MarkDirty(); }

        /// <summary>Duplicates the current line below the cursor.</summary>
        public void DuplicateLine() { SaveUndoState(); _buffer.DuplicateLine(); MarkDirty(); }

        /// <summary>Moves the cursor to the top of the document.</summary>
        public void GoToTop() { _buffer.Top(); }

        /// <summary>Moves the cursor to the bottom of the document.</summary>
        public void GoToBottom() { _buffer.Bottom(); }

        internal readonly List<TabState> _tabs = new();
        internal int _activeTabIndex = 0;

        public void SaveActiveTabState()
        {
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
            {
                var tab = _tabs[_activeTabIndex];
                tab.FilePath = _filePath;
                tab.Lines = _buffer.Lines.ToList();
                tab.CursorLine = _buffer.CursorLine;
                tab.CursorColumn = _buffer.CursorColumn;
                tab.SelectionStartLine = _buffer.SelectionStartLine;
                tab.SelectionStartCol = _buffer.SelectionStartCol;
                tab.IsDirty = _isDirty;
                tab.ScrollLine = _renderer.ScrollLine;
                tab.ScrollCol = _renderer.ScrollCol;
                tab.Diagnostics = _diagnostics.ToList();
            }
        }

        public void LoadTabState(int index)
        {
            if (index >= 0 && index < _tabs.Count)
            {
                _activeTabIndex = index;
                var tab = _tabs[index];
                _filePath = tab.FilePath;
                _buffer.Load(tab.Lines);
                _buffer.CursorLine = tab.CursorLine;
                _buffer.CursorColumn = tab.CursorColumn;
                _buffer.SelectionStartLine = tab.SelectionStartLine;
                _buffer.SelectionStartCol = tab.SelectionStartCol;
                _isDirty = tab.IsDirty;
                _renderer.ScrollLine = tab.ScrollLine;
                _renderer.ScrollCol = tab.ScrollCol;
                _diagnostics.Clear();
                _diagnostics.AddRange(tab.Diagnostics);
                
                // Clear query results and display states when switching tabs
                _evaluator.ClearResults();
                _evaluator.LastResultSets.Clear();
                _renderer.ResultScrollRow = 0;
                _renderer.ResultScrollCol = 0;
                _renderer.ActiveResultSetIndex = 0;
                _renderer.FilterText = "";

                _renderer.ForceFullRepaint();
                _renderer.ShowStatus($"Switched to: {Path.GetFileName(_filePath)}");
            }
        }

        public async Task OpenFileInTab(string filePath)
        {
            SaveActiveTabState();

            string fullPath = Path.GetFullPath(filePath);
            for (int i = 0; i < _tabs.Count; i++)
            {
                try
                {
                    if (Path.GetFullPath(_tabs[i].FilePath) == fullPath)
                    {
                        LoadTabState(i);
                        return;
                    }
                }
                catch {}
            }

            var tab = new TabState { FilePath = filePath };
            _tabs.Add(tab);
            _activeTabIndex = _tabs.Count - 1;
            await LoadFile(filePath);
        }

        public async Task NewTab()
        {
            SaveActiveTabState();
            var tab = new TabState { FilePath = "untitled.etlsql" };
            _tabs.Add(tab);
            _activeTabIndex = _tabs.Count - 1;
            await _evaluator.ResetSessionAsync();
            _buffer.Load(new[] { "" });
            _filePath = "untitled.etlsql";
            _isDirty = false;
            _undo.Clear();
            _renderer.ShowStatus("New tab started.");
            _renderer.ForceFullRepaint();
        }

        public async Task CloseActiveTab()
        {
            if (_tabs.Count <= 1)
            {
                if (_isDirty)
                {
                    var choice = await ShowPrompt("Save changes before closing? (y/n/c)", "");
                    if (string.IsNullOrEmpty(choice) || choice.Equals("c", StringComparison.OrdinalIgnoreCase)) return;
                    if (choice.Equals("y", StringComparison.OrdinalIgnoreCase)) { if (!await SaveScript()) return; }
                }
                await NewFile();
                return;
            }

            if (_isDirty)
            {
                var choice = await ShowPrompt("Save changes before closing tab? (y/n/c)", "");
                if (string.IsNullOrEmpty(choice) || choice.Equals("c", StringComparison.OrdinalIgnoreCase)) return;
                if (choice.Equals("y", StringComparison.OrdinalIgnoreCase)) { if (!await SaveScript()) return; }
            }

            _tabs.RemoveAt(_activeTabIndex);
            _activeTabIndex = Math.Clamp(_activeTabIndex - 1, 0, _tabs.Count - 1);
            LoadTabState(_activeTabIndex);
        }
    }

    public class TabState
    {
        public string FilePath { get; set; } = "untitled.etlsql";
        public List<string> Lines { get; set; } = new() { "" };
        public int CursorLine { get; set; }
        public int CursorColumn { get; set; }
        public int? SelectionStartLine { get; set; }
        public int? SelectionStartCol { get; set; }
        public bool IsDirty { get; set; }
        public int ScrollLine { get; set; }
        public int ScrollCol { get; set; }
        public List<EditorDiagnostic> Diagnostics { get; set; } = new();
    }
}
