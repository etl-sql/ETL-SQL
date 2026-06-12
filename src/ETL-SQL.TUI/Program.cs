using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.TUI
{
    public class Program
    {
        /// <summary>Singleton service provider — referenced by ReplUi for Evaluator access.</summary>
        public static IServiceProvider ServiceProvider { get; set; } = null!;

        static async Task<int> Main(string[] args)
        {
            try
            {
                ServiceProvider = TuiDependencyInjectionSetup.BuildServiceProvider();


                var scheduler = ServiceProvider.GetRequiredService<SchedulerService>();
                scheduler.Start();
                AppDomain.CurrentDomain.ProcessExit += (_, _) => scheduler.Stop();

                var ctx = BuildContext(args);
                return await TuiRunner.Run(ctx, ServiceProvider);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FATAL] {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        /// <summary>
        /// Minimal arg parsing for the TUI executable.
        /// Supported forms:
        ///   etl-sql-tui                    → open Terminal IDE (empty script)
        ///   etl-sql-tui path/to/script.sql → open Terminal IDE with file pre-loaded
        ///   etl-sql-tui --repl             → start REPL (used by IDE extensions)
        ///   etl-sql-tui --simple           → start simple menu UI
        /// </summary>
        private static CliContext BuildContext(string[] args)
        {
            var ctx = new CliContext { Command = "ui", UiMode = "ide" };

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--repl":
                        ctx.UiMode = "repl";
                        break;
                    case "--simple":
                        ctx.UiMode = "simple";
                        break;
                    case "--verbose":
                    case "-v":
                        ctx.IsVerbose = true;
                        break;
                    default:
                        // Positional arg → treat as UI mode or script file
                        if (!args[i].StartsWith("-"))
                        {
                            var argLower = args[i].ToLower();
                            if (argLower == "ui") continue;
                            if (argLower == "simple" || argLower == "repl" || argLower == "ide")
                            {
                                ctx.UiMode = argLower;
                                continue;
                            }

                            var fi = new FileInfo(args[i]);
                            if (fi.Exists) ctx.ScriptFile = fi;
                        }
                        break;
                }
            }

            return ctx;
        }
    }
}
