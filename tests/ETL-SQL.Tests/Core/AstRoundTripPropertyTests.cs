using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    /// <summary>
    /// No clause may disappear when a statement is serialized back to SQL.
    ///
    /// <para>The round-trip tests that existed were written per feature, by whoever added the
    /// feature — so a clause added later had none. That is how <c>ON FAILURE</c> came to be dropped
    /// entirely by <c>ToSql()</c>: the statement still parsed, and the resulting script routed its
    /// <c>@fail: 'QUARANTINE'</c> rows nowhere. Silent behaviour loss, found by reading the code
    /// rather than by any test.</para>
    ///
    /// <para>Rather than compare ASTs — which differ in source positions and would need a bespoke
    /// comparer per node — this asserts the weaker but broadly applicable property that every
    /// keyword in the input survives serialization. A dropped clause always loses its keyword.</para>
    /// </summary>
    public class AstRoundTripPropertyTests
    {
        /// <summary>
        /// Keywords the serializer may legitimately not reproduce, because it normalizes the form
        /// rather than dropping meaning. Each entry is a decision, so keep this list short and say
        /// why — an entry added to silence a failure would hide exactly what the test is for.
        /// </summary>
        private static readonly HashSet<string> NormalizedAway = new(StringComparer.OrdinalIgnoreCase)
        {
            "AS",     // optional before an alias; the alias itself still has to survive
            "INNER",  // INNER JOIN normalizes to JOIN
            "OUTER",  // LEFT OUTER JOIN normalizes to LEFT JOIN
            "ROWS",   // OFFSET n ROWS / FETCH ... ROWS ONLY spelling
        };

        public static TheoryData<string, string> Statements() => new()
        {
            { "select-full", "SELECT DISTINCT TOP (5) a, b FROM t WHERE a > 1 GROUP BY a HAVING COUNT(*) > 2 ORDER BY a DESC LIMIT 10 OFFSET 2 ROWS;" },
            { "select-into", "SELECT a, b INTO dest FROM src;" },
            { "select-join", "SELECT t.a, u.b FROM t INNER JOIN u ON t.id = u.id LEFT JOIN v ON v.id = t.id;" },
            { "select-window", "SELECT a, ROW_NUMBER() OVER (PARTITION BY a ORDER BY b) AS rn FROM t;" },
            { "select-qualify", "SELECT a, ROW_NUMBER() OVER (PARTITION BY a ORDER BY b) AS rn FROM t QUALIFY rn <= 1;" },
            { "select-cte", "WITH c AS (SELECT a FROM t) SELECT a FROM c;" },
            { "select-sample", "SELECT a FROM t USING SAMPLE 10 PERCENT REPEATABLE (7);" },
            { "select-on-failure", "load: SELECT a INTO dest FROM src ON FAILURE QUARANTINE TO q WITH (RETENTION = '30 DAYS', HANDLING = SCRIPT) ON FAILURE WARN TO w ON FAILURE THROW;" },
            { "insert-values", "INSERT INTO t (a, b) VALUES (1, 2), (3, 4);" },
            { "insert-select", "INSERT INTO t (a) SELECT a FROM src WHERE a > 0;" },
            { "update", "UPDATE t SET a = 1 FROM src WHERE t.id = src.id;" },
            { "delete", "DELETE FROM t WHERE a = 1;" },
            { "truncate", "TRUNCATE TABLE t;" },
            { "drop-if-exists", "DROP TABLE IF EXISTS t;" },
            { "create-index", "CREATE INDEX ix_a ON t (a);" },
            { "replay-quarantine", "REPLAY QUARANTINE q;" },
        };

        [Theory]
        [MemberData(nameof(Statements))]
        public void SerializingAStatement_KeepsEveryKeyword(string name, string sql)
        {
            // The whole script, not one statement: a section label ("load:") parses as its own
            // statement ahead of the one under test, and a quarantining statement needs one.
            var serialized = SerializeScript(sql);

            var missing = KeywordCounts(sql)
                .Where(entry => !NormalizedAway.Contains(entry.Key))
                .Where(entry => KeywordCounts(serialized).GetValueOrDefault(entry.Key) < entry.Value)
                .Select(entry => entry.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            Assert.True(missing.Count == 0,
                $"{name}: ToSql() dropped {string.Join(", ", missing)}.\n"
                + $"  in:  {sql}\n  out: {serialized}\n"
                + "A clause the serializer drops is silent behaviour loss: the script still parses "
                + "and does something different.");
        }

        [Theory]
        [MemberData(nameof(Statements))]
        public void SerializedSqlReparses(string name, string sql)
        {
            var serialized = SerializeScript(sql);

            var exception = Record.Exception(() => Parse(serialized));

            Assert.True(exception == null, $"{name}: ToSql() output did not reparse.\n  out: {serialized}\n{exception}");
        }

        /// <summary>
        /// Keyword occurrences, by uppercase text. Identifiers and literals are excluded — the
        /// lexer classifies keywords with their own token types, so anything left with an
        /// alphabetic value is one.
        /// </summary>
        private static Dictionary<string, int> KeywordCounts(string sql)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in new Lexer(sql).Tokenize())
            {
                if (token.Type is TokenType.IDENTIFIER or TokenType.STRING_LITERAL
                    or TokenType.NUMBER or TokenType.EOF) continue;
                if (string.IsNullOrEmpty(token.Value) || !char.IsLetter(token.Value[0])) continue;
                counts[token.Value] = counts.GetValueOrDefault(token.Value) + 1;
            }
            return counts;
        }

        /// <summary>
        /// Every statement serialized and rejoined, which is what a tool rewriting a script would
        /// produce. Statements whose ToSql is deliberately a summary rather than source -- blocks
        /// and TRY/CATCH render as "BEGIN ... END" -- are therefore not covered here; that is a
        /// real limit of ToSql worth knowing before using it to rewrite anything.
        /// </summary>
        private static string SerializeScript(string sql) =>
            string.Join(" ", Parse(sql).Statements.Select(statement => statement.ToSql()));

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
