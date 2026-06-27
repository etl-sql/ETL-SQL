# ETL-SQL Benchmarks

Benchmark runs are written to timestamped folders by default:

```powershell
dotnet run --project tests\ETL-SQL.Benchmarks -c Release -- --filter "*SelectShape*" --exporters json
```

Results are emitted under `BenchmarkDotNet.Artifacts/runs/<yyyyMMdd-HHmmss>/results/` so repeated runs do not overwrite each other. Pass `--artifacts <path>` when a fixed output directory is needed for CI.

Run all standard suites while excluding the explicit large-scale wrapper classes:

```powershell
dotnet run --project tests\ETL-SQL.Benchmarks -c Release -- --filter "*ParserBenchmarks.*" "*RuntimeServiceBenchmarks.*" "*SelectPipelineBenchmarks.*" "*SelectShapeBenchmarks.*" "*TpcHBenchmarks.*" --exporters json
```

## Coverage Map

- `ParserBenchmarks`: lexer/parser throughput for small and larger scripts.
- `SelectShapeBenchmarks`: common 10k-row SELECT shapes: streaming filter, distinct, Top-N sort, window + QUALIFY, and UNION ALL.
- `SelectShapeBenchmarksLargeScale`: same SELECT shapes at 100k rows.
- `SelectPipelineBenchmarks`: spill-backed SELECT pipeline paths covering aggregate LIMIT/Top-N/full ORDER BY, join-to-aggregate/window handoffs, high-cardinality DISTINCT, TOP PERCENT replay, WITH TIES boundary streaming, and external window QUALIFY/bounded-state paths.
- `TpcHBenchmarks`: representative analytic joins, filters, aggregates, grouping, ordering, and expression-heavy projections at small TPC-H mock scale.
- `TpcHBenchmarksLargeScale`: same TPC-H query set at larger mock scale.
- `RuntimeServiceBenchmarks`: encrypted Arrow snapshot save/load, report rendering, and SQLite-backed orchestrator scheduling acquisition.

Warm-runner process throughput remains an end-to-end capacity workload rather than a microbenchmark because process lifecycle and executable discovery dominate isolated BenchmarkDotNet iterations.
