using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class GotoAndCheckpointTests : IDisposable
    {
        private readonly string _sessionDir;
        private readonly string _sessionId;

        public GotoAndCheckpointTests()
        {
            _sessionDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-GotoTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sessionDir);
            _sessionId = "goto-session-" + Guid.NewGuid().ToString("N");
        }

        public void Dispose()
        {
            if (Directory.Exists(_sessionDir))
            {
                try { Directory.Delete(_sessionDir, true); } catch { }
            }
        }

        private static Evaluator MakeEval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static (IServiceProvider Provider, Evaluator Eval, SessionStateManager Manager) MakeEvalWithSession(string sessionId, string sessionDir)
        {
            var configOverrides = new Dictionary<string, string?>
            {
                ["Session:Root"] = sessionDir
            };
            var sp = DependencyInjectionSetup.BuildServiceProvider(configOverrides);
            var ev = sp.GetRequiredService<Evaluator>();
            var manager = sp.GetRequiredService<SessionStateManager>();

            // Configure test mode on SecurityService
            var security = sp.GetRequiredService<SecurityService>();
            security.IsTestMode = true;

            ev.IsPersistentSession = true;
            ev.SessionId = sessionId;
            ev.SessionRoot = sessionDir;

            return (sp, ev, manager);
        }

        [Fact]
        public async Task TestSimpleGotoForward()
        {
            var ev = MakeEval();
            string script = @"
                DECLARE @res INT = 1;
                GOTO target_label;
                SET @res = 2;
                target_label:
                SET @res = 3;
            ";
            await ev.Evaluate(TestHelpers.Parse(script));
            Assert.Equal(3m, ev.Variables["@res"]);
        }

        [Fact]
        public async Task TestGotoBackwardLoop()
        {
            var ev = MakeEval();
            string script = @"
                DECLARE @count INT = 0;
                start_label:
                SET @count = @count + 1;
                IF @count < 5 BEGIN
                    GOTO start_label;
                END
            ";
            await ev.Evaluate(TestHelpers.Parse(script));
            Assert.Equal(5m, ev.Variables["@count"]);
        }

        [Fact]
        public void TestDuplicateLabel_ThrowsParserError()
        {
            string script = @"
                label1:
                PRINT 'hello';
                label1:
                PRINT 'world';
            ";
            var parsed = TestHelpers.Parse(script);
            Assert.NotEmpty(parsed.Diagnostics);
            Assert.Contains(parsed.Diagnostics, d => d.Message.Contains("Duplicate label 'label1'"));
        }

        [Fact]
        public void TestJumpIntoIf_ThrowsParserError()
        {
            string script = @"
                GOTO inner_label;
                IF 1 = 1 BEGIN
                    inner_label:
                    PRINT 'jumped in';
                END
            ";
            var parsed = TestHelpers.Parse(script);
            Assert.NotEmpty(parsed.Diagnostics);
            Assert.Contains(parsed.Diagnostics, d => d.Message.Contains("GOTO cannot jump into nested If block"));
        }

        [Fact]
        public void TestJumpIntoWhile_ThrowsParserError()
        {
            string script = @"
                GOTO inner_label;
                WHILE 1 = 1 BEGIN
                    inner_label:
                    PRINT 'jumped in';
                END
            ";
            var parsed = TestHelpers.Parse(script);
            Assert.NotEmpty(parsed.Diagnostics);
            Assert.Contains(parsed.Diagnostics, d => d.Message.Contains("GOTO cannot jump into nested While block"));
        }

        [Fact]
        public void TestJumpIntoTry_ThrowsParserError()
        {
            string script = @"
                GOTO inner_label;
                BEGIN TRY
                    inner_label:
                    PRINT 'jumped in';
                END TRY
                BEGIN CATCH
                END CATCH
            ";
            var parsed = TestHelpers.Parse(script);
            Assert.NotEmpty(parsed.Diagnostics);
            Assert.Contains(parsed.Diagnostics, d => d.Message.Contains("GOTO cannot jump into nested TryCatch block"));
        }

        [Fact]
        public void TestJumpAcrossGoBatches_ThrowsParserError()
        {
            string script = @"
                GOTO label2;
                GO
                label2:
                PRINT 'jumped across GO';
            ";
            var parsed = TestHelpers.Parse(script);
            Assert.NotEmpty(parsed.Diagnostics);
            Assert.Contains(parsed.Diagnostics, d => d.Message.Contains("GOTO cannot jump across GO batch boundaries to label 'label2'"));
        }

        [Fact]
        public void TestJumpToUndefinedLabel_ThrowsParserError()
        {
            string script = @"
                GOTO non_existent;
            ";
            var parsed = TestHelpers.Parse(script);
            Assert.NotEmpty(parsed.Diagnostics);
            Assert.Contains(parsed.Diagnostics, d => d.Message.Contains("Label 'non_existent' is not defined in this script"));
        }

        [Fact]
        public async Task TestCheckpointAndResume()
        {
            string scriptText = @"
                DECLARE @val INT = 10;
                checkpoint1:
                SET @val = @val + 5;
                checkpoint2:
                SET @val = @val + 10;
            ";

            // 1st run: Execute script to completion. Checkpoints should trigger SaveSession.
            var (_, ev1, sessionManager1) = MakeEvalWithSession(_sessionId, _sessionDir);

            var parsed = TestHelpers.Parse(scriptText);
            await ev1.Evaluate(parsed);

            Assert.Equal(25m, ev1.Variables["@val"]);

            // 2nd run: With --resume, we load the saved state from checkpoint2.
            var (_, ev2, sessionManager2) = MakeEvalWithSession(_sessionId, _sessionDir);
            ev2.IsResuming = true;

            var state = await sessionManager2.LoadSession(_sessionId);
            Assert.NotNull(state);
            await ev2.LoadSessionState(state);

            // Execute the script again in resume mode.
            await ev2.Evaluate(parsed);

            // If it resumed correctly:
            // 1. All statements before checkpoint2 are skipped.
            // 2. The variable state is loaded from the checkpoint, so @val starts at 15.
            // 3. We run the rest of the script: "SET @val = @val + 10", so @val becomes 25.
            Assert.Equal(25m, ev2.Variables["@val"]);
        }
    }
}
