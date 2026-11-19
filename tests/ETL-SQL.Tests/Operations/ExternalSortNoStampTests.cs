using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SortKey = ETL_SQL.Engine.Engines.SortKey;

namespace ETL_SQL.Tests.Operations;

public class ExternalSortNoStampTests
{
    private static (Evaluator Eval, ETL_SQL.Common.ILogger Logger) BuildContext(
        int chunkSize = 2,
        string spillFormat = "Arrow",
        bool caseSensitive = false)
    {
        var sp = DependencyInjectionSetup.BuildServiceProvider();
        var eval = sp.GetRequiredService<Evaluator>();
        eval.ExternalSortChunkSize = chunkSize;
        eval.SpillFormat = spillFormat;
        eval.CaseSensitiveComparison = caseSensitive;
        var logger = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
        return (eval, logger);
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private static async Task<List<SortKey>> RoundTripArrowSortKeysAsync(
        Evaluator eval,
        string chunkName,
        IReadOnlyList<object?> values,
        bool stampNormalizedKey)
    {
        var schema = new TableSchema(["k"]);
        await using (var writer = await eval.SpillStore.CreateWriterAsync(chunkName))
        {
            var rows = new List<Row>(values.Count);
            foreach (var v in values)
            {
                var row = new Row(schema);
                row["k"] = stampNormalizedKey ? SortKey.FromValue(v).ToObject() : v;
                rows.Add(row);
            }
            await writer.WriteRowsAsync(rows);
        }

        var result = new List<SortKey>(values.Count);
        var keyCols = new[] { "k" };
        await using var reader = await eval.SpillStore.CreateReaderAsync(chunkName);
        var sortReader = Assert.IsAssignableFrom<ISortSpillReader>(reader);
        while (await sortReader.MoveToNextBatchAsync())
        {
            while (sortReader.TryReadBufferedSortRow(keyCols, null, false, out _, out var key0, Span<SortKey>.Empty))
                result.Add(key0);
        }

        return result;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArrowRoundTrip_SortKeyFromValue_MatchesStampedForAllDataTypes(bool startWithNull)
    {
        var (eval, _) = BuildContext();
        var asc = new OrderByClause(new IdentifierExpression("k"), descending: false);
        var desc = new OrderByClause(new IdentifierExpression("k"), descending: true);

        // Groups that share a compatible Arrow column representation when non-null.
        var groups = new (string Name, object?[] Samples)[]
        {
            ("decimal_scale", [1.20m, 1.2m, 1.0000000000000000000000000001m, 1.0000000000000000000000000002m, -0.0000000000000000000000000001m, decimal.MaxValue, decimal.MinValue]),
            ("plain_numeric_strings", ["123", "-45.67", " 42 "]),
            ("nonplain_numeric_strings", ["00123", "1e5", "007"]),
            ("date_strings", ["2026-01-05", "2026-01-05 14:30:00", "2024-02-29"]),
            ("datetime_kinds", [
                new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Unspecified),
                new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Local),
            ]),
            ("dateonly", [new DateOnly(2026, 1, 15), new DateOnly(2024, 2, 29)]),
            ("bool", [false, true]),
            ("text_case", ["Alpha", "alpha", "  Beta  ", "beta", "zebra"]),
        };

        foreach (var (name, rawSamples) in groups)
        {
            object?[] samples = startWithNull ? [null, .. rawSamples] : rawSamples;
            var stampedKeys = await RoundTripArrowSortKeysAsync(eval, $"rt_stamped_{name}_{startWithNull}.tmp", samples, stampNormalizedKey: true);
            var unstampedKeys = await RoundTripArrowSortKeysAsync(eval, $"rt_unstamped_{name}_{startWithNull}.tmp", samples, stampNormalizedKey: false);

            Assert.Equal(stampedKeys.Count, unstampedKeys.Count);
            for (int i = 0; i < samples.Length; i++)
            {
                foreach (bool cs in new[] { false, true })
                {
                    int cmpAsc = SortKey.Compare(stampedKeys[i], unstampedKeys[i], asc, cs);
                    Assert.True(cmpAsc == 0, $"Group={name} idx={i} sample={samples[i]} cs={cs}: stamped=({stampedKeys[i].Kind},{stampedKeys[i].ToObject()}) unstamped=({unstampedKeys[i].Kind},{unstampedKeys[i].ToObject()}) cmp={cmpAsc}");
                    Assert.Equal(0, SortKey.Compare(stampedKeys[i], unstampedKeys[i], desc, cs));
                    Assert.Equal(
                        ExternalSortEngine.ComputeKeyPrefix(stampedKeys[i], asc, cs),
                        ExternalSortEngine.ComputeKeyPrefix(unstampedKeys[i], asc, cs));
                    Assert.Equal(
                        ExternalSortEngine.ComputeKeyPrefix(stampedKeys[i], desc, cs),
                        ExternalSortEngine.ComputeKeyPrefix(unstampedKeys[i], desc, cs));
                }
            }
        }

        // Explicit NULL / DBNull check.
        var nullSamples = new object?[] { null, DBNull.Value };
        var stampedNulls = await RoundTripArrowSortKeysAsync(eval, $"rt_stamped_null_{startWithNull}.tmp", nullSamples, stampNormalizedKey: true);
        var unstampedNulls = await RoundTripArrowSortKeysAsync(eval, $"rt_unstamped_null_{startWithNull}.tmp", nullSamples, stampNormalizedKey: false);
        Assert.All(stampedNulls, k => Assert.True(k.IsNull));
        Assert.All(unstampedNulls, k => Assert.True(k.IsNull));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SortExternal_UnstampedPreservesRawValuesAndMatchesStampedOrder(bool caseSensitive)
    {
        var schema = new TableSchema(["k", "seq"]);
        var datasets = new (string Name, (object? Key, int Seq)[] Rows)[]
        {
            ("plain_numeric_strings", [("  42  ", 1), ("123", 2), ("-5.50", 3), ("0", 4), ("99", 5)]),
            ("date_strings", [("2026-01-05", 1), ("2024-02-29", 2), (null, 0), ("2026-01-05 14:30:00", 3)]),
            ("text_strings", [("Alpha", 1), ("alpha", 2), (null, 0), ("  Beta  ", 3), ("zebra", 4), ("00123", 5)]),
            ("decimals", [(null, 0), (1.20m, 1), (1.2m, 2), (-5.50m, 3), (0m, 4)]),
            ("dateonly", [(null, 0), (new DateOnly(2025, 12, 31), 1), (new DateOnly(2024, 2, 29), 2), (new DateOnly(2026, 1, 5), 3)]),
            ("datetime", [
                (null, 0),
                (new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc), 1),
                (new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Unspecified), 2),
                (new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc), 3),
            ]),
            ("bools", [(null, 0), (true, 1), (false, 2), (true, 3)]),
        };

        foreach (var (name, rawPairs) in datasets)
        {
            List<Row> MakeRows() => rawPairs.Select(p =>
            {
                var r = new Row(schema);
                r["k"] = p.Key;
                r["seq"] = (decimal)p.Seq;
                return r;
            }).ToList();

            var (evalUnstamped, loggerU) = BuildContext(chunkSize: 3, caseSensitive: caseSensitive);
            var engineUnstamped = new ExternalSortEngine(evalUnstamped, loggerU);
            var plainOrder = new List<OrderByClause>
            {
                new(new IdentifierExpression("k"), false),
                new(new IdentifierExpression("seq"), false),
            };
            var unstampedSorted = await engineUnstamped.SortExternal(MakeRows(), plainOrder);

            Assert.Equal(0, engineUnstamped.StampedSpillRowCount);
            Assert.Equal(0, engineUnstamped.LastSpilledSentinelColumnCount);

            // Compare against forced-stamped path via non-plain expression.
            var (evalStamped, loggerS) = BuildContext(chunkSize: 3, caseSensitive: caseSensitive);
            var engineStamped = new ExternalSortEngine(evalStamped, loggerS);
            var exprOrder = new List<OrderByClause>
            {
                new(new FunctionCallExpression("COALESCE", [new IdentifierExpression("k"), new LiteralExpression(null, TokenType.NULL)]), false),
                new(new IdentifierExpression("seq"), false),
            };
            var stampedSorted = await engineStamped.SortExternal(MakeRows(), exprOrder);

            Assert.True(engineStamped.StampedSpillRowCount > 0, $"Expected stamped path for {name}");
            Assert.Equal(
                stampedSorted.Select(r => Convert.ToInt32(r["seq"], CultureInfo.InvariantCulture)),
                unstampedSorted.Select(r => Convert.ToInt32(r["seq"], CultureInfo.InvariantCulture)));

            if (name == "plain_numeric_strings")
            {
                // Verify raw string formatting (e.g. "  42  ") was preserved on output rows.
                Assert.Contains(unstampedSorted, r => Equals(r["k"], "  42  "));
            }
            if (name == "text_strings")
            {
                Assert.Contains(unstampedSorted, r => Equals(r["k"], "  Beta  "));
                Assert.Contains(unstampedSorted, r => Equals(r["k"], "00123"));
            }
        }
    }

    [Fact]
    public async Task SortExternal_DecimalScalePreservedWhenUnstamped()
    {
        var schema = new TableSchema(["d", "tag"]);
        Row MakeRow(decimal d, string tag)
        {
            var r = new Row(schema);
            r["d"] = d;
            r["tag"] = tag;
            return r;
        }

        var rows = new List<Row>
        {
            MakeRow(1.2500m, "s4"),
            MakeRow(1.0000000000000000000000000002m, "b"),
            MakeRow(1.0000000000000000000000000001m, "a"),
            MakeRow(decimal.MinValue, "min"),
            MakeRow(decimal.MaxValue, "max"),
        };

        var (eval, logger) = BuildContext(chunkSize: 2);
        var engine = new ExternalSortEngine(eval, logger);
        var sorted = await engine.SortExternal(rows, [new(new IdentifierExpression("d"), false)]);

        Assert.Equal(0, engine.StampedSpillRowCount);
        Assert.Equal(["min", "a", "b", "s4", "max"], sorted.Select(r => (string)r["tag"]!).ToArray());
        Assert.Equal("1.2500", ((decimal)sorted[3]["d"]!).ToString(CultureInfo.InvariantCulture));
        Assert.Equal(["d", "tag"], sorted[0].Schema!.ColumnNames.ToArray());
    }

    [Fact]
    public async Task SortExternal_NameResolution_CaseAliasAndQualified_UnstampsCorrectly()
    {
        // 1. Case-insensitive plain column + schema alias
        var schema = new TableSchema(["id", "val"]);
        schema.AddAlias("t.val", 1);

        List<Row> MakeAliasedRows()
        {
            var list = new List<Row>();
            for (int i = 10; i >= 1; i--)
            {
                var r = new Row(schema);
                r["id"] = (decimal)i;
                r["val"] = (decimal)(i * 10);
                list.Add(r);
            }
            return list;
        }

        var (eval1, logger1) = BuildContext(chunkSize: 3);
        var engine1 = new ExternalSortEngine(eval1, logger1);
        var sorted1 = await engine1.SortExternal(MakeAliasedRows(), [new(new IdentifierExpression("T.VAL"), false)]);
        Assert.Equal(0, engine1.StampedSpillRowCount);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (decimal)(i * 10)), sorted1.Select(r => (decimal)r["val"]!));

        // 2. Qualified ORDER BY t.val against schema without alias (ColumnMatcher fallback)
        var plainSchema = new TableSchema(["id", "val"]);
        var plainRows = Enumerable.Range(1, 8).Reverse().Select(i =>
        {
            var r = new Row(plainSchema);
            r["id"] = (decimal)i;
            r["val"] = (decimal)(i * 3);
            return r;
        }).ToList();

        var (eval2, logger2) = BuildContext(chunkSize: 3);
        var engine2 = new ExternalSortEngine(eval2, logger2);
        var sorted2 = await engine2.SortExternal(plainRows, [new(new IdentifierExpression("t.val"), false)]);
        Assert.Equal(0, engine2.StampedSpillRowCount);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (decimal)(i * 3)), sorted2.Select(r => (decimal)r["val"]!));

        // 3. Unqualified ORDER BY val against qualified canonical column "t.val"
        var qualSchema = new TableSchema(["id", "t.val"]);
        var qualRows = Enumerable.Range(1, 8).Reverse().Select(i =>
        {
            var r = new Row(qualSchema);
            r["id"] = (decimal)i;
            r["t.val"] = (decimal)(i * 5);
            return r;
        }).ToList();

        var (eval3, logger3) = BuildContext(chunkSize: 3);
        var engine3 = new ExternalSortEngine(eval3, logger3);
        var sorted3 = await engine3.SortExternal(qualRows, [new(new IdentifierExpression("VAL"), false)]);
        Assert.Equal(0, engine3.StampedSpillRowCount);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (decimal)(i * 5)), sorted3.Select(r => (decimal)r["t.val"]!));
    }

    [Fact]
    public async Task SortExternal_DuplicateCanonicalNamesWithAlias_FallsBackToStamping()
    {
        var schema = new TableSchema(["a", "a"]);
        schema.AddAlias("t1.a", 0);
        schema.AddAlias("t2.a", 1);

        var rows = new List<Row>();
        for (int i = 0; i < 8; i++)
        {
            var r = new Row(schema);
            r[0] = (decimal)i;
            r[1] = (decimal)(100 - i);
            rows.Add(r);
        }

        var (eval, logger) = BuildContext(chunkSize: 3);
        var engine = new ExternalSortEngine(eval, logger);
        var sorted = await engine.SortExternal(rows, [new(new IdentifierExpression("t2.a"), false)]);

        Assert.True(engine.StampedSpillRowCount > 0);
        // Sorted ascending by t2.a (100 - i), so slot 0 ('a' = i) arrives in descending order 7..0.
        Assert.Equal(Enumerable.Range(0, 8).Reverse().Select(i => (decimal)i), sorted.Select(r => (decimal)r[0]!));
        Assert.DoesNotContain(sorted[0].GetColumnNames(), c => c.StartsWith("_SYS_SK_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SortExternal_MixedPlainAndExpressionKeys_StampsOnlyExpressionKey()
    {
        var schema = new TableSchema(["grp", "val", "id"]);
        var rows = new List<Row>();
        for (int i = 1; i <= 12; i++)
        {
            var r = new Row(schema);
            r["grp"] = i % 2 == 0 ? "B" : "A";
            r["val"] = (decimal)i;
            r["id"] = (decimal)(100 - i);
            rows.Add(r);
        }

        var (eval, logger) = BuildContext(chunkSize: 4);
        var engine = new ExternalSortEngine(eval, logger);
        var orderBy = new List<OrderByClause>
        {
            new(new IdentifierExpression("grp"), false),
            new(new BinaryExpression(new IdentifierExpression("val"), TokenType.PLUS, new LiteralExpression(10m, TokenType.NUMBER)), true),
            new(new IdentifierExpression("id"), false),
        };

        var sorted = await engine.SortExternal(rows, orderBy);

        Assert.Equal(1, engine.LastSpilledSentinelColumnCount);
        Assert.Equal(12, engine.StampedSpillRowCount);
        Assert.Equal(["grp", "val", "id"], sorted[0].Schema!.ColumnNames.ToArray());
        Assert.DoesNotContain(sorted[0].GetColumnNames(), c => c.StartsWith("_SYS_SK_", StringComparison.Ordinal));

        // Expected: grp A descending by val (11,9,7,5,3,1), then grp B descending by val (12,10,8,6,4,2)
        var expectedVals = new decimal[] { 11, 9, 7, 5, 3, 1, 12, 10, 8, 6, 4, 2 };
        Assert.Equal(expectedVals, sorted.Select(r => (decimal)r["val"]!));
    }

    [Fact]
    public async Task SortExternal_VariablesFunctionsConnectionsOuterRefsAndAmbiguous_StillStamp()
    {
        // Variable
        {
            var (eval, logger) = BuildContext(chunkSize: 2);
            await eval.Evaluate(Parse("DECLARE @k INT = 1;"));
            var schema = new TableSchema(["id"]);
            var rows = Enumerable.Range(1, 5).Select(i => { var r = new Row(schema); r["id"] = (decimal)i; return r; }).ToList();
            var engine = new ExternalSortEngine(eval, logger);
            var sorted = await engine.SortExternal(rows, [new(new IdentifierExpression("@k"), false)]);
            Assert.Equal(5, sorted.Count);
            Assert.True(engine.StampedSpillRowCount > 0);
        }

        // Registered no-arg function name
        {
            var (eval, logger) = BuildContext(chunkSize: 2);
            eval.FunctionRegistry.Register("CONST_KEY", (_, _) => 1m);
            var schema = new TableSchema(["id"]);
            var rows = Enumerable.Range(1, 5).Select(i => { var r = new Row(schema); r["id"] = (decimal)i; return r; }).ToList();
            var engine = new ExternalSortEngine(eval, logger);
            var sorted = await engine.SortExternal(rows, [new(new IdentifierExpression("CONST_KEY"), false)]);
            Assert.Equal(5, sorted.Count);
            Assert.True(engine.StampedSpillRowCount > 0);
        }

        // OuterRowStack non-empty
        {
            var (eval, logger) = BuildContext(chunkSize: 2);
            var outerRow = new Row { ["outer_col"] = 42m };
            eval.OuterRowStack.Push(outerRow);
            try
            {
                var schema = new TableSchema(["id"]);
                var rows = Enumerable.Range(1, 5).Select(i => { var r = new Row(schema); r["id"] = (decimal)i; return r; }).ToList();
                var engine = new ExternalSortEngine(eval, logger);
                var sorted = await engine.SortExternal(rows, [new(new IdentifierExpression("outer_col"), false)]);
                Assert.Equal(5, sorted.Count);
                Assert.True(engine.StampedSpillRowCount > 0);
            }
            finally
            {
                eval.OuterRowStack.Pop();
            }
        }

        // Ambiguous column name still throws
        {
            var (eval, logger) = BuildContext(chunkSize: 2);
            var schema = new TableSchema(["a.val", "b.val"]);
            var rows = Enumerable.Range(1, 4).Select(i =>
            {
                var r = new Row(schema);
                r[0] = (decimal)i;
                r[1] = (decimal)(i * 2);
                return r;
            }).ToList();
            var engine = new ExternalSortEngine(eval, logger);
            await Assert.ThrowsAsync<ExecutionException>(() =>
                engine.SortExternal(rows, [new(new IdentifierExpression("val"), false)]));
        }
    }

    [Fact]
    public async Task SortExternal_DynamicChunkFallbackWhenLaterChunkLacksColumn_StampsOnlyFallbackChunk()
    {
        var schemaWithVal = new TableSchema(["id", "val"]);
        var schemaWithoutVal = new TableSchema(["id"]);

        var rows = new List<Row>();
        // Chunk 0 (2 rows): has "val" -> unstamped
        for (int i = 1; i <= 2; i++)
        {
            var r = new Row(schemaWithVal);
            r["id"] = (decimal)i;
            r["val"] = (decimal)(100 - i);
            rows.Add(r);
        }
        // Chunk 1 (2 rows): lacks "val" with MayLackColumns -> falls back to stamping _SYS_SK_0 = null
        for (int i = 3; i <= 4; i++)
        {
            var r = new Row(schemaWithoutVal) { MayLackColumns = true };
            r["id"] = (decimal)i;
            rows.Add(r);
        }

        var (eval, logger) = BuildContext(chunkSize: 2);
        var engine = new ExternalSortEngine(eval, logger);
        var sorted = await engine.SortExternal(rows, [new(new IdentifierExpression("val"), false)]);

        Assert.Equal(4, sorted.Count);
        Assert.Equal(2, engine.StampedSpillRowCount);
        // ASC default places NULLs first (rows 3,4), then 98 (row 2), then 99 (row 1)
        Assert.Null(sorted[0]["val"]);
        Assert.Null(sorted[1]["val"]);
        Assert.Equal(98m, sorted[2]["val"]);
        Assert.Equal(99m, sorted[3]["val"]);
        Assert.All(sorted, r => Assert.DoesNotContain(r.GetColumnNames(), c => c.StartsWith("_SYS_SK_", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("Arrow")]
    [InlineData("Json")]
    public async Task SortExternal_MultiPassMerge_UnstampedAndMixed_ProducesExactOrder(string spillFormat)
    {
        const int n = 150;
        var schema = new TableSchema(["grp", "val"]);
        var rows = new List<Row>(n);
        for (int i = 0; i < n; i++)
        {
            int shuffled = (i * 73 + 19) % n;
            var r = new Row(schema);
            r["grp"] = (decimal)(shuffled % 5);
            r["val"] = (decimal)shuffled;
            rows.Add(r);
        }

        // Unstamped multi-pass (> 64 chunks at chunkSize=2)
        var (eval1, logger1) = BuildContext(chunkSize: 2, spillFormat: spillFormat);
        var engine1 = new ExternalSortEngine(eval1, logger1);
        var sorted1 = await engine1.SortExternal(rows, [
            new(new IdentifierExpression("grp"), false),
            new(new IdentifierExpression("val"), true),
        ]);
        Assert.Equal(n, sorted1.Count);
        Assert.Equal(0, engine1.StampedSpillRowCount);
        Assert.True(engine1.IntermediateMergeRunCount > 0);
        for (int i = 1; i < sorted1.Count; i++)
        {
            var prev = sorted1[i - 1];
            var curr = sorted1[i];
            int gCmp = ((decimal)prev["grp"]!).CompareTo((decimal)curr["grp"]!);
            Assert.True(gCmp <= 0);
            if (gCmp == 0)
                Assert.True(((decimal)prev["val"]!).CompareTo((decimal)curr["val"]!) >= 0);
        }

        // Mixed multi-pass (1 unstamped + 1 expression)
        var (eval2, logger2) = BuildContext(chunkSize: 2, spillFormat: spillFormat);
        var engine2 = new ExternalSortEngine(eval2, logger2);
        var sorted2 = await engine2.SortExternal(rows, [
            new(new IdentifierExpression("grp"), false),
            new(new BinaryExpression(new IdentifierExpression("val"), TokenType.MINUS, new LiteralExpression(0m, TokenType.NUMBER)), true),
        ]);
        Assert.Equal(n, sorted2.Count);
        Assert.Equal(n, engine2.StampedSpillRowCount);
        Assert.Equal(1, engine2.LastSpilledSentinelColumnCount);
        Assert.Equal(sorted1.Select(r => r["val"]), sorted2.Select(r => r["val"]));
        Assert.All(sorted2, r => Assert.DoesNotContain(r.GetColumnNames(), c => c.StartsWith("_SYS_SK_", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SortStreamAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        var schema = new TableSchema(["id"]);
        var rows = Enumerable.Range(1, 20).Reverse().Select(i =>
        {
            var r = new Row(schema);
            r["id"] = (decimal)i;
            return r;
        }).ToAsyncEnumerable();

        var (eval, logger) = BuildContext(chunkSize: 2);
        var engine = new ExternalSortEngine(eval, logger);
        using var cts = new CancellationTokenSource();

        int count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in engine.SortStreamAsync(rows, [new(new IdentifierExpression("id"), false)], cancellationToken: cts.Token))
            {
                count++;
                if (count == 3)
                    cts.Cancel();
            }
        });
    }

    [Fact]
    public async Task SqlEndToEnd_ExternalSortAndWindow_PreservesSchemaOrderNullsAndResults()
    {
        var (eval, _) = BuildContext(chunkSize: 3);
        eval.JoinSpillThreshold = 5;
        eval.WindowSpillThreshold = 5;

        await eval.Evaluate(Parse("""
            CREATE TABLE #items (id INT, grp VARCHAR, score DECIMAL(10, 2));
            INSERT INTO #items VALUES
                (1, 'B', 20.50),
                (2, 'A', NULL),
                (3, 'A', 10.25),
                (4, 'B', 5.00),
                (5, 'A', 30.00),
                (6, 'B', NULL),
                (7, 'A', 10.25),
                (8, 'B', 15.00);
            SELECT id, grp, score FROM #items ORDER BY grp ASC, score DESC NULLS LAST, id ASC;
            """));

        var sortResult = eval.LastResult!;
        Assert.Equal(["id", "grp", "score"], sortResult.Schema.ColumnNames);
        Assert.Equal([5m, 3m, 7m, 2m, 1m, 8m, 4m, 6m], sortResult.Rows.Select(r => Convert.ToDecimal(r["id"])));
        Assert.True(eval.Telemetry.SortSpillCount > 0);

        await eval.Evaluate(Parse("""
            SELECT id, grp, ROW_NUMBER() OVER (PARTITION BY grp ORDER BY score DESC NULLS LAST, id ASC) AS rn
            FROM #items
            ORDER BY id ASC;
            """));

        var winResult = eval.LastResult!;
        Assert.Equal(["id", "grp", "rn"], winResult.Schema.ColumnNames);
        var byId = winResult.Rows.ToDictionary(r => Convert.ToInt32(r["id"]), r => Convert.ToInt32(r["rn"]));
        Assert.Equal(1, byId[5]);
        Assert.Equal(2, byId[3]);
        Assert.Equal(3, byId[7]);
        Assert.Equal(4, byId[2]);
        Assert.Equal(1, byId[1]);
        Assert.Equal(2, byId[8]);
        Assert.Equal(3, byId[4]);
        Assert.Equal(4, byId[6]);
    }
}
