# Reporting Phase 12 Refinement Measurements

Re-measured on 2026-08-30 with .NET 10 Debug binaries on the repository's local Windows validation host. The declared workload is the 5,000-row quantitative X/Y scatter with a diverging color range, stable-key jitter, fixed aspect, serialization, and native SVG rendering implemented by `RepresentativeRefinementWorkload_HasBoundedResolverAndRendererWork`.

| Metric | Measured result | Regression budget |
| :--- | ---: | ---: |
| PlotPlan resolver time | 58–63 ms | < 5,000 ms |
| Native SVG render time | 19–21 ms | < 5,000 ms |
| Resolver thread allocation | 10,138,072 bytes | < 16 MiB |
| Serialized PlotPlan size | 5,427,304 bytes | < 6 MiB |
| Native SVG size | 534,754 bytes | < 600 KiB |
| Resolved data marks | 5,000 | exactly one per input row |

The regression test also proves that scale inference/color resolution and display offsets are computed once in `PlotPlanResolver`; SVG consumes the resolved offsets and does not scan source columns or infer domains. Facet resolution separately rejects more than 100 panels, more than 1,000,000 panel-row work cells, and sub-minimum panels before allocating panel contracts.
