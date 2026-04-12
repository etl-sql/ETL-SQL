using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Terminal.Gui;
using NStack;
using ETL_SQL.Core;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.TUI.UI;
using Spectre.Console;
using System.Threading;
using Spectre.Console.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.TUI.UI
{
    public class TerminalIdeWindow : Window
    {
        private readonly CliContext _context;
        private readonly IServiceProvider? _serviceProvider;
        private readonly EditorFileHandler _fileHandler;
        private readonly SuggestionEngine _suggestionEngine = new();
        private readonly MetadataManager _metadata;
        private ExecutionSession? _session;

        // ── Views (internal for testability) ──────────────────────────────────
        internal readonly SyntaxTextView _editor;
        internal readonly TextView _resultsView;
        internal readonly TextView _messagesView;
        internal readonly ListView _treeView;
        internal readonly TextView _perfView;
        internal readonly TabView _tabView;

        // ── Tab handles ───────────────────────────────────────────────────────
        private readonly TabView.Tab _resultsTab;
        private readonly TabView.Tab _messagesTab;
        private readonly TabView.Tab _treeTab;
        private readonly TabView.Tab _perfTab;

        // ── State ─────────────────────────────────────────────────────────────
        internal string _activeTab = "results";
        internal string? _currentFilePath;
        private List<string> _treeLines = new();
        private bool _justAccepted = false;
        private CancellationTokenSource? _autocompleteCts;
        
        private Dictionary<string, IDataSource> _connectionCache = new();

        // ── Launch (production entry point) ───────────────────────────────────

        public static void Launch(CliContext ctx, IServiceProvider serviceProvider)
        {
            Application.Init();

            var win = new TerminalIdeWindow(ctx, serviceProvider);
            win._session = new ExecutionSession(serviceProvider, ctx);
            win.X = 0; win.Y = 0;
            win.Width = Dim.Fill(); win.Height = Dim.Fill() - 1;

            var statusBar = win.BuildStatusBar();

            Application.Top.Add(win, statusBar);
            Application.Run();
            Application.Shutdown();
        }

        // ── Constructor ───────────────────────────────────────────────────────

        public TerminalIdeWindow(CliContext ctx, IServiceProvider? serviceProvider = null)
            : base("ETL-SQL Editor")
        {
            // Apply a global Dark Theme to prevent Terminal.Gui's default 'Blue' layout
            ColorScheme = new ColorScheme 
            {
                Normal = new Terminal.Gui.Attribute(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
                Focus = new Terminal.Gui.Attribute(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
                HotNormal = new Terminal.Gui.Attribute(Terminal.Gui.Color.Cyan, Terminal.Gui.Color.Black),
                HotFocus = new Terminal.Gui.Attribute(Terminal.Gui.Color.Cyan, Terminal.Gui.Color.Black)
            };

            _context = ctx;
            _serviceProvider = serviceProvider;
            _fileHandler = new EditorFileHandler(new PhysicalFileSystem(), new ETL_SQL.Services.SecurityService());
            _metadata = new MetadataManager(_connectionCache);

            // Eagerly resolve IConnectorRegistry so ConnectorRegistry.Instance is set before
            // the user starts typing. DatabaseSchemaProvider reads the static Instance property,
            // which is only assigned by the DI-created ConnectorRegistry constructor.
            _serviceProvider?.GetService(typeof(IConnectorRegistry));

            // ── Editor (top 60%) ─────────────────────────────────────────────
            var editorFrame = new FrameView("Editor")
            {
                X = 0, Y = 0,
                Width = Dim.Fill(), Height = Dim.Percent(60)
            };

            _editor = new SyntaxTextView
            {
                X = 0, Y = 0,
                Width = Dim.Fill(), Height = Dim.Fill(),
                AllowsReturn = true,
                AllowsTab = true   // Must be true to prevent focus loss, intercepted in SyntaxTextView
            };
            editorFrame.Add(_editor);

            // Configure the built-in TextViewAutocomplete.
            // Autocomplete.ProcessKey runs BEFORE the host's ProcessKey, so Tab is
            // intercepted correctly without any manual key-dispatch hacks.
            // NOTE: HostControl is NOT auto-set by the TextView constructor — we must
            // set it explicitly here or GenerateSuggestions will NullRef on GetCurrentWord.
            _editor.Autocomplete.HostControl = _editor;
            _editor.Autocomplete.SelectionKey = Key.Enter; // Enter is safer for Terminal.Gui default handling
            _editor.Autocomplete.MaxHeight = 6;
            _editor.Autocomplete.MaxWidth = 40;

            // ── Output tab panes ─────────────────────────────────────────────
            _resultsView = new TextView
            {
                X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(),
                ReadOnly = true, WordWrap = false
            };
            _messagesView = new TextView
            {
                X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(),
                ReadOnly = true, WordWrap = false
            };
            _treeView = new ListView(new List<string> { "Execution tree will appear here..." })
            {
                X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill()
            };
            _perfView = new TextView
            {
                X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(),
                ReadOnly = true, WordWrap = false
            };

            _resultsTab  = new TabView.Tab("Results",      _resultsView);
            _messagesTab = new TabView.Tab("Messages",     _messagesView);
            _treeTab     = new TabView.Tab("Execute Tree", _treeView);
            _perfTab     = new TabView.Tab("Perf",         _perfView);

            _tabView = new TabView
            {
                X = 0, Y = Pos.Bottom(editorFrame),
                Width = Dim.Fill(), Height = Dim.Fill()
            };
            _tabView.AddTab(_treeTab,     andSelect: true);
            _tabView.AddTab(_resultsTab,  andSelect: false);
            _tabView.AddTab(_messagesTab, andSelect: false);
            _tabView.AddTab(_perfTab,     andSelect: false);

            Add(editorFrame, _tabView);

            SubscribeToLogs();
            SetupEditorEvents();
        }

        // ── Status bar ────────────────────────────────────────────────────────

        internal StatusBar BuildStatusBar() => new StatusBar(new StatusItem[]
        {
            new StatusItem(Key.F5,                       "~F5~ Run",     () => _ = RunScriptAsync(false)),
            new StatusItem(Key.F6,                       "~F6~ RunSel",  () => _ = RunScriptAsync(true)),
            new StatusItem(Key.F1,                       "~F1~ Results", () => SwitchTab("results")),
            new StatusItem(Key.F2,                       "~F2~ Messages",() => SwitchTab("messages")),
            new StatusItem(Key.F3,                       "~F3~ Tree",    () => SwitchTab("tree")),
            new StatusItem(Key.F4,                       "~F4~ Perf",    () => SwitchTab("perf")),
            new StatusItem(Key.CtrlMask | Key.S,         "~^S~ Save",    () => _ = SaveScriptAsync()),
            new StatusItem(Key.CtrlMask | Key.Q,         "~^Q~ Quit",    () => HandleExit()),
        });

        internal void UpdateStatusBar()
        {
            // Status bar items are rebuilt at Launch time; the title bar shows
            // the filename and modified state.
            var file     = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "New Script";
            var modified = _editor.IsDirty ? "*" : "";
            Title = $"ETL-SQL Editor — {file}{modified}";
        }

        // ── Tab switching ─────────────────────────────────────────────────────

        internal void SwitchTab(string tab)
        {
            _activeTab = tab;
            _tabView.SelectedTab = tab switch
            {
                "messages" => _messagesTab,
                "tree"     => _treeTab,
                "perf"     => _perfTab,
                _          => _resultsTab
            };
            _tabView.SetFocus();
        }

        // ── Key handling ──────────────────────────────────────────────────────
        // Tab when autocomplete is visible is handled by TextViewAutocomplete.ProcessKey
        // which runs inside TextView.ProcessKey BEFORE any other key dispatch. No
        // manual key-routing hacks are needed here.

        public override bool ProcessKey(KeyEvent keyEvent)
        {
            // Global shortcuts
            if (keyEvent.Key == Key.F5) { _ = RunScriptAsync(false); return true; }
            if (keyEvent.Key == Key.F6) { _ = RunScriptAsync(true);  return true; }
            if (keyEvent.Key == Key.F1) { SwitchTab("results");   return true; }
            if (keyEvent.Key == Key.F2) { SwitchTab("messages");  return true; }
            if (keyEvent.Key == Key.F3) { SwitchTab("tree");      return true; }
            if (keyEvent.Key == Key.F4) { SwitchTab("perf");      return true; }

            if (keyEvent.Key == (Key.CtrlMask | Key.S)) { _ = SaveScriptAsync(); return true; }
            if (keyEvent.Key == (Key.CtrlMask | Key.Q)) { HandleExit(); return true; }
            if (keyEvent.Key == (Key.CtrlMask | Key.Space)) { _ = UpdateAutocompleteAsync(forced: true); return true; }

            // Shift+Alt+F — format
            if (keyEvent.IsShift && keyEvent.IsAlt && (keyEvent.Key & ~Key.ShiftMask & ~Key.AltMask) == (Key)'f')
            {
                FormatScript();
                return true;
            }

            return base.ProcessKey(keyEvent);
        }

        // ── Format ────────────────────────────────────────────────────────────

        internal void FormatScript()
        {
            var text = _editor.Text?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var formatted = SqlFormatter.Format(text);
                _editor.Text = ustring.Make(formatted);
            }
            catch
            {
                // Leave unchanged on formatter error
            }
        }

        // ── Autocomplete ──────────────────────────────────────────────────────
        // TextViewAutocomplete.ProcessKey is called inside TextView.ProcessKey BEFORE
        // any other key handling. When the popup is visible, Tab calls Select() →
        // InsertSelection() → DeleteTextBackwards() + InsertText(). No manual key
        // routing or text manipulation needed here.

        private void SetupEditorEvents()
        {
            // KeyDown fires BEFORE TextViewAutocomplete.ProcessKey handles Tab.
            // We mark _justAccepted here so UpdateAutocompleteAsync skips the next
            // KeyUp cycle, preventing the just-accepted suggestion from immediately
            // re-triggering the popup. We also schedule Visible=false on the main
            // loop so the blue selection overlay is cleared after Tab acceptance.
            _editor.KeyDown += (args) =>
            {
                if (args.KeyEvent.Key == Key.Tab && _editor.Autocomplete.Visible)
                {
                    _justAccepted = true;
                    Application.MainLoop?.Invoke(() =>
                    {
                        _editor.Autocomplete.Visible = false;
                        _editor.SetNeedsDisplay();
                    });
                }
            };

            _editor.KeyUp += async (e) =>
            {
                UpdateStatusBar();
                
                // If the user just typed a space, and we just accepted a suggestion,
                // we should ensure it's not double-spaced or eaten. 
                // However, most TUI inconsistency comes from the popup not clearing.
                await UpdateAutocompleteAsync(forced: false);
            };
        }

        private async Task UpdateAutocompleteAsync(bool forced = false)
        {
            // Skip the immediate KeyUp after a Tab acceptance to prevent the popup
            // re-appearing on the character that was just inserted.
            if (_justAccepted) { _justAccepted = false; return; }

            // Cancel any still-running autocomplete request (rapid-typing race condition).
            _autocompleteCts?.Cancel();
            var cts = new CancellationTokenSource();
            _autocompleteCts = cts;

            var text  = _editor.Text?.ToString() ?? "";
            var row   = _editor.CurrentRow;
            var col   = _editor.CurrentColumn;
            var lines = text.Split('\n');
            if (row >= lines.Length)
            {
                _editor.Autocomplete.ClearSuggestions();
                _editor.Autocomplete.Visible = false;
                return;
            }

            var currentLine = lines[row];
            var prefix = GetWordPrefix(currentLine, col);

            // Ctrl+Space on '*': cursor is right after a '*' character.
            // Extend the prefix to include it so AliasColumnProvider can handle
            // "alias.*" → expand to full column list.
            if (forced && prefix.Length == 0 && col > 0 && col <= currentLine.Length && currentLine[col - 1] == '*')
                prefix = GetWordPrefix(currentLine, col - 1) + "*";

            if (!forced && prefix.Length < 2)
            {
                _editor.Autocomplete.ClearSuggestions();
                _editor.Autocomplete.Visible = false;
                return;
            }

            _metadata.RefreshConnections(text);

            // Rule 4: Use cached connections — never call GetService<Evaluator>() here.
            var connections = _connectionCache;
            var aliases     = ETLSuggestEngine.ParseAliases(text);
            var virtuals    = ETLSuggestEngine.ParseVirtualSchemas(text);

            var scriptBefore = col <= currentLine.Length
                ? string.Join("\n", lines.Take(row)) + "\n" + currentLine.Substring(0, col)
                : string.Join("\n", lines.Take(row + 1));

            var ctx = new SuggestionContext
            {
                Prefix       = prefix,
                FullScript   = text,
                ScriptBefore   = scriptBefore,
                Connections    = connections,
                Aliases        = aliases,
                VirtualSchemas = virtuals
            };

            List<Suggestion> suggestions;
            try
            {
                suggestions = await _suggestionEngine.GetSuggestionsAsync(ctx);
            }
            catch (OperationCanceledException) { return; }

            // A newer request may have started while we were awaiting — discard stale results.
            if (cts.IsCancellationRequested) return;

            if (!suggestions.Any())
            {
                _editor.Autocomplete.ClearSuggestions();
                _editor.Autocomplete.Visible = false;
                return;
            }

            // Immediately expand '*' or '.*' wildcards without popping up a menu, 
            // bypassing Terminal.Gui's 'char.IsLetterOrDigit' word-boundary replacement bug entirely.
            if (forced && (prefix == "*" || prefix.EndsWith(".*")) && suggestions.Count == 1 
                && suggestions[0].Type != SuggestionType.Keyword)
            {
                var lineText = lines[row];
                var startPos = col - prefix.Length;
                _editor.Text = lineText.Remove(startPos, prefix.Length).Insert(startPos, suggestions[0].Text);
                _editor.CursorPosition = new Point(startPos + suggestions[0].Text.Length, row);
                _justAccepted = true;
                return;
            }
            _editor.Autocomplete.AllSuggestions = suggestions.Select(s => s.Text).ToList();
            _editor.Autocomplete.GenerateSuggestions(0);
            // GenerateSuggestions populates Suggestions but does NOT set Visible — do it here.
            _editor.Autocomplete.Visible = _editor.Autocomplete.Suggestions?.Count > 0;
        }

        internal static string GetWordPrefix(string line, int col)
        {
            if (col <= 0 || col > line.Length) return "";
            var sub = line.Substring(0, col);
            // Include '*' in the word prefix regex so "u.*" can be expanded as a single token
            var m = Regex.Match(sub, @"[\w.#@/\\*]*$");
            return m.Success ? m.Value : "";
        }

        // ── Execution ─────────────────────────────────────────────────────────

        private async Task RunScriptAsync(bool selectedOnly)
        {
            var fullText = _editor.Text?.ToString() ?? "";
            string script;
            if (selectedOnly && _editor.Selecting)
            {
                var sel = _editor.SelectedText?.ToString() ?? "";
                script = string.IsNullOrWhiteSpace(sel) ? fullText : sel;
            }
            else
            {
                script = fullText;
            }

            if (string.IsNullOrWhiteSpace(script)) return;

            var sp = _serviceProvider;
            if (sp == null)
            {
                SetResultsText("[red]No service provider — cannot execute.[/]");
                return;
            }

            // Rule 5: clear all output tabs before every new execution so no
            // content from a prior run is visible alongside the new run.
            _resultsView.Text  = ustring.Make("");
            _messagesView.Text = ustring.Make("");
            _perfView.Text     = ustring.Make("");
            _treeLines         = new List<string> { "Executing…" };
            _treeView.SetSource(_treeLines);
            SwitchTab("tree");

            try
            {
                if (_session != null)
                {
                    // Wire live tree updates once
                    _session.OnTreeNodeAdded = line => Application.MainLoop?.Invoke(() =>
                    {
                        _treeLines.Add(line);
                        _treeView.SetSource(new List<string>(_treeLines));
                        _treeView.MoveDown();
                        _treeView.SetNeedsDisplay(); // Force physical redraw for live updates
                        Application.Refresh();
                    });
                }

                // Run the actual execution on a background thread so the UI MainLoop 
                // remains free to process and render live tree updates!
                var result = await Task.Run(async () => await _session!.ExecuteAsync(script));

                Application.MainLoop?.Invoke(() =>
                {
                    // Execution tree
                    if (result.ExecutionTree != null)
                    {
                        var treeText = RenderToPlainText(result.ExecutionTree);
                        _treeLines = treeText.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                    }
                    else
                    {
                        _treeLines = new List<string> { "No execution tree." };
                    }
                    _treeView.SetSource(_treeLines);

                    // Results
                    if (result.Success)
                    {
                        var sb = new StringBuilder();
                        foreach (var table in result.ResultsTables)
                        {
                            if (sb.Length > 0) sb.AppendLine(new string('─', 40));
                            sb.AppendLine(RenderToPlainText(table));
                        }
                        _resultsView.Text = ustring.Make(
                            sb.Length > 0 ? sb.ToString() : "No results returned.");
                    }
                    else
                    {
                        var errors = string.Join("\n", result.Diagnostics.Select(d => $"Error: {d.Message}"));
                        _resultsView.Text = ustring.Make(errors);
                    }

                    // Perf tab
                    _perfView.Text = ustring.Make(
                        $"Execution time : {result.ExecutionTimeMs}ms\n" +
                        $"Rows processed : {result.RowsProcessed:N0}");

                    // Messages tab: execution-scoped messages only.
                    _messagesView.Text = ustring.Make(string.Join("\n", result.Messages));

                    // Rule 4: Update the connection cache for subsequent autocomplete suggests.
                    // DO NOT clear the cache if the engine failed to launch (e.g. syntax error), 
                    // otherwise the user loses all autocompletes due to a typo.
                    if (result.Success || result.ActiveConnections.Any())
                    {
                        _connectionCache = result.ActiveConnections;
                    }

                    UpdateStatusBar();
                });
            }
            catch (Exception ex)
            {
                Application.MainLoop?.Invoke(() =>
                {
                    SetResultsText($"Fatal Error: {ex.Message}");
                    SwitchTab("results");
                });
            }
        }

        private void SetResultsText(string text)
        {
            _resultsView.Text = ustring.Make(text);
        }

        private static string RenderToPlainText(IRenderable renderable)
        {
            var sw = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(sw),
                ColorSystem = ColorSystemSupport.NoColors
            });
            console.Write(renderable);
            return sw.ToString();
        }

        // ── File operations ───────────────────────────────────────────────────

        public async Task SaveScriptAsync()
        {
            if (_currentFilePath == null)
            {
                var path = await PromptForPath("Save As", "Enter file path:");
                if (string.IsNullOrWhiteSpace(path)) return;
                _currentFilePath = path.Trim();
            }

            var script  = _editor.Text?.ToString() ?? "";
            bool success = await _fileHandler.SaveAsync(_currentFilePath, script, PromptForPassword);
            if (success)
            {
                // IsDirty is read-only in Terminal.Gui 1.x
                SetResultsText($"Saved to {_currentFilePath}");
                UpdateStatusBar();
            }
        }

        public async Task LoadScriptAsync(string path)
        {
            if (!File.Exists(path)) return;
            var (lines, actualPath) = await _fileHandler.LoadAsync(path, PromptForPassword);
            _editor.Text = ustring.Make(string.Join("\n", lines));
            _currentFilePath = actualPath;
            // IsDirty is read-only in Terminal.Gui 1.x
            UpdateStatusBar();
        }

        private Task<string?> PromptForPath(string title, string prompt)
        {
            var tcs = new TaskCompletionSource<string?>();
            var dialog = new Dialog(title, 60, 7);
            var field  = new TextField("") { X = 1, Y = 1, Width = Dim.Fill(1) };
            var ok     = new Button("OK",    is_default: true);
            var cancel = new Button("Cancel");

            ok.Clicked     += () => { tcs.TrySetResult(field.Text?.ToString()); Application.RequestStop(); };
            cancel.Clicked += () => { tcs.TrySetResult(null); Application.RequestStop(); };

            dialog.Add(new Label(prompt) { X = 1, Y = 0 }, field);
            dialog.AddButton(ok);
            dialog.AddButton(cancel);
            Application.Run(dialog);
            return tcs.Task;
        }

        private Task<string?> PromptForPassword(string title, string message, bool isSecret)
            => PromptForPath(title, message);

        // ── Exit ──────────────────────────────────────────────────────────────

        private void HandleExit()
        {
            if (_editor.IsDirty)
            {
                var result = MessageBox.Query("Unsaved Changes",
                    "Save before exiting?", "Save", "Discard", "Cancel");
                if (result == 0) { _ = SaveScriptAsync().ContinueWith(_ => Application.RequestStop()); return; }
                if (result == 1) { Application.RequestStop(); return; }
                // Cancel — do nothing
            }
            else
            {
                Application.RequestStop();
            }
        }

        // ── Logger subscription ───────────────────────────────────────────────
        // Rule 2: ILogger.OnMessage is a system-level diagnostic channel — it fires for
        // engine housekeeping messages not appropriate for end users. We do NOT subscribe
        // here. User-facing messages come from ExecutionResult.Diagnostics instead.
        private void SubscribeToLogs() { /* intentionally empty — see Rule 2 */ }
    }
}
