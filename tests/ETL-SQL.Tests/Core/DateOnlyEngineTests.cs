using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Spill;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class DateOnlyEngineTests
{
    private static async Task<DataTable> RunAsync(string sql, int spillThresholdRows = 1000)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.TempTableSpillThresholdRows = spillThresholdRows;
        await evaluator.Evaluate(new Lexer(sql).TokenizeToScript());
        return evaluator.LastResult!;
    }

    [Fact]
    public async Task DateCastsToIsoDateWithoutTimeWhileDatetimeKeepsTime()
    {
        var table = await RunAsync(
            "SELECT " +
            "  CAST('2024-01-08' AS DATE) AS d, " +
            "  CAST(CAST('2024-01-08' AS DATE) AS STRING) AS d_text, " +
            "  CAST(CAST('2024-01-08 00:00:00' AS DATETIME) AS STRING) AS dt_text, " +
            "  TO_DATE('2024-01-08') AS parsed_date, " +
            "  CURRENT_DATE() AS today;");

        var row = Assert.Single(table.Rows);
        Assert.Equal(new DateOnly(2024, 1, 8), row["d"]);
        Assert.Equal("2024-01-08", row["d_text"]);
        Assert.Equal("2024-01-08 00:00:00", row["dt_text"]);
        Assert.Equal(new DateOnly(2024, 1, 8), row["parsed_date"]);
        Assert.IsType<DateOnly>(row["today"]);
    }

    [Fact]
    public async Task DateArithmeticComparisonsAndFunctionsWorkAcrossCultures()
    {
        var table = await RunAsync(
            "DECLARE @d DATE = '2024-01-08'; " +
            "SELECT " +
            "  @d + 2 AS plus_days, " +
            "  @d - 3 AS minus_days, " +
            "  @d + 0.5 AS plus_half_day, " +
            "  @d - CAST('2024-01-05' AS DATE) AS day_diff, " +
            "  EOMONTH(@d) AS month_end, " +
            "  START_OF_MONTH(@d) AS month_start, " +
            "  END_OF_QUARTER(@d) AS quarter_end, " +
            "  SAME_PERIOD_LAST_YEAR(@d) AS last_year, " +
            "  DATEADD(day, 5, @d) AS add_days, " +
            "  DATEADD(hour, 3, @d) AS add_hours, " +
            "  DATE_TRUNC(month, @d) AS trunc_month, " +
            "  FORMAT(@d, 'yyyy-MM-dd') AS fmt_date, " +
            "  FORMAT(@d, 'yyyy-MM-dd HH:mm:ss') AS fmt_datetime, " +
            "  CASE WHEN @d = CAST('2024-01-08 00:00:00' AS DATETIME) THEN 1 ELSE 0 END AS eq_midnight, " +
            "  CASE WHEN @d LIKE '2024-01-%' THEN 1 ELSE 0 END AS like_iso;");

        var row = Assert.Single(table.Rows);
        Assert.Equal(new DateOnly(2024, 1, 10), row["plus_days"]);
        Assert.Equal(new DateOnly(2024, 1, 5), row["minus_days"]);
        Assert.Equal(new DateTime(2024, 1, 8, 12, 0, 0), row["plus_half_day"]);
        Assert.Equal(3m, row["day_diff"]);
        Assert.Equal(new DateOnly(2024, 1, 31), row["month_end"]);
        Assert.Equal(new DateOnly(2024, 1, 1), row["month_start"]);
        Assert.Equal(new DateOnly(2024, 3, 31), row["quarter_end"]);
        Assert.Equal(new DateOnly(2023, 1, 8), row["last_year"]);
        Assert.Equal(new DateOnly(2024, 1, 13), row["add_days"]);
        Assert.Equal(new DateTime(2024, 1, 8, 3, 0, 0), row["add_hours"]);
        Assert.Equal(new DateOnly(2024, 1, 1), row["trunc_month"]);
        Assert.Equal("2024-01-08", row["fmt_date"]);
        Assert.Equal("2024-01-08 00:00:00", row["fmt_datetime"]);
        Assert.Equal(1m, Convert.ToDecimal(row["eq_midnight"], CultureInfo.InvariantCulture));
        Assert.Equal(1m, Convert.ToDecimal(row["like_iso"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task JoinsAndGroupingMatchDateAndMidnightDatetimeKeys()
    {
        var table = await RunAsync(
            "CREATE TABLE #dates (d DATE, label STRING); " +
            "INSERT INTO #dates VALUES ('2024-01-08', 'jan8'), ('2024-01-09', 'jan9'); " +
            "CREATE TABLE #times (ts DATETIME, qty INT); " +
            "INSERT INTO #times VALUES ('2024-01-08 00:00:00', 4), ('2024-01-08 00:00:00', 6); " +
            "SELECT d.d, SUM(t.qty) AS total_qty " +
            "FROM #dates d INNER JOIN #times t ON d.d = t.ts " +
            "GROUP BY d.d;");

        var row = Assert.Single(table.Rows);
        Assert.Equal(new DateOnly(2024, 1, 8), row["d"]);
        Assert.Equal(10m, Convert.ToDecimal(row["total_qty"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ColumnarBuffersSortJoinAndArrowSpillPreserveDateOnly()
    {
        var leftTable = new DataTable();
        leftTable.SetColumns(["d", "val"]);
        var r1 = leftTable.NewRow(); r1["d"] = new DateOnly(2024, 3, 15); r1["val"] = 10; leftTable.Rows.Add(r1);
        var r2 = leftTable.NewRow(); r2["d"] = new DateOnly(2024, 1, 8); r2["val"] = 20; leftTable.Rows.Add(r2);
        var r3 = leftTable.NewRow(); r3["d"] = null; r3["val"] = 30; leftTable.Rows.Add(r3);

        var schema = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["d"] = new ColumnDefinition("d", "DATE", false),
            ["val"] = new ColumnDefinition("val", "INT", false)
        };

        using var batch = ColumnBatchAdapter.FromDataTable(leftTable, schema);
        Assert.Equal(typeof(DateOnly), batch.GetColumn("d").ElementType);

        using var sortRun = ColumnBatchSortKernels.CreateRun<DateOnly>(
            batch, "d", descending: false, nullsFirst: false, cancellationToken: CancellationToken.None);
        Assert.Equal(new[] { 1, 0, 2 }, sortRun.Ordinals.ToArray());

        var spillDir = Path.Combine(Path.GetTempPath(), $"etlsql-dateonly-spill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(spillDir);
        try
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            evaluator.SpillFormat = "Arrow";
            evaluator.SessionRoot = spillDir;
            using var spill = new SpillStore(evaluator);

            await using (var writer = await spill.CreateWriterAsync("col_dates"))
            {
                await ((IColumnarSpillWriter)writer).WriteBatchAsync(batch);
            }

            await using (var reader = await spill.CreateReaderAsync("col_dates"))
            {
                var loaded = new List<ColumnBatch>();
                await foreach (var cb in ((IColumnarSpillReader)reader).AsColumnBatchesAsync())
                    loaded.Add(cb);
                try
                {
                    var roundTripped = ColumnBatchAdapter.ToDataTable(loaded[0]);
                    Assert.Equal(new DateOnly(2024, 3, 15), roundTripped.Rows[0]["d"]);
                    Assert.Equal(new DateOnly(2024, 1, 8), roundTripped.Rows[1]["d"]);
                    Assert.Null(roundTripped.Rows[2]["d"]);
                }
                finally
                {
                    foreach (var cb in loaded) cb.Dispose();
                }
            }

            // Dynamic first-row-null row spill must still restore row 1's DateOnly as DateOnly.
            var dynamicTable = new DataTable();
            dynamicTable.SetColumns(["d"]);
            var d0 = dynamicTable.NewRow(); d0["d"] = null; dynamicTable.Rows.Add(d0);
            var d1 = dynamicTable.NewRow(); d1["d"] = new DateOnly(2024, 5, 20); dynamicTable.Rows.Add(d1);

            await using (var writer = await spill.CreateWriterAsync("row_dates"))
            {
                foreach (var row in dynamicTable.Rows)
                    await writer.WriteRowAsync(row);
            }

            await using var rowReader = await spill.CreateReaderAsync("row_dates");
            var restoredRows = new List<Row>();
            await foreach (var row in rowReader.AsEnumerableAsync())
                restoredRows.Add(row);

            Assert.Equal(2, restoredRows.Count);
            Assert.Null(restoredRows[0]["d"]);
            Assert.Equal(new DateOnly(2024, 5, 20), Assert.IsType<DateOnly>(restoredRows[1]["d"]));
        }
        finally
        {
            Directory.Delete(spillDir, recursive: true);
        }
    }

    [Fact]
    public async Task CatalogRecursiveCteUniqueConstraintAndSumErrorHandleDateOnly()
    {
        var catalogTable = await RunAsync(
            "DECLARE @d DATE = '2024-01-08'; " +
            "SELECT data_type, value FROM eng.variables WHERE variable_name = '@d' OR variable_name = 'd';");
        var catRow = Assert.Single(catalogTable.Rows);
        Assert.Equal("DATE", catRow["data_type"]);
        Assert.Equal("2024-01-08", catRow["value"]);

        // Composite UNIQUE constraint on a DATE column uses PackedCompositeKey.
        var uniqueTable = await RunAsync(
            "CREATE TABLE #t (id INT, d DATE, UNIQUE (id, d)); " +
            "INSERT INTO #t VALUES (1, '2024-01-08'), (1, '2024-01-09'); " +
            "SELECT d FROM #t ORDER BY d;");
        Assert.Equal(2, uniqueTable.Rows.Count);
        Assert.Equal(new DateOnly(2024, 1, 8), uniqueTable.Rows[0]["d"]);
        Assert.Equal(new DateOnly(2024, 1, 9), uniqueTable.Rows[1]["d"]);

        // Recursive CTE preserves DateOnly across iterations.
        var cteTable = await RunAsync(
            "WITH days AS ( " +
            "  SELECT CAST('2024-01-08' AS DATE) AS d, 1 AS n " +
            "  UNION ALL " +
            "  SELECT DATEADD(day, 1, d), n + 1 FROM days WHERE n < 3 " +
            ") " +
            "SELECT d FROM days ORDER BY d;");
        Assert.Equal(3, cteTable.Rows.Count);
        Assert.Equal(new DateOnly(2024, 1, 8), cteTable.Rows[0]["d"]);
        Assert.Equal(new DateOnly(2024, 1, 9), cteTable.Rows[1]["d"]);
        Assert.Equal(new DateOnly(2024, 1, 10), cteTable.Rows[2]["d"]);

        // Numeric aggregation on a DATE column throws the descriptive date error.
        var sumEx = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
            RunAsync(
                "CREATE TABLE #s (d DATE); " +
                "INSERT INTO #s VALUES ('2024-01-08'); " +
                "SELECT SUM(d) FROM #s;"));
        Assert.Contains("Cannot perform numeric aggregation on a date value: '2024-01-08'", sumEx.Message, StringComparison.Ordinal);

        // WatermarkManager and CompiledSql handle DateOnly in ISO yyyy-MM-dd format.
        Assert.Equal("2024-01-08", ETL_SQL.Engine.Services.WatermarkManager.FormatWatermarkValue(new DateOnly(2024, 1, 8)));
        Assert.True(ETL_SQL.Engine.Services.WatermarkManager.CompareWatermarkValues(new DateOnly(2024, 1, 8), new DateOnly(2024, 1, 9)) < 0);

        var compiled = new CompiledSql("SELECT * FROM t WHERE d = @p0", new Dictionary<string, object?> { ["@p0"] = new DateOnly(2024, 1, 8) });
        Assert.Equal("SELECT * FROM t WHERE d = '2024-01-08'", compiled.ToEscapedSql());
    }
}
