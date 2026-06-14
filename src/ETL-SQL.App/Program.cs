using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

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
            // Ensure consistent encoding for IDE communication
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            try
            {
                var isHelpOnly = args.Any(a => a is "--help" or "-h" or "-?");
                if (isHelpOnly && args.Length > 1)
                {
                    var helpCommand = CliOrchestrator.BuildRootCommand(_ => Task.FromResult(0));
                    var parseResult = helpCommand.Parse(args, null);
                    return await parseResult.InvokeAsync(new InvocationConfiguration(), default);
                }

                var isDoctorJson = args.Any(a => string.Equals(a, "doctor", StringComparison.OrdinalIgnoreCase))
                    && args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

                // Diagnostic breadcrumb for IDEs
                if (!isDoctorJson)
                    Console.Error.WriteLine("[PROC_START] ETL-SQL Engine process identified.");

                ServiceProvider = DependencyInjectionSetup.BuildServiceProvider();
                if (!isDoctorJson)
                    Console.Error.WriteLine("[DI_READY] Dependency injection logic completed.");


                // Start scheduler only for interactive/daemon modes, not for one-shot script execution
                bool isOneShot = args.Length > 0 && (args[0] == "run" || args[0] == "--run" || args[0] == "doctor" || args[0] == "purge" || args[0] == "admin" || args[0] == "init");
                if (!isOneShot)
                {
                    try
                    {
                        var scheduler = ServiceProvider.GetRequiredService<SchedulerService>();
                        scheduler.Start();
                        Console.Error.WriteLine("[SCHEDULER_START] Background scheduler is active.");
                        AppDomain.CurrentDomain.ProcessExit += (s, e) => scheduler.Stop();
                    }
                    catch (Exception schedEx)
                    {
                        Console.Error.WriteLine($"[SCHEDULER_WARN] Background scheduler failed to start. Scheduled jobs will not run in this session. Error: {schedEx.Message}");
                    }
                }

                if (args.Length == 0 || (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "-?")))
                {
                    CliOrchestrator.ShowAdvancedHelp();
                    return 0;
                }

                var rootCommand = CliOrchestrator.BuildRootCommand(async (ctx) =>
                {
                    return await EngineRunner.Run(ctx);
                });

                var rootParseResult = rootCommand.Parse(args, null);
                return await rootParseResult.InvokeAsync(new InvocationConfiguration(), default);
            }
            catch (Exception ex)
            {
                // Ensure any fatal startup error is visible to the IDE even before JSON protocol starts
                Console.Error.WriteLine($"[FATAL_STARTUP_ERROR] {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
