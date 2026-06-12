using System;
using System.IO;
using System.Runtime.CompilerServices;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ETL_SQL.Tests.Core
{
    public static class TestSetup
    {
        [ModuleInitializer]
        public static void InitializeTests()
        {
            try
            {
                // ── Test file logger ─────────────────────────────────────────────
                // Use the centralized Logger for test output to ensure all engine 
                // messages are captured in the logs/tests folder.
                var sp = DependencyInjectionSetup.BuildServiceProvider();
                var loggerService = sp.GetRequiredService<ILoggerService>();
                loggerService.InitializeTestLogger("logs/tests");

                // ── DI container shared across tests ─────────────────────────────
                ETL_SQL.Program.ServiceProvider = sp;
                // TUI types (ConsoleEditor, ReplUi) resolve Program → ETL_SQL.TUI.Program
                ETL_SQL.TUI.Program.ServiceProvider = sp;

                // Force initialization of ConnectorRegistry.Instance
                sp.GetService<IConnectorRegistry>();

                // ── Security Hardening for Tests ──────────────────────────────
                // Explicitly enable TestMode to allow access to the bin/debug folder
                var securityService = sp.GetRequiredService<SecurityService>();
                securityService.IsTestMode = true;

                // Suppress console noise (ETL-SQL.Common.Logger)
                if (loggerService is LoggerService ls) ls.SuppressConsole = true;
            }
            catch (Exception ex)
            {
                // Critical failure during test initialization. 
                // Write to Error console so it appears in the test runner output.
                Console.Error.WriteLine($"FATAL: Test initialization failed: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                throw; // rethrow to fail the test run immediately and visibly
            }
        }

        private static void PurgeOldLogs(string directory, int days)
        {
            if (days <= 0) return;
            var cutoff = DateTime.Now.AddDays(-days);
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
            }
            catch { /* best-effort */ }
        }
    }

    public static class TestExtensions
    {
        public static Script TokenizeToScript(this Lexer lexer)
        {
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
