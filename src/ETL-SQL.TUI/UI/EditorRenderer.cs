using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using ETL_SQL.Core;

namespace ETL_SQL.TUI.UI
{
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

        public bool ResultsFocus { get; set; } = false;
        public int ResultScrollRow { get; set; } = 0;
        public int ActiveResultSetIndex { get; set; } = 0;

        public string? PromptTitle { get; set; }
        public string PromptValue { get; set; } = "";
        public int PromptCursor { get; set; } = 0;
        public List<string> PromptSuggestions { get; set; } = new();
        public int PromptSuggestionIndex { get; set; } = 0;
        public bool PromptIsSecret { get; set; } = false;
        public bool HelpVisible { get; set; } = false;
        public bool PerformanceVisible { get; set; } = false;
        public bool TreeVisible { get; set; } = false;
        public bool PromptVisible => !string.IsNullOrEmpty(PromptTitle);

        private readonly IConsoleInterface _console;
        private readonly EditorPanel _editorPanel;
        private readonly MessagePanel _messagePanel;
        private readonly ResultsPanel _resultsPanel;
        private readonly PerformancePanel _performancePanel;
        private readonly TreePanel _treePanel;

        /// <summary>Initializes a new instance of the <see cref="EditorRenderer"/> class.</summary>
        /// <param name="buffer">The editor text buffer.</param>
        /// <param name="evaluator">The current execution context.</param>
        /// <param name="console">Optional console abstraction for testing or alternative outputs.</param>
        public EditorRenderer(EditorBuffer buffer, Evaluator evaluator, IConsoleInterface? console = null)
        {
            _console = console ?? new PhysicalConsole();
            _editorPanel = new EditorPanel(buffer, this);
            _messagePanel = new MessagePanel(evaluator);
            _resultsPanel = new ResultsPanel(evaluator, this);
            _performancePanel = new PerformancePanel(evaluator);
            _treePanel = new TreePanel(evaluator);
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
            if (!Headless) _console.CursorVisible = false;

            // Layout definitions
            int editorAreaTop = 1;
            int statusHeight = 1;
            int messageAreaHeight = 4;
            int resultAreaHeight = 6;
            int editorAreaHeight = Math.Max(1, totalHeight - resultAreaHeight - messageAreaHeight - statusHeight - editorAreaTop);

            // Viewport clamping
            if (buffer.CursorLine < ScrollLine) ScrollLine = buffer.CursorLine;
            if (buffer.CursorLine >= ScrollLine + editorAreaHeight) ScrollLine = buffer.CursorLine - editorAreaHeight + 1;

            if (evaluator.LastResult != null)
            {
                int maxScroll = Math.Max(0, evaluator.LastResult.Rows.Count - (resultAreaHeight - 4));
                ResultScrollRow = Math.Clamp(ResultScrollRow, 0, maxScroll);
                if (ActiveResultSetIndex >= evaluator.LastResultSets.Count) ActiveResultSetIndex = Math.Max(0, evaluator.LastResultSets.Count - 1);
            }
            else
            {
                ResultScrollRow = 0;
                ActiveResultSetIndex = 0;
            }

            int gutterWidth = (buffer.Lines.Count).ToString().Length + 2;
            int editorWidth = totalWidth - gutterWidth - 1;
            if (buffer.CursorColumn < ScrollCol) ScrollCol = buffer.CursorColumn;
            if (buffer.CursorColumn >= ScrollCol + editorWidth) ScrollCol = buffer.CursorColumn - editorWidth + 1;

            // 1. Header
            if (!Headless)
            {
                _console.SetCursorPosition(0, 0);
                string fileLabel = string.IsNullOrEmpty(filePath) ? "Untitled.etlsql" : System.IO.Path.GetFileName(filePath);
                string header = $" ETL-SQL Console | {fileLabel}{(isDirty ? "*" : "")} ".PadRight(totalWidth - 1);
                _console.Markup($"[white on grey15]{Markup.Escape(header)}[/]");
            }

            // 2. Main Panels (Panels should also respect headless if they touch console, but they usually just return strings or use IRenderable)
            // Actually, Panels take the console now.
            if (!Headless)
            {
                _editorPanel.Render(_console, 0, editorAreaTop, totalWidth, editorAreaHeight);
                _messagePanel.Render(_console, 0, editorAreaTop + editorAreaHeight, totalWidth, messageAreaHeight);
                
                if (TreeVisible)
                    _treePanel.Render(_console, 0, editorAreaTop + editorAreaHeight + messageAreaHeight, totalWidth, resultAreaHeight);
                else if (PerformanceVisible)
                    _performancePanel.Render(_console, 0, editorAreaTop + editorAreaHeight + messageAreaHeight, totalWidth, resultAreaHeight);
                else
                    _resultsPanel.Render(_console, 0, editorAreaTop + editorAreaHeight + messageAreaHeight, totalWidth, resultAreaHeight);
            }

            // 4. Status Bar
            if (!Headless)
            {
                int statusRow = totalHeight - 1;
                _console.SetCursorPosition(0, statusRow);
                _console.Markup($"[white on grey15]{new string(' ', totalWidth)}[/]");
                _console.SetCursorPosition(0, statusRow);

                var debugInfo2 = $"Ln {buffer.CursorLine + 1}, Col {buffer.CursorColumn + 1}";
                var status2 = (DateTime.Now < StatusMessageExpiry) ? $" | {Markup.Escape(StatusMessage ?? "")}" : "";
                var focusInfo2 = ResultsFocus ? " FOCUS: RESULTS" : " FOCUS: EDITOR";
                var perfLabel = TreeVisible ? " F4:Results " : (PerformanceVisible ? " F4:Tree    " : " F4:Perf    ");
                
                // Build status text components
                string shortcuts = " F1:Help ^S:Save ^O:Open ^F:Find F5:Run | F3:Focus |" + perfLabel + "| F6:Tree |" + focusInfo2;
                string cursor = " | " + debugInfo2 + status2;
                
                // Combine and ensure it fits the width
                string plainStatus2 = shortcuts + cursor;
                if (plainStatus2.Length > totalWidth)
                {
                    // If too long, try removing focusInfo first
                    shortcuts = " F1:Help ^S:Save ^O:Open ^F:Find F5:Run | F3:F |" + perfLabel;
                    plainStatus2 = shortcuts + cursor;
                    
                    if (plainStatus2.Length > totalWidth)
                    {
                        // Still too long, truncate from right and ensure we don't wrap
                        plainStatus2 = plainStatus2.Substring(0, Math.Max(0, totalWidth));
                    }
                }
                
                string renderedStatus = plainStatus2.PadRight(totalWidth);
                _console.Markup($"[white on grey15]{Markup.Escape(renderedStatus)}[/]");
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
                int cursorX = (PromptTitle?.Length ?? 0) + 4 + PromptCursor;
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
            if (!ResultsFocus && !Headless && !HelpVisible)
            {
                _console.SetCursorPosition((buffer.CursorColumn - ScrollCol) + gutterWidth, (buffer.CursorLine - ScrollLine) + editorAreaTop);
                _console.CursorVisible = true;
            }
        }

        private void RenderHelpOverlay(int totalWidth, int totalHeight)
        {
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Yellow).Expand();
            table.AddColumn("[yellow]Shortcut[/]");
            table.AddColumn("[white]Description[/]");
            
            table.AddRow("F1", "Show/Hide this help screen");
            table.AddRow("F3", "Toggle Focus (Editor vs Results)");
            table.AddRow("F5", "Run entire script");
            table.AddRow("Shift+F5", "Run statement at cursor");
            table.AddRow("Ctrl+S", "Save current script");
            table.AddRow("Ctrl+O", "Open script (with file autocomplete)");
            table.AddRow("Ctrl+N", "New script");
            table.AddRow("Ctrl+F", "Find text");
            table.AddRow("Ctrl+I / Alt+F", "Format script (SQL Beautifier)");
            table.AddRow("Ctrl+H", "Replace text");
            table.AddRow("Ctrl+G", "Go to line");
            table.AddRow("Ctrl+X", "Exit or Cut (if text selected)");
            table.AddRow("Ctrl+C / Ctrl+V", "Copy / Paste");
            table.AddRow("Ctrl+Z / Ctrl+Y", "Undo / Redo");
            table.AddRow("Ctrl+D / Ctrl+K", "Duplicate / Delete Line");
            table.AddRow("Ctrl+Home/End", "Go to start/end of script");
            table.AddRow("Arrows / Tab", "Navigate / Cycle Suggestions");
            table.AddRow("Shift+Arrows", "Select text");

            var panel = new Panel(table)
            {
                Height = Math.Min(22, totalHeight - 4),
                Width = Math.Min(60, totalWidth - 4),
                Border = BoxBorder.Double
            };

            int startRow = (totalHeight - (panel.Height ?? 20)) / 2;
            int startCol = (totalWidth - (panel.Width ?? 60)) / 2;

            _console.SetCursorPosition(0, startRow);
            _console.WriteWidget(new Padder(panel, new Padding(startCol, 0, 0, 0)));
        }

        /// <summary>Displays a temporary status message in the status bar.</summary>
        public void ShowStatus(string message) { StatusMessage = message; StatusMessageExpiry = DateTime.Now.AddSeconds(3); }
    }
}
