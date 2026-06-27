using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Benchmarks;

/// <summary>
/// Focused SELECT pipeline benchmarks for spill-backed stages that should preserve
/// streaming through non-blocking downstream operators.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class SelectPipelineBenchmarks
{
    private readonly int _rowCount;
    private Evaluator _evaluator = null!;
    private Script _externalAggregateLimitScript = null!;
    private Script _externalAggregateOrderLimitScript = null!;
    private Script _externalAggregateFullOrderScript = null!;
    private Script _externalWindowQualifyLimitScript = null!;
    private Script _externalWindowRunningStateScript = null!;
    private Script _joinAggregateScript = null!;
    private Script _joinWindowScript = null!;
    private Script _highCardinalityDistinctScript = null!;
    private Script _topPercentScript = null!;
    private Script _withTiesScript = null!;

    public SelectPipelineBenchmarks() => _rowCount = 50_000;
    public SelectPipelineBenchmarks(int rowCount) => _rowCount = rowCount;

    public DataTable? LastResult => _evaluator?.LastResult;

    [GlobalSetup]
    public async Task Setup()
    {
        _evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        _evaluator.RedirectOutput = true;
        _evaluator.BatchSize = 2_000;
        _evaluator.JoinSpillThreshold = 1_000;
        _evaluator.WindowSpillThreshold = 1_000;
        _evaluator.OperatorMemoryGrantMB = 1;

        await Execute("CREATE TABLE #pipeline (ID INT, Grp INT, Val INT);");

        var rowValues = new List<List<Expression>>(_rowCount);
        for (var i = 1; i <= _rowCount; i++)
        {
            rowValues.Add(new List<Expression>
            {
                new LiteralExpression(i, TokenType.NUMBER),
                new LiteralExpression(i % 100, TokenType.NUMBER),
                new LiteralExpression((i * 17) % 10_000, TokenType.NUMBER)
            });
        }

        var insert = new InsertStatement(
            new TableReference("#pipeline"),
            new List<string> { "ID", "Grp", "Val" },
            rowValues);
        var seed = new Script();
        seed.Statements.Add(insert);
        await _evaluator.Evaluate(seed);

        await Execute("CREATE TABLE #groups (Grp INT, Label VARCHAR);");
        var lookupValues = new List<List<Expression>>(100);
        for (var i = 0; i < 100; i++)
            lookupValues.Add(new List<Expression>
            {
                new LiteralExpression(i, TokenType.NUMBER),
                new LiteralExpression($"Group-{i}", TokenType.STRING)
            });
        var lookupInsert = new InsertStatement(
            new TableReference("#groups"),
            new List<string> { "Grp", "Label" },
            lookupValues);
        var lookupSeed = new Script();
        lookupSeed.Statements.Add(lookupInsert);
        await _evaluator.Evaluate(lookupSeed);

        _externalAggregateLimitScript = Parse(
            "SELECT Grp, SUM(Val) AS Total FROM #pipeline GROUP BY Grp LIMIT 5;");

        _externalAggregateOrderLimitScript = Parse(
            "SELECT Grp, SUM(Val) AS Total FROM #pipeline GROUP BY Grp ORDER BY Total DESC LIMIT 5;");

        _externalAggregateFullOrderScript = Parse(
            "SELECT Grp, SUM(Val) AS Total FROM #pipeline GROUP BY Grp ORDER BY Total DESC;");

        _externalWindowQualifyLimitScript = Parse(@"
            SELECT ID, Grp, Val,
                   ROW_NUMBER() OVER (PARTITION BY Grp ORDER BY Val DESC) AS rn
            FROM #pipeline
            QUALIFY rn <= 2
            LIMIT 10;");

        _externalWindowRunningStateScript = Parse(@"
            SELECT ID, Grp, Val,
                   SUM(Val) OVER (
                       PARTITION BY Grp
                       ORDER BY ID
                       ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                   ) AS RunningTotal,
                   LAG(Val, 2, -1) OVER (PARTITION BY Grp ORDER BY ID) AS PreviousTwo
            FROM #pipeline
            LIMIT 100;");

        _joinAggregateScript = Parse(@"
            SELECT g.Label, SUM(p.Val) AS Total
            FROM #pipeline p JOIN #groups g ON p.Grp = g.Grp
            GROUP BY g.Label ORDER BY Total DESC;");

        _joinWindowScript = Parse(@"
            SELECT p.ID, g.Label, p.Val,
                   ROW_NUMBER() OVER (PARTITION BY g.Label ORDER BY p.Val DESC) AS rn
            FROM #pipeline p JOIN #groups g ON p.Grp = g.Grp
            QUALIFY rn <= 2;");

        _highCardinalityDistinctScript = Parse(
            "SELECT DISTINCT ID, Val FROM #pipeline ORDER BY ID DESC;");
        _topPercentScript = Parse(
            "SELECT TOP 10 PERCENT ID, Val FROM #pipeline ORDER BY Val DESC, ID;");
        _withTiesScript = Parse(
            "SELECT TOP 100 WITH TIES ID, Val FROM #pipeline ORDER BY Val DESC;");
    }

    [Benchmark(Description = "ExternalAggregateLimit — spilled GROUP BY through LIMIT")]
    public async Task ExternalAggregateLimit() => await _evaluator.Evaluate(_externalAggregateLimitScript);

    [Benchmark(Description = "ExternalAggregateOrderLimit — spilled GROUP BY through Top-N")]
    public async Task ExternalAggregateOrderLimit() => await _evaluator.Evaluate(_externalAggregateOrderLimitScript);

    [Benchmark(Description = "ExternalAggregateFullOrder — spilled GROUP BY streams into external sort")]
    public async Task ExternalAggregateFullOrder() => await _evaluator.Evaluate(_externalAggregateFullOrderScript);

    [Benchmark(Description = "ExternalWindowQualifyLimit — spilled window through QUALIFY and LIMIT")]
    public async Task ExternalWindowQualifyLimit() => await _evaluator.Evaluate(_externalWindowQualifyLimitScript);

    [Benchmark(Description = "ExternalWindowRunningState — cumulative aggregate and bounded LAG")]
    public async Task ExternalWindowRunningState() => await _evaluator.Evaluate(_externalWindowRunningStateScript);

    [Benchmark(Description = "JoinAggregate — streaming join into external aggregate")]
    public async Task JoinAggregate() => await _evaluator.Evaluate(_joinAggregateScript);

    [Benchmark(Description = "JoinWindow — streaming join into external window")]
    public async Task JoinWindow() => await _evaluator.Evaluate(_joinWindowScript);

    [Benchmark(Description = "HighCardinalityDistinct — projected hash partitions then sort")]
    public async Task HighCardinalityDistinct() => await _evaluator.Evaluate(_highCardinalityDistinctScript);

    [Benchmark(Description = "TopPercent — external sort count/replay")]
    public async Task TopPercent() => await _evaluator.Evaluate(_topPercentScript);

    [Benchmark(Description = "WithTies — external sort boundary-key streaming")]
    public async Task WithTies() => await _evaluator.Evaluate(_withTiesScript);

    [GlobalCleanup]
    public void ReportExtraMetrics()
    {
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        proc.Refresh();
        var mem = GC.GetGCMemoryInfo();
        long lohBytes = mem.GenerationInfo.Length > 3 ? mem.GenerationInfo[3].SizeAfterBytes : -1;
        Console.WriteLine($"// [ExtraMetrics] WorkingSet={proc.WorkingSet64 / 1024 / 1024} MB, " +
            $"ManagedHeap={GC.GetTotalMemory(false) / 1024 / 1024} MB, " +
            $"LOH≈{(lohBytes >= 0 ? lohBytes / 1024 : -1)} KB, " +
            $"SpillBytes={_evaluator.Telemetry.TotalSpilledBytes}, " +
            $"SortSpills={_evaluator.Telemetry.SortSpillCount}, " +
            $"RowsProcessed={_evaluator.Telemetry.RowsProcessed}, " +
            $"RetainedRows={_evaluator.LastResult?.Rows.Count ?? 0}");
    }

    private async Task Execute(string sql) => await _evaluator.Evaluate(Parse(sql));

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();
}
