using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.TUI
{
    /// <summary>
    /// Entry point for TUI command dispatch. Mirrors EngineRunner in the App project
    /// but handles only UI-mode commands (ide, repl, simple).
    /// </summary>
    public static class TuiRunner
    {
        public static async Task<int> Run(CliContext ctx, IServiceProvider serviceProvider)
        {
            if (!string.Equals(ctx.UiMode, "repl", StringComparison.OrdinalIgnoreCase))
            {
                // Enforce UTF-8 for consistent symbol rendering
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                TuiTheme.Load();

                // Disable Ctrl+C interrupt so we can use it for Copy
                if (OperatingSystem.IsWindows())
                {
                    try { Console.TreatControlCAsInput = true; } catch { }
                    
                    // Stabilize buffer: if buffer > window, TUI will drift/scroll on last line writes
                    if (!Console.IsOutputRedirected)
                    {
                        try 
                        { 
                            if (Console.BufferHeight > Console.WindowHeight)
                                Console.BufferHeight = Console.WindowHeight; 
                        } 
                        catch { }
                    }
                }
                Console.Clear();
            }

            switch (ctx.UiMode?.ToLower())
            {
                case "repl":
                {
                    var repl = new ReplUi(ctx, serviceProvider);
                    await repl.RunAsync();
                    return 0;
                }
                case "simple":
                {
                    var simpleUi = new SimpleUi(ctx, serviceProvider);
                    await simpleUi.RunAsync();
                    return 0;
                }
                case "ide":
                default:
                {
                    var editor = TuiDependencyInjectionSetup.CreateEditor(
                        serviceProvider,
                        ctx.ScriptFile?.FullName ?? "untitled.etlsql",
                        new System.Collections.Generic.Dictionary<string, IDataSource>());
                    await editor.InitializeAsync();
                    await editor.Run();
                    return 0;
                }
            }
        }
    }
}
