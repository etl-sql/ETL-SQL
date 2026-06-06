using System;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ETL_SQL.TUI.UI
{
    public interface IConsoleInterface
    {
        int WindowWidth { get; }
        int WindowHeight { get; }
        bool CursorVisible { get; set; }
        IReadOnlyCapabilities Capabilities { get; }
        
        void SetCursorPosition(int left, int top);
        ConsoleKeyInfo ReadKey(bool intercept);
        void Write(string value);
        void Clear();
        
        // Spectre.Console integration
        void Markup(string markup);
        void WriteWidget(IRenderable widget);
        void ClearLine(int left, int top, int width);
    }

    public class PhysicalConsole : IConsoleInterface
    {
        public int WindowWidth => AnsiConsole.Console.Profile.Width;
        public int WindowHeight => AnsiConsole.Console.Profile.Height;
        public bool CursorVisible { 
            get => true; 
            set { if (value) AnsiConsole.Console.Cursor.Show(); else AnsiConsole.Console.Cursor.Hide(); } 
        }
        public IReadOnlyCapabilities Capabilities => AnsiConsole.Console.Profile.Capabilities;

        public void SetCursorPosition(int left, int top) => AnsiConsole.Console.Cursor.SetPosition(left + 1, top + 1);
        public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
        public void Write(string value) => AnsiConsole.Console.Write(value);
        public void Clear() => AnsiConsole.Console.Clear();

        public void Markup(string markup) => AnsiConsole.Console.Write(new Markup(markup));
        public void WriteWidget(IRenderable widget) => AnsiConsole.Console.Write(widget);

        public void ClearLine(int left, int top, int width)
        {
            try
            {
                SetCursorPosition(left, top);
                // Clear exactly the specified width using spaces to prevent full-line wipe
                // sequences (\x1b[2K) from destroying adjacent side-by-side panels.
                AnsiConsole.Console.Write(new string(' ', width));
                SetCursorPosition(left, top);
            }
            catch { /* Ignore terminal out-of-bounds */ }
        }
    }
}
