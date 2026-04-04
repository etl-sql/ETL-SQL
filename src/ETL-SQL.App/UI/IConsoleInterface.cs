using System;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ETL_SQL.UI
{
    public interface IConsoleInterface
    {
        int WindowWidth { get; }
        int WindowHeight { get; }
        bool CursorVisible { get; set; }
        
        void SetCursorPosition(int left, int top);
        ConsoleKeyInfo ReadKey(bool intercept);
        void Write(string value);
        void Clear();
        
        // Spectre.Console integration
        void Markup(string markup);
        void WriteWidget(IRenderable widget);
    }

    public class PhysicalConsole : IConsoleInterface
    {
        public int WindowWidth => AnsiConsole.Console.Profile.Width;
        public int WindowHeight => AnsiConsole.Console.Profile.Height;
        public bool CursorVisible { get => OperatingSystem.IsWindows() && Console.CursorVisible; set { if (OperatingSystem.IsWindows()) Console.CursorVisible = value; } }

        public void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);
        public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
        public void Write(string value) => Console.Write(value);
        public void Clear() => Console.Clear();

        public void Markup(string markup) => AnsiConsole.Markup(markup);
        public void WriteWidget(IRenderable widget) => AnsiConsole.Write(widget);
    }
}
