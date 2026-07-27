using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core.Parsing
{
    /// <summary>
    /// BUTTON had only <c>CREATE</c>. <c>DROP BUTTON</c> and <c>ALTER BUTTON</c> did not parse at
    /// all, even though <c>DropReportObjectStatementHandler</c> already removed buttons and
    /// <c>CreateButtonStatementHandler</c>'s duplicate-name error told the author to
    /// "use CREATE OR ALTER or DROP BUTTON first" — advice the parser then rejected.
    /// </summary>
    public class ButtonLifecycleTests
    {
        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        private static async Task<Evaluator> Run(string sql)
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        // ── DROP BUTTON ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("DROP BUTTON GoBack;", false)]
        [InlineData("DROP BUTTON IF EXISTS GoBack;", true)]
        public void DropButton_Parses(string sql, bool ifExists)
        {
            var script = Parse(sql);

            Assert.Empty(script.Diagnostics);
            var stmt = Assert.IsType<DropReportObjectStatement>(Assert.Single(script.Statements));
            Assert.Equal(ReportObjectType.Button, stmt.ObjectType);
            Assert.Equal("GoBack", stmt.Name);
            Assert.Equal(ifExists, stmt.IfExists);
        }

        /// <summary>
        /// The existence modifier goes before the name for every DROP kind; BUTTON must not be the
        /// one exception just because it was added later.
        /// </summary>
        [Fact]
        public void DropButton_TrailingIfExists_IsRejectedWithTheCanonicalForm()
        {
            var diagnostic = Assert.Single(Parse("DROP BUTTON GoBack IF EXISTS;").Diagnostics);

            Assert.Contains("IF EXISTS must come before the object name", diagnostic.Message, StringComparison.Ordinal);
            Assert.Contains("DROP BUTTON IF EXISTS GoBack", diagnostic.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DropButton_RemovesItFromTheReportContext()
        {
            var eval = await Run(
                "CREATE BUTTON GoBack AS (TITLE = 'Back', ACTIONS (ON_CLICK = BACK));" +
                "DROP BUTTON GoBack;");

            Assert.False(eval.ReportContext.ButtonDefinitions.ContainsKey("GoBack"));
        }

        [Fact]
        public async Task DropButton_Missing_Throws_UnlessIfExists()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("DROP BUTTON no_such_button;"));

            // IF EXISTS is the escape hatch, and it must not throw.
            await Run("DROP BUTTON IF EXISTS no_such_button;");
        }

        // ── ALTER BUTTON ──────────────────────────────────────────────────────

        [Fact]
        public void AlterButton_Parses()
        {
            var script = Parse("ALTER BUTTON GoBack (TITLE = 'Return');");

            Assert.Empty(script.Diagnostics);
            var stmt = Assert.IsType<AlterReportObjectStatement>(Assert.Single(script.Statements));
            Assert.Equal(ReportObjectType.Button, stmt.ObjectType);
            Assert.Equal("GoBack", stmt.Name);
        }

        /// <summary>
        /// CREATE BUTTON refuses a trigger other than ON_CLICK. ALTER shares the action parser, so
        /// without the same check a button could be patched into a state CREATE would not accept.
        /// </summary>
        [Fact]
        public void AlterButton_NonClickTrigger_IsRejected()
        {
            var diagnostic = Assert.Single(
                Parse("ALTER BUTTON GoBack (ACTIONS (ON_CHANGE = REFRESH_REPORT));").Diagnostics);

            Assert.Contains("BUTTON actions only support ACTIONS (ON_CLICK = ...)", diagnostic.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AlterButton_PatchesTitleAndLeavesActionsAlone()
        {
            var eval = await Run(
                "CREATE BUTTON GoBack AS (TITLE = 'Back', ACTIONS (ON_CLICK = BACK));" +
                "ALTER BUTTON GoBack (TITLE = 'Return');");

            var button = eval.ReportContext.ButtonDefinitions["GoBack"];
            Assert.Contains("Return", button.Title!.ToSql(), StringComparison.Ordinal);
            Assert.Single(button.Actions);
        }

        [Fact]
        public async Task AlterButton_ReplacesActions()
        {
            var eval = await Run(
                "CREATE BUTTON Refresher AS (TITLE = 'Refresh', ACTIONS (ON_CLICK = BACK));" +
                "ALTER BUTTON Refresher (ACTIONS (ON_CLICK = REFRESH_REPORT));");

            var button = eval.ReportContext.ButtonDefinitions["Refresher"];
            var action = Assert.Single(button.Actions);
            Assert.Contains("REFRESH", action.ToSql(), StringComparison.OrdinalIgnoreCase);
            // The title was not in the patch, so it must survive.
            Assert.Contains("Refresh", button.Title!.ToSql(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task AlterButton_Missing_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("ALTER BUTTON no_such_button (TITLE = 'x');"));
        }

        // ── The advice CREATE BUTTON gives on a duplicate must work ────────────

        [Fact]
        public async Task DuplicateCreate_AdvisesDropButton_AndThatSequenceRuns()
        {
            var duplicate = await Assert.ThrowsAsync<ExecutionException>(() => Run(
                "CREATE BUTTON GoBack AS (TITLE = 'Back', ACTIONS (ON_CLICK = BACK));" +
                "CREATE BUTTON GoBack AS (TITLE = 'Back Again', ACTIONS (ON_CLICK = BACK));"));

            Assert.Contains("DROP BUTTON", duplicate.Message, StringComparison.Ordinal);

            // The remedy the message names has to parse and run.
            await Run(
                "CREATE BUTTON GoBack AS (TITLE = 'Back', ACTIONS (ON_CLICK = BACK));" +
                "DROP BUTTON GoBack;" +
                "CREATE BUTTON GoBack AS (TITLE = 'Back Again', ACTIONS (ON_CLICK = BACK));");
        }
    }
}
