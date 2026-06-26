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
    private Script _externalWindowQualifyLimitScript = null!;

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

        _externalAggregateLimitScript = Parse(
            "SELECT Grp, SUM(Val) AS Total FROM #pipeline GROUP BY Grp LIMIT 5;");

        _externalAggregateOrderLimitScript = Parse(
            "SELECT Grp, SUM(Val) AS Total FROM #pipeline GROUP BY Grp ORDER BY Total DESC LIMIT 5;");

        _externalWindowQualifyLimitScript = Parse(@"
            SELECT ID, Grp, Val,
                   ROW_NUMBER() OVER (PARTITION BY Grp ORDER BY Val DESC) AS rn
            FROM #pipeline
            QUALIFY rn <= 2
            LIMIT 10;");
    }

    [Benchmark(Description = "ExternalAggregateLimit — spilled GROUP BY through LIMIT")]
    public async Task ExternalAggregateLimit() => await _evaluator.Evaluate(_externalAggregateLimitScript);

    [Benchmark(Description = "ExternalAggregateOrderLimit — spilled GROUP BY through Top-N")]
    public async Task ExternalAggregateOrderLimit() => await _evaluator.Evaluate(_externalAggregateOrderLimitScript);

    [Benchmark(Description = "ExternalWindowQualifyLimit — spilled window through QUALIFY and LIMIT")]
    public async Task ExternalWindowQualifyLimit() => await _evaluator.Evaluate(_externalWindowQualifyLimitScript);

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
