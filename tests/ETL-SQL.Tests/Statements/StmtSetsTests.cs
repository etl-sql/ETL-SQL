using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements.Statements
{
    public class SetsTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task CreateSets_StoresTwoVariables()
        {
            var e = NewEvaluator();
            var script = @"
DECLARE @env VARCHAR(50);
CREATE SETS !DEV
BEGIN
    @env = 'development'
END
USE SETS !DEV;
SELECT @env;";
            await e.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            Assert.NotNull(e.LastResult);
            Assert.Equal("development", e.LastResult.Rows[0][0]?.ToString());
        }

        [Fact]
        public async Task CreateSets_MultipleVariables()
        {
            var e = NewEvaluator();
            var script = @"
DECLARE @conn1 VARCHAR(100);
DECLARE @conn2 VARCHAR(100);
CREATE SETS !PROD
BEGIN
    @conn1 = 'prod_db1',
    @conn2 = 'prod_db2'
END
USE SETS !PROD;
SELECT @conn1, @conn2;";
            await e.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            Assert.NotNull(e.LastResult);
            Assert.Equal("prod_db1", e.LastResult.Rows[0][0]?.ToString());
            Assert.Equal("prod_db2", e.LastResult.Rows[0][1]?.ToString());
        }

        [Fact]
        public async Task DropSets_RemovesSet()
        {
            var e = NewEvaluator();
            var script = @"
CREATE SETS !TEMP BEGIN @x = 1 END
DROP SETS !TEMP;";
            await e.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
            Assert.False(e.NamedSets.ContainsKey("TEMP"), "Set should be removed after DROP SETS");
        }

        [Fact]
        public async Task DropSets_IfExists_DoesNotThrowWhenMissing()
        {
            var e = NewEvaluator();
            var script = "DROP SETS IF EXISTS !NONEXISTENT;";
            await e.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
            // No exception = pass
        }

        [Fact]
        public async Task UseSets_WithPrompt_NonInteractiveAutoProceed()
        {
            var e = NewEvaluator();
            // OnPrompt is null → non-interactive → auto-proceed even with WITH_PROMPT ON
            var script = @"
DECLARE @mode VARCHAR(50) = 'dev';
CREATE SETS !PROD
BEGIN
    @mode = 'prod';
    SET WITH_PROMPT ON;
END
USE SETS !PROD;
SELECT @mode;";
            await e.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
            Assert.Equal("prod", e.LastResult?.Rows[0][0]?.ToString());
        }

        [Fact]
        public async Task UseSets_WithPrompt_UserAborts()
        {
            var e = NewEvaluator();
            e.OnPrompt = _ => Task.FromResult(false); // user says NO

            var script = @"
DECLARE @mode VARCHAR(50) = 'dev';
CREATE SETS !PROD
BEGIN
    @mode = 'prod';
    SET WITH_PROMPT ON;
END
USE SETS !PROD;
SELECT @mode;";
            await e.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
            // Variable should remain 'dev' because user aborted
            Assert.Equal("dev", e.LastResult?.Rows[0][0]?.ToString());
        }

        [Fact]
        public async Task ParseSets_AstRoundTrip()
        {
            var src = "CREATE SETS !MY_ENV BEGIN @x = 1 END";
            var ast = new Parser(new Lexer(src).Tokenize()).Parse();
            Assert.Single(ast.Statements);
            Assert.IsType<CreateSetsStatement>(ast.Statements[0]);
            var stmt = (CreateSetsStatement)ast.Statements[0];
            Assert.Equal("MY_ENV", stmt.Name);
            Assert.Single(stmt.Assignments);
            Assert.Equal("x", stmt.Assignments[0].VariableName);
        }
    }
}
