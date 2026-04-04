using Spectre.Console;

namespace ETL_SQL.UI
{
    public interface IUIComponent
    {
        void Render(IConsoleInterface console, int x, int y, int width, int height);
    }
}
