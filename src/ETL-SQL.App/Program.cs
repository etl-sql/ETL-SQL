using System;
using System.Linq;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.CommandLine.Invocation;
using Spectre.Console;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.UI;
using ETL_SQL.Common;
using ETL_SQL.App;
using ETL_SQL.Engine.Scheduling;

namespace ETL_SQL
{
    /// <summary>
    /// Entry point for the ETL-SQL Console Application.
    /// Handles dependency injection setup, background scheduling, and CLI command orchestration.
    /// </summary>
    public class Program
    {
        /// <summary>Gets or sets the singleton service provider for the application.</summary>
        public static IServiceProvider ServiceProvider { get; set; } = null!;

        /// <summary>The main entry point for the application.</summary>
        /// <param name="args">The command-line arguments.</param>
        /// <returns>Exit code (0 for success, non-zero for failure).</returns>
        static async Task<int> Main(string[] args)
        {
            ServiceProvider = DependencyInjectionSetup.BuildServiceProvider();
            Logger.Factory = ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();

            // Start scheduler
            var scheduler = ServiceProvider.GetRequiredService<SchedulerService>();
            scheduler.Start();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => scheduler.Stop();

            if (args.Length == 0 || (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "-?")))
            {
                CliOrchestrator.ShowAdvancedHelp();
                return 0;
            }

            var rootCommand = CliOrchestrator.BuildRootCommand(async (ctx) =>
            {
                return await EngineRunner.Run(ctx);
            });

            return await rootCommand.InvokeAsync(args);
        }
    }
}
