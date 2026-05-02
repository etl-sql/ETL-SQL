using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using ETL_SQL.Core;
using ETL_SQL.ReportBuilder;

namespace ETL_SQL.TUI.UI
{
    public enum EditorFocus
    {
        Editor,
        ExecutionTree,
        Messages,
        Results,
        Performance
    }

    /// <summary>
    /// Orchestrates the visual representation of the editor, including panels, status bars, and overlays.
    /// </summary>
    public class EditorRenderer
    {
        public int ScrollLine { get; set; } = 0;
        public int ScrollCol { get; set; } = 0;
        public bool Headless { get; set; } = false;

        public string? StatusMessage { get; set; }
        public DateTime StatusMessageExpiry { get; set; } = DateTime.MinValue;

        public bool AutocompleteVisible { get; set; } = false;
        public List<Suggestion> AutocompleteOptions { get; set; } = new();
        public int AutocompleteIndex { get; set; } = 0;

        public EditorFocus Focus { get; set; } = EditorFocus.Editor;
        public bool ResultsFocus => Focus == EditorFocus.Results;
        public int ResultScrollRow { get; set; } = 0;
        public int ResultScrollCol { get; set; } = 0;
        public int MessageScrollRow { get; set; } = 0;
        public int TreeScrollRow { get; set; } = 0;
        public int ActiveResultSetIndex { get; set; } = 0;
        public bool IsBottomMaximized { get; set; } = false;
        private int _lastWidth = 0;
        private int _lastHeight = 0;
        private bool _forceFullRepaintPending = false;

        public void ForceFullRepaint() => _forceFullRepaintPending = true;

        public string? PromptTitle { get; set; }
        public string PromptValue { get; set; } = "";
        public int PromptCursor { get; set; } = 0;
        public List<string> PromptSuggestions { get; set; } = new();
        public int PromptSuggestionIndex { get; set; } = 0;
        public bool PromptIsSecret { get; set; } = false;
        public bool HelpVisible { get; set; } = false;
        public string FilterText { get; set; } = "";
        public bool PerformanceVisible { get; set; } = false;
        public bool ResultsVisible { get; set; } = false;
        public bool CompareMode { get; set; } = false;
        public int CompareFocusIndex { get; set; } = 0;
        public List<int> CompareScrollRows { get; set; } = new();
        public List<string> CompareFilters { get; set; } = new();
        public bool PromptVisible => !string.IsNullOrEmpty(PromptTitle);

        // Report-SQL Preview State (Phase 5)
        public bool ReportVisible { get; set; } = false;
        public int ActiveReportPageIndex { get; set; } = 0;
        public int ReportScrollRow { get; set; } = 0;
        public ReportManifest? CurrentReportManifest { get; set; }

        private readonly IConsoleInterface _console;
        private readonly EditorPanel _editorPanel;
        private readonly MessageTreePanel _messageTreePanel;
        private readonly ResultsPanel _resultsPanel;
        private readonly PerformancePanel _performancePanel;
        private readonly ReportPreviewPanel _reportPreviewPanel;

        /// <summary>Initializes a new instance of the <see cref="EditorRenderer"/> class.</summary>
        /// <param name="buffer">The editor text buffer.</param>
        /// <param name="evaluator">The current execution context.</param>
        /// <param name="console">Optional console abstraction for testing or alternative outputs.</param>
        public EditorRenderer(EditorBuffer buffer, Evaluator evaluator, IConsoleInterface? console = null)
        {
            _console = console ?? new PhysicalConsole();
            _editorPanel = new EditorPanel(buffer, this);
            _messageTreePanel = new MessageTreePanel(evaluator);
            _resultsPanel = new ResultsPanel(evaluator, this);
            _performancePanel = new PerformancePanel(evaluator, this);
            _reportPreviewPanel = new ReportPreviewPanel(this);
        }

        /// <summary>Renders the entire editor UI to the console.</summary>
        /// <param name="buffer">The text buffer to display.</param>
        /// <param name="evaluator">The evaluator containing results and logs.</param>
        /// <param name="filePath">The current file path.</param>
        /// <param name="isDirty">Whether the document has unsaved changes.</param>
        /// <param name="totalWidth">The width of the console window.</param>
        /// <param name="totalHeight">The height of the console window.</param>
        public void Render(EditorBuffer buffer, Evaluator evaluator, string filePath, bool isDirty, int totalWidth, int totalHeight)
        {
            if (totalWidth != _lastWidth || totalHeight != _lastHeight)
            {
                _forceFullRepaintPending = true;
                _lastWidth = totalWidth;
                _lastHeight = totalHeight;
            }

            if (!Headless) 
            {
                _console.CursorVisible = false;
                // Hard reset: Force cursor to (0,0) and ensure we are relative to the viewport top
                _console.Write("\x1b[H"); 
            }

            if (_forceFullRepaintPending)
            {
                try { Console.Clear(); } catch { }
                _forceFullRepaintPending = false;
            }

            // ── Layout Definitions ──────────────────────────────────────────
            int editorAreaTop = 1;
            int statusHeight  = 2; // Two lines for status/help bar
            int reservedBottom = statusHeight + 4; // status bar + 4 lines buffer for high-density safety

            // Prioritize 14 lines for the lower area to show 10 messages + rounded borders correctly
            int lowerAreaHeight = 14; 
            int available = totalHeight - editorAreaTop - reservedBottom;
            if (IsBottomMaximized || CompareMode) lowerAreaHeight = Math.Max(5, available - 5);
            else if (lowerAreaHeight > available - 8) lowerAreaHeight = Math.Max(5, available - 8);

            int editorAreaHeight = Math.Max(3, totalHeight - lowerAreaHeight - reservedBottom - editorAreaTop);
            int gutterWidth = (buffer.Lines.Count).ToString().Length + 2;

            // Report Preview takes over the entire central area if enabled
            if (ReportVisible)
            {
                _reportPreviewPanel.Render(_console, 0, editorAreaTop, totalWidth, totalHeight - statusHeight - editorAreaTop);
            }
            else
            {
                // Viewport clamping
                if (buffer.CursorLine < ScrollLine) ScrollLine = buffer.CursorLine;
                if (buffer.CursorLine >= ScrollLine + editorAreaHeight) ScrollLine = buffer.CursorLine - editorAreaHeight + 1;

            // Scroll limits
            if (PerformanceVisible)
            {
                int maxPerf = Math.Max(0, evaluator.Telemetry.ProfileMetrics.Count - (lowerAreaHeight - 4));
                ResultScrollRow = Math.Clamp(ResultScrollRow, 0, maxPerf);
            }
            else if (ResultsVisible && evaluator.LastResult != null)
            {
                int maxResult = Math.Max(0, evaluator.LastResult.Rows.Count - (lowerAreaHeight - 4));
                ResultScrollRow = Math.Clamp(ResultScrollRow, 0, maxResult);
            }
            else
            {
                // MessageTree panel — independent scroll for each column
                int innerRows = lowerAreaHeight - 3;
                int maxMsg  = Math.Max(0, evaluator.Messages.Count - innerRows);
                int maxTree = Math.Max(0, evaluator.Telemetry.ExecutionTree.GetAllNodes().Count() - innerRows);
                MessageScrollRow = Math.Clamp(MessageScrollRow, 0, maxMsg);
                TreeScrollRow    = Math.Clamp(TreeScrollRow, 0, maxTree);
            }

            if (ActiveResultSetIndex >= evaluator.LastResultSets.Count)
                ActiveResultSetIndex = Math.Max(0, evaluator.LastResultSets.Count - 1);

            // Ensure compare scroll/filter arrays are sized to match result sets
            if (CompareMode)
            {
                while (CompareScrollRows.Count < evaluator.LastResultSets.Count) CompareScrollRows.Add(0);
                while (CompareFilters.Count  < evaluator.LastResultSets.Count) CompareFilters.Add("");
                if (CompareFocusIndex >= evaluator.LastResultSets.Count)
                    CompareFocusIndex = Math.Max(0, evaluator.LastResultSets.Count - 1);
            }

            int editorWidth = totalWidth - gutterWidth - 1;
            if (buffer.CursorColumn < ScrollCol) ScrollCol = buffer.CursorColumn;
            if (buffer.CursorColumn >= ScrollCol + editorWidth) ScrollCol = buffer.CursorColumn - editorWidth + 1;

            // Ensure we always start at the top-left to prevent line-drift/repeat
            if (!Headless) _console.SetCursorPosition(0, 0);

            // 1. Header
            if (!Headless)
            {
                _console.ClearLine(0, 0, totalWidth);
                string fileLabel = string.IsNullOrEmpty(filePath) ? "Untitled.etlsql" : System.IO.Path.GetFileName(filePath);
                string headerBase = $" ETL-SQL IDE | {fileLabel}{(isDirty ? "*" : "")}";
                string focusInfo = Focus == EditorFocus.Editor ? " [bold yellow](FOCUSED)[/]" : " [grey](F3 to focus)[/]";
                
                _console.Markup($"[white on grey15]{Markup.Escape(headerBase)} [/]{focusInfo}");
                
                int plainLen = headerBase.Length + 1 + (Focus == EditorFocus.Editor ? 9 : 13);
                if (totalWidth > plainLen)
                    _console.Markup($"[white on grey15]{new string(' ', totalWidth - plainLen)}[/]");
            }

            // 2. Main Panels
            if (!Headless)
            {
                int lowerY = editorAreaTop + editorAreaHeight;
                _editorPanel.Render(_console, 0, editorAreaTop, totalWidth, editorAreaHeight);

                if (CompareMode)
                    _resultsPanel.RenderCompare(_console, 0, lowerY, totalWidth, lowerAreaHeight, evaluator, this);
                else if (PerformanceVisible)
                    _performancePanel.Render(_console, 0, lowerY, totalWidth, lowerAreaHeight);
                else if (ResultsVisible)
                    _resultsPanel.Render(_console, 0, lowerY, totalWidth, lowerAreaHeight, ResultScrollRow);
                else
                    _messageTreePanel.Render(_console, 0, lowerY, totalWidth, lowerAreaHeight, TreeScrollRow, MessageScrollRow, Focus);
            }
        }

        // 4. Two-Line Status/Help Bar
            if (!Headless)
            {
                int helpRow = totalHeight - 2;
                int statusRow = totalHeight - 1;

                // ── Row 1: Help Bar (Static Shortcuts) ───────────────────────
                string helpText = " F1:Help  F5:Run  F6:Focus  F4:Panel  Alt+R:Report  F2:Save  F12:Format  ^Q:Exit ";
                _console.ClearLine(0, helpRow, totalWidth);
                _console.SetCursorPosition(0, helpRow);
                _console.Markup($"[white on grey23]{Markup.Escape(helpText.PadRight(totalWidth - 1))}[/]");

                // ── Row 2: Status Bar (Dynamic Info) ─────────────────────────
                string fileLabel2 = string.IsNullOrEmpty(filePath) ? "Untitled.etlsql" : System.IO.Path.GetFileName(filePath);
                string dirtyDot = isDirty ? "● " : "○ ";

                string panelPill;
                bool hasError = evaluator.LastError != null;
                if (CompareMode)
                    panelPill = $"[bold magenta] COMPARE {CompareFocusIndex + 1}/{Math.Max(1, evaluator.LastResultSets.Count)} [/]";
                else if (Focus == EditorFocus.Results)
                    panelPill = "[bold yellow] ▶ RESULTS FOCUS [/]";
                else if (Focus == EditorFocus.ExecutionTree)
                    panelPill = "[bold cyan] ▶ PIPELINE FOCUS [/]";
                else if (Focus == EditorFocus.Messages)
                    panelPill = "[bold yellow] ▶ MESSAGES FOCUS [/]";
                else if (Focus == EditorFocus.Performance)
                    panelPill = "[bold magenta] ▶ PERF FOCUS [/]";
                else if (hasError)
                    panelPill = "[bold red] ✗ ERROR [/]";
                else if (PerformanceVisible)
                    panelPill = "[bold cyan] PERF [/]";
                else if (ReportVisible)
                    panelPill = "[bold blue] REPORT [/]";
                else if (ResultsVisible)
                    panelPill = "[bold yellow] RESULTS [/]";
                else
                    panelPill = "[grey] PIPELINE [/]";

                string cursor3 = $" Ln {buffer.CursorLine + 1}, Col {buffer.CursorColumn + 1}";
                if (evaluator.LastExecTimeMs > 0)
                {
                    string elapsed = evaluator.LastExecTimeMs >= 60_000
                        ? $"{evaluator.LastExecTimeMs / 60_000.0:N1}m"
                        : evaluator.LastExecTimeMs >= 1_000
                            ? $"{evaluator.LastExecTimeMs / 1_000.0:N1}s"
                            : $"{evaluator.LastExecTimeMs}ms";
                    cursor3 += $"  ⏱ {elapsed}";
                }

                // Status message now has much more space
                string statusMsg = (DateTime.Now < StatusMessageExpiry) ? StatusMessage ?? "" : "";
                
                // Layout: [Dirty Filename] [Pill] [Cursor/Time] | [StatusMessage]
                string leftZone = $" {dirtyDot}{fileLabel2} ";
                string midZone = panelPill;
                string rightZone = $" {cursor3} ";
                
                int leftWidth = leftZone.Length;
                int midWidth = PerformanceVisible ? 8 : ResultsVisible ? 11 : hasError ? 9 : ResultsFocus ? 18 : 11;
                int rightWidth = rightZone.Length;
                
                int availForStatus = totalWidth - leftWidth - midWidth - rightWidth - 6; // separators
                if (statusMsg.Length > availForStatus && availForStatus > 5)
                    statusMsg = statusMsg[..Math.Max(0, availForStatus - 3)] + "...";

                string sep = "[grey]│[/]";
                string statusMarkup = 
                    $"[white on grey15]{Markup.Escape(leftZone)}[/]" + sep +
                    midZone + sep +
                    $"[white on grey15]{Markup.Escape(rightZone)}[/]" + sep +
                    $"[white on grey15] {Markup.Escape(statusMsg).PadRight(Math.Max(0, availForStatus))} [/]";

                _console.ClearLine(0, statusRow, totalWidth);
                _console.SetCursorPosition(0, statusRow);
                _console.Markup(statusMarkup);
            }

            // 5. Draw Prompt if active
            if (PromptVisible && !Headless)
            {
                int promptRow = totalHeight - 2;
                // Draw suggestions box if any
                if (PromptSuggestions.Any())
                {
                    int boxHeight = Math.Min(5, PromptSuggestions.Count);
                    int boxStartRow = promptRow - boxHeight;
                    for (int i = 0; i < boxHeight; i++)
                    {
                        _console.SetCursorPosition(0, boxStartRow + i);
                        var sugg = PromptSuggestions[i];
                        string style = i == PromptSuggestionIndex ? "black on yellow" : "white on blue";
                        string renderedSugg = Markup.Escape(sugg).PadRight(totalWidth);
                        if (renderedSugg.Length > totalWidth) renderedSugg = renderedSugg.Substring(0, totalWidth);
                        _console.Markup($"[{style}]{renderedSugg}[/]");
                    }
                }

                _console.SetCursorPosition(0, promptRow);
                string displayValue = PromptIsSecret ? new string('*', PromptValue.Length) : PromptValue;
                string promptText = $" [yellow]{PromptTitle}:[/] {displayValue}";
                
                // Ensure prompt doesn't wrap
                string renderedPrompt = promptText.PadRight(totalWidth);
                if (renderedPrompt.Length > totalWidth)
                {
                    // Truncate the displayValue if needed to fit the prompt
                    int labelLen = PromptTitle?.Length ?? 0 + 3; // " title: "
                    int availValue = Math.Max(0, totalWidth - labelLen);
                    if (displayValue.Length > availValue) displayValue = "..." + displayValue.Substring(displayValue.Length - availValue + 3);
                    promptText = $" [yellow]{PromptTitle}:[/] {displayValue}";
                    renderedPrompt = promptText.PadRight(totalWidth);
                    if (renderedPrompt.Length > totalWidth) renderedPrompt = renderedPrompt.Substring(0, totalWidth);
                }
                _console.Markup($"[white on black]{renderedPrompt}[/]");
                
                // Set cursor for prompt
                int cursorX = (PromptTitle?.Length ?? 0) + 3 + PromptCursor;
                _console.SetCursorPosition(Math.Min(cursorX, totalWidth - 1), promptRow);
                _console.CursorVisible = true;
            }

            // 6. Autocomplete popup
            if (AutocompleteVisible && AutocompleteOptions.Any() && !PromptVisible && !HelpVisible && !Headless)
            {
                int popupHeight = Math.Min(5, AutocompleteOptions.Count);
                int popupRow = (buffer.CursorLine - ScrollLine) + editorAreaTop + 1;
                int statusRow = totalHeight - 1;
                if (popupRow + popupHeight > statusRow) popupRow = (buffer.CursorLine - ScrollLine) + editorAreaTop - popupHeight;

                int viewStart = Math.Max(0, Math.Min(AutocompleteOptions.Count - popupHeight, AutocompleteIndex - (popupHeight / 2)));
                
                for (int i = 0; i < popupHeight; i++)
                {
                    int optionIndex = viewStart + i;
                    if (optionIndex >= AutocompleteOptions.Count) break;

                    int screenRow = popupRow + i;
                    if (screenRow < 0 || screenRow >= totalHeight) continue;
                    _console.SetCursorPosition((buffer.CursorColumn - ScrollCol) + gutterWidth, screenRow);
                    
                    var suggestion = AutocompleteOptions[optionIndex];
                    var text = Markup.Escape(suggestion.Text);
                    string color = suggestion.Type switch
                    {
                        SuggestionType.OptionName => "yellow",
                        SuggestionType.OptionValue => "green",
                        SuggestionType.Keyword => "white",
                        SuggestionType.Function => "cyan",
                        SuggestionType.Table => "cyan",
                        SuggestionType.Column => "white",
                        SuggestionType.Variable => "green",
                        SuggestionType.Alias => "purple",
                        SuggestionType.Connection => "blue",
                        _ => "white"
                    };

                    if (optionIndex == AutocompleteIndex) _console.Markup($"[black on white]{text.PadRight(20)}[/]");
                    else _console.Markup($"[{color} on blue]{text.PadRight(20)}[/]");
                }
            }

            // 7. Help Overlay
            if (HelpVisible) RenderHelpOverlay(totalWidth, totalHeight);

            // 8. Restore absolute cursor
            if (!ResultsFocus && !Headless && !HelpVisible && !PromptVisible)
            {
                _console.SetCursorPosition((buffer.CursorColumn - ScrollCol) + gutterWidth, (buffer.CursorLine - ScrollLine) + editorAreaTop);
                _console.CursorVisible = true;
            }
        }

        private void RenderHelpOverlay(int totalWidth, int totalHeight)
        {
            // Live state annotations
            string focusState  = ResultsFocus    ? "[bold yellow]RESULTS[/]"  : "[grey]EDITOR[/]";
            string panelState  = PerformanceVisible ? "[bold cyan]PERF[/]"
                               : ResultsVisible     ? "[bold yellow]RESULTS[/]"
                               :                      "[grey]PIPELINE[/]";

            var table = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn("").Width(16))
                .AddColumn(new TableColumn(""));

            void Section(string title)
            {
                table.AddRow(new Markup(""), new Markup($"[bold grey] ── {title} ──[/]"));
            }

            void Row(string key, string desc)
            {
                table.AddRow(new Markup($"[yellow]{Markup.Escape(key)}[/]"), new Markup(Markup.Escape(desc)));
            }

            void RowAnnotated(string key, string desc, string annotation)
            {
                table.AddRow(new Markup($"[yellow]{Markup.Escape(key)}[/]"), new Markup($"{Markup.Escape(desc)}  {annotation}"));
            }

            Section("View");
            RowAnnotated("F6",             "Toggle focus: Editor / Results", $"now: {focusState}");
            RowAnnotated("F4",             "Cycle lower panel",              $"now: {panelState}");
            Row("Ctrl+M",                  "Maximize / Restore lower panel");
            Row("F7",                      "Enter / exit Compare mode (2+ result sets)");
            Row("F8",                      "Cycle active pane  [grey](Compare mode)[/]");
            Row("F1",                      "Close this help screen");
            Row("Escape",                  "Clear filter / Exit focus or Compare mode");

            Section("Execution");
            Row("F5",                      "Run entire script");
            Row("Shift+F5",               "Run current statement only");
            Row("Ctrl+R",                  "Clear all results and output");

            Section("File");
            Row("Ctrl+S",                  "Save");
            Row("Ctrl+Shift+S",            "Save As");
            Row("Ctrl+O",                  "Open (with file autocomplete)");
            Row("Ctrl+N",                  "New script");
            Row("Ctrl+P",                  "Export results to CSV");

            Section("Editing");
            Row("Ctrl+Z / Ctrl+Y",        "Undo / Redo");
            Row("Ctrl+C / Ctrl+V",        "Copy / Paste");
            Row("Ctrl+X",                  "Cut selection");
            Row("Ctrl+A",                  "Select all");
            Row("Ctrl+Q",                  "Exit");
            Row("Ctrl+D / Ctrl+K",        "Duplicate / Delete line");
            Row("Ctrl+/",                  "Toggle line comment (--)");
            Row("Tab / Shift+Tab",        "Indent / Outdent (selection-aware)");
            Row("Ctrl+I  / Alt+F",        "Format SQL (Beautifier)");
            Row("Ctrl+Space",              "Autocomplete suggestions");
            Row("Alt+Up / Down",           "Add cursor above / below");
            Row("Escape",                  "Clear multi-cursors");

            Section("Navigation");
            Row("Ctrl+F",                  "Find text  [grey](Filter rows when Results focused)[/]");
            Row("Ctrl+H",                  "Replace text");
            Row("Ctrl+G",                  "Go to line");
            Row("Ctrl+Home / Ctrl+End",   "Start / End of script");
            Row("Ctrl+Left / Right",       "Jump word left / right");
            Row("Ctrl+Shift+Left / Right", "Select word left / right");
            Row("Shift+Arrows",            "Select text");
            Row("Ctrl+Up / Down",          "Scroll panel (line)");
            Row("Ctrl+PgUp / PgDn",       "Scroll panel (page)");

            int panelWidth  = Math.Min(72, totalWidth  - 4);
            int panelHeight = Math.Min(32, totalHeight - 4);

            var inner = new Rows(
                table,
                new Markup("[grey] ─────────────────────────────────────────────[/]"),
                new Markup("[grey] Press any key to close[/]")
            );

            var panel = new Panel(inner)
            {
                Header = new PanelHeader("[bold yellow] ETL-SQL Keyboard Reference [/]", Justify.Left),
                Height = panelHeight,
                Width  = panelWidth,
                Border = BoxBorder.Double
            };

            int startRow = Math.Max(0, (totalHeight - panelHeight) / 2);

            _console.SetCursorPosition(0, startRow);
            _console.WriteWidget(panel);
        }

        /// <summary>Displays a temporary status message in the status bar.</summary>
        public void ShowStatus(string message) { StatusMessage = message; StatusMessageExpiry = DateTime.Now.AddSeconds(3); }
    }
}
