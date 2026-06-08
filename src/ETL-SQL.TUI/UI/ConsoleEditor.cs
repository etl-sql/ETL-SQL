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
        private readonly Core.Functions.IFunctionRegistry? _functionRegistry;
        private readonly Core.Interfaces.ILanguageHelpRegistry? _helpRegistry;
        private readonly PortalClient _portal = new();
        private readonly Dictionary<string, IDataSource> _connections;
        // Swapped (not mutated) so a background analysis can replace the set atomically while the
        // render thread enumerates the old one — see AnalyzeAsync / ScheduleLiveAnalysis.
        private volatile List<EditorDiagnostic> _diagnostics = new();
        private int _activeDiagnosticIndex = -1;

        private System.Threading.CancellationTokenSource? _analysisCts;
        private volatile int _analysisGen;

        private readonly Services.IClipboardService _clipboard;
        private readonly SecurityService _security;
        private readonly EditorFileHandler _fileHandler;
        private readonly FileChangeTracker _fileTracker;

        private Task? _runningTask;
        private System.Threading.CancellationTokenSource? _runCts;

        private readonly WorkspaceStore _workspace = new();
        private System.Threading.CancellationTokenSource? _sessionSaveCts;

        /// <summary>True while a script execution is in flight on a background task.</summary>
        public bool IsRunning => _runningTask is { IsCompleted: false };

        /// <summary>Awaits the current background run (used by tests); completes immediately if idle.</summary>
        public Task WaitForRunAsync() => _runningTask ?? Task.CompletedTask;

        /// <summary>Requests cancellation of the running script, if any.</summary>
        public void CancelRun()
        {
            if (IsRunning) { _runCts?.Cancel(); _renderer.ShowStatus("Stopping execution…"); }
        }

        // Starts execution on a background task so the input loop stays responsive (live updates,
        // Stop key). Concurrent runs are rejected. Returns the run task so tests can await it.
        private Task StartRun(string source, string label)
        {
            if (IsRunning)
            {
                _renderer.ShowStatus("A script is already running — press Esc to stop it.");
                return Task.CompletedTask;
            }

            _runCts?.Dispose();
            _runCts = new System.Threading.CancellationTokenSource();
            var ct = _runCts.Token;
            _renderer.ExecutionRunning = true;
            _renderer.ShowStatus(label);

            _runningTask = Task.Run(async () =>
            {
                try { await ExecuteSource(source, ct); }
                finally { _renderer.ExecutionRunning = false; }
            });
            return _runningTask;
        }
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
            _fileTracker = new FileChangeTracker(new PhysicalFileSystem());
            _metadata = new MetadataManager(_evaluator, _connections);
            _renderer._sidebarPanel.SetMetadata(_metadata);
            var helpRegistry = Program.ServiceProvider.GetService<Core.Interfaces.ILanguageHelpRegistry>();
            _helpRegistry = helpRegistry;
            _functionRegistry = Program.ServiceProvider.GetService<Core.Functions.IFunctionRegistry>();
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
            _fileTracker.Record(path);
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
            EnableWindowsMouseSupport();
            try
            {
                // Perform a robust full-screen clear to purge artifacts from previous CLI statements
                try 
                { 
                    if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
                    {
                        Console.BufferHeight = Console.WindowHeight;
                    }
                    AnsiConsole.Console.Cursor.Hide();
                    // Alternative screen buffer on every platform. VT mouse tracking
                    // (?1000h/?1006h) is requested only off-Windows; on Windows the mouse
                    // is read via Win32 ReadConsoleInput (see EnableWindowsMouseSupport), so
                    // we must NOT also request VT mouse or the terminal would inject raw
                    // ESC[< byte sequences into the input stream.
                    AnsiConsole.Console.Write("\x1b[?1049h");
                    if (!OperatingSystem.IsWindows())
                        AnsiConsole.Console.Write("\x1b[?1000h\x1b[?1006h");
                    AnsiConsole.Console.Write("\x1b[H\x1b[2J\x1b[3J");
                    AnsiConsole.Console.Clear(); 
                    AnsiConsole.Console.Cursor.SetPosition(1, 1);
                    _renderer.ForceFullRepaint();
                } 
                catch { }

                // Silently restore the previous session (and offer crash recovery) before the loop.
                try { await RestoreWorkspaceAsync(); } catch { }

                _metadata.RefreshConnections(_buffer.GetText(), force: true);

                while (!_isExiting)
                {
                    // A background run mutates evaluator/renderer state concurrently; a raced
                    // render just skips a frame and the next heartbeat repaints.
                    try { _renderer.Render(this, Console.WindowWidth, Console.WindowHeight); }
                    catch { }

                    var keyOpt = await ReadKeyOrHandleMouse();

                    if (keyOpt.HasValue)
                    {
                        var key = keyOpt.Value;
                        // While a script is running, Esc stops it instead of reaching the editor.
                        if (IsRunning && key.Key == ConsoleKey.Escape && !_renderer.PromptVisible)
                        {
                            CancelRun();
                        }
                        else if (!_renderer.PromptVisible)
                        {
                            await _input.HandleKey(key);
                        }
                    }
                }
            }
            finally
            {
                // Persist the final session and clear the crash sentinel — this is a clean exit.
                try { _workspace.Save(CaptureSession()); _workspace.MarkCleanExit(Directory.GetCurrentDirectory()); } catch { }

                if (!OperatingSystem.IsWindows())
                    AnsiConsole.Console.Write("\x1b[?1000l\x1b[?1006l"); // Disable VT mouse tracking
                AnsiConsole.Console.Write("\x1b[?1049l"); // Exit alternative buffer
                AnsiConsole.Console.Cursor.Show();
                try { Console.Clear(); } catch { }
                try { Console.SetCursorPosition(0, 0); } catch { }
                try { Console.CursorVisible = true; } catch { }
                RestoreWindowsConsoleMode();
            }
        }

        /// <summary>Saves the script and serves it in the browser via the ETL-SQL `serve` command.</summary>
        public async Task ServeInBrowser()
        {
            if (_isDirty || string.IsNullOrEmpty(_filePath) || _filePath == "untitled.etlsql")
            {
                if (!await SaveScript()) { _renderer.ShowStatus("Save the report before serving."); return; }
            }

            var found = ReportLauncher.FindReportPlayer();
            if (found == null) { await ShowServeError(PlayerNotFound); return; }
            var (exe, prefix) = found.Value;
            await LaunchReportServer(ReportLauncher.BuildServeProcess(exe, prefix, Path.GetFullPath(_filePath)), "Report preview");
        }

        /// <summary>Serves every report in the current file's folder (multi-report mode).</summary>
        public async Task ServeFolderInBrowser()
        {
            if (string.IsNullOrEmpty(_filePath) || _filePath == "untitled.etlsql")
            {
                if (!await SaveScript()) { _renderer.ShowStatus("Save the file first."); return; }
            }

            string folder = Path.GetDirectoryName(Path.GetFullPath(_filePath)) ?? ".";
            var files = Directory.EnumerateFiles(folder, "*.rptsql")
                .Concat(Directory.EnumerateFiles(folder, "*.etlsql"))
                .Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0) { await ShowServeError($"No .rptsql or .etlsql reports found in {folder}."); return; }

            // A generated manifest beside the reports (paths must resolve within its directory).
            string manifestPath = Path.Combine(folder, ".etlsql-reports.json");
            var reports = files.Select(f => new { name = Path.GetFileNameWithoutExtension(f), path = f });
            await File.WriteAllTextAsync(manifestPath,
                System.Text.Json.JsonSerializer.Serialize(new { reports }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            var found = ReportLauncher.FindReportPlayer();
            if (found == null) { await ShowServeError(PlayerNotFound); return; }
            var (exe, prefix) = found.Value;
            await LaunchReportServer(ReportLauncher.BuildManifestProcess(exe, prefix, manifestPath),
                $"Serving {files.Count} report(s) from {Path.GetFileName(folder)}");
        }

        private const string PlayerNotFound =
            "Could not locate ETL-SQL.ReportPlayer. Build the solution (dotnet build ETL-SQL.slnx), then try again.";

        /// <summary>
        /// Starts the ReportPlayer process, reads its REPORT_URL line, opens the browser,
        /// records the location in Output, and surfaces any startup failure.
        /// </summary>
        private async Task LaunchReportServer(System.Diagnostics.ProcessStartInfo psi, string label)
        {
            System.Diagnostics.Process? proc;
            try { proc = System.Diagnostics.Process.Start(psi); }
            catch (Exception ex) { await ShowServeError($"Could not start the server process: {ex.Message}"); return; }
            if (proc == null) { await ShowServeError("Could not start the server process."); return; }

            var output = new System.Text.StringBuilder();
            var urlTcs = new TaskCompletionSource<string?>();
            _ = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                    {
                        lock (output) output.AppendLine(line);
                        var u = ReportLauncher.ParseReportUrl(line);
                        if (u != null) urlTcs.TrySetResult(u);
                    }
                }
                catch { }
                urlTcs.TrySetResult(null);
            });
            var stderrTask = proc.StandardError.ReadToEndAsync();

            _renderer.ShowStatus("Starting report server… (first run may build)");
            _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);

            var finished = await Task.WhenAny(urlTcs.Task, Task.Delay(TimeSpan.FromSeconds(90)));
            string? url = finished == urlTcs.Task ? urlTcs.Task.Result : null;

            if (!string.IsNullOrEmpty(url))
            {
                ReportLauncher.OpenBrowser(url);
                _renderer.AddOutput(OutputKind.Server, url);
                await ShowInfoOverlay("Serving report",
                    $"{label} is live:\n\n{url}\n\n" +
                    "Your browser should have opened. If not, Ctrl+click the link above.\n" +
                    "The server keeps running after you close this message.\n\n" +
                    "Press any key to close.",
                    "");
                return;
            }

            string err = "";
            try { if (await Task.WhenAny(stderrTask, Task.Delay(2000)) == stderrTask) err = stderrTask.Result; } catch { }
            string captured; lock (output) captured = output.ToString();
            string detail = ReportLauncher.FirstMeaningfulLine(err)
                          ?? ReportLauncher.FirstMeaningfulLine(captured)
                          ?? (proc.HasExited ? $"The server process exited with code {proc.ExitCode}." : "No URL was reported within 90 seconds.");
            await ShowServeError(detail);
        }

        private Task ShowServeError(string detail) =>
            ShowInfoOverlay("Serve failed",
                "The report preview server did not start.\n\n" +
                $"{detail}\n\n" +
                "Things to check:\n" +
                "- The file is a Report-SQL (.rptsql) report.\n" +
                "- The ReportPlayer is built (build the whole solution).\n" +
                "- Run `etl-sql serve <file>` in a terminal to see full output.\n\n" +
                "Press any key to close.",
                "");

        /// <summary>Opens the searchable command palette (Ctrl+Shift+P).</summary>
        public async Task ShowCommandPalette()
        {
            string filter = "";
            int selected = 0;
            _renderer.PaletteVisible = true;
            try
            {
                while (true)
                {
                    var matches = CommandPalette.Filter(filter);
                    selected = Math.Clamp(selected, 0, Math.Max(0, matches.Count - 1));
                    _renderer.PaletteFilter = filter;
                    _renderer.PaletteItems = matches.Select(c => (c.Title, c.Shortcut ?? "")).ToList();
                    _renderer.PaletteIndex = selected;
                    _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);

                    var keyOpt = await ReadKeyOrHandleMouse();
                    if (!keyOpt.HasValue) continue;
                    var key = keyOpt.Value;

                    if (key.Key == ConsoleKey.Escape) break;
                    if (key.Key == ConsoleKey.Enter)
                    {
                        _renderer.PaletteVisible = false;
                        if (matches.Count > 0 && selected >= 0 && selected < matches.Count)
                            await matches[selected].Run(this);
                        return;
                    }
                    if (key.Key == ConsoleKey.UpArrow) { selected = Math.Max(0, selected - 1); continue; }
                    if (key.Key == ConsoleKey.DownArrow) { selected = Math.Min(Math.Max(0, matches.Count - 1), selected + 1); continue; }
                    if (key.Key == ConsoleKey.Backspace) { if (filter.Length > 0) filter = filter.Substring(0, filter.Length - 1); selected = 0; continue; }
                    if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0') { filter += key.KeyChar; selected = 0; continue; }
                }
            }
            finally { _renderer.PaletteVisible = false; }
        }

        /// <summary>Exports the current report to Markdown next to the script.</summary>
        public Task ExportReportMarkdown() => ExportReport(pdf: false);

        /// <summary>Exports the current report to PDF next to the script.</summary>
        public Task ExportReportPdf() => ExportReport(pdf: true);

        private async Task ExportReport(bool pdf)
        {
            if (string.IsNullOrEmpty(_filePath) || _filePath == "untitled.etlsql")
            {
                if (!await SaveScript()) { _renderer.ShowStatus("Save the report before exporting."); return; }
            }

            try
            {
                _renderer.ShowStatus("Building report…");
                _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);

                await RunScript(); // populate the report context for the manifest
                var manifest = await new ETL_SQL.Reporting.ManifestBuilder(_evaluator).BuildAsync(_buffer.GetText());
                string outPath = Path.ChangeExtension(Path.GetFullPath(_filePath), pdf ? ".pdf" : ".md");

                if (pdf)
                    await File.WriteAllBytesAsync(outPath, new ETL_SQL.Reporting.PdfExporter().Export(manifest));
                else
                    await File.WriteAllTextAsync(outPath, new ETL_SQL.Reporting.MarkdownRenderer().Render(manifest));

                _renderer.AddOutput(pdf ? OutputKind.Pdf : OutputKind.Markdown, outPath);
                _renderer.ShowStatus($"Exported: {outPath} — see the Output tab (F4)");
            }
            catch (Exception ex)
            {
                await ShowInfoOverlay("Export failed",
                    $"Could not export the report.\n\n{ex.Message}\n\nPress any key to close.", "");
            }
        }

        /// <summary>Publishes the current report to the Report Portal (mirrors the VS Code flow).</summary>
        public async Task PublishToPortal()
        {
            if (string.IsNullOrEmpty(_filePath) || _filePath == "untitled.etlsql")
            {
                if (!await SaveScript()) { _renderer.ShowStatus("Save the report before publishing."); return; }
            }
            string full = Path.GetFullPath(_filePath);
            var cfg = PortalConfig.Load();

            // 1. Portal URL (first-time setup; stored thereafter).
            string? url = cfg.Url;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = await ShowPrompt("Report Portal URL (e.g. http://localhost:5001)", "");
                if (string.IsNullOrWhiteSpace(url)) { _renderer.ShowStatus("Publish cancelled."); return; }
                url = url.Trim().TrimEnd('/');
                cfg.Url = url; cfg.Save();
            }

            // 2. Token (cached until expiry, else username/password login).
            string? token = cfg.HasValidToken ? cfg.Token : null;
            if (token == null)
            {
                var user = await ShowPrompt("Portal username", "");
                if (user == null) return;
                var pass = await ShowPrompt("Portal password", "", isSecret: true);
                if (pass == null) return;
                token = await _portal.LoginAsync(url, user, pass);
                if (string.IsNullOrEmpty(token)) { await PublishError("Login failed — check the URL and credentials."); return; }
                cfg.Token = token; cfg.Expiry = DateTime.UtcNow.AddMinutes(55); cfg.Save();
            }

            _renderer.ShowStatus("Uploading to portal…");
            _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);

            // 3. Upload the script.
            string base64 = Convert.ToBase64String(await File.ReadAllBytesAsync(full));
            var (ok, scriptPath, upErr) = await _portal.UploadScriptAsync(url, token, Path.GetFileName(full), base64);
            if (!ok) { await PublishError($"Upload failed: {upErr}"); return; }

            // 4. Report name.
            var name = await ShowPrompt("Report name", Path.GetFileNameWithoutExtension(full));
            if (string.IsNullOrWhiteSpace(name)) return;

            // 5. Destination folder.
            var folders = await _portal.GetFoldersAsync(url, token);
            if (folders.Count == 0) { await PublishError("No folders available. You need Manage permission on at least one folder."); return; }
            int pick = await ShowChooser("Destination folder", folders.ConvertAll(f => f.path));
            if (pick < 0) return;

            // 6. Description (optional).
            var description = await ShowPrompt("Description (optional)", "") ?? "";

            // 7. Register the report.
            var (status, msg) = await _portal.CreateReportAsync(url, token, folders[pick].id, name.Trim(), scriptPath!, description);
            if (status == 200 || status == 201)
            {
                _renderer.AddOutput(OutputKind.Portal, url);
                _renderer.ShowStatus($"Published '{name.Trim()}' to the portal — see Output (F4).");
            }
            else if (status == 401)
            {
                cfg.Token = null; cfg.Save();
                await PublishError("Session expired. Run Publish again to sign in.");
            }
            else if (status == 403)
                await PublishError("Insufficient permissions on the selected folder.");
            else
                await PublishError(msg ?? $"HTTP {status}");
        }

        /// <summary>Forgets the stored portal URL and token so Publish re-runs first-time setup.</summary>
        public void ResetPortalConnection()
        {
            PortalConfig.Clear();
            _renderer.ShowStatus("Portal connection reset — Publish will ask for the URL and login again.");
        }

        /// <summary>Rolls back every open transaction after confirming with the user; reports to Messages.</summary>
        public async Task RollbackAllTransactionsCommand()
        {
            int count = _evaluator.TranCount;
            if (count == 0)
            {
                _renderer.ShowStatus("No active transactions to roll back.");
                return;
            }

            var answer = await ShowPrompt($"Roll back {count} open transaction(s)? This cannot be undone. (y/n)", "");
            if (string.IsNullOrEmpty(answer) || !answer.TrimStart().StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                _renderer.ShowStatus("Rollback cancelled.");
                return;
            }

            try
            {
                await _evaluator.RollbackAllTransactions();
                _evaluator.Log($"[ROLLBACK] Rolled back {count} open transaction(s).", ConsoleColor.Yellow);
                _renderer.ShowStatus($"Rolled back {count} transaction(s).");
            }
            catch (Exception ex)
            {
                // Surface failure without leaking provider-specific detail.
                _evaluator.Log("[ROLLBACK ERROR] Failed to roll back all transactions.", ConsoleColor.Red);
                _renderer.ShowStatus($"Rollback failed: {ex.Message}");
            }
        }

        private Task PublishError(string detail) =>
            ShowInfoOverlay("Publish failed", $"{detail}\n\nPress any key to close.", "");

        /// <summary>A modal, filterable single-choice list (reuses the palette overlay). Returns the chosen index or -1.</summary>
        private async Task<int> ShowChooser(string title, IReadOnlyList<string> items)
        {
            string filter = "";
            int selected = 0;
            _renderer.PaletteVisible = true;
            try
            {
                while (true)
                {
                    var matches = new List<(string s, int i)>();
                    for (int i = 0; i < items.Count; i++)
                        if (filter.Length == 0 || items[i].ToLowerInvariant().Contains(filter.ToLowerInvariant()))
                            matches.Add((items[i], i));
                    selected = Math.Clamp(selected, 0, Math.Max(0, matches.Count - 1));
                    _renderer.PaletteFilter = $"{title}: {filter}";
                    _renderer.PaletteItems = matches.ConvertAll(m => (m.s, ""));
                    _renderer.PaletteIndex = selected;
                    _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);

                    var keyOpt = await ReadKeyOrHandleMouse();
                    if (!keyOpt.HasValue) continue;
                    var key = keyOpt.Value;
                    if (key.Key == ConsoleKey.Escape) return -1;
                    if (key.Key == ConsoleKey.Enter) return matches.Count > 0 ? matches[selected].i : -1;
                    if (key.Key == ConsoleKey.UpArrow) { selected = Math.Max(0, selected - 1); continue; }
                    if (key.Key == ConsoleKey.DownArrow) { selected = Math.Min(Math.Max(0, matches.Count - 1), selected + 1); continue; }
                    if (key.Key == ConsoleKey.Backspace) { if (filter.Length > 0) filter = filter.Substring(0, filter.Length - 1); selected = 0; continue; }
                    if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0') { filter += key.KeyChar; selected = 0; continue; }
                }
            }
            finally { _renderer.PaletteVisible = false; }
        }

        private OutputEntry? SelectedOutput()
        {
            var list = _renderer.OutputEntries;
            int i = _renderer.OutputSelectedIndex;
            return (i >= 0 && i < list.Count) ? list[i] : null;
        }

        /// <summary>Opens the selected Output entry: a URL in the browser, a file in the OS file manager.</summary>
        public Task OpenSelectedOutput()
        {
            var e = SelectedOutput();
            if (e == null) return Task.CompletedTask;
            if (e.IsUrl) ReportLauncher.OpenBrowser(e.Location);
            else RevealInFileManager(e.Location);
            return Task.CompletedTask;
        }

        /// <summary>Copies the selected Output location to the clipboard.</summary>
        public async Task CopySelectedOutput()
        {
            var e = SelectedOutput();
            if (e == null) return;
            try { await _clipboard.SetTextAsync(e.Location); _renderer.ShowStatus("Copied location to clipboard."); }
            catch { _renderer.ShowStatus("Copy failed."); }
        }

        private static void RevealInFileManager(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                else
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = System.IO.Path.GetDirectoryName(path) ?? path, UseShellExecute = true });
            }
            catch { }
        }

        public async Task HandleExit()
        {
            SaveActiveTabState();
            int dirty = CountDirtyTabs();
            if (dirty > 0)
            {
                string label = dirty == 1 ? "1 tab has" : $"{dirty} tabs have";
                var choice = await ShowPrompt($"{label} unsaved changes. Save all / Discard / Cancel? (s/d/c)", "");
                if (string.IsNullOrWhiteSpace(choice)) return; // cancel on empty/Esc
                char c = char.ToLowerInvariant(choice.Trim()[0]);
                if (c == 's') { if (!await SaveAllTabs()) return; }
                else if (c != 'd') return; // 'c' or anything unrecognized → cancel (safe default)
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
                var keyOpt = await ReadKeyOrHandleMouse();
                if (keyOpt.HasValue)
                {
                    await _input.HandlePromptKey(keyOpt.Value);
                }
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
        public async Task ShowHelp()
        {
            _renderer.HelpVisible = true;
            _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);
            
            while (true)
            {
                var keyOpt = await ReadKeyOrHandleMouse();
                if (keyOpt.HasValue)
                {
                    break;
                }
            }

            _renderer.HelpVisible = false;
        }

        private string CurrentLineText() =>
            (_buffer.CursorLine >= 0 && _buffer.CursorLine < _buffer.Lines.Count) ? _buffer.Lines[_buffer.CursorLine] : "";

        /// <summary>Shows function/keyword help for the word at the cursor (Shift+F1).</summary>
        public Task ShowHelpAtCursor()
        {
            string? help = InfoAtCursor.BuildHelp(CurrentLineText(), _buffer.CursorColumn, _functionRegistry, _helpRegistry, out string title);
            return ShowInfoOverlay(title, help, "No help at cursor.");
        }

        /// <summary>Shows lineage (and graph) for the identifier at the cursor (Shift+F2).</summary>
        public Task ShowLineageAtCursor()
        {
            string? lineage = InfoAtCursor.BuildLineage(_evaluator.LineageTracker, CurrentLineText(), _buffer.CursorLine, _buffer.CursorColumn, out string title);
            return ShowInfoOverlay(title, lineage,
                "No lineage captured. Lineage is tracked when a script writes to a target (SELECT … INTO, INSERT INTO, CREATE TABLE).");
        }

        /// <summary>Shows a scrollable, dismiss-on-key overlay; status-only when content is empty.</summary>
        private async Task ShowInfoOverlay(string title, string? content, string emptyMessage)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                _renderer.ShowStatus(emptyMessage);
                return;
            }

            _renderer.InfoTitle = title;
            _renderer.InfoContent = content;
            _renderer.InfoScrollRow = 0;
            _renderer.InfoVisible = true;
            try
            {
                while (true)
                {
                    _renderer.Render(this, Console.WindowWidth, Console.WindowHeight);
                    var keyOpt = await ReadKeyOrHandleMouse();
                    if (!keyOpt.HasValue) continue; // mouse wheel scrolled — re-render

                    switch (keyOpt.Value.Key)
                    {
                        case ConsoleKey.UpArrow:   _renderer.InfoScrollRow--; continue;
                        case ConsoleKey.DownArrow: _renderer.InfoScrollRow++; continue;
                        case ConsoleKey.PageUp:    _renderer.InfoScrollRow -= 10; continue;
                        case ConsoleKey.PageDown:  _renderer.InfoScrollRow += 10; continue;
                        case ConsoleKey.Home:      _renderer.InfoScrollRow = 0; continue;
                        case ConsoleKey.End:       _renderer.InfoScrollRow = int.MaxValue; continue;
                    }
                    break; // any other key closes
                }
            }
            finally
            {
                _renderer.InfoVisible = false;
            }
        }

        private readonly Queue<ConsoleKeyInfo> _pendingKeys = new();

        private async Task<ConsoleKeyInfo?> ReadKeyOrHandleMouse()
        {
            if (_pendingKeys.Count > 0)
            {
                return _pendingKeys.Dequeue();
            }

            // On Windows we read raw INPUT_RECORDs so we get both proper key decoding and
            // mouse events without ENABLE_VIRTUAL_TERMINAL_INPUT (which breaks special keys).
            if (OperatingSystem.IsWindows() && _consoleModeModified)
            {
                return await ReadInputWindows();
            }

            // Heartbeat while a run or a live-analysis pass is in flight so the loop repaints
            // updates without blocking on input.
            if (_renderer.ExecutionRunning || _renderer.LiveAnalysisPending)
            {
                try { if (!Console.KeyAvailable) { await Task.Delay(80); return null; } }
                catch (InvalidOperationException) { await Task.Delay(80); return null; }
            }

            ConsoleKeyInfo key;
            try
            {
                key = Console.ReadKey(true);
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(100);
                return null;
            }

            if (key.Key == ConsoleKey.Escape || key.KeyChar == '\x1b')
            {
                var sequenceKeys = new List<ConsoleKeyInfo>();
                var sb = new System.Text.StringBuilder();
                var sw = System.Diagnostics.Stopwatch.StartNew();

                while (true)
                {
                    try
                    {
                        if (Console.KeyAvailable)
                        {
                            var nextKey = Console.ReadKey(true);
                            sequenceKeys.Add(nextKey);
                            sb.Append(nextKey.KeyChar);
                            sw.Restart();
                        }
                        else
                        {
                            if (sw.ElapsedMilliseconds >= 30)
                            {
                                break;
                            }
                            System.Threading.Thread.Sleep(1);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                }

                string seq = sb.ToString();
                if (!string.IsNullOrEmpty(seq))
                {
                    if (seq.StartsWith("[<"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(seq, @"^\[<(\d+);(\d+);(\d+)([Mm])");
                        if (match.Success)
                        {
                            int button = int.Parse(match.Groups[1].Value);
                            int mouseX = int.Parse(match.Groups[2].Value) - 1;
                            int mouseY = int.Parse(match.Groups[3].Value) - 1;
                            bool isRelease = match.Groups[4].Value == "m";

                            if (_renderer.ModalOverlayVisible)
                            {
                                // Any click dismisses the help/info overlay.
                                if (!isRelease) return new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false);
                                return null;
                            }

                            await _renderer.HandleMouseClick(button, mouseX, mouseY, isRelease, this);
                            return null;
                        }
                    }

                    if (!seq.StartsWith("[") && !seq.StartsWith("O"))
                    {
                        foreach (var k in sequenceKeys)
                        {
                            _pendingKeys.Enqueue(k);
                        }
                        return key;
                    }
                    return null;
                }
            }

            return key;
        }

        // ── Windows raw input (ReadConsoleInputW) ──────────────────────────────
        // Decodes keys exactly as Console.ReadKey would (from KEY_EVENT records) and
        // dispatches mouse clicks/wheel from MOUSE_EVENT records. Mouse movement and
        // button-up events are consumed silently to avoid needless re-renders.
        private readonly INPUT_RECORD[] _inputRecordBuffer = new INPUT_RECORD[1];
        private uint _prevMouseButtons = 0;
        private bool _mouseDragging = false;
        private int _dragAnchorLine = 0;
        private int _dragAnchorCol = 0;

        private async Task<ConsoleKeyInfo?> ReadInputWindows()
        {
            IntPtr hStdin = GetStdHandle(STD_INPUT_HANDLE);

            while (true)
            {
                // While a script runs or a live-analysis pass is pending, wake periodically (even
                // with no input) so the loop repaints execution/message/diagnostic updates;
                // otherwise block until input arrives.
                bool tick = _renderer.ExecutionRunning || _renderer.LiveAnalysisPending;
                if (WaitForSingleObject(hStdin, tick ? 80u : INFINITE) == WAIT_TIMEOUT)
                {
                    return null;
                }

                if (!ReadConsoleInputW(hStdin, _inputRecordBuffer, 1, out uint read) || read == 0)
                {
                    await Task.Delay(10);
                    return null;
                }

                var record = _inputRecordBuffer[0];

                if (record.EventType == KEY_EVENT)
                {
                    var k = record.KeyEvent;
                    if (k.bKeyDown == 0) continue;            // ignore key-up
                    ushort vk = k.wVirtualKeyCode;
                    if (IsModifierKey(vk)) continue;          // ignore lone modifier presses

                    bool shift = (k.dwControlKeyState & SHIFT_PRESSED) != 0;
                    bool alt   = (k.dwControlKeyState & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED)) != 0;
                    bool ctrl  = (k.dwControlKeyState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0;

                    return new ConsoleKeyInfo((char)k.UnicodeChar, (ConsoleKey)vk, shift, alt, ctrl);
                }

                if (record.EventType == MOUSE_EVENT)
                {
                    var m = record.MouseEvent;
                    uint left = m.dwButtonState & FROM_LEFT_1ST_BUTTON_PRESSED;

                    if ((m.dwEventFlags & MOUSE_WHEELED) != 0)
                    {
                        short delta = (short)(m.dwButtonState >> 16);
                        if (_renderer.InfoVisible)
                        {
                            _renderer.InfoScrollRow += delta > 0 ? -3 : 3; // wheel scrolls the info overlay
                            return null;
                        }
                        if (_renderer.HelpVisible) return null; // ignore wheel while help is open
                        await _renderer.HandleMouseClick(delta > 0 ? 64 : 65, m.MousePositionX, m.MousePositionY, false, this);
                        return null;
                    }

                    if ((m.dwEventFlags & MOUSE_MOVED) != 0)
                    {
                        _prevMouseButtons = m.dwButtonState;
                        if (_mouseDragging && left != 0)
                        {
                            // Extend the editor selection from the press anchor to the cursor.
                            _renderer.DragExtendSelection(m.MousePositionX, m.MousePositionY, this, _dragAnchorLine, _dragAnchorCol);
                            return null; // re-render the growing selection
                        }
                        continue; // plain movement — no redraw
                    }

                    // Button press / release.
                    uint prevLeft = _prevMouseButtons & FROM_LEFT_1ST_BUTTON_PRESSED;
                    _prevMouseButtons = m.dwButtonState;

                    if (left != 0 && prevLeft == 0) // left pressed
                    {
                        if (_renderer.ModalOverlayVisible)
                            return new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false); // any click dismisses the overlay
                        await _renderer.HandleMouseClick(0, m.MousePositionX, m.MousePositionY, false, this);
                        if (_renderer.Focus == EditorFocus.Editor)
                        {
                            _mouseDragging = true;
                            _dragAnchorLine = _buffer.CursorLine;
                            _dragAnchorCol = _buffer.CursorColumn;
                        }
                        return null;
                    }
                    if (left == 0 && prevLeft != 0) // left released
                    {
                        _mouseDragging = false;
                        return null;
                    }

                    continue;
                }

                if (record.EventType == WINDOW_BUFFER_SIZE_EVENT)
                {
                    _renderer.ForceFullRepaint();
                    return null; // let the main loop re-render at the new size
                }

                // Focus / menu / other events — ignore and keep reading.
            }
        }

        private static bool IsModifierKey(ushort vk)
        {
            // VK_SHIFT 0x10, VK_CONTROL 0x11, VK_MENU 0x12, VK_CAPITAL 0x14,
            // VK_LWIN 0x5B, VK_RWIN 0x5C, VK_NUMLOCK 0x90, VK_SCROLL 0x91
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14
                || vk == 0x5B || vk == 0x5C || vk == 0x90 || vk == 0x91;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool ReadConsoleInputW(IntPtr hConsoleInput, [System.Runtime.InteropServices.Out] INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

        private const ushort KEY_EVENT = 0x0001;
        private const ushort MOUSE_EVENT = 0x0002;
        private const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;
        private const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;
        private const uint MOUSE_MOVED = 0x0001;
        private const uint MOUSE_WHEELED = 0x0004;
        private const uint RIGHT_ALT_PRESSED = 0x0001;
        private const uint LEFT_ALT_PRESSED = 0x0002;
        private const uint RIGHT_CTRL_PRESSED = 0x0004;
        private const uint LEFT_CTRL_PRESSED = 0x0008;
        private const uint SHIFT_PRESSED = 0x0010;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct INPUT_RECORD
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public ushort EventType;
            [System.Runtime.InteropServices.FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
            [System.Runtime.InteropServices.FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KEY_EVENT_RECORD
        {
            public int bKeyDown;
            public ushort wRepeatCount;
            public ushort wVirtualKeyCode;
            public ushort wVirtualScanCode;
            public ushort UnicodeChar;
            public uint dwControlKeyState;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MOUSE_EVENT_RECORD
        {
            public short MousePositionX;
            public short MousePositionY;
            public uint dwButtonState;
            public uint dwControlKeyState;
            public uint dwEventFlags;
        }

        private uint _originalInputMode;
        private bool _consoleModeModified = false;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        private const uint WAIT_TIMEOUT = 0x00000102;
        private const uint INFINITE = 0xFFFFFFFF;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        private const int STD_INPUT_HANDLE = -10;
        private const uint ENABLE_MOUSE_INPUT = 0x0010;
        private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
        private const uint ENABLE_EXTENDED_FLAGS = 0x0080;
        private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

        private void EnableWindowsMouseSupport()
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                IntPtr hStdin = GetStdHandle(STD_INPUT_HANDLE);
                if (hStdin == IntPtr.Zero || hStdin == new IntPtr(-1)) return;

                if (GetConsoleMode(hStdin, out uint mode))
                {
                    _originalInputMode = mode;
                    _consoleModeModified = true;

                    // Enable Win32 mouse INPUT_RECORDs; keep raw key decoding intact.
                    // Critically, leave ENABLE_VIRTUAL_TERMINAL_INPUT OFF: with it on, special
                    // and control keys (Backspace, Tab, Ctrl+Q, arrows) arrive as raw VT bytes
                    // that .NET's ReadKey cannot turn back into ConsoleKey values, which silently
                    // breaks those shortcuts. We read input via ReadConsoleInputW instead.
                    uint newMode = mode;
                    newMode |= ENABLE_MOUSE_INPUT;
                    newMode |= ENABLE_EXTENDED_FLAGS;
                    newMode &= ~ENABLE_QUICK_EDIT_MODE;
                    newMode &= ~ENABLE_VIRTUAL_TERMINAL_INPUT;

                    SetConsoleMode(hStdin, newMode);
                }
            }
            catch
            {
                // Ignore errors in environments where console handle is not accessible
            }
        }

        private void RestoreWindowsConsoleMode()
        {
            if (!OperatingSystem.IsWindows() || !_consoleModeModified) return;
            try
            {
                IntPtr hStdin = GetStdHandle(STD_INPUT_HANDLE);
                if (hStdin != IntPtr.Zero && hStdin != new IntPtr(-1))
                {
                    SetConsoleMode(hStdin, _originalInputMode);
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>Saves the current buffer state (text + cursor) for undo.</summary>
        public void SaveUndoState() => _undo.SaveState(_buffer.Lines, _buffer.CursorLine, _buffer.CursorColumn);

        /// <summary>Restores the previous buffer state, including the cursor position.</summary>
        public void Undo() => ApplySnapshot(_undo.Undo(_buffer.Lines, _buffer.CursorLine, _buffer.CursorColumn));

        /// <summary>Restores the state that was undone, including the cursor position.</summary>
        public void Redo() => ApplySnapshot(_undo.Redo(_buffer.Lines, _buffer.CursorLine, _buffer.CursorColumn));

        /// <summary>Loads a snapshot's text and restores its cursor, clamped to the restored buffer.</summary>
        private void ApplySnapshot(EditorSnapshot? snap)
        {
            if (snap == null) return;
            _buffer.Load(snap.Lines); // resets cursor to 0,0 — reposition below
            _buffer.CursorLine = Math.Clamp(snap.CursorLine, 0, _buffer.Lines.Count - 1);
            _buffer.CursorColumn = Math.Clamp(snap.CursorColumn, 0, _buffer.Lines[_buffer.CursorLine].Length);
        }

        /// <summary>Marks the current document as modified and re-runs live diagnostics (debounced).</summary>
        public void MarkDirty()
        {
            _isDirty = true;
            ScheduleLiveAnalysis();
            ScheduleSessionSave();
        }

        /// <summary>The current buffer text (used by the schema explorer to re-scan connections).</summary>
        public string CurrentScriptText => _buffer.GetText();

        /// <summary>Inserts text at the cursor (used by the schema explorer's insert-at-cursor action).</summary>
        public void InsertAtCursor(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            SaveUndoState();
            _buffer.Paste(text);
            MarkDirty();
        }

        /// <summary>Automatically formats the current script buffer.</summary>
        public void FormatScript()
        {
            var text = _buffer.GetText();
            SaveUndoState();
            _buffer.Load(SqlFormatter.Format(text).Split('\n'));
            MarkDirty();
        }

        /// <summary>Prompts for a search term (pre-filled with the last one), highlights all
        /// matches, and jumps to the next occurrence. F3/Shift+F3 then repeat; Esc clears.</summary>
        public async Task Find()
        {
            var target = await ShowPrompt("Find", _renderer.FindTerm ?? "");
            if (string.IsNullOrEmpty(target)) return;
            _renderer.FindTerm = target;
            if (TryFindNext(target))
                _renderer.ShowStatus($"Find '{target}' — F3 next · Shift+F3 prev · Esc clear");
            else
                _renderer.ShowStatus($"'{target}' not found.");
        }

        /// <summary>Clears the active find highlight.</summary>
        public void ClearFind()
        {
            _renderer.FindTerm = null;
            _renderer.ShowStatus("Find cleared.");
        }

        /// <summary>Moves the cursor to the next case-insensitive match, wrapping to the top when needed.</summary>
        public bool TryFindNext(string? target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            var text = _buffer.GetText();
            int start = _buffer.GetFlatPosition(_buffer.CursorLine, _buffer.CursorColumn);
            int index = text.IndexOf(target, Math.Min(start + 1, text.Length), StringComparison.OrdinalIgnoreCase);
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

        /// <summary>Moves the cursor to the previous case-insensitive match, wrapping to the bottom.</summary>
        public bool TryFindPrev(string? target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            var text = _buffer.GetText();
            int caret = _buffer.GetFlatPosition(_buffer.CursorLine, _buffer.CursorColumn);

            int chosen = -1, last = -1;
            for (int i = text.IndexOf(target, 0, StringComparison.OrdinalIgnoreCase);
                 i != -1;
                 i = text.IndexOf(target, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                last = i;
                if (i < caret) chosen = i; // matches scanned ascending → keep the last one before the caret
            }
            if (chosen == -1) chosen = last; // none before caret → wrap to the final match
            if (chosen == -1) return false;

            var pos = _buffer.GetLineColFromFlat(chosen);
            _buffer.CursorLine = pos.line;
            _buffer.CursorColumn = pos.col;
            return true;
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

        /// <summary>Starts executing the entire script (non-blocking). Returns the run task.</summary>
        public Task RunScript() => StartRun(_buffer.GetText(), "Executing script…");

        /// <summary>Starts executing only the currently selected text (non-blocking).</summary>
        public Task RunSelectedText()
        {
            var selectedText = _buffer.GetSelectedText();
            if (string.IsNullOrEmpty(selectedText))
            {
                _renderer.ShowStatus("No text selected.");
                return Task.CompletedTask;
            }
            return StartRun(selectedText, "Executing selection…");
        }

        /// <summary>Starts executing only the line at the current cursor position (non-blocking).</summary>
        public Task RunStatementAtCursor()
        {
            var currentLine = _buffer.Lines[_buffer.CursorLine];
            return StartRun(currentLine, $"Executing line {_buffer.CursorLine + 1}…");
        }

        private async Task ExecuteSource(string source, System.Threading.CancellationToken ct = default)
        {
            try
            {
                _activeDiagnosticIndex = -1;
                _renderer.IsBottomMaximized = false;
                var totalSw = System.Diagnostics.Stopwatch.StartNew();

                // Lex/parse/lint, logging diagnostics to Messages, and publish them for the gutter.
                var (script, diags) = await AnalyzeAsync(source, logToMessages: true, ct);
                _diagnostics = diags;

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
                    await _evaluator.Evaluate(script, ct);
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
            catch (OperationCanceledException)
            {
                _evaluator.Log("[STOPPED] Execution cancelled by user.", ConsoleColor.Yellow);
                _renderer.ShowStatus("Execution stopped.");
            }
            catch (Exception ex)
            {
                _evaluator.Log($"[ERROR] {ex.Message}", ConsoleColor.Red);
                _renderer.ShowStatus($"Error: {ex.Message}");
            }
            finally
            {
                _renderer.MessageScrollRow = int.MaxValue; // Auto-scroll to latest messages
                // The interactive loop repaints on its heartbeat; rendering here would race it
                // from the background thread. In headless (tests) there is no loop, so paint once.
                if (_renderer.Headless) RenderCurrent();
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

        /// <summary>
        /// Lexes, parses and lints <paramref name="source"/> into a sorted diagnostics list — no
        /// evaluator, no execution. Shared by F5 (logs to Messages) and the live debounced pass
        /// (silent). Returns the parsed script so the caller can decide whether to execute.
        /// </summary>
        internal async Task<(Script script, List<EditorDiagnostic> diagnostics)> AnalyzeAsync(
            string source, bool logToMessages, System.Threading.CancellationToken ct = default)
        {
            var diagnostics = new List<EditorDiagnostic>();

            var lexSw = System.Diagnostics.Stopwatch.StartNew();
            var tokens = new Lexer(source).Tokenize();
            lexSw.Stop();
            _evaluator.LastLexTimeMs = lexSw.ElapsedMilliseconds;

            var parseSw = System.Diagnostics.Stopwatch.StartNew();
            var script = new Parser(tokens).Parse();
            parseSw.Stop();
            _evaluator.LastParseTimeMs = parseSw.ElapsedMilliseconds;

            foreach (var diag in script.Diagnostics)
            {
                diagnostics.Add(new EditorDiagnostic("PARSER", diag.Severity.ToString(), diag.Message, Math.Max(1, diag.Line), Math.Max(1, diag.Column)));
                if (logToMessages)
                    _evaluator.Log($"[PARSER {diag.Severity}] {diag.Message} at line {diag.Line}, col {diag.Column}", diag.Severity == DiagnosticSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow);
            }

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
            ct.ThrowIfCancellationRequested();

            foreach (var res in lintResults)
            {
                diagnostics.Add(new EditorDiagnostic("LINT", res.Severity.ToString(), res.Message, Math.Max(1, res.LineNumber), Math.Max(1, res.ColumnNumber)));
                if (logToMessages)
                    _evaluator.Log($"[LINT {res.Severity}] {res.Message} at line {res.LineNumber}, col {res.ColumnNumber}", res.Severity == LintSeverity.Error ? ConsoleColor.Red : ConsoleColor.Yellow);
            }

            diagnostics.Sort(CompareDiagnostics);
            return (script, diagnostics);
        }

        private static int CompareDiagnostics(EditorDiagnostic left, EditorDiagnostic right)
        {
            int line = left.Line.CompareTo(right.Line);
            if (line != 0) return line;
            int col = left.Column.CompareTo(right.Column);
            if (col != 0) return col;
            return string.Compare(left.Source, right.Source, StringComparison.Ordinal);
        }

        /// <summary>
        /// Debounced, cancellable static analysis after an edit: refreshes diagnostics/gutter
        /// markers without running the script. Each edit cancels the pending pass. Skipped while
        /// headless (tests) or a run is in flight.
        /// </summary>
        public void ScheduleLiveAnalysis()
        {
            if (_renderer.Headless || IsRunning) return;

            _analysisCts?.Cancel();
            _analysisCts = new System.Threading.CancellationTokenSource();
            var ct = _analysisCts.Token;
            int gen = ++_analysisGen;
            var text = _buffer.GetText();
            _renderer.LiveAnalysisPending = true; // wakes the input loop so new markers paint

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(350, ct); // debounce a burst of keystrokes
                    var (_, diags) = await AnalyzeAsync(text, logToMessages: false, ct);
                    ct.ThrowIfCancellationRequested();
                    _diagnostics = diags;        // atomic reference swap
                    _activeDiagnosticIndex = -1;
                }
                catch (OperationCanceledException) { }
                catch { /* never let a background analysis crash the app */ }
                finally
                {
                    // Only the latest scheduled pass clears the wake flag (a cancelled earlier pass
                    // must not stop the loop from repainting the newest results).
                    if (gen == _analysisGen) _renderer.LiveAnalysisPending = false;
                }
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

            // Guard against clobbering changes another program made since we loaded/saved.
            if (_fileTracker.HasChangedExternally(_filePath))
            {
                var choice = (await ShowPrompt(
                    $"{Path.GetFileName(_filePath)} changed on disk. Overwrite / Reload / Cancel? (o/r/c)", ""))
                    ?.Trim().ToLowerInvariant();
                if (choice == "r")
                {
                    await LoadFile(_filePath);
                    _renderer.ShowStatus("Reloaded from disk — your unsaved edits were discarded.");
                    return false;
                }
                if (choice != "o")
                {
                    _renderer.ShowStatus("Save cancelled.");
                    return false;
                }
            }

            var text = _buffer.GetText();
            bool success = await _fileHandler.SaveAsync(_filePath, text, ShowPrompt);

            if (success)
            {
                _isDirty = false;
                _fileTracker.Record(_filePath);
                _renderer.ShowStatus($"Saved to {_filePath}");
                _renderer._sidebarPanel.Initialize(_filePath);
                ScheduleSessionSave();
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

        // ── Workspace persistence (silent session restore + crash recovery) ─────────────

        /// <summary>Snapshots the open tabs + cursors for persistence. Dirty buffers carry a
        /// recovery snapshot — except those containing secrets, which are never written.</summary>
        private WorkspaceSession CaptureSession()
        {
            var s = new WorkspaceSession
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                ActiveTab = _activeTabIndex,
                SavedUtc = DateTime.UtcNow
            };

            for (int i = 0; i < _tabs.Count; i++)
            {
                bool active = i == _activeTabIndex;
                var t = _tabs[i];
                string filePath = active ? _filePath : t.FilePath;
                bool dirty = active ? _isDirty : t.IsDirty;
                var lines = active ? _buffer.Lines : t.Lines;

                string? recovery = null;
                if (dirty && lines != null)
                {
                    string text = string.Join("\n", lines);
                    // Honor "do not persist decrypted script text": skip snapshots holding secrets.
                    if (!_security.RequiresSavePassword(text)) recovery = text;
                }

                s.Tabs.Add(new WorkspaceTab
                {
                    FilePath = filePath,
                    IsDirty = dirty,
                    CursorLine = active ? _buffer.CursorLine : t.CursorLine,
                    CursorColumn = active ? _buffer.CursorColumn : t.CursorColumn,
                    ScrollLine = active ? _renderer.ScrollLine : t.ScrollLine,
                    ScrollCol = active ? _renderer.ScrollCol : t.ScrollCol,
                    RecoveryText = recovery
                });
            }
            return s;
        }

        /// <summary>Debounced background write of the workspace session (skipped while headless).</summary>
        public void ScheduleSessionSave()
        {
            if (_renderer.Headless) return;
            var session = CaptureSession(); // capture on the UI thread; only the write is deferred
            _sessionSaveCts?.Cancel();
            _sessionSaveCts = new System.Threading.CancellationTokenSource();
            var ct = _sessionSaveCts.Token;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(1500, ct); _workspace.Save(session); }
                catch { }
            });
        }

        /// <summary>
        /// Silently restores the previous session's tabs/cursors for this directory, and offers to
        /// recover unsaved buffers if the last run crashed. The launched file is kept open + active.
        /// </summary>
        public async Task RestoreWorkspaceAsync()
        {
            string cwd = Directory.GetCurrentDirectory();
            bool unclean = _workspace.WasUncleanShutdown(cwd);
            var session = _workspace.Load(cwd);
            _workspace.MarkRunning(cwd);

            if (session == null || session.Tabs.Count == 0) return;

            bool recover = false;
            if (unclean && session.Tabs.Exists(t => !string.IsNullOrEmpty(t.RecoveryText)))
            {
                var ans = await ShowPrompt("Recover unsaved changes from your last session? (y/n)", "");
                recover = !string.IsNullOrEmpty(ans) && ans.TrimStart().StartsWith("y", StringComparison.OrdinalIgnoreCase);
            }

            string launched = _filePath; // opened by InitializeAsync — keep it available
            var newTabs = new List<TabState>();
            foreach (var wt in session.Tabs)
            {
                var ts = await BuildTabFromAsync(wt, recover);
                if (ts != null) newTabs.Add(ts);
            }
            if (newTabs.Count == 0) return;

            _tabs.Clear();
            _tabs.AddRange(newTabs);
            _activeTabIndex = Math.Clamp(session.ActiveTab, 0, _tabs.Count - 1);

            // Make sure the explicitly launched file is present and focused.
            if (!string.IsNullOrEmpty(launched) && launched != "untitled.etlsql")
            {
                int existing = _tabs.FindIndex(t => SamePath(t.FilePath, launched));
                if (existing >= 0) _activeTabIndex = existing;
                else if (File.Exists(launched))
                {
                    _tabs.Add(new TabState { FilePath = launched, Lines = (await _fileHandler.LoadAsync(launched, ShowPrompt)).lines.ToList() });
                    _activeTabIndex = _tabs.Count - 1;
                }
            }

            LoadTabState(_activeTabIndex);
            _renderer._sidebarPanel.Initialize(_filePath);
            _renderer.ShowStatus(recover ? "Recovered unsaved changes from your last session." : "Restored previous session.");
        }

        private static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private async Task<TabState?> BuildTabFromAsync(WorkspaceTab wt, bool recover)
        {
            string[] lines;
            bool dirty = false;

            if (recover && !string.IsNullOrEmpty(wt.RecoveryText))
            {
                lines = wt.RecoveryText.Split('\n');
                dirty = true;
            }
            else if (!string.IsNullOrEmpty(wt.FilePath) && wt.FilePath != "untitled.etlsql" && File.Exists(wt.FilePath))
            {
                lines = (await _fileHandler.LoadAsync(wt.FilePath, ShowPrompt)).lines;
            }
            else
            {
                return null; // untitled-without-recovery or a file that no longer exists → drop
            }

            int cursorLine = Math.Clamp(wt.CursorLine, 0, Math.Max(0, lines.Length - 1));
            int cursorCol = lines.Length > 0 ? Math.Clamp(wt.CursorColumn, 0, lines[cursorLine].Length) : 0;
            return new TabState
            {
                FilePath = string.IsNullOrEmpty(wt.FilePath) ? "untitled.etlsql" : wt.FilePath,
                Lines = lines.ToList(),
                CursorLine = cursorLine,
                CursorColumn = cursorCol,
                ScrollLine = wt.ScrollLine,
                ScrollCol = wt.ScrollCol,
                IsDirty = dirty
            };
        }

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

                // Save results & telemetry state
                tab.LastResultSets = _evaluator.LastResultSets.ToList();
                tab.LastResult = _evaluator.LastResult;
                tab.Messages = _evaluator.Messages.ToList();
                tab.ProfileMetrics = _evaluator.Telemetry.ProfileMetrics.ToList();
                tab.ExecutionTreeNodes = _evaluator.Telemetry.ExecutionTree.GetAllNodes().ToList();

                // Save lower panel display states
                tab.ResultScrollRow = _renderer.ResultScrollRow;
                tab.ResultScrollCol = _renderer.ResultScrollCol;
                tab.ActiveResultSetIndex = _renderer.ActiveResultSetIndex;
                tab.FilterText = _renderer.FilterText;
                tab.TreeScrollRow = _renderer.TreeScrollRow;
                tab.MessageScrollRow = _renderer.MessageScrollRow;
                tab.ActiveLowerTab = _renderer.ActiveLowerTab;
                tab.ResultsVisible = _renderer.ResultsVisible;
                tab.PerformanceVisible = _renderer.PerformanceVisible;
                tab.OutputVisible = _renderer.OutputVisible;
                tab.VariablesVisible = _renderer.VariablesVisible;
                tab.CompareMode = _renderer.CompareMode;
                tab.IsBottomMaximized = _renderer.IsBottomMaximized;
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
                _analysisCts?.Cancel(); // drop any pending analysis from the previous tab
                _diagnostics = new List<EditorDiagnostic>(tab.Diagnostics);
                
                // Restore results & telemetry state
                _evaluator.LastResultSets.Clear();
                _evaluator.LastResultSets.AddRange(tab.LastResultSets);
                _evaluator.LastResult = tab.LastResult;
                _evaluator.Messages.Clear();
                _evaluator.Messages.AddRange(tab.Messages);
                _evaluator.Telemetry.ProfileMetrics.Clear();
                _evaluator.Telemetry.ProfileMetrics.AddRange(tab.ProfileMetrics);
                _evaluator.Telemetry.ExecutionTree.Clear();
                foreach (var node in tab.ExecutionTreeNodes)
                {
                    _evaluator.Telemetry.ExecutionTree.AddNode(node);
                }

                // Restore lower panel display states
                _renderer.ResultScrollRow = tab.ResultScrollRow;
                _renderer.ResultScrollCol = tab.ResultScrollCol;
                _renderer.ActiveResultSetIndex = tab.ActiveResultSetIndex;
                _renderer.FilterText = tab.FilterText;
                _renderer.TreeScrollRow = tab.TreeScrollRow;
                _renderer.MessageScrollRow = tab.MessageScrollRow;
                _renderer.ActiveLowerTab = tab.ActiveLowerTab;
                _renderer.ResultsVisible = tab.ResultsVisible;
                _renderer.PerformanceVisible = tab.PerformanceVisible;
                _renderer.OutputVisible = tab.OutputVisible;
                _renderer.VariablesVisible = tab.VariablesVisible;
                _renderer.CompareMode = tab.CompareMode;
                _renderer.IsBottomMaximized = tab.IsBottomMaximized;

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
            ScheduleSessionSave();
        }

        public async Task NewTab()
        {
            SaveActiveTabState();
            _tabs.Add(new TabState { FilePath = "untitled.etlsql" });
            await _evaluator.ResetSessionAsync();
            _undo.Clear();
            // Load the (empty) new tab through the normal pipeline so the buffer AND
            // the bottom pane (results, messages, telemetry, view mode) all reset to
            // empty rather than inheriting the previous tab's session.
            LoadTabState(_tabs.Count - 1);
            _renderer.ShowStatus("New tab started.");
            ScheduleSessionSave();
        }

        /// <summary>Saves the current tab's session and switches to another tab.</summary>
        public void SwitchToTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            SaveActiveTabState();
            LoadTabState(index);
            ScheduleSessionSave();
        }

        /// <summary>
        /// Counts tabs with unsaved changes, using the live dirty flag for the active
        /// tab and the cached flag for the rest.
        /// </summary>
        public int CountDirtyTabs()
        {
            int count = 0;
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool dirty = (i == _activeTabIndex) ? _isDirty : _tabs[i].IsDirty;
                if (dirty) count++;
            }
            return count;
        }

        /// <summary>Saves every dirty tab. Returns false if the user cancels any save.</summary>
        private async Task<bool> SaveAllTabs()
        {
            SaveActiveTabState();
            int original = _activeTabIndex;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (!_tabs[i].IsDirty) continue;
                if (i != _activeTabIndex) LoadTabState(i);
                if (!await SaveScript()) return false; // user cancelled Save As
                SaveActiveTabState();
            }
            if (original >= 0 && original < _tabs.Count) LoadTabState(original);
            return true;
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

        // Cached Bottom Pane State
        public List<DataTable> LastResultSets { get; set; } = new();
        public DataTable? LastResult { get; set; }
        public List<LogEntry> Messages { get; set; } = new();
        public List<ExecutionMetrics> ProfileMetrics { get; set; } = new();
        public List<ExecutionNode> ExecutionTreeNodes { get; set; } = new();

        // Lower panel display & scroll states
        public int ResultScrollRow { get; set; }
        public int ResultScrollCol { get; set; }
        public int ActiveResultSetIndex { get; set; }
        public string FilterText { get; set; } = "";
        public int TreeScrollRow { get; set; }
        public int MessageScrollRow { get; set; }
        public EditorFocus ActiveLowerTab { get; set; } = EditorFocus.Messages;

        // Which bottom view is active for this tab. Defaults give a fresh tab the
        // empty "Pipeline & Messages" view rather than inheriting the prior tab's.
        public bool ResultsVisible { get; set; }
        public bool PerformanceVisible { get; set; }
        public bool OutputVisible { get; set; }
        public bool VariablesVisible { get; set; }
        public bool CompareMode { get; set; }
        public bool IsBottomMaximized { get; set; }
    }
}
