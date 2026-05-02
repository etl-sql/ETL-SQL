using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using Xunit;

namespace ETL_SQL.Tests
{
    public class SetWeekStartDayTests
    {
        // ── Parser ─────────────────────────────────────────────────────────────

        [Fact]
        public void Parser_ParsesSetWeekStartDay()
        {
            var script = TestHelpers.Parse("SET WEEK_START_DAY = 'Sunday';");
            var stmt = Assert.IsType<SetWeekStartDayStatement>(Assert.Single(script.Statements));
            Assert.Equal("Sunday", stmt.DayName);
        }

        [Fact]
        public void Parser_ParsesWithoutSemicolon()
        {
            var script = TestHelpers.Parse("SET WEEK_START_DAY = 'Wednesday'");
            Assert.IsType<SetWeekStartDayStatement>(Assert.Single(script.Statements));
        }

        // ── Engine: valid day names ────────────────────────────────────────────

        [Theory]
        [InlineData("Monday",    DayOfWeek.Monday)]
        [InlineData("Tuesday",   DayOfWeek.Tuesday)]
        [InlineData("Wednesday", DayOfWeek.Wednesday)]
        [InlineData("Thursday",  DayOfWeek.Thursday)]
        [InlineData("Friday",    DayOfWeek.Friday)]
        [InlineData("Saturday",  DayOfWeek.Saturday)]
        [InlineData("Sunday",    DayOfWeek.Sunday)]
        public async Task Engine_AllSevenDays_SetCorrectly(string dayName, DayOfWeek expected)
        {
            var eval = ETL_SQL.Program.ServiceProvider!.GetService(typeof(Evaluator)) as Evaluator
                       ?? throw new InvalidOperationException("Evaluator not registered.");

            await TestHelpers.Execute(eval, $"SET WEEK_START_DAY = '{dayName}';");
            Assert.Equal(expected, eval.WeekStartDay);
        }

        [Theory]
        [InlineData("MONDAY")]
        [InlineData("monday")]
        [InlineData("Monday")]
        public async Task Engine_CaseInsensitive(string dayName)
        {
            var eval = ETL_SQL.Program.ServiceProvider!.GetService(typeof(Evaluator)) as Evaluator
                       ?? throw new InvalidOperationException("Evaluator not registered.");

            await TestHelpers.Execute(eval, $"SET WEEK_START_DAY = '{dayName}';");
            Assert.Equal(DayOfWeek.Monday, eval.WeekStartDay);
        }

        [Fact]
        public async Task Engine_InvalidDay_ThrowsExecutionException()
        {
            var eval = ETL_SQL.Program.ServiceProvider!.GetService(typeof(Evaluator)) as Evaluator
                       ?? throw new InvalidOperationException("Evaluator not registered.");

            await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(
                () => TestHelpers.Execute(eval, "SET WEEK_START_DAY = 'Funday';"));
        }
    }
}
