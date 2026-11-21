using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Tests.Core;
using ETL_SQL.Tests.Scale;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations;

public sealed class SpillProjectionPruningTests
{
    private static Evaluator CreateEvaluator()
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        eval.IsPersistentSession = false;
        eval.JoinSpillThreshold = 50;
        eval.WindowSpillThreshold = 50;
        eval.ExternalSortChunkSize = 50;
        return eval;
    }

    [Fact]
    public async Task ExternalJoin_PrunesUnusedWideColumnsAndReducesSpillVolume()
    {
        const int rows = 500;
        var wideLeft = new string('L', 256);
        var wideRight = new string('R', 256);

        long prunedSpillBytes;
        List<Row> prunedRows;
        await using (var eval = CreateEvaluator())
        {
            eval.Connections["#l"] = new StreamingRowSource(rows,
                ("id", i => (int)(i + 1)),
                ("unused_l1", _ => wideLeft),
                ("unused_l2", _ => wideLeft),
                ("unused_l3", _ => wideLeft));
            eval.Connections["#r"] = new StreamingRowSource(rows,
                ("id", i => (int)(i + 1)),
                ("score", i => (decimal)(i + 1) * 2),
                ("unused_r1", _ => wideRight),
                ("unused_r2", _ => wideRight));
            eval.Telemetry.Clear();

            var tables = await eval.ExecuteQuery(TestHelpers.Parse(
                "SELECT l.id, r.score FROM #l l JOIN #r r ON l.id = r.id;").Statements[0]).ToListAsync();
            prunedRows = tables.SelectMany(t => t.Rows).ToList();
            prunedSpillBytes = eval.Telemetry.TotalSpilledBytes;
        }

        long fullSpillBytes;
        await using (var eval = CreateEvaluator())
        {
            eval.Connections["#l"] = new StreamingRowSource(rows,
                ("id", i => (int)(i + 1)),
                ("unused_l1", _ => wideLeft),
                ("unused_l2", _ => wideLeft),
                ("unused_l3", _ => wideLeft));
            eval.Connections["#r"] = new StreamingRowSource(rows,
                ("id", i => (int)(i + 1)),
                ("score", i => (decimal)(i + 1) * 2),
                ("unused_r1", _ => wideRight),
                ("unused_r2", _ => wideRight));
            eval.Telemetry.Clear();

            var tables = await eval.ExecuteQuery(TestHelpers.Parse(
                "SELECT * FROM #l l JOIN #r r ON l.id = r.id;").Statements[0]).ToListAsync();
            Assert.Equal(rows, tables.Sum(t => t.Rows.Count));
            fullSpillBytes = eval.Telemetry.TotalSpilledBytes;
        }

        Assert.Equal(rows, prunedRows.Count);
        Assert.Equal(
            Enumerable.Range(1, rows).Select(i => (i, i * 2m)).ToArray(),
            prunedRows.Select(r => (Convert.ToInt32(r["id"], CultureInfo.InvariantCulture), Convert.ToDecimal(r["score"], CultureInfo.InvariantCulture)))
                .OrderBy(x => x.Item1)
                .ToArray());
        Assert.True(prunedSpillBytes > 0, "Expected join to spill.");
        Assert.True(prunedSpillBytes < fullSpillBytes / 2,
            $"Expected pruned join spill ({prunedSpillBytes}) to be less than half of full spill ({fullSpillBytes}).");
    }

    [Fact]
    public async Task ExternalJoin_PreservesQualifiedAliasesOnBareNameCollisions()
    {
        const int rows = 120;
        await using var eval = CreateEvaluator();
        eval.Connections["#l"] = new StreamingRowSource(rows,
            ("id", i => (int)(i + 1)),
            ("name", i => $"left_{i + 1}"),
            ("unused_l", _ => "drop_me"));
        eval.Connections["#r"] = new StreamingRowSource(rows,
            ("id", i => (int)(i + 1)),
            ("name", i => $"right_{i + 1}"),
            ("unused_r", _ => "drop_me_too"));

        var tables = await eval.ExecuteQuery(TestHelpers.Parse(
            "SELECT l.id AS lid, r.id AS rid, l.name AS lname, r.name AS rname FROM #l l JOIN #r r ON l.id = r.id;")
            .Statements[0]).ToListAsync();
        var result = tables.SelectMany(t => t.Rows)
            .OrderBy(r => Convert.ToInt32(r["lid"], CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(rows, result.Count);
        for (int i = 0; i < rows; i++)
        {
            Assert.Equal((decimal)(i + 1), Convert.ToDecimal(result[i]["lid"], CultureInfo.InvariantCulture));
            Assert.Equal((decimal)(i + 1), Convert.ToDecimal(result[i]["rid"], CultureInfo.InvariantCulture));
            Assert.Equal($"left_{i + 1}", result[i]["lname"]);
            Assert.Equal($"right_{i + 1}", result[i]["rname"]);
        }
    }

    [Fact]
    public async Task ExternalSort_PrunesUnusedAndWhereOnlyColumnsBeforeSpill()
    {
        const int rows = 400;
        var wide = new string('X', 256);

        long prunedSpillBytes;
        List<Row> prunedRows;
        await using (var eval = CreateEvaluator())
        {
            eval.Connections["#src"] = new StreamingRowSource(rows,
                ("id", i => (int)(rows - i)),
                ("flag", i => (int)(i % 2)),
                ("unused1", _ => wide),
                ("unused2", _ => wide),
                ("unused3", _ => wide));
            eval.Telemetry.Clear();

            var tables = await eval.ExecuteQuery(TestHelpers.Parse(
                "SELECT id FROM #src WHERE flag = 0 ORDER BY id ASC;").Statements[0]).ToListAsync();
            prunedRows = tables.SelectMany(t => t.Rows).ToList();
            prunedSpillBytes = eval.Telemetry.TotalSpilledBytes;
        }

        long fullSpillBytes;
        await using (var eval = CreateEvaluator())
        {
            eval.Connections["#src"] = new StreamingRowSource(rows,
                ("id", i => (int)(rows - i)),
                ("flag", i => (int)(i % 2)),
                ("unused1", _ => wide),
                ("unused2", _ => wide),
                ("unused3", _ => wide));
            eval.Telemetry.Clear();

            var tables = await eval.ExecuteQuery(TestHelpers.Parse(
                "SELECT * FROM #src WHERE flag = 0 ORDER BY id ASC;").Statements[0]).ToListAsync();
            Assert.Equal(rows / 2, tables.Sum(t => t.Rows.Count));
            fullSpillBytes = eval.Telemetry.TotalSpilledBytes;
        }

        Assert.Equal(rows / 2, prunedRows.Count);
        var ids = prunedRows.Select(r => Convert.ToInt32(r["id"], CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(ids.OrderBy(x => x).ToArray(), ids);
        Assert.True(prunedSpillBytes > 0, "Expected external sort to spill.");
        Assert.True(prunedSpillBytes < fullSpillBytes / 2,
            $"Expected pruned sort spill ({prunedSpillBytes}) to be less than half of full spill ({fullSpillBytes}).");
    }

    [Fact]
    public async Task ExternalWindow_PrunesUnusedAndWhereOnlyColumnsBeforeSpill()
    {
        const int rows = 400;
        var wide = new string('W', 256);

        long prunedSpillBytes;
        List<Row> prunedRows;
        await using (var eval = CreateEvaluator())
        {
            eval.Connections["#src"] = new StreamingRowSource(rows,
                ("val", i => (int)(rows - i)),
                ("flag", i => (int)(i % 2)),
                ("unused1", _ => wide),
                ("unused2", _ => wide),
                ("unused3", _ => wide));
            eval.Telemetry.Clear();

            var tables = await eval.ExecuteQuery(TestHelpers.Parse(
                "SELECT val, ROW_NUMBER() OVER (ORDER BY val ASC) AS rn FROM #src WHERE flag = 0;").Statements[0]).ToListAsync();
            prunedRows = tables.SelectMany(t => t.Rows).ToList();
            prunedSpillBytes = eval.Telemetry.TotalSpilledBytes;
        }

        long fullSpillBytes;
        await using (var eval = CreateEvaluator())
        {
            eval.Connections["#src"] = new StreamingRowSource(rows,
                ("val", i => (int)(rows - i)),
                ("flag", i => (int)(i % 2)),
                ("unused1", _ => wide),
                ("unused2", _ => wide),
                ("unused3", _ => wide));
            eval.Telemetry.Clear();

            var tables = await eval.ExecuteQuery(TestHelpers.Parse(
                "SELECT val, flag, unused1, unused2, unused3, ROW_NUMBER() OVER (ORDER BY val ASC) AS rn FROM #src WHERE flag = 0;").Statements[0]).ToListAsync();
            Assert.Equal(rows / 2, tables.Sum(t => t.Rows.Count));
            fullSpillBytes = eval.Telemetry.TotalSpilledBytes;
        }

        Assert.Equal(rows / 2, prunedRows.Count);
        for (int i = 0; i < prunedRows.Count; i++)
            Assert.Equal((decimal)(i + 1), Convert.ToDecimal(prunedRows[i]["rn"], CultureInfo.InvariantCulture));
        Assert.True(prunedSpillBytes > 0, "Expected external window to spill.");
        Assert.True(prunedSpillBytes < fullSpillBytes / 2,
            $"Expected pruned window spill ({prunedSpillBytes}) to be less than half of full spill ({fullSpillBytes}).");
    }

    [Fact]
    public async Task ProjectionPruning_CountStarAndCorrelatedSubqueryStayCorrect()
    {
        await using var eval = CreateEvaluator();
        eval.Connections["#a"] = new StreamingRowSource(100,
            ("id", i => (int)(i + 1)),
            ("grp", i => (int)(i % 5)),
            ("extra", i => $"e{i}"));
        eval.Connections["#b"] = new StreamingRowSource(50,
            ("id", i => (int)(i * 2 + 1)));

        var countTables = await eval.ExecuteQuery(TestHelpers.Parse(
            "SELECT COUNT(*) AS n FROM #a;").Statements[0]).ToListAsync();
        Assert.Equal(100m, Convert.ToDecimal(countTables.SelectMany(t => t.Rows).Single()["n"], CultureInfo.InvariantCulture));

        var subTables = await eval.ExecuteQuery(TestHelpers.Parse(
            "SELECT a.grp FROM #a a WHERE EXISTS (SELECT 1 FROM #b b WHERE b.id = a.id) ORDER BY a.grp;").Statements[0]).ToListAsync();
        Assert.Equal(50, subTables.Sum(t => t.Rows.Count));
    }

    [Theory]
    [InlineData(128)] // Multi-batch: falls back to StringArray so new values in later batches stay lossless
    [InlineData(512)] // Single-batch: uses bounded Arrow DictionaryType for low-cardinality strings
    public async Task ArrowSpill_BoundedDictionaryEncoding_RoundTripsLowAndHighCardinalityStrings(int flushBatchRows)
    {
        await using var eval = CreateEvaluator();
        eval.SpillCompressionEnabled = false;
        eval.SpillEncryptionEnabled = false;

        var regions = new[]
        {
            "NORTH-AMERICA-EAST-REGION-01",
            "NORTH-AMERICA-WEST-REGION-02",
            "EUROPE-CENTRAL-REGION-03",
            "ASIA-PACIFIC-SOUTH-REGION-04",
        };

        var schema = new TableSchema(["id", "region", "unique_tag"]);
        const int rowCount = 400;
        var rows = new List<Row>(rowCount);
        for (int i = 0; i < rowCount; i++)
        {
            var r = new Row(schema);
            r["id"] = (decimal)i;
            // Introduce a new low-cardinality value in later batches plus periodic NULLs.
            r["region"] = i % 19 == 0
                ? null
                : i >= 250 && i % 7 == 0
                    ? "LATIN-AMERICA-SOUTH-REGION-05"
                    : regions[i % regions.Length];
            r["unique_tag"] = $"unique-payload-value-{i:D6}";
            rows.Add(r);
        }

        var chunk = $"sp07_dict_test_{flushBatchRows}.arrow";
        await using (var writer = await eval.SpillStore.CreateWriterAsync(chunk, flushBatchRows: flushBatchRows))
            await writer.WriteRowsAsync(rows);

        // Verify row readback
        var readRows = new List<Row>(rowCount);
        await using (var reader = await eval.SpillStore.CreateReaderAsync(chunk))
        {
            await foreach (var row in reader.AsEnumerableAsync())
                readRows.Add(row);
        }

        Assert.Equal(rowCount, readRows.Count);
        for (int i = 0; i < rowCount; i++)
        {
            Assert.Equal(rows[i]["id"], readRows[i]["id"]);
            Assert.Equal(rows[i]["region"], readRows[i]["region"]);
            Assert.Equal(rows[i]["unique_tag"], readRows[i]["unique_tag"]);
        }

        // Verify columnar batch readback
        var batchRegions = new List<string?>(rowCount);
        await using (var reader = await eval.SpillStore.CreateReaderAsync(chunk))
        {
            var columnar = Assert.IsAssignableFrom<ETL_SQL.Core.Spill.IColumnarSpillReader>(reader);
            await foreach (var batch in columnar.AsColumnBatchesAsync())
            {
                using (batch)
                {
                    var regionCol = batch.Columns[batch.Schema.GetOrdinal("region")];
                    for (int r = 0; r < batch.RowCount; r++)
                        batchRegions.Add(regionCol.IsNull(r) ? null : (string?)regionCol.GetBoxedValue(r));
                }
            }
        }
        Assert.Equal(rows.Select(r => (string?)r["region"]).ToArray(), batchRegions.ToArray());

        eval.SpillStore.DeleteChunk(chunk);
    }

    [Fact]
    public async Task ExternalSort_MultiPassIntermediateMerge_UsesColumnarBatchMergeAndPreservesOrder()
    {
        await using var eval = CreateEvaluator();
        eval.ExternalSortChunkSize = 5; // 700 rows -> 140 chunks (> MaxMergeFanIn 64) -> multi-pass merge
        var logger = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<ETL_SQL.Common.ILogger>();
        var engine = new ETL_SQL.Engine.Engines.ExternalSortEngine(eval, logger);

        var schema = new TableSchema(["grp", "val", "label"]);
        const int count = 700;
        var input = new List<Row>(count);
        for (int i = 0; i < count; i++)
        {
            var r = new Row(schema);
            r["grp"] = (decimal)(i % 7);
            r["val"] = i % 13 == 0 ? null : (decimal)((count - i) * 1.25m);
            r["label"] = i % 11 == 0 ? null : $"lbl-{i % 17:D2}";
            input.Add(r);
        }

        var orderBy = new List<ETL_SQL.Core.OrderByClause>
        {
            new(new ETL_SQL.Core.IdentifierExpression("grp"), descending: false),
            new(new ETL_SQL.Core.IdentifierExpression("val"), descending: true),
        };

        var sorted = await engine.SortExternal(input, orderBy);

        Assert.Equal(count, sorted.Count);
        Assert.True(engine.IntermediateMergeRunCount > 0, "Expected multi-pass intermediate merge.");
        Assert.True(engine.ColumnarIntermediateMergeRows > 0, "Expected intermediate merge to use columnar batch path.");

        // Verify non-decreasing grp, and within each grp non-increasing val (NULLs last for DESC).
        for (int i = 1; i < sorted.Count; i++)
        {
            var prevGrp = Convert.ToDecimal(sorted[i - 1]["grp"], CultureInfo.InvariantCulture);
            var currGrp = Convert.ToDecimal(sorted[i]["grp"], CultureInfo.InvariantCulture);
            Assert.True(prevGrp <= currGrp);
            if (prevGrp == currGrp)
            {
                var prevVal = sorted[i - 1]["val"];
                var currVal = sorted[i]["val"];
                if (prevVal != null && currVal != null)
                    Assert.True(Convert.ToDecimal(prevVal, CultureInfo.InvariantCulture) >= Convert.ToDecimal(currVal, CultureInfo.InvariantCulture));
            }
        }
    }

    [Fact]
    public async Task ExternalSort_SortSpillChunkAsync_UsesColumnarBatchInputAndMatchesStreamSort()
    {
        await using var eval = CreateEvaluator();
        eval.ExternalSortChunkSize = 10; // 750 rows -> 75 chunks -> multi-pass merge too
        var logger = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<ETL_SQL.Common.ILogger>();

        var schema = new TableSchema(["id", "k", "txt"]);
        const int count = 750;
        var rows = new List<Row>(count);
        for (int i = 0; i < count; i++)
        {
            var r = new Row(schema);
            r["id"] = (decimal)i;
            r["k"] = (decimal)((i * 37) % 101);
            r["txt"] = i % 5 == 0 ? null : $"v-{i % 23}";
            rows.Add(r);
        }

        const string chunk = "sp07_col_sort_input.arrow";
        await using (var writer = await eval.SpillStore.CreateWriterAsync(chunk, flushBatchRows: 64))
            await writer.WriteRowsAsync(rows);

        var orderBy = new List<ETL_SQL.Core.OrderByClause>
        {
            new(new ETL_SQL.Core.IdentifierExpression("k"), descending: false),
            new(new ETL_SQL.Core.IdentifierExpression("id"), descending: false),
        };

        var colEngine = new ETL_SQL.Engine.Engines.ExternalSortEngine(eval, logger);
        var colSorted = await colEngine.SortSpillChunkAsync(chunk, orderBy).ToListAsync();

        var streamEngine = new ETL_SQL.Engine.Engines.ExternalSortEngine(eval, logger);
        var streamSorted = await streamEngine.SortExternal(rows, orderBy);

        Assert.Equal(count, colSorted.Count);
        Assert.Equal(count, colEngine.ColumnarSortInputRows);
        Assert.True(colEngine.ColumnarIntermediateMergeRows > 0);
        Assert.Equal(
            streamSorted.Select(r => (Convert.ToDecimal(r["k"], CultureInfo.InvariantCulture), Convert.ToDecimal(r["id"], CultureInfo.InvariantCulture), (string?)r["txt"])).ToArray(),
            colSorted.Select(r => (Convert.ToDecimal(r["k"], CultureInfo.InvariantCulture), Convert.ToDecimal(r["id"], CultureInfo.InvariantCulture), (string?)r["txt"])).ToArray());

        eval.SpillStore.DeleteChunk(chunk);
    }
}
