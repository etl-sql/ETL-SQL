using System.Linq;
using Spectre.Console;
using ETL_SQL.Core;

namespace ETL_SQL.TUI.UI
{
    public class MessagePanel : IUIComponent
    {
        private readonly Evaluator _evaluator;

        public MessagePanel(Evaluator evaluator)
        {
            _evaluator = evaluator;
        }

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0)
        {
            for (int i = 0; i < height; i++)
            {
                console.SetCursorPosition(x, y + i);
                console.Write(new string(' ', width));
            }

            var messages = _evaluator.Messages.Skip(scrollRow).Take(height - 2).ToList();
            var content = string.Join("\n", messages.Select(m => Markup.Escape(m)));
            if (string.IsNullOrEmpty(content)) content = "[grey]No system messages.[/]";

            string headerTitle = "[yellow]Messages[/]";
            if (scrollRow > 0) headerTitle += $" [grey](Scrolled: {scrollRow})[/]";

            var panel = new Panel(content)
            {
                Header = new PanelHeader(headerTitle),
                Height = height,
                Width = width,
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }
    }
}
