using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
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

                default:
                    // "ide" or no mode → launch the full Terminal IDE window
                    TerminalIdeWindow.Launch(ctx, serviceProvider);
                    return 0;
            }
        }
    }
}
