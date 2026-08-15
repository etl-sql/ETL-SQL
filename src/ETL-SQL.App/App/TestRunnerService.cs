using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace ETL_SQL.App;

/// <summary>
/// Native Unit Testing &amp; Table Assertion Runner.
/// Discovers and executes *.test.etlsql and *.test.sql test scripts in isolated scopes,
/// reporting tabular test results and returning non-zero exit codes on assertion failure.
/// </summary>
public static class TestRunnerService
{
    public sealed record TestResult(
        string FilePath,
        string RelativePath,
        bool Passed,
        TimeSpan Duration,
        string? ErrorMessage = null,
        int? ErrorLine = null,
        int? ErrorColumn = null);

    public static async Task<int> RunAsync(CliContext ctx, ILogger logger)
    {
        var testFiles = DiscoverTestFiles(ctx.TestVal);

        if (testFiles.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(ctx.TestVal) && ctx.TestVal != "unit" && ctx.TestVal != "all")
            {
                logger.WriteLine($"No test files matched target '{ctx.TestVal}'.", ConsoleColor.Red);
                return 1;
            }

            logger.WriteLine("No test files found (*.test.etlsql, *.test.sql).", ConsoleColor.Yellow);
            return 0;
        }

        logger.WriteLine($"Discovered {testFiles.Count} test suite(s). Executing tests...", ConsoleColor.Cyan);

        var results = new List<TestResult>();
        var totalStopwatch = Stopwatch.StartNew();

        foreach (var file in testFiles)
        {
            var result = await RunSingleTestAsync(file, ctx);
            results.Add(result);

            if (!ctx.IsJsonMode && !ctx.IsSilentMode)
            {
                if (result.Passed)
                {
                    logger.WriteLine($"  ✓ PASS  {result.RelativePath} ({result.Duration.TotalMilliseconds:F0} ms)", ConsoleColor.Green);
                }
                else
                {
                    logger.WriteLine($"  ✗ FAIL  {result.RelativePath} ({result.Duration.TotalMilliseconds:F0} ms)", ConsoleColor.Red);
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    {
                        var indent = "    ";
                        var lines = result.ErrorMessage.Split('\n');
                        foreach (var l in lines)
                        {
                            logger.WriteLine($"{indent}{l}", ConsoleColor.DarkYellow);
                        }
                    }
                }
            }
        }

        totalStopwatch.Stop();

        int passedCount = results.Count(r => r.Passed);
        int failedCount = results.Count(r => !r.Passed);

        if (ctx.IsJsonMode)
        {
            var jsonPayload = new
            {
                type = "test_run_summary",
                total = results.Count,
                passed = passedCount,
                failed = failedCount,
                durationSeconds = totalStopwatch.Elapsed.TotalSeconds,
                results = results.Select(r => new
                {
                    file = r.RelativePath,
                    passed = r.Passed,
                    durationMs = r.Duration.TotalMilliseconds,
                    error = r.ErrorMessage,
                    line = r.ErrorLine,
                    column = r.ErrorColumn
                })
            };

            Console.WriteLine(JsonSerializer.Serialize(jsonPayload, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (!ctx.IsSilentMode)
        {
            PrintSummaryTable(results, totalStopwatch.Elapsed);
        }

        return failedCount > 0 ? 1 : 0;
    }

    private static async Task<TestResult> RunSingleTestAsync(string filePath, CliContext ctx)
    {
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, filePath);
        var sw = Stopwatch.StartNew();

        try
        {
            string source = await File.ReadAllTextAsync(filePath);

            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();

            var parser = new Parser(tokens, source);
            var script = parser.Parse();

            var parseErrors = script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (parseErrors.Count > 0)
            {
                var first = parseErrors[0];
                return new TestResult(
                    filePath,
                    relativePath,
                    Passed: false,
                    sw.Elapsed,
                    ErrorMessage: $"Parse error: {first.Message}",
                    ErrorLine: first.Line,
                    ErrorColumn: first.Column);
            }

            // Create a fresh scope for test isolation
            await using var scope = Program.ServiceProvider.CreateAsyncScope();
            var evaluator = scope.ServiceProvider.GetRequiredService<Evaluator>();
            evaluator.CurrentScriptPath = filePath;
            evaluator.SessionId = Guid.NewGuid().ToString("N");
            evaluator.IsVerbose = ctx.IsVerbose;

            await evaluator.Evaluate(script);
            sw.Stop();

            return new TestResult(filePath, relativePath, Passed: true, sw.Elapsed);
        }
        catch (ExecutionException ex)
        {
            sw.Stop();
            return new TestResult(
                filePath,
                relativePath,
                Passed: false,
                sw.Elapsed,
                ErrorMessage: ex.Message,
                ErrorLine: ex.Line,
                ErrorColumn: ex.Column);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult(
                filePath,
                relativePath,
                Passed: false,
                sw.Elapsed,
                ErrorMessage: ex.Message);
        }
    }

    private static List<string> DiscoverTestFiles(string? target)
    {
        var files = new List<string>();

        if (!string.IsNullOrWhiteSpace(target) && target != "unit" && target != "all")
        {
            // Specific file
            if (File.Exists(target))
            {
                files.Add(Path.GetFullPath(target));
                return files;
            }

            // Specific directory
            if (Directory.Exists(target))
            {
                files.AddRange(Directory.GetFiles(target, "*.test.etlsql", SearchOption.AllDirectories));
                files.AddRange(Directory.GetFiles(target, "*.test.sql", SearchOption.AllDirectories));
                return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
            }

            // Glob/pattern in current directory
            var dir = Path.GetDirectoryName(target);
            var pattern = Path.GetFileName(target);
            var searchDir = string.IsNullOrWhiteSpace(dir) ? Environment.CurrentDirectory : Path.GetFullPath(dir);

            if (Directory.Exists(searchDir))
            {
                files.AddRange(Directory.GetFiles(searchDir, pattern, SearchOption.AllDirectories));
                return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
            }
        }

        // Default search: current directory & common test folders
        var root = Environment.CurrentDirectory;
        files.AddRange(Directory.GetFiles(root, "*.test.etlsql", SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles(root, "*.test.sql", SearchOption.AllDirectories));

        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
    }

    private static void PrintSummaryTable(List<TestResult> results, TimeSpan totalDuration)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold]Status[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Test Suite[/]"));
        table.AddColumn(new TableColumn("[bold]Duration[/]").RightAligned());

        foreach (var r in results)
        {
            var status = r.Passed ? "[green]PASS[/]" : "[red]FAIL[/]";
            var name = Markup.Escape(r.RelativePath);
            var dur = $"{r.Duration.TotalMilliseconds:F0} ms";
            table.AddRow(status, name, dur);
        }

        AnsiConsole.Write(table);

        int passed = results.Count(r => r.Passed);
        int failed = results.Count(r => !r.Passed);

        if (failed == 0)
        {
            AnsiConsole.MarkupLine($"\n[bold green]✓ All {passed} test suites passed![/] ([dim]{totalDuration.TotalSeconds:F2}s total[/])\n");
        }
        else
        {
            AnsiConsole.MarkupLine($"\n[bold red]✗ {failed} failed[/], [bold green]{passed} passed[/] ([dim]{totalDuration.TotalSeconds:F2}s total[/])\n");
        }
    }
}
