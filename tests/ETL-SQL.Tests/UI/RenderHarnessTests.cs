using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Render harness: drives a real <see cref="EditorRenderer"/> against a recording console at
    /// many sizes (incl. below the minimum) and asserts no cursor/clear write lands out of bounds.
    /// </summary>
    public class RenderHarnessTests
    {
        static RenderHarnessTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        // Captures the coordinate writes the editor makes and flags any outside the viewport.
        private sealed class RecordingConsole : IConsoleInterface
        {
            public int W, H;
            public readonly List<string> OutOfBounds = new();
            public bool CursorVisible { get; set; }
            public int WindowWidth => W;
            public int WindowHeight => H;
            public IReadOnlyCapabilities Capabilities => AnsiConsole.Console.Profile.Capabilities;

            public void SetCursorPosition(int left, int top)
            {
                if (left < 0 || top < 0 || left >= W || top >= H)
                    OutOfBounds.Add($"SetCursor({left},{top}) in {W}x{H}");
            }
            public void ClearLine(int left, int top, int width)
            {
                if (left < 0 || top < 0 || top >= H || left + width > W)
                    OutOfBounds.Add($"ClearLine({left},{top},{width}) in {W}x{H}");
            }
            public ConsoleKeyInfo ReadKey(bool intercept) => default;
            public void Write(string value) { }
            public void Clear() { }
            public void Markup(string markup) { }
            public void WriteWidget(IRenderable widget) { }
        }

        private static ConsoleEditor NewEditor()
        {
            var e = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            e._renderer.Headless = true;
            return e;
        }

        [Theory]
        [InlineData(120, 40)]
        [InlineData(80, 24)]
        [InlineData(40, 10)]  // the supported minimum
        [InlineData(30, 8)]   // below minimum -> "too small" fallback
        [InlineData(10, 5)]
        [InlineData(1, 1)]
        public async Task Render_NeverWritesOutOfBounds_AcrossSizesAndPanels(int w, int h)
        {
            var editor = NewEditor();
            editor._buffer.Load(new[] { "SELECT 1;", "SELECT 2;" });
            await editor.RunScript();
            await editor.WaitForRunAsync(); // populate results/messages/tree

            var fake = new RecordingConsole();
            // A standalone renderer that draws to the recording console using the editor's data.
            var r = new EditorRenderer(editor._buffer, editor._evaluator, fake);

            foreach (var (sidebar, lower) in new (bool, string)[]
                { (false, "pipeline"), (false, "results"), (false, "performance"),
                  (false, "output"), (false, "variables"), (true, "results") })
            {
                r.SidebarVisible = sidebar;
                r.ResultsVisible = lower == "results";
                r.PerformanceVisible = lower == "performance";
                r.OutputVisible = lower == "output";
                r.VariablesVisible = lower == "variables";

                fake.W = w; fake.H = h;
                r.ForceFullRepaint();
                r.Render(editor, w, h);
            }

            Assert.True(fake.OutOfBounds.Count == 0,
                "Out-of-bounds writes: " + string.Join("; ", fake.OutOfBounds));
        }
    }
}
