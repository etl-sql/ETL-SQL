# ETL-SQL Performance Benchmarks

[« Back to Documentation Hub](../README.md)

This directory contains execution baseline reports and test result datasets for engine throughput, memory utilization, native rendering performance, cascading slicers, and large dataset handling.

---

## Baseline Reports

| Document | Description |
| :--- | :--- |
| [Reporting Phase 2 Baselines](reporting-phase2-baselines.md) | Initial baseline timings for core reporting components. |
| [Reporting Phase 4 Native Render Baseline](reporting-phase4-native-render-baseline.md) | Benchmarks for native SVG vector chart generation vs. server-side rendering. |
| [Reporting Phase 4 Payload Crossover](reporting-phase4-payload-crossover.md) | Memory and network payload benchmarks across dataset size boundaries. |
| [Reporting Phase 6 Cascading Slicer Baselines](reporting-phase6-cascading-slicer-baselines.md) | Multi-tier parent-child filter cascade execution timings and memory footprints. |
| [Reporting Phase 7 Semantic Readiness](reporting-phase7-semantic-readiness.md) | PlotPlan semantic lowering and rendering readiness baselines. |
| [Reporting Phase 8 Baselines](reporting-phase8-baselines.md) | End-to-end dashboard compilation and snapshot delivery performance metrics. |
| [Reporting Phase 8 Results](reporting-phase8-results.md) | Release validation test execution logs and throughput statistics. |
| [Reporting Phase 12 Refinements](reporting-phase12-refinements.md) | Resolver, allocation, serialized-plan, native-SVG size, and rendering budgets for the shipped declarative geometry refinements. |
| [Reporting Phase 13 Closure](reporting-phase13-closure.md) | Requirement-to-evidence index for versioning, cross-backend conformance, accessibility, capability parity, and reproducible performance measurements. |

---

## Related References

- [Tuning Pipeline Performance](../guides/operations/tuning-pipeline-performance.md)
- [Large Data Certification Reference](../reference/performance/large-data-certification.md)
