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
using Xunit;

namespace ETL_SQL.Tests.Operations.Operations
{
    public class ExternalSortTests
    {
        private (Evaluator eval, ETL_SQL.Common.ILogger logger) BuildContext(int chunkSize = 100000)
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            eval.ExternalSortChunkSize = chunkSize;
            var logger = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
            return (eval, logger);
        }

        [Fact]
        public async Task SortExternal_AcrossMultipleChunks_ProducesCorrectOrder()
        {
            // Forces 4 chunks: [10, 9], [8, 7], [6, 5], [4, 3, 2, 1] -> wait no, 2 per chunk
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>();
            for (int i = 10; i >= 1; i--)
            {
                rows.Add(new Row { ["id"] = i, ["val"] = $"val-{i}" });
            }

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("id"), false) // ASC
            };

            var startSpill = eval.Telemetry.TotalSpilledBytes;
            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(10, sorted.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal((decimal)(i + 1), Convert.ToDecimal(sorted[i]["id"]));
            }

            Assert.True(eval.Telemetry.TotalSpilledBytes > startSpill, "Should have reported spilled bytes to context");
        }

        [Fact]
        public async Task SortExternal_DescendingOrder_ProducesCorrectOrder()
        {
            var (eval, logger) = BuildContext(chunkSize: 5);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>();
            for (int i = 1; i <= 10; i++)
            {
                rows.Add(new Row { ["id"] = i });
            }

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("id"), true) // DESC
            };

            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(10, sorted.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal((decimal)(10 - i), Convert.ToDecimal(sorted[i]["id"]));
            }
        }

        [Fact]
        public async Task SortExternal_ExceedsMergeFanIn_MultiPassMergeProducesCorrectOrder()
        {
            // chunkSize 2 over 300 rows -> 150 spill chunks, which exceeds the 64-way
            // MaxMergeFanIn cap and forces a multi-pass (reduction) merge. The output must
            // still be fully and correctly ordered with no rows lost or duplicated.
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);

            const int n = 300;
            // Deterministic shuffle so the test is reproducible but not pre-sorted.
            var ids = Enumerable.Range(1, n).ToList();
            for (int i = 0; i < n; i++)
            {
                int j = (i * 137 + 53) % n;
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            var rows = ids.Select(i => new Row { ["id"] = i, ["val"] = $"val-{i}" }).ToList();

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("id"), false) // ASC
            };

            var startExtents = eval.Telemetry.SpillExtentCount;
            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(n, sorted.Count);
            for (int i = 0; i < n; i++)
            {
                Assert.Equal((decimal)(i + 1), Convert.ToDecimal(sorted[i]["id"]));
                Assert.Equal($"val-{i + 1}", sorted[i]["val"]); // row payload travels with its key
            }
            Assert.InRange(engine.MaxConcurrentMergeReaders, 1, 64);
            Assert.Equal(2, engine.MergePassCount);
            Assert.Equal(3, engine.IntermediateMergeRunCount);
            Assert.Equal(153, eval.Telemetry.SpillExtentCount - startExtents);
            Assert.Equal(0, engine.IntermediateKeyStripCount);
            Assert.True(engine.PrefixResolvedComparisons > 0);
            Assert.All(sorted, r => Assert.DoesNotContain(r.GetColumnNames(), c => c.StartsWith("_SYS_SK_", StringComparison.Ordinal)));
        }

        [Theory]
        [InlineData(false, null, false)]
        [InlineData(false, false, false)]
        [InlineData(true, null, false)]
        [InlineData(true, true, false)]
        [InlineData(false, null, true)]
        public async Task SortExternal_KeyPrefixAndMultiPass_PreservesNullPlacementMixedTypesAndFractionalDecimals(
            bool descending, bool? nullsFirst, bool caseSensitive)
        {
            var numericValues = new object?[] { null, -2.75m, -2.25m, -2m, 1m, 1.0m, 1.2m, 1.8m };
            var dateValues = new object?[]
            {
                null,
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 1, 12, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            };
            var textValues = new object?[] { null, "Alpha", "alpha", "beta", "zebra" };

            foreach (var values in new[] { numericValues, dateValues, textValues })
            {
                var (eval, logger) = BuildContext(chunkSize: 2);
                eval.CaseSensitiveComparison = caseSensitive;

                // Expand to >64 chunks (chunkSize=2 -> >=135 rows -> >=68 chunks) to force intermediate merge passes.
                var rows = new List<Row>();
                int repeat = Math.Max(18, 140 / values.Length);
                int seq = 0;
                for (int rep = 0; rep < repeat; rep++)
                {
                    foreach (var val in values)
                        rows.Add(new Row { ["k"] = val, ["seq"] = (decimal)seq++ });
                }

                // Deterministic permutation before external sort.
                for (int i = 0; i < rows.Count; i++)
                {
                    int j = (i * 73 + 19) % rows.Count;
                    (rows[i], rows[j]) = (rows[j], rows[i]);
                }

                var orderBy = new List<OrderByClause>
                {
                    new OrderByClause(new IdentifierExpression("k"), descending, nullsFirst),
                    new OrderByClause(new IdentifierExpression("seq"), false)
                };

                var engine = new ExternalSortEngine(eval, logger);
                var sorted = await engine.SortExternal(rows, orderBy);

                Assert.Equal(rows.Count, sorted.Count);
                Assert.Equal(0, engine.IntermediateKeyStripCount);
                Assert.True(engine.IntermediateMergeRunCount > 0);
                Assert.True(engine.PrefixResolvedComparisons > 0);
                Assert.All(sorted, r => Assert.DoesNotContain(r.GetColumnNames(), c => c.StartsWith("_SYS_SK_", StringComparison.Ordinal)));

                for (int i = 1; i < sorted.Count; i++)
                {
                    var prev = sorted[i - 1];
                    var curr = sorted[i];
                    var kCmp = orderBy[0].OutputOrder(eval.CompareConstants(prev["k"], curr["k"]), prev["k"], curr["k"]);
                    Assert.True(kCmp <= 0, $"Out-of-order primary keys at index {i}: '{prev["k"]}' before '{curr["k"]}'");
                    if (kCmp == 0)
                    {
                        var sCmp = orderBy[1].OutputOrder(eval.CompareConstants(prev["seq"], curr["seq"]), prev["seq"], curr["seq"]);
                        Assert.True(sCmp <= 0, $"Out-of-order tie-breaker at index {i}");
                    }
                }
            }

            // Direct ComputeKeyPrefix monotonic consistency across NULL placement, equivalent decimals, mixed types, and bytes.
            var checkOrder = new List<OrderByClause> { new(new IdentifierExpression("k"), descending, nullsFirst) };
            Assert.Equal(
                ExternalSortEngine.ComputeKeyPrefix(new object?[] { 1m }, checkOrder, caseSensitive),
                ExternalSortEngine.ComputeKeyPrefix(new object?[] { 1.0m }, checkOrder, caseSensitive));
            bool expectNullsFirst = nullsFirst ?? !descending;
            ulong nullPrefix = ExternalSortEngine.ComputeKeyPrefix(new object?[] { null }, checkOrder, caseSensitive);
            Assert.Equal(expectNullsFirst ? 0UL : ulong.MaxValue, nullPrefix);

            var (evalCheck, _) = BuildContext(chunkSize: 2);
            evalCheck.CaseSensitiveComparison = caseSensitive;
            var mixedSamples = new object?[]
            {
                null,
                -2.75m,
                -2m,
                1m,
                1.8m,
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                "Alpha",
                "zebra",
                new byte[] { 1, 2 },
                new byte[] { 2, 0 },
            };
            foreach (var a in mixedSamples)
            {
                ulong pa = ExternalSortEngine.ComputeKeyPrefix(new object?[] { a }, checkOrder, caseSensitive);
                foreach (var b in mixedSamples)
                {
                    ulong pb = ExternalSortEngine.ComputeKeyPrefix(new object?[] { b }, checkOrder, caseSensitive);
                    if (pa != pb)
                    {
                        int oracle = checkOrder[0].OutputOrder(evalCheck.CompareConstants(a, b), a, b);
                        Assert.Equal(Math.Sign(pa.CompareTo(pb)), Math.Sign(oracle));
                    }
                }
            }
        }

        // Note: no dedicated "early-flush under a 1-byte ceiling" test — asserting the guard tripped
        // depends on real-heap timing (non-deterministic in a shared-process run). The memory-guarded
        // early flush is exercised by the scale-cert repro (sort stays ~500MB at 10M); chunked
        // multi-pass merge correctness is covered by the multi-pass test above.

        [Fact]
        public async Task SortExternal_MultiColumnSort_ProducesCorrectOrder()
        {
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>
            {
                new Row { ["cat"] = "B", ["val"] = 1m },
                new Row { ["cat"] = "A", ["val"] = 2m },
                new Row { ["cat"] = "A", ["val"] = 1m },
                new Row { ["cat"] = "B", ["val"] = 2m },
            };

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("cat"), false), // cat ASC
                new OrderByClause(new IdentifierExpression("val"), true)   // val DESC
            };

            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(4, sorted.Count);

            // Expected: A/2, A/1, B/2, B/1
            Assert.Equal("A", sorted[0]["cat"]); Assert.Equal(2m, Convert.ToDecimal(sorted[0]["val"]));
            Assert.Equal("A", sorted[1]["cat"]); Assert.Equal(1m, Convert.ToDecimal(sorted[1]["val"]));
            Assert.Equal("B", sorted[2]["cat"]); Assert.Equal(2m, Convert.ToDecimal(sorted[2]["val"]));
            Assert.Equal("B", sorted[3]["cat"]); Assert.Equal(1m, Convert.ToDecimal(sorted[3]["val"]));
        }

        [Fact]
        public async Task SortKey_DecimalPrecisionAndScale_PreservesExactOrderAndValue()
        {
            decimal a = 1.0000000000000000000000000001m;
            decimal b = 1.0000000000000000000000000002m;
            decimal c = 1.000m;
            decimal d = 1m;
            decimal negTiny = -0.0000000000000000000000000001m;

            var asc = new OrderByClause(new IdentifierExpression("k"), descending: false);
            Assert.True(SortKey.Compare(SortKey.FromValue(negTiny), SortKey.FromValue(0m), asc, caseSensitive: false) < 0);
            Assert.True(SortKey.Compare(SortKey.FromValue(a), SortKey.FromValue(b), asc, caseSensitive: false) < 0);
            Assert.Equal(0, SortKey.Compare(SortKey.FromValue(c), SortKey.FromValue(d), asc, caseSensitive: false));
            Assert.Equal(
                ExternalSortEngine.ComputeKeyPrefix(SortKey.FromValue(c), asc, caseSensitive: false),
                ExternalSortEngine.ComputeKeyPrefix(SortKey.FromValue(d), asc, caseSensitive: false));
            Assert.Equal(a, SortKey.FromValue(a).DecimalValue);
            Assert.Equal(decimal.MaxValue, SortKey.FromValue(decimal.MaxValue).DecimalValue);
            Assert.Equal(decimal.MinValue, SortKey.FromValue(decimal.MinValue).DecimalValue);

            // Round-trip through Arrow spill (chunkSize: 2) preserves exact precision and scale on output rows.
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);
            var rows = new List<Row>
            {
                new Row { ["k"] = b, ["tag"] = "b" },
                new Row { ["k"] = decimal.MaxValue, ["tag"] = "max" },
                new Row { ["k"] = a, ["tag"] = "a" },
                new Row { ["k"] = decimal.MinValue, ["tag"] = "min" },
                new Row { ["k"] = negTiny, ["tag"] = "neg" },
                new Row { ["k"] = 1.2500m, ["tag"] = "scale4" },
            };
            var sorted = await engine.SortExternal(rows, [asc]);

            Assert.Equal(["min", "neg", "a", "b", "scale4", "max"], sorted.Select(r => (string)r["tag"]!).ToArray());
            Assert.Equal(decimal.MinValue, sorted[0]["k"]);
            Assert.Equal(negTiny, sorted[1]["k"]);
            Assert.Equal(a, sorted[2]["k"]);
            Assert.Equal(b, sorted[3]["k"]);
            Assert.Equal("1.2500", ((decimal)sorted[4]["k"]!).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(decimal.MaxValue, sorted[5]["k"]);
        }

        [Theory]
        [InlineData(false, null, -1)]
        [InlineData(false, true, -1)]
        [InlineData(false, false, 1)]
        [InlineData(true, null, 1)]
        [InlineData(true, true, -1)]
        [InlineData(true, false, 1)]
        public void SortKey_NullOrdering_RespectsAscDescAndNullsFirstLast(
            bool descending, bool? nullsFirst, int expectedNullVsValueSign)
        {
            var clause = new OrderByClause(new IdentifierExpression("k"), descending, nullsFirst);
            var nullKey = SortKey.FromValue(null);
            var dbNullKey = SortKey.FromValue(DBNull.Value);
            var numKey = SortKey.FromValue(42m);

            Assert.Equal(0, SortKey.Compare(nullKey, dbNullKey, clause, caseSensitive: false));
            Assert.Equal(expectedNullVsValueSign, Math.Sign(SortKey.Compare(nullKey, numKey, clause, caseSensitive: false)));
            Assert.Equal(-expectedNullVsValueSign, Math.Sign(SortKey.Compare(numKey, nullKey, clause, caseSensitive: false)));

            ulong expectedNullPrefix = (nullsFirst ?? !descending) ? 0UL : ulong.MaxValue;
            Assert.Equal(expectedNullPrefix, ExternalSortEngine.ComputeKeyPrefix(nullKey, clause, caseSensitive: false));
            Assert.Equal(expectedNullPrefix, ExternalSortEngine.ComputeKeyPrefix(dbNullKey, clause, caseSensitive: false));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SortKey_MixedTypes_MatchesCompareConstantsAndPrefixOrder(bool caseSensitive)
        {
            var rawSamples = new object?[]
            {
                null,
                -10,
                -2.5m,
                "-2.25",
                "00123",
                123L,
                456.75m,
                new DateOnly(2026, 1, 15),
                new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
                "2026-04-01",
                false,
                true,
                "Alpha",
                "alpha",
                "zebra",
                new byte[] { 1, 2 },
                new byte[] { 2, 0 },
            };

            var asc = new OrderByClause(new IdentifierExpression("k"), descending: false);
            var desc = new OrderByClause(new IdentifierExpression("k"), descending: true);

            foreach (var a in rawSamples)
            {
                var ka = SortKey.FromValue(a);
                var na = CompoundKey.NormalizeValue(a);
                Assert.Equal(
                    ExternalSortEngine.ComputeKeyPrefix(new object?[] { na }, [asc], caseSensitive),
                    ExternalSortEngine.ComputeKeyPrefix(ka, asc, caseSensitive));
                Assert.Equal(
                    ExternalSortEngine.ComputeKeyPrefix(new object?[] { na }, [desc], caseSensitive),
                    ExternalSortEngine.ComputeKeyPrefix(ka, desc, caseSensitive));

                foreach (var b in rawSamples)
                {
                    var kb = SortKey.FromValue(b);
                    var nb = CompoundKey.NormalizeValue(b);
                    int oracleAsc = asc.OutputOrder(ETL_SQL.Core.Data.EvaluationUtils.CompareConstants(na, nb, caseSensitive), na, nb);
                    int actualAsc = SortKey.Compare(ka, kb, asc, caseSensitive);
                    Assert.Equal(Math.Sign(oracleAsc), Math.Sign(actualAsc));

                    int oracleDesc = desc.OutputOrder(ETL_SQL.Core.Data.EvaluationUtils.CompareConstants(na, nb, caseSensitive), na, nb);
                    int actualDesc = SortKey.Compare(ka, kb, desc, caseSensitive);
                    Assert.Equal(Math.Sign(oracleDesc), Math.Sign(actualDesc));
                }
            }
        }

        [Fact]
        public async Task SortKey_CaseSensitivity_RespectsCaseSensitiveFlag()
        {
            var clause = new OrderByClause(new IdentifierExpression("s"), descending: false);
            var upper = SortKey.FromValue("Alpha");
            var lower = SortKey.FromValue("alpha");
            var beta = SortKey.FromValue("beta");

            Assert.Equal(0, SortKey.Compare(upper, lower, clause, caseSensitive: false));
            Assert.True(SortKey.Compare(upper, lower, clause, caseSensitive: true) < 0);
            Assert.True(SortKey.Compare(lower, beta, clause, caseSensitive: false) < 0);
            Assert.True(SortKey.Compare(lower, beta, clause, caseSensitive: true) < 0);

            var (evalSensitive, loggerSensitive) = BuildContext(chunkSize: 2);
            evalSensitive.CaseSensitiveComparison = true;
            var sensitiveRows = new List<Row>
            {
                new Row { ["s"] = "beta", ["seq"] = 1 },
                new Row { ["s"] = "alpha", ["seq"] = 2 },
                new Row { ["s"] = "Alpha", ["seq"] = 3 },
                new Row { ["s"] = "Beta", ["seq"] = 4 },
            };
            var sortedSensitive = await new ExternalSortEngine(evalSensitive, loggerSensitive)
                .SortExternal(sensitiveRows, [clause]);
            Assert.Equal(["Alpha", "Beta", "alpha", "beta"], sortedSensitive.Select(r => (string)r["s"]!).ToArray());
        }
    }
}
