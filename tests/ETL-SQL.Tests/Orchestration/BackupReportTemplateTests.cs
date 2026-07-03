using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Proves the mechanic behind samples/admin_operations/backup_and_report.etlsql: the injected
    /// @backup_exit_code drives the SUCCESS/FAILURE branch, the SET_JOB_STATE markers are written, and
    /// only a failure produces an alert body. Runs fully in-process — no backup is taken (the script
    /// deliberately never runs one; it only records the outcome of the external `admin backup` CLI).
    /// </summary>
    public class BackupReportTemplateTests
    {
        [Fact]
        public async Task NonZeroExitCode_MarksFailure_AndBuildsAlertBody()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var eval = provider.GetRequiredService<Evaluator>();

            // DECLARE simulates the CLI's `--var backup_exit_code=2 --var backup_target=nightly`.
            await TestHelpers.Execute(eval, @"
DECLARE @backup_exit_code = 2;
DECLARE @backup_target = 'nightly';

DECLARE @label   = @backup_target;
DECLARE @failed  = (@backup_exit_code <> 0);
DECLARE @status  = IIF(@failed, 'FAILURE', 'SUCCESS');
DECLARE @stamp   = FORMAT(GETDATE(), 'yyyy-MM-dd HH:mm:ss');

SELECT SET_JOB_STATE('last_backup_status', @status);
SELECT SET_JOB_STATE('last_backup_exit_code', CAST(@backup_exit_code AS VARCHAR));

DECLARE @body = CONCAT('The backup ', @label, ' did not complete successfully.',
                       ' Exit code: ', @backup_exit_code);");

            Assert.Equal("FAILURE", eval.GetVariable("@status")?.ToString());
            Assert.True(Convert.ToBoolean(eval.GetVariable("@failed")));
            Assert.Equal("nightly", eval.GetVariable("@label")?.ToString());
            Assert.Contains("did not complete successfully", eval.GetVariable("@body")?.ToString() ?? "");
            Assert.Contains("Exit code: 2", eval.GetVariable("@body")?.ToString() ?? "");
        }

        [Fact]
        public async Task ZeroExitCode_MarksSuccess_NoAlert()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var eval = provider.GetRequiredService<Evaluator>();

            await TestHelpers.Execute(eval, @"
DECLARE @backup_exit_code = 0;
DECLARE @backup_target = 'weekly';
DECLARE @label   = @backup_target;
DECLARE @failed  = (@backup_exit_code <> 0);
DECLARE @status  = IIF(@failed, 'FAILURE', 'SUCCESS');
SELECT SET_JOB_STATE('last_backup_status', @status);");

            Assert.Equal("SUCCESS", eval.GetVariable("@status")?.ToString());
            Assert.False(Convert.ToBoolean(eval.GetVariable("@failed")));
            Assert.Equal("weekly", eval.GetVariable("@label")?.ToString());
        }

        [Fact]
        public void ShippedTemplate_ParsesWithoutErrors()
        {
            // Covers the parts not executed above (CREATE CONNECTION SMTP, IF/BEGIN/END,
            // SEND EMAIL with expression arguments, the bare SET_JOB_STATE statements).
            var path = FindRepoFile("samples/admin_operations/backup_and_report.etlsql");
            var text = File.ReadAllText(path);
            var script = new Parser(new Lexer(text).Tokenize(), text).Parse();
            var errors = script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                "Template has parse errors: " + string.Join("; ", errors.Select(e => e.Message)));
        }

        private static string FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException($"Could not locate {relative} above {AppContext.BaseDirectory}");
        }
    }
}
