using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// Proves the mechanic behind the daily failure-digest admin template
    /// (samples/admin_operations/daily_failure_digest.etlsql): SHOW JOB HISTORY INTO a queryable
    /// temp table, then filter to recent non-success runs. Runs fully in-process against the local
    /// IJobHistoryStore — no orchestrator HTTP or Docker required.
    /// </summary>
    public class JobFailureDigestTemplateTests
    {
        [Fact]
        public async Task ShowJobHistoryInto_FiltersRecentFailures_ForDigest()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var store = provider.GetRequiredService<IJobHistoryStore>();
            var eval = provider.GetRequiredService<Evaluator>();
            var suffix = Guid.NewGuid().ToString("N")[..8];

            await store.InitializeAsync();
            // A failed run and a successful run, both recent.
            var failed = await store.LogJobStartAsync($"digest_{suffix}_fail");
            await store.LogJobEndAsync(failed, "FAILURE", "boom");
            var ok = await store.LogJobStartAsync($"digest_{suffix}_ok");
            await store.LogJobEndAsync(ok, "SUCCESS");

            // The template's core: capture history into a temp table, filter to recent non-success runs.
            await TestHelpers.Execute(eval, $@"
SELECT * INTO #jobhist FROM eng.job_history;
SELECT job_name, status, error_message
INTO #failures
FROM #jobhist
WHERE status NOT IN ('SUCCESS', 'RUNNING')
  AND start_time >= DATEADD(DAY, -1, GETDATE())
  AND job_name LIKE 'digest_{suffix}_%';
DECLARE @failCount = (SELECT COUNT(*) FROM #failures);
DECLARE @detail = (
    SELECT STRING_AGG(CONCAT(job_name, ' [', status, ']: ', ISNULL(error_message, '(no message)')), CHAR(10))
    FROM #failures);");

            Assert.Equal(1, Convert.ToInt32(eval.GetVariable("@failCount")));
            // Matches the template's exact email-body expression (STRING_AGG + CONCAT + ISNULL + CHAR).
            Assert.Contains($"digest_{suffix}_fail [FAILURE]: boom", eval.GetVariable("@detail")?.ToString() ?? "");
        }

        [Fact]
        public void ShippedTemplate_ParsesWithoutErrors()
        {
            // Parses the whole shipped template, covering the parts not executed above
            // (CREATE CONNECTION SMTP, IF/BEGIN/END, SEND EMAIL with expression arguments).
            var path = FindRepoFile("samples/admin_operations/daily_failure_digest.etlsql");
            var script = new Parser(new Lexer(File.ReadAllText(path)).Tokenize(), File.ReadAllText(path)).Parse();

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
