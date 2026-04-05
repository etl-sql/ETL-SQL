using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Performance
{
    /// <summary>
    /// Verifies that ExternalSortEngine and ExternalJoinEngine activate at their
    /// spill-to-disk thresholds and produce correct results.
    ///
    /// Direct engine tests instantiate the engines with a live Evaluator context and
    /// pass pre-built row lists. This avoids the cost of driving 100k+ rows through
    /// the script parser and keeps test execution time reasonable.
    /// </summary>
    public class SpillToDiskTests
    {
        private readonly ITestOutputHelper _output;

        public SpillToDiskTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a flat list of rows with two columns: Id (int, sequential) and Val (int, i % 100).
        /// Deliberately unordered (reversed) so an ORDER BY Id ASC will do real work.
        /// </summary>
        private static List<Row> BuildRows(int count, bool reversed = false)
        {
            var schema = new TableSchema(new[] { "Id", "Val" });
            var rows   = new List<Row>(count);
            for (int i = 0; i < count; i++)
            {
                var row = new Row(schema);
                row["Id"]  = reversed ? (count - 1 - i) : i;
                row["Val"] = i % 100;
                rows.Add(row);
            }
            return rows;
        }

        private static List<OrderByClause> OrderById(bool descending = false) =>
            new() { new OrderByClause(new IdentifierExpression("Id"), descending) };

        private static JoinClause InnerJoinOnId() =>
            new JoinClause(
                "INNER",
                new TableReference("right"),
                new BinaryExpression(
                    new IdentifierExpression("Id"),
                    TokenType.EQUALS,
                    new IdentifierExpression("Id")
                )
            );

        // ─── ExternalSortEngine — unit tests ─────────────────────────────────────

        [Fact]
        public async Task ExternalSortEngine_SortsAscending_BelowChunkSize()
        {
            // 10k rows — single chunk, verifies correctness of the engine itself
            const int COUNT = 10_000;
            var e    = NewEvaluator();
            var rows = BuildRows(COUNT, reversed: true);

            var engine = new ExternalSortEngine(e);
            var result = await engine.SortExternal(rows, OrderById());

            Assert.Equal(COUNT, result.Count);
            for (int i = 1; i < result.Count; i++)
                Assert.True(Convert.ToInt32(result[i]["Id"]) >= Convert.ToInt32(result[i - 1]["Id"]));
        }

        [Fact]
        public async Task ExternalSortEngine_SortsAscending_MultipleChunks()
        {
            // 250k rows — forces multiple 100k chunks and k-way merge
            const int COUNT = 250_000;
            var e    = NewEvaluator();
            var rows = BuildRows(COUNT, reversed: true);

            long spillBefore = e.TotalSpilledBytes;
            var engine = new ExternalSortEngine(e);
            var result = await engine.SortExternal(rows, OrderById());

            Assert.Equal(COUNT, result.Count);
            Assert.Equal(0,         Convert.ToInt32(result[0]["Id"]));
            Assert.Equal(COUNT - 1, Convert.ToInt32(result[COUNT - 1]["Id"]));
            Assert.True(e.TotalSpilledBytes > spillBefore, "Expected spilled bytes to increase.");
            _output.WriteLine($"Spilled {e.TotalSpilledBytes - spillBefore:N0} bytes for {COUNT} rows.");
        }

        [Fact]
        public async Task ExternalSortEngine_SortsDescending_MultipleChunks()
        {
            const int COUNT = 150_000;
            var e    = NewEvaluator();
            var rows = BuildRows(COUNT, reversed: false); // ascending input → descending output

            var engine = new ExternalSortEngine(e);
            var result = await engine.SortExternal(rows, OrderById(descending: true));

            Assert.Equal(COUNT, result.Count);
            Assert.Equal(COUNT - 1, Convert.ToInt32(result[0]["Id"]));
            Assert.Equal(0,         Convert.ToInt32(result[COUNT - 1]["Id"]));
        }

        [Fact]
        public async Task ExternalSortEngine_PreservesAllRows_NoDuplicatesOrDrops()
        {
            const int COUNT = 120_000;
            var e    = NewEvaluator();
            var rows = BuildRows(COUNT, reversed: true);

            var engine = new ExternalSortEngine(e);
            var result = await engine.SortExternal(rows, OrderById());

            var ids = result.Select(r => Convert.ToInt32(r["Id"])).ToHashSet();
            Assert.Equal(COUNT, ids.Count); // no duplicates, no drops
        }

        // ─── ExternalJoinEngine — unit tests ─────────────────────────────────────

        [Fact]
        public async Task ExternalJoinEngine_InnerJoin_ProducesCorrectMatches()
        {
            // 5k rows on each side; all Ids match — should produce 5k joined rows
            const int SIDE_COUNT = 5_000;
            var e = NewEvaluator();

            var leftSchema  = new TableSchema(new[] { "Id", "LeftVal" });
            var rightSchema = new TableSchema(new[] { "Id", "RightVal" });

            async IAsyncEnumerable<Row> LeftStream()
            {
                for (int i = 0; i < SIDE_COUNT; i++)
                {
                    var r = new Row(leftSchema);
                    r["Id"]      = i;
                    r["LeftVal"] = i * 2;
                    yield return r;
                }
            }

            async IAsyncEnumerable<Row> RightStream()
            {
                for (int i = 0; i < SIDE_COUNT; i++)
                {
                    var r = new Row(rightSchema);
                    r["Id"]       = i;
                    r["RightVal"] = i * 3;
                    yield return r;
                }
            }

            var engine = new ExternalJoinEngine(e);
            var result = await engine.ApplyHashJoinExternal(
                LeftStream(), RightStream(), InnerJoinOnId(),
                new List<string> { "Id" }, new List<string> { "Id" });

            Assert.Equal(SIDE_COUNT, result.Count);
            _output.WriteLine($"ExternalJoinEngine produced {result.Count:N0} rows.");
        }

        [Fact]
        public async Task ExternalJoinEngine_InnerJoin_NoMatchesProducesEmpty()
        {
            const int SIDE_COUNT = 1_000;
            var e = NewEvaluator();

            var leftSchema  = new TableSchema(new[] { "Id", "LeftVal" });
            var rightSchema = new TableSchema(new[] { "Id", "RightVal" });

            async IAsyncEnumerable<Row> LeftStream()
            {
                for (int i = 0; i < SIDE_COUNT; i++)
                {
                    var r = new Row(leftSchema);
                    r["Id"]      = i;
                    r["LeftVal"] = i;
                    yield return r;
                }
            }

            async IAsyncEnumerable<Row> RightStream()
            {
                // Right side Ids are offset by SIDE_COUNT — no overlap
                for (int i = SIDE_COUNT; i < SIDE_COUNT * 2; i++)
                {
                    var r = new Row(rightSchema);
                    r["Id"]       = i;
                    r["RightVal"] = i;
                    yield return r;
                }
            }

            var engine = new ExternalJoinEngine(e);
            var result = await engine.ApplyHashJoinExternal(
                LeftStream(), RightStream(), InnerJoinOnId(),
                new List<string> { "Id" }, new List<string> { "Id" });

            Assert.Empty(result);
        }

        [Fact]
        public async Task ExternalJoinEngine_SpillsBytesToContext()
        {
            const int SIDE_COUNT = 10_000;
            var e = NewEvaluator();

            var leftSchema  = new TableSchema(new[] { "Id", "L" });
            var rightSchema = new TableSchema(new[] { "Id", "R" });

            async IAsyncEnumerable<Row> LeftStream()
            {
                for (int i = 0; i < SIDE_COUNT; i++)
                {
                    var r = new Row(leftSchema); r["Id"] = i; r["L"] = i; yield return r;
                }
            }
            async IAsyncEnumerable<Row> RightStream()
            {
                for (int i = 0; i < SIDE_COUNT; i++)
                {
                    var r = new Row(rightSchema); r["Id"] = i; r["R"] = i; yield return r;
                }
            }

            long before = e.TotalSpilledBytes;
            var engine  = new ExternalJoinEngine(e);
            await engine.ApplyHashJoinExternal(
                LeftStream(), RightStream(), InnerJoinOnId(),
                new List<string> { "Id" }, new List<string> { "Id" });

            Assert.True(e.TotalSpilledBytes > before,
                $"Expected TotalSpilledBytes to increase. Before: {before}, After: {e.TotalSpilledBytes}");
            _output.WriteLine($"ExternalJoinEngine spilled {e.TotalSpilledBytes - before:N0} bytes for {SIDE_COUNT}x{SIDE_COUNT} join.");
        }

        // ─── JoinEngine threshold integration ────────────────────────────────────

        [Fact]
        public async Task JoinEngine_ActivatesExternalPath_WhenRightSideExceedsThreshold()
        {
            // Left table has exactly 100k rows (fits in buffer); right has 110k.
            // JoinEngine.ApplyJoins checks joinRows.Count > 100k and delegates to ExternalJoinEngine.
            const int LEFT_SIDE  = 100_000;
            const int RIGHT_SIDE = 110_000;
            var e = NewEvaluator();

            var leftSchema  = new TableSchema(new[] { "Id", "LeftV" });
            var rightSchema = new TableSchema(new[] { "Id", "RightV" });

            var leftTable  = new DataTable();
            leftTable.SetColumns(new[] { "Id", "LeftV" });
            var rightTable = new DataTable();
            rightTable.SetColumns(new[] { "Id", "RightV" });

            for (int i = 0; i < RIGHT_SIDE; i++)
            {
                if (i < LEFT_SIDE)
                {
                    var lr = new Row(leftSchema); lr["Id"] = i; lr["LeftV"] = i * 2; leftTable.AddRow(lr);
                }
                var rr = new Row(rightSchema); rr["Id"] = i; rr["RightV"] = i * 3; rightTable.AddRow(rr);
            }

            var leftMem = new InMemoryDataSource();
            await leftMem.WriteBatches(new[] { leftTable }.ToAsyncEnumerable());
            e.Connections["#left"] = leftMem;

            var rightMem = new InMemoryDataSource();
            await rightMem.WriteBatches(new[] { rightTable }.ToAsyncEnumerable());
            e.Connections["#right"] = rightMem;

            long before = e.TotalSpilledBytes;

            // ORDER BY forces the multi-pass pipeline that checks the spill threshold
            var script = "SELECT L.Id, L.LeftV, R.RightV FROM #left AS L INNER JOIN #right AS R ON L.Id = R.Id ORDER BY L.Id;";
            await e.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

            Assert.NotNull(e.LastResult);
            Assert.Equal(LEFT_SIDE, e.LastResult.TotalRowsMatched);
            Assert.True(e.TotalSpilledBytes > before,
                $"Expected spill for {LEFT_SIDE}x{RIGHT_SIDE} join. Before={before}, After={e.TotalSpilledBytes}");
            _output.WriteLine($"JoinEngine spilled {e.TotalSpilledBytes - before:N0} bytes. Result rows: {e.LastResult.TotalRowsMatched:N0}");
        }
    }
}
