# Reporting Phase 4 Native Render Baseline

Measured on 2026-08-21 using the Debug `net10.0` test build on the development workstation. The
measurement creates a typed `ChartSpec` and `ChartDataSet`, resolves a `PlotPlan`, and renders native
SVG for each sample; it therefore measures the complete server-side micro-chart path rather than SVG
string formatting alone.

| Workload | Iterations | Elapsed | Managed allocation | Gate |
| :--- | ---: | ---: | ---: | :--- |
| Five-point line sparkline | 1,000 | 77.604 ms | 36,753,976 bytes | < 5 seconds and < 100 MB |

This is a regression budget, not a throughput promise. The deliberately broad gate protects CI from
machine variance while catching accidental orders-of-magnitude regressions. Re-run with:

```powershell
dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --no-restore `
  --filter "FullyQualifiedName~MicroChartGeometry_IsDeterministicAndWithinMeasuredRenderBudget" `
  -m:1 --logger "console;verbosity=detailed"
```

The same test pins SHA-256 geometry goldens for line, area, bar, and progress micro-charts. The
representative native SVG suite separately pins bar, stacked bar, temporal line, null-gap line,
scatter, pie/donut, dual-axis combo, and statistical-rule output. A deterministic Markdown/email-image
snapshot and a structural native PDF export test cover static export behavior.
