using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Connectors;


namespace ETL_SQL.Tests.Statements
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

        private static async Task<System.Collections.Generic.List<LintResult>> Lint(string sql)
        {
            // Ensure connectors are registered for the linter
            var connectors = new System.Collections.Generic.List<IConnector> 
            { 
                new ETL_SQL.Connectors.SqlServer.SqlServerConnector(),
                new ETL_SQL.Connectors.Postgres.PostgresConnector()
            };
            var registry = new ConnectorRegistry(connectors);

            var script = Parse(sql);
            var linter = new Linter();
            linter.AddRule(new DialectKeywordRule());
            return (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();
        }


        [Fact]
        public async Task DialectKeyword_Warns_When_TOP_Used_In_Postgres_Pushdown()
        {
            var sql = @"
CREATE CONNECTION pg_conn ON POSTGRES('Server=localhost;');
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
CREATE CONNECTION ss_conn ON MSSQL('Server=localhost;');
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
CREATE CONNECTION pg_conn ON POSTGRES('Server=localhost;');
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
CREATE CONNECTION pg_conn ON POSTGRES('Server=localhost;');
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
CREATE CONNECTION pg_conn ON POSTGRES('Server=localhost;');
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
CREATE CONNECTION pg_conn ON POSTGRES('Server=localhost;');
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
CREATE CONNECTION ss_conn ON MSSQL('Server=localhost;');
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
CREATE CONNECTION ora_conn ON ORACLE('Server=localhost;');
SELECT id FROM ora_conn.users WHERE ROWNUM < 10;";
            var results = await Lint(sql);
            Assert.DoesNotContain(results, r => r.RuleName == "DialectKeyword");
        }

        [Fact]
        public async Task DialectKeyword_Warns_When_ISNULL_Used_In_Postgres_Pushdown()
        {
            var sql = @"
CREATE CONNECTION pg_conn ON POSTGRES('Server=localhost;');
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
