# Reporting Phase 13 Closure Evidence

Phase 13 closes the native Grammar-of-Graphics reporting program against the acceptance evidence in `GrammarOfGraphicsSpecIR.md`. Evidence is reproducible from source and tests; checked-in timing values are observations from the declared host, while test budgets are regression gates.

| Required evidence | Authoritative proof |
| :--- | :--- |
| Serialization and version compatibility | `GrammarOfGraphicsContractTests` proves stable `ChartSpec`, typed data, and `PlotPlan` round trips, rejects unknown schemas/versions, and migrates the supported v1 contracts. |
| Cross-backend semantics | `RepresentativeVisualConformanceTests`, `AdvancedChartProductionTests`, and `TerminalSemanticSnapshotTests` consume the shared resolved plan across native SVG, browser manifest, terminal, PDF, Markdown/plain text, and semantic fallbacks. |
| Visual goldens | `NativeSvgGeometryGoldenTests` and the conformance fixtures pin representative native geometry and micro-chart export hashes. |
| Accessibility and fallbacks | `terminal_accessible_summary.rptsql`, `TerminalSemanticSnapshotTests`, and advanced color/facet tests prove ordered descriptions and meaningful non-graphical output. |
| Bundle and cold start | [`reporting-phase8-results.md`](reporting-phase8-results.md) records raw/gzip/Brotli shared assets, the first fixture-build cold path, and the post-ECharts/ClearScript footprint. |
| Memory, export time, output size | The Phase 8 results record per-fixture managed allocations plus Markdown/CSV/SVG/manifest measurements; [`reporting-phase12-refinements.md`](reporting-phase12-refinements.md) adds the 5,000-row refinement workload's resolver allocation, serialization size, SVG size, and resolver/render time. |
| Capability/source parity | `VisualCapabilityMatrix` is the source contract. `ReportingBaselineTests` requires one entry per `VisualType`, checks renderer boundaries, and proves that no graphical visual depends on an external chart runtime. The generated matrix is checked in with the Phase 8 results. |
| Parser-tested samples and authoring surfaces | `AdvancedChartProductionTests`, parser/formatter tests, LSP tests, Report Builder tests, snippet tests, and `Test-AllSamples.ps1` cover the native grammar and production samples. |

## Reproduce

```powershell
./scripts/Measure-ReportingBaselines.ps1 -CheckOnly
dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter "FullyQualifiedName~GrammarOfGraphicsContractTests|FullyQualifiedName~RepresentativeVisualConformanceTests|FullyQualifiedName~AdvancedChartProductionTests|FullyQualifiedName~NativeSvgGeometryGoldenTests|FullyQualifiedName~TerminalSemanticSnapshotTests|FullyQualifiedName~ReportingBaselineTests"
./scripts/Test-AllSamples.ps1
```

The current capability matrix is [`reporting-phase8-results.md`](reporting-phase8-results.md). Files named `reporting-phase2-baselines.*` and `reporting-phase8-baselines.*` are intentionally retained historical before-state measurements; they are not statements about the current renderer.
