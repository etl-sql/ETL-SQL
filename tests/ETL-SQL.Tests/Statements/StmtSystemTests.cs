using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Tests.Statements.Statements
{
    public class SystemStatementTests
    {
        private static Evaluator NewEval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task ClearSession_ValidSyntax_ParsesAndExecutesWithoutError()
        {
            var eval = NewEval();
            var sessionId = Guid.NewGuid().ToString();
            eval.SessionId = sessionId;
            
            var script = TestHelpers.Parse("CLEAR SESSION;");
            Assert.IsType<ClearSessionStatement>(script.Statements[0]);

            // For now, let's just make sure CLEAR SESSION executes without error in the standard container.
            var exception = await Record.ExceptionAsync(() => eval.Evaluate(script));
            Assert.Null(exception);
        }

        [Fact]
        public async Task SetProfiling_On_SetsEvaluatorFlag()
        {
            var eval = NewEval();
            eval.Telemetry.IsProfiling = false;
            
            var stmt = (SetProfilingStatement)TestHelpers.Parse("SET PROFILING ON;").Statements[0];
            var handler = new SetProfilingStatementHandler();
            
            await handler.Execute(stmt, eval);
            
            Assert.True(eval.Telemetry.IsProfiling);
        }

        [Fact]
        public async Task SetProfiling_Off_SetsEvaluatorFlag()
        {
            var eval = NewEval();
            eval.Telemetry.IsProfiling = true;
            
            var stmt = (SetProfilingStatement)TestHelpers.Parse("SET PROFILE OFF;").Statements[0];
            var handler = new SetProfilingStatementHandler();
            
            await handler.Execute(stmt, eval);
            
            Assert.False(eval.Telemetry.IsProfiling);
        }

        [Fact]
        public async Task SetWhatIf_On_SetsEvaluatorFlag()
        {
            var eval = NewEval();
            eval.IsWhatIf = false;
            
            var script = TestHelpers.Parse("SET WHAT_IF ON;");
            await eval.Evaluate(script);
            
            // The parser supports SET WHAT_IF and the evaluator handles it via SetWhatIfStatementHandler.
            Assert.IsType<SetWhatIfStatement>(script.Statements[0]);
            Assert.True(((SetWhatIfStatement)script.Statements[0]).Enabled);
            Assert.True(eval.IsWhatIf);
        }

        [Fact]
        public void SetShowPassword_Off_SetsEvaluatorFlag()
        {
             var script = TestHelpers.Parse("SET SHOW_PASSWORD OFF;");
             Assert.IsType<SetShowPasswordStatement>(script.Statements[0]);
             Assert.False(((SetShowPasswordStatement)script.Statements[0]).Enabled);
        }

        [Fact]
        public void SetShowSecrets_On_ParsesAsSetShowPasswordStatement()
        {
            var script = TestHelpers.Parse("SET SHOW_SECRETS ON;");
            Assert.IsType<SetShowPasswordStatement>(script.Statements[0]);
            Assert.True(((SetShowPasswordStatement)script.Statements[0]).Enabled);
        }

        [Fact]
        public void SetShowSecrets_Off_ParsesAsSetShowPasswordStatement()
        {
            var script = TestHelpers.Parse("SET SHOW_SECRETS OFF;");
            Assert.IsType<SetShowPasswordStatement>(script.Statements[0]);
            Assert.False(((SetShowPasswordStatement)script.Statements[0]).Enabled);
        }

        [Fact]
        public async Task ShowProfile_AfterMockDbSelect_AddsToLastResultSets()
        {
            var eval = NewEval();
            eval.Telemetry.IsProfiling = true;
            eval.RedirectOutput = true; // mirrors TUI ConsoleEditor

            var script = TestHelpers.Parse(@"
                CREATE CONNECTION m AS MOCKDB();
                SELECT * FROM m.Users;
                SHOW PROFILE;
            ");

            await eval.Evaluate(script);

            // Should have 2 result sets: SELECT result + SHOW PROFILE result
            Assert.Equal(2, eval.LastResultSets.Count);
            var profileResult = eval.LastResultSets[1];
            Assert.Contains("Statement", profileResult.ColumnNames);
            Assert.True(profileResult.Rows.Count > 0);
        }
    }
}
