using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class ResumeEdgeCaseTests : IDisposable
    {
        private readonly string _sessionDir;
        private readonly string _sessionId;

        public ResumeEdgeCaseTests()
        {
            _sessionDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-ResumeEdge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sessionDir);
            _sessionId = "resume-edge-" + Guid.NewGuid().ToString("N");
        }

        public void Dispose()
        {
            if (Directory.Exists(_sessionDir))
                try { Directory.Delete(_sessionDir, true); } catch { }
        }

        private static (IServiceProvider Provider, Evaluator Eval, SessionStateManager Manager)
            MakeEvalWithSession(string sessionId, string sessionDir)
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider(
                new Dictionary<string, string?> { ["Session:Root"] = sessionDir });
            var ev = sp.GetRequiredService<Evaluator>();
            var manager = sp.GetRequiredService<SessionStateManager>();
            sp.GetRequiredService<SecurityService>().IsTestMode = true;
            ev.IsPersistentSession = true;
            ev.SessionId = sessionId;
            ev.SessionRoot = sessionDir;
            return (sp, ev, manager);
        }

        // Scenario 1: IsResuming=true with no checkpoint loaded → clear error, not silent fresh run.
        // Simulates --resume on a session that has no saved state (e.g. first ever run, cleared session).
        // The evaluator should fail fast so the user knows their resume request had no effect,
        // rather than silently running the whole job again from the beginning.
        [Fact]
        public async Task IsResuming_WithNoCheckpointLoaded_ThrowsDescriptiveError()
        {
            var (_, ev, _) = MakeEvalWithSession(_sessionId, _sessionDir);
            ev.IsResuming = true;
            // LoadSessionState intentionally not called — no saved checkpoint exists.

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                ev.Evaluate(TestHelpers.Parse(@"
                    DECLARE @x INT = 0;
                    step1:
                    SET @x = @x + 1;
                ")));

            Assert.Contains("checkpoint", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Scenario 2: Re-running with the same --session but no --resume must start with fresh variable state.
        // Regression guard for the bug where LoadSessionState fired on any --session run,
        // causing stale values from a prior run to bleed into subsequent runs.
        [Fact]
        public async Task SameSession_WithoutResume_VariablesNotRestoredFromPriorRun()
        {
            // Run 1: saves @x=999 at the checkpoint.
            var (_, ev1, _) = MakeEvalWithSession(_sessionId, _sessionDir);
            await ev1.Evaluate(TestHelpers.Parse(@"
                DECLARE @x INT = 999;
                step1:
                PRINT @x;
            "));
            Assert.Equal(999m, ev1.Variables["@x"]);

            // Run 2: same session ID, IsResuming=false, LoadSessionState NOT called.
            var (_, ev2, _) = MakeEvalWithSession(_sessionId, _sessionDir);
            await ev2.Evaluate(TestHelpers.Parse(@"
                DECLARE @x INT = 1;
                step1:
                PRINT @x;
            "));

            // Must use the declared value; prior run's 999 must not be visible.
            Assert.Equal(1m, ev2.Variables["@x"]);
        }

        // Scenario 3: GOTO targeting a reserved keyword must be rejected at parse time.
        // Regression guard for the &&-logic bug in ParseGoto where keyword tokens
        // bypassed the identifier-only guard and produced a GotoStatement("SELECT").
        [Fact]
        public void Goto_TargetingReservedKeyword_IsRejectedAtParseTime()
        {
            var parsed = TestHelpers.Parse("GOTO SELECT;");

            Assert.NotEmpty(parsed.Diagnostics);
            Assert.Contains(parsed.Diagnostics, d =>
                d.Message.Contains("identifier", StringComparison.OrdinalIgnoreCase));
        }

        // Scenario 4: SaveSession called with a non-Evaluator object must fail explicitly.
        // A successful-looking no-op loses the checkpoint and moves the failure to resume time.
        [Fact]
        public async Task SaveSession_WithNonEvaluatorObject_RejectsCaller()
        {
            var (_, _, manager) = MakeEvalWithSession(_sessionId, _sessionDir);

            var ex = await Assert.ThrowsAsync<ArgumentException>(
                () => manager.SaveSession(_sessionId, new object()));

            Assert.Equal("evaluatorObj", ex.ParamName);
            Assert.Contains("evaluator", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Scenario 5: Resume from a mid-script checkpoint must use the loaded variable state,
        // not the value from the DECLARE at the top of the script.
        // The script sets @x=100 between checkpoint_a and checkpoint_b. The checkpoint at
        // checkpoint_b captures @x=100. On resume, DECLARE and the SET @x=100 are both
        // skipped; only the post-checkpoint_b code runs. If DECLARE were incorrectly
        // re-executed it would reset @x to 7, making the final value 8 instead of 101.
        [Fact]
        public async Task Resume_MidScript_SkipsToLastCheckpoint_UsesLoadedState()
        {
            const string script = @"
                DECLARE @x INT = 7;
                checkpoint_a:
                SET @x = 100;
                checkpoint_b:
                SET @x = @x + 1;
            ";

            // Run 1: checkpoint_a saves @x=7; SET @x=100 runs; checkpoint_b saves @x=100; +1 → 101.
            var (_, ev1, _) = MakeEvalWithSession(_sessionId, _sessionDir);
            await ev1.Evaluate(TestHelpers.Parse(script));
            Assert.Equal(101m, ev1.Variables["@x"]);

            // Resume from last checkpoint (checkpoint_b, @x=100).
            var (_, ev2, manager2) = MakeEvalWithSession(_sessionId, _sessionDir);
            ev2.IsResuming = true;
            var state = await manager2.LoadSession(_sessionId);
            Assert.NotNull(state);
            await ev2.LoadSessionState(state);
            await ev2.Evaluate(TestHelpers.Parse(script));

            // Correct:   @x=100 (loaded), DECLARE skipped, SET @x=100 skipped, +1 → 101.
            // Regression: DECLARE overrides loaded state → @x=7, SET skipped, +1 → 8.
            Assert.Equal(101m, ev2.Variables["@x"]);
        }
    }
}
