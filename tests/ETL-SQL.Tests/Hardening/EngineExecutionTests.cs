using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Planning;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Engine.Planning;
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
        public async Task SelectExecutionEngine_SimpleJoinWithLimit_DoesNotMaterializeAllJoinedRows()
        {
            var e = NewEvaluator();
            e.BatchSize = 2;

            var right = new InMemoryDataSource { Validator = e, ExecutionContext = e };
            var rightTable = new DataTable();
            rightTable.SetColumns(new[] { "Id", "payload" });
            for (var i = 0; i < 10; i++)
            {
                var row = rightTable.NewRow();
                row["Id"] = i;
                row["payload"] = $"r-{i}";
                await rightTable.AddRowAsync(row);
            }
            await right.WriteBatches(new[] { rightTable }.ToAsyncEnumerable());
            e.Connections["#right"] = right;

            async IAsyncEnumerable<DataTable> LeftBatches()
            {
                for (var i = 0; i < 10; i++)
                {
                    if (i > 3)
                        throw new InvalidOperationException("Join pipeline materialized past the LIMIT.");

                    var batch = new DataTable();
                    batch.SetColumns(new[] { "Id", "value" });
                    var row = batch.NewRow();
                    row["Id"] = i;
                    row["value"] = $"l-{i}";
                    await batch.AddRowAsync(row);
                    yield return batch;
                }
            }

            var stmt = new SelectStatement(
                new List<SelectColumn> { new(new IdentifierExpression("l.Id")) },
                null,
                new TableReference("#left", alias: "l"),
                new List<JoinClause>
                {
                    new(
                        "INNER JOIN",
                        new TableReference("#right", alias: "r"),
                        new BinaryExpression(
                            new IdentifierExpression("l.Id"),
                            TokenType.EQUALS,
                            new IdentifierExpression("r.Id")))
                },
                null)
            {
                LimitCount = new LiteralExpression(3, TokenType.NUMBER)
            };

            var engine = new SelectExecutionEngine(e, NullLogger.Instance);
            var batches = await engine.ExecuteHeavyPipeline(
                stmt,
                LeftBatches(),
                stmt.Columns,
                new List<string> { "Id" }).ToListAsync();

            var rows = batches.SelectMany(b => b.Rows).ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { 0, 1, 2 }, rows.Select(r => Convert.ToInt32(r["Id"])).ToArray());
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
            var decision = Assert.Single(e.Telemetry.PlanDecisions,
                d => d.CandidatePath == "ExternalJoin" && d.Outcome == PlanDecisionOutcome.Accepted);
            Assert.Equal("external-join", decision.OperatorId);
            Assert.Equal(PlanDecisionReasonCodes.SemanticGuard, decision.ReasonCode);
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
            var decision = Assert.Single(e.Telemetry.PlanDecisions,
                d => d.CandidatePath == "ExternalSort" && d.Outcome == PlanDecisionOutcome.Accepted);
            Assert.Equal("external-sort", decision.OperatorId);

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
            e.JoinSpillThreshold = 10_000_000;
            e.MemoryGovernorPolicy = MemoryGovernorPolicy.SpillOnly;
            var saved = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 256;
            try
            {
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
                    "Expected byte-governed recursive partitioning beyond the initial left/right pass.");
                Assert.Contains(e.Telemetry.PlanDecisions,
                    d => d.CandidatePath == "ExternalJoin"
                        && d.Outcome == PlanDecisionOutcome.Degraded
                        && d.ReasonCode == PlanDecisionReasonCodes.MemoryAdmissionRejected);
            }
            finally
            {
                MemoryGrantArbiter.Shared.TotalBudgetBytes = saved;
            }
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
        public async Task ExternalWindowEngine_FullPartitionAggregates_UseStreamingSpillReplay()
        {
            var e = NewEvaluator(externalPartitions: 1);
            e.WindowSpillThreshold = 3;
            var logger = new CapturingLogger();
            var aggregateEngine = new AggregateEngine(e, logger);
            var windowEngine = new WindowEngine(e, aggregateEngine, logger);
            var externalWindowEngine = new ExternalWindowEngine(e, windowEngine, logger);

            var window = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause>());
            var sum = new FunctionCallExpression("SUM", new List<Expression> { new IdentifierExpression("Val") }) { Window = window };
            var count = new FunctionCallExpression("COUNT", new List<Expression> { new IdentifierExpression("*") }) { Window = window };
            var first = new FunctionCallExpression("FIRST_VALUE", new List<Expression> { new IdentifierExpression("Val") }) { Window = window };
            var last = new FunctionCallExpression("LAST_VALUE", new List<Expression> { new IdentifierExpression("Val") }) { Window = window };
            var stmt = new SelectStatement(
                new List<SelectColumn>
                {
                    new(new IdentifierExpression("Grp"), "Grp"),
                    new(sum, "Total"),
                    new(count, "Rows"),
                    new(first, "FirstVal"),
                    new(last, "LastVal")
                },
                null,
                new TableReference("#input"),
                new List<JoinClause>(),
                null);

            async IAsyncEnumerable<Row> Rows()
            {
                var schema = new TableSchema(new[] { "Grp", "Val" });
                for (var i = 1; i <= 10; i++)
                {
                    var row = new Row(schema);
                    row["Grp"] = "A";
                    row["Val"] = i;
                    yield return row;
                }
                await Task.CompletedTask;
            }

            var result = await externalWindowEngine.ApplyWindowFunctionsExternal(Rows(), stmt).ToListAsync();

            Assert.Equal(10, result.Count);
            Assert.Equal(10, externalWindowEngine.ColumnarWindowScanRows);
            Assert.Contains(logger.Messages, m => m.Message.Contains("PARTITION-REPLAY-SPILL", StringComparison.OrdinalIgnoreCase));
            var decision = Assert.Single(e.Telemetry.PlanDecisions,
                d => d.CandidatePath == "ExternalWindow" && d.Outcome == PlanDecisionOutcome.Accepted);
            Assert.Equal("external-window", decision.OperatorId);

            var sumKey = $"WINDOW_{sum.ToSql().ToUpperInvariant()}";
            var countKey = $"WINDOW_{count.ToSql().ToUpperInvariant()}";
            var firstKey = $"WINDOW_{first.ToSql().ToUpperInvariant()}";
            var lastKey = $"WINDOW_{last.ToSql().ToUpperInvariant()}";
            Assert.All(result, row =>
            {
                Assert.Equal(55m, row[sumKey]);
                Assert.Equal(10m, row[countKey]);
                Assert.Equal(1m, Convert.ToDecimal(row[firstKey]));
                Assert.Equal(10m, Convert.ToDecimal(row[lastKey]));
            });
        }

        [Fact]
        public async Task ExternalWindowEngine_PartitionSampleIncreasesFanOutWithoutLosingRows()
        {
            var e = NewEvaluator(externalPartitions: 2);
            e.OperatorMemoryGrantMB = 1;
            var logger = new CapturingLogger();
            var aggregateEngine = new AggregateEngine(e, logger);
            var windowEngine = new WindowEngine(e, aggregateEngine, logger);
            var externalWindowEngine = new ExternalWindowEngine(e, windowEngine, logger);
            var window = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause>());
            var count = new FunctionCallExpression("COUNT", new List<Expression>
            {
                new IdentifierExpression("*")
            })
            { Window = window };
            var stmt = new SelectStatement(
                new List<SelectColumn>
                {
                    new(new IdentifierExpression("Grp"), "Grp"),
                    new(count, "Rows")
                },
                null,
                new TableReference("#input"),
                new List<JoinClause>(),
                null);
            var savedBudget = MemoryGrantArbiter.Shared.TotalBudgetBytes;
            MemoryGrantArbiter.Shared.TotalBudgetBytes = 0;
            try
            {
                var payload = new string('x', 1024);
                var rows = Enumerable.Range(0, 4096)
                    .Select(id => new Row
                    {
                        ["Grp"] = "g" + id,
                        ["Val"] = payload + id
                    })
                    .ToAsyncEnumerable();

                var result = await externalWindowEngine
                    .ApplyWindowFunctionsExternal(rows, stmt).ToListAsync();

                Assert.Equal(4096, result.Count);
                Assert.True(externalWindowEngine.PartitionCount > 2);
                var countKey = $"WINDOW_{count.ToSql().ToUpperInvariant()}";
                Assert.All(result, row => Assert.Equal(1m, row[countKey]));
            }
            finally
            {
                MemoryGrantArbiter.Shared.TotalBudgetBytes = savedBudget;
            }
        }

        [Fact]
        public async Task ExternalWindowEngine_ExactInputEstimateReducesOversizedBaseline()
        {
            var e = NewEvaluator(externalPartitions: 64);
            e.OperatorMemoryGrantMB = 1;
            var logger = new CapturingLogger();
            var aggregateEngine = new AggregateEngine(e, logger);
            var windowEngine = new WindowEngine(e, aggregateEngine, logger);
            var externalWindowEngine = new ExternalWindowEngine(e, windowEngine, logger);
            var window = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause>());
            var count = new FunctionCallExpression("COUNT", new List<Expression>
            {
                new IdentifierExpression("*")
            })
            { Window = window };
            var stmt = new SelectStatement(
                new List<SelectColumn>
                {
                    new(new IdentifierExpression("Grp"), "Grp"),
                    new(count, "Rows")
                },
                null,
                new TableReference("#input"),
                new List<JoinClause>(),
                null);
            var rows = Enumerable.Range(0, 16)
                .Select(id => new Row { ["Grp"] = "g" + id, ["Val"] = id })
                .ToList();

            var result = await externalWindowEngine.ApplyWindowFunctionsExternal(
                rows.ToAsyncEnumerable(), stmt,
                knownRowCount: rows.Count,
                knownInputBytes: RowWidthEstimator.EstimateTotalBytes(rows)).ToListAsync();

            Assert.Equal(16, result.Count);
            Assert.True(externalWindowEngine.PartitionCount < 64);
        }

        [Fact]
        public async Task ExternalWindowEngine_RunningAggregates_UseDeepSpillStreaming()
        {
            var e = NewEvaluator(externalPartitions: 1);
            e.WindowSpillThreshold = 3;
            var logger = new CapturingLogger();
            var aggregateEngine = new AggregateEngine(e, logger);
            var windowEngine = new WindowEngine(e, aggregateEngine, logger);
            var externalWindowEngine = new ExternalWindowEngine(e, windowEngine, logger);

            var frame = new WindowFrame(
                WindowFrameType.ROWS,
                WindowFrameBoundType.UNBOUNDED_PRECEDING,
                endBound: WindowFrameBoundType.CURRENT_ROW);
            var window = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause> { new(new IdentifierExpression("Val"), false) },
                frame);
            FunctionCallExpression WindowCall(string name, params Expression[] args)
                => new(name, args.ToList()) { Window = window };

            var sum = WindowCall("SUM", new IdentifierExpression("Val"));
            var count = WindowCall("COUNT", new IdentifierExpression("*"));
            var avg = WindowCall("AVG", new IdentifierExpression("Val"));
            var min = WindowCall("MIN", new IdentifierExpression("Val"));
            var max = WindowCall("MAX", new IdentifierExpression("Val"));
            var nth = WindowCall(
                "NTH_VALUE",
                new IdentifierExpression("Val"),
                new LiteralExpression(3, TokenType.NUMBER));
            var slidingFrame = new WindowFrame(
                WindowFrameType.ROWS,
                WindowFrameBoundType.PRECEDING,
                new LiteralExpression(2, TokenType.NUMBER),
                WindowFrameBoundType.CURRENT_ROW);
            var slidingWindow = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause> { new(new IdentifierExpression("Val"), false) },
                slidingFrame);
            FunctionCallExpression SlidingCall(string name, Expression argument)
                => new(name, new List<Expression> { argument }) { Window = slidingWindow };
            var slidingSum = SlidingCall("SUM", new IdentifierExpression("Val"));
            var slidingAvg = SlidingCall("AVG", new IdentifierExpression("Val"));
            var slidingCount = SlidingCall("COUNT", new IdentifierExpression("*"));
            var slidingMin = SlidingCall("MIN", new IdentifierExpression("Val"));
            var slidingMax = SlidingCall("MAX", new IdentifierExpression("Val"));
            var stmt = new SelectStatement(
                new List<SelectColumn>
                {
                    new(new IdentifierExpression("Grp"), "Grp"),
                    new(new IdentifierExpression("Val"), "Val"),
                    new(sum, "RunningSum"),
                    new(count, "RunningCount"),
                    new(avg, "RunningAvg"),
                    new(min, "RunningMin"),
                    new(max, "RunningMax"),
                    new(nth, "ThirdVal"),
                    new(slidingSum, "RollingSum"),
                    new(slidingAvg, "RollingAvg"),
                    new(slidingCount, "RollingCount"),
                    new(slidingMin, "RollingMin"),
                    new(slidingMax, "RollingMax")
                },
                null,
                new TableReference("#input"),
                new List<JoinClause>(),
                null);

            async IAsyncEnumerable<Row> Rows()
            {
                var schema = new TableSchema(new[] { "Grp", "Val" });
                for (var i = 1; i <= 10; i++)
                {
                    var row = new Row(schema);
                    row["Grp"] = "A";
                    row["Val"] = i;
                    yield return row;
                }
                await Task.CompletedTask;
            }

            var result = await externalWindowEngine.ApplyWindowFunctionsExternal(Rows(), stmt).ToListAsync();

            Assert.Equal(10, result.Count);
            Assert.Contains(logger.Messages, m => m.Message.Contains("DEEP-SPILL", StringComparison.OrdinalIgnoreCase));

            var sumKey = $"WINDOW_{sum.ToSql().ToUpperInvariant()}";
            var countKey = $"WINDOW_{count.ToSql().ToUpperInvariant()}";
            var avgKey = $"WINDOW_{avg.ToSql().ToUpperInvariant()}";
            var minKey = $"WINDOW_{min.ToSql().ToUpperInvariant()}";
            var maxKey = $"WINDOW_{max.ToSql().ToUpperInvariant()}";
            var nthKey = $"WINDOW_{nth.ToSql().ToUpperInvariant()}";
            var slidingSumKey = $"WINDOW_{slidingSum.ToSql().ToUpperInvariant()}";
            var slidingAvgKey = $"WINDOW_{slidingAvg.ToSql().ToUpperInvariant()}";
            var slidingCountKey = $"WINDOW_{slidingCount.ToSql().ToUpperInvariant()}";
            var slidingMinKey = $"WINDOW_{slidingMin.ToSql().ToUpperInvariant()}";
            var slidingMaxKey = $"WINDOW_{slidingMax.ToSql().ToUpperInvariant()}";
            for (var i = 0; i < result.Count; i++)
            {
                var n = i + 1;
                Assert.Equal((decimal)(n * (n + 1) / 2), result[i][sumKey]);
                Assert.Equal((decimal)n, result[i][countKey]);
                Assert.Equal((n + 1) / 2m, result[i][avgKey]);
                Assert.Equal(1m, Convert.ToDecimal(result[i][minKey]));
                Assert.Equal((decimal)n, Convert.ToDecimal(result[i][maxKey]));
                if (n < 3)
                    Assert.Null(result[i][nthKey]);
                else
                    Assert.Equal(3m, Convert.ToDecimal(result[i][nthKey]));
                var frameStart = Math.Max(1, n - 2);
                var frameCount = n - frameStart + 1;
                var frameSum = (frameStart + n) * frameCount / 2m;
                Assert.Equal(frameSum, Convert.ToDecimal(result[i][slidingSumKey]));
                Assert.Equal(frameSum / frameCount, Convert.ToDecimal(result[i][slidingAvgKey]));
                Assert.Equal((decimal)frameCount, Convert.ToDecimal(result[i][slidingCountKey]));
                Assert.Equal((decimal)frameStart, Convert.ToDecimal(result[i][slidingMinKey]));
                Assert.Equal((decimal)n, Convert.ToDecimal(result[i][slidingMaxKey]));
            }
        }

        [Fact]
        public async Task ExternalWindowEngine_LagAndFirstValue_UseBoundedDeepSpillState()
        {
            var e = NewEvaluator(externalPartitions: 1);
            e.WindowSpillThreshold = 3;
            var logger = new CapturingLogger();
            var aggregateEngine = new AggregateEngine(e, logger);
            var windowEngine = new WindowEngine(e, aggregateEngine, logger);
            var externalWindowEngine = new ExternalWindowEngine(e, windowEngine, logger);

            var window = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause> { new(new IdentifierExpression("Val"), false) });
            var lag = new FunctionCallExpression("LAG", new List<Expression>
            {
                new IdentifierExpression("Val"),
                new LiteralExpression(2, TokenType.NUMBER),
                new LiteralExpression(-1, TokenType.NUMBER)
            })
            { Window = window };
            var first = new FunctionCallExpression("FIRST_VALUE", new List<Expression>
            {
                new IdentifierExpression("Val")
            })
            { Window = window };
            var stmt = new SelectStatement(
                new List<SelectColumn>
                {
                    new(new IdentifierExpression("Grp"), "Grp"),
                    new(new IdentifierExpression("Val"), "Val"),
                    new(lag, "PreviousTwo"),
                    new(first, "FirstVal")
                },
                null,
                new TableReference("#input"),
                new List<JoinClause>(),
                null);

            async IAsyncEnumerable<Row> Rows()
            {
                var schema = new TableSchema(new[] { "Grp", "Val" });
                foreach (var grp in new[] { "A", "B" })
                {
                    for (var i = 1; i <= 5; i++)
                    {
                        var row = new Row(schema);
                        row["Grp"] = grp;
                        row["Val"] = i;
                        yield return row;
                    }
                }
                await Task.CompletedTask;
            }

            var result = await externalWindowEngine.ApplyWindowFunctionsExternal(Rows(), stmt).ToListAsync();

            Assert.Equal(10, result.Count);
            Assert.Contains(logger.Messages, m => m.Message.Contains("DEEP-SPILL", StringComparison.OrdinalIgnoreCase));

            var lagKey = $"WINDOW_{lag.ToSql().ToUpperInvariant()}";
            var firstKey = $"WINDOW_{first.ToSql().ToUpperInvariant()}";
            foreach (var partition in result.GroupBy(r => r["Grp"]?.ToString()))
            {
                var rows = partition.OrderBy(r => Convert.ToInt32(r["Val"])).ToList();
                Assert.Equal(5, rows.Count);
                for (var i = 0; i < rows.Count; i++)
                {
                    Assert.Equal(1m, Convert.ToDecimal(rows[i][firstKey]));
                    var expectedLag = i < 2 ? -1m : i - 1m;
                    Assert.Equal(expectedLag, Convert.ToDecimal(rows[i][lagKey]));
                }
            }
        }

        [Fact]
        public async Task ExternalWindowEngine_Lead_UsesBoundedLookaheadSpillState()
        {
            var e = NewEvaluator(externalPartitions: 1);
            e.WindowSpillThreshold = 3;
            var logger = new CapturingLogger();
            var aggregateEngine = new AggregateEngine(e, logger);
            var windowEngine = new WindowEngine(e, aggregateEngine, logger);
            var externalWindowEngine = new ExternalWindowEngine(e, windowEngine, logger);

            var window = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause> { new(new IdentifierExpression("Val"), false) });
            FunctionCallExpression Lead(int offset, int defaultValue) => new("LEAD", new List<Expression>
            {
                new IdentifierExpression("Val"),
                new LiteralExpression(offset, TokenType.NUMBER),
                new LiteralExpression(defaultValue, TokenType.NUMBER)
            })
            { Window = window };
            var leadTwo = Lead(2, -1);
            var leadZero = Lead(0, -2);
            var stmt = new SelectStatement(
                new List<SelectColumn>
                {
                    new(new IdentifierExpression("Grp"), "Grp"),
                    new(new IdentifierExpression("Val"), "Val"),
                    new(leadTwo, "NextTwo"),
                    new(leadZero, "CurrentVal")
                },
                null,
                new TableReference("#input"),
                new List<JoinClause>(),
                null);

            async IAsyncEnumerable<Row> Rows()
            {
                var schema = new TableSchema(new[] { "Grp", "Val" });
                foreach (var grp in new[] { "B", "A" })
                {
                    for (var i = 5; i >= 1; i--)
                    {
                        var row = new Row(schema);
                        row["Grp"] = grp;
                        row["Val"] = i;
                        yield return row;
                    }
                }
                await Task.CompletedTask;
            }

            var result = await externalWindowEngine.ApplyWindowFunctionsExternal(Rows(), stmt).ToListAsync();

            Assert.Equal(10, result.Count);
            Assert.Contains(logger.Messages, m => m.Message.Contains("LEAD-SPILL", StringComparison.OrdinalIgnoreCase));
            var leadTwoKey = $"WINDOW_{leadTwo.ToSql().ToUpperInvariant()}";
            var leadZeroKey = $"WINDOW_{leadZero.ToSql().ToUpperInvariant()}";
            foreach (var partition in result.GroupBy(r => r["Grp"]?.ToString()))
            {
                var rows = partition.OrderBy(r => Convert.ToInt32(r["Val"])).ToList();
                for (var i = 0; i < rows.Count; i++)
                {
                    Assert.Equal((decimal)(i + 1), Convert.ToDecimal(rows[i][leadZeroKey]));
                    var expected = i < 3 ? i + 3m : -1m;
                    Assert.Equal(expected, Convert.ToDecimal(rows[i][leadTwoKey]));
                }
            }
        }

        [Fact]
        public async Task ExternalWindowEngine_PercentRankAndNtile_UseDistributionReplay()
        {
            var e = NewEvaluator(externalPartitions: 1);
            e.WindowSpillThreshold = 3;
            var logger = new CapturingLogger();
            var aggregateEngine = new AggregateEngine(e, logger);
            var windowEngine = new WindowEngine(e, aggregateEngine, logger);
            var externalWindowEngine = new ExternalWindowEngine(e, windowEngine, logger);

            var window = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause> { new(new IdentifierExpression("Val"), false) });
            var percentRank = new FunctionCallExpression("PERCENT_RANK", new List<Expression>()) { Window = window };
            var cumeDist = new FunctionCallExpression("CUME_DIST", new List<Expression>()) { Window = window };
            var ntile = new FunctionCallExpression("NTILE", new List<Expression>
            {
                new LiteralExpression(3, TokenType.NUMBER)
            })
            { Window = window };
            var stmt = new SelectStatement(
                new List<SelectColumn>
                {
                    new(new IdentifierExpression("Grp"), "Grp"),
                    new(new IdentifierExpression("Val"), "Val"),
                    new(percentRank, "PercentRank"),
                    new(cumeDist, "CumeDist"),
                    new(ntile, "Bucket")
                },
                null,
                new TableReference("#input"),
                new List<JoinClause>(),
                null);

            async IAsyncEnumerable<Row> Rows()
            {
                var schema = new TableSchema(new[] { "Grp", "Val" });
                foreach (var grp in new[] { "B", "A" })
                {
                    foreach (var value in new[] { 3, 1, 2, 3, 1 })
                    {
                        var row = new Row(schema);
                        row["Grp"] = grp;
                        row["Val"] = value;
                        yield return row;
                    }
                }
                await Task.CompletedTask;
            }

            var result = await externalWindowEngine.ApplyWindowFunctionsExternal(Rows(), stmt).ToListAsync();

            Assert.Equal(10, result.Count);
            Assert.Contains(logger.Messages, m => m.Message.Contains("DISTRIBUTION-SPILL", StringComparison.OrdinalIgnoreCase));
            var percentRankKey = $"WINDOW_{percentRank.ToSql().ToUpperInvariant()}";
            var cumeDistKey = $"WINDOW_{cumeDist.ToSql().ToUpperInvariant()}";
            var ntileKey = $"WINDOW_{ntile.ToSql().ToUpperInvariant()}";
            var expectedRanks = new[] { 0m, 0m, 0.5m, 0.75m, 0.75m };
            var expectedCumeDist = new[] { 0.4m, 0.4m, 0.6m, 1m, 1m };
            var expectedBuckets = new[] { 1m, 1m, 2m, 2m, 3m };
            foreach (var partition in result.GroupBy(r => r["Grp"]?.ToString()))
            {
                var rows = partition.ToList();
                Assert.Equal(expectedRanks, rows.Select(r => Convert.ToDecimal(r[percentRankKey])));
                Assert.Equal(expectedCumeDist, rows.Select(r => Convert.ToDecimal(r[cumeDistKey])));
                Assert.Equal(expectedBuckets, rows.Select(r => Convert.ToDecimal(r[ntileKey])));
            }
        }

        [Fact]
        public async Task ExternalWindowEngine_OrderedFirstAndLastValue_UseSortedReplay()
        {
            var e = NewEvaluator(externalPartitions: 1);
            e.WindowSpillThreshold = 3;
            var logger = new CapturingLogger();
            var aggregateEngine = new AggregateEngine(e, logger);
            var windowEngine = new WindowEngine(e, aggregateEngine, logger);
            var externalWindowEngine = new ExternalWindowEngine(e, windowEngine, logger);

            var window = new WindowClause(
                new List<Expression> { new IdentifierExpression("Grp") },
                new List<OrderByClause> { new(new IdentifierExpression("Val"), false) });
            var first = new FunctionCallExpression("FIRST_VALUE", new List<Expression>
            {
                new IdentifierExpression("Val")
            })
            { Window = window };
            var last = new FunctionCallExpression("LAST_VALUE", new List<Expression>
            {
                new IdentifierExpression("Val")
            })
            { Window = window };
            var stmt = new SelectStatement(
                new List<SelectColumn>
                {
                    new(new IdentifierExpression("Grp"), "Grp"),
                    new(new IdentifierExpression("Val"), "Val"),
                    new(first, "FirstVal"),
                    new(last, "LastVal")
                },
                null,
                new TableReference("#input"),
                new List<JoinClause>(),
                null);

            async IAsyncEnumerable<Row> Rows()
            {
                var schema = new TableSchema(new[] { "Grp", "Val" });
                foreach (var grp in new[] { "B", "A" })
                {
                    for (var value = 5; value >= 1; value--)
                    {
                        var row = new Row(schema);
                        row["Grp"] = grp;
                        row["Val"] = value;
                        yield return row;
                    }
                }
                await Task.CompletedTask;
            }

            var result = await externalWindowEngine.ApplyWindowFunctionsExternal(Rows(), stmt).ToListAsync();

            Assert.Equal(10, result.Count);
            Assert.Contains(logger.Messages, m => m.Message.Contains("ORDERED-VALUE-SPILL", StringComparison.OrdinalIgnoreCase));
            var firstKey = $"WINDOW_{first.ToSql().ToUpperInvariant()}";
            var lastKey = $"WINDOW_{last.ToSql().ToUpperInvariant()}";
            Assert.All(result, row =>
            {
                Assert.Equal(1m, Convert.ToDecimal(row[firstKey]));
                Assert.Equal(5m, Convert.ToDecimal(row[lastKey]));
            });
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
