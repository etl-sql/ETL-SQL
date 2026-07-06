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
            // Typed exactly like the shipped template so the STRING/BOOL casts execute here too.
            await TestHelpers.Execute(eval, @"
DECLARE @backup_exit_code INT = 2;
DECLARE @backup_target STRING = 'nightly';

DECLARE @label   STRING = @backup_target;
DECLARE @failed  BOOL   = (@backup_exit_code <> 0);
DECLARE @status  STRING = IIF(@failed, 'FAILURE', 'SUCCESS');
DECLARE @stamp   STRING = FORMAT(GETDATE(), 'yyyy-MM-dd HH:mm:ss');

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
        public async Task Markers_PersistDurably_AndReadBackViaGetJobState()
        {
            // The template's whole point is a durable marker a later monitoring script can read.
            // Prove the persistence round-trip, not just that SET_JOB_STATE executed: run under the
            // orchestrator job-state path (JobName → IJobHistoryStore), then read the markers back
            // from the store directly and via GET_JOB_STATE in a separate execution.
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var eval = provider.GetRequiredService<Evaluator>();
            var store = provider.GetRequiredService<ETL_SQL.Core.Data.IJobHistoryStore>();
            await store.InitializeAsync();

            var jobName = $"backup_marker_{Guid.NewGuid():N}";
            eval.JobName = jobName;

            await TestHelpers.Execute(eval, @"
DECLARE @backup_exit_code = 3;
DECLARE @status = IIF(@backup_exit_code <> 0, 'FAILURE', 'SUCCESS');
SELECT SET_JOB_STATE('last_backup_status', @status);
SELECT SET_JOB_STATE('last_backup_exit_code', CAST(@backup_exit_code AS VARCHAR));");

            // Committed to the orchestrator store on successful completion.
            Assert.Equal("FAILURE", await store.GetJobStateAsync(jobName, "last_backup_status"));
            Assert.Equal("3", await store.GetJobStateAsync(jobName, "last_backup_exit_code"));

            // And readable from a later script (the monitoring-check scenario).
            await TestHelpers.Execute(eval, @"
DECLARE @lastStatus = GET_JOB_STATE('last_backup_status');
DECLARE @lastCode   = GET_JOB_STATE('last_backup_exit_code');");
            Assert.Equal("FAILURE", eval.GetVariable("@lastStatus")?.ToString());
            Assert.Equal("3", eval.GetVariable("@lastCode")?.ToString());
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
