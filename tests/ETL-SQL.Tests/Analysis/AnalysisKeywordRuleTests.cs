using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Connectors;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using Xunit;


namespace ETL_SQL.Tests.Analysis.Statements
{
    /// <summary>
    /// Tests for Syntax 19: DialectKeywordRule warns when pushdown SQL uses keywords
    /// excluded by the target connector's dialect.
    /// </summary>
    public class DialectKeywordRuleTests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        /// <summary>
        /// Builds the two connectors this rule is about, without touching
        /// <see cref="ConnectorRegistry.Instance"/>.
        ///
        /// <para>The <c>ConnectorRegistry(IEnumerable&lt;IConnector&gt;)</c> constructor assigns the
        /// process-wide instance as a side effect. This helper used to call it, never pass the
        /// result anywhere, and lint with a bare context — so the rule was reached through the global
        /// the constructor had just overwritten, and every later test in the run inherited a registry
        /// holding two connectors. Registering onto a private instance and handing it to the context
        /// makes the dependency the one the code actually reads.</para>
        /// </summary>
        private static IConnectorRegistry DialectConnectors()
        {
            var registry = new ConnectorRegistry();
            registry.Register(new ETL_SQL.Connectors.SqlServer.SqlServerConnector());
            registry.Register(new ETL_SQL.Connectors.Postgres.PostgresConnector());
            return registry;
        }

        private static async Task<System.Collections.Generic.List<LintResult>> Lint(string sql)
        {
            var script = Parse(sql);
            var linter = new Linter();
            linter.AddRule(new DialectKeywordRule());
            var context = new DefaultLintContext { Connectors = DialectConnectors() };
            return (await linter.AnalyzeAsync(script, context)).ToList();
        }

        [Fact]
        public async Task DialectKeyword_Reads_The_Context_Registry_Not_The_Global()
        {
            // The point of ILintContext.Connectors: a caller's own registry must win over whatever
            // the process-wide one happens to hold. An empty global would make the rule skip
            // entirely if it were still reading Instance, so a warning here can only come from the
            // registry the context supplied.
            using var _ = ConnectorRegistry.UseScoped(new ConnectorRegistry());

            var script = Parse(@"
CREATE CONNECTION pg_conn AS POSTGRES('Server=localhost;');
EXECUTE pg_conn BEGIN
    SELECT TOP 10 id, name FROM users;
END;");
            var linter = new Linter();
            linter.AddRule(new DialectKeywordRule());

            var withContextRegistry = (await linter.AnalyzeAsync(script, new DefaultLintContext { Connectors = DialectConnectors() })).ToList();
            var withEmptyGlobal = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.NotEmpty(withContextRegistry);
            Assert.Empty(withEmptyGlobal);
        }

        [Fact]
        public void UseScoped_Restores_The_Previous_Registry_And_Is_Idempotent()
        {
            var original = ConnectorRegistry.Instance;
            var scoped = new ConnectorRegistry();

            var scope = ConnectorRegistry.UseScoped(scoped);
            Assert.Same(scoped, ConnectorRegistry.Instance);

            scope.Dispose();
            Assert.Same(original, ConnectorRegistry.Instance);

            // A second dispose must not put `original` back over a registry some later scope has
            // since installed — that would be the restore itself becoming the pollution.
            var later = new ConnectorRegistry();
            using var laterScope = ConnectorRegistry.UseScoped(later);
            scope.Dispose();
            Assert.Same(later, ConnectorRegistry.Instance);
        }


        [Fact]
        public async Task DialectKeyword_Warns_When_TOP_Used_In_Postgres_Pushdown()
        {
            var sql = @"
CREATE CONNECTION pg_conn AS POSTGRES('Server=localhost;');
EXECUTE pg_conn BEGIN
    SELECT TOP 10 id, name FROM users;
END;";
            var results = await Lint(sql);
            Assert.Contains(results, r => r.RuleName == "DialectKeyword"
                && r.Message.Contains("TOP")
                && r.Message.Contains("POSTGRES"));
        }

        [Fact]
        public async Task DialectKeyword_Warns_When_LIMIT_Used_In_SqlServer_Pushdown()
        {
            var sql = @"
CREATE CONNECTION ss_conn AS MSSQL('Server=localhost;');
EXECUTE ss_conn BEGIN
    SELECT id, name FROM users LIMIT 10;
END;";
            var results = await Lint(sql);
            Assert.Contains(results, r => r.RuleName == "DialectKeyword"
                && r.Message.Contains("LIMIT")
                && r.Message.Contains("MSSQL"));
        }

        [Fact]
        public async Task DialectKeyword_NoWarning_For_Valid_Postgres_Pushdown()
        {
            var sql = @"
CREATE CONNECTION pg_conn AS POSTGRES('Server=localhost;');
EXECUTE pg_conn BEGIN
    SELECT id, name FROM users LIMIT 10;
END;";
            var results = await Lint(sql);
            Assert.DoesNotContain(results, r => r.RuleName == "DialectKeyword");
        }

        [Fact]
        public async Task DialectKeyword_NoWarning_When_No_Connection_Declared()
        {
            // EXECUTE without a CREATE CONNECTION — rule skips silently
            var sql = @"
EXECUTE [some_conn] BEGIN
    SELECT TOP 10 * FROM t;
END;";
            var results = await Lint(sql);
            Assert.DoesNotContain(results, r => r.RuleName == "DialectKeyword");
        }

        [Fact]
        public async Task DialectKeyword_Warns_When_TOP_Used_In_Postgres_Select()
        {
            var sql = @"
CREATE CONNECTION pg_conn AS POSTGRES('Server=localhost;');
SELECT TOP 10 id FROM pg_conn.users;";
            var results = await Lint(sql);
            Assert.Contains(results, r => r.RuleName == "DialectKeyword"
                && r.Message.Contains("TOP")
                && r.Message.Contains("pg_conn"));
        }

        [Fact]
        public async Task DialectKeyword_Warns_When_PERCENT_Used_In_Postgres_Select()
        {
            var sql = @"
CREATE CONNECTION pg_conn AS POSTGRES('Server=localhost;');
SELECT TOP 10 PERCENT id FROM pg_conn.users;";
            var results = await Lint(sql);
            Assert.Contains(results, r => r.RuleName == "DialectKeyword"
                && r.Message.Contains("PERCENT")
                && r.Message.Contains("pg_conn"));
        }

        [Fact]
        public async Task DialectKeyword_Warns_When_ROWNUM_Used_In_Postgres_Select()
        {
            var sql = @"
CREATE CONNECTION pg_conn AS POSTGRES('Server=localhost;');
SELECT id FROM pg_conn.users WHERE ROWNUM < 10;";
            var results = await Lint(sql);
            Assert.Contains(results, r => r.RuleName == "DialectKeyword"
                && r.Message.Contains("ROWNUM")
                && r.Message.Contains("pg_conn"));
        }

        [Fact]
        public async Task DialectKeyword_Warns_When_LIMIT_Used_In_SqlServer_Select()
        {
            var sql = @"
CREATE CONNECTION ss_conn AS MSSQL('Server=localhost;');
SELECT id FROM ss_conn.users LIMIT 10;";
            var results = await Lint(sql);
            Assert.Contains(results, r => r.RuleName == "DialectKeyword"
                && r.Message.Contains("LIMIT")
                && r.Message.Contains("ss_conn"));
        }

        [Fact]
        public async Task DialectKeyword_Allows_ROWNUM_In_Oracle_Select()
        {
            // Register oracle for this test
            var connectors = new System.Collections.Generic.List<IConnector>
            {
                new ETL_SQL.Connectors.Oracle.OracleConnector()
            };
            new ConnectorRegistry(connectors);

            var sql = @"
CREATE CONNECTION ora_conn AS ORACLE('Server=localhost;');
SELECT id FROM ora_conn.users WHERE ROWNUM < 10;";
            var results = await Lint(sql);
            Assert.DoesNotContain(results, r => r.RuleName == "DialectKeyword");
        }

        [Fact]
        public async Task DialectKeyword_Warns_When_ISNULL_Used_In_Postgres_Pushdown()
        {
            var sql = @"
CREATE CONNECTION pg_conn AS POSTGRES('Server=localhost;');
EXECUTE pg_conn BEGIN
    SELECT ISNULL(col, 0) FROM t;
END;";
            var results = await Lint(sql);
            Assert.Contains(results, r => r.RuleName == "DialectKeyword"
                && r.Message.Contains("ISNULL")
                && r.Message.Contains("POSTGRES"));
        }
    }
}
