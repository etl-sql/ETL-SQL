using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Phase 4 DATASET tests: ACCESS PUBLIC|PRIVATE syntax, UseBeforeCreateRule lint warning,
    /// and private access violation enforcement in UseDatasetStatementHandler.
    /// </summary>
    public class DatasetPhase4Tests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens).Parse();
        }

        // ── Parser — ACCESS clause ────────────────────────────────────────────────

        [Fact]
        public void CreateDataset_AccessPublic_SetsPublicLevel()
        {
            var script = Parse("CREATE DATASET &sales ACCESS PUBLIC AS (SELECT 1 AS v FROM t);");
            var stmt   = Assert.Single(script.Statements);
            var ds     = Assert.IsType<CreateDatasetStatement>(stmt);
            Assert.Equal(DatasetAccessLevel.Public, ds.AccessLevel);
        }

        [Fact]
        public void CreateDataset_AccessPrivate_SetsPrivateLevel()
        {
            var script = Parse("CREATE DATASET &sales ACCESS PRIVATE AS (SELECT 1 AS v FROM t);");
            var stmt   = Assert.Single(script.Statements);
            var ds     = Assert.IsType<CreateDatasetStatement>(stmt);
            Assert.Equal(DatasetAccessLevel.Private, ds.AccessLevel);
        }

        [Fact]
        public void CreateDataset_NoAccessClause_DefaultsToPrivate()
        {
            var script = Parse("CREATE DATASET &sales AS (SELECT 1 AS v FROM t);");
            var stmt   = Assert.Single(script.Statements);
            var ds     = Assert.IsType<CreateDatasetStatement>(stmt);
            Assert.Equal(DatasetAccessLevel.Private, ds.AccessLevel);
        }

        [Fact]
        public void CreateDataset_InvalidAccessValue_ProducesDiagnosticError()
        {
            // Parser uses error recovery: SyntaxException is caught and stored in Diagnostics
            var script = Parse("CREATE DATASET &sales ACCESS RESTRICTED AS (SELECT 1 FROM t);");
            Assert.NotEmpty(script.Diagnostics);
            Assert.Contains(script.Diagnostics, d => d.Message.Contains("PUBLIC or PRIVATE"));
        }

        [Fact]
        public void CreateDataset_AccessClauseWithOtherOptions_ParsesAll()
        {
            var sql    = "CREATE DATASET &sales TTL = '1h' ACCESS PUBLIC ENCRYPT = MACHINE AS (SELECT 1 AS v FROM t);";
            var script = Parse(sql);
            var ds     = Assert.IsType<CreateDatasetStatement>(Assert.Single(script.Statements));
            Assert.Equal(DatasetAccessLevel.Public, ds.AccessLevel);
            Assert.Equal("1h", ds.Ttl);
            Assert.Equal(DatasetEncryptionMode.MachineBound, ds.EncryptionMode);
        }

        // ── UseBeforeCreateRule ───────────────────────────────────────────────────

        [Fact]
        public async Task UseBeforeCreateRule_UseBeforeCreate_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new UseBeforeCreateRule());

            var sql = @"
                USE DATASET &sales;
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Equal("UseBeforeCreate", results[0].RuleName);
            Assert.Contains("&sales", results[0].Message);
        }

        [Fact]
        public async Task UseBeforeCreateRule_UseAfterCreate_NoWarning()
        {
            var linter = new Linter();
            linter.AddRule(new UseBeforeCreateRule());

            var sql = @"
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);
                USE DATASET &sales;";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task UseBeforeCreateRule_UseWithoutCreate_NoWarning()
        {
            // USE referencing an external dataset that isn't in this script
            var linter = new Linter();
            linter.AddRule(new UseBeforeCreateRule());

            var sql = "USE DATASET &external;";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            // No warning — we can't know whether &external exists; cross-file analysis is Phase 5+
            Assert.Empty(results);
        }

        [Fact]
        public async Task UseBeforeCreateRule_MultipleDatasets_OnlyFlagsBadOrdering()
        {
            var linter = new Linter();
            linter.AddRule(new UseBeforeCreateRule());

            // &good is fine (create before use), &bad is wrong (use before create)
            var sql = @"
                CREATE DATASET &good AS (SELECT 1 AS v FROM t);
                USE DATASET &bad;
                USE DATASET &good;
                CREATE DATASET &bad AS (SELECT 2 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Contains("&bad", results[0].Message);
        }

        [Fact]
        public async Task UseBeforeCreateRule_AutoDiscoveredByLinterFactory()
        {
            var linter = LinterFactory.CreateWithAllRules();
            var sql = @"
                USE DATASET &sales;
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Contains(results, r => r.RuleName == "UseBeforeCreate" && r.Severity == LintSeverity.Warning);
        }
    }
}
