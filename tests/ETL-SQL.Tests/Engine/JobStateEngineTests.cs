using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class JobStateEngineTests : IDisposable
    {
        private readonly string _tempFile;

        public JobStateEngineTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"test_job_state_{Guid.NewGuid():N}.etlsql");
            File.WriteAllText(_tempFile, "SELECT 1;");
        }

        public void Dispose()
        {
            try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
            var stateFile = Path.ChangeExtension(_tempFile, ".etlstate");
            try { if (File.Exists(stateFile)) File.Delete(stateFile); } catch { }
        }

        [Fact]
        public async Task TestLocalStateFallback_GetAndSet()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.CurrentScriptPath = _tempFile;

            // Set state in script
            await eval.Evaluate(new Lexer(@"
                SELECT SET_JOB_STATE('Watermark', '2026-06-19');
            ").TokenizeToScript());

            // State file should be generated and hold value after successful completion
            var stateFile = Path.ChangeExtension(_tempFile, ".etlstate");
            Assert.True(File.Exists(stateFile));
            var content = File.ReadAllText(stateFile);
            Assert.Contains("Watermark", content);
            Assert.Contains("2026-06-19", content);

            // Read state back in script
            await eval.Evaluate(new Lexer(@"
                DECLARE @Val STRING = GET_JOB_STATE('Watermark');
                SELECT @Val AS Result;
            ").TokenizeToScript());

            var row = eval.LastResult?.Rows.FirstOrDefault();
            Assert.NotNull(row);
            Assert.Equal("2026-06-19", row["Result"]);
        }

        [Fact]
        public async Task TestAtomicCommit_StateNotCommittedOnFailure()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.CurrentScriptPath = _tempFile;

            // Set state, but crash the script (div by zero)
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await eval.Evaluate(new Lexer(@"
                    SELECT SET_JOB_STATE('Watermark', '2026-06-19');
                    SELECT 1/0;
                ").TokenizeToScript());
            });

            // State file should not be updated or exist since it failed
            var stateFile = Path.ChangeExtension(_tempFile, ".etlstate");
            Assert.False(File.Exists(stateFile));
        }

        [Fact]
        public async Task TestJobStateWithJobName_GetAndSet()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var eval = provider.GetRequiredService<Evaluator>();
            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();

            eval.JobName = "BackupJobTest";

            // Set state
            await eval.Evaluate(new Lexer(@"
                SELECT SET_JOB_STATE('LastRunTimestamp', '123456');
            ").TokenizeToScript());

            // Retrieve directly from store to confirm it committed
            var storeValue = await store.GetJobStateAsync("BackupJobTest", "LastRunTimestamp");
            Assert.Equal("123456", storeValue);

            // Retrieve inside script
            await eval.Evaluate(new Lexer(@"
                DECLARE @ts STRING = GET_JOB_STATE('LastRunTimestamp');
                SELECT @ts AS Value;
            ").TokenizeToScript());

            var row = eval.LastResult?.Rows.FirstOrDefault();
            Assert.NotNull(row);
            Assert.Equal("123456", row["Value"]);
        }
    }
}
