using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Functions
{
    public class FuzzyJoinTests
    {
        private readonly Evaluator _ev;
        public FuzzyJoinTests()
            => _ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private async Task<List<Row>> RunQuery(string sql)
        {
            var script = new Parser(new Lexer(sql).Tokenize()).Parse();
            var rows = new List<Row>();
            await foreach (var b in _ev.ExecuteQuery(script.Statements[0])) rows.AddRange(b.Rows);
            return rows;
        }

        // ── Parser ────────────────────────────────────────────────────────────────

        [Fact]
        public void Parser_FuzzyJoin_ParsesWithoutError()
        {
            const string sql = @"
                SELECT a.name, b.name, __score
                FROM #a a
                FUZZY JOIN #b b ON SIMILARITY(a.name, b.name) > 0.8
                KEEP BEST 1";
            var script = new Parser(new Lexer(sql).Tokenize()).Parse();
            var stmt = Assert.IsType<SelectStatement>(script.Statements[0]);
            Assert.Single(stmt.Joins);
            var join = stmt.Joins[0];
            Assert.Equal("FUZZY", join.JoinType);
            Assert.Equal(1, join.KeepBest);
        }

        [Fact]
        public void Parser_LeftFuzzyJoin_ParsesWithoutError()
        {
            const string sql = @"
                SELECT a.name, b.name, __score
                FROM #a a
                LEFT FUZZY JOIN #b b ON SIMILARITY(a.name, b.name) > 0.7";
            var script = new Parser(new Lexer(sql).Tokenize()).Parse();
            var stmt = Assert.IsType<SelectStatement>(script.Statements[0]);
            var join = stmt.Joins[0];
            Assert.Equal("LEFT FUZZY", join.JoinType);
            Assert.Null(join.KeepBest);
        }

        [Fact]
        public void Parser_FuzzyJoin_NoBestClause_KeepBestIsNull()
        {
            const string sql = "SELECT * FROM #a a FUZZY JOIN #b b ON SIMILARITY(a.x, b.x) > 0.8";
            var stmt = (SelectStatement)new Parser(new Lexer(sql).Tokenize()).Parse().Statements[0];
            Assert.Null(stmt.Joins[0].KeepBest);
        }

        // ── End-to-end execution ──────────────────────────────────────────────────

        private void LoadTable(string name, IEnumerable<(string id, string val)> data)
        {
            var ds = new MockMemoryTable();
            foreach (var (id, val) in data)
                ds.InsertRow(new Row { ["id"] = id, ["name"] = val });
            _ev.Connections[name] = ds;
        }

        [Fact]
        public async Task FuzzyJoin_ExactMatch_Scores1()
        {
            LoadTable("a1", new[] { ("1", "Alice") });
            LoadTable("b1", new[] { ("A", "Alice"), ("B", "Bob") });

            var rows = await RunQuery(@"
                SELECT a.name AS a_name, b.name AS b_name, __score
                FROM a1 a FUZZY JOIN b1 b ON SIMILARITY(a.name, b.name) > 0.8
                KEEP BEST 1");

            Assert.Single(rows);
            Assert.Equal("Alice", rows[0]["a.a_name"]?.ToString() ?? rows[0]["a_name"]?.ToString());
            var score = Convert.ToDecimal(rows[0]["__score"]);
            Assert.True(score >= 0.99m, $"Expected score ~1 but got {score}");
        }

        [Fact]
        public async Task FuzzyJoin_CloseMatch_Passes()
        {
            LoadTable("a2", new[] { ("1", "Microsft") });
            LoadTable("b2", new[] { ("A", "Microsoft"), ("B", "Apple") });

            var rows = await RunQuery(@"
                SELECT __score
                FROM a2 a FUZZY JOIN b2 b ON SIMILARITY(a.name, b.name) > 0.70
                KEEP BEST 1");

            Assert.Single(rows);
            var score = Convert.ToDecimal(rows[0]["__score"]);
            Assert.True(score > 0.70m, $"Expected score > 0.70 but got {score}");
        }

        [Fact]
        public async Task FuzzyJoin_NoMatch_ExcludesRow()
        {
            LoadTable("a3", new[] { ("1", "xyz_nomatch_12345") });
            LoadTable("b3", new[] { ("A", "Alice"), ("B", "Bob") });

            var rows = await RunQuery(@"
                SELECT __score
                FROM a3 a FUZZY JOIN b3 b ON SIMILARITY(a.name, b.name) > 0.90
                KEEP BEST 1");

            Assert.Empty(rows);
        }

        [Fact]
        public async Task LeftFuzzyJoin_NoMatch_IncludesRowWithNullScore()
        {
            LoadTable("a4", new[] { ("1", "zzznomatch") });
            LoadTable("b4", new[] { ("A", "Alice"), ("B", "Bob") });

            var rows = await RunQuery(@"
                SELECT __score
                FROM a4 a LEFT FUZZY JOIN b4 b ON SIMILARITY(a.name, b.name) > 0.90");

            Assert.Single(rows);
            Assert.Null(rows[0]["__score"]);
        }

        [Fact]
        public async Task FuzzyJoin_KeepBest1_ReturnsSingleBestMatch()
        {
            LoadTable("a5", new[] { ("1", "Robert") });
            LoadTable("b5", new[] { ("A", "Robert"), ("B", "Roberto"), ("C", "Roberta") });

            var rows = await RunQuery(@"
                SELECT __score FROM a5 a
                FUZZY JOIN b5 b ON SIMILARITY(a.name, b.name) > 0.70
                KEEP BEST 1");

            Assert.Single(rows);
        }

        [Fact]
        public async Task FuzzyJoin_KeepBest3_ReturnsUpToThree()
        {
            LoadTable("a6", new[] { ("1", "Robert") });
            LoadTable("b6", new[] { ("A", "Robert"), ("B", "Roberto"), ("C", "Roberta"), ("D", "xyz_nomatch") });

            var rows = await RunQuery(@"
                SELECT __score FROM a6 a
                FUZZY JOIN b6 b ON SIMILARITY(a.name, b.name) > 0.70
                KEEP BEST 3");

            Assert.True(rows.Count <= 3 && rows.Count > 0);
        }

        [Fact]
        public async Task FuzzyJoin_MultipleLeftRows_EachMatchedIndependently()
        {
            LoadTable("a7", new[] { ("1", "Alice"), ("2", "Bob"), ("3", "Charlie") });
            LoadTable("b7", new[] { ("A", "Alice"), ("B", "Bob"), ("C", "Carol") });

            var rows = await RunQuery(@"
                SELECT __score FROM a7 a
                FUZZY JOIN b7 b ON SIMILARITY(a.name, b.name) > 0.80
                KEEP BEST 1");

            Assert.Equal(2, rows.Count); // Alice→Alice, Bob→Bob match; Charlie→Carol does not at 0.80
        }

        [Fact]
        public async Task FuzzyJoin_ScoreOrderedDescending_WithKeepBest()
        {
            LoadTable("a8", new[] { ("1", "Smith") });
            LoadTable("b8", new[] { ("A", "Smithson"), ("B", "Smith"), ("C", "Smyth") });

            var rows = await RunQuery(@"
                SELECT __score FROM a8 a
                FUZZY JOIN b8 b ON SIMILARITY(a.name, b.name) > 0.60
                KEEP BEST 2
                ORDER BY __score DESC");

            Assert.Equal(2, rows.Count);
            var s0 = Convert.ToDecimal(rows[0]["__score"]);
            var s1 = Convert.ToDecimal(rows[1]["__score"]);
            Assert.True(s0 >= s1, $"Expected descending scores but got {s0}, {s1}");
        }

        [Fact]
        public async Task FuzzyJoin_WithNormalize_MatchesDespiteSuffixes()
        {
            LoadTable("a9", new[] { ("1", "Acme Corp.") });
            LoadTable("b9", new[] { ("A", "Acme Inc"), ("B", "Something Else LLC") });

            var rows = await RunQuery(@"
                SELECT __score FROM a9 a
                FUZZY JOIN b9 b
                    ON SIMILARITY(NORMALIZE(a.name, 'COMPANY'), NORMALIZE(b.name, 'COMPANY')) > 0.75
                KEEP BEST 1");

            Assert.Single(rows);
            var score = Convert.ToDecimal(rows[0]["__score"]);
            Assert.True(score > 0.75m, $"Expected score > 0.75 but got {score}");
        }

        // ── FuzzyJoinEngine unit tests ─────────────────────────────────────────────

        [Fact]
        public void ExtractScoreExpression_GreaterThan_ReturnsSimilarityExpr()
        {
            // SIMILARITY(a.name, b.name) > 0.8  →  should return the SIMILARITY call
            const string sql = "SELECT * FROM #a a FUZZY JOIN #b b ON SIMILARITY(a.name, b.name) > 0.8";
            var stmt = (SelectStatement)new Parser(new Lexer(sql).Tokenize()).Parse().Statements[0];
            var scoreExpr = FuzzyJoinEngine.ExtractScoreExpression(stmt.Joins[0].Condition);
            Assert.NotNull(scoreExpr);
            var fn = Assert.IsType<FunctionCallExpression>(scoreExpr);
            Assert.Equal("SIMILARITY", fn.FunctionName, ignoreCase: true);
        }

        [Fact]
        public void ExtractRightBlockingColumn_FindsRightSideColumn()
        {
            const string sql = "SELECT * FROM #a a FUZZY JOIN #b b ON SIMILARITY(a.name, b.name) > 0.8";
            var stmt = (SelectStatement)new Parser(new Lexer(sql).Tokenize()).Parse().Statements[0];
            var scoreExpr = FuzzyJoinEngine.ExtractScoreExpression(stmt.Joins[0].Condition)!;
            var col = FuzzyJoinEngine.ExtractRightBlockingColumn(scoreExpr, "b");
            Assert.NotNull(col);
        }
    }

    /// <summary>Simple in-memory table for test fixtures.</summary>
    internal class MockMemoryTable : IDataSource
    {
        private readonly List<Row> _rows = new();
        private readonly List<string> _columns = new();

        public string Path => "MOCK_MEM";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "MOCK_MEM";

        public void InsertRow(Row row)
        {
            _rows.Add(row);
            foreach (var k in row.Columns.Keys)
                if (!_columns.Contains(k)) _columns.Add(k);
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            var dt = new DataTable();
            dt.SetColumns(_columns);
            foreach (var r in _rows) await dt.AddRowAsync(r);
            yield return dt;
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(_columns);
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task TruncateAsync() { _rows.Clear(); return Task.CompletedTask; }
    }
}
