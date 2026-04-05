using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;


namespace ETL_SQL.Tests
{
    public class LinterTests
    {
        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }

        [Fact]
        public async Task TestSafeDeleteUpdateRule()
        {
            var linter = new Linter();
            linter.AddRule(new SafeDeleteUpdateRule());

            var sql = @"
                DELETE FROM MyTable WHERE ID = 1;
                DELETE FROM GlobalTable;
                UPDATE Customers SET Name = 'Bob';
                UPDATE Orders SET Status = 'Shipped' WHERE ID = 5;
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Equal(2, results.Count(r => r.Severity == LintSeverity.Error));
            Assert.Contains(results, r => r.Message.Contains("DELETE") && r.Message.Contains("missing a WHERE clause"));
            Assert.Contains(results, r => r.Message.Contains("UPDATE") && r.Message.Contains("missing a WHERE clause"));
        }

        [Fact]
        public async Task TestAvoidSelectStarRule()
        {
            var linter = new Linter();
            linter.AddRule(new AvoidSelectStarRule());

            var sql = @"
                SELECT ID, Name FROM Users;
                SELECT * FROM Logs;
                SELECT * INTO ConfigBackup FROM Config;
                INSERT INTO Dest SELECT * FROM Src;
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Equal(3, results.Count(r => r.Severity == LintSeverity.Warning));
        }

        [Fact]
        public async Task TestUndeclaredVariableRule()
        {
            var linter = new Linter();
            linter.AddRule(new UndeclaredVariableRule());

            var sql = @"
                DECLARE @declared INT = 10;
                PRINT(@declared);
                SET @undeclared = 20;
                IF @declared > 5 
                BEGIN
                    PRINT(@anotherUndeclared);
                END
                FOR @i = 1 TO 10
                BEGIN
                    PRINT(@i);
                END
                PRINT(@i);
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            // Expect 3 errors: @undeclared, @anotherUndeclared, and @i (after loop)
            Assert.Equal(3, results.Count(r => r.Severity == LintSeverity.Error));
            Assert.Contains(results, r => r.Message.Contains("@undeclared"));
            Assert.Contains(results, r => r.Message.Contains("@anotherUndeclared"));
            Assert.Contains(results, r => r.Message.Contains("@i"));
        }
        [Fact]
        public void TestLintStatementParsing()
        {
            var sqlPrefix = "LINT 'test.sql';";
            var script = Parse(sqlPrefix);
            Assert.Single(script.Statements);
            Assert.IsType<LintStatement>(script.Statements[0]);
            Assert.Equal("test.sql", ((LintStatement)script.Statements[0]).ScriptPath);
        }
        [Fact]
        public async Task TestProcedureParameterScoping()
        {
            var sql = @"
CREATE PROCEDURE MyProc (@param1 INT)
AS
BEGIN
    SELECT * FROM MyTable WHERE Id = @param1;
END;

SELECT * FROM MyTable WHERE Id = @param2; -- Should error
";
            var script = Parse(sql);
            
            var rule = new UndeclaredVariableRule();
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            
            Assert.Single(results);
            Assert.Equal("@param2", results[0].Message.Split('\'')[1]);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_TrustedPlusUserId_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // TRUSTED_CONNECTION and USER_ID together — should be flagged
            var sql = "CREATE CONNECTION db ON MSSQL() WITH(TRUSTED_CONNECTION='TRUE', USER_ID='sa', DATABASE='AdventureWorks');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("TRUSTED_CONNECTION", results[0].Message);
            Assert.Contains("USER_ID", results[0].Message);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_TrustedPlusPassword_IsError()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // TRUSTED_CONNECTION and PASSWORD together — should also be flagged
            var sql = "CREATE CONNECTION db ON MSSQL() WITH(TRUSTED_CONNECTION='TRUE', PASSWORD='secret');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("TRUSTED_CONNECTION", results[0].Message);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_SqlAuthOnly_NoError()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // Valid SQL auth — no TRUSTED_CONNECTION at all
            var sql = "CREATE CONNECTION db ON MSSQL() WITH(USER_ID='sa', PASSWORD='secret', DATABASE='AdventureWorks');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_WindowsAuthOnly_NoError()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // Valid Windows auth — TRUSTED_CONNECTION with no USER_ID or PASSWORD
            var sql = "CREATE CONNECTION db ON MSSQL() WITH(TRUSTED_CONNECTION='TRUE', DATABASE='AdventureWorks');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task TestConnectionAuthConflictRule_FileConnectorExempt()
        {
            var linter = new Linter();
            linter.AddRule(new ConnectionAuthConflictRule());

            // File connectors should not be checked for TRUSTED_CONNECTION conflicts
            var sql = "CREATE CONNECTION f ON FLATFILE('C:\\Data\\') WITH(TRUSTED_CONNECTION='TRUE', PASSWORD='secret');";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }
    }
}
