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

        var insertRow = rows.FirstOrDefault(r => r["Id"]?.ToString() == "4");
        Assert.NotNull(insertRow);
        Assert.Equal("INSERT", insertRow["_change_type"]?.ToString());

        var deleteRow = rows.FirstOrDefault(r => r["Id"]?.ToString() == "3");
        Assert.NotNull(deleteRow);
        Assert.Equal("DELETE", deleteRow["_change_type"]?.ToString());
    }
}
