using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Displays a terminal-based report preview in the IDE.
    /// Implements Phase 5 (Research Paper View) of the TUI graphical previews.
    /// </summary>
    public class ReportPreviewPanel : IUIComponent
    {
        private readonly EditorRenderer _renderer;

        public ReportPreviewPanel(EditorRenderer renderer)
        {
            _renderer = renderer;
        }

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0)
        {
            // Clear area
            for (int i = 0; i < height; i++)
            {
                console.ClearLine(x, y + i, width);
            }

            var manifest = _renderer.CurrentReportManifest;
            if (manifest == null || manifest.Pages.Count == 0)
            {
                console.SetCursorPosition(x, y);
                console.WriteWidget(new Panel(new Text("No report definitions found in active script.\nUse CREATE PAGE and CREATE VISUAL to build a report.", new Style(Color.Grey)))
                    .Header("Report Preview")
                    .Border(BoxBorder.Rounded)
                    .Expand());
                return;
            }

            // Select the active page (Research Paper mode: usually Page 0)
            var activePageIndex = Math.Clamp(_renderer.ActiveReportPageIndex, 0, manifest.Pages.Count - 1);
            var page = manifest.Pages[activePageIndex];

            // Render the page using the shared reporting terminal renderer.
            var reportContent = TerminalRenderer.RenderPage(page, manifest);

            // ── Scrolling Implementation ──
            // We render to segments and then slice by line.
            var renderOptions = new RenderOptions(console.Capabilities, new Size(width, 5000));
            var segments = reportContent.Render(renderOptions, width).ToList();
            
            // Group segments into lines
            var lines = new List<List<Segment>>();
            var currentLine = new List<Segment>();
            foreach (var segment in segments)
            {
                if (segment.IsLineBreak)
                {
                    lines.Add(currentLine);
                    currentLine = new List<Segment>();
                }
                else
                {
                    currentLine.Add(segment);
                }
            }
            if (currentLine.Any()) lines.Add(currentLine);

            // Clamp scroll position
            int maxScroll = Math.Max(0, lines.Count - height + 4);
            _renderer.ReportScrollRow = Math.Clamp(_renderer.ReportScrollRow, 0, maxScroll);
            
            // Take the visible slice
            var visibleLines = lines.Skip(_renderer.ReportScrollRow).Take(height - 2).ToList();
            var visibleContent = new List<IRenderable>();
            foreach (var line in visibleLines)
            {
                visibleContent.Add(new RawLine(line));
            }

            string pageInfo = $"[cyan]Page {activePageIndex + 1}/{manifest.Pages.Count}: {page.Name} (Line {_renderer.ReportScrollRow + 1}/{lines.Count})[/]";
            var borderColor = _renderer.ResultsFocus ? Color.Yellow : Color.Blue; 

            var panel = new Panel(new Rows(visibleContent))
            {
                Header = new PanelHeader(pageInfo),
                Height = height,
                Width = width,
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(borderColor),
                Padding = new Padding(1, 0, 1, 0)
            };

            try
            {
                console.SetCursorPosition(x, y);
                console.WriteWidget(panel);
            }
            catch (Exception ex)
            {
                console.SetCursorPosition(x, y);
                console.Markup($"[red]Render Error: {Markup.Escape(ex.Message)}[/]");
            }
        }

        /// <summary>
        /// A simple renderable that preserves original Spectre segments (including colors/styles).
        /// </summary>
        private class RawLine : IRenderable
        {
            private readonly List<Segment> _segments;
            public RawLine(List<Segment> segments) => _segments = segments;
            public Measurement Measure(RenderOptions options, int maxWidth) => new Measurement(maxWidth, maxWidth);
            public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
            {
                foreach (var s in _segments) yield return s;
                yield return Segment.LineBreak;
            }
        }
    }
}
