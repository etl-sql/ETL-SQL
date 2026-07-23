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
    public async Task TestFillDatesStatement()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        string script = @"
CREATE TABLE #sales (Region VARCHAR(20), OrderDate DATE, Quantity INT);
INSERT INTO #sales VALUES ('East', '2026-05-01', 10);
INSERT INTO #sales VALUES ('East', '2026-05-03', 30);
INSERT INTO #sales VALUES ('West', '2026-05-02', 5);
INSERT INTO #sales VALUES ('West', '2026-05-04', 15);

FILL_DATES(#sales, DATE_COL = 'OrderDate', GAPS_FILL = 0, BY_GROUP = 'Region') INTO #filled;
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
}
