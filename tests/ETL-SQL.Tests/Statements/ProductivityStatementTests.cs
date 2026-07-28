using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public class ProductivityStatementTests
{
    [Fact]
    public async Task TestGenerateCalendarStatement()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
GENERATE CALENDAR FROM '2026-05-01' TO '2026-05-05' INTO #calendar;
SELECT COUNT(*) AS TotalDays FROM #calendar;
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var table = evaluator.Connections["#calendar"] as InMemoryDataSource;
        Assert.NotNull(table);

        var batches = await table.ReadBatches().ToListAsync();
        Assert.Single(batches);
        Assert.Equal(5, batches[0].Rows.Count);

        var firstRow = batches[0].Rows[0];
        Assert.Equal(20260501, Convert.ToInt32(firstRow["DateKey"]));
        Assert.Equal(2026, Convert.ToInt32(firstRow["Year"]));
        Assert.Equal(5, Convert.ToInt32(firstRow["Month"]));
        Assert.Equal("May", firstRow["MonthName"]?.ToString());
    }

    [Fact]
    public async Task TestCompareDatasetsStatement()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
-- Baseline dataset
CREATE TABLE #yesterday (Id INT, Name VARCHAR(50), Email VARCHAR(50));
INSERT INTO #yesterday VALUES (1, 'Alice', 'alice@old.com');
INSERT INTO #yesterday VALUES (2, 'Bob', 'bob@test.com');
INSERT INTO #yesterday VALUES (3, 'Charlie', 'charlie@test.com');

-- Today dataset
CREATE TABLE #today (Id INT, Name VARCHAR(50), Email VARCHAR(50));
INSERT INTO #today VALUES (1, 'Alice', 'alice@new.com'); -- UPDATE
INSERT INTO #today VALUES (2, 'Bob', 'bob@test.com');    -- UNCHANGED
INSERT INTO #today VALUES (4, 'David', 'david@test.com'); -- INSERT
-- (3, Charlie) is DELETED

COMPARE DATASETS #today WITH #yesterday KEY (Id) INTO #diff;
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var diffDs = evaluator.Connections["#diff"] as InMemoryDataSource;
        Assert.NotNull(diffDs);

        var batches = await diffDs.ReadBatches().ToListAsync();
        Assert.Single(batches);

        var rows = batches[0].Rows;
        Assert.Equal(3, rows.Count); // 1 INSERT, 1 UPDATE, 1 DELETE

        var updateRow = rows.FirstOrDefault(r => r["Id"]?.ToString() == "1");
        Assert.NotNull(updateRow);
        Assert.Equal("UPDATE", updateRow["_change_type"]?.ToString());
        Assert.Equal("Email", updateRow["_changed_columns"]?.ToString());
        Assert.Equal("alice@old.com", updateRow["Email_old"]?.ToString());
        Assert.Equal("alice@new.com", updateRow["Email_new"]?.ToString());

        var insertRow = rows.FirstOrDefault(r => r["Id"]?.ToString() == "4");
        Assert.NotNull(insertRow);
        Assert.Equal("INSERT", insertRow["_change_type"]?.ToString());
        Assert.Null(insertRow["Email_old"]);
        Assert.Equal("david@test.com", insertRow["Email_new"]?.ToString());

        var deleteRow = rows.FirstOrDefault(r => r["Id"]?.ToString() == "3");
        Assert.NotNull(deleteRow);
        Assert.Equal("DELETE", deleteRow["_change_type"]?.ToString());
        Assert.Equal("charlie@test.com", deleteRow["Email_old"]?.ToString());
        Assert.Null(deleteRow["Email_new"]);
    }



    [Fact]
    public async Task TestTransformFillDates()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #sales (Region VARCHAR(20), OrderDate DATE, Quantity INT);
INSERT INTO #sales VALUES ('East', '2026-05-01', 10);
INSERT INTO #sales VALUES ('East', '2026-05-03', 30);
INSERT INTO #sales VALUES ('West', '2026-05-02', 5);
INSERT INTO #sales VALUES ('West', '2026-05-04', 15);

TRANSFORM #filled
FROM #sales
USING FILL_DATES (
  DATE_COL = 'OrderDate',
  GAPS_FILL = 0,
  BY_GROUP = 'Region'
);
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var filled = evaluator.Connections["#filled"] as InMemoryDataSource;
        Assert.NotNull(filled);

        var batches = await filled.ReadBatches().ToListAsync();
        var rows = batches.SelectMany(b => b.Rows).ToList();

        Assert.Equal(6, rows.Count);

        var eastGap = rows.Single(r =>
            r["Region"]?.ToString() == "East"
            && Convert.ToDateTime(r["OrderDate"]).Date == new DateTime(2026, 5, 2));
        Assert.Equal(0, Convert.ToInt32(eastGap["Quantity"]));

        var westGap = rows.Single(r =>
            r["Region"]?.ToString() == "West"
            && Convert.ToDateTime(r["OrderDate"]).Date == new DateTime(2026, 5, 3));
        Assert.Equal(0, Convert.ToInt32(westGap["Quantity"]));
    }

    [Fact]
    public async Task TestTransformInterpolate()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #data (Seq INT, Val DECIMAL(18,2));
INSERT INTO #data VALUES (1, 10.0);
INSERT INTO #data VALUES (2, NULL);
INSERT INTO #data VALUES (3, NULL);
INSERT INTO #data VALUES (4, 40.0);

TRANSFORM #interpolated
FROM #data
USING INTERPOLATE (
  VALUE_COL = 'Val',
  ORDER_COL = 'Seq',
  METHOD = 'LINEAR'
);
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var interpolated = evaluator.Connections["#interpolated"] as InMemoryDataSource;
        Assert.NotNull(interpolated);

        var batches = await interpolated.ReadBatches().ToListAsync();
        var rows = batches.SelectMany(b => b.Rows).ToList();

        Assert.Equal(4, rows.Count);
        Assert.Equal(10.0m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r["Seq"]) == 1)["Val"]));
        Assert.Equal(20.0m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r["Seq"]) == 2)["Val"]));
        Assert.Equal(30.0m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r["Seq"]) == 3)["Val"]));
        Assert.Equal(40.0m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r["Seq"]) == 4)["Val"]));
    }

    [Fact]
    public async Task TestTransformDeduplicate()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #customers (Id INT, Name VARCHAR(50), Priority INT);
INSERT INTO #customers VALUES (1, 'Acme', 10);
INSERT INTO #customers VALUES (1, 'Acme Corp', 20);
INSERT INTO #customers VALUES (2, 'Globex', 5);

TRANSFORM #deduped
FROM #customers
USING DEDUPLICATE (
  KEY_COLS = 'Id',
  ORDER_BY = 'Priority DESC',
  KEEP = 'FIRST'
);
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var deduped = evaluator.Connections["#deduped"] as InMemoryDataSource;
        Assert.NotNull(deduped);

        var batches = await deduped.ReadBatches().ToListAsync();
        var rows = batches.SelectMany(b => b.Rows).ToList();

        Assert.Equal(2, rows.Count);
        var acme = rows.Single(r => Convert.ToInt32(r["Id"]) == 1);
        Assert.Equal("Acme Corp", acme["Name"]?.ToString());
        Assert.Equal(20, Convert.ToInt32(acme["Priority"]));
    }

    [Fact]
    public async Task TestTransformPivot()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #sales (Region VARCHAR(20), Year INT, Sales INT);
INSERT INTO #sales VALUES ('East', 2024, 100);
INSERT INTO #sales VALUES ('East', 2025, 150);
INSERT INTO #sales VALUES ('West', 2024, 200);

TRANSFORM #pivoted
FROM #sales
USING PIVOT (
  ROW_FIELDS = 'Region',
  PIVOT_FIELD = 'Year',
  VALUE_FIELD = 'Sales',
  AGGREGATE = 'SUM'
);
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var pivoted = evaluator.Connections["#pivoted"] as InMemoryDataSource;
        Assert.NotNull(pivoted);

        var batches = await pivoted.ReadBatches().ToListAsync();
        var rows = batches.SelectMany(b => b.Rows).ToList();

        Assert.Equal(2, rows.Count);
        
        var east = rows.Single(r => r["Region"]?.ToString() == "East");
        Assert.Equal(100m, Convert.ToDecimal(east["2024"]));
        Assert.Equal(150m, Convert.ToDecimal(east["2025"]));

        var west = rows.Single(r => r["Region"]?.ToString() == "West");
        Assert.Equal(200m, Convert.ToDecimal(west["2024"]));
        Assert.Null(west["2025"]);
    }

    [Fact]
    public async Task TestTransformTopNOthers()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #cats (Category VARCHAR(20), Val INT);
INSERT INTO #cats VALUES ('A', 10);
INSERT INTO #cats VALUES ('B', 20);
INSERT INTO #cats VALUES ('C', 5);
INSERT INTO #cats VALUES ('D', 2);
INSERT INTO #cats VALUES ('E', 1);

TRANSFORM #top3
FROM #cats
USING TOP_N_OTHERS (
  N = 2,
  VALUE_COL = 'Val',
  CATEGORY_COL = 'Category',
  OTHERS_LABEL = 'All Others'
);
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var top3 = evaluator.Connections["#top3"] as InMemoryDataSource;
        Assert.NotNull(top3);

        var batches = await top3.ReadBatches().ToListAsync();
        var rows = batches.SelectMany(b => b.Rows).ToList();

        // Top 2 are 'B' (20) and 'A' (10). Others are C (5) + D (2) + E (1) = 8.
        Assert.Equal(3, rows.Count);
        Assert.Equal(20m, Convert.ToDecimal(rows.Single(r => r["Category"]?.ToString() == "B")["Val"]));
        Assert.Equal(10m, Convert.ToDecimal(rows.Single(r => r["Category"]?.ToString() == "A")["Val"]));
        Assert.Equal(8m, Convert.ToDecimal(rows.Single(r => r["Category"]?.ToString() == "All Others")["Val"]));
    }

    [Fact]
    public async Task TestTransformPeriodComparison()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #ts (Dt DATE, Val INT);
INSERT INTO #ts VALUES ('2026-01-01', 100);
INSERT INTO #ts VALUES ('2026-01-02', 150);
INSERT INTO #ts VALUES ('2026-01-03', 120);

TRANSFORM #compared
FROM #ts
USING PERIOD_COMPARISON (
  DATE_COL = 'Dt',
  VALUE_COL = 'Val',
  PERIOD = 'DAY'
);
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var compared = evaluator.Connections["#compared"] as InMemoryDataSource;
        Assert.NotNull(compared);

        var batches = await compared.ReadBatches().ToListAsync();
        var rows = batches.SelectMany(b => b.Rows).ToList();

        Assert.Equal(3, rows.Count);
        
        var r1 = rows.Single(r => Convert.ToDateTime(r["Dt"]).Date == new DateTime(2026, 1, 1));
        Assert.Null(r1["Val_Diff"]);
        Assert.Null(r1["Val_Pct"]);

        var r2 = rows.Single(r => Convert.ToDateTime(r["Dt"]).Date == new DateTime(2026, 1, 2));
        Assert.Equal(50m, Convert.ToDecimal(r2["Val_Diff"]));
        Assert.Equal(50m, Convert.ToDecimal(r2["Val_Pct"]));

        var r3 = rows.Single(r => Convert.ToDateTime(r["Dt"]).Date == new DateTime(2026, 1, 3));
        Assert.Equal(-30m, Convert.ToDecimal(r3["Val_Diff"]));
        Assert.Equal(-20m, Convert.ToDecimal(r3["Val_Pct"]));
    }

    [Fact]
    public async Task TestTransformShareOfTotal()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #items (Item VARCHAR(10), Val INT);
INSERT INTO #items VALUES ('A', 10);
INSERT INTO #items VALUES ('B', 30);

TRANSFORM #shares
FROM #items
USING SHARE_OF_TOTAL (
  VALUE_COL = 'Val'
);
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var shares = evaluator.Connections["#shares"] as InMemoryDataSource;
        Assert.NotNull(shares);

        var batches = await shares.ReadBatches().ToListAsync();
        var rows = batches.SelectMany(b => b.Rows).ToList();

        // Total = 40. A = 10 / 40 = 0.25. B = 30 / 40 = 0.75.
        Assert.Equal(2, rows.Count);
        Assert.Equal(0.25m, Convert.ToDecimal(rows.Single(r => r["Item"]?.ToString() == "A")["Val_Share"]));
        Assert.Equal(0.75m, Convert.ToDecimal(rows.Single(r => r["Item"]?.ToString() == "B")["Val_Share"]));
    }

    [Fact]
    public async Task TestTransformNormalize()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #metrics (Id INT, Val INT);
INSERT INTO #metrics VALUES (1, 10);
INSERT INTO #metrics VALUES (2, 20);
INSERT INTO #metrics VALUES (3, 30);

TRANSFORM #norm
FROM #metrics
USING NORMALIZE (
  VALUE_COL = 'Val',
  METHOD = 'MIN_MAX'
);
";
        var parsed = new Parser(new Lexer(script).Tokenize()).Parse();
        await evaluator.Evaluate(parsed);

        var norm = evaluator.Connections["#norm"] as InMemoryDataSource;
        Assert.NotNull(norm);

        var batches = await norm.ReadBatches().ToListAsync();
        var rows = batches.SelectMany(b => b.Rows).ToList();

        // Min = 10, Max = 30. range = 20.
        // 10 -> 0.0. 20 -> 0.5. 30 -> 1.0.
        Assert.Equal(3, rows.Count);
        Assert.Equal(0m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r["Id"]) == 1)["Val_Normalized"]));
        Assert.Equal(0.5m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r["Id"]) == 2)["Val_Normalized"]));
        Assert.Equal(1m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r["Id"]) == 3)["Val_Normalized"]));
    }
}
