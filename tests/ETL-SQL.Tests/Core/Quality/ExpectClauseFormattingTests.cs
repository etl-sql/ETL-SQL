using ETL_SQL.Core;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core.Quality
{
    /// <summary>
    /// The formatter must not lose a rule. Rules moved into the grammar precisely so that no tool
    /// could quietly remove enforcement, and the formatter is the tool most likely to try: it
    /// rewrites every script an author runs it over. A dropped or mangled <c>EXPECT</c> clause here
    /// would reintroduce, in the formatter, the exact silent failure the comment form had.
    /// </summary>
    public class ExpectClauseFormattingTests
    {
        [Theory]
        [InlineData("SELECT Id EXPECT NOT NULL ON FAILURE THROW INTO #c FROM #s;")]
        [InlineData("SELECT Id EXPECT NOT NULL ON FAILURE THROW EXPECT UNIQUE ON FAILURE QUARANTINE INTO #c FROM #s ON FAILURE QUARANTINE TO q;")]
        [InlineData("SELECT Email EXPECT MATCHES '^[^@]+@[^@]+$' ON FAILURE QUARANTINE INTO #c FROM #s ON FAILURE QUARANTINE TO q;")]
        [InlineData("SELECT Age EXPECT >= 0 AND <= 120 INTO #c FROM #s ON FAILURE WARN;")]
        [InlineData("SELECT Region EXPECT IN ('NA','EMEA') ON FAILURE QUARANTINE INTO #c FROM #s ON FAILURE QUARANTINE TO q;")]
        [InlineData("SELECT RegionId EXPECT EXISTS IN dim_region(Id) ON FAILURE QUARANTINE INTO #c FROM #s ON FAILURE QUARANTINE TO q;")]
        [InlineData("SELECT LoadedAt EXPECT BETWEEN '2020-01-01' AND '2030-01-01' INTO #c FROM #s ON FAILURE WARN;")]
        [InlineData("SELECT EventId EXPECT UNIQUE_FIRST BY LoadedAt ON FAILURE QUARANTINE INTO #c FROM #s ON FAILURE QUARANTINE TO q;")]
        [InlineData("SELECT Amount EXPECT CASTABLE AS DECIMAL(18,2) ON FAILURE QUARANTINE INTO #c FROM #s ON FAILURE QUARANTINE TO q;")]
        public void Formatter_PreservesRules_ThroughAReformat(string sql)
        {
            var formatted = SqlFormatter.Format(sql, new FormatterOptions());

            var before = Rules(sql);
            var after = Rules(formatted);

            Assert.Equal(before, after);
        }

        [Fact]
        public void Serializer_RoundTripsAColumnsRules()
        {
            const string sql =
                "SELECT Id EXPECT NOT NULL ON FAILURE THROW EXPECT UNIQUE ON FAILURE QUARANTINE "
                + "INTO #c FROM #s ON FAILURE QUARANTINE TO q;";

            var reSerialized = Parse(sql).ToSql();

            Assert.Contains("EXPECT NOT NULL ON FAILURE THROW", reSerialized);
            Assert.Contains("EXPECT UNIQUE ON FAILURE QUARANTINE", reSerialized);
            Assert.Equal(Rules(sql), Rules(reSerialized));
        }

        [Fact]
        public void Serializer_OmitsADefaultedAction_RatherThanInventingOne()
        {
            // Re-emitting an implied WARN would make a formatting pass rewrite scripts it had no
            // reason to touch.
            var reSerialized = Parse("SELECT Age EXPECT >= 0 INTO #c FROM #s ON FAILURE WARN;").ToSql();

            Assert.Contains("EXPECT >= 0", reSerialized);
            Assert.DoesNotContain("EXPECT >= 0 ON FAILURE", reSerialized);
        }

        private static Statement Parse(string sql) =>
            new ETL_SQL.Core.Parser.Parser(new Lexer(sql).Tokenize(), sql).ParseStatement();

        /// <summary>The rule text and action of every clause on every column, in order.</summary>
        private static string Rules(string sql)
        {
            var select = (SelectStatement)Parse(sql);
            var parts = new System.Collections.Generic.List<string>();
            foreach (var column in select.Columns)
                foreach (var clause in column.Expectations ?? [])
                    parts.Add($"{clause.Text}|{clause.Action}|{clause.ActionExplicit}");
            return string.Join(" ;; ", parts);
        }
    }
}
