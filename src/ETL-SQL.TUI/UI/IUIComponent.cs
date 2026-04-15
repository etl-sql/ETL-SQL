using Spectre.Console;

namespace ETL_SQL.TUI.UI
{
    public interface IUIComponent
    {
        void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0);
    }
}
