# ETL-SQL Benchmarks

Benchmark runs are written to timestamped folders by default:

```powershell
dotnet run --project tests\ETL-SQL.Benchmarks -c Release -- --filter "*SelectShape*" --exporters json
```

Results are emitted under `BenchmarkDotNet.Artifacts/runs/<yyyyMMdd-HHmmss>/results/` so repeated runs do not overwrite each other. Pass `--artifacts <path>` when a fixed output directory is needed for CI.

## Coverage Map

- `ParserBenchmarks`: lexer/parser throughput for small and larger scripts.
- `SelectShapeBenchmarks`: common 10k-row SELECT shapes: streaming filter, distinct, Top-N sort, window + QUALIFY, and UNION ALL.
- `SelectShapeBenchmarksLargeScale`: same SELECT shapes at 100k rows.
- `SelectPipelineBenchmarks`: spill-backed SELECT pipeline paths that should preserve streaming through non-blocking stages: external aggregate + LIMIT, external aggregate + Top-N, external window + QUALIFY + LIMIT, and bounded-state cumulative/LAG windows.
- `TpcHBenchmarks`: representative analytic joins, filters, aggregates, grouping, ordering, and expression-heavy projections at small TPC-H mock scale.
- `TpcHBenchmarksLargeScale`: same TPC-H query set at larger mock scale.

Current known gaps are Report Portal visual/render throughput, orchestrator warm-runner throughput, and snapshot read/write latency. Those are covered by targeted tests today, but not by BenchmarkDotNet suites.
