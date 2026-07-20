using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Proves the grammar state tree is a faithful model of the production parser on a curated corpus
    /// of complete statements covering the main authoring workflows. The fuzzer measures this
    /// statistically at scale; these tests pin the invariant for specific, human-readable cases so a
    /// regression names the exact statement that drifted.
    ///
    /// Two directions:
    ///   Recall    — every statement the parser accepts, the grammar must also accept (otherwise
    ///               completion would stop offering valid next tokens mid-statement).
    ///   Precision — the grammar must reject what the parser rejects (otherwise completion would
    ///               suggest tokens that lead to invalid SQL).
    /// </summary>
    public class GrammarParserConformanceTests
    {
        private static List<Token> Tokenize(string sql) => new Lexer(sql).Tokenize();

        private static bool ParserAccepts(string sql)
        {
            var parsed = new Parser(Tokenize(sql), sql).Parse();
            return !parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        }

        private static bool GrammarAccepts(string sql, out string? error) =>
            DefaultGrammar.Build().ValidateSequence(Tokenize(sql), out error, requireComplete: true);

        // Complete, valid statements across the main authoring workflows.
        public static IEnumerable<object[]> ValidStatements() => new[]
        {
            // SELECT shapes
            new object[] { "SELECT * FROM src.Users;" },
            new object[] { "SELECT UserID, UserName FROM src.Users WHERE UserID > 5;" },
            new object[] { "SELECT DISTINCT Region FROM src.Sales ORDER BY Region DESC;" },
            new object[] { "SELECT COUNT(*) AS cnt FROM src.Sales GROUP BY Region HAVING COUNT(*) > 1;" },
            // Joins / aliases
            new object[] { "SELECT u.UserID, o.Total FROM src.Users u JOIN src.Orders o ON u.UserID = o.UserID;" },
            new object[] { "SELECT * FROM src.Users u LEFT JOIN src.Orders o ON u.UserID = o.UserID;" },
            // CTE
            new object[] { "WITH t AS (SELECT * FROM src.Users) SELECT * FROM t;" },
            // Window / OVER / QUALIFY
            new object[] { "SELECT UserID, ROW_NUMBER() OVER (PARTITION BY Region ORDER BY Total DESC) AS rn FROM src.Sales;" },
            new object[] { "SELECT UserID FROM src.Sales QUALIFY ROW_NUMBER() OVER (ORDER BY Total) = 1;" },
            // DML
            new object[] { "INSERT INTO src.Users (UserID, UserName) VALUES (1, 'Alice');" },
            new object[] { "UPDATE src.Users SET UserName = 'Bob' WHERE UserID = 1;" },
            new object[] { "DELETE FROM src.Users WHERE UserID = 1;" },
            // Connections
            new object[] { "CREATE CONNECTION src AS MSSQL (PASSWORD = 'abc');" },
            new object[] { "CREATE CONNECTION f AS FLATFILE (PATH = 'C:\\data.csv', COMPRESS = ON);" },
            // File operations
            new object[] { "COMPRESS FILE 'C:\\raw.csv' TO 'C:\\raw.zip' WITH (OVERWRITE = ON);" },
            new object[] { "ENCRYPT FILE 'C:\\raw.csv' TO 'C:\\raw.pgp' PASSWORD 'Secret123';" },
            // WAITFOR FILE UNLOCKED — the grammar modelled only DELAY/TIME/(condition), so these
            // valid statements linted as syntax errors ("Unexpected token 'FILE'").
            new object[] { "WAITFOR FILE UNLOCKED 'C:\\drop\\export.csv';" },
            new object[] { "WAITFOR FILE UNLOCKED 'C:\\drop\\export.csv' WITH (TIMEOUT = 120);" },
            new object[] { "WAITFOR FILE UNLOCKED 'C:\\drop\\export.csv' WITH (TIMEOUT = 120, POLL_INTERVAL_MS = 500);" },
            // The parser also accepts the options bare, without the WITH(...) wrapper
            // (ParseWaitForFileStatement's else-branch), so the grammar must accept that shape too.
            new object[] { "WAITFOR FILE UNLOCKED 'C:\\drop\\export.csv' TIMEOUT 120;" },
            new object[] { "WAITFOR FILE UNLOCKED 'C:\\drop\\export.csv' TIMEOUT 120 POLL_INTERVAL_MS 500;" },
            // Control flow
            new object[] { "IF @x > 1 BEGIN SELECT 1; END;" },
            new object[] { "WHILE @x < 10 BEGIN SET @x = @x + 1; END;" },
            new object[] { "DECLARE @x INT;" },
            new object[] { "SET @x = 5;" },
        };

        // Statements the parser rejects; the grammar must reject them too.
        public static IEnumerable<object[]> InvalidStatements() => new[]
        {
            new object[] { "CREATE CONNECTION c WITH (PATH = 'x');" },   // WITH instead of AS TYPE(...)
            new object[] { "CREATE CONNECTION c AS MSSQL PATH = 'x';" }, // missing parentheses
            new object[] { "SELECT FROM WHERE;" },                       // no columns / garbage
            new object[] { "COMPRESS 'C:\\raw.csv';" },                  // legacy short form
        };

        [Theory]
        [MemberData(nameof(ValidStatements))]
        public void Grammar_Accepts_EverythingTheParserAccepts(string sql)
        {
            Assert.True(ParserAccepts(sql), $"Corpus sample should be valid SQL but the parser rejected it: {sql}");

            bool grammarAccepts = GrammarAccepts(sql, out var error);
            Assert.True(grammarAccepts,
                $"Grammar recall gap — the parser accepts this but the grammar rejected it (completion would " +
                $"stop suggesting valid next tokens):\n{sql}\nError: {error}");
        }

        [Theory]
        [MemberData(nameof(InvalidStatements))]
        public void Grammar_Rejects_WhatTheParserRejects(string sql)
        {
            Assert.False(ParserAccepts(sql), $"Corpus 'invalid' sample was actually accepted by the parser: {sql}");

            bool grammarAccepts = GrammarAccepts(sql, out _);
            Assert.False(grammarAccepts,
                $"Grammar precision gap — the parser rejects this but the grammar accepted it (completion would " +
                $"suggest tokens leading to invalid SQL):\n{sql}");
        }
    }
}
