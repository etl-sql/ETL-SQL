# ETL-SQL Performance Benchmarks

[« Back to Documentation Hub](../README.md)

This directory contains execution baseline reports and test result datasets for engine throughput, memory utilization, native rendering performance, cascading slicers, and large dataset handling.

---

## Baseline Reports

| Document | Description |
| :--- | :--- |
| [Paired GC Investigation](gc-paired-comparison-2026-10-27.md) | Source-control/candidate captures and 40 fixed-binary runtime controls investigate a failed workstation gate; original failures and baselines remain retained. |
| [Scale Baseline Calibration](scale-baseline-calibration-2026-10-23.md) | Accepted Smoke/Standard calibration and promoted references, with earlier failed controls retained. |
| [Smoke Scale Order Independence](scale-order-independence-2026-10-22.md) | Fresh-process Smoke captures pass all directional timing checks; earlier failed experiments remain recorded. |
| [Studio Performance Budgets](studio-performance-budgets.md) | Cross-platform browser fixture, checked-in regression ceilings, and measurement workflow for Studio startup, heap, editing, aggregation, and canvas redraw. |
| [Studio Delivery Budgets](studio-delivery-budgets.md) | Production asset inventory and transfer ceilings, uncached Portal/desktop editor measurements, and the large-document character contract. |
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
