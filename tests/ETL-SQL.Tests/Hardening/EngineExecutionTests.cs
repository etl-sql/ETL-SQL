using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class EngineExecutionTests
    {
        private static Evaluator NewEvaluator(int externalPartitions = 8)
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var e = services.GetRequiredService<Evaluator>();
            e.ExternalHashPartitions = externalPartitions; // Override for testing
            return e;
        }

        private static JoinClause CreateJoin(string type) =>
            new JoinClause(
                type,
                new TableReference("right"),
                new BinaryExpression(
                    new IdentifierExpression("Id"),
                    TokenType.EQUALS,
                    new IdentifierExpression("Id")
                )
            );

        private async IAsyncEnumerable<Row> Stream(TableSchema schema, int start, int count, bool addNulls = false, string valCol = "Val")
        {
            for (int i = start; i < start + count; i++)
            {
                var r = new Row(schema);
                r["Id"] = (addNulls && i % 5 == 0) ? null : i; // 20% nulls if addNulls
                r[valCol] = i * 2;
                yield return r;
            }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task JoinEngine_StreamingUnqualifiedEquality_UsesHashJoin()
        {
            var e = NewEvaluator();
            var logger = new CapturingLogger();
            var engine = new JoinEngine(e, logger);

            var right = new InMemoryDataSource { Validator = e, ExecutionContext = e };
            var rightTable = new DataTable();
            rightTable.SetColumns(new[] { "right_id", "payload" });
            for (var i = 0; i < 64; i++)
            {
                var row = rightTable.NewRow();
                row["right_id"] = i;
                row["payload"] = $"r-{i}";
                await rightTable.AddRowAsync(row);
            }
            await right.WriteBatches(new[] { rightTable }.ToAsyncEnumerable());
            e.Connections["#right"] = right;

            var leftSchema = new TableSchema(new[] { "left_id", "value" });
            async IAsyncEnumerable<Row> LeftRows()
            {
                for (var i = 0; i < 64; i++)
                {
                    var row = new Row(leftSchema);
                    row["left_id"] = i;
                    row["value"] = $"l-{i}";
                    yield return row;
                }
                await Task.CompletedTask;
            }

            var join = new JoinClause(
                "INNER JOIN",
                new TableReference("#right"),
                new BinaryExpression(
                    new IdentifierExpression("left_id"),
                    TokenType.EQUALS,
                    new IdentifierExpression("right_id")));

            var stmt = new SelectStatement(
                new List<SelectColumn> { new(new IdentifierExpression("left_id")) },
                null,
                new TableReference("#left"),
                new List<JoinClause> { join },
                null);

            var result = await engine.ApplyJoinsStreaming(LeftRows(), stmt.Joins, stmt).ToListAsync();

            Assert.Equal(64, result.Count);
            Assert.Contains(logger.Messages, m =>
                m.Level == LogLevel.Debug
                && m.Message.Contains("Hash Join", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ExternalJoinEngine_SinglePartition_Correctness()
        {
            var e = NewEvaluator(externalPartitions: 1); // Exact override for degenerate case
            var engine = new ExternalJoinEngine(e, NullLogger.Instance);

            var schema = new TableSchema(new[] { "Id", "Val" });

            // Should properly process within 1 partition directly
            var result = await engine.ApplyHashJoinExternal(
                Stream(schema, 0, 100),
                Stream(schema, 50, 100), // overlap 50..99
                CreateJoin("INNER"),
                new List<string> { "Id" },
                new List<string> { "Id" }).ToListAsync();

            Assert.Equal(50, result.Count);
        }

        [Fact]
        public async Task ExternalJoinEngine_LeftJoin_ProducesMatchesAndUnmatched()
        {
            var e = NewEvaluator();
            var engine = new ExternalJoinEngine(e, NullLogger.Instance);

            var schemaLeft = new TableSchema(new[] { "Id", "ValLeft" });
            var schemaRight = new TableSchema(new[] { "Id", "ValRight" });

            var result = await engine.ApplyHashJoinExternal(
                Stream(schemaLeft, 0, 100, false, "ValLeft"), // Left has 0..99
                Stream(schemaRight, 50, 100, false, "ValRight"), // Right has 50..149
                CreateJoin("LEFT"),
                new List<string> { "Id" },
                new List<string> { "Id" }).ToListAsync();

            // 50 matched + 50 unmatched from left = 100 total
            Assert.Equal(100, result.Count);

            // Only 50 rows have an actual right match, so exactly 50 rows will have "ValRight" populated
            var nullRightVals = result.Count(r => r["ValRight"] == null);
            Assert.Equal(50, nullRightVals);
        }

        [Fact]
        public async Task ExternalSortEngine_MixedDirection_Order()
        {
            var e = NewEvaluator();
            var engine = new ExternalSortEngine(e, NullLogger.Instance);

            var schema = new TableSchema(new[] { "Group", "Id" });
            var rows = new List<Row>();
            for (int i = 0; i < 100; i++)
            {
                var r = new Row(schema);
                r["Group"] = i % 5;
                r["Id"] = i;
                rows.Add(r);
            }

            var orderList = new List<OrderByClause> {
                new OrderByClause(new IdentifierExpression("Group"), descending: false),
                new OrderByClause(new IdentifierExpression("Id"), descending: true)
            };

            var result = await engine.SortExternal(rows, orderList);

            Assert.Equal(100, result.Count);

            // Verify
            for (int i = 1; i < result.Count; i++)
            {
                int prevGroup = Convert.ToInt32(result[i - 1]["Group"]);
                int currGroup = Convert.ToInt32(result[i]["Group"]);

                Assert.True(currGroup >= prevGroup);

                if (currGroup == prevGroup)
                {
                    int prevId = Convert.ToInt32(result[i - 1]["Id"]);
                    int currId = Convert.ToInt32(result[i]["Id"]);
                    Assert.True(currId <= prevId); // Descending for ID within same Group
                }
            }
        }

        [Fact]
        public async Task ExternalJoinEngine_NullKeyParity()
        {
            var e = NewEvaluator();
            var engine = new ExternalJoinEngine(e, NullLogger.Instance);
            var schema = new TableSchema(new[] { "Id", "Val" });

            // In SQL, NULL = NULL is false. Hash joins must NOT match rows where the key is NULL.
            var result = await engine.ApplyHashJoinExternal(
                Stream(schema, 0, 100, addNulls: true),
                Stream(schema, 0, 100, addNulls: true),
                CreateJoin("INNER"),
                new List<string> { "Id" },
                new List<string> { "Id" }).ToListAsync();

            // 100 total rows... 20 are null. So 80 matching.
            Assert.Equal(80, result.Count);
        }

        [Fact]
        public async Task ExternalJoinEngine_RecursivelyPartitionsOversizedPartitions()
        {
            var e = NewEvaluator(externalPartitions: 4);
            e.JoinSpillThreshold = 2;
            var engine = new ExternalJoinEngine(e, NullLogger.Instance);
            var schema = new TableSchema(new[] { "Id", "Val" });

            var result = await engine.ApplyHashJoinExternal(
                Stream(schema, 0, 32),
                Stream(schema, 0, 32),
                CreateJoin("INNER"),
                new List<string> { "Id" },
                new List<string> { "Id" }).ToListAsync();

            Assert.Equal(32, result.Count);
            Assert.True(
                e.Telemetry.PartitionsCount > e.ExternalHashPartitions * 2,
                "Expected recursive partitioning to create additional spill partitions beyond the initial left/right pass.");
        }

        [Fact]
        public async Task ExternalJoinEngine_SkewedPartitionFallsBackWhenItCannotSplit()
        {
            var e = NewEvaluator(externalPartitions: 4);
            e.JoinSpillThreshold = 1;
            var engine = new ExternalJoinEngine(e, NullLogger.Instance);

            static async IAsyncEnumerable<Row> DuplicateIdRows(int count, string valueColumn)
            {
                for (int i = 0; i < count; i++)
                {
                    yield return new Row { ["Id"] = 1, [valueColumn] = i };
                }

                await Task.CompletedTask;
            }

            var result = await engine.ApplyHashJoinExternal(
                DuplicateIdRows(3, "LeftVal"),
                DuplicateIdRows(5, "RightVal"),
                CreateJoin("INNER"),
                new List<string> { "Id" },
                new List<string> { "Id" }).ToListAsync();

            Assert.Equal(15, result.Count);
        }

        [Fact]
        public async Task ExternalAggregateEngine_PartitionIndexOverflow()
        {
            var e = NewEvaluator();
            var engine = new ExternalAggregateEngine(e, NullLogger.Instance);
            var schema = new TableSchema(new[] { "Group", "Val" });

            // Produce rows where Group = int.MinValue to try to cause partition hash index to go negative
            var rows = new List<Row>();
            for (int i = 0; i < 100; i++)
            {
                var r = new Row(schema);
                r["Group"] = int.MinValue;
                r["Val"] = 1;
                rows.Add(r);
            }

            // Aggregate: SUM(Val) GROUP BY Group
            var groupBy = new List<Expression> { new IdentifierExpression("Group") };
            var cols = new List<SelectColumn> {
                new SelectColumn(new IdentifierExpression("Group"), "Group"),
                new SelectColumn(new FunctionCallExpression("SUM", new List<Expression> { new IdentifierExpression("Val") }), "Total")
            };

            var stream = rows.ToAsyncEnumerable();
            var result = await engine.ApplyAggregationExternal(stream, groupBy, cols, new List<string> { "Group", "Total" }).ToListAsync();

            Assert.Single(result);
            Assert.Equal(100L, Convert.ToInt64(result[0]["Total"]));
        }

        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Messages { get; } = new();
            public string? SessionId { get; set; }
            public bool IsDebugEnabled => true;
            public bool IsVerboseEnabled => false;
            public bool IsVerbose { get; set; }
            public bool SuppressConsole { get; set; } = true;
            public bool IsJsonMode { get; set; }
            public event Action<string, string?, ConsoleColor>? OnMessage;

            public void Log(LogLevel level, string message, Exception? ex = null)
            {
                Messages.Add((level, message));
                OnMessage?.Invoke(message, null, ConsoleColor.White);
            }
        }

        [Fact]
        public void AggregateEngine_Cube_LargeColumnCount()
        {
            var e = NewEvaluator();
            var engine = new AggregateEngine(e, NullLogger.Instance);

            // Group by 11 columns in CUBE => 2^11 = 2048 grouping sets (exceeds 1024 limit)
            var exprs = new List<Expression>();
            for (int i = 0; i < 11; i++) exprs.Add(new IdentifierExpression($"Col{i}"));

            var clause = new GroupingSetClause(GroupingSetType.Cube, new List<List<Expression>> { exprs });

            var ex = Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => engine.ExpandGroupingSets(clause));
            Assert.Contains("exceeds the maximum grouping sets limit", ex.Message);
        }

        [Fact]
        public async Task AggregateEngine_BigIntegerIsTreatedAsIntegerType()
        {
            var e = NewEvaluator();
            var engine = new AggregateEngine(e, NullLogger.Instance);

            // Average of BigInteger 10 and BigInteger 11 should be truncated to 10
            // because they are integer types. If they were treated as decimals/floats,
            // the average would be 10.5.
            var schema = new TableSchema(new[] { "Val" });
            var rows = new List<Row>();

            var r1 = new Row(schema);
            r1["Val"] = new System.Numerics.BigInteger(10);
            rows.Add(r1);

            var r2 = new Row(schema);
            r2["Val"] = new System.Numerics.BigInteger(11);
            rows.Add(r2);

            var groupBy = new List<Expression>();
            var cols = new List<SelectColumn> {
                new SelectColumn(new FunctionCallExpression("AVG", new List<Expression> { new IdentifierExpression("Val") }), "AvgVal")
            };

            var stream = rows.ToAsyncEnumerable();
            var result = await engine.ApplyAggregation(stream, groupBy, cols, new List<string> { "AvgVal" });

            Assert.Single(result);
            Assert.Equal(new decimal(10), result[0]["AvgVal"]);
        }
    }
}
