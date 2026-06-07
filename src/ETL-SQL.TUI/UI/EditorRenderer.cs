using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using ETL_SQL.Core;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Services;
using ETL_SQL.Reporting;

namespace ETL_SQL.TUI.UI
{
    public enum EditorFocus
    {
        Editor,
        ExecutionTree,
        Messages,
        Results,
        Performance,
        Sidebar,
        Output
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
        public bool SnippetModeActive { get; set; } = false;
        public int HelpPageIndex { get; set; } = 0;
        public bool InfoVisible { get; set; } = false;
        public string InfoContent { get; set; } = "";
        public string InfoTitle { get; set; } = "Info";
        public int InfoScrollRow { get; set; } = 0;

        public bool PaletteVisible { get; set; } = false;
        public string PaletteFilter { get; set; } = "";
        public int PaletteIndex { get; set; } = 0;
        public List<(string Title, string Shortcut)> PaletteItems { get; set; } = new();
        /// <summary>True when a "press any key to dismiss" overlay (help or info) is showing.</summary>
        public bool ModalOverlayVisible => HelpVisible || InfoVisible;
        public string FilterText { get; set; } = "";
        public bool PerformanceVisible { get; set; } = false;
        public bool ResultsVisible { get; set; } = false;
        public bool OutputVisible { get; set; } = false;
        public bool CompareMode { get; set; } = false;

        // Durable list of locations the TUI produced (served URLs, export paths).
        public List<OutputEntry> OutputEntries { get; } = new();
        public int OutputSelectedIndex { get; set; } = 0;
        public int OutputScrollRow { get; set; } = 0;
        public void AddOutput(OutputKind kind, string location)
        {
            OutputEntries.Add(new OutputEntry(kind, location, DateTime.Now));
            OutputSelectedIndex = OutputEntries.Count - 1;
        }
        public int CompareFocusIndex { get; set; } = 0;
        public EditorFocus ActiveLowerTab { get; set; } = EditorFocus.Messages;
        private int _lastMessageCount = 0;
        public List<int> CompareScrollRows { get; set; } = new();
        public List<string> CompareFilters { get; set; } = new();
        public bool PromptVisible => !string.IsNullOrEmpty(PromptTitle);

        // Report-SQL Preview State (Phase 5)
        public bool ReportVisible { get; set; } = false;
        public int ActiveReportPageIndex { get; set; } = 0;
        public int ReportScrollRow { get; set; } = 0;
        public ReportManifest? CurrentReportManifest { get; set; }
        private Dictionary<int, int> _linePhysicalShifts = new();

        public void ReportPrevPage()
        {
            if (CurrentReportManifest == null) return;
            ActiveReportPageIndex = Math.Max(0, ActiveReportPageIndex - 1);
            ReportScrollRow = 0;
            ForceFullRepaint();
        }

        public void ReportNextPage()
        {
            if (CurrentReportManifest == null || CurrentReportManifest.Pages.Count == 0) return;
            ActiveReportPageIndex = Math.Min(CurrentReportManifest.Pages.Count - 1, ActiveReportPageIndex + 1);
            ReportScrollRow = 0;
            ForceFullRepaint();
        }

        public void SetLinePhysicalShift(int lineIdx, int shift) => _linePhysicalShifts[lineIdx] = shift;
        public int GetLinePhysicalShift(int lineIdx) => _linePhysicalShifts.TryGetValue(lineIdx, out var s) ? s : 0;

        public bool SidebarVisible { get; set; } = false;
        public int SidebarWidth { get; set; } = 30;
        public int SidebarScrollRow { get; set; } = 0;
        public int SidebarSelectedIndex { get => _sidebarPanel.SelectedIndex; set => _sidebarPanel.SelectedIndex = value; }
        public int LastHeight => _lastHeight;
        public int LastWidth => _lastWidth;

        private readonly IConsoleInterface _console;
        private readonly EditorPanel _editorPanel;
        private readonly MessageTreePanel _messageTreePanel;
        private readonly ResultsPanel _resultsPanel;
        private readonly PerformancePanel _performancePanel;
        private readonly ReportPreviewPanel _reportPreviewPanel;
        private readonly OutputPanel _outputPanel = new();
        public readonly SidebarPanel _sidebarPanel;

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
            _sidebarPanel = new SidebarPanel(this, evaluator);
        }

        /// <summary>Renders the entire editor UI to the console.</summary>
        /// <param name="buffer">The text buffer to display.</param>
        /// <param name="evaluator">The evaluator containing results and logs.</param>
        /// <param name="filePath">The current file path.</param>
        /// <param name="isDirty">Whether the document has unsaved changes.</param>
        /// <param name="totalWidth">The width of the console window.</param>
        /// <param name="totalHeight">The height of the console window.</param>
        public void Render(ConsoleEditor editor, int totalWidth, int totalHeight)
        {
            var buffer = editor._buffer;
            var evaluator = editor._evaluator;
            var filePath = editor._filePath;
            var isDirty = editor._isDirty;

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

            // ── Layout Definitions (single source of truth — see LayoutCalculator) ──
            var layout = LayoutCalculator.Compute(totalWidth, totalHeight, buffer.Lines.Count,
                SidebarVisible, SidebarWidth, IsBottomMaximized, CompareMode);
            int editorAreaTop = layout.EditorAreaTop;
            int statusHeight  = layout.StatusHeight;
            int lowerAreaHeight = layout.LowerAreaHeight;
            int editorAreaHeight = layout.EditorAreaHeight;
            int gutterWidth = layout.GutterWidth;

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

            if (ActiveResultSetIndex >= evaluator.LastResultSets.Count)
                ActiveResultSetIndex = Math.Max(0, evaluator.LastResultSets.Count - 1);

            // Scroll limits
            if (PerformanceVisible)
            {
                int maxPerf = Math.Max(0, evaluator.Telemetry.ProfileMetrics.Count - 1);
                ResultScrollRow = Math.Clamp(ResultScrollRow, 0, maxPerf);
            }
            else if (ResultsVisible && evaluator.LastResultSets.Count > 0)
            {
                var res = evaluator.LastResultSets[ActiveResultSetIndex];
                int rowCount = res.Rows.Count;
                if (!string.IsNullOrEmpty(FilterText))
                {
                    rowCount = res.Rows.Count(row => res.ColumnNames.Any(c =>
                        (row[c]?.ToString() ?? "").Contains(FilterText, StringComparison.OrdinalIgnoreCase)));
                }
                int maxResult = Math.Max(0, rowCount - 1);
                ResultScrollRow = Math.Clamp(ResultScrollRow, 0, maxResult);
            }
            else
            {
                // MessageTree panel — independent scroll for each column
                int innerRows = lowerAreaHeight - 3;
                // Since messages can now wrap into multiple lines, maxMsg is an estimate based on message count.
                // We use an 8x multiplier to account for long lines; MessageTreePanel handles exact clipping.
                int maxMsg  = Math.Max(0, (evaluator.Messages.Count * 8) - innerRows); 
                int maxTree = Math.Max(0, evaluator.Telemetry.ExecutionTree.GetAllNodes().Count() - innerRows);

                // Auto-scroll for messages: if new messages arrived and we were at the bottom, stay at the bottom.
                if (evaluator.Messages.Count > _lastMessageCount)
                {
                    bool wasAtBottom = MessageScrollRow >= Math.Max(0, (_lastMessageCount * 8) - innerRows - 2);
                    if (wasAtBottom || _lastMessageCount == 0) 
                    {
                        MessageScrollRow = maxMsg;
                    }
                    _lastMessageCount = evaluator.Messages.Count;
                }

                MessageScrollRow = Math.Clamp(MessageScrollRow, 0, maxMsg);
                TreeScrollRow    = Math.Clamp(TreeScrollRow, 0, maxTree);
            }

            // Ensure compare scroll/filter arrays are sized to match result sets
            if (CompareMode)
            {
                while (CompareScrollRows.Count < evaluator.LastResultSets.Count) CompareScrollRows.Add(0);
                while (CompareFilters.Count  < evaluator.LastResultSets.Count) CompareFilters.Add("");
                if (CompareFocusIndex >= evaluator.LastResultSets.Count)
                    CompareFocusIndex = Math.Max(0, evaluator.LastResultSets.Count - 1);
            }

            int activeWidth = SidebarVisible ? totalWidth - SidebarWidth : totalWidth;
            int editorWidth = activeWidth - gutterWidth - 1;
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
                string focusInfo = Focus == EditorFocus.Editor ? $" [bold {TuiTheme.Instance.Ui.EditorFocusedBorder}](FOCUSED)[/]" : $" [{TuiTheme.Instance.Ui.EditorUnfocusedBorder}](F6 to focus)[/]";
                if (Focus == EditorFocus.Sidebar)
                {
                    focusInfo = " [bold yellow](EXPLORER FOCUS)[/]";
                }
                
                _console.Markup($"[{TuiTheme.Instance.Ui.StatusBackground}]{Markup.Escape(headerBase)} [/]{focusInfo}");
                
                int plainLen = headerBase.Length + 1 + (Focus == EditorFocus.Editor ? 9 : Focus == EditorFocus.Sidebar ? 16 : 13);
                if (totalWidth > plainLen)
                    _console.Markup($"[{TuiTheme.Instance.Ui.StatusBackground}]{new string(' ', totalWidth - plainLen)}[/]");
            }

            // 1b. Tab Bar
            if (!Headless)
            {
                _console.ClearLine(0, 1, totalWidth);
                _console.SetCursorPosition(0, 1);
                var tabBuilder = new System.Text.StringBuilder();
                for (int i = 0; i < editor._tabs.Count; i++)
                {
                    var tab = editor._tabs[i];
                    // Label and per-tab width are shared with the mouse hit-test via TabBarLayout
                    // so the close "x" and "+" land exactly where they are drawn.
                    string label = TabBarLayout.Label(tab.FilePath, tab.IsDirty);

                    if (i == editor._activeTabIndex)
                    {
                        tabBuilder.Append($"[bold black on yellow] {Markup.Escape(label)} [/][bold red on yellow]x[/][bold black on yellow] [/]");
                    }
                    else
                    {
                        tabBuilder.Append($"[white on grey23] {Markup.Escape(label)} [/][red on grey23]x[/][white on grey23] [/]");
                    }
                    tabBuilder.Append("[grey37]│[/]"); // separator after every tab
                }
                tabBuilder.Append("[white on grey23] + [/]");
                _console.Markup(tabBuilder.ToString());
            }

            // 2. Main Panels
            if (!Headless)
            {
                int lowerY = editorAreaTop + editorAreaHeight;
                int sidebarW = SidebarVisible ? SidebarWidth : 0;

                if (SidebarVisible)
                {
                    _sidebarPanel.Render(_console, 0, editorAreaTop, sidebarW, editorAreaHeight, SidebarScrollRow);
                }

                _editorPanel.Render(_console, sidebarW, editorAreaTop, totalWidth - sidebarW, editorAreaHeight);

                // Clickable tab strip on the first row of the lower pane; panels render below it.
                int lowerContentTop = layout.LowerContentTop;
                int lowerContentHeight = layout.LowerContentHeight;
                BottomTab activeBottom = OutputVisible ? BottomTab.Output
                                       : PerformanceVisible ? BottomTab.Performance
                                       : (ResultsVisible || CompareMode) ? BottomTab.Results
                                       : BottomTab.Pipeline;
                RenderBottomTabStrip(lowerY, totalWidth, activeBottom);
                if (ResultsVisible && !CompareMode && evaluator.LastResultSets.Count > 1)
                    RenderResultSetNav(lowerY, totalWidth, ActiveResultSetIndex, evaluator.LastResultSets.Count);

                if (CompareMode)
                    _resultsPanel.RenderCompare(_console, 0, lowerContentTop, totalWidth, lowerContentHeight, evaluator, this);
                else if (OutputVisible)
                    _outputPanel.Render(_console, 0, lowerContentTop, totalWidth, lowerContentHeight, OutputEntries, OutputSelectedIndex, OutputScrollRow, Focus == EditorFocus.Output);
                else if (PerformanceVisible)
                    _performancePanel.Render(_console, 0, lowerContentTop, totalWidth, lowerContentHeight);
                else if (ResultsVisible)
                    _resultsPanel.Render(_console, 0, lowerContentTop, totalWidth, lowerContentHeight, ResultScrollRow);
                else
                    _messageTreePanel.Render(_console, 0, lowerContentTop, totalWidth, lowerContentHeight, TreeScrollRow, MessageScrollRow, ActiveLowerTab);
            }
        }

        // 4. Two-Line Status/Help Bar
            if (!Headless)
            {
                int helpRow = totalHeight - 2;
                int statusRow = totalHeight - 1;

                // ── Row 1: Help Bar (clickable shortcuts; geometry in StatusBar) ─
                string helpBg = TuiTheme.Instance.Ui.HelpBackground;
                int helpPad = Math.Max(0, (totalWidth - 1) - StatusBar.PlainText().Length);
                var help = new System.Text.StringBuilder();
                help.Append($"[{helpBg}] ");
                bool firstBtn = true;
                foreach (var b in StatusBar.Buttons)
                {
                    if (!firstBtn) help.Append("  ");
                    firstBtn = false;
                    int colon = b.Label.IndexOf(':');
                    if (colon > 0)
                        help.Append($"[bold yellow]{Markup.Escape(b.Label.Substring(0, colon))}[/]{Markup.Escape(b.Label.Substring(colon))}");
                    else
                        help.Append(Markup.Escape(b.Label));
                }
                help.Append(' ');
                help.Append(new string(' ', helpPad));
                help.Append("[/]");
                _console.ClearLine(0, helpRow, totalWidth);
                _console.SetCursorPosition(0, helpRow);
                _console.Markup(help.ToString());

                // ── Row 2: Status Bar (Dynamic Info) ─────────────────────────
                string fileLabel2 = string.IsNullOrEmpty(filePath) ? "Untitled.etlsql" : System.IO.Path.GetFileName(filePath);
                string dirtyDot = isDirty ? "● " : "○ ";

                string panelPill;
                bool hasError = evaluator.LastError != null;
                if (CompareMode)
                    panelPill = $"[bold magenta] COMPARE {CompareFocusIndex + 1}/{Math.Max(1, evaluator.LastResultSets.Count)} [/]";
                else if (Focus == EditorFocus.Sidebar)
                    panelPill = "[bold yellow] ▶ EXPLORER [/]";
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

                string sep = $"[{TuiTheme.Instance.Ui.PanelUnfocusedBorder}]│[/]";
                string statusMarkup = 
                    $"[{TuiTheme.Instance.Ui.StatusBackground}]{Markup.Escape(leftZone)}[/]" + sep +
                    midZone + sep +
                    $"[{TuiTheme.Instance.Ui.StatusBackground}]{Markup.Escape(rightZone)}[/]" + sep +
                    $"[{TuiTheme.Instance.Ui.StatusBackground}] {Markup.Escape(statusMsg).PadRight(Math.Max(0, availForStatus))} [/]";

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
                string promptText = BuildPromptMarkup(PromptTitle, displayValue);
                
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
                    int sidebarW = SidebarVisible ? SidebarWidth : 0;
                    int physicalX = (buffer.CursorColumn - ScrollCol) + gutterWidth + GetLinePhysicalShift(buffer.CursorLine) + sidebarW;
                    _console.SetCursorPosition(physicalX, screenRow);
                    
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

                // Documentation sidecar
                var currentSugg = AutocompleteOptions[AutocompleteIndex];
                if (!string.IsNullOrEmpty(currentSugg.Documentation))
                {
                    int sidebarW = SidebarVisible ? SidebarWidth : 0;
                    RenderAutocompleteDocumentation(currentSugg.Documentation, popupRow, (buffer.CursorColumn - ScrollCol) + gutterWidth + 21 + sidebarW, totalWidth, totalHeight);
                }
            }

            // 7. Help / Info / Palette Overlay
            if (HelpVisible) RenderHelpOverlay(totalWidth, totalHeight);
            if (InfoVisible) RenderInfoOverlay(totalWidth, totalHeight);
            if (PaletteVisible) RenderCommandPalette(totalWidth, totalHeight);

            // 8. Restore absolute cursor
            if (!ResultsFocus && Focus != EditorFocus.Sidebar && !Headless && !HelpVisible && !InfoVisible && !PaletteVisible && !PromptVisible)
            {
                int sidebarW = SidebarVisible ? SidebarWidth : 0;
                int physicalX = (buffer.CursorColumn - ScrollCol) + gutterWidth + GetLinePhysicalShift(buffer.CursorLine) + sidebarW;
                _console.SetCursorPosition(physicalX, (buffer.CursorLine - ScrollLine) + editorAreaTop);
                _console.CursorVisible = true;
            }
        }

        private void RenderAutocompleteDocumentation(string doc, int row, int col, int totalWidth, int totalHeight)
        {
            if (col >= totalWidth - 10) return; // Not enough space

            int maxWidth = Math.Min(60, totalWidth - col - 2);
            var lines = doc.Replace("\r", "").Split('\n');
            var wrappedLines = new List<string>();

            foreach (var line in lines)
            {
                var words = line.Split(' ');
                var currentLine = "";
                foreach (var word in words)
                {
                    if (currentLine.Length + word.Length + 1 > maxWidth)
                    {
                        wrappedLines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        currentLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                    }
                }
                wrappedLines.Add(currentLine);
            }

            int height = Math.Min(10, wrappedLines.Count);
            if (row + height > totalHeight - 1) row = totalHeight - height - 1;

            for (int i = 0; i < height; i++)
            {
                _console.SetCursorPosition(col, row + i);
                string content = wrappedLines[i].PadRight(maxWidth);
                if (content.Length > maxWidth) content = content.Substring(0, maxWidth);
                _console.Markup($"[white on grey15] {Markup.Escape(content)} [/]");
            }
        }

        private void RenderCommandPalette(int totalWidth, int totalHeight)
        {
            int panelWidth = Math.Min(72, totalWidth - 6);
            int listCap = Math.Clamp(totalHeight - 10, 5, 14);

            // Keep the selected item visible.
            int start = 0;
            if (PaletteItems.Count > listCap)
            {
                start = Math.Clamp(PaletteIndex - listCap / 2, 0, PaletteItems.Count - listCap);
                if (PaletteIndex < start) start = PaletteIndex;
            }
            int end = Math.Min(PaletteItems.Count, start + listCap);

            var list = new Table().Border(TableBorder.None).HideHeaders()
                .AddColumn(new TableColumn("").Width(panelWidth - 20))
                .AddColumn(new TableColumn(""));

            if (PaletteItems.Count == 0)
            {
                list.AddRow(new Markup("[grey]No matching commands[/]"), new Markup(""));
            }
            for (int i = start; i < end; i++)
            {
                var (title, shortcut) = PaletteItems[i];
                var titleCell = i == PaletteIndex
                    ? new Markup($"[black on yellow] {Markup.Escape(title)} [/]")
                    : new Markup($" {Markup.Escape(title)}");
                var shortcutCell = new Markup(string.IsNullOrEmpty(shortcut) ? "" : $"[grey]{Markup.Escape(shortcut)}[/]");
                list.AddRow(titleCell, shortcutCell);
            }

            var inner = new Rows(
                new Markup($"[yellow]›[/] {Markup.Escape(PaletteFilter)}[grey]▏[/]"),
                new Markup("[grey]" + new string('─', Math.Max(1, panelWidth - 4)) + "[/]"),
                list);

            int panelHeight = Math.Min(totalHeight - 4, (end - start) + 5);
            var panel = new Panel(inner)
            {
                Header = new PanelHeader("[bold yellow] Command Palette [/]", Justify.Left),
                Width = panelWidth,
                Height = panelHeight,
                Border = BoxBorder.Double,
                Padding = new Padding(1, 0, 1, 0)
            };

            int startRow = Math.Max(1, (totalHeight - panelHeight) / 3);
            _console.SetCursorPosition(0, startRow); // column 0: multi-line widgets only honor the first line's column
            _console.WriteWidget(panel);
        }

        private void RenderInfoOverlay(int totalWidth, int totalHeight)
        {
            int panelWidth = Math.Min(84, totalWidth - 6);
            var lines = InfoContent.Replace("\r", "").Split('\n');

            // Compact, scrollable panel rather than a full-screen wall of text.
            int maxPanelHeight = Math.Min(20, Math.Max(7, totalHeight - 6));
            int panelHeight = Math.Min(maxPanelHeight, lines.Length + 3);
            int visibleRows = Math.Max(1, panelHeight - 3); // minus 2 borders and the footer line

            int maxScroll = Math.Max(0, lines.Length - visibleRows);
            InfoScrollRow = Math.Clamp(InfoScrollRow, 0, maxScroll);

            var rows = new List<IRenderable>();
            int last = Math.Min(lines.Length, InfoScrollRow + visibleRows);
            for (int i = InfoScrollRow; i < last; i++) rows.Add(new Markup(MarkdownToMarkup(lines[i])));

            string footer = maxScroll > 0
                ? $"[grey]{InfoScrollRow + 1}-{last}/{lines.Length}  ↑↓ PgUp/PgDn scroll · any other key closes[/]"
                : "[grey]any key closes[/]";
            rows.Add(new Markup(footer));

            var panel = new Panel(new Rows(rows))
            {
                Header = new PanelHeader($"[bold yellow] {Markup.Escape(InfoTitle)} [/]", Justify.Left),
                Width = panelWidth,
                Height = panelHeight,
                Border = BoxBorder.Double,
                Padding = new Padding(1, 0, 1, 0)
            };

            // Start a couple rows below the top so it never collides with the tab/header bar.
            // Drawn at column 0: a multi-line widget written after a single SetCursorPosition
            // only honors the column on its first line, so a non-zero column offsets the header.
            int startRow = Math.Max(2, (totalHeight - panelHeight) / 2);
            _console.SetCursorPosition(0, startRow);
            _console.WriteWidget(panel);
        }

        private static readonly System.Text.RegularExpressions.Regex _mdHeader =
            new(@"^(#{1,6})\s+(.*)$", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _mdBold =
            new(@"\*\*(.+?)\*\*", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _mdCode =
            new(@"`([^`]+)`", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Converts a single line of lightweight markdown (headers, **bold**, `code`, code
        /// fences) into Spectre markup. The text is escaped first so brackets in the content
        /// can never be parsed as markup.
        /// </summary>
        public static string MarkdownToMarkup(string raw)
        {
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("```")) return "[grey]────────[/]"; // code fence marker

            // A bare URL becomes a real clickable hyperlink (OSC 8) where the terminal supports it.
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^https?://\S+$"))
                return $"[link={trimmed}][underline blue]{Markup.Escape(trimmed)}[/][/]";

            string escaped = Markup.Escape(raw);

            var header = _mdHeader.Match(escaped);
            if (header.Success)
            {
                string inner = _mdCode.Replace(header.Groups[2].Value, "[cyan]$1[/]");
                return $"[bold yellow]{inner}[/]";
            }

            escaped = _mdBold.Replace(escaped, "[bold]$1[/]");
            escaped = _mdCode.Replace(escaped, "[cyan]$1[/]");
            return escaped;
        }

        private void RenderHelpOverlay(int totalWidth, int totalHeight)
        {
            if (HelpPageIndex == 1)
            {
                RenderSnippetListOverlay(totalWidth, totalHeight);
                return;
            }

            int panelWidth  = Math.Min(92, totalWidth  - 4);
            int panelHeight = Math.Min(32, totalHeight - 4);

            // Build one renderable column of categories from the keybinding catalog.
            IRenderable BuildColumn(IEnumerable<KeyCategory> categories)
            {
                var blocks = new List<IRenderable>();
                foreach (var category in categories)
                {
                    blocks.Add(new Markup($"[bold grey] ── {Markup.Escape(KeyBindings.CategoryTitles[category])} ──[/]"));

                    var section = new Table()
                        .Border(TableBorder.None)
                        .HideHeaders()
                        .AddColumn(new TableColumn("").Width(16))
                        .AddColumn(new TableColumn(""));

                    foreach (var binding in KeyBindings.InCategory(category))
                    {
                        string desc = Markup.Escape(binding.Description);
                        if (binding.LiveAnnotation != null)
                            desc += $"  [grey]{Markup.Escape(binding.LiveAnnotation(this))}[/]";

                        section.AddRow(
                            new Markup($"[yellow]{Markup.Escape(binding.Keys)}[/]"),
                            new Markup(desc));
                    }

                    blocks.Add(section);
                }
                return new Rows(blocks);
            }

            var columns = KeyBindings.HelpColumnLayout();
            var grid = new Table().Border(TableBorder.None).HideHeaders();
            foreach (var _ in columns) grid.AddColumn(new TableColumn(""));
            grid.AddRow(columns.Select(BuildColumn).ToArray());

            var inner = new Rows(
                grid,
                new Markup("[grey] ─────────────────────────────────────────────[/]"),
                new Markup("[yellow]F2[/][grey]: Snippet Reference   ·   any other key to close[/]")
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

        private void RenderSnippetListOverlay(int totalWidth, int totalHeight)
        {
            var snippetTable = new Table()
                .Border(TableBorder.None)
                .HideHeaders()
                .AddColumn(new TableColumn("").Width(14))
                .AddColumn(new TableColumn(""));

            foreach (var s in ETL_SQL.Core.Metadata.SnippetLibrary.Instance.GetAll())
            {
                snippetTable.AddRow(
                    new Markup($"[yellow]{Markup.Escape(s.Trigger)}[/]"),
                    new Markup(Markup.Escape(s.Description)));
            }

            int panelWidth  = Math.Min(72, totalWidth  - 4);
            int panelHeight = Math.Min(32, totalHeight - 4);

            var inner = new Rows(
                snippetTable,
                new Markup("[grey] ─────────────────────────────────────────────[/]"),
                new Markup("[yellow]F2[/][grey]: Keyboard Reference   Press any other key to close[/]")
            );

            var panel = new Panel(inner)
            {
                Header = new PanelHeader("[bold yellow] ETL-SQL Snippet Reference [/]", Justify.Left),
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

        /// <summary>
        /// Builds the markup for the prompt line. The title and value are user/caller
        /// supplied (file paths, search terms, prompt captions) and must be escaped so a
        /// literal '[' or ']' cannot be parsed as Spectre markup and crash the editor.
        /// </summary>
        public static string BuildPromptMarkup(string? title, string displayValue)
            => $" [yellow]{Markup.Escape(title ?? string.Empty)}:[/] {Markup.Escape(displayValue ?? string.Empty)}";

        /// <summary>Draws the clickable bottom-pane tab strip; widths match BottomTabStrip.Segments.</summary>
        private void RenderBottomTabStrip(int row, int totalWidth, BottomTab active)
        {
            _console.ClearLine(0, row, totalWidth);
            _console.SetCursorPosition(0, row);

            var sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (var seg in BottomTabStrip.Segments())
            {
                if (!first) sb.Append("[grey15] [/]"); // one separator column
                first = false;
                string style = seg.Tab == active ? "black on yellow" : "grey85 on grey23";
                sb.Append($"[{style}] {Markup.Escape(seg.Label)} [/]");
            }
            _console.Markup(sb.ToString());
        }

        /// <summary>Switches the bottom pane to the given view (the F4 targets, click-addressable).</summary>
        public void ShowBottomTab(BottomTab tab)
        {
            switch (tab)
            {
                case BottomTab.Results:
                    ResultsVisible = true; PerformanceVisible = false; OutputVisible = false;
                    ShowStatus("View: Query Results");
                    break;
                case BottomTab.Performance:
                    PerformanceVisible = true; ResultsVisible = false; OutputVisible = false; CompareMode = false;
                    ShowStatus("View: Performance Metrics");
                    break;
                case BottomTab.Output:
                    OutputVisible = true; ResultsVisible = false; PerformanceVisible = false; CompareMode = false;
                    ShowStatus("View: Output");
                    break;
                default: // Pipeline & Messages
                    ResultsVisible = false; PerformanceVisible = false; OutputVisible = false; CompareMode = false;
                    ShowStatus("View: Pipeline & Messages");
                    break;
            }
        }

        /// <summary>Draws the right-aligned "◀ i/N ▶" result-set navigator on the strip row.</summary>
        private void RenderResultSetNav(int row, int totalWidth, int index, int count)
        {
            int start = ResultSetNav.StartX(totalWidth, index, count);
            string label = ResultSetNav.FormatLabel(index, count);
            _console.SetCursorPosition(start, row);
            _console.Markup($"[black on cyan] ◀ [/][grey85 on grey23]{Markup.Escape(label)}[/][black on cyan] ▶ [/]");
        }

        /// <summary>
        /// Extends the editor text selection from a drag anchor to the screen point (x, y).
        /// The point is clamped into the editor band and the line length, so dragging past
        /// the edges extends to the nearest valid position.
        /// </summary>
        public void DragExtendSelection(int x, int y, ConsoleEditor editor, int anchorLine, int anchorCol)
        {
            var buffer = editor._buffer;
            if (buffer.Lines.Count == 0) return;

            var layout = LayoutCalculator.Compute(_lastWidth, _lastHeight, buffer.Lines.Count,
                SidebarVisible, SidebarWidth, IsBottomMaximized, CompareMode);

            int clampedY = Math.Clamp(y, layout.EditorAreaTop, layout.EditorAreaTop + layout.EditorAreaHeight - 1);
            int line = Math.Clamp(layout.EditorLineAt(clampedY, ScrollLine), 0, buffer.Lines.Count - 1);
            int col = Math.Clamp(layout.EditorColumnAt(x, ScrollCol), 0, buffer.Lines[line].Length);

            buffer.SelectionStartLine = anchorLine;
            buffer.SelectionStartCol = anchorCol;
            buffer.CursorLine = line;
            buffer.CursorColumn = col;
        }

        /// <summary>Switches the active result set by <paramref name="delta"/> (clamped), resetting scroll/filter.</summary>
        public void CycleResultSet(int delta, int setCount)
        {
            if (setCount <= 1) return;
            int next = Math.Clamp(ActiveResultSetIndex + delta, 0, setCount - 1);
            if (next == ActiveResultSetIndex) return;
            ActiveResultSetIndex = next;
            ResultScrollRow = 0;
            ResultScrollCol = 0;
            FilterText = "";
            ShowStatus($"View: Result Set {next + 1}/{setCount}");
        }

        public void ScrollRegion(int x, int y, int delta)
        {
            var layout = LayoutCalculator.Compute(_lastWidth, _lastHeight, 1,
                SidebarVisible, SidebarWidth, IsBottomMaximized, CompareMode);

            if (layout.InEditorBand(y))
            {
                if (SidebarVisible && x < SidebarWidth)
                {
                    SidebarScrollRow = Math.Max(0, SidebarScrollRow + delta);
                }
                else if (ReportVisible)
                {
                    ReportScrollRow = Math.Max(0, ReportScrollRow + delta);
                }
                else
                {
                    ScrollLine = Math.Max(0, ScrollLine + delta);
                }
            }
            else if (layout.InLowerContent(y))
            {
                if (CompareMode && CompareScrollRows.Count > 0)
                {
                    // Wheel scrolls whichever compare pane the cursor is over.
                    int clickedPaneIndex = Math.Clamp((y - layout.LowerContentTop) / Math.Max(4, layout.LowerContentHeight / Math.Max(1, CompareScrollRows.Count)), 0, CompareScrollRows.Count - 1);
                    CompareScrollRows[clickedPaneIndex] = Math.Max(0, CompareScrollRows[clickedPaneIndex] + delta);
                }
                else if (PerformanceVisible)
                {
                    ResultScrollRow = Math.Max(0, ResultScrollRow + delta);
                }
                else if (ResultsVisible)
                {
                    ResultScrollRow = Math.Max(0, ResultScrollRow + delta);
                }
                else
                {
                    if (x < _lastWidth * 0.35)
                    {
                        TreeScrollRow = Math.Max(0, TreeScrollRow + delta);
                    }
                    else
                    {
                        MessageScrollRow = Math.Max(0, MessageScrollRow + delta);
                    }
                }
            }
        }

        public async Task HandleMouseClick(int button, int x, int y, bool isRelease, ConsoleEditor editor)
        {
            if (isRelease) return;

            if (button == 64)
            {
                ScrollRegion(x, y, -3);
                return;
            }
            if (button == 65)
            {
                ScrollRegion(x, y, 3);
                return;
            }

            if (button != 0) return;

            // Report preview: click the page arrows on the top border row; ignore other clicks.
            if (ReportVisible)
            {
                if (y == LayoutCalculator.EditorAreaTopRows)
                {
                    if (x >= _lastWidth - 8 && x < _lastWidth - 5) ReportPrevPage();
                    else if (x >= _lastWidth - 5 && x < _lastWidth - 2) ReportNextPage();
                }
                return;
            }

            // Help bar (second-to-last row): clicking a shortcut runs it via its key.
            if (y == _lastHeight - 2)
            {
                var btn = StatusBar.HitTest(x);
                if (btn != null)
                {
                    await editor.HandleKey(btn.ToKeyInfo());
                    ForceFullRepaint();
                }
                return;
            }

            if (y == 1)
            {
                var labels = editor._tabs.Select(t => TabBarLayout.Label(t.FilePath, t.IsDirty)).ToList();
                foreach (var seg in TabBarLayout.Tabs(labels))
                {
                    if (x >= seg.StartX && x < seg.StartX + seg.Width)
                    {
                        if (x >= seg.CloseX) // the "x" and the column after it close the tab
                        {
                            if (seg.Index == editor._activeTabIndex)
                            {
                                await editor.CloseActiveTab();
                            }
                            else
                            {
                                editor.SwitchToTab(seg.Index);
                                await editor.CloseActiveTab();
                            }
                        }
                        else
                        {
                            editor.SwitchToTab(seg.Index);
                        }
                        ForceFullRepaint();
                        return;
                    }
                }

                int plusX = TabBarLayout.PlusStartX(labels);
                if (x >= plusX && x < plusX + TabBarLayout.PlusWidth)
                {
                    await editor.NewTab();
                    ForceFullRepaint();
                    return;
                }
                return;
            }

            var layout = LayoutCalculator.Compute(_lastWidth, _lastHeight, editor._buffer.Lines.Count,
                SidebarVisible, SidebarWidth, IsBottomMaximized, CompareMode);

            if (layout.InSidebar(x, y) && !ReportVisible)
            {
                Focus = EditorFocus.Sidebar;
                int clickedIndex = layout.SidebarItemIndexAt(y, SidebarScrollRow);
                var items = _sidebarPanel.GetFlatVisibleItems();
                if (clickedIndex >= 0 && clickedIndex < items.Count)
                {
                    bool wasSelected = SidebarSelectedIndex == clickedIndex;
                    SidebarSelectedIndex = clickedIndex;
                    if (wasSelected)
                    {
                        await _sidebarPanel.HandleEnter(editor);
                    }
                    else
                    {
                        ForceFullRepaint();
                    }
                }
            }
            else if (layout.InEditorBand(y) && !ReportVisible)
            {
                Focus = EditorFocus.Editor;

                int clickLine = layout.EditorLineAt(y, ScrollLine);
                int clickCol = layout.EditorColumnAt(x, ScrollCol);

                if (clickLine >= 0 && clickLine < editor._buffer.Lines.Count)
                {
                    editor._buffer.CursorLine = clickLine;
                    editor._buffer.CursorColumn = Math.Clamp(clickCol, 0, editor._buffer.Lines[clickLine].Length);
                    editor._buffer.SelectionStartLine = null;
                }
            }
            else if (layout.InLowerPane(y))
            {
                if (layout.OnBottomTabStrip(y))
                {
                    // Result-set arrows (right side) take precedence over the tabs in Results view.
                    int navDelta = (ResultsVisible && !CompareMode)
                        ? ResultSetNav.HitTest(x, _lastWidth, ActiveResultSetIndex, editor._evaluator.LastResultSets.Count)
                        : 0;
                    if (navDelta != 0)
                    {
                        CycleResultSet(navDelta, editor._evaluator.LastResultSets.Count);
                        ForceFullRepaint();
                    }
                    else
                    {
                        var clicked = BottomTabStrip.HitTest(x);
                        if (clicked.HasValue)
                        {
                            ShowBottomTab(clicked.Value);
                            ForceFullRepaint();
                        }
                    }
                }
                else if (CompareMode)
                {
                    // Click selects the compare pane to scroll (F7 leaves ResultsVisible false).
                    Focus = EditorFocus.Results;
                    int paneCount = editor._evaluator.LastResultSets.Count;
                    if (paneCount > 0)
                    {
                        int paneHeight = Math.Max(4, layout.LowerContentHeight / Math.Max(1, paneCount));
                        int clickedPaneIndex = Math.Clamp((y - layout.LowerContentTop) / paneHeight, 0, paneCount - 1);
                        CompareFocusIndex = clickedPaneIndex;
                        ForceFullRepaint();
                    }
                }
                else if (OutputVisible)
                {
                    Focus = EditorFocus.Output;
                    int row = (y - layout.LowerContentTop - 1) + OutputScrollRow; // -1 for the panel border
                    if (row >= 0 && row < OutputEntries.Count) OutputSelectedIndex = row;
                    ForceFullRepaint();
                }
                else if (PerformanceVisible)
                {
                    Focus = EditorFocus.Performance;
                }
                else if (ResultsVisible)
                {
                    Focus = EditorFocus.Results;
                }
                else
                {
                    if (x < _lastWidth * 0.35)
                    {
                        Focus = EditorFocus.ExecutionTree;
                    }
                    else
                    {
                        Focus = EditorFocus.Messages;
                    }
                }
            }
        }
    }
}
