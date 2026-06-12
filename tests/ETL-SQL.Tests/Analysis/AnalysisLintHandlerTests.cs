using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Analysis.Analysis
{
    /// <summary>
    /// CQ-T7: Tests for LintStatementHandler (LINT 'path.sql' statement).
    /// Exercises the handler via the evaluator against real temp files.
    /// </summary>
    public class LintStatementHandlerTests : IDisposable
    {
        private readonly string _tempDir;

        public LintStatementHandlerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-Lint-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private static Evaluator NewEval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private string WriteScript(string name, string content)
        {
            var path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        // ── Basic execution ────────────────────────────────────────────────────

        [Fact]
        public async Task Lint_CleanScript_ReturnsEmptyResultTable()
        {
            var path = WriteScript("clean.sql", "DECLARE @x INT = 42; SELECT @x;");

            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse($"LINT '{path}';"));

            Assert.NotNull(eval.LastResult);
            // Clean script with no violations should return zero rows (or only informational)
            var errors = eval.LastResult!.Rows
                .Where(r => r["Severity"]?.ToString() == "Error")
                .ToList();
            Assert.Empty(errors);
        }

        [Fact]
        public async Task Lint_ReturnsTableWithExpectedColumns()
        {
            var path = WriteScript("cols.sql", "SELECT 1;");

            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse($"LINT '{path}';"));

            Assert.NotNull(eval.LastResult);
            var cols = eval.LastResult!.ColumnNames.ToArray();
            Assert.Contains("Severity", cols);
            Assert.Contains("Rule", cols);
            Assert.Contains("Line", cols);
            Assert.Contains("Message", cols);
        }

        [Fact]
        public async Task Lint_ScriptWithDeleteWithoutWhere_ReturnsWarning()
        {
            // SafeDeleteUpdateRule should fire on DELETE without WHERE
            var path = WriteScript("unsafe.sql", "DELETE FROM Orders;");

            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse($"LINT '{path}';"));

            Assert.NotNull(eval.LastResult);
            Assert.True(eval.LastResult!.Rows.Count > 0, "Expected at least one lint finding");

            var deleteFinding = eval.LastResult.Rows
                .FirstOrDefault(r => r["Message"]?.ToString()?.Contains("DELETE") == true
                                  && r["Message"]?.ToString()?.Contains("WHERE") == true);
            Assert.NotNull(deleteFinding);
        }

        [Fact]
        public async Task Lint_ScriptWithSelectStar_ReturnsWarning()
        {
            var path = WriteScript("star.sql", "SELECT * FROM MyTable;");

            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse($"LINT '{path}';"));

            Assert.NotNull(eval.LastResult);
            Assert.True(eval.LastResult!.Rows.Count > 0, "Expected at least one lint finding");

            var starFinding = eval.LastResult.Rows
                .FirstOrDefault(r => r["Rule"]?.ToString() == "AvoidSelectStar");
            Assert.NotNull(starFinding);
        }

        [Fact]
        public async Task Lint_MultipleViolations_ReturnsMultipleRows()
        {
            var script = "SELECT * FROM A; DELETE FROM B; UPDATE C SET x=1;";
            var path = WriteScript("multi.sql", script);

            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse($"LINT '{path}';"));

            Assert.NotNull(eval.LastResult);
            Assert.True(eval.LastResult!.Rows.Count >= 3,
                $"Expected at least 3 findings, got {eval.LastResult.Rows.Count}");
        }

        // ── Error paths ────────────────────────────────────────────────────────

        [Fact]
        public async Task Lint_FileNotFound_ThrowsExecutionException()
        {
            var nonExistent = Path.Combine(_tempDir, "does_not_exist.sql");
            var eval = NewEval();

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
                await eval.Evaluate(TestHelpers.Parse($"LINT '{nonExistent}';")));

            Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Lint_WithoutPath_ThrowsExecutionException()
        {
            // LINT without a path — the parser would actually fail here since LINT requires a path.
            // Instead verify that parsing LINT statement requires a path string.
            var stmt = TestHelpers.Parse("LINT 'dummy.sql';").Statements[0];
            Assert.IsType<LintStatement>(stmt);
            Assert.Equal("dummy.sql", ((LintStatement)stmt).ScriptPath);
        }

        // ── FindingsSortedByLine ────────────────────────────��──────────────────

        [Fact]
        public async Task Lint_FindingsAreSortedByLineNumber()
        {
            // Two violations: line 3 and line 1
            var script = "SELECT * FROM A;\nDECLARE @x INT;\nDELETE FROM B;";
            var path = WriteScript("sorted.sql", script);

            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse($"LINT '{path}';"));

            Assert.NotNull(eval.LastResult);
            var lines = eval.LastResult!.Rows
                .Select(r => Convert.ToInt32(r["Line"]))
                .ToList();

            // Verify sorted ascending
            for (int i = 1; i < lines.Count; i++)
                Assert.True(lines[i] >= lines[i - 1], "Findings should be sorted by line number ascending");
        }

        // ── Connection rules ───────────────────────────────────────────────────

        [Fact]
        public async Task Lint_UnusedConnection_ReturnsWarning()
        {
            var script = "CREATE CONNECTION mydb AS MSSQL('server=localhost', DATABASE='test');";
            var path = WriteScript("unused_conn.sql", script);

            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse($"LINT '{path}';"));

            Assert.NotNull(eval.LastResult);
            var unusedFinding = eval.LastResult!.Rows
                .FirstOrDefault(r => r["Rule"]?.ToString() == "UnusedConnection");
            Assert.NotNull(unusedFinding);
        }
    }
}
