# Columnar Crossover Benchmark Capture

Captured: 2026-07-10

Command:

```powershell
dotnet run --project tests\ETL-SQL.Benchmarks\ETL-SQL.Benchmarks.csproj -c Release -- --filter "*ColumnarCrossover*" --exporters json --artifacts certification-results\columnar-crossover-benchmarks\latest
```

Admission result: failed. The current native columnar candidates are slower than the row-reference
paths at both checked row counts, so this capture does not approve any new native-path expansion.

Included artifacts:

- `results/ETL_SQL.Benchmarks.ColumnarCrossoverBenchmarks-report-full-compressed.json`
- `results/ETL_SQL.Benchmarks.ColumnarCrossoverBenchmarks-report-github.md`
- `results/ETL_SQL.Benchmarks.ColumnarCrossoverBenchmarks-report.csv`
