using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public class TransformCorrectnessTests
{
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
}
