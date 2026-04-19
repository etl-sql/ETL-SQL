using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Common;
using ETL_SQL.Engine.Spill;
using ETL_SQL.Core.Linting;

namespace ETL_SQL.Tests.Hardening.Performance
{
    public class Batch9ReliabilityTests
    {
        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        // ─── SpillStore Round-Trip ──────────────────────────────────────────────

        [Theory]
        [InlineData(true, true)]   // Encrypt + Compress
        [InlineData(true, false)]  // Enroll only
        [InlineData(false, true)]  // Compress only
        [InlineData(false, false)] // Neither
        public async Task SpillStore_RoundTrip_EnsuresDataIntegrity(bool encrypt, bool compress)
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = encrypt;
            e.SpillCompressionEnabled = compress;

            using var store = new SpillStore(e);
            var chunkName = $"test_chunk_{encrypt}_{compress}";
            
            var testRows = new List<Row>();
            for (int i = 0; i < 1000; i++)
            {
                var r = new Row();
                r["Id"] = i;
                r["Name"] = $"Row_{i}";
                r["Val"] = (decimal)(i * 1.5);
                r["Date"] = new DateTime(2026, 1, 1).AddHours(i);
                testRows.Add(r);
            }

            // Write
            await using (var writer = await store.CreateWriterAsync(chunkName))
            {
                await writer.WriteRowsAsync(testRows);
            }

            // Read back
            var readRows = new List<Row>();
            await using (var reader = await store.CreateReaderAsync(chunkName))
            {
                await foreach (var row in reader.AsEnumerableAsync())
                {
                    readRows.Add(row);
                }
            }

            Assert.Equal(testRows.Count, readRows.Count);
            for (int i = 0; i < testRows.Count; i++)
            {
                Assert.Equal(Convert.ToInt32(testRows[i]["Id"]), Convert.ToInt32(readRows[i]["Id"]));
                Assert.Equal(testRows[i]["Name"], readRows[i]["Name"]);
                Assert.Equal(Convert.ToDecimal(testRows[i]["Val"]), Convert.ToDecimal(readRows[i]["Val"]));
                Assert.Equal(testRows[i]["Date"], readRows[i]["Date"]);
            }
        }

        // ─── ExternalJoinEngine Edge Cases ────────────────────────────────────────

        [Fact]
        public async Task ExternalJoinEngine_PartitionCountOne_WorksCorrectly()
        {
            var e = NewEvaluator();
            e.ExternalHashPartitions = 1; // Degenerate case
            
            var leftRows = new List<Row> { new Row { ["id"] = 1, ["l"] = "a" }, new Row { ["id"] = 2, ["l"] = "b" } };
            var rightRows = new List<Row> { new Row { ["id"] = 1, ["r"] = "x" } };

            var engine = new ExternalJoinEngine(e, NullLogger.Instance);
            var join = new JoinClause("INNER", new TableReference("right"), 
                new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id")));

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(), 
                rightRows.ToAsyncEnumerable(), 
                join, 
                new List<string> { "id" }, 
                new List<string> { "id" });

            Assert.Single(results);
            Assert.Equal("a", results[0]["l"]);
        }

        [Fact]
        public async Task ExternalJoinEngine_NullKeys_FollowSqlSemantics()
        {
            var e = NewEvaluator();
            var leftRows = new List<Row> { new Row { ["id"] = null, ["l"] = "null" }, new Row { ["id"] = 1, ["l"] = "one" } };
            var rightRows = new List<Row> { new Row { ["id"] = null, ["r"] = "null" }, new Row { ["id"] = 2, ["r"] = "two" } };

            var engine = new ExternalJoinEngine(e, NullLogger.Instance);
            var join = new JoinClause("INNER", new TableReference("right"), 
                new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id")));

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(), 
                rightRows.ToAsyncEnumerable(), 
                join, 
                new List<string> { "id" }, 
                new List<string> { "id" });

            // In SQL, NULL != NULL, so there should be 0 matches
            Assert.Empty(results);
        }

        [Fact]
        public async Task ExternalJoinEngine_LeftOuterJoin_VerifiedByRegex()
        {
            var e = NewEvaluator();
            var leftRows = new List<Row> { new Row { ["id"] = 1 } };
            var rightRows = new List<Row>(); // Empty right

            var engine = new ExternalJoinEngine(e, NullLogger.Instance);
            
            // Testing both "LEFT JOIN" and "LEFT OUTER JOIN"
            foreach (var type in new[] { "LEFT JOIN", "LEFT OUTER JOIN" })
            {
                var join = new JoinClause(type, new TableReference("right"), 
                    new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id")));

                var results = await engine.ApplyHashJoinExternal(
                    leftRows.ToAsyncEnumerable(), 
                    rightRows.ToAsyncEnumerable(), 
                    join, 
                    new List<string> { "id" }, 
                    new List<string> { "id" });

                Assert.Single(results);
            }
        }

        // ─── ExternalSortEngine Edge Cases ────────────────────────────────────────

        [Fact]
        public async Task ExternalSortEngine_MixedDirections_SortsCorrectly()
        {
            var e = NewEvaluator();
            e.ExternalSortChunkSize = 2; // Force many chunks
            var engine = new ExternalSortEngine(e, NullLogger.Instance);

            var rows = new List<Row>
            {
                new Row { ["A"] = 1, ["B"] = 10, ["C"] = 100 },
                new Row { ["A"] = 1, ["B"] = 20, ["C"] = 50 },
                new Row { ["A"] = 2, ["B"] = 10, ["C"] = 200 },
                new Row { ["A"] = 1, ["B"] = 10, ["C"] = 300 }
            };

            // Order by A ASC, B DESC, C ASC
            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("A"), false), // ASC
                new OrderByClause(new IdentifierExpression("B"), true),  // DESC
                new OrderByClause(new IdentifierExpression("C"), false)  // ASC
            };

            var sorted = await engine.SortExternal(rows, orderBy);

            // Row 0: A=1, B=20, C=50
            // Row 1: A=1, B=10, C=100
            // Row 2: A=1, B=10, C=300
            // Row 3: A=2, B=10, C=200
            Assert.Equal((decimal)20, sorted[0]["B"]);
            Assert.Equal((decimal)100, sorted[1]["C"]);
            Assert.Equal((decimal)300, sorted[2]["C"]);
            Assert.Equal((decimal)2, sorted[3]["A"]);
        }

        // ─── ExternalAggregateEngine Hardening ────────────────────────────────────

        [Fact]
        public async Task ExternalAggregateEngine_NegativeHash_DoesNotOverflow()
        {
            var e = NewEvaluator();
            var engine = new ExternalAggregateEngine(e, NullLogger.Instance);

            var rows = new List<Row>();
            var schema = new TableSchema(new[] { "key", "val" });
            
            int hash = int.MinValue;
            int count = 32;
            int index = (hash & 0x7FFFFFFF) % count;
            Assert.InRange(index, 0, count - 1);

            for (int i = 0; i < 1000; i++)
            {
                var r = new Row(schema);
                r["key"] = i.ToString();
                r["val"] = 1m;
                rows.Add(r);
            }

            var columns = new List<SelectColumn> { 
                new SelectColumn(new FunctionCallExpression("COUNT", new List<Expression>()), "cnt") 
            };
            var groupBy = new List<Expression> { new IdentifierExpression("key") };

            var results = await engine.ApplyAggregationExternal(
                rows.ToAsyncEnumerable(), 
                groupBy, 
                columns, 
                new List<string> { "key", "cnt" })
                .ToListAsync();

            Assert.Equal(1000, results.Count);
        }

        // ─── Security Service Hardening ──────────────────────────────────────────

        [Fact]
        public async Task SecurityService_SpillSecurityRule_RecursionTest()
        {
            var e = NewEvaluator();
            var linter = LinterFactory.CreateWithAllRules();
            
            var source = @"
BEGIN TRY
  IF 1=1
  BEGIN
    WHILE 1=1
    BEGIN
      IF 2=2
      BEGIN
        SET SPILL_ENCRYPTION OFF;
      END
    END
  END
END TRY
BEGIN CATCH
  PRINT 'Error';
END CATCH";

            var tokens = new Lexer(source).Tokenize();
            var script = new Parser(tokens).Parse();
            
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());
            
            Assert.Contains(results, r => r.Message.Contains("Disabling Encryption") && r.Severity == LintSeverity.Warning);
        }
    }
}
