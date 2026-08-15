using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class WatermarkQueryTests
{
    private static Evaluator NewEvaluator() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    private static async Task<int> CountRows(Evaluator eval, string table)
    {
        var rows = new List<Row>();
        if (!eval.Connections.TryGetValue(table, out var source)) return 0;
        await foreach (var batch in source.ReadBatches(1000))
            rows.AddRange(batch.Rows);
        return rows.Count;
    }

    [Fact]
    public async Task Watermark_InitialLoad_FiltersAboveInitialValue_AndRecordsState()
    {
        var eval = NewEvaluator();
        var script = @"
            CREATE TABLE #orders (OrderId INT, UpdatedAt VARCHAR(30), Amount DECIMAL(10,2));
            INSERT INTO #orders VALUES
                (1, '2020-01-01', 50.00),
                (2, '2021-06-15', 100.00),
                (3, '2023-01-01', 250.00);

            SELECT OrderId, UpdatedAt, Amount
            INTO #stage
            FROM #orders WITH (WATERMARK = 'UpdatedAt', INITIAL = '2021-01-01');
        ";

        await eval.Evaluate(new Lexer(script).TokenizeToScript());

        Assert.Equal(2, await CountRows(eval, "#stage"));
        Assert.True(eval.SessionJobState.Count > 0);

        var key = eval.SessionJobState.Keys.First();
        Assert.Contains("UpdatedAt", key);
        Assert.Equal("2023-01-01", eval.SessionJobState[key]);
    }

    [Fact]
    public async Task Watermark_IncrementalRun_UsesPreviousWatermarkState()
    {
        var eval = NewEvaluator();

        // 1. Initial Load
        var initScript = @"
            CREATE TABLE #orders (OrderId INT, UpdatedAt VARCHAR(30), Amount DECIMAL(10,2));
            INSERT INTO #orders VALUES
                (1, '2020-01-01', 50.00),
                (2, '2021-06-15', 100.00);

            SELECT OrderId, UpdatedAt, Amount
            INTO #stage1
            FROM #orders WITH (WATERMARK = 'UpdatedAt', INITIAL = '2020-01-01', KEY = 'orders_etl');
        ";

        await eval.Evaluate(new Lexer(initScript).TokenizeToScript());
        Assert.Equal(1, await CountRows(eval, "#stage1"));
        Assert.Equal("2021-06-15", eval.SessionJobState["orders_etl"]);

        // 2. Incremental Load in same session / state context
        var deltaScript = @"
            INSERT INTO #orders VALUES
                (3, '2021-06-15', 75.00),
                (4, '2022-03-10', 300.00);

            SELECT OrderId, UpdatedAt, Amount
            INTO #stage2
            FROM #orders WITH (WATERMARK = 'UpdatedAt', INITIAL = '2020-01-01', KEY = 'orders_etl');
        ";

        await eval.Evaluate(new Lexer(deltaScript).TokenizeToScript());
        Assert.Equal(1, await CountRows(eval, "#stage2"));
        Assert.Equal("2022-03-10", eval.SessionJobState["orders_etl"]);
    }

    [Fact]
    public async Task Watermark_NumericColumn_PassesAndIncrements()
    {
        var eval = NewEvaluator();
        var script = @"
            CREATE TABLE #events (EventId INT, EventType VARCHAR(20));
            INSERT INTO #events VALUES (10, 'LOGIN'), (11, 'CLICK'), (12, 'PURCHASE'), (13, 'LOGOUT');

            SELECT EventId, EventType
            INTO #extracted
            FROM #events WITH (WATERMARK = 'EventId', INITIAL = 11, KEY = 'events_sync');
        ";

        await eval.Evaluate(new Lexer(script).TokenizeToScript());

        Assert.Equal(2, await CountRows(eval, "#extracted")); // 12 and 13
        Assert.Equal("13", eval.SessionJobState["events_sync"]);
    }

    [Fact]
    public async Task Watermark_WithInclusiveOption_UsesGreaterEquals()
    {
        var eval = NewEvaluator();
        var script = @"
            CREATE TABLE #items (ItemId INT, Code VARCHAR(10));
            INSERT INTO #items VALUES (100, 'A'), (101, 'B'), (102, 'C');

            SELECT ItemId, Code
            INTO #inclusive_items
            FROM #items WITH (WATERMARK = 'ItemId', INITIAL = 101, INCLUSIVE = TRUE, KEY = 'items_sync');
        ";

        await eval.Evaluate(new Lexer(script).TokenizeToScript());

        Assert.Equal(2, await CountRows(eval, "#inclusive_items")); // 101 and 102
        Assert.Equal("102", eval.SessionJobState["items_sync"]);
    }
}
