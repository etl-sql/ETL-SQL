using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Statements
{
    /// <summary>
    /// Verifies that parser error messages name the construct and the expected token.
    /// One Theory per language construct; each InlineData is one common mistake.
    /// </summary>
    public class ParserErrorQualityTests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens).Parse();
        }

        private static string GetFirstErrorMessage(string sql)
        {
            var script = Parse(sql);
            var error = script.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            Assert.NotNull(error);
            return error.Message;
        }

        // ── GOTO ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("GOTO ;",   "GOTO",  "identifier")]
        [InlineData("GOTO 42;", "GOTO",  "identifier")]
        public void GotoError_MessageNamesConstructAndExpectedToken(
            string sql, string construct, string expectedToken)
        {
            var msg = GetFirstErrorMessage(sql);
            Assert.Contains(construct,     msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedToken, msg, StringComparison.OrdinalIgnoreCase);
        }

        // ── CREATE CONNECTION ─────────────────────────────────────────────────

        [Theory]
        [InlineData("CREATE CONNECTION ;",
            "CREATE CONNECTION", "connection name")]
        [InlineData("CREATE CONNECTION MyConn SQLSERVER 'server=.' WITH key = 'val';",
            "CREATE CONNECTION", "(")]
        [InlineData("CREATE CONNECTION MyConn SQLSERVER 'server=.' WITH (key 'val');",
            "CREATE CONNECTION", "=")]
        [InlineData("CREATE CONNECTION MyConn SQLSERVER 'server=.' WITH (key = 'val'",
            "CREATE CONNECTION", ")")]
        public void CreateConnectionError_MessageNamesConstructAndExpectedToken(
            string sql, string construct, string expectedToken)
        {
            var msg = GetFirstErrorMessage(sql);
            Assert.Contains(construct,     msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedToken, msg, StringComparison.OrdinalIgnoreCase);
        }

        // ── SEND EMAIL ────────────────────────────────────────────────────────

        [Theory]
        [InlineData("SEND EMAIL FROM 'a@b.com' SUBJECT 'Hi' BODY 'Hello';",
            "SEND EMAIL", "TO")]
        [InlineData("SEND EMAIL TO 'a@b.com' SUBJECT 'Hi' BODY 'Hello';",
            "SEND EMAIL", "FROM")]
        [InlineData("SEND EMAIL TO 'a@b.com' FROM 'b@c.com' BODY 'Hello';",
            "SEND EMAIL", "SUBJECT")]
        [InlineData("SEND EMAIL TO 'a@b.com' FROM 'b@c.com' SUBJECT 'Hi';",
            "SEND EMAIL", "BODY")]
        public void SendEmailError_MessageNamesConstructAndMissingClause(
            string sql, string construct, string missingClause)
        {
            var msg = GetFirstErrorMessage(sql);
            Assert.Contains(construct,     msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(missingClause, msg, StringComparison.OrdinalIgnoreCase);
        }

        // ── RUN SCRIPT ────────────────────────────────────────────────────────

        [Theory]
        [InlineData("RUN 'path.sql';",
            "RUN", "SCRIPT")]
        [InlineData("RUN SCRIPT 'path.sql' WITH @x = 1;",
            "RUN SCRIPT", "(")]
        [InlineData("RUN SCRIPT 'path.sql' WITH (@x 1);",
            "RUN SCRIPT", "=")]
        [InlineData("RUN SCRIPT 'path.sql' WITH (@x = 1",
            "RUN SCRIPT", ")")]
        public void RunScriptError_MessageNamesConstructAndExpectedToken(
            string sql, string construct, string expectedToken)
        {
            var msg = GetFirstErrorMessage(sql);
            Assert.Contains(construct,     msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedToken, msg, StringComparison.OrdinalIgnoreCase);
        }

        // ── BEGIN / END block ─────────────────────────────────────────────────

        [Theory]
        [InlineData("BEGIN PRINT 'hello';",           "BEGIN", "END")]
        [InlineData("BEGIN\nIF 1=1\nBEGIN PRINT 'x';", "BEGIN", "END")]
        public void BeginEndError_MessageNamesConstructAndExpectedToken(
            string sql, string construct, string expectedToken)
        {
            var msg = GetFirstErrorMessage(sql);
            Assert.Contains(construct,     msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedToken, msg, StringComparison.OrdinalIgnoreCase);
        }
    }
}
