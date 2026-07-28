using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    /// <summary>
    /// The catalog statements end to end: parsed, executed against a real store, and observed through
    /// the store rather than through the handler's own return value.
    /// </summary>
    public class CatalogStatementExecutionTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteJobHistoryStore _store;

        public CatalogStatementExecutionTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"etlsql-catexec-{Guid.NewGuid():N}.db");
            _store = new SQLiteJobHistoryStore(_dbPath);
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch (IOException) { }
        }

        /// <summary>
        /// Parses, then runs each statement through the handler DI would select — but against this
        /// test's temp database. The registered singleton points at the machine-wide job store, which
        /// a test must never write to.
        /// </summary>
        private async Task RunAsync(string sql)
        {
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
            Assert.Empty(script.Diagnostics);

            var evaluator = ETL_SQL.App.DependencyInjectionSetup.BuildServiceProvider()
                .GetRequiredService<Evaluator>();

            foreach (var statement in script.Statements)
                await HandlerFor(statement).Execute(statement, evaluator);
        }

        private IStatementHandler HandlerFor(Statement statement) => statement switch
        {
            CreateScheduleStatement => new CreateScheduleStatementHandler(_store),
            CreateNotificationStatement => new CreateNotificationStatementHandler(_store),
            AlterCatalogObjectStatement => new AlterCatalogObjectStatementHandler(_store),
            DropCatalogObjectStatement => new DropCatalogObjectStatementHandler(_store),
            SetCatalogObjectEnabledStatement => new SetCatalogObjectEnabledStatementHandler(_store),
            AlterJobAttachmentStatement => new AlterJobAttachmentStatementHandler(_store),
            CreateJobStatement => new CreateJobStatementHandler(_store, _store),
            DropJobStatement => new DropJobStatementHandler(_store),
            SetWhatIfStatement => new SetWhatIfStatementHandler(new EngineLogger()),
            _ => throw new InvalidOperationException($"No catalog handler for {statement.GetType().Name}.")
        };

        private async Task SaveJobAsync(string name) =>
            await _store.SaveJobAsync(new JobDefinition(name, "reports/x.rptsql", 1, "HOUR", null, null, null));

        // ── CREATE ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateJob_PersistsANormalizedScriptTarget()
        {
            await RunAsync(
                "CREATE JOB Nightly FOR SCRIPT 'jobs/nightly.etlsql' " +
                "WITH (MAX_RETRIES = 3, RETRY_DELAY = 60, DISPLAY_NAME = 'Nightly load');");

            var job = await _store.GetJobAsync("Nightly");
            Assert.NotNull(job);
            Assert.Equal(JobTargetKind.Script, job.JobType);
            Assert.Equal("jobs/nightly.etlsql", job.TargetPath);
            Assert.Contains("RUN SCRIPT", job.Script, StringComparison.Ordinal);
            Assert.Equal(3, job.MaxRetries);
            Assert.Equal("Nightly load", job.DisplayName);
            Assert.NotNull(job.ScriptHash);
            Assert.Null(job.NextRun);
        }

        [Fact]
        public async Task CreateOrAlterJob_PatchesDefinitionAndKeepsLinks()
        {
            await RunAsync("CREATE SCHEDULE T ON '0 2 * * *' AT TIME ZONE 'UTC';");
            await RunAsync("CREATE JOB Nightly FOR SCRIPT 'jobs/v1.etlsql' WITH (MAX_RETRIES = 4);");
            await RunAsync("ALTER JOB Nightly ADD SCHEDULE T;");

            await RunAsync("CREATE OR ALTER JOB Nightly FOR SCRIPT 'jobs/v2.etlsql';");

            var job = await _store.GetJobAsync("Nightly");
            Assert.Equal("jobs/v2.etlsql", job!.TargetPath);
            Assert.Equal(4, job.MaxRetries);
            Assert.Single(await _store.GetJobSchedulesAsync("Nightly"));
        }

        [Fact]
        public async Task CreateOrReplaceJob_FullyRedefinesAndDropsLinks()
        {
            await RunAsync("CREATE SCHEDULE T ON '0 2 * * *' AT TIME ZONE 'UTC';");
            await RunAsync("CREATE NOTIFICATION N USING mail;");
            await RunAsync("CREATE JOB Nightly FOR SCRIPT 'jobs/v1.etlsql';");
            await RunAsync("ALTER JOB Nightly ADD SCHEDULE T;");
            await RunAsync("ALTER JOB Nightly ADD NOTIFICATION N ON FAILURE;");

            await RunAsync("CREATE OR REPLACE JOB Nightly FOR REPORT 'reports/Finance';");

            var job = await _store.GetJobAsync("Nightly");
            Assert.Equal(JobTargetKind.Report, job!.JobType);
            Assert.Equal(string.Empty, job.Script);
            Assert.Null(job.ScriptHash);
            Assert.Empty(await _store.GetJobSchedulesAsync("Nightly"));
            Assert.Empty(await _store.GetJobNotificationsAsync("Nightly"));
        }

        [Fact]
        public async Task CreateSchedule_PersistsAndDefaultsTheTimeZone()
        {
            await RunAsync("CREATE SCHEDULE Nightly ON '0 2 * * *';");

            var schedule = await _store.GetScheduleAsync("Nightly");
            Assert.Equal("0 2 * * *", schedule!.Cron);
            // Resolved and stored at creation, so editing configuration later cannot move it.
            Assert.Equal("UTC", schedule.TimeZone);
        }

        [Fact]
        public async Task CreateSchedule_Duplicate_IsRefusedAndNamesTheAlternatives()
        {
            await RunAsync("CREATE SCHEDULE Nightly ON '0 2 * * *';");

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => RunAsync("CREATE SCHEDULE Nightly ON '0 3 * * *';"));

            Assert.Contains("CREATE OR ALTER", ex.Message, StringComparison.Ordinal);
            Assert.Contains("CREATE OR REPLACE", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>Replay of an exported script must converge rather than fail on its second run.</summary>
        [Fact]
        public async Task CreateOrAlterSchedule_IsIdempotent()
        {
            await RunAsync("CREATE OR ALTER SCHEDULE Nightly ON '0 2 * * *';");
            await RunAsync("CREATE OR ALTER SCHEDULE Nightly ON '0 3 * * *';");

            Assert.Equal("0 3 * * *", (await _store.GetScheduleAsync("Nightly"))!.Cron);
        }

        /// <summary>
        /// A bad expression is caught when the schedule is written. Catching it at first fire instead
        /// would be indistinguishable from a job that simply has not come due yet.
        /// </summary>
        [Theory]
        [InlineData("CREATE SCHEDULE Bad ON 'not a cron';")]
        [InlineData("CREATE SCHEDULE Bad ON '*/5 * * * * *';")]
        [InlineData("CREATE SCHEDULE Bad ON '0 2 * * *' AT TIME ZONE 'Mars/Olympus_Mons';")]
        public async Task CreateSchedule_ValidatesAtWriteTime(string sql)
        {
            await Assert.ThrowsAsync<ExecutionException>(() => RunAsync(sql));
            Assert.Empty(await _store.GetSchedulesAsync());
        }

        [Fact]
        public async Task CreateNotification_PersistsTheAliasNotACredential()
        {
            await RunAsync("CREATE NOTIFICATION OpsAlert USING local_mail TO 'ops@example.com';");

            var notification = await _store.GetNotificationAsync("OpsAlert");
            Assert.Equal("local_mail", notification!.ConnectionName);
            Assert.Equal("ops@example.com", notification.Recipient);
        }

        [Fact]
        public async Task WhatIf_CreateScheduleAndNotification_DoNotPersist()
        {
            await RunAsync(
                "SET WHAT_IF ON;" +
                "CREATE SCHEDULE T ON '0 2 * * *';" +
                "CREATE NOTIFICATION N USING local_mail;");

            Assert.Empty(await _store.GetSchedulesAsync());
            Assert.Empty(await _store.GetNotificationsAsync());
        }

        // ── ALTER ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task AlterSchedule_PatchesOnlyTheNamedClause()
        {
            await RunAsync("CREATE SCHEDULE Nightly ON '0 2 * * *' AT TIME ZONE 'America/New_York' WITH (DISPLAY_NAME = 'Overnight');");
            await RunAsync("ALTER SCHEDULE Nightly SET CRON = '0 4 * * *';");

            var schedule = await _store.GetScheduleAsync("Nightly");
            Assert.Equal("0 4 * * *", schedule!.Cron);
            Assert.Equal("America/New_York", schedule.TimeZone);
            Assert.Equal("Overnight", schedule.DisplayName);
        }

        [Fact]
        public async Task AlterSchedule_ValidatesTheResultingCombination()
        {
            await RunAsync("CREATE SCHEDULE Nightly ON '0 2 * * *';");

            await Assert.ThrowsAsync<ExecutionException>(
                () => RunAsync("ALTER SCHEDULE Nightly SET TIME ZONE 'Mars/Olympus_Mons';"));

            Assert.Equal("UTC", (await _store.GetScheduleAsync("Nightly"))!.TimeZone);
        }

        [Fact]
        public async Task AlterSchedule_UnknownName_ExplainsThatNamesAreNotRenamed()
        {
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => RunAsync("ALTER SCHEDULE NoSuchThing SET CRON = '0 2 * * *';"));

            Assert.Contains("never renamed", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task WhatIf_AlterScheduleAndNotification_DoNotPersist()
        {
            await RunAsync("CREATE SCHEDULE T ON '0 2 * * *';");
            await RunAsync("CREATE NOTIFICATION N USING local_mail TO 'ops@example.com';");

            await RunAsync(
                "SET WHAT_IF ON;" +
                "ALTER SCHEDULE T SET CRON = '0 3 * * *';" +
                "ALTER NOTIFICATION N SET TO 'audit@example.com';");

            Assert.Equal("0 2 * * *", (await _store.GetScheduleAsync("T"))!.Cron);
            Assert.Equal("ops@example.com", (await _store.GetNotificationAsync("N"))!.Recipient);
        }

        // ── Attachments ───────────────────────────────────────────────────────────

        [Fact]
        public async Task AddSchedule_ArmsTheLink()
        {
            await SaveJobAsync("Nightly");
            await RunAsync("CREATE SCHEDULE T ON '0 2 * * *';");
            await RunAsync("ALTER JOB Nightly ADD SCHEDULE T;");

            var link = Assert.Single(await _store.GetJobSchedulesAsync("Nightly"));
            Assert.NotNull(link.NextRun);
            Assert.Null(link.LastRun);
        }

        /// <summary>Re-running an export re-issues every attachment; that must be a no-op.</summary>
        [Fact]
        public async Task Attachments_AreIdempotentOnReplay()
        {
            await SaveJobAsync("Nightly");
            await RunAsync(
                "CREATE OR REPLACE SCHEDULE T ON '0 2 * * *';" +
                "CREATE OR REPLACE NOTIFICATION N USING local_mail;" +
                "ALTER JOB Nightly ADD SCHEDULE T;" +
                "ALTER JOB Nightly ADD NOTIFICATION N ON FAILURE;");

            // Replay the identical script.
            await RunAsync(
                "CREATE OR REPLACE SCHEDULE T ON '0 2 * * *';" +
                "CREATE OR REPLACE NOTIFICATION N USING local_mail;" +
                "ALTER JOB Nightly ADD SCHEDULE T;" +
                "ALTER JOB Nightly ADD NOTIFICATION N ON FAILURE;");

            Assert.Single(await _store.GetJobSchedulesAsync("Nightly"));
            Assert.Single(await _store.GetJobNotificationsAsync("Nightly"));
        }

        [Fact]
        public async Task RemoveAttachment_ThatIsNotThere_IsANoOp()
        {
            await SaveJobAsync("Nightly");
            await RunAsync("CREATE SCHEDULE T ON '0 2 * * *';");

            // No exception: removing what is absent is how a replayed script converges.
            await RunAsync("ALTER JOB Nightly REMOVE SCHEDULE T;");
        }

        [Fact]
        public async Task WhatIf_Attachments_DoNotPersist()
        {
            await SaveJobAsync("Nightly");
            await RunAsync("CREATE SCHEDULE T ON '0 2 * * *';");
            await RunAsync("CREATE NOTIFICATION N USING local_mail;");

            await RunAsync(
                "SET WHAT_IF ON;" +
                "ALTER JOB Nightly ADD SCHEDULE T;" +
                "ALTER JOB Nightly ADD NOTIFICATION N ON FAILURE;");

            Assert.Empty(await _store.GetJobSchedulesAsync("Nightly"));
            Assert.Empty(await _store.GetJobNotificationsAsync("Nightly"));
        }

        [Fact]
        public async Task CatalogMutations_EmitSecurityAuditEvents()
        {
            var sink = new RecordingSecurityEventSink();
            using var eventScope = SecurityEventRuntime.UseSinkForScope(sink);

            await RunAsync(
                "CREATE SCHEDULE T ON '0 2 * * *';" +
                "CREATE NOTIFICATION N USING local_mail;" +
                "CREATE JOB Nightly FOR SCRIPT 'jobs/nightly.etlsql';" +
                "ALTER JOB Nightly ADD SCHEDULE T;" +
                "ALTER JOB Nightly ADD NOTIFICATION N ON FAILURE;" +
                "DROP JOB Nightly;");

            Assert.Contains(sink.Events, e =>
                e.Type == SecurityEventType.CatalogMutation &&
                e.Decision == SecurityEventDecision.Allowed &&
                e.SanitizedTarget == "JOB:Nightly" &&
                e.Reason.Contains("CREATE_JOB", StringComparison.Ordinal));
            Assert.Contains(sink.Events, e =>
                e.Type == SecurityEventType.CatalogMutation &&
                e.SanitizedTarget == "JOB:Nightly/SCHEDULE:T" &&
                e.Reason.Contains("ATTACH_SCHEDULE", StringComparison.Ordinal));
            Assert.Contains(sink.Events, e =>
                e.Type == SecurityEventType.CatalogMutation &&
                e.SanitizedTarget == "JOB:Nightly/NOTIFICATION:N/ON:FAILURE" &&
                e.Reason.Contains("ATTACH_NOTIFICATION", StringComparison.Ordinal));
            Assert.Contains(sink.Events, e =>
                e.Type == SecurityEventType.CatalogMutation &&
                e.SanitizedTarget == "JOB:Nightly" &&
                e.Reason.Contains("DROP_JOB", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AddSchedule_UnknownSchedule_IsRefused()
        {
            await SaveJobAsync("Nightly");

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => RunAsync("ALTER JOB Nightly ADD SCHEDULE NoSuchThing;"));

            Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>COMPLETION covers SUCCESS and FAILURE, so the pair would deliver twice per run.</summary>
        [Fact]
        public async Task OverlappingNotificationTriggers_AreRefusedAtTheStatement()
        {
            await SaveJobAsync("Nightly");
            await RunAsync("CREATE NOTIFICATION N USING local_mail;");
            await RunAsync("ALTER JOB Nightly ADD NOTIFICATION N ON COMPLETION;");

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => RunAsync("ALTER JOB Nightly ADD NOTIFICATION N ON SUCCESS;"));

            Assert.Contains("COMPLETION covers both", ex.Message, StringComparison.Ordinal);
        }

        // ── DROP / ENABLE / DISABLE ───────────────────────────────────────────────

        /// <summary>
        /// Restrict, not cascade — and the error names the jobs, because "it is in use" without
        /// saying by what leaves the operator to go looking.
        /// </summary>
        [Fact]
        public async Task DropSchedule_WhileAttached_NamesTheBlockingJobs()
        {
            await SaveJobAsync("Nightly");
            await RunAsync("CREATE SCHEDULE T ON '0 2 * * *';");
            await RunAsync("ALTER JOB Nightly ADD SCHEDULE T;");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("DROP SCHEDULE T;"));

            Assert.Contains("Nightly", ex.Message, StringComparison.Ordinal);
            Assert.Contains("REMOVE SCHEDULE", ex.Message, StringComparison.Ordinal);
            Assert.NotNull(await _store.GetScheduleAsync("T"));
        }

        [Fact]
        public async Task DropSchedule_Missing_ThrowsUnlessIfExists()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("DROP SCHEDULE NoSuchThing;"));
            await RunAsync("DROP SCHEDULE IF EXISTS NoSuchThing;");
        }

        [Fact]
        public async Task DisableSchedule_PersistsAndIsCaseInsensitive()
        {
            await RunAsync("CREATE SCHEDULE Nightly ON '0 2 * * *';");
            await RunAsync("DISABLE SCHEDULE nightly;");

            Assert.False((await _store.GetScheduleAsync("Nightly"))!.IsEnabled);

            await RunAsync("ENABLE SCHEDULE NIGHTLY;");
            Assert.True((await _store.GetScheduleAsync("Nightly"))!.IsEnabled);
        }

        [Fact]
        public async Task WhatIf_DropAndDisable_DoNotPersist()
        {
            await RunAsync("CREATE SCHEDULE T ON '0 2 * * *';");
            await RunAsync("CREATE NOTIFICATION N USING local_mail;");

            await RunAsync(
                "SET WHAT_IF ON;" +
                "DISABLE SCHEDULE T;" +
                "DROP NOTIFICATION N;");

            Assert.True((await _store.GetScheduleAsync("T"))!.IsEnabled);
            Assert.NotNull(await _store.GetNotificationAsync("N"));
        }

        /// <summary>
        /// Disabling is not deleting, and re-creating must not silently re-enable something an
        /// operator deliberately paused.
        /// </summary>
        [Fact]
        public async Task CreateOrAlter_DoesNotResurrectADisabledSchedule()
        {
            await RunAsync("CREATE SCHEDULE Nightly ON '0 2 * * *';");
            await RunAsync("DISABLE SCHEDULE Nightly;");
            await RunAsync("CREATE OR ALTER SCHEDULE Nightly ON '0 5 * * *';");

            var schedule = await _store.GetScheduleAsync("Nightly");
            Assert.Equal("0 5 * * *", schedule!.Cron);
            Assert.False(schedule.IsEnabled);
        }

        private sealed class RecordingSecurityEventSink : ISecurityEventSink
        {
            public List<SecurityEvent> Events { get; } = [];
            public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
        }
    }
}
