using System;
using Spectre.Console;
using ETL_SQL.Core;
using ETL_SQL.Common;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// A TUI-based panel that renders the graphical execution tree.
    /// Provides real-time visual feedback on the progress of statements in a script.
    /// </summary>
    public class TreePanel(Evaluator evaluator, EditorRenderer renderer)
    {
        private readonly Evaluator _evaluator = evaluator;
        private readonly EditorRenderer _renderer = renderer;

        public void Render(IConsoleInterface console, int x, int y, int width, int height)
        {
            if (height <= 0 || width <= 0) return;

            var tree = _evaluator.ExecutionTree;
            var visualizer = new ExecuteTreeVisualizer(tree);
            
            // Generate the Spectre.Console Table renderable
            var treeWidget = visualizer.CreateRenderable();
            
            var borderColor = _renderer.ResultsFocus ? Color.Yellow : Color.Cyan;
            
            // Wrap in a panel to provide a border and header
            var panel = new Panel(treeWidget)
            {
                Header = new PanelHeader("[bold cyan] Execution Pipeline [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(borderColor),
                Height = height,
                Width = width
            };

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }
    }
}
