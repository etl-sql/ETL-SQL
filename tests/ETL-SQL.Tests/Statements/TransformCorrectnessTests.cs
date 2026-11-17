using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Statements;

[Trait("CompatBreak", "0.23")]
public class TransformCorrectnessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagedRowsRemainReservedUntilOutputCompletes(bool failWrite)
    {
        var evaluator = await RunScriptAsync("CREATE TABLE #src (Id INT); INSERT INTO #src VALUES (1), (1), (2);");
        var arbiter = new ETL_SQL.Core.MemoryGrantArbiter(1024 * 1024);
        evaluator.MemoryArbiter = arbiter;
        var target = new Mock<IDataSource>();
        target.Setup(t => t.TruncateAsync()).Returns(Task.CompletedTask);
        bool wrote = false;
        target.Setup(t => t.WriteBatches(It.IsAny<IAsyncEnumerable<DataTable>>(), true, It.IsAny<CancellationToken>()))
            .Returns(async (IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
            {
                Assert.True(arbiter.ReservedBytes > 0, "Staged source rows must remain reserved during output.");
                var rows = (await batches.ToListAsync()).SelectMany(batch => batch.Rows).ToList();
                Assert.Equal([1m, 2m], rows.Select(row => Convert.ToDecimal(row["Id"])));
                wrote = true;
                if (failWrite) throw new ExecutionException("Output failed.");
            });
        evaluator.Connections["#out"] = target.Object;
        const string sql = "TRANSFORM #out FROM #src USING DEDUPLICATE (KEY_COLS = 'Id');";
        var ast = new Parser(new Lexer(sql).Tokenize()).Parse();
        if (failWrite)
            await Assert.ThrowsAsync<ExecutionException>(() => evaluator.Evaluate(ast));
        else
            await evaluator.Evaluate(ast);
        Assert.True(wrote);
        Assert.Equal(0, arbiter.ReservedBytes);
    }

    [Theory]
    [InlineData("HOUR")]
    [InlineData("DAY")]
    [InlineData("WEEK")]
    [InlineData("MONTH")]
    [InlineData("QUARTER")]
    [InlineData("YEAR")]
    public async Task FillDates_StopsWithinTheRepresentableDateRange(string interval)
    {
        var evaluator = await RunScriptAsync($"""
            CREATE TABLE #src (Dt DATE, Val INT);
            INSERT INTO #src VALUES ('9999-12-31', 42);
            TRANSFORM #out FROM #src USING FILL_DATES (DATE_COL = 'Dt', INTERVAL = '{interval}');
            """);
        var row = Assert.Single(await ReadRowsAsync(evaluator, "#out"));
        Assert.Equal(new DateOnly(9999, 12, 31), row["Dt"]);
        Assert.Equal(42m, row["Val"]);
    }

    [Theory]
    [InlineData("('a', 'Region', 10)")]
    [InlineData("('a', NULL, 10), ('a', 'NULL', 20)")]
    public async Task Pivot_RejectsAmbiguousOutputColumnsBeforeChangingTheTarget(string values)
    {
        var evaluator = await RunScriptAsync($"""
            CREATE TABLE #src (Region VARCHAR, Category VARCHAR, Val INT);
            INSERT INTO #src VALUES {values};
            CREATE TABLE #out (Sentinel INT);
            INSERT INTO #out VALUES (99);
            """);
        const string transform = "TRANSFORM #out FROM #src USING PIVOT (ROW_FIELDS = 'Region', PIVOT_FIELD = 'Category', VALUE_FIELD = 'Val');";
        var error = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(transform).Tokenize()).Parse()));
        Assert.Contains("output column", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(99m, Assert.Single(await ReadRowsAsync(evaluator, "#out"))["Sentinel"]);
    }

    private static async Task<Evaluator> RunScriptAsync(string script)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);
        return evaluator;
    }

    private static async Task<List<Row>> ReadRowsAsync(Evaluator evaluator, string tableName)
    {
        var table = evaluator.Connections[tableName] as InMemoryDataSource;
        Assert.NotNull(table);
        var batches = await table.ReadBatches().ToListAsync();
        return batches.SelectMany(b => b.Rows).ToList();
    }

    [Fact]
    public async Task Normalize_MinMaxAndZScore_PreserveNullAndExcludeFromGroupStats()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #src (Id INT, Val DECIMAL(18, 2));
INSERT INTO #src VALUES (1, 10), (2, NULL), (3, 30);

TRANSFORM #minmax
FROM #src
USING NORMALIZE (
  VALUE_COL = 'Val',
  METHOD = 'MIN_MAX'
);

TRANSFORM #zscore
FROM #src
USING NORMALIZE (
  VALUE_COL = 'Val',
  METHOD = 'Z_SCORE'
);
");
        var minmaxRows = await ReadRowsAsync(evaluator, "#minmax");
        Assert.Equal(3, minmaxRows.Count);
        Assert.Equal(0m, Convert.ToDecimal(minmaxRows.Single(r => Convert.ToInt32(r["Id"]) == 1)["Val_Normalized"]));
        Assert.Null(minmaxRows.Single(r => Convert.ToInt32(r["Id"]) == 2)["Val_Normalized"]);
        Assert.Equal(1m, Convert.ToDecimal(minmaxRows.Single(r => Convert.ToInt32(r["Id"]) == 3)["Val_Normalized"]));

        var zscoreRows = await ReadRowsAsync(evaluator, "#zscore");
        Assert.Equal(3, zscoreRows.Count);
        Assert.Equal(-1m, Convert.ToDecimal(zscoreRows.Single(r => Convert.ToInt32(r["Id"]) == 1)["Val_Normalized"]));
        Assert.Null(zscoreRows.Single(r => Convert.ToInt32(r["Id"]) == 2)["Val_Normalized"]);
        Assert.Equal(1m, Convert.ToDecimal(zscoreRows.Single(r => Convert.ToInt32(r["Id"]) == 3)["Val_Normalized"]));
    }

    [Fact]
    public async Task ShareOfTotal_PreservesNullAndExcludesNullFromTotal()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #src (Item VARCHAR(10), Val DECIMAL(18, 2));
INSERT INTO #src VALUES ('A', 10), ('B', NULL), ('C', 30);

TRANSFORM #shares
FROM #src
USING SHARE_OF_TOTAL (
  VALUE_COL = 'Val'
);
");
        var rows = await ReadRowsAsync(evaluator, "#shares");
        Assert.Equal(3, rows.Count);
        Assert.Equal(0.25m, Convert.ToDecimal(rows.Single(r => r["Item"]?.ToString() == "A")["Val_Share"]));
        Assert.Null(rows.Single(r => r["Item"]?.ToString() == "B")["Val_Share"]);
        Assert.Equal(0.75m, Convert.ToDecimal(rows.Single(r => r["Item"]?.ToString() == "C")["Val_Share"]));
    }

    [Fact]
    public async Task PeriodComparison_HandlesNullDuplicateDatesAndMonthEnd()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #monthly (Dt DATE, Val DECIMAL(18, 2));
INSERT INTO #monthly VALUES
  ('2026-01-31', 100),
  ('2026-02-28', 150),
  ('2026-03-31', NULL),
  ('2026-04-30', 200);

TRANSFORM #mom
FROM #monthly
USING PERIOD_COMPARISON (
  DATE_COL = 'Dt',
  VALUE_COL = 'Val',
  PERIOD = 'MONTH'
);

CREATE TABLE #dupes (Dt DATE, Val DECIMAL(18, 2));
INSERT INTO #dupes VALUES
  ('2026-01-01', 60),
  ('2026-01-01', 40),
  ('2026-01-02', 125);

TRANSFORM #dupes_dod
FROM #dupes
USING PERIOD_COMPARISON (
  DATE_COL = 'Dt',
  VALUE_COL = 'Val',
  PERIOD = 'DAY'
);
");
        var momRows = await ReadRowsAsync(evaluator, "#mom");
        Assert.Equal(4, momRows.Count);
        var feb = momRows.Single(r => r["Dt"] is DateOnly d && d == new DateOnly(2026, 2, 28));
        Assert.Equal(50m, Convert.ToDecimal(feb["Val_Diff"]));
        Assert.Equal(50m, Convert.ToDecimal(feb["Val_Pct"]));

        var mar = momRows.Single(r => r["Dt"] is DateOnly d && d == new DateOnly(2026, 3, 31));
        Assert.Null(mar["Val_Diff"]);
        Assert.Null(mar["Val_Pct"]);

        var apr = momRows.Single(r => r["Dt"] is DateOnly d && d == new DateOnly(2026, 4, 30));
        Assert.Null(apr["Val_Diff"]);
        Assert.Null(apr["Val_Pct"]);

        var dupeRows = await ReadRowsAsync(evaluator, "#dupes_dod");
        Assert.Equal(3, dupeRows.Count);
        var jan2 = dupeRows.Single(r => r["Dt"] is DateOnly d && d == new DateOnly(2026, 1, 2));
        Assert.Equal(25m, Convert.ToDecimal(jan2["Val_Diff"]));
        Assert.Equal(25m, Convert.ToDecimal(jan2["Val_Pct"]));
    }

    [Fact]
    public async Task TopNOthers_AlignsGrainAndHandlesNullCategoriesAndValues()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #sales (Cat VARCHAR(20), Detail VARCHAR(20), Val DECIMAL(18, 2));
INSERT INTO #sales VALUES
  ('A', 'd1', 10),
  ('A', 'd2', 20),
  ('B', 'd3', -5),
  ('C', 'd4', 2),
  ('D', 'd5', NULL);

TRANSFORM #top
FROM #sales
USING TOP_N_OTHERS (
  N = 2,
  VALUE_COL = 'Val',
  CATEGORY_COL = 'Cat'
);
");
        var rows = await ReadRowsAsync(evaluator, "#top");
        // Aggregated grain: A (30), C (2) are top 2; B (-5) + D (NULL) -> Others (-5).
        Assert.Equal(3, rows.Count);
        var aRow = rows.Single(r => r["Cat"]?.ToString() == "A");
        Assert.Equal(30m, Convert.ToDecimal(aRow["Val"]));
        Assert.Null(aRow["Detail"]);

        var cRow = rows.Single(r => r["Cat"]?.ToString() == "C");
        Assert.Equal(2m, Convert.ToDecimal(cRow["Val"]));

        var othersRow = rows.Single(r => r["Cat"]?.ToString() == "Others");
        Assert.Equal(-5m, Convert.ToDecimal(othersRow["Val"]));
    }

    [Fact]
    public async Task FillDates_PreservesDuplicateDateRowsWithoutDataLoss()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #src (Grp VARCHAR(10), Dt DATE, Val INT);
INSERT INTO #src VALUES
  ('A', '2026-01-01', 10),
  ('A', '2026-01-01', 15),
  ('A', '2026-01-03', 30);

TRANSFORM #filled
FROM #src
USING FILL_DATES (
  DATE_COL = 'Dt',
  BY_GROUP = 'Grp',
  GAPS_FILL = 0
);
");
        var rows = await ReadRowsAsync(evaluator, "#filled");
        Assert.Equal(4, rows.Count);
        var jan1Vals = rows
            .Where(r => r["Dt"] is DateOnly d && d == new DateOnly(2026, 1, 1))
            .Select(r => Convert.ToInt32(r["Val"]))
            .ToList();
        Assert.Equal([10, 15], jan1Vals);

        var jan2 = rows.Single(r => r["Dt"] is DateOnly d && d == new DateOnly(2026, 1, 2));
        Assert.Equal(0, Convert.ToInt32(jan2["Val"]));
    }

    [Fact]
    public async Task InterpolateAndRolling_AcceptIsoDateStringsAndRejectNullOrderOrInvalidWindow()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #src (DtStr VARCHAR(20), Val DECIMAL(18, 2));
INSERT INTO #src VALUES
  ('2026-01-01', 10),
  ('2026-01-02', NULL),
  ('2026-01-03', 30);

TRANSFORM #interp
FROM #src
USING INTERPOLATE (
  VALUE_COL = 'Val',
  ORDER_COL = 'DtStr',
  METHOD = 'LINEAR'
);

TRANSFORM #roll
FROM #interp
USING ROLLING_AGGREGATE (
  VALUE_COL = 'Val',
  ORDER_COL = 'DtStr',
  WINDOW_SIZE = 2,
  AGGREGATE = 'AVG'
);
");
        var interpRows = await ReadRowsAsync(evaluator, "#interp");
        Assert.Equal(20m, Convert.ToDecimal(interpRows.Single(r => r["DtStr"]?.ToString() == "2026-01-02")["Val"]));

        var rollRows = await ReadRowsAsync(evaluator, "#roll");
        Assert.Equal(25m, Convert.ToDecimal(rollRows.Single(r => r["DtStr"]?.ToString() == "2026-01-03")["Val_Rolling"]));

        await Assert.ThrowsAsync<ExecutionException>(() => RunScriptAsync(@"
CREATE TABLE #bad_order (Seq INT, Val INT);
INSERT INTO #bad_order VALUES (NULL, 10), (2, 20);
TRANSFORM #out FROM #bad_order USING INTERPOLATE (VALUE_COL = 'Val', ORDER_COL = 'Seq');
"));

        await Assert.ThrowsAsync<ExecutionException>(() => RunScriptAsync(@"
CREATE TABLE #bad_win (Seq INT, Val INT);
INSERT INTO #bad_win VALUES (1, 10);
TRANSFORM #out FROM #bad_win USING ROLLING_AGGREGATE (VALUE_COL = 'Val', ORDER_COL = 'Seq', WINDOW_SIZE = 0);
"));
    }

    [Fact]
    public async Task WhatIf_DoesNotTruncateOrWriteTargetTable()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #src (Id INT, Val INT);
INSERT INTO #src VALUES (1, 10), (2, 20);

CREATE TABLE #target (Id INT, Val INT);
INSERT INTO #target VALUES (99, 999);

SET WHAT_IF ON;
TRANSFORM #target
FROM #src
USING NORMALIZE (VALUE_COL = 'Val');
TRANSFORM #new_target
FROM #src
USING NORMALIZE (VALUE_COL = 'Val');
SET WHAT_IF OFF;
");
        var targetRows = await ReadRowsAsync(evaluator, "#target");
        Assert.Single(targetRows);
        Assert.Equal(99, Convert.ToInt32(targetRows[0]["Id"]));
        Assert.False(evaluator.Connections.ContainsKey("#new_target"));
    }

    [Fact]
    public async Task InPlaceTransform_DefaultsSourceToTarget()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #t (Item VARCHAR(10), Val INT);
INSERT INTO #t VALUES ('A', 10), ('B', 30);

TRANSFORM #t
USING SHARE_OF_TOTAL (
  VALUE_COL = 'Val'
);
");
        var rows = await ReadRowsAsync(evaluator, "#t");
        Assert.Equal(2, rows.Count);
        Assert.Equal(0.25m, Convert.ToDecimal(rows.Single(r => r["Item"]?.ToString() == "A")["Val_Share"]));
        Assert.Equal(0.75m, Convert.ToDecimal(rows.Single(r => r["Item"]?.ToString() == "B")["Val_Share"]));
    }

    [Fact]
    public async Task Validation_RejectsUnknownOptionsAndInvalidByGroupColumns()
    {
        await Assert.ThrowsAsync<ExecutionException>(() => RunScriptAsync(@"
CREATE TABLE #t (Id INT, Val INT);
INSERT INTO #t VALUES (1, 10);
TRANSFORM #out FROM #t USING NORMALIZE (VALUE_COL = 'Val', BOGUS = 'X');
"));

        await Assert.ThrowsAsync<ExecutionException>(() => RunScriptAsync(@"
CREATE TABLE #t (Id INT, Val INT);
INSERT INTO #t VALUES (1, 10);
TRANSFORM #out FROM #t USING SHARE_OF_TOTAL (VALUE_COL = 'Val', BY_GROUP = 'MissingCol');
"));

        await Assert.ThrowsAsync<ExecutionException>(() => RunScriptAsync(@"
CREATE TABLE #t (Cat VARCHAR(10), Val INT);
INSERT INTO #t VALUES ('A', 10);
TRANSFORM #out FROM #t USING TOP_N_OTHERS (VALUE_COL = 'Val', CATEGORY_COL = 'Cat', BY_GROUP = 'MissingCol');
"));
    }

    [Fact]
    public async Task GroupKey_DistinguishesNullFromEmptyString_AndDeduplicateTieOrderIsStable()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #t (Grp VARCHAR(10), Seq INT, Val INT);
INSERT INTO #t VALUES
  (NULL, 1, 100),
  ('', 1, 200),
  ('X', 1, 10),
  ('X', 1, 20),
  ('X', 1, 30);

TRANSFORM #dedup_first
FROM #t
USING DEDUPLICATE (
  KEY_COLS = 'Grp',
  ORDER_BY = 'Seq ASC',
  KEEP = 'FIRST'
);

TRANSFORM #dedup_last
FROM #t
USING DEDUPLICATE (
  KEY_COLS = 'Grp',
  ORDER_BY = 'Seq ASC',
  KEEP = 'LAST'
);
");
        var firstRows = await ReadRowsAsync(evaluator, "#dedup_first");
        Assert.Equal(3, firstRows.Count);
        Assert.Equal(100, Convert.ToInt32(firstRows.Single(r => r["Grp"] == null || r["Grp"] == DBNull.Value)["Val"]));
        Assert.Equal(200, Convert.ToInt32(firstRows.Single(r => r["Grp"] is string s && s == "")["Val"]));
        Assert.Equal(10, Convert.ToInt32(firstRows.Single(r => r["Grp"]?.ToString() == "X")["Val"]));

        var lastRows = await ReadRowsAsync(evaluator, "#dedup_last");
        Assert.Equal(3, lastRows.Count);
        Assert.Equal(30, Convert.ToInt32(lastRows.Single(r => r["Grp"]?.ToString() == "X")["Val"]));
    }

    [Fact]
    public async Task OutputSchema_PreservesSourceColumnTypesAndTypedComputedColumns()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #src (Id INT, Dt DATE, Label VARCHAR(20), Val DECIMAL(18, 2));
INSERT INTO #src VALUES
  (1, '2026-01-01', 'A', 10.00),
  (2, '2026-01-03', 'B', 30.00);

TRANSFORM #filled
FROM #src
USING FILL_DATES (
  DATE_COL = 'Dt',
  GAPS_FILL = 0
);

TRANSFORM #period
FROM #src
USING PERIOD_COMPARISON (
  DATE_COL = 'Dt',
  VALUE_COL = 'Val',
  PERIOD = 'DAY'
);

TRANSFORM #pivoted
FROM #src
USING PIVOT (
  ROW_FIELDS = 'Id',
  PIVOT_FIELD = 'Label',
  VALUE_FIELD = 'Val',
  AGGREGATE = 'COUNT'
);
");
        var filled = Assert.IsType<InMemoryDataSource>(evaluator.Connections["#filled"]);
        Assert.Equal("INT", filled.Schema["Id"].DataType);
        Assert.Equal("DATE", filled.Schema["Dt"].DataType);
        Assert.Equal("VARCHAR(20)", filled.Schema["Label"].DataType);
        Assert.Equal("DECIMAL(18,2)", filled.Schema["Val"].DataType);

        var period = Assert.IsType<InMemoryDataSource>(evaluator.Connections["#period"]);
        Assert.Equal("INT", period.Schema["Id"].DataType);
        Assert.Equal("DECIMAL(18,2)", period.Schema["Val"].DataType);
        Assert.Equal("DECIMAL", period.Schema["Val_Diff"].DataType);
        Assert.Equal("DECIMAL", period.Schema["Val_Pct"].DataType);

        var pivoted = Assert.IsType<InMemoryDataSource>(evaluator.Connections["#pivoted"]);
        Assert.Equal("INT", pivoted.Schema["Id"].DataType);
        Assert.Equal("INT", pivoted.Schema["A"].DataType);
        Assert.Equal("INT", pivoted.Schema["B"].DataType);
    }

    [Fact]
    public async Task FillDates_SupportsIntervalsStartEndAndForwardFill()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #hourly (Ts DATETIME, Reading DECIMAL(18, 2));
INSERT INTO #hourly VALUES
  ('2026-01-01 09:00:00', 10),
  ('2026-01-01 11:00:00', 30);

TRANSFORM #hourly_filled
FROM #hourly
USING FILL_DATES (
  DATE_COL = 'Ts',
  INTERVAL = 'HOUR',
  START_DATE = '2026-01-01 08:00:00',
  END_DATE = '2026-01-01 12:00:00',
  GAPS_FILL = 'FORWARD'
);

CREATE TABLE #monthly (Dt DATE, Val DECIMAL(18, 2));
INSERT INTO #monthly VALUES
  ('2026-01-31', 100),
  ('2026-03-31', 300);

TRANSFORM #monthly_filled
FROM #monthly
USING FILL_DATES (
  DATE_COL = 'Dt',
  INTERVAL = 'MONTH',
  GAPS_FILL = 'FORWARD'
);

CREATE TABLE #quarterly (Dt DATE, Val INT);
INSERT INTO #quarterly VALUES
  ('2026-01-01', 1),
  ('2026-07-01', 3);

TRANSFORM #quarterly_filled
FROM #quarterly
USING FILL_DATES (
  DATE_COL = 'Dt',
  INTERVAL = 'QUARTER',
  END_DATE = '2026-10-01',
  GAPS_FILL = -1
);
");
        var hourlyRows = await ReadRowsAsync(evaluator, "#hourly_filled");
        Assert.Equal(5, hourlyRows.Count);
        Assert.Null(hourlyRows[0]["Reading"]); // 08:00 leading gap
        Assert.Equal(10m, Convert.ToDecimal(hourlyRows[1]["Reading"])); // 09:00
        Assert.Equal(10m, Convert.ToDecimal(hourlyRows[2]["Reading"])); // 10:00 forward-filled
        Assert.Equal(new DateTime(2026, 1, 1, 10, 0, 0), Assert.IsType<DateTime>(hourlyRows[2]["Ts"]));
        Assert.Equal(30m, Convert.ToDecimal(hourlyRows[3]["Reading"])); // 11:00
        Assert.Equal(30m, Convert.ToDecimal(hourlyRows[4]["Reading"])); // 12:00 forward-filled

        var monthlyRows = await ReadRowsAsync(evaluator, "#monthly_filled");
        Assert.Equal(3, monthlyRows.Count);
        Assert.Equal(new DateOnly(2026, 2, 28), Assert.IsType<DateOnly>(monthlyRows[1]["Dt"]));
        Assert.Equal(100m, Convert.ToDecimal(monthlyRows[1]["Val"]));

        var quarterlyRows = await ReadRowsAsync(evaluator, "#quarterly_filled");
        Assert.Equal(4, quarterlyRows.Count);
        Assert.Equal(new DateOnly(2026, 4, 1), Assert.IsType<DateOnly>(quarterlyRows[1]["Dt"]));
        Assert.Equal(-1m, Convert.ToDecimal(quarterlyRows[1]["Val"]));
        Assert.Equal(new DateOnly(2026, 10, 1), Assert.IsType<DateOnly>(quarterlyRows[3]["Dt"]));
        Assert.Equal(-1m, Convert.ToDecimal(quarterlyRows[3]["Val"]));

        await Assert.ThrowsAsync<ExecutionException>(() => RunScriptAsync(@"
CREATE TABLE #bad (Dt DATE, Val INT);
INSERT INTO #bad VALUES ('2026-01-05', 1);
TRANSFORM #bad_out FROM #bad USING FILL_DATES (
  DATE_COL = 'Dt',
  START_DATE = '2026-02-01',
  END_DATE = '2026-01-01'
);
"));
    }

    [Fact]
    public async Task InterpolateAndNormalize_SupportCommaSeparatedValueCols()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #series (Seq INT, Temp DECIMAL(18, 2), Humidity DECIMAL(18, 2));
INSERT INTO #series VALUES
  (1, 10, 40),
  (2, NULL, NULL),
  (3, 30, 80);

TRANSFORM #interp_multi
FROM #series
USING INTERPOLATE (
  VALUE_COLS = 'Temp, Humidity',
  ORDER_COL = 'Seq',
  METHOD = 'LINEAR'
);

TRANSFORM #norm_multi
FROM #interp_multi
USING NORMALIZE (
  VALUE_COLS = 'Temp, Humidity',
  METHOD = 'MIN_MAX'
);
");
        var interpRows = await ReadRowsAsync(evaluator, "#interp_multi");
        var mid = interpRows.Single(r => Convert.ToDecimal(r["Seq"]) == 2m);
        Assert.Equal(20m, Convert.ToDecimal(mid["Temp"]));
        Assert.Equal(60m, Convert.ToDecimal(mid["Humidity"]));

        var normRows = await ReadRowsAsync(evaluator, "#norm_multi");
        var midNorm = normRows.Single(r => Convert.ToDecimal(r["Seq"]) == 2m);
        Assert.Equal(0.5m, Convert.ToDecimal(midNorm["Temp_Normalized"]));
        Assert.Equal(0.5m, Convert.ToDecimal(midNorm["Humidity_Normalized"]));

        await Assert.ThrowsAsync<ExecutionException>(() => RunScriptAsync(@"
CREATE TABLE #t (Id INT, A INT, B INT);
INSERT INTO #t VALUES (1, 10, 20);
TRANSFORM #out FROM #t USING NORMALIZE (VALUE_COL = 'A', VALUE_COLS = 'A, B');
"));
    }

    [Fact]
    public async Task PeriodComparison_SupportsWeekAndQuarter()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #weekly (Dt DATE, Val DECIMAL(18, 2));
INSERT INTO #weekly VALUES
  ('2026-01-05', 100),
  ('2026-01-12', 125);

TRANSFORM #wow
FROM #weekly
USING PERIOD_COMPARISON (
  DATE_COL = 'Dt',
  VALUE_COL = 'Val',
  PERIOD = 'WEEK'
);

CREATE TABLE #qtr (Dt DATE, Val DECIMAL(18, 2));
INSERT INTO #qtr VALUES
  ('2025-11-30', 200),
  ('2026-02-28', 250);

TRANSFORM #qoq
FROM #qtr
USING PERIOD_COMPARISON (
  DATE_COL = 'Dt',
  VALUE_COL = 'Val',
  PERIOD = 'QUARTER'
);
");
        var wowRows = await ReadRowsAsync(evaluator, "#wow");
        var jan12 = wowRows.Single(r => r["Dt"] is DateOnly d && d == new DateOnly(2026, 1, 12));
        Assert.Equal(25m, Convert.ToDecimal(jan12["Val_Diff"]));
        Assert.Equal(25m, Convert.ToDecimal(jan12["Val_Pct"]));

        var qoqRows = await ReadRowsAsync(evaluator, "#qoq");
        var feb28 = qoqRows.Single(r => r["Dt"] is DateOnly d && d == new DateOnly(2026, 2, 28));
        Assert.Equal(50m, Convert.ToDecimal(feb28["Val_Diff"]));
        Assert.Equal(25m, Convert.ToDecimal(feb28["Val_Pct"]));
    }

    [Fact]
    public async Task Deduplicate_SupportsRejectIntoAndKeepUniqueOnly()
    {
        var evaluator = await RunScriptAsync(@"
CREATE TABLE #src (Id INT, Seq INT, Tag VARCHAR(10));
INSERT INTO #src VALUES
  (1, 1, 'a1'),
  (2, 1, 'b1'),
  (2, 2, 'b2'),
  (3, 1, 'c1'),
  (3, 2, 'c2');

TRANSFORM #kept_first
FROM #src
USING DEDUPLICATE (
  KEY_COLS = 'Id',
  ORDER_BY = 'Seq ASC',
  KEEP = 'FIRST',
  REJECT_INTO = #rejected_first
);

TRANSFORM #kept_unique
FROM #src
USING DEDUPLICATE (
  KEY_COLS = 'Id',
  KEEP = 'UNIQUE_ONLY',
  REJECT_INTO = #rejected_unique
);
");
        var keptFirst = await ReadRowsAsync(evaluator, "#kept_first");
        Assert.Equal(3, keptFirst.Count);
        var rejectedFirst = await ReadRowsAsync(evaluator, "#rejected_first");
        Assert.Equal(2, rejectedFirst.Count);
        Assert.Equal(["b2", "c2"], rejectedFirst.Select(r => r["Tag"]?.ToString()).OrderBy(x => x));

        var keptUnique = await ReadRowsAsync(evaluator, "#kept_unique");
        var onlyUnique = Assert.Single(keptUnique);
        Assert.Equal(1m, Convert.ToDecimal(onlyUnique["Id"]));

        var rejectedUnique = await ReadRowsAsync(evaluator, "#rejected_unique");
        Assert.Equal(4, rejectedUnique.Count);
        Assert.Equal(["b1", "b2", "c1", "c2"], rejectedUnique.Select(r => r["Tag"]?.ToString()).OrderBy(x => x));
    }
}
